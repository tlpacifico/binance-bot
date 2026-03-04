import path from 'path';
import fs from 'fs';
import cron from 'node-cron';
import { loadConfig } from './config';
import { createLogger } from './logger';
import { initDb, getTradesToday } from './db';
import { BinanceClient } from './binance-client';
import { TradingEngine } from './trading-engine';
import { PriceMonitor } from './price-monitor';
import { TelegramNotifier } from './telegram';
import { startDashboard } from './dashboard';

async function main() {
  const config = loadConfig();
  const logger = createLogger(config.logLevel);

  const dataDir = path.join(__dirname, '..', 'data');
  if (!fs.existsSync(dataDir)) fs.mkdirSync(dataDir, { recursive: true });

  const db = initDb(path.join(dataDir, 'bot.db'));
  const client = new BinanceClient(config.binanceApiKey, config.binanceApiSecret, config.binanceTestnet);
  await client.init();
  logger.info('Binance time synced');

  const telegram = new TelegramNotifier(config.telegramBotToken, config.telegramChatId);
  const engine = new TradingEngine(config, client, db, telegram, logger);
  const monitor = new PriceMonitor(client, engine, config, logger, telegram);

  await engine.initialize();
  await monitor.start();

  const server = startDashboard(config, client, engine, monitor, logger);

  // Daily summary at 20:00 UTC
  cron.schedule('0 20 * * *', async () => {
    try {
      const status = engine.getStatus(monitor.getCurrentPrice());
      const todayTrades = getTradesToday(db);
      await telegram.sendDailySummary(status, todayTrades);
    } catch (err) {
      logger.error({ err }, 'Daily summary failed');
    }
  });

  // Startup notification
  const price = monitor.getCurrentPrice();
  telegram.sendStartup(engine.getState(), price).catch((err) => {
    logger.warn({ err }, 'Startup notification failed');
  });

  logger.info('Bot started successfully');

  // Graceful shutdown
  const shutdown = () => {
    logger.info('Shutting down...');
    monitor.stop();
    server.close();
    db.close();
    process.exit(0);
  };
  process.on('SIGTERM', shutdown);
  process.on('SIGINT', shutdown);
}

main().catch((err) => {
  console.error('Fatal startup error:', err);
  process.exit(1);
});
