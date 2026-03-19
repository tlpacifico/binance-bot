export enum BotState {
  HOLDING_EUR = 'HOLDING_EUR',
  HOLDING_BTC = 'HOLDING_BTC',
}

export interface TradeRecord {
  id?: number;
  timestamp: string;
  side: 'BUY' | 'SELL';
  price: number;
  quantity: number;
  quoteAmount: number;
  fee: number;
}

export interface BotStatus {
  state: BotState;
  lastTradePrice: number;
  btcBalance: number;
  eurBalance: number;
  initialBalanceEur: number;
  currentBtcPrice: number;
  pnlEur: number;
  pnlPct: number;
  targetPrice: number;
  uptimeMs: number;
  confirmation: {
    active: boolean;
    ticksCollected: number;
    ticksRequired: number;
    avgPrice: number;
  };
}

export interface PriceData {
  last: number;
  low24h: number;
}

export interface AppConfig {
  binanceApiKey: string;
  binanceApiSecret: string;
  binanceTestnet: boolean;
  tradingPair: string;
  initialBalanceEur: number;
  sellThresholdPct: number;
  buyThresholdPct: number;
  priceIntervalMs: number;
  telegramBotToken: string;
  telegramChatId: string;
  dashboardPort: number;
  dashboardAuthToken: string;
  confirmationTicks: number;
  staleTradeDays: number;
  logLevel: string;
}
