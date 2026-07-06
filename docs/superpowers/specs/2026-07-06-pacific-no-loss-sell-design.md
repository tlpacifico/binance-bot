# Pacific: Never Sell BTC Below Purchase Price

**Date:** 2026-07-06
**Status:** Approved design, pending implementation plan

## Problem

The Pacific strategy can sell BTC below the price it was bought at, realizing a loss. When
`HOLDING BTC`, `lastTradePrice` is the last **buy** price. `EvaluateSell` has three sell paths:

1. **Profit target** — sells at `buy × (1 + sellThresholdPct)` (always above buy). ✅
2. **Trailing escape (sell side)** — arms when the low falls ≥ `EscapeDrawdownPct` below buy, and fires
   when price rises to `lowSinceTrade × (1 + EscapeRecoveryPct)`. That escape target is **below the buy
   price** (e.g. low at −4% → escape target ≈ −1.6%; low at −10% → ≈ −7.75%). It realizes a loss. ❌
3. **Hard stop-loss** — sells at `buy × (1 − HardStopLossPct)`, below buy by definition. Currently
   disabled (`HardStopLossPct = 0`) but a latent violation. ❌

The owner's requirement: **never sell BTC below the purchase price.** The sell-side escape and the
hard-stop both violate it.

## Decisions (from brainstorming)

- **Holding BTC:** remove the trailing-escape and the hard-stop from the sell side. Sell **only** at the
  profit target. Hold BTC indefinitely through a dip rather than realize a loss.
- **Holding EUR:** keep the buy-side latched escape unchanged (accumulate-BTC bias — re-enter on a
  pullback even if higher than the last sell, so the bot is never stuck in EUR through an up-move).
- **Remove `HardStopLossPct` entirely** (setting + parameter + logic) so no future config can breach the
  invariant.

## Invariant

> When `HOLDING BTC`, the only sell path is the profit target (`buy × (1 + sellThresholdPct)`), which is
> strictly above the buy price. Therefore the bot never sells BTC below its purchase price, by
> construction.

## Non-goals

- No change to the buy side (`EvaluateBuy`) — the latched escape stays.
- No change to `PositionExtremes` tracking or persistence. `lowSinceTrade` keeps being tracked and
  remains available as position telemetry (shown on the dashboard), even though the sell decision no
  longer consults it.
- No new framework/build step on the dashboard.

## Design

### 1. `PacificCalculator` — sell side collapses to profit-target-only

`EvaluateSell` becomes:

```
profitTarget = lastTradePrice * (1 + sellThresholdPct)
if (currentPrice >= profitTarget)
    return SellAll("... >= profit target ... (normal)")   // still guarded by minTradeEur
return Hold("... below profit target (normal)")
```

Remove from `EvaluateSell`: the `drawdown`/`maxDrawdown` computation, the hard-stop branch, and the
trailing-escape branch. `EvaluateSell` no longer needs `lowSinceTrade`, `escapeDrawdownPct`,
`escapeRecoveryPct`, or `hardStopLossPct`.

**Public signature change** — drop `hardStopLossPct` and `lowSinceTrade` (both now unused across the
whole calculator; the buy side uses only `highSinceTrade`):

```csharp
// before
Evaluate(currentPrice, portfolio, lastTradePrice, lowSinceTrade, highSinceTrade,
         sellThresholdPct, buyThresholdPct, escapeDrawdownPct, escapeRecoveryPct,
         hardStopLossPct, minTradeEur)
// after
Evaluate(currentPrice, portfolio, lastTradePrice, highSinceTrade,
         sellThresholdPct, buyThresholdPct, escapeDrawdownPct, escapeRecoveryPct,
         minTradeEur)
```

`EvaluateBuy` is unchanged in behavior (latched escape via `maxRunup` from `highSinceTrade`).

### 2. `PacificStrategy` — update the call

Drop `HardStopLossPct` and the `lowSinceTrade` argument from the `PacificCalculator.Evaluate` call.
The confirmation-ticks wrapper and everything else are unchanged. `PositionExtremes` still tracks and
persists both the low and the high (unchanged); the low is simply no longer forwarded to the calculator.
The dashboard reads the persisted extremes independently in `/api/status`.

