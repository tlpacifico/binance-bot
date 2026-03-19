import Database from 'better-sqlite3';
import pino from 'pino';
import { AppConfig, BotState, BotStatus, PriceData, TradeRecord } from './types';
import { BinanceClient } from './binance-client';
import { TelegramNotifier } from './telegram';
import { getState, setState, insertTrade, getLastTradeTimestamp } from './db';

const MAX_RETRIES = 3;
const RETRY_DELAYS = [5000, 10000, 20000]; // 5s, 10s, 20s

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

export class TradingEngine {
  private state: BotState;
  private lastTradePrice: number;
  private btcBalance: number;
  private eurBalance: number;
  private initialBalanceEur: number;
  private busy = false;
  private initialized = false;
  private confirmationPrices: number[] = [];
  private confirmationActive = false;
  private lastTradeTimestamp: string | null = null;
  private lastLow24h = 0;

  constructor(
    private config: AppConfig,
    private client: BinanceClient,
    private db: Database.Database,
    private telegram: TelegramNotifier,
    private logger: pino.Logger,
  ) {
    const saved = getState(db);
    if (saved) {
      this.state = saved.state;
      this.lastTradePrice = saved.lastTradePrice;
      this.btcBalance = saved.btcBalance;
      this.eurBalance = saved.eurBalance;
      this.initialBalanceEur = saved.initialBalanceEur;
      this.lastTradeTimestamp = saved.lastTradeTimestamp || getLastTradeTimestamp(db);
      this.initialized = true;
      this.logger.info({ state: this.state, lastTradePrice: this.lastTradePrice, lastTradeTimestamp: this.lastTradeTimestamp }, 'Resumed from saved state');
    } else {
      this.state = BotState.HOLDING_EUR;
      this.lastTradePrice = 0;
      this.btcBalance = 0;
      this.eurBalance = 0;
      this.initialBalanceEur = config.initialBalanceEur;
    }
  }

  async initialize(): Promise<void> {
    if (this.initialized) {
      await this.reconcile();
      return;
    }

    this.logger.info('First run: detecting state from Binance wallet...');

    const balances = await this.client.getBalances();
    const hasBtc = balances.btc > 0.00001;
    const hasEur = balances.eur > 1;

    if (hasBtc) {
      this.state = BotState.HOLDING_BTC;
      this.btcBalance = balances.btc;
      this.eurBalance = 0;
      this.logger.info({ btcBalance: this.btcBalance }, 'Detected BTC in wallet → HOLDING_BTC');
    } else if (hasEur) {
      this.state = BotState.HOLDING_EUR;
      this.eurBalance = balances.eur;
      this.btcBalance = 0;
      this.logger.info({ eurBalance: this.eurBalance }, 'Detected EUR in wallet → HOLDING_EUR');
    } else {
      this.logger.warn('No BTC or EUR found in wallet. Defaulting to HOLDING_EUR.');
      this.state = BotState.HOLDING_EUR;
    }

    const lastTrade = await this.client.getLastTrade(this.config.tradingPair);
    if (lastTrade) {
      this.lastTradePrice = lastTrade.price;
      this.lastTradeTimestamp = lastTrade.timestamp;
      this.logger.info({ lastTradePrice: this.lastTradePrice, side: lastTrade.side, lastTradeTimestamp: this.lastTradeTimestamp }, 'Reference price from last Binance trade');
    } else {
      const priceData = await this.client.getPrice(this.config.tradingPair);
      this.lastTradePrice = priceData.last;
      this.lastTradeTimestamp = new Date().toISOString();
      this.logger.info({ lastTradePrice: this.lastTradePrice }, 'No trade history found, using current market price as reference');
    }

    // Auto-calculate initialBalanceEur if not set or clearly wrong
    if (this.initialBalanceEur <= 0) {
      if (this.state === BotState.HOLDING_BTC) {
        this.initialBalanceEur = this.btcBalance * this.lastTradePrice;
      } else {
        this.initialBalanceEur = this.eurBalance;
      }
      this.logger.info({ initialBalanceEur: this.initialBalanceEur }, 'Auto-calculated initial balance for P&L');
    }

    setState(this.db, this.state, this.lastTradePrice, this.btcBalance, this.eurBalance, this.initialBalanceEur, this.lastTradeTimestamp || undefined);
    this.initialized = true;

    const targetPrice = this.state === BotState.HOLDING_BTC
      ? this.lastTradePrice * (1 + this.config.sellThresholdPct)
      : this.lastTradePrice * (1 - this.config.buyThresholdPct);

    this.logger.info({ state: this.state, lastTradePrice: this.lastTradePrice, targetPrice }, 'Bot initialized — waiting for target');
  }

