# Pacific Never-Sell-Below-Buy Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Guarantee the Pacific strategy never sells BTC below its purchase price by collapsing the sell side to profit-target-only and removing the hard-stop; keep the buy-side latched escape.

**Architecture:** `EvaluateSell` reduces to a single profit-target path (`buy × (1 + sellThresholdPct)`), so every sell is strictly above the buy price by construction. The hard-stop mechanism and `HardStopLossPct` setting are removed. `EvaluateBuy` is unchanged. The dashboard projection (`PacificView`) and card are updated so holding BTC always shows Normal with no escape markers.

**Tech Stack:** .NET 9, xUnit + FluentAssertions, static HTML/CSS/vanilla JS.

## Global Constraints

- Invariant: when holding BTC the only sell path is the profit target (`buy × (1 + sellThresholdPct)`), strictly above buy. No code path may sell BTC at or below the buy price.
- Buy side (`EvaluateBuy`) behavior is unchanged — the latched escape (armed via `highSinceTrade`) stays.
- `HardStopLossPct` is removed entirely (setting, parameter, logic, JSON field) — no dormant version.
- `PositionExtremes` tracking/persistence is unchanged; `lowSinceTrade` stays tracked (telemetry) but is no longer passed to `PacificCalculator`.
- Dashboard stays static HTML/CSS/vanilla JS; no JS test harness exists.

---

### Task 1: Sell side → profit-target-only (`PacificCalculator` + `PacificStrategy`)

**Files:**
- Modify: `src/BinanceBot.Strategies/Pacific/PacificCalculator.cs`
- Modify: `src/BinanceBot.Strategies/Pacific/PacificStrategy.cs` (private `Evaluate` helper body, ~lines 90-96)
- Modify: `tests/BinanceBot.Strategies.Tests/PacificCalculatorTests.cs`

**Interfaces:**
- Produces: `PacificCalculator.Evaluate(decimal currentPrice, Portfolio portfolio, decimal lastTradePrice, decimal highSinceTrade, decimal sellThresholdPct, decimal buyThresholdPct, decimal escapeDrawdownPct, decimal escapeRecoveryPct, decimal minTradeEur) → TradeDecision`. (Dropped: `lowSinceTrade`, `hardStopLossPct`.)
- Consumes: nothing new.

- [ ] **Step 1: Rewrite the calculator tests for the new behavior + signature**

Replace the entire body of `tests/BinanceBot.Strategies.Tests/PacificCalculatorTests.cs` with:

