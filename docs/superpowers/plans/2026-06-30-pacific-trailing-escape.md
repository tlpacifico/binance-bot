# Pacific v2 — Trailing-Escape Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the broken stale-24h escape in the Pacific strategy with a profit-locking ping-pong plus a trailing-recovery escape anchored to the local price extreme since the last trade.

**Architecture:** `PacificCalculator` (pure logic) gains a trailing-escape path that activates after a configurable drawdown/run-up and triggers on a recovery bounce from the local extreme. The engine (`TradingEngineService`) tracks the low/high-since-last-trade in the existing `StrategyStateJson` column (no schema migration) and feeds them to the strategy via `StrategyContext`. The dashboard mirrors the new target-price logic.

**Tech Stack:** .NET 9, xUnit + FluentAssertions + NSubstitute, EF Core (PostgreSQL/Npgsql), System.Text.Json.

## Global Constraints

- Target framework: .NET 9.0. Decimal money math throughout (no `double`/`float`).
- Strategies are pure decision-makers: they never execute trades and never persist state. The engine owns persistence.
- No EF Core migration in this plan. The `Last24hLowPrice`/`Last24hHighPrice`/`Last24hPriceTimestamp` columns on `BotStateEntity` stay in the schema as deprecated/orphaned (no longer read or written).
- Build with `dotnet build -c Release`; test with `dotnet test -c Release`. Run from repo root `C:\Repos\Thacio\binance-bot`.
- Code identifiers/types in English; commit messages may be in Portuguese or English.
- Commit message trailer on every commit: `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`
- Work happens on branch `feature/pacific-trailing-escape` (already created).

**Default parameter values:** `SellThresholdPct=0.025`, `BuyThresholdPct=0.025`, `EscapeDrawdownPct=0.05`, `EscapeRecoveryPct=0.025`, `HardStopLossPct=0` (disabled), `ConfirmationTicks=10`, `CheckIntervalSeconds=30`, `MinTradeEur=10`.

---

### Task 1: Additive scaffolding — new settings & context fields

Add the new configuration knobs and the context fields the later tasks consume. Purely additive (old `StaleTradeDays`/`Last24h*` fields stay for now), so the whole solution still compiles and all existing tests pass.

**Files:**
- Modify: `src/BinanceBot.Strategies/Pacific/PacificSettings.cs`
- Modify: `src/BinanceBot.Core/Models/StrategyContext.cs`
- Modify: `src/BinanceBot.Worker/appsettings.json`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `PacificSettings.EscapeDrawdownPct` (`decimal`, default `0.05`), `PacificSettings.EscapeRecoveryPct` (`decimal`, default `0.025`), `PacificSettings.HardStopLossPct` (`decimal`, default `0m`).
  - `StrategyContext.LowSinceTrade` (`decimal?`), `StrategyContext.HighSinceTrade` (`decimal?`).

- [ ] **Step 1: Add escape fields to `PacificSettings`**

Edit `src/BinanceBot.Strategies/Pacific/PacificSettings.cs` to this full content (keeps `StaleTradeDays` for now):

```csharp
namespace BinanceBot.Strategies.Pacific;

public sealed class PacificSettings
{
    public const string Section = "Strategy:Pacific";

    public decimal SellThresholdPct { get; set; } = 0.025m;
    public decimal BuyThresholdPct { get; set; } = 0.025m;
    public int ConfirmationTicks { get; set; } = 10;
    public int StaleTradeDays { get; set; } = 2; // deprecated — removed in Task 2
    public int CheckIntervalSeconds { get; set; } = 30;
    public decimal MinTradeEur { get; set; } = 10m;

    // Trailing-escape (Pacific v2)
    public decimal EscapeDrawdownPct { get; set; } = 0.05m;
    public decimal EscapeRecoveryPct { get; set; } = 0.025m;
    public decimal HardStopLossPct { get; set; } = 0m; // 0 = disabled (BTC side only)
}
```

- [ ] **Step 2: Add extreme fields to `StrategyContext`**

Edit `src/BinanceBot.Core/Models/StrategyContext.cs` — add two fields (keep `Last24h*` for now):

```csharp
namespace BinanceBot.Core.Models;

public sealed record StrategyContext
{
    public required PriceData CurrentPrice { get; init; }
    public required Portfolio Portfolio { get; init; }
    public IReadOnlyList<TradeRecord> RecentTrades { get; init; } = [];
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public DateTime? LastRebalanceTimestamp { get; init; }
    public decimal? LastTradePrice { get; init; }
    public decimal? Last24hLowPrice { get; init; }  // deprecated — removed in Task 5
    public decimal? Last24hHighPrice { get; init; } // deprecated — removed in Task 5
    public decimal? LowSinceTrade { get; init; }
    public decimal? HighSinceTrade { get; init; }
}
```

- [ ] **Step 3: Add escape keys to `appsettings.json`**

In `src/BinanceBot.Worker/appsettings.json`, replace the `"Strategy:Pacific"` block with (note: `StaleTradeDays` still present, removed in Task 2):

```json
  "Strategy:Pacific": {
    "SellThresholdPct": 0.025,
    "BuyThresholdPct": 0.025,
    "ConfirmationTicks": 10,
    "StaleTradeDays": 2,
    "CheckIntervalSeconds": 30,
    "MinTradeEur": 10,
    "EscapeDrawdownPct": 0.05,
    "EscapeRecoveryPct": 0.025,
    "HardStopLossPct": 0
  }
```

- [ ] **Step 4: Build to verify it compiles**

Run: `dotnet build -c Release`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Run full test suite (no behavior change yet)**

Run: `dotnet test -c Release`
Expected: PASS — same tests green as before.

- [ ] **Step 6: Commit**

