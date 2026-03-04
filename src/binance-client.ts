import ccxt from 'ccxt';
import { TradeRecord } from './types';

export class BinanceClient {
  private exchange: InstanceType<typeof ccxt.binance>;

  constructor(apiKey: string, apiSecret: string, testnet: boolean) {
    this.exchange = new ccxt.binance({
      apiKey,
      secret: apiSecret,
      enableRateLimit: true,
      options: {
        adjustForTimeDifference: true,
        recvWindow: 60000,
      },
    });
    if (testnet) {
      this.exchange.setSandboxMode(true);
    }
  }

  async init(): Promise<void> {
    await this.exchange.loadTimeDifference();
  }

  async getPrice(pair: string): Promise<number> {
    const ticker = await this.exchange.fetchTicker(pair);
    const price = ticker.last;
    if (!price || price <= 0) {
      throw new Error(`Invalid price received: ${price}`);
    }
    return price;
  }

  async marketBuy(pair: string, eurAmount: number): Promise<TradeRecord> {
    // Use createMarketBuyOrderWithCost to spend exact EUR amount
    // This internally uses quoteOrderQty on Binance
    const order = await this.exchange.createMarketBuyOrderWithCost(pair, eurAmount);
    return this.toTradeRecord(order, 'BUY');
  }

  async marketSell(pair: string, btcAmount: number): Promise<TradeRecord> {
    const order = await this.exchange.createMarketSellOrder(pair, btcAmount);
    return this.toTradeRecord(order, 'SELL');
  }

  async getLastTrade(pair: string): Promise<{ price: number; side: string } | null> {
    try {
      const trades = await this.exchange.fetchMyTrades(pair, undefined, undefined, { limit: 1 });
      if (trades.length === 0) return null;
      const last = trades[trades.length - 1];
      return {
        price: last.price,
        side: (last.side || 'buy').toUpperCase(),
      };
    } catch {
      return null;
    }
  }

  async getMyTrades(pair: string, limit = 50): Promise<TradeRecord[]> {
    try {
      const trades = await this.exchange.fetchMyTrades(pair, undefined, undefined, { limit });
      return trades.map((t) => ({
        timestamp: new Date(t.timestamp!).toISOString(),
        side: (t.side || 'buy').toUpperCase() as 'BUY' | 'SELL',
        price: t.price,
        quantity: t.amount || 0,
        quoteAmount: t.cost || 0,
        fee: t.fee?.cost || 0,
      })).reverse();
    } catch {
      return [];
    }
  }

  async getBalances(): Promise<{ btc: number; eur: number }> {
    const balance = await this.exchange.fetchBalance();
    const free = balance.free as unknown as Record<string, number>;
    return {
      btc: free['BTC'] || 0,
      eur: free['EUR'] || 0,
    };
  }

  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  private toTradeRecord(order: any, side: 'BUY' | 'SELL'): TradeRecord {
    return {
      timestamp: new Date(order.timestamp).toISOString(),
      side,
      price: order.average || order.price || 0,
      quantity: order.filled || 0,
      quoteAmount: order.cost || 0,
      fee: order.fee?.cost || 0,
    };
  }
}