```csharp
using BinanceBot.Core.Enums;
using BinanceBot.Core.Models;
using BinanceBot.Strategies.Pacific;
using FluentAssertions;

namespace BinanceBot.Strategies.Tests;

public class PacificCalculatorTests
{
    private const decimal Sell = 0.025m;
    private const decimal Buy = 0.025m;
    private const decimal EscapeDrawdown = 0.05m;
    private const decimal EscapeRecovery = 0.025m;
    private const decimal MinTrade = 10m;

    private static TradeDecision Eval(
        decimal price, Portfolio portfolio, decimal lastTrade, decimal highSinceTrade) =>
        PacificCalculator.Evaluate(
            price, portfolio, lastTrade, highSinceTrade,
            Sell, Buy, EscapeDrawdown, EscapeRecovery, MinTrade);

    private static Portfolio Btc(decimal price) =>
        new() { BtcBalance = 0.01m, EurBalance = 5m, CurrentBtcPrice = price };

    private static Portfolio Eur(decimal price) =>
        new() { BtcBalance = 0m, EurBalance = 500m, CurrentBtcPrice = price };

    // ---- Sell side: profit target only, never below buy ----

    [Fact]
    public void HoldingBtc_PriceAtProfitTarget_ShouldSellNormal()
    {
        // target = 60000 * 1.025 = 61500
        var result = Eval(61_500m, Btc(61_500m), 60_000m, 61_500m);
        result.Action.Should().Be(TradeAction.Sell);
        result.Reason.Should().Contain("normal");
    }

    [Fact]
    public void HoldingBtc_BelowProfit_ShouldHold()
    {
        var result = Eval(59_000m, Btc(59_000m), 60_000m, 60_000m);
        result.Action.Should().Be(TradeAction.Hold);
        result.Reason.Should().Contain("normal");
    }

    [Fact]
    public void HoldingBtc_BelowProfitTarget_NeverSells()
    {
        // Invariant: nothing at or below buy (and up to just under +2.5%) may sell.
        const decimal buy = 60_000m;
        foreach (var price in new[] { 54_000m, 57_000m, 59_400m, 59_940m, 60_000m, 61_499m })
        {
            var result = Eval(price, Btc(price), buy, buy);
            result.Action.Should().Be(TradeAction.Hold, "price {0} is below the +2.5% profit target", price);
        }
    }

    [Fact]
    public void HoldingBtc_DeepDipThenPartialRecovery_HoldsInsteadOfEscapeSelling()
    {
        // Old behavior escape-sold at a loss here; new behavior holds (never sell below buy).
        var result = Eval(58_000m, Btc(58_000m), 60_000m, 60_000m);
        result.Action.Should().Be(TradeAction.Hold);
        result.Reason.Should().Contain("normal");
    }

    // ---- Buy side: unchanged (profit target + latched escape) ----

    [Fact]
    public void HoldingEur_PriceAtProfitTarget_ShouldBuyNormal()
    {
        // target = 60000 * 0.975 = 58500
        var result = Eval(58_500m, Eur(58_500m), 60_000m, 60_000m);
        result.Action.Should().Be(TradeAction.Buy);
        result.Reason.Should().Contain("normal");
    }

    [Fact]
    public void HoldingEur_RunupReached_PriceFellFromHigh_ShouldEscapeBuy()
    {
        // highSinceTrade 72000 → escape target = 72000 * 0.975 = 70200 → price <= target → Buy
        var result = Eval(70_200m, Eur(70_200m), 60_000m, 72_000m);
        result.Action.Should().Be(TradeAction.Buy);
        result.Reason.Should().Contain("trailing-escape");
    }

    [Fact]
    public void HoldingEur_RunupReached_NoPullbackYet_ShouldHold()
    {
        // escape target 70200; price 71000 > target → Hold (armed)
        var result = Eval(71_000m, Eur(71_000m), 60_000m, 72_000m);
        result.Action.Should().Be(TradeAction.Hold);
        result.Reason.Should().Contain("trailing-escape");
    }

    [Fact]
    public void HoldingEur_PeakArmedThenRetracedBelowArmThreshold_ShouldStillEscapeBuy()
    {
        // high 63600 (+6%) but current 62000 (instantaneous run-up 3.33% < 5%); latched by high.
        // escape target = 63600 * 0.975 = 62010; price 62000 <= 62010 → Buy.
        var result = Eval(62_000m, Eur(62_000m), 60_000m, 63_600m);
        result.Action.Should().Be(TradeAction.Buy);
        result.Reason.Should().Contain("trailing-escape");
    }

    // ---- Guards ----

    [Fact]
    public void EmptyPortfolio_ShouldHold()
    {
        var portfolio = new Portfolio { BtcBalance = 0m, EurBalance = 0m, CurrentBtcPrice = 60_000m };
        var result = Eval(60_000m, portfolio, 60_000m, 60_000m);
        result.Action.Should().Be(TradeAction.Hold);
        result.Reason.Should().Contain("zero");
    }

    [Fact]
    public void HoldingEur_BelowMinTrade_ShouldHold()
    {
        var portfolio = new Portfolio { BtcBalance = 0m, EurBalance = 5m, CurrentBtcPrice = 58_000m };
        // at profit target but EUR balance €5 < €10 min
        var result = PacificCalculator.Evaluate(58_500m, portfolio, 60_000m, 60_000m,
            Sell, Buy, EscapeDrawdown, EscapeRecovery, MinTrade);
        result.Action.Should().Be(TradeAction.Hold);
        result.Reason.Should().Contain("minimum");
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail (build error — old signature)**

Run: `dotnet test tests/BinanceBot.Strategies.Tests --filter PacificCalculatorTests`
Expected: FAIL to build — `PacificCalculator.Evaluate` still has the old 11-arg signature.

- [ ] **Step 3: Update `PacificCalculator.Evaluate` signature + dispatch**

In `src/BinanceBot.Strategies/Pacific/PacificCalculator.cs`, replace the `Evaluate` method (the public method at the top) with:

```csharp
    public static TradeDecision Evaluate(
        decimal currentPrice,
        Portfolio portfolio,
        decimal lastTradePrice,
        decimal highSinceTrade,
        decimal sellThresholdPct,
        decimal buyThresholdPct,
        decimal escapeDrawdownPct,
        decimal escapeRecoveryPct,
        decimal minTradeEur)
    {
        if (portfolio.TotalValueEur <= 0)
            return TradeDecision.Hold("Portfolio value is zero");

        var btcValueEur = portfolio.BtcBalance * currentPrice;
        var holdingBtc = btcValueEur > portfolio.EurBalance;

        return holdingBtc
            ? EvaluateSell(currentPrice, portfolio, lastTradePrice, sellThresholdPct, minTradeEur)
            : EvaluateBuy(currentPrice, portfolio, lastTradePrice, highSinceTrade,
                buyThresholdPct, escapeDrawdownPct, escapeRecoveryPct, minTradeEur);
    }