```bash
git add src/BinanceBot.Strategies/Pacific/PacificSettings.cs src/BinanceBot.Core/Models/StrategyContext.cs src/BinanceBot.Worker/appsettings.json
git commit -m "Add Pacific trailing-escape settings and context extreme fields

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: New decision logic — `PacificCalculator` + `PacificStrategy`

Rewrite the calculator with the profit→hard-stop→trailing-escape ladder (symmetric for buy), rewire the strategy to the new signature and context fields, and remove the dead `StaleTradeDays`. This is the cohesive "new decision logic" unit; both Strategies test files are rewritten with it.

**Files:**
- Modify: `src/BinanceBot.Strategies/Pacific/PacificCalculator.cs` (full rewrite)
- Modify: `src/BinanceBot.Strategies/Pacific/PacificStrategy.cs`
- Modify: `src/BinanceBot.Strategies/Pacific/PacificSettings.cs` (remove `StaleTradeDays`)
- Modify: `src/BinanceBot.Worker/appsettings.json` (remove `StaleTradeDays` key)
- Test: `tests/BinanceBot.Strategies.Tests/PacificCalculatorTests.cs` (full rewrite)
- Test: `tests/BinanceBot.Strategies.Tests/PacificStrategyTests.cs`

**Interfaces:**
- Consumes: `PacificSettings.EscapeDrawdownPct/EscapeRecoveryPct/HardStopLossPct`, `StrategyContext.LowSinceTrade/HighSinceTrade` (Task 1).
- Produces:
  - `PacificCalculator.Evaluate(decimal currentPrice, Portfolio portfolio, decimal lastTradePrice, decimal lowSinceTrade, decimal highSinceTrade, decimal sellThresholdPct, decimal buyThresholdPct, decimal escapeDrawdownPct, decimal escapeRecoveryPct, decimal hardStopLossPct, decimal minTradeEur) → TradeDecision`.
  - Reason strings contain a mode tag: `(normal)`, `(hard-stop)`, or `(trailing-escape)`.

- [ ] **Step 1: Rewrite `PacificCalculatorTests` (failing tests first)**

Replace the entire content of `tests/BinanceBot.Strategies.Tests/PacificCalculatorTests.cs`:

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
        decimal price, Portfolio portfolio, decimal lastTrade,
        decimal lowSinceTrade, decimal highSinceTrade, decimal hardStop = 0m) =>
        PacificCalculator.Evaluate(
            price, portfolio, lastTrade, lowSinceTrade, highSinceTrade,
            Sell, Buy, EscapeDrawdown, EscapeRecovery, hardStop, MinTrade);

    private static Portfolio Btc(decimal price) =>
        new() { BtcBalance = 0.01m, EurBalance = 5m, CurrentBtcPrice = price };

    private static Portfolio Eur(decimal price) =>
        new() { BtcBalance = 0m, EurBalance = 500m, CurrentBtcPrice = price };

    // ---- Normal profit mode ----

    [Fact]
    public void HoldingBtc_PriceAtProfitTarget_ShouldSellNormal()
    {
        // target = 60000 * 1.025 = 61500
        var result = Eval(61_500m, Btc(61_500m), 60_000m, 60_000m, 61_500m);
        result.Action.Should().Be(TradeAction.Sell);
        result.Reason.Should().Contain("normal");
    }

    [Fact]
    public void HoldingBtc_BelowProfit_SmallDrawdown_ShouldHold()
    {
        // price 59000, lastTrade 60000 → drawdown 1.7% < 5% escape, below profit → Hold
        var result = Eval(59_000m, Btc(59_000m), 60_000m, 59_000m, 60_000m);
        result.Action.Should().Be(TradeAction.Hold);
        result.Reason.Should().Contain("normal");
    }

    [Fact]
    public void HoldingEur_PriceAtProfitTarget_ShouldBuyNormal()
    {
        // target = 60000 * 0.975 = 58500
        var result = Eval(58_500m, Eur(58_500m), 60_000m, 58_500m, 60_000m);
        result.Action.Should().Be(TradeAction.Buy);
        result.Reason.Should().Contain("normal");
    }

    // ---- Trailing escape (BTC side) ----

    [Fact]
    public void HoldingBtc_DrawdownReached_PriceRecoveredFromLow_ShouldEscapeSell()
    {
        // lastTrade 60000, price 51250, drawdown = 14.6% >= 5%
        // lowSinceTrade 50000 → escape target = 50000 * 1.025 = 51250 → price >= target → Sell
        var result = Eval(51_250m, Btc(51_250m), 60_000m, 50_000m, 60_000m);
        result.Action.Should().Be(TradeAction.Sell);
        result.Reason.Should().Contain("trailing-escape");
    }

    [Fact]
    public void HoldingBtc_DrawdownReached_NoBounceYet_ShouldHold()
    {
        // lowSinceTrade 50000 → escape target 51250; price 50500 < target → Hold (armed)
        var result = Eval(50_500m, Btc(50_500m), 60_000m, 50_000m, 60_000m);
        result.Action.Should().Be(TradeAction.Hold);
        result.Reason.Should().Contain("trailing-escape");
    }

    // ---- Hard stop-loss ----

    [Fact]
    public void HoldingBtc_HardStopDisabled_DeepLoss_NoForcedSell()
    {
        // drawdown 30%, hardStop=0 (disabled); lowSinceTrade=price so no bounce → Hold
        var result = Eval(42_000m, Btc(42_000m), 60_000m, 42_000m, 60_000m, hardStop: 0m);
        result.Action.Should().Be(TradeAction.Hold);
    }

    [Fact]
    public void HoldingBtc_HardStopEnabled_DrawdownExceeds_ShouldForceSell()
    {
        // drawdown 30% >= hardStop 20%; lowSinceTrade=price so no trailing bounce → hard-stop fires
        var result = Eval(42_000m, Btc(42_000m), 60_000m, 42_000m, 60_000m, hardStop: 0.20m);
        result.Action.Should().Be(TradeAction.Sell);
        result.Reason.Should().Contain("hard-stop");
    }

    // ---- Trailing escape (EUR side, symmetric) ----

    [Fact]
    public void HoldingEur_RunupReached_PriceFellFromHigh_ShouldEscapeBuy()
    {
        // lastTrade 60000 (last sell), price 70200, runup = 17% >= 5%
        // highSinceTrade 72000 → escape target = 72000 * 0.975 = 70200 → price <= target → Buy
        var result = Eval(70_200m, Eur(70_200m), 60_000m, 60_000m, 72_000m);
        result.Action.Should().Be(TradeAction.Buy);
        result.Reason.Should().Contain("trailing-escape");
    }

    [Fact]
    public void HoldingEur_RunupReached_NoPullbackYet_ShouldHold()
    {
        // highSinceTrade 72000 → escape target 70200; price 71000 > target → Hold (armed)
        var result = Eval(71_000m, Eur(71_000m), 60_000m, 60_000m, 72_000m);
        result.Action.Should().Be(TradeAction.Hold);
        result.Reason.Should().Contain("trailing-escape");
    }

    // ---- Guards ----

    [Fact]
    public void EmptyPortfolio_ShouldHold()
    {
        var portfolio = new Portfolio { BtcBalance = 0m, EurBalance = 0m, CurrentBtcPrice = 60_000m };
        var result = Eval(60_000m, portfolio, 60_000m, 60_000m, 60_000m);
        result.Action.Should().Be(TradeAction.Hold);
        result.Reason.Should().Contain("zero");
    }

    [Fact]
    public void HoldingEur_BelowMinTrade_ShouldHold()
    {
        var portfolio = new Portfolio { BtcBalance = 0m, EurBalance = 5m, CurrentBtcPrice = 58_000m };
        // at profit target but EUR balance €5 < €10 min
        var result = PacificCalculator.Evaluate(58_500m, portfolio, 60_000m, 58_500m, 60_000m,
            Sell, Buy, EscapeDrawdown, EscapeRecovery, 0m, MinTrade);
        result.Action.Should().Be(TradeAction.Hold);
        result.Reason.Should().Contain("minimum");
    }
}
```

