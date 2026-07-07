# Design: Deposit/Withdraw Commands + Cash-Flow Ledger

**Date:** 2026-07-07
**Status:** Approved (design)
**Tech Stack:** .NET 9, EF Core (PostgreSQL/Npgsql), Telegram.Bot, xUnit + FluentAssertions + NSubstitute, ASP.NET Core Minimal APIs (dashboard).

## Problem

The P&L baseline (`BotState.InitialBalanceEur`) represents the **net capital deposited** (cost basis), not a one-time snapshot. When the user adds or removes money from the Binance wallet, the wallet value changes but that is **not trading profit**. Today there is no way to record a deposit/withdrawal, so any capital movement silently corrupts the P&L (exactly the failure mode that produced the bogus 257.95% reading from the €100 placeholder).

## Goal

Let the user record capital movements from Telegram, keeping the P&L baseline correct, and keep an auditable history of every movement viewable on the dashboard.

The rule: **deposit → baseline += amount; withdrawal → baseline -= amount.** Both wallet value and baseline move together, so P&L keeps reflecting only trading gains.

## Scope decisions (from brainstorming)

- **EUR only.** Movements are always in euros (`/deposit 100`). No BTC deposits (a BTC transfer would need price-at-time conversion — out of scope, YAGNI).
- **Write via Telegram, view via Dashboard.** No Telegram list command.
- **Immediate execution, no confirmation flow.** The Telegram host stays stateless (single-shot commands). Each command replies with the old → new baseline. Typos are corrected with the opposite command (a reversing entry), not an undo/confirm mechanism.

## Non-goals

- BTC-denominated movements.
- `/undo` or conversational confirmation (would require conversation state in `TelegramCommandHost`).
- A Telegram command to list the ledger (dashboard covers viewing).
- Touching actual wallet balances — those are read live from Binance (`GetBalancesAsync`) and reconciled on restart. The commands adjust bookkeeping (baseline + ledger) only.

## Architecture

### 1. Data model — new `CashFlows` table

Mirrors the existing `Trades` pattern. One row per capital movement.

`CashFlowEntity` (BinanceBot.Infrastructure.Persistence.Entities):

| Column | Type | Notes |
|---|---|---|
| `Id` | int (identity) | PK |
| `Timestamp` | timestamp with time zone | when the movement was recorded |
| `Type` | text | `Deposit` or `Withdrawal` (stored as string, like `TradeEntity.Side`) |
| `AmountEur` | numeric(18,8) | positive magnitude of the movement |
| `BalanceAfter` | numeric(18,8) | resulting `InitialBalanceEur` after applying — for audit + dashboard display |

- `CashFlowType` enum (`Deposit`, `Withdrawal`) in BinanceBot.Core.Enums.
- Register `DbSet<CashFlowEntity> CashFlows` in `BotDbContext`; configure `ToTable("CashFlows")`, index on `Timestamp`, `numeric(18,8)` on `AmountEur`/`BalanceAfter`.
- New EF Core migration (`AddCashFlows`). Applied automatically on startup if the project already runs migrations on boot; otherwise generated via `dotnet ef migrations add`.

### 2. Repository — `ICashFlowRepository`

Interface in BinanceBot.Core.Interfaces, implementation in BinanceBot.Infrastructure.Persistence.

```csharp
public sealed record CashFlowResult(decimal OldBaseline, decimal NewBaseline);

public interface ICashFlowRepository
{
    Task<CashFlowResult> ApplyAsync(CashFlowType type, decimal amountEur, CancellationToken ct = default);
    Task<IReadOnlyList<CashFlowRecord>> GetRecentAsync(int limit, CancellationToken ct = default);
}
```

- `ApplyAsync` is **atomic**: within a single `BotDbContext`/`SaveChangesAsync` it loads `BotState` (Id=1), adjusts `InitialBalanceEur` (+ for deposit, − for withdrawal), inserts the `CashFlow` row with the resulting `BalanceAfter`, and saves once. Baseline and ledger can never diverge.
- Validation lives in the command (see below); `ApplyAsync` assumes a positive amount and a valid resulting baseline. It throws if `BotState` (Id=1) does not exist (bot not initialized yet).
- `GetRecentAsync` returns the latest `limit` movements ordered by `Timestamp` desc, projected to a `CashFlowRecord` model (Core), like `ITradeRepository.GetRecentAsync`.

Registered scoped in `Program.cs` alongside `ITradeRepository`/`IStateRepository`.

### 3. Telegram commands — `DepositCommand` + `WithdrawCommand`