```

- [ ] **Step 4: Replace `EvaluateSell` with profit-target-only**

In the same file, replace the entire `EvaluateSell` method with:

```csharp
    private static TradeDecision EvaluateSell(
        decimal currentPrice,
        Portfolio portfolio,
        decimal lastTradePrice,
        decimal sellThresholdPct,
        decimal minTradeEur)
    {
        var sellValueEur = portfolio.BtcBalance * currentPrice;

        TradeDecision SellAll(string reason) =>
            sellValueEur < minTradeEur
                ? TradeDecision.Hold($"Sell value €{sellValueEur:N2} below minimum €{minTradeEur:N2}")
                : TradeDecision.Sell(portfolio.BtcBalance, reason);

        // Only ever sell at a profit — never below the purchase price. No escape, no hard stop.
        var profitTarget = lastTradePrice * (1 + sellThresholdPct);
        if (currentPrice >= profitTarget)
            return SellAll($"Sell all BTC: price €{currentPrice:N2} >= profit target €{profitTarget:N2} (normal)");

        return TradeDecision.Hold($"Price €{currentPrice:N2} below profit target €{profitTarget:N2} (normal)");
    }
```

Leave `EvaluateBuy` exactly as-is.

- [ ] **Step 5: Update the `PacificStrategy` call to the calculator**

In `src/BinanceBot.Strategies/Pacific/PacificStrategy.cs`, replace the private `Evaluate` helper (the method that calls `PacificCalculator.Evaluate`):

```csharp
    private TradeDecision Evaluate(decimal price, Portfolio portfolio, decimal lastTradePrice,
        decimal lowSinceTrade, decimal highSinceTrade) =>
        PacificCalculator.Evaluate(
            price, portfolio, lastTradePrice, lowSinceTrade, highSinceTrade,
            _settings.SellThresholdPct, _settings.BuyThresholdPct,
            _settings.EscapeDrawdownPct, _settings.EscapeRecoveryPct, _settings.HardStopLossPct,
            _settings.MinTradeEur);
```

with (keep the `lowSinceTrade` parameter — it is still used by the caller's debug log — but stop forwarding it and drop `HardStopLossPct`):

```csharp
    private TradeDecision Evaluate(decimal price, Portfolio portfolio, decimal lastTradePrice,
        decimal lowSinceTrade, decimal highSinceTrade) =>
        PacificCalculator.Evaluate(
            price, portfolio, lastTradePrice, highSinceTrade,
            _settings.SellThresholdPct, _settings.BuyThresholdPct,
            _settings.EscapeDrawdownPct, _settings.EscapeRecoveryPct,
            _settings.MinTradeEur);