- [ ] **Step 2: Run the new tests to verify they fail to compile/fail**

Run: `dotnet test -c Release --filter "FullyQualifiedName~PacificCalculatorTests"`
Expected: FAIL — compile error (old `Evaluate` signature) or assertion failures.

- [ ] **Step 3: Rewrite `PacificCalculator`**

Replace the entire content of `src/BinanceBot.Strategies/Pacific/PacificCalculator.cs`:

```csharp
using BinanceBot.Core.Models;

namespace BinanceBot.Strategies.Pacific;

public static class PacificCalculator
{
    public static TradeDecision Evaluate(
        decimal currentPrice,
        Portfolio portfolio,
        decimal lastTradePrice,
        decimal lowSinceTrade,
        decimal highSinceTrade,
        decimal sellThresholdPct,
        decimal buyThresholdPct,
        decimal escapeDrawdownPct,
        decimal escapeRecoveryPct,
        decimal hardStopLossPct,
        decimal minTradeEur)
    {
        if (portfolio.TotalValueEur <= 0)
            return TradeDecision.Hold("Portfolio value is zero");

        var btcValueEur = portfolio.BtcBalance * currentPrice;
        var holdingBtc = btcValueEur > portfolio.EurBalance;

        return holdingBtc
            ? EvaluateSell(currentPrice, portfolio, lastTradePrice, lowSinceTrade,
                sellThresholdPct, escapeDrawdownPct, escapeRecoveryPct, hardStopLossPct, minTradeEur)
            : EvaluateBuy(currentPrice, portfolio, lastTradePrice, highSinceTrade,
                buyThresholdPct, escapeDrawdownPct, escapeRecoveryPct, minTradeEur);
    }

    private static TradeDecision EvaluateSell(
        decimal currentPrice,
        Portfolio portfolio,
        decimal lastTradePrice,
        decimal lowSinceTrade,
        decimal sellThresholdPct,
        decimal escapeDrawdownPct,
        decimal escapeRecoveryPct,
        decimal hardStopLossPct,
        decimal minTradeEur)
    {
        var sellValueEur = portfolio.BtcBalance * currentPrice;

        TradeDecision SellAll(string reason) =>
            sellValueEur < minTradeEur
                ? TradeDecision.Hold($"Sell value €{sellValueEur:N2} below minimum €{minTradeEur:N2}")
                : TradeDecision.Sell(portfolio.BtcBalance, reason);

        // 1. Profit target (preferred)
        var profitTarget = lastTradePrice * (1 + sellThresholdPct);
        if (currentPrice >= profitTarget)
            return SellAll($"Sell all BTC: price €{currentPrice:N2} >= profit target €{profitTarget:N2} (normal)");

        var drawdown = lastTradePrice > 0 ? (lastTradePrice - currentPrice) / lastTradePrice : 0m;

        // 2. Hard stop-loss (if enabled)
        if (hardStopLossPct > 0 && drawdown >= hardStopLossPct)
            return SellAll($"Sell all BTC: drawdown {drawdown:P1} >= hard stop {hardStopLossPct:P1} (hard-stop)");

        // 3. Trailing escape
        if (drawdown >= escapeDrawdownPct)
        {
            var escapeTarget = lowSinceTrade * (1 + escapeRecoveryPct);
            return currentPrice >= escapeTarget
                ? SellAll($"Sell all BTC: price €{currentPrice:N2} >= escape target €{escapeTarget:N2} (trailing-escape)")
                : TradeDecision.Hold($"Escape armed: price €{currentPrice:N2} below escape target €{escapeTarget:N2} (trailing-escape)");
        }

        return TradeDecision.Hold($"Price €{currentPrice:N2} below profit target €{profitTarget:N2} (normal)");
    }

    private static TradeDecision EvaluateBuy(
        decimal currentPrice,
        Portfolio portfolio,
        decimal lastTradePrice,
        decimal highSinceTrade,
        decimal buyThresholdPct,
        decimal escapeDrawdownPct,
        decimal escapeRecoveryPct,
        decimal minTradeEur)
    {
        TradeDecision BuyAll(string reason) =>
            portfolio.EurBalance < minTradeEur
                ? TradeDecision.Hold($"EUR balance €{portfolio.EurBalance:N2} below minimum €{minTradeEur:N2}")
                : TradeDecision.Buy(portfolio.EurBalance, reason);

        // 1. Profit target (preferred)
        var profitTarget = lastTradePrice * (1 - buyThresholdPct);
        if (currentPrice <= profitTarget)
            return BuyAll($"Buy all EUR: price €{currentPrice:N2} <= profit target €{profitTarget:N2} (normal)");

        var runup = lastTradePrice > 0 ? (currentPrice - lastTradePrice) / lastTradePrice : 0m;

        // 2. Trailing escape (no hard stop on EUR side — run-up is opportunity cost, not loss)
        if (runup >= escapeDrawdownPct)
        {
            var escapeTarget = highSinceTrade * (1 - escapeRecoveryPct);
            return currentPrice <= escapeTarget
                ? BuyAll($"Buy all EUR: price €{currentPrice:N2} <= escape target €{escapeTarget:N2} (trailing-escape)")
                : TradeDecision.Hold($"Escape armed: price €{currentPrice:N2} above escape target €{escapeTarget:N2} (trailing-escape)");
        }

        return TradeDecision.Hold($"Price €{currentPrice:N2} above profit target €{profitTarget:N2} (normal)");
    }
}
```