### 3. `PacificSettings` + `appsettings.json` — remove `HardStopLossPct`

Delete the `HardStopLossPct` property from `PacificSettings` and the `"HardStopLossPct": 0` line from
`appsettings.json`. `SellThresholdPct`, `BuyThresholdPct`, `EscapeDrawdownPct`, `EscapeRecoveryPct`
remain.

### 4. `PacificView` (dashboard projection)

- Drop the `hardStopLossPct` parameter.
- `EscapeArmPrice` becomes `decimal?`.
- **Holding BTC branch:** `Mode` is always `normal`; `EscapeArmPrice = null`; `EscapeTarget = null`;
  `ActiveTarget = ProfitTarget`. (No escape, no hard-stop.)
- **Holding EUR branch:** unchanged — `EscapeArmPrice = lastTrade × (1 + escapeDrawdownPct)`, latched
  escape via `maxRunup`, `Mode` normal/escape-armed.
- `LowSinceTrade`/`HighSinceTrade` telemetry fields stay on the record.

The `ModeHardStop` constant is removed (no producer remains).

### 5. Backend `/api/status`

Drop `hardStopLossPct` from the `PacificView.Compute` call and remove the `hardStopLossPct` field from
the emitted `pacific` object. `escapeArmPrice` is now nullable in the JSON (null when holding BTC).

### 6. Dashboard `index.html`

- **Ladder:** only add the `Escape arm` marker when `p.escapeArmPrice != null`. (Fixes the confusing
  "Escape arm" marker that showed while `HOLDING BTC` in Normal mode.) The `Escape target` marker
  already renders only when `escapeTarget != null`.
- **`explainPacific`:** the holding-BTC branch becomes a single line — *"Holding BTC. Sells at €X
  (+2.5%)."* — with no escape or hard-stop clause. Remove the BTC hard-stop and BTC escape-armed
  branches (dead once BTC mode is always normal). EUR branches unchanged.
- Remove any `hardStopLossPct` reference from the JS.

## Testing

**`PacificCalculatorTests`:**
- Remove the now-invalid sell-side tests: the two hard-stop tests, the two trailing-escape *sell* tests,
  and the latched-sell regression test (`HoldingBtc_TroughArmedThenBounced...`).
- Update the `Eval` helper to the new signature (no `lowSinceTrade`, no `hardStop`).
- Add **invariant test**: holding BTC, `buy = 60000`; for `currentPrice` at each of −10%, −5%, −1%,
  −0.1%, and exactly buy, and up to just below the profit target → assert `Hold` (never `Sell`).
- Add: holding BTC, deep dip then partial recovery (e.g. low 54000, price 58000, buy 60000) → `Hold`.
- Keep: profit-target sell, buy-side normal, buy-side escape, buy-side latched escape.

**`PacificViewTests`:**
- Update helper (drop `hardStop`).
- Holding BTC: assert `Mode == normal`, `EscapeArmPrice == null`, `EscapeTarget == null` even after a
  deep dip. Remove the BTC hard-stop test.
- Keep EUR normal / escape-armed / latched tests.

**`PacificStrategyTests`:** remove `HardStopLossPct` from settings setup; behavior assertions unchanged.

No JS test harness exists; dashboard verified by inspection + Node logic check (as before).

## Files touched

- `src/BinanceBot.Strategies/Pacific/PacificCalculator.cs`
- `src/BinanceBot.Strategies/Pacific/PacificStrategy.cs`
- `src/BinanceBot.Strategies/Pacific/PacificSettings.cs`
- `src/BinanceBot.Worker/appsettings.json`
- `src/BinanceBot.Worker/Services/PacificView.cs`
- `src/BinanceBot.Worker/Program.cs`
- `src/BinanceBot.Worker/wwwroot/index.html`
- `tests/BinanceBot.Strategies.Tests/PacificCalculatorTests.cs`
- `tests/BinanceBot.Strategies.Tests/PacificStrategyTests.cs`
- `tests/BinanceBot.Worker.Tests/PacificViewTests.cs`