```

(`_settings.HardStopLossPct` still exists at this point — it is removed in Task 2. `lowSinceTrade` remains referenced by the `_logger.LogDebug(... low=€{Low:N2} ...)` call, so it is not dead.)

- [ ] **Step 6: Run tests + full build to verify pass**

Run: `dotnet test tests/BinanceBot.Strategies.Tests --filter PacificCalculatorTests`
Expected: PASS (10 tests).
Run: `dotnet build`
Expected: succeeds (PacificSettings.HardStopLossPct still exists, so Program.cs/PacificView still compile).

- [ ] **Step 7: Commit**

```bash
git add src/BinanceBot.Strategies/Pacific/PacificCalculator.cs src/BinanceBot.Strategies/Pacific/PacificStrategy.cs tests/BinanceBot.Strategies.Tests/PacificCalculatorTests.cs
git commit -m "Pacific: sell only at profit target, never below buy price"
```

---

### Task 2: Remove `HardStopLossPct` + simplify BTC-side dashboard projection

**Files:**
- Modify: `src/BinanceBot.Strategies/Pacific/PacificSettings.cs`
- Modify: `src/BinanceBot.Worker/appsettings.json`
- Modify: `src/BinanceBot.Worker/Services/PacificView.cs`
- Modify: `src/BinanceBot.Worker/Program.cs` (`/api/status`, lines ~141-173)
- Modify: `tests/BinanceBot.Worker.Tests/PacificViewTests.cs`
- Modify: `tests/BinanceBot.Strategies.Tests/PacificStrategyTests.cs` (settings setup, line ~23)

**Interfaces:**
- Consumes: nothing new.
- Produces: `PacificView.Compute(bool holdingBtc, decimal lastTradePrice, decimal currentPrice, decimal lowSinceTrade, decimal highSinceTrade, decimal sellThresholdPct, decimal buyThresholdPct, decimal escapeDrawdownPct, decimal escapeRecoveryPct) → PacificView?` where `PacificView.EscapeArmPrice` is now `decimal?` (null when holding BTC). `ModeHardStop` no longer exists. `/api/status` `pacific` object no longer has `hardStopLossPct`.

- [ ] **Step 1: Update the `PacificView` tests for the new BTC-side behavior + signature**

Replace the entire body of `tests/BinanceBot.Worker.Tests/PacificViewTests.cs` with:

```csharp
using BinanceBot.Worker.Services;
using FluentAssertions;

namespace BinanceBot.Worker.Tests;

public class PacificViewTests
{
    private static PacificView? Compute(bool holdingBtc, decimal lastTrade, decimal price,
        decimal low, decimal high) =>
        PacificView.Compute(holdingBtc, lastTrade, price, low, high,
            sellThresholdPct: 0.025m, buyThresholdPct: 0.025m,
            escapeDrawdownPct: 0.05m, escapeRecoveryPct: 0.025m);

    [Fact]
    public void NoLastTrade_ReturnsNull()
    {
        Compute(true, 0m, 60_000m, 60_000m, 60_000m).Should().BeNull();
    }

    [Fact]
    public void HoldingBtc_IsAlwaysNormal_NoEscapeMarkers()
    {
        var v = Compute(true, 60_000m, 59_000m, 59_000m, 60_000m)!;
        v.HoldingBtc.Should().BeTrue();
        v.Mode.Should().Be(PacificView.ModeNormal);
        v.ProfitTarget.Should().Be(61_500m);
        v.EscapeArmPrice.Should().BeNull();
        v.EscapeTarget.Should().BeNull();
        v.ActiveTarget.Should().Be(61_500m);
    }

    [Fact]
    public void HoldingBtc_DeepDip_StaysNormal_NoEscape()
    {
        // Even after a big drop the BTC side never arms an escape (never sells below buy).
        var v = Compute(true, 60_000m, 50_000m, 50_000m, 60_000m)!;
        v.Mode.Should().Be(PacificView.ModeNormal);
        v.EscapeArmPrice.Should().BeNull();
        v.EscapeTarget.Should().BeNull();
        v.ActiveTarget.Should().Be(61_500m);
    }

    [Fact]
    public void HoldingEur_SmallRunup_IsNormal_ProfitTargetActive()
    {
        // runup 1.7% < 5% → normal; profit target 60000*0.975 = 58500
        var v = Compute(false, 60_000m, 61_000m, 60_000m, 61_000m)!;
        v.HoldingBtc.Should().BeFalse();
        v.Mode.Should().Be(PacificView.ModeNormal);
        v.ProfitTarget.Should().Be(58_500m);
        v.EscapeArmPrice.Should().Be(63_000m); // 60000*(1+0.05)
        v.EscapeTarget.Should().BeNull();
        v.ActiveTarget.Should().Be(58_500m);
    }

    [Fact]
    public void HoldingEur_InEscapeZone_IsArmed_EscapeTargetActive()
    {
        // runup 20% >= 5% → escape target = high 72000 * 0.975 = 70200
        var v = Compute(false, 60_000m, 72_000m, 60_000m, 72_000m)!;
        v.Mode.Should().Be(PacificView.ModeEscapeArmed);
        v.EscapeTarget.Should().Be(70_200m);
        v.ActiveTarget.Should().Be(70_200m);
    }