- [ ] **Step 4: Run calculator tests to verify they pass**

Run: `dotnet test -c Release --filter "FullyQualifiedName~PacificCalculatorTests"`
Expected: PASS (all). Note: the project won't fully build yet because `PacificStrategy` still calls the old signature — if the test run fails to build, that's expected; proceed to Step 5 and re-run in Step 8.

- [ ] **Step 5: Rewire `PacificStrategy` to the new signature**

Replace the entire content of `src/BinanceBot.Strategies/Pacific/PacificStrategy.cs`:

```csharp
using BinanceBot.Core.Enums;
using BinanceBot.Core.Interfaces;
using BinanceBot.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BinanceBot.Strategies.Pacific;

public sealed class PacificStrategy : ITradingStrategy
{
    private readonly PacificSettings _settings;
    private readonly ILogger<PacificStrategy> _logger;
    private readonly List<decimal> _confirmationPrices = [];
    private readonly object _lock = new();

    public string Name => "pacific";
    public string Description => "Threshold-based all-in buy/sell with trailing-escape recovery and confirmation ticks";
    public TimeSpan EvaluationInterval => TimeSpan.FromSeconds(_settings.CheckIntervalSeconds);

    public PacificStrategy(IOptions<PacificSettings> settings, ILogger<PacificStrategy> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public Task<TradeDecision> EvaluateAsync(StrategyContext context, CancellationToken ct)
    {
        var price = context.CurrentPrice.Last;
        var lastTradePrice = GetLastTradePrice(context);
        var lowSinceTrade = context.LowSinceTrade ?? price;
        var highSinceTrade = context.HighSinceTrade ?? price;

        var decision = Evaluate(price, context.Portfolio, lastTradePrice, lowSinceTrade, highSinceTrade);

        _logger.LogDebug(
            "Pacific eval: price=€{Price:N2}, lastTrade=€{LastTrade:N2}, low=€{Low:N2}, high=€{High:N2}, decision={Action}",
            price, lastTradePrice, lowSinceTrade, highSinceTrade, decision.Action);

        if (decision.Action == TradeAction.Hold)
        {
            lock (_lock)
            {
                if (_confirmationPrices.Count > 0)
                {
                    _logger.LogDebug("Price back inside threshold, resetting {Ticks} confirmation ticks",
                        _confirmationPrices.Count);
                    _confirmationPrices.Clear();
                }
            }
            return Task.FromResult(decision);
        }

        // Threshold crossed — handle confirmation
        if (_settings.ConfirmationTicks <= 0)
            return Task.FromResult(decision);

        lock (_lock)
        {
            _confirmationPrices.Add(price);

            _logger.LogDebug("Confirmation tick {Count}/{Required}",
                _confirmationPrices.Count, _settings.ConfirmationTicks);

            if (_confirmationPrices.Count < _settings.ConfirmationTicks)
                return Task.FromResult(TradeDecision.Hold(
                    $"Confirmation {_confirmationPrices.Count}/{_settings.ConfirmationTicks} — waiting for more ticks"));

            // Enough ticks — verify average price also crosses threshold
            var avgPrice = _confirmationPrices.Average();
            _confirmationPrices.Clear();

            var avgDecision = Evaluate(avgPrice, context.Portfolio, lastTradePrice, lowSinceTrade, highSinceTrade);

            if (avgDecision.Action != TradeAction.Hold)
            {
                _logger.LogInformation(
                    "Confirmation complete: avg €{AvgPrice:N2} beyond threshold — executing {Action}",
                    avgPrice, avgDecision.Action);
                return Task.FromResult(avgDecision);
            }

            _logger.LogInformation(
                "Confirmation complete but avg €{AvgPrice:N2} NOT beyond threshold — resetting",
                avgPrice);
            return Task.FromResult(TradeDecision.Hold(
                $"Confirmation avg €{avgPrice:N2} did not pass threshold — reset"));
        }
    }

    private TradeDecision Evaluate(decimal price, Portfolio portfolio, decimal lastTradePrice,
        decimal lowSinceTrade, decimal highSinceTrade) =>
        PacificCalculator.Evaluate(
            price, portfolio, lastTradePrice, lowSinceTrade, highSinceTrade,
            _settings.SellThresholdPct, _settings.BuyThresholdPct,
            _settings.EscapeDrawdownPct, _settings.EscapeRecoveryPct, _settings.HardStopLossPct,
            _settings.MinTradeEur);

    private static decimal GetLastTradePrice(StrategyContext context)
    {
        if (context.LastTradePrice is > 0)
            return context.LastTradePrice.Value;
        if (context.RecentTrades.Count > 0)
            return context.RecentTrades[0].Price;
        return context.CurrentPrice.Last;
    }
}
```

- [ ] **Step 6: Remove `StaleTradeDays` from `PacificSettings`**

