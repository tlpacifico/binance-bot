# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run Commands

- **Restore**: `dotnet restore`
- **Build**: `dotnet build`
- **Test**: `dotnet test`
- **Run**: `dotnet run --project src/BinanceBot.Worker`

## Architecture

This is a .NET 9 Binance trading bot with a **plugin-based strategy system**. The default strategy is DCA with Portfolio Rebalancing (60% BTC / 40% EUR target). The bot trades BTC/EUR, is controllable via Telegram commands, and has a web dashboard.

### Solution Structure

- **BinanceBot.Core** — Domain: interfaces (`ITradingStrategy`, `IBinanceClient`, `ITelegramService`, `ITradeRepository`, `IStateRepository`), models (`TradeRecord`, `Portfolio`, `TradeDecision`, `StrategyContext`, `PriceData`), enums, configuration classes. Zero external dependencies.
- **BinanceBot.Infrastructure** — Adapters: Binance.Net client, Telegram.Bot service + command system, EF Core + SQLite persistence.
- **BinanceBot.Strategies** — Strategy plugins. Currently: `DcaRebalancingStrategy` with `AllocationCalculator` (pure logic).
- **BinanceBot.Worker** — Host: ASP.NET Core Minimal APIs for dashboard, BackgroundServices for price monitoring, trading engine, and Telegram command polling.

### Strategy Pattern

Strategies implement `ITradingStrategy` and are registered in `StrategyResolver`. The engine calls `strategy.EvaluateAsync(context)` which returns a `TradeDecision` (Hold/Buy/Sell). Strategies **never** execute trades — the engine does. Hot-swap via Telegram `/strategy` command.

### Telegram Commands

`/status`, `/start`, `/stop`, `/rebalance`, `/settings`, `/history [n]`, `/strategy [name]`, `/help`. Authorization via ChatId. Commands implement `ITelegramCommand`.

### State Persistence

EF Core + SQLite (`data/bot.db`). Tables: `Trades` (history) and `BotState` (single-row: active strategy, balances, run state, strategy-specific JSON).

### Configuration

`appsettings.json` + environment variables + user-secrets. Sections: `Binance`, `Telegram`, `Dashboard`, `Strategy:DcaRebalancing`.

### Dashboard

ASP.NET Core Minimal APIs serving static files from `wwwroot/`. Endpoints: `/api/health`, `/api/status`, `/api/trades`. Bearer token auth on status/trades.

## Legacy

The original TypeScript bot is in `ts_backup/` for reference.

## Language Note

Documentation (DEPLOY.md) and some comments are in Portuguese. Code identifiers and types are in English.