  async onPriceTick(priceData: PriceData): Promise<void> {
    if (this.busy) return;

    const currentPrice = priceData.last;
    this.lastLow24h = priceData.low24h;

    const beyondThreshold = this.isPriceBeyondThreshold(currentPrice);

    if (!beyondThreshold) {
      if (this.confirmationActive) {
        this.logger.info({ currentPrice, ticksCollected: this.confirmationPrices.length }, 'Price back inside threshold, resetting confirmation window');
        this.resetConfirmation();
      }
      return;
    }

    // Immediate execution when confirmation is disabled
    if (this.config.confirmationTicks === 0) {
      await this.executeTrade();
      return;
    }

    // Add tick to confirmation window
    this.confirmationPrices.push(currentPrice);
    this.confirmationActive = true;
    this.logger.debug({ currentPrice, ticksCollected: this.confirmationPrices.length, ticksRequired: this.config.confirmationTicks }, 'Confirmation tick collected');

    if (this.confirmationPrices.length >= this.config.confirmationTicks) {
      const avgPrice = this.confirmationPrices.reduce((sum, p) => sum + p, 0) / this.confirmationPrices.length;
      const avgBeyond = this.isPriceBeyondThreshold(avgPrice);

      if (avgBeyond) {
        this.logger.info({ avgPrice, ticks: this.confirmationPrices.length }, 'Confirmation window complete, average beyond threshold — executing trade');
        this.resetConfirmation();
        await this.executeTrade();
      } else {
        this.logger.info({ avgPrice, ticks: this.confirmationPrices.length }, 'Confirmation window complete but average NOT beyond threshold — resetting');
        this.resetConfirmation();
      }
    }
  }

  private isTradeStale(): boolean {
    if (!this.lastTradeTimestamp || this.config.staleTradeDays <= 0) return false;
    const daysSince = (Date.now() - new Date(this.lastTradeTimestamp).getTime()) / (1000 * 60 * 60 * 24);
    return daysSince > this.config.staleTradeDays;
  }

  private isPriceBeyondThreshold(price: number): boolean {
    if (this.state === BotState.HOLDING_BTC) {
      const targetSellPrice = this.lastTradePrice * (1 + this.config.sellThresholdPct);
      this.logger.debug({ price, targetSellPrice }, 'Checking sell condition');
      return price >= targetSellPrice;
    }

    if (this.isTradeStale() && this.lastLow24h > 0) {
      const targetBuyPrice = this.lastLow24h * (1 - this.config.buyThresholdPct);
      this.logger.debug({ price, targetBuyPrice, low24h: this.lastLow24h, mode: 'stale-24h-low' }, 'Checking buy condition (stale → 24h low)');
      return price <= targetBuyPrice;
    }

    const targetBuyPrice = this.lastTradePrice * (1 - this.config.buyThresholdPct);
    this.logger.debug({ price, targetBuyPrice }, 'Checking buy condition');
    return price <= targetBuyPrice;
  }

  private async executeTrade(): Promise<void> {
    if (this.state === BotState.HOLDING_BTC) {
      await this.executeSell();
    } else {
      await this.executeBuy();
    }
  }

  private resetConfirmation(): void {
    this.confirmationPrices = [];
    this.confirmationActive = false;
  }

  getStatus(currentPrice: number): BotStatus {
    const currentValueEur =
      this.state === BotState.HOLDING_BTC
        ? this.btcBalance * currentPrice
        : this.eurBalance;

    const pnlEur = currentValueEur - this.initialBalanceEur;
    const pnlPct = this.initialBalanceEur > 0 ? (pnlEur / this.initialBalanceEur) * 100 : 0;

    const targetPrice =
      this.state === BotState.HOLDING_BTC
        ? this.lastTradePrice * (1 + this.config.sellThresholdPct)
        : this.isTradeStale() && this.lastLow24h > 0
          ? this.lastLow24h * (1 - this.config.buyThresholdPct)
          : this.lastTradePrice * (1 - this.config.buyThresholdPct);

    const avgPrice = this.confirmationPrices.length > 0
      ? this.confirmationPrices.reduce((sum, p) => sum + p, 0) / this.confirmationPrices.length
      : 0;

    return {
      state: this.state,
      lastTradePrice: this.lastTradePrice,
      btcBalance: this.btcBalance,
      eurBalance: this.eurBalance,
      initialBalanceEur: this.initialBalanceEur,
      currentBtcPrice: currentPrice,
      pnlEur,
      pnlPct,
      targetPrice,
      uptimeMs: process.uptime() * 1000,
      confirmation: {
        active: this.confirmationActive,
        ticksCollected: this.confirmationPrices.length,
        ticksRequired: this.config.confirmationTicks,
        avgPrice,
      },
    };
  }

  getState(): BotState {
    return this.state;
  }

