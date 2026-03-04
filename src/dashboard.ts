import express, { Request, Response, NextFunction } from 'express';
import http from 'http';
import path from 'path';
import pino from 'pino';
import { AppConfig } from './types';
import { BinanceClient } from './binance-client';
import { TradingEngine } from './trading-engine';
import { PriceMonitor } from './price-monitor';

function authMiddleware(token: string) {
  return (req: Request, res: Response, next: NextFunction) => {
    const provided = req.headers.authorization?.replace('Bearer ', '');
    if (provided !== token) {
      res.status(401).json({ error: 'Unauthorized' });
      return;
    }
    next();
  };
}

export function startDashboard(
  config: AppConfig,
  client: BinanceClient,
  engine: TradingEngine,
  monitor: PriceMonitor,
  logger: pino.Logger,
): http.Server {
  const app = express();

  app.use(express.static(path.join(__dirname, '..', 'public')));

  app.get('/api/status', authMiddleware(config.dashboardAuthToken), (_req: Request, res: Response) => {
    const price = monitor.getCurrentPrice();
    res.json(engine.getStatus(price));
  });

  app.get('/api/trades', authMiddleware(config.dashboardAuthToken), async (req: Request, res: Response) => {
    const limit = parseInt((req.query.limit as string) || '50', 10);
    try {
      const trades = await client.getMyTrades(config.tradingPair, limit);
      res.json({ trades, total: trades.length });
    } catch (err) {
      logger.error({ err }, 'Failed to fetch trades from Binance');
      res.json({ trades: [], total: 0 });
    }
  });

  app.get('/api/health', (_req: Request, res: Response) => {
    res.json({ ok: true, uptime: process.uptime() });
  });

  const server = app.listen(config.dashboardPort, () => {
    logger.info({ port: config.dashboardPort }, 'Dashboard started');
  });

  return server;
}
