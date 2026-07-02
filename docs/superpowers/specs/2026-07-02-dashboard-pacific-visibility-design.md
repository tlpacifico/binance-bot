# Dashboard: Pacific Strategy Visibility + Trade Timestamp Fix

**Date:** 2026-07-02
**Status:** Approved design, pending implementation plan

## Problem

The web dashboard shows a single **"Target Price"** with no context. It cannot explain *why*
the bot is holding and not trading. The situation that motivated this: the bot sold BTC and is
now `HOLDING EUR`, waiting to buy back at `lastTrade × 0.975`, while the price drifted up ~2.5% —
sitting in the **dead band** between the profit target (below) and the trailing-escape trigger
(above), so neither fires. From the dashboard this is indistinguishable from a stuck/broken bot.

Separately, the trade-history table shows a trade dated `01/01/1` — `DateTime.MinValue`. Root
cause: the Binance client stamps trades with `data.CreateTime`, and Binance's place-order ack
frequently returns that field unset, overwriting the model's `DateTime.UtcNow` default with zero.
This is a real bug that can affect live trades, not only seed data.

## Goals

1. Make the Pacific strategy's decision **self-explanatory** on the dashboard: current mode, the
   plain-language "what it will do next", and a visual of where the price sits relative to its
   triggers (so the dead band is obvious).
2. Guarantee trades never persist a zero timestamp; guard the display against bad dates.

## Non-goals

- No changes to the Pacific trading logic or thresholds. This is display-only, plus the
  timestamp-source bug fix.
- No retroactive correction of the already-persisted `01/01/1` row (it is existing data; it will
  be superseded by real trades or cleared manually).
- No new frontend framework or build step. The dashboard stays static HTML/CSS/vanilla JS.
- No changes to the DCA strategy's dashboard experience (the new card simply doesn't render for it).

## Design

### 1. `PacificView` — one pure function for the display math

Grow the existing `PacificTargetPrice` helper (in `BinanceBot.Worker/Services`) into
`PacificView.Compute(...)`, keeping all Pacific display math in one unit-tested place that mirrors
`PacificCalculator`. It returns a record:

```csharp
public sealed record PacificView(
    bool HoldingBtc,              // btcValueEur > eurBalance — same definition the calculator uses
    string Mode,                  // "normal" | "escape-armed" | "hard-stop"
    decimal LastTradePrice,
    decimal ProfitTarget,         // lastTrade × (1 + sell)  /  lastTrade × (1 − buy)
    decimal EscapeArmPrice,       // lastTrade × (1 − escapeDrawdown) / (1 + escapeDrawdown)
    decimal? EscapeTarget,        // low × (1 + rec) / high × (1 − rec); null until armed
    decimal ActiveTarget,         // what the bot is actually waiting for (escapeTarget if armed, else profitTarget)
    decimal LowSinceTrade,
    decimal HighSinceTrade,
    decimal MoveFromLastTradePct); // signed % change vs last trade
```

**Signature:**
```csharp
static PacificView? Compute(
    bool holdingBtc,
    decimal lastTradePrice,
    decimal currentPrice,
    decimal lowSinceTrade,
    decimal highSinceTrade,
    decimal sellThresholdPct,
    decimal buyThresholdPct,
    decimal escapeDrawdownPct,
    decimal escapeRecoveryPct,
    decimal hardStopLossPct);
```

Returns `null` when `lastTradePrice <= 0`.

**Mode logic (mirrors `PacificCalculator` exactly):**

- Holding BTC (wants to sell): `drawdown = (lastTrade − current) / lastTrade`.
  - `hard-stop` when `hardStopLossPct > 0 && drawdown >= hardStopLossPct`.
  - else `escape-armed` when `drawdown >= escapeDrawdownPct` → `EscapeTarget = low × (1 + rec)`.
  - else `normal`.
  - `EscapeArmPrice = lastTrade × (1 − escapeDrawdownPct)`.
- Holding EUR (wants to buy): `runup = (current − lastTrade) / lastTrade`. No hard-stop on this side.
  - `escape-armed` when `runup >= escapeDrawdownPct` → `EscapeTarget = high × (1 − rec)`.
  - else `normal`.
  - `EscapeArmPrice = lastTrade × (1 + escapeDrawdownPct)`.
- `ActiveTarget` = `EscapeTarget` when armed, else `ProfitTarget`.