  private async executeBuy(): Promise<void> {
    this.busy = true;
    this.logger.info({ eurBalance: this.eurBalance }, 'Executing BUY');

    for (let attempt = 1; attempt <= MAX_RETRIES; attempt++) {
      try {
        const trade = await this.client.marketBuy(this.config.tradingPair, this.eurBalance);

        this.btcBalance = trade.quantity;
        this.eurBalance = 0;
        this.lastTradePrice = trade.price;
        this.lastTradeTimestamp = trade.timestamp;
        this.state = BotState.HOLDING_BTC;

        insertTrade(this.db, trade);
        setState(this.db, this.state, this.lastTradePrice, this.btcBalance, this.eurBalance, this.initialBalanceEur, this.lastTradeTimestamp);

        this.logger.info({ trade, attempt }, 'BUY executed');
        this.notifySafe(() => this.telegram.sendTradeAlert(trade, this.getPnl(trade.price)));
        this.busy = false;
        return;
      } catch (err) {
        this.logger.error({ err, attempt }, `BUY attempt ${attempt}/${MAX_RETRIES} failed`);
        if (attempt < MAX_RETRIES) {
          await sleep(RETRY_DELAYS[attempt - 1]);
        } else {
          this.notifySafe(() => this.telegram.sendError(`BUY failed after ${MAX_RETRIES} attempts: ${(err as Error).message}`));
        }
      }
    }

    this.busy = false;
  }

  private async executeSell(): Promise<void> {
    this.busy = true;
    this.logger.info({ btcBalance: this.btcBalance }, 'Executing SELL');

    for (let attempt = 1; attempt <= MAX_RETRIES; attempt++) {
      try {
        const trade = await this.client.marketSell(this.config.tradingPair, this.btcBalance);

        this.eurBalance = trade.quoteAmount - trade.fee;
        this.btcBalance = 0;
        this.lastTradePrice = trade.price;
        this.lastTradeTimestamp = trade.timestamp;
        this.state = BotState.HOLDING_EUR;

        insertTrade(this.db, trade);
        setState(this.db, this.state, this.lastTradePrice, this.btcBalance, this.eurBalance, this.initialBalanceEur, this.lastTradeTimestamp);

        this.logger.info({ trade, attempt }, 'SELL executed');
        this.notifySafe(() => this.telegram.sendTradeAlert(trade, this.getPnl(trade.price)));
        this.busy = false;
        return;
      } catch (err) {
        this.logger.error({ err, attempt }, `SELL attempt ${attempt}/${MAX_RETRIES} failed`);
        if (attempt < MAX_RETRIES) {
          await sleep(RETRY_DELAYS[attempt - 1]);
        } else {
          this.notifySafe(() => this.telegram.sendError(`SELL failed after ${MAX_RETRIES} attempts: ${(err as Error).message}`));
        }
      }
    }

    this.busy = false;
  }

  private getPnl(currentPrice: number) {
    const currentValueEur =
      this.state === BotState.HOLDING_BTC
        ? this.btcBalance * currentPrice
        : this.eurBalance;

    return {
      eurValue: currentValueEur,
      pnlEur: currentValueEur - this.initialBalanceEur,
      pnlPct: this.initialBalanceEur > 0 ? ((currentValueEur - this.initialBalanceEur) / this.initialBalanceEur) * 100 : 0,
    };
  }

  private async reconcile(): Promise<void> {
    const saved = getState(this.db);
    if (!saved) return;

    try {
      const balances = await this.client.getBalances();
      const hasBtc = balances.btc > 0.00001;
      const hasEur = balances.eur > 1;

      if (saved.state === BotState.HOLDING_BTC && !hasBtc && hasEur) {
        this.logger.warn('State mismatch: DB says HOLDING_BTC but exchange has EUR. Reconciling.');
        this.state = BotState.HOLDING_EUR;
        this.eurBalance = balances.eur;
        this.btcBalance = 0;
      } else if (saved.state === BotState.HOLDING_EUR && hasBtc && !hasEur) {
        this.logger.warn('State mismatch: DB says HOLDING_EUR but exchange has BTC. Reconciling.');
        this.state = BotState.HOLDING_BTC;
        this.btcBalance = balances.btc;
        this.eurBalance = 0;
      } else {
        // State matches — still sync balances from exchange
        if (this.state === BotState.HOLDING_BTC) {
          if (Math.abs(this.btcBalance - balances.btc) > 0.00000001) {
            this.logger.info({ saved: this.btcBalance, exchange: balances.btc }, 'Syncing BTC balance from exchange');
            this.btcBalance = balances.btc;
          }
        } else {
          if (Math.abs(this.eurBalance - balances.eur) > 0.01) {
            this.logger.info({ saved: this.eurBalance, exchange: balances.eur }, 'Syncing EUR balance from exchange');
            this.eurBalance = balances.eur;
          }
        }
      }

      setState(this.db, this.state, this.lastTradePrice, this.btcBalance, this.eurBalance, this.initialBalanceEur);
    } catch (err) {
      this.logger.warn({ err }, 'Reconciliation failed, using saved state');
    }
  }

  private notifySafe(fn: () => Promise<void>): void {
    fn().catch((err) => this.logger.warn({ err }, 'Telegram notification failed'));
  }
}