Both implement `ITelegramCommand`, registered as singletons in `Program.cs`, resolve `ICashFlowRepository` via `IServiceScopeFactory` (the existing pattern, since commands are singletons and repos are scoped).

**Shared parse/validation:**
- `args[0]` parsed as `decimal` (invariant culture). Missing/non-numeric/`<= 0` → reply `❌ Uso: /deposit <valor em EUR maior que 0>` (respective verb).
- Withdrawal guard-rail: `WithdrawCommand` reads the current baseline via `IStateRepository.GetAsync` **before** calling `ApplyAsync`; if `baseline - amount < 0` it rejects with `❌ Saque de €X deixaria o capital aportado negativo (atual: €Y). Ajuste o valor.` and does not call `ApplyAsync`. (`ApplyAsync` itself assumes the caller validated; it does not re-check.)

**Success reply:**
```
✅ Depósito de €100.00 registrado.
Capital aportado: €340.00 → €440.00
```
(Withdrawal analogous.)

- `/deposit` — `Name => "/deposit"`, `Description => "Registrar aporte de capital (EUR)"`.
- `/withdraw` — `Name => "/withdraw"`, `Description => "Registrar saque de capital (EUR)"`.
- `HelpCommand` output updated to include both.

### 4. Dashboard

- New endpoint `GET /api/cashflows` in the Worker Minimal APIs, **Bearer-auth like `/api/trades`**, returning the recent movements (`GetRecentAsync`).
- New "Aportes / Saques" section in `wwwroot/index.html`: table of Timestamp, Type, Amount (EUR), Balance After. Fetched and rendered like the existing Trade History table.
- **Relabel the existing "Initial Balance" card → "Capital Aportado"** (Net Deposits). The value already comes from `InitialBalanceEur`; only the label text changes. This fixes the original confusion — the number now has an accurate name and a visible history behind it.

### 5. P&L consumers (unchanged logic)

`StatusCommand` (`/status`) and the dashboard `/api/status` endpoint already compute `pnl = totalValue - InitialBalanceEur`. Because `ApplyAsync` keeps `InitialBalanceEur` correct, P&L stays accurate automatically — no change needed there beyond the label rename.

## Data flow (deposit)

```
User: /deposit 100
  → TelegramCommandHost routes to DepositCommand (authorized ChatId only)
  → parse 100, validate > 0
  → scope: ICashFlowRepository.ApplyAsync(Deposit, 100)
       BotState.InitialBalanceEur: 340 → 440   ┐ one transaction
       insert CashFlow{Deposit, 100, BalanceAfter=440} ┘
  → reply "✅ ... Capital aportado: €340.00 → €440.00"

Later: dashboard GET /api/cashflows → shows the row.
       /status and /api/status → P&L now computed against 440.
```

## Error handling

- Invalid/missing amount → command replies with usage, no DB write.
- Withdrawal that would make baseline negative → rejected, no DB write.
- `BotState` not initialized (first run hasn't happened) → `ApplyAsync` throws; `TelegramCommandHost` already wraps command execution in try/catch and replies `❌ Command failed: ...`.
- DB failure → transaction rolls back (single `SaveChanges`); nothing partially applied.

## Testing

xUnit + FluentAssertions + NSubstitute; EF Core with an in-memory/SQLite-in-memory or Npgsql test context matching existing repository tests (`StateRepositoryTests` pattern).

- **Repository:**
  - Deposit adds to baseline; withdrawal subtracts.
  - `BalanceAfter` recorded equals the new baseline.
  - Atomicity: after `ApplyAsync`, both the baseline change and the ledger row are present (and both absent on a simulated failure).
  - `GetRecentAsync` returns newest-first, respects `limit`.
- **Commands:**
  - Valid amount → correct reply text and repo call.
  - Missing / non-numeric / `<= 0` → usage message, no repo call.
  - Withdrawal exceeding baseline → rejected, no repo call.

## Files touched (summary)

- **Core:** `Enums/CashFlowType.cs`, `Interfaces/ICashFlowRepository.cs`, `CashFlowRecord` model.
- **Infrastructure:** `Persistence/Entities/CashFlowEntity.cs`, `Persistence/CashFlowRepository.cs`, `Persistence/BotDbContext.cs` (DbSet + config), new migration, `Telegram/Commands/DepositCommand.cs`, `Telegram/Commands/WithdrawCommand.cs`, `Telegram/Commands/HelpCommand.cs` (update).
- **Worker:** `Program.cs` (register repo + 2 commands + `/api/cashflows` endpoint), `wwwroot/index.html` (ledger section + card relabel).
- **Tests:** repository tests + command tests.