Edit `src/BinanceBot.Strategies/Pacific/PacificSettings.cs` and delete the line:

```csharp
    public int StaleTradeDays { get; set; } = 2; // deprecated — removed in Task 2
```

- [ ] **Step 7: Remove `StaleTradeDays` key from `appsettings.json`**

In `src/BinanceBot.Worker/appsettings.json`, delete the `"StaleTradeDays": 2,` line from the `"Strategy:Pacific"` block.

- [ ] **Step 8: Update `PacificStrategyTests`**

In `tests/BinanceBot.Strategies.Tests/PacificStrategyTests.cs`, change the `CreateStrategy` helper to drop `StaleTradeDays` and add escape settings, and fix the `Description` assertion. Replace the `CreateStrategy` method and the `Properties_ShouldBeCorrect` test:

```csharp
    private PacificStrategy CreateStrategy(int confirmationTicks = 0)
    {
        var settings = Options.Create(new PacificSettings
        {
            SellThresholdPct = 0.025m,
            BuyThresholdPct = 0.025m,
            ConfirmationTicks = confirmationTicks,
            CheckIntervalSeconds = 30,
            MinTradeEur = 10m,
            EscapeDrawdownPct = 0.05m,
            EscapeRecoveryPct = 0.025m,
            HardStopLossPct = 0m
        });
        return new PacificStrategy(settings, NullLogger<PacificStrategy>.Instance);
    }

    [Fact]
    public void Properties_ShouldBeCorrect()
    {
        var strategy = CreateStrategy();
        strategy.Name.Should().Be("pacific");
        strategy.EvaluationInterval.Should().Be(TimeSpan.FromSeconds(30));
        strategy.Description.Should().Contain("trailing-escape");
    }
```

> Note: existing confirmation-tick tests use `CreateSellContext`, which leaves `LowSinceTrade`/`HighSinceTrade` null — the strategy defaults them to the current price, so normal-mode confirmation behavior is unchanged. No other edits to this file are needed.

- [ ] **Step 9: Build and run the full Strategies test suite**

Run: `dotnet test -c Release --filter "FullyQualifiedName~Strategies.Tests"`
Expected: PASS (all calculator + strategy tests).

- [ ] **Step 10: Build the whole solution**

