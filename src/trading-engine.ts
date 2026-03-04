import Database from 'better-sqlite3';
import pino from 'pino';
import { AppConfig, BotState, BotStatus, TradeRecord } from './types';
import { BinanceClient } from './binance-client';
import { TelegramNotifier } from './telegram';
import { getState, setState, insertTrade } from './db';

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
      this.initialized = true;
      this.logger.info({ state: this.state, lastTradePrice: this.lastTradePrice }, 'Resumed from saved state');
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
      this.logger.info({ lastTradePrice: this.lastTradePrice, side: lastTrade.side }, 'Reference price from last Binance trade');
    } else {
      this.lastTradePrice = await this.client.getPrice(this.config.tradingPair);
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

    setState(this.db, this.state, this.lastTradePrice, this.btcBalance, this.eurBalance, this.initialBalanceEur);
    this.initialized = true;

    const targetPrice = this.state === BotState.HOLDING_BTC
      ? this.lastTradePrice * (1 + this.config.sellThresholdPct)
      : this.lastTradePrice * (1 - this.config.buyThresholdPct);

    this.logger.info({ state: this.state, lastTradePrice: this.lastTradePrice, targetPrice }, 'Bot initialized — waiting for target');
  }

  async onPriceTick(currentPrice: number): Promise<void> {
    if (this.busy) return;

    if (this.state === BotState.HOLDING_BTC) {
      const targetSellPrice = this.lastTradePrice * (1 + this.config.sellThresholdPct);
      this.logger.debug({ currentPrice, targetSellPrice }, 'Checking sell condition');

      if (currentPrice >= targetSellPrice) {
        await this.executeSell();
      }
    } else if (this.state === BotState.HOLDING_EUR) {
      const targetBuyPrice = this.lastTradePrice * (1 - this.config.buyThresholdPct);
      this.logger.debug({ currentPrice, targetBuyPrice }, 'Checking buy condition');

      if (currentPrice <= targetBuyPrice) {
        await this.executeBuy();
      }
    }
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
        : this.lastTradePrice * (1 - this.config.buyThresholdPct);

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
        this.state = BotState.HOLDING_BTC;

        insertTrade(this.db, trade);
        setState(this.db, this.state, this.lastTradePrice, this.btcBalance, this.eurBalance, this.initialBalanceEur);

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
        this.state = BotState.HOLDING_EUR;

        insertTrade(this.db, trade);
        setState(this.db, this.state, this.lastTradePrice, this.btcBalance, this.eurBalance, this.initialBalanceEur);

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
        setState(this.db, this.state, this.lastTradePrice, this.btcBalance, this.eurBalance, this.initialBalanceEur);
      } else if (saved.state === BotState.HOLDING_EUR && hasBtc && !hasEur) {
        this.logger.warn('State mismatch: DB says HOLDING_EUR but exchange has BTC. Reconciling.');
        this.state = BotState.HOLDING_BTC;
        this.btcBalance = balances.btc;
        this.eurBalance = 0;
        setState(this.db, this.state, this.lastTradePrice, this.btcBalance, this.eurBalance, this.initialBalanceEur);
      }
    } catch (err) {
      this.logger.warn({ err }, 'Reconciliation failed, using saved state');
    }
  }

  private notifySafe(fn: () => Promise<void>): void {
    fn().catch((err) => this.logger.warn({ err }, 'Telegram notification failed'));
  }
}
