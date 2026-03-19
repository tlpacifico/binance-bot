import Database from 'better-sqlite3';
import { BotState, TradeRecord } from './types';

export interface SavedState {
  state: BotState;
  lastTradePrice: number;
  btcBalance: number;
  eurBalance: number;
  initialBalanceEur: number;
  lastTradeTimestamp: string | null;
}

export function initDb(dbPath: string): Database.Database {
  const db = new Database(dbPath);
  db.pragma('journal_mode = WAL');

  db.exec(`
    CREATE TABLE IF NOT EXISTS trades (
      id            INTEGER PRIMARY KEY AUTOINCREMENT,
      timestamp     TEXT NOT NULL,
      side          TEXT NOT NULL CHECK(side IN ('BUY', 'SELL')),
      price         REAL NOT NULL,
      quantity      REAL NOT NULL,
      quote_amount  REAL NOT NULL,
      fee           REAL NOT NULL DEFAULT 0
    );

    CREATE TABLE IF NOT EXISTS bot_state (
      key   TEXT PRIMARY KEY,
      value TEXT NOT NULL
    );
  `);

  return db;
}

export function getState(db: Database.Database): SavedState | null {
  const row = db.prepare('SELECT key, value FROM bot_state').all() as { key: string; value: string }[];
  if (row.length === 0) return null;

  const map = new Map(row.map((r) => [r.key, r.value]));
  const state = map.get('state');
  if (!state) return null;

  return {
    state: state as BotState,
    lastTradePrice: parseFloat(map.get('last_trade_price') || '0'),
    btcBalance: parseFloat(map.get('btc_balance') || '0'),
    eurBalance: parseFloat(map.get('eur_balance') || '0'),
    initialBalanceEur: parseFloat(map.get('initial_balance_eur') || '0'),
    lastTradeTimestamp: map.get('last_trade_timestamp') || null,
  };
}

export function setState(
  db: Database.Database,
  state: BotState,
  lastTradePrice: number,
  btcBalance: number,
  eurBalance: number,
  initialBalanceEur?: number,
  lastTradeTimestamp?: string,
): void {
  const upsert = db.prepare(
    'INSERT INTO bot_state (key, value) VALUES (?, ?) ON CONFLICT(key) DO UPDATE SET value = excluded.value',
  );

  const tx = db.transaction(() => {
    upsert.run('state', state);
    upsert.run('last_trade_price', lastTradePrice.toString());
    upsert.run('btc_balance', btcBalance.toString());
    upsert.run('eur_balance', eurBalance.toString());
    if (initialBalanceEur !== undefined) {
      upsert.run('initial_balance_eur', initialBalanceEur.toString());
    }
    if (lastTradeTimestamp !== undefined) {
      upsert.run('last_trade_timestamp', lastTradeTimestamp);
    }
  });

  tx();
}

export function insertTrade(db: Database.Database, trade: TradeRecord): void {
  db.prepare(
    'INSERT INTO trades (timestamp, side, price, quantity, quote_amount, fee) VALUES (?, ?, ?, ?, ?, ?)',
  ).run(trade.timestamp, trade.side, trade.price, trade.quantity, trade.quoteAmount, trade.fee);
}

export function getTrades(db: Database.Database, limit = 50, offset = 0): TradeRecord[] {
  return db
    .prepare('SELECT * FROM trades ORDER BY id DESC LIMIT ? OFFSET ?')
    .all(limit, offset) as any[];
}

export function getTradeCount(db: Database.Database): number {
  const row = db.prepare('SELECT COUNT(*) as count FROM trades').get() as { count: number };
  return row.count;
}

export function getLastTradeTimestamp(db: Database.Database): string | null {
  const row = db.prepare('SELECT timestamp FROM trades ORDER BY id DESC LIMIT 1').get() as { timestamp: string } | undefined;
  return row?.timestamp || null;
}

export function getTradesToday(db: Database.Database): TradeRecord[] {
  const today = new Date().toISOString().slice(0, 10);
  return db
    .prepare("SELECT * FROM trades WHERE timestamp >= ? ORDER BY id DESC")
    .all(today + 'T00:00:00.000Z') as any[];
}