The old `Compute` returned just `decimal?` (the active target). Callers that only need the target
price read `view?.ActiveTarget`, preserving current behavior.

### 2. Backend — `/api/status`

In the Pacific branch, build the `PacificView` (reading `PositionExtremes.FromJson(state.StrategyStateJson)`,
falling back to `PositionExtremes.Initial(currentPrice)` as today) and add a `pacific` object to the
response = the `PacificView` fields **plus** the settings percentages the UI needs for labels:
`sellThresholdPct`, `buyThresholdPct`, `escapeDrawdownPct`, `escapeRecoveryPct`, `hardStopLossPct`.

`holdingBtc` is computed as `btcBalance × price > eurBalance` (the calculator's definition), replacing
the `btcAllocationPct >= 50` heuristic for this object so the displayed mode always matches what the
engine would decide.

Keep the existing top-level `targetPrice` field, now set to `view?.ActiveTarget` — backward compatible.

### 3. Frontend — new "Pacific Strategy" card

Rendered only when `status.strategy === "pacific"` and `status.pacific != null`; otherwise the card is
hidden. Placed between the Status and Balances cards. Three parts:

1. **Mode badge** — `Normal` / `Escape armed` / `Hard-stop`, color-coded (reuse the existing
   `.badge` styles; new modifier classes for escape/hard-stop colors).
2. **Plain-language line**, generated in JS from the `pacific` object. Examples:
   - EUR / normal: *"Holding EUR. Buys back at €51,322 (−2.5%), or if price rises to €55,270 (+5%)
     buys on a 2.5% pullback from the peak."*
   - EUR / escape-armed: *"Escape armed. Tracking peak €55,400; buys back at €54,015 (−2.5% from peak)."*
   - BTC / normal: *"Holding BTC. Sells at €X (+2.5%), or if price falls to €Y (−5%) sells on a 2.5%
     bounce from the low."*
   - BTC / escape-armed: *"Escape armed. Tracking low €X; sells at €Y (+2.5% from low)."*
   - BTC / hard-stop: *"Hard-stop zone. Drawdown N% ≥ M%; sells at market."*
3. **Horizontal price ladder** — a CSS bar with labeled markers positioned on a shared min–max scale
   (min/max derived from all relevant prices with a small margin): last-trade, profit target,
   escape-arm, escape-target (only when armed), and a distinct **current-price** pointer. Price sitting
   between the profit target and the escape-arm marker visually *is* the dead band.

No JS test harness exists; the frontend stays untested, consistent with the current dashboard.

### 4. Trade timestamp fix

In `BinanceClientAdapter.MarketBuyAsync` and `MarketSellAsync`:

```csharp
Timestamp = data.CreateTime == default ? DateTime.UtcNow : data.CreateTime,
```

Market orders fill immediately, so `UtcNow` is accurate. Add a display guard in the dashboard's
`formatTime`: render any timestamp before year 2000 as `—` rather than a nonsense date.

## Testing

Unit tests for `PacificView.Compute` (in the Worker test project):

- returns `null` when `lastTradePrice <= 0`
- holding EUR, normal: mode `normal`, `ActiveTarget == ProfitTarget`, `EscapeTarget == null`
- holding EUR, escape-armed (run-up ≥ drawdown): mode `escape-armed`, `ActiveTarget == high × (1 − rec)`
- holding BTC, normal
- holding BTC, escape-armed (drawdown ≥ threshold): `ActiveTarget == low × (1 + rec)`
- holding BTC, hard-stop (`hardStopLossPct > 0`, drawdown ≥ it): mode `hard-stop`

Existing `PacificTargetPrice`/target-price behavior remains covered via `view?.ActiveTarget`.

The timestamp fix is verified by inspection of the two call sites; no exchange round-trip is unit-tested
(the Binance client is not currently under test).

## Files touched

- `src/BinanceBot.Worker/Services/PacificTargetPrice.cs` → `PacificView` (record + `Compute`).
- `src/BinanceBot.Worker/Program.cs` — `/api/status` builds and returns the `pacific` object.
- `src/BinanceBot.Worker/wwwroot/index.html` — new card, plain-language builder, ladder, `formatTime` guard.
- `src/BinanceBot.Worker/wwwroot/style.css` — badge modifiers + ladder styles.
- `src/BinanceBot.Infrastructure/Binance/BinanceClientAdapter.cs` — timestamp fallback (both methods).
- `tests/BinanceBot.Worker.Tests/PacificViewTests.cs` — new.
