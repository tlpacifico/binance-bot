import dotenv from 'dotenv';
import { AppConfig } from './types';

dotenv.config();

function requireEnv(key: string): string {
  const val = process.env[key];
  if (!val) throw new Error(`Missing required env var: ${key}`);
  return val;
}

export function loadConfig(): AppConfig {
  const config: AppConfig = Object.freeze({
    binanceApiKey: requireEnv('BINANCE_API_KEY'),
    binanceApiSecret: requireEnv('BINANCE_API_SECRET'),
    binanceTestnet: process.env.BINANCE_TESTNET === 'true',
    tradingPair: process.env.TRADING_PAIR || 'BTC/EUR',
    initialBalanceEur: parseFloat(process.env.INITIAL_BALANCE_EUR || '0'),
    sellThresholdPct: parseFloat(process.env.SELL_THRESHOLD_PCT || '0.025'),
    buyThresholdPct: parseFloat(process.env.BUY_THRESHOLD_PCT || '0.025'),
    priceIntervalMs: parseInt(process.env.PRICE_POLL_INTERVAL_MS || '30000', 10),
    telegramBotToken: requireEnv('TELEGRAM_BOT_TOKEN'),
    telegramChatId: requireEnv('TELEGRAM_CHAT_ID'),
    dashboardPort: parseInt(process.env.DASHBOARD_PORT || '3000', 10),
    dashboardAuthToken: requireEnv('DASHBOARD_AUTH_TOKEN'),
    logLevel: process.env.LOG_LEVEL || 'info',
  });

  // Validation
  if (config.sellThresholdPct <= 0 || config.sellThresholdPct > 0.5) {
    throw new Error(`SELL_THRESHOLD_PCT must be between 0 and 0.5 (got ${config.sellThresholdPct})`);
  }
  if (config.buyThresholdPct <= 0 || config.buyThresholdPct > 0.5) {
    throw new Error(`BUY_THRESHOLD_PCT must be between 0 and 0.5 (got ${config.buyThresholdPct})`);
  }
  if (config.priceIntervalMs < 5000) {
    throw new Error(`PRICE_POLL_INTERVAL_MS must be >= 5000ms (got ${config.priceIntervalMs})`);
  }
  if (config.dashboardPort < 1 || config.dashboardPort > 65535) {
    throw new Error(`DASHBOARD_PORT must be 1-65535 (got ${config.dashboardPort})`);
  }
  if (config.dashboardAuthToken.length < 16) {
    throw new Error('DASHBOARD_AUTH_TOKEN must be at least 16 characters');
  }
  if (!config.tradingPair.includes('/')) {
    throw new Error(`TRADING_PAIR must be in format BASE/QUOTE (got ${config.tradingPair})`);
  }

  return config;
}
