import pino from 'pino';
import { AppConfig } from './types';
import { BinanceClient } from './binance-client';
import { TradingEngine } from './trading-engine';
import { TelegramNotifier } from './telegram';

const MAX_CONSECUTIVE_FAILURES = 10;

export class PriceMonitor {
  private intervalHandle: ReturnType<typeof setInterval> | null = null;
  private currentPrice = 0;
  private consecutiveFailures = 0;
  private alertSent = false;

  constructor(
    private client: BinanceClient,
    private engine: TradingEngine,
    private config: AppConfig,
    private logger: pino.Logger,
    private telegram: TelegramNotifier,
  ) {}

  async start(): Promise<void> {
    await this.tick();
    this.intervalHandle = setInterval(() => this.tick(), this.config.priceIntervalMs);
    this.logger.info({ intervalMs: this.config.priceIntervalMs }, 'Price monitor started');
  }

  stop(): void {
    if (this.intervalHandle) {
      clearInterval(this.intervalHandle);
      this.intervalHandle = null;
    }
  }

  getCurrentPrice(): number {
    return this.currentPrice;
  }

  private async tick(): Promise<void> {
    try {
      this.currentPrice = await this.client.getPrice(this.config.tradingPair);
      this.logger.debug({ price: this.currentPrice }, 'Price tick');
      await this.engine.onPriceTick(this.currentPrice);

      // Reset failure counter on success
      if (this.consecutiveFailures > 0) {
        this.logger.info({ previousFailures: this.consecutiveFailures }, 'Price feed recovered');
        this.consecutiveFailures = 0;
        this.alertSent = false;
      }
    } catch (err) {
      this.consecutiveFailures++;
      this.logger.error({ err, consecutiveFailures: this.consecutiveFailures }, 'Price fetch failed');

      if (this.consecutiveFailures >= MAX_CONSECUTIVE_FAILURES && !this.alertSent) {
        this.alertSent = true;
        this.telegram.sendError(
          `Price feed down! ${this.consecutiveFailures} consecutive failures. Bot is NOT trading.`,
        ).catch(() => {});
      }
    }
  }
}