    [Fact]
    public void HoldingEur_PeakArmedThenRetraced_StaysArmed()
    {
        // high 63600 (+6%, past +5% arm) but current 62000 (instantaneous run-up 3.33% < 5%).
        // Latched by the high → still escape-armed; escape target = 63600 * 0.975 = 62010.
        var v = Compute(false, 60_000m, 62_000m, 60_000m, 63_600m)!;
        v.Mode.Should().Be(PacificView.ModeEscapeArmed);
        v.EscapeTarget.Should().Be(62_010m);
    }

    [Fact]
    public void MoveFromLastTradePct_IsSigned()
    {
        Compute(false, 60_000m, 63_000m, 60_000m, 63_000m)!.MoveFromLastTradePct.Should().Be(0.05m);
        Compute(true, 60_000m, 57_000m, 57_000m, 60_000m)!.MoveFromLastTradePct.Should().Be(-0.05m);
    }
}
```

- [ ] **Step 2: Run to verify fail**

Run: `dotnet test tests/BinanceBot.Worker.Tests --filter PacificViewTests`
Expected: FAIL to build — `PacificView.Compute` still takes `hardStopLossPct` and `EscapeArmPrice` is non-nullable.

- [ ] **Step 3: Rewrite `PacificView`**

Replace the entire contents of `src/BinanceBot.Worker/Services/PacificView.cs` with:

```csharp
namespace BinanceBot.Worker.Services;

/// <summary>
/// Read-only projection of the Pacific strategy's decision state for the dashboard.
/// Mirrors PacificCalculator: the sell side (holding BTC) only ever sells at the profit
/// target — never below buy — so it has no escape and is always Normal. The buy side keeps
/// the latched trailing escape. Display-only: it never drives trades.
/// </summary>
public sealed record PacificView(
    bool HoldingBtc,
    string Mode,
    decimal LastTradePrice,
    decimal ProfitTarget,
    decimal? EscapeArmPrice,
    decimal? EscapeTarget,
    decimal ActiveTarget,
    decimal LowSinceTrade,
    decimal HighSinceTrade,
    decimal MoveFromLastTradePct)
{
    public const string ModeNormal = "normal";
    public const string ModeEscapeArmed = "escape-armed";

    public static PacificView? Compute(
        bool holdingBtc,
        decimal lastTradePrice,
        decimal currentPrice,
        decimal lowSinceTrade,
        decimal highSinceTrade,
        decimal sellThresholdPct,
        decimal buyThresholdPct,
        decimal escapeDrawdownPct,
        decimal escapeRecoveryPct)
    {
        if (lastTradePrice <= 0) return null;

        var movePct = (currentPrice - lastTradePrice) / lastTradePrice;

        if (holdingBtc)
        {
            // Sell side: profit target only, never below buy → always Normal, no escape.
            var profitTarget = lastTradePrice * (1 + sellThresholdPct);
            return new PacificView(true, ModeNormal, lastTradePrice, profitTarget,
                null, null, profitTarget,
                lowSinceTrade, highSinceTrade, movePct);
        }
        else
        {
            var profitTarget = lastTradePrice * (1 - buyThresholdPct);
            var escapeArmPrice = lastTradePrice * (1 + escapeDrawdownPct);
            var maxRunup = (highSinceTrade - lastTradePrice) / lastTradePrice;   // latched, arms the escape

            string mode;
            decimal? escapeTarget = null;
            if (maxRunup >= escapeDrawdownPct)
            {
                mode = ModeEscapeArmed;
                escapeTarget = highSinceTrade * (1 - escapeRecoveryPct);
            }
            else
            {
                mode = ModeNormal;
            }

            return new PacificView(false, mode, lastTradePrice, profitTarget,
                escapeArmPrice, escapeTarget, escapeTarget ?? profitTarget,
                lowSinceTrade, highSinceTrade, movePct);
        }
    }
}
```

- [ ] **Step 4: Update `/api/status` in `Program.cs`**

In `src/BinanceBot.Worker/Program.cs`, remove the `hardStopLossPct` argument from the `PacificView.Compute` call. Replace:

```csharp
                escapeDrawdownPct: pacificSettings.EscapeDrawdownPct,
                escapeRecoveryPct: pacificSettings.EscapeRecoveryPct,
                hardStopLossPct: pacificSettings.HardStopLossPct);