Run: `dotnet build -c Release`
Expected: Build succeeded. (Worker still sets `Last24h*` on context and reads them — that's fine, those fields still exist.)

- [ ] **Step 11: Commit**

```bash
git add src/BinanceBot.Strategies/Pacific/PacificCalculator.cs src/BinanceBot.Strategies/Pacific/PacificStrategy.cs src/BinanceBot.Strategies/Pacific/PacificSettings.cs src/BinanceBot.Worker/appsettings.json tests/BinanceBot.Strategies.Tests/PacificCalculatorTests.cs tests/BinanceBot.Strategies.Tests/PacificStrategyTests.cs
git commit -m "Rewrite Pacific decision logic with trailing-escape ladder

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 3: Engine — track local extremes in `StrategyStateJson`

Introduce a pure `PositionExtremes` value object (testable) and wire `TradingEngineService` to maintain the low/high-since-last-trade, persist them in `StrategyStateJson`, feed them into `StrategyContext`, reset them on each executed trade, and remove the old 24h-price block.

**Files:**
- Create: `src/BinanceBot.Worker/Services/PositionExtremes.cs`
- Modify: `src/BinanceBot.Worker/Services/TradingEngineService.cs`
- Test: `tests/BinanceBot.Worker.Tests/PositionExtremesTests.cs`

**Interfaces:**
- Consumes: `StrategyContext.LowSinceTrade/HighSinceTrade` (Task 1), `BotStateData.StrategyStateJson` (existing).
- Produces:
  - `PositionExtremes` record with `decimal LowSinceTrade`, `decimal HighSinceTrade`.
  - `PositionExtremes.Initial(decimal price) → PositionExtremes`.
  - `PositionExtremes Observe(decimal price) → PositionExtremes` (returns updated copy).
  - `PositionExtremes.FromJson(string? json) → PositionExtremes?` (null when json null/empty/invalid).
  - `string ToJson()`.

- [ ] **Step 1: Write `PositionExtremesTests` (failing)**

Create `tests/BinanceBot.Worker.Tests/PositionExtremesTests.cs`:

```csharp
using BinanceBot.Worker.Services;
using FluentAssertions;

namespace BinanceBot.Worker.Tests;

public class PositionExtremesTests
{
    [Fact]
    public void Initial_SetsBothToPrice()
    {
        var e = PositionExtremes.Initial(60_000m);
        e.LowSinceTrade.Should().Be(60_000m);
        e.HighSinceTrade.Should().Be(60_000m);
    }

    [Fact]
    public void Observe_LowerPrice_UpdatesLowOnly()
    {
        var e = PositionExtremes.Initial(60_000m).Observe(55_000m);
        e.LowSinceTrade.Should().Be(55_000m);
        e.HighSinceTrade.Should().Be(60_000m);
    }

    [Fact]
    public void Observe_HigherPrice_UpdatesHighOnly()
    {
        var e = PositionExtremes.Initial(60_000m).Observe(63_000m);
        e.LowSinceTrade.Should().Be(60_000m);
        e.HighSinceTrade.Should().Be(63_000m);
    }

    [Fact]
    public void Observe_WithinRange_NoChange()
    {
        var start = new PositionExtremes(50_000m, 70_000m);
        var e = start.Observe(60_000m);
        e.Should().Be(start);
    }

    [Fact]
    public void JsonRoundTrip_Preserves()
    {
        var original = new PositionExtremes(50_000m, 70_000m);
        var restored = PositionExtremes.FromJson(original.ToJson());
        restored.Should().Be(original);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-json")]
    public void FromJson_NullOrInvalid_ReturnsNull(string? json)
    {
        PositionExtremes.FromJson(json).Should().BeNull();
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test -c Release --filter "FullyQualifiedName~PositionExtremesTests"`
Expected: FAIL — `PositionExtremes` does not exist.

- [ ] **Step 3: Implement `PositionExtremes`**

Create `src/BinanceBot.Worker/Services/PositionExtremes.cs`:

```csharp
using System.Text.Json;

namespace BinanceBot.Worker.Services;

/// <summary>
/// Tracks the lowest and highest observed price since the last executed trade.
/// Persisted in BotState.StrategyStateJson; consumed by the Pacific trailing-escape logic.
/// </summary>
public sealed record PositionExtremes(decimal LowSinceTrade, decimal HighSinceTrade)
{
    public static PositionExtremes Initial(decimal price) => new(price, price);

    public PositionExtremes Observe(decimal price) =>
        new(Math.Min(LowSinceTrade, price), Math.Max(HighSinceTrade, price));

    public string ToJson() => JsonSerializer.Serialize(this);

    public static PositionExtremes? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<PositionExtremes>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test -c Release --filter "FullyQualifiedName~PositionExtremesTests"`
Expected: PASS (all).

- [ ] **Step 5: Update `TradingEngineService.OnPriceTickAsync` — replace the 24h block with extreme tracking**

In `src/BinanceBot.Worker/Services/TradingEngineService.cs`, inside `OnPriceTickAsync`, replace this block:

```csharp
            var state = await stateRepo.GetAsync(ct);
            var balances = await _client.GetBalancesAsync(ct);

            // Update 24h prices if expired (> 24h) or not yet set
            var now = DateTime.UtcNow;
            decimal low24h, high24h;
            if (state?.Last24hPriceTimestamp is null || now > state.Last24hPriceTimestamp.Value.AddHours(24))
            {
                low24h = price.Low24H;
                high24h = price.High24H;
                state = (state ?? new BotStateData()) with
                {
                    Last24hLowPrice = low24h,
                    Last24hHighPrice = high24h,
                    Last24hPriceTimestamp = now
                };
                await stateRepo.SaveAsync(state, ct);
            }
            else
            {
                low24h = state.Last24hLowPrice;
                high24h = state.Last24hHighPrice;
            }
```

with:

```csharp
            var state = await stateRepo.GetAsync(ct);
            var balances = await _client.GetBalancesAsync(ct);

            // Track local price extremes since the last trade (for Pacific trailing-escape)
            var extremes = (PositionExtremes.FromJson(state?.StrategyStateJson)
                ?? PositionExtremes.Initial(price.Last)).Observe(price.Last);
            if (state is not null &&
                (string.IsNullOrWhiteSpace(state.StrategyStateJson) || state.StrategyStateJson != extremes.ToJson()))
            {
                state = state with { StrategyStateJson = extremes.ToJson() };
                await stateRepo.SaveAsync(state, ct);
            }
```

- [ ] **Step 6: Update the `StrategyContext` construction in `OnPriceTickAsync`**

Still in `OnPriceTickAsync`, replace the `context` initializer's last two lines:

```csharp
                Last24hLowPrice = low24h,
                Last24hHighPrice = high24h
```

with:

```csharp
                LowSinceTrade = extremes.LowSinceTrade,
                HighSinceTrade = extremes.HighSinceTrade
```

- [ ] **Step 7: Reset extremes on executed trade**

In `ExecuteTradeAsync`, in the `stateRepo.SaveAsync(state with { ... }, ct)` call after a successful trade, add the `StrategyStateJson` reset. Change:

```csharp
                await stateRepo.SaveAsync(state with
                {
                    ActiveStrategy = _strategyResolver.ActiveKey,
                    BtcBalance = balances.Btc,
                    EurBalance = balances.Eur,
                    LastTradePrice = trade.Price,
                    LastRebalanceTimestamp = DateTime.UtcNow,
                    RunState = _controlState.RunState
                }, ct);
```

to:

```csharp
                await stateRepo.SaveAsync(state with
                {
                    ActiveStrategy = _strategyResolver.ActiveKey,
                    BtcBalance = balances.Btc,
                    EurBalance = balances.Eur,
                    LastTradePrice = trade.Price,
                    LastRebalanceTimestamp = DateTime.UtcNow,
                    RunState = _controlState.RunState,
                    StrategyStateJson = PositionExtremes.Initial(trade.Price).ToJson()
                }, ct);
```

- [ ] **Step 8: Seed extremes on first-run init**

In `InitializeAsync`, in the first-run `stateRepo.SaveAsync(new BotStateData { ... }, ct)` call, replace the three 24h lines:

```csharp
                Last24hLowPrice = price.Low24H,
                Last24hHighPrice = price.High24H,
                Last24hPriceTimestamp = DateTime.UtcNow
```

with:

```csharp
                StrategyStateJson = PositionExtremes.Initial(price.Last).ToJson()
```

- [ ] **Step 9: Add the using directive**

Ensure `src/BinanceBot.Worker/Services/TradingEngineService.cs` has `using` for the namespace — `PositionExtremes` is in `BinanceBot.Worker.Services`, the same namespace as `TradingEngineService`, so no new `using` is required. Verify the file still references no `low24h`/`high24h`/`Last24h*` symbols (search the file; there should be none left).

- [ ] **Step 10: Build and run full suite**

Run: `dotnet build -c Release && dotnet test -c Release`
Expected: Build succeeded; all tests PASS.

- [ ] **Step 11: Commit**

```bash
git add src/BinanceBot.Worker/Services/PositionExtremes.cs src/BinanceBot.Worker/Services/TradingEngineService.cs tests/BinanceBot.Worker.Tests/PositionExtremesTests.cs
git commit -m "Track position extremes in StrategyStateJson for Pacific escape

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 4: Dashboard target price mirrors new logic

Extract the target-price computation into a pure, testable helper and update `/api/status` to use it, reading the persisted extremes from `StrategyStateJson`.

**Files:**
- Create: `src/BinanceBot.Worker/Services/PacificTargetPrice.cs`
- Modify: `src/BinanceBot.Worker/Program.cs`
- Test: `tests/BinanceBot.Worker.Tests/PacificTargetPriceTests.cs`

**Interfaces:**
- Consumes: `PositionExtremes.FromJson` (Task 3), `PacificSettings` (Task 1/2).
- Produces:
  - `PacificTargetPrice.Compute(bool holdingBtc, decimal lastTradePrice, decimal currentPrice, decimal lowSinceTrade, decimal highSinceTrade, decimal sellThresholdPct, decimal buyThresholdPct, decimal escapeDrawdownPct, decimal escapeRecoveryPct) → decimal?` (null when `lastTradePrice <= 0`).

- [ ] **Step 1: Write `PacificTargetPriceTests` (failing)**

Create `tests/BinanceBot.Worker.Tests/PacificTargetPriceTests.cs`:

```csharp
using BinanceBot.Worker.Services;
using FluentAssertions;

namespace BinanceBot.Worker.Tests;

public class PacificTargetPriceTests
{
    private static decimal? Compute(bool holdingBtc, decimal lastTrade, decimal price,
        decimal low, decimal high) =>
        PacificTargetPrice.Compute(holdingBtc, lastTrade, price, low, high,
            sellThresholdPct: 0.025m, buyThresholdPct: 0.025m,
            escapeDrawdownPct: 0.05m, escapeRecoveryPct: 0.025m);

    [Fact]
    public void NoLastTrade_ReturnsNull()
    {
        Compute(true, 0m, 60_000m, 60_000m, 60_000m).Should().BeNull();
    }

    [Fact]
    public void HoldingBtc_SmallDrawdown_ReturnsProfitTarget()
    {
        // drawdown 1.7% < 5% → profit target 60000*1.025 = 61500
        Compute(true, 60_000m, 59_000m, 59_000m, 60_000m).Should().Be(61_500m);
    }

    [Fact]
    public void HoldingBtc_InEscapeZone_ReturnsEscapeTarget()
    {
        // drawdown 16.7% >= 5% → escape target = low 50000 * 1.025 = 51250
        Compute(true, 60_000m, 50_000m, 50_000m, 60_000m).Should().Be(51_250m);
    }

    [Fact]
    public void HoldingEur_SmallRunup_ReturnsProfitTarget()
    {
        // runup 1.7% < 5% → profit target 60000*0.975 = 58500
        Compute(false, 60_000m, 61_000m, 60_000m, 61_000m).Should().Be(58_500m);
    }

    [Fact]
    public void HoldingEur_InEscapeZone_ReturnsEscapeTarget()
    {
        // runup 20% >= 5% → escape target = high 72000 * 0.975 = 70200
        Compute(false, 60_000m, 72_000m, 60_000m, 72_000m).Should().Be(70_200m);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test -c Release --filter "FullyQualifiedName~PacificTargetPriceTests"`
Expected: FAIL — `PacificTargetPrice` does not exist.

- [ ] **Step 3: Implement `PacificTargetPrice`**

Create `src/BinanceBot.Worker/Services/PacificTargetPrice.cs`:

```csharp
namespace BinanceBot.Worker.Services;

/// <summary>
/// Computes the price the dashboard should display as the active Pacific target,
/// mirroring PacificCalculator: the profit target normally, or the trailing-escape
/// target once the position is in the escape zone.
/// </summary>
public static class PacificTargetPrice
{
    public static decimal? Compute(
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

        if (holdingBtc)
        {
            var drawdown = (lastTradePrice - currentPrice) / lastTradePrice;
            if (drawdown >= escapeDrawdownPct && lowSinceTrade > 0)
                return lowSinceTrade * (1 + escapeRecoveryPct);
            return lastTradePrice * (1 + sellThresholdPct);
        }
        else
        {
            var runup = (currentPrice - lastTradePrice) / lastTradePrice;
            if (runup >= escapeDrawdownPct && highSinceTrade > 0)
                return highSinceTrade * (1 - escapeRecoveryPct);
            return lastTradePrice * (1 - buyThresholdPct);
        }
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test -c Release --filter "FullyQualifiedName~PacificTargetPriceTests"`
Expected: PASS (all).

- [ ] **Step 5: Replace the dashboard target-price block in `Program.cs`**

In `src/BinanceBot.Worker/Program.cs`, in the `/api/status` handler, replace the current interim target-price block (an earlier task collapsed the original stale logic into this normal-mode-only calc):

```csharp
        decimal? targetPrice = null;
        var lastTradePrice = state?.LastTradePrice ?? 0;
        if (strategyResolver.ActiveKey == "pacific" && lastTradePrice > 0)
        {
            var pacificSettings = app.Services.GetRequiredService<IOptions<PacificSettings>>().Value;

            targetPrice = btcAllocationPct >= 50
                ? lastTradePrice * (1 + pacificSettings.SellThresholdPct)
                : lastTradePrice * (1 - pacificSettings.BuyThresholdPct);
        }
```

with:

```csharp
        decimal? targetPrice = null;
        var lastTradePrice = state?.LastTradePrice ?? 0;
        if (strategyResolver.ActiveKey == "pacific" && lastTradePrice > 0)
        {
            var pacificSettings = app.Services.GetRequiredService<IOptions<PacificSettings>>().Value;
            var extremes = PositionExtremes.FromJson(state?.StrategyStateJson)
                ?? PositionExtremes.Initial(price.Last);

            targetPrice = PacificTargetPrice.Compute(
                holdingBtc: btcAllocationPct >= 50,
                lastTradePrice: lastTradePrice,
                currentPrice: price.Last,
                lowSinceTrade: extremes.LowSinceTrade,
                highSinceTrade: extremes.HighSinceTrade,
                sellThresholdPct: pacificSettings.SellThresholdPct,
                buyThresholdPct: pacificSettings.BuyThresholdPct,
                escapeDrawdownPct: pacificSettings.EscapeDrawdownPct,
                escapeRecoveryPct: pacificSettings.EscapeRecoveryPct);
        }
```

> The `tradeRepo` parameter is still used elsewhere (`/api/trades`); the `/api/status` handler no longer needs it but leave the parameter — removing it is out of scope. If the compiler warns about an unused `tradeRepo` in `/api/status`, ignore it (it is a route delegate parameter, not flagged by default).

- [ ] **Step 6: Build and run full suite**

Run: `dotnet build -c Release && dotnet test -c Release`
Expected: Build succeeded; all tests PASS.

- [ ] **Step 7: Commit**

```bash
git add src/BinanceBot.Worker/Services/PacificTargetPrice.cs src/BinanceBot.Worker/Program.cs tests/BinanceBot.Worker.Tests/PacificTargetPriceTests.cs
git commit -m "Mirror Pacific trailing-escape target in dashboard /api/status

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 5: Cleanup — remove orphaned `Last24h*` model fields

Remove the now-unused 24h fields from the domain model and repository mapping. The `BotStateEntity` columns stay in the database (no migration) but are no longer read or written.

**Files:**
- Modify: `src/BinanceBot.Core/Interfaces/IStateRepository.cs` (drop fields from `BotStateData`)
- Modify: `src/BinanceBot.Infrastructure/Persistence/StateRepository.cs` (drop mappings)
- Modify: `src/BinanceBot.Core/Models/StrategyContext.cs` (drop `Last24h*`)
- Modify: `src/BinanceBot.Infrastructure/Persistence/Entities/BotStateEntity.cs` (mark deprecated)

**Interfaces:**
- Consumes: nothing new.
- Produces: `BotStateData` and `StrategyContext` without `Last24hLowPrice`/`Last24hHighPrice`/`Last24hPriceTimestamp`.

- [ ] **Step 1: Confirm nothing else references the fields**

Run: `git grep -n "Last24h"  -- "src/*.cs"`
Expected: matches only in `IStateRepository.cs`, `StateRepository.cs`, `StrategyContext.cs`, `BotStateEntity.cs`, `BotDbContext.cs`, and the EF migration/snapshot files. (No references in `TradingEngineService.cs` or `Program.cs` — those were removed in Tasks 3–4.)

- [ ] **Step 2: Remove fields from `BotStateData`**

In `src/BinanceBot.Core/Interfaces/IStateRepository.cs`, delete these three lines from the `BotStateData` record:

```csharp
    public decimal Last24hLowPrice { get; init; }
    public decimal Last24hHighPrice { get; init; }
    public DateTime? Last24hPriceTimestamp { get; init; }
```

- [ ] **Step 3: Remove mappings from `StateRepository`**

In `src/BinanceBot.Infrastructure/Persistence/StateRepository.cs`, delete the three lines in `GetAsync` (inside the `new BotStateData { ... }`):

```csharp
            Last24hLowPrice = entity.Last24hLowPrice,
            Last24hHighPrice = entity.Last24hHighPrice,
            Last24hPriceTimestamp = entity.Last24hPriceTimestamp
```

(remove the trailing comma issue: ensure the property before them — `StrategyStateJson = entity.StrategyStateJson` — no longer has a trailing comma if it becomes the last initializer.)

And delete the three lines in `SaveAsync`:

```csharp
        entity.Last24hLowPrice = state.Last24hLowPrice;
        entity.Last24hHighPrice = state.Last24hHighPrice;
        entity.Last24hPriceTimestamp = state.Last24hPriceTimestamp;
```

- [ ] **Step 4: Remove `Last24h*` from `StrategyContext`**

In `src/BinanceBot.Core/Models/StrategyContext.cs`, delete:

```csharp
    public decimal? Last24hLowPrice { get; init; }  // deprecated — removed in Task 5
    public decimal? Last24hHighPrice { get; init; } // deprecated — removed in Task 5
```

- [ ] **Step 5: Mark the entity columns deprecated (keep them — no migration)**

In `src/BinanceBot.Infrastructure/Persistence/Entities/BotStateEntity.cs`, add a comment above the three properties so future readers know they are orphaned:

```csharp
    // Deprecated (Pacific v2): no longer read or written. Kept to avoid a schema migration.
    public decimal Last24hLowPrice { get; set; }
    public decimal Last24hHighPrice { get; set; }
    public DateTime? Last24hPriceTimestamp { get; set; }
```

- [ ] **Step 6: Build and run full suite**

Run: `dotnet build -c Release && dotnet test -c Release`
Expected: Build succeeded; all tests PASS.

- [ ] **Step 7: Commit**

```bash
git add src/BinanceBot.Core/Interfaces/IStateRepository.cs src/BinanceBot.Infrastructure/Persistence/StateRepository.cs src/BinanceBot.Core/Models/StrategyContext.cs src/BinanceBot.Infrastructure/Persistence/Entities/BotStateEntity.cs
git commit -m "Remove orphaned Last24h fields from model and repository

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Verification (after all tasks)

- [ ] `dotnet build -c Release` — succeeds with 0 errors.
- [ ] `dotnet test -c Release` — all tests pass, including the new `PacificCalculatorTests`, `PositionExtremesTests`, and `PacificTargetPriceTests`.
- [ ] `git grep -n "StaleTradeDays\|Last24h" -- "src/*.cs"` — only the deprecated (commented) `BotStateEntity` columns and EF migration/snapshot files remain.
- [ ] Manual smoke (optional, needs PostgreSQL + testnet creds): `dotnet run --project src/BinanceBot.Worker`, hit `GET /api/status`, confirm `targetPrice` is populated and reachable relative to `currentBtcPrice`.

## Notes for the implementer

- **Decimal math only.** Every price/threshold is `decimal`. Do not introduce `double`.
- **`P1` format** renders a `decimal` ratio as a percentage (e.g. `0.146m` → `14.6 %`). Used only in log/reason strings.
- **System.Text.Json + records:** `PositionExtremes` (de)serializes by property name (PascalCase) via its constructor. Both write and read use the default options, so round-tripping is stable.
- **Why the additive-then-cleanup shape (Tasks 1 & 5):** changing `PacificCalculator`'s signature ripples into `PacificStrategy`, and swapping `StrategyContext` fields ripples into the engine. Keeping the old fields until every consumer is migrated lets each task end with a green build and test run.