```

with:

```csharp
                escapeDrawdownPct: pacificSettings.EscapeDrawdownPct,
                escapeRecoveryPct: pacificSettings.EscapeRecoveryPct);
```

Then remove the `hardStopLossPct` field from the emitted `pacific` object. Replace:

```csharp
                    escapeDrawdownPct = pacificSettings.EscapeDrawdownPct,
                    escapeRecoveryPct = pacificSettings.EscapeRecoveryPct,
                    hardStopLossPct = pacificSettings.HardStopLossPct,
                };
```

with:

```csharp
                    escapeDrawdownPct = pacificSettings.EscapeDrawdownPct,
                    escapeRecoveryPct = pacificSettings.EscapeRecoveryPct,
                };
```

- [ ] **Step 5: Remove `HardStopLossPct` from settings and config**

In `src/BinanceBot.Strategies/Pacific/PacificSettings.cs`, delete the line:

```csharp
    public decimal HardStopLossPct { get; set; } = 0m; // 0 = disabled (BTC side only)
```

In `src/BinanceBot.Worker/appsettings.json`, delete the `"HardStopLossPct": 0` line (and ensure the preceding line's trailing comma/JSON validity is preserved — `"EscapeRecoveryPct": 0.025` becomes the last key in the `Strategy:Pacific` block):

```json
    "EscapeDrawdownPct": 0.04,
    "EscapeRecoveryPct": 0.025
  }
```

In `tests/BinanceBot.Strategies.Tests/PacificStrategyTests.cs`, remove the `HardStopLossPct = 0m` line from the `PacificSettings` initializer (around line 23), leaving `EscapeRecoveryPct = 0.025m` as the last initializer.

- [ ] **Step 6: Run full test suite + build**

Run: `dotnet build`
Expected: succeeds (no remaining `HardStopLossPct` or `ModeHardStop` references).
Run: `dotnet test -c Release`
Expected: all pass.

- [ ] **Step 7: Commit**

```bash
git add src/BinanceBot.Strategies/Pacific/PacificSettings.cs src/BinanceBot.Worker/appsettings.json src/BinanceBot.Worker/Services/PacificView.cs src/BinanceBot.Worker/Program.cs tests/BinanceBot.Worker.Tests/PacificViewTests.cs tests/BinanceBot.Strategies.Tests/PacificStrategyTests.cs
git commit -m "Remove HardStopLossPct; holding-BTC dashboard is always Normal (no escape)"
```

---

### Task 3: Dashboard card — drop escape markers/copy when holding BTC

**Files:**
- Modify: `src/BinanceBot.Worker/wwwroot/index.html`

**Interfaces:**
- Consumes: `status.pacific` where `escapeArmPrice` is now null when holding BTC, and the holding-BTC mode is always `normal`.
- Produces: (frontend only).

- [ ] **Step 1: Only render the "Escape arm" marker when present**

In `src/BinanceBot.Worker/wwwroot/index.html`, in `renderLadder`, replace the marker-array construction:

```javascript
      const markers = [
        { label: 'Last', price: p.lastTradePrice, cls: 'last' },
        { label: p.holdingBtc ? 'Sell target' : 'Buy target', price: p.profitTarget, cls: 'profit' },
        { label: 'Escape arm', price: p.escapeArmPrice, cls: 'escape-arm' },
        { label: 'Now', price: currentPrice, cls: 'current' },
      ];
      if (p.escapeTarget != null)
        markers.push({ label: 'Escape target', price: p.escapeTarget, cls: 'escape-target' });
```

with:

```javascript
      const markers = [
        { label: 'Last', price: p.lastTradePrice, cls: 'last' },
        { label: p.holdingBtc ? 'Sell target' : 'Buy target', price: p.profitTarget, cls: 'profit' },
        { label: 'Now', price: currentPrice, cls: 'current' },
      ];
      if (p.escapeArmPrice != null)
        markers.push({ label: 'Escape arm', price: p.escapeArmPrice, cls: 'escape-arm' });
      if (p.escapeTarget != null)
        markers.push({ label: 'Escape target', price: p.escapeTarget, cls: 'escape-target' });
```

- [ ] **Step 2: Simplify the holding-BTC explanation**

In the same file, replace the `holdingBtc` branch of `explainPacific`:

```javascript
      if (p.holdingBtc) {
        if (p.mode === 'hard-stop')
          return 'Hard-stop zone. Drawdown ' + pctStr(-p.moveFromLastTradePct)
            + ' ≥ ' + pctStr(p.hardStopLossPct) + '; sells at market.';
        if (p.mode === 'escape-armed')
          return 'Escape armed. Tracking low ' + eur(p.lowSinceTrade)
            + '; sells at ' + eur(p.escapeTarget) + ' (+' + rec + ' from low).';
        return 'Holding BTC. Sells at ' + eur(p.profitTarget) + ' (+' + pctStr(p.sellThresholdPct)
          + '), or if price falls to ' + eur(p.escapeArmPrice) + ' (−' + pctStr(p.escapeDrawdownPct)
          + ') sells on a ' + rec + ' bounce from the low.';
      } else {
```

with:

```javascript
      if (p.holdingBtc) {
        return 'Holding BTC. Sells at ' + eur(p.profitTarget) + ' (+' + pctStr(p.sellThresholdPct)
          + '). Never sells below the buy price.';
      } else {
```

(The `const rec = pctStr(p.escapeRecoveryPct);` line at the top of `explainPacific` is still used by the EUR branch — leave it.)

- [ ] **Step 3: Build + verify the dashboard logic under Node**

Run: `dotnet build`
Expected: succeeds.

Create `verify-btc.mjs` in the scratchpad with a DOM mock and the `explainPacific`/`renderLadder` functions copied from the edited `index.html`, then assert the holding-BTC case:

```javascript
// holdingBtc payload with escapeArmPrice null (as the backend now emits)
const btc = { mode:'normal', holdingBtc:true, lastTradePrice:54289.51, profitTarget:55646.75,
  escapeArmPrice:null, escapeTarget:null, activeTarget:55646.75, lowSinceTrade:52000,
  highSinceTrade:54289.51, moveFromLastTradePct:-0.0025,
  sellThresholdPct:0.025, buyThresholdPct:0.025, escapeDrawdownPct:0.04, escapeRecoveryPct:0.025 };
console.log('explain:', explainPacific(btc));                       // expect: no "Escape arm", no "escape"
console.log('ladder cls:', renderLadder(btc, 54151).map(m=>m.cls)); // expect: last, profit, current (NO escape-arm)
```

Run: `node verify-btc.mjs`
Expected: the explanation contains "Never sells below the buy price" and no "Escape"; the ladder marker classes are `last, profit, current` with no `escape-arm`.

- [ ] **Step 4: Commit**

```bash
git add src/BinanceBot.Worker/wwwroot/index.html
git commit -m "Dashboard: hide escape markers/copy when holding BTC"
```

---

## Self-Review

**Spec coverage:**
- Sell side → profit-target-only, invariant → Task 1 (rewrite + invariant test). ✅
- Drop `lowSinceTrade`/`hardStopLossPct` from calculator; update strategy call → Task 1. ✅
- Remove `HardStopLossPct` setting + appsettings + strategy-test setup → Task 2. ✅
- `PacificView`: BTC always Normal, `EscapeArmPrice` nullable, no `ModeHardStop` → Task 2. ✅
- `/api/status` drops `hardStopLossPct` field, nullable `escapeArmPrice` → Task 2. ✅
- Buy side unchanged → Task 1 (kept `EvaluateBuy`), Task 2 (EUR branch unchanged). ✅
- Dashboard: hide Escape arm marker when null, simplify BTC copy → Task 3. ✅
- `PositionExtremes` unchanged; `lowSinceTrade` still tracked → not modified anywhere. ✅

**Placeholder scan:** No TBD/TODO; every code step has complete code. ✅

**Type consistency:** `PacificCalculator.Evaluate` 9-arg signature is identical in Task 1's test helper, the implementation, and the `PacificStrategy` call. `PacificView.Compute` 9-arg signature matches across Task 2's test helper, implementation, and the `Program.cs` call. `EscapeArmPrice` is `decimal?` in the record (Task 2) and the JS guards `escapeArmPrice != null` (Task 3). `ModeHardStop` is removed in Task 2 and no test or JS references it afterward. ✅
