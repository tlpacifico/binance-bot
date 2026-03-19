using BinanceBot.Core.Enums;
using BinanceBot.Core.Models;
using BinanceBot.Strategies.Pacific;
using FluentAssertions;

namespace BinanceBot.Strategies.Tests;

public class PacificCalculatorTests
{
    private const decimal SellThreshold = 0.025m;
    private const decimal BuyThreshold = 0.025m;
    private const decimal MinTradeEur = 10m;

    [Fact]
    public void HoldingBtc_PriceAboveSellThreshold_ShouldSell()
    {
        var portfolio = new Portfolio { BtcBalance = 0.01m, EurBalance = 10m, CurrentBtcPrice = 61_500m };
        // lastTradePrice=60000, target=60000*1.025=61500 → price >= target
        var result = PacificCalculator.Evaluate(61_500m, portfolio, 60_000m, 59_000m, 61_000m,
            SellThreshold, BuyThreshold, isStale: false, MinTradeEur);

        result.Action.Should().Be(TradeAction.Sell);
        result.QuantityBtc.Should().Be(0.01m);
        result.Reason.Should().Contain("Sell all BTC");
    }

    [Fact]
    public void HoldingBtc_PriceBelowSellThreshold_ShouldHold()
    {
        var portfolio = new Portfolio { BtcBalance = 0.01m, EurBalance = 10m, CurrentBtcPrice = 60_500m };
        // target=60000*1.025=61500 → 60500 < 61500
        var result = PacificCalculator.Evaluate(60_500m, portfolio, 60_000m, 59_000m, 61_000m,
            SellThreshold, BuyThreshold, isStale: false, MinTradeEur);

        result.Action.Should().Be(TradeAction.Hold);
    }

    [Fact]
    public void HoldingEur_PriceBelowBuyThreshold_ShouldBuy()
    {
        var portfolio = new Portfolio { BtcBalance = 0m, EurBalance = 500m, CurrentBtcPrice = 58_400m };
        // lastTradePrice=60000, target=60000*0.975=58500 → 58400 <= 58500
        var result = PacificCalculator.Evaluate(58_400m, portfolio, 60_000m, 57_000m, 61_000m,
            SellThreshold, BuyThreshold, isStale: false, MinTradeEur);

        result.Action.Should().Be(TradeAction.Buy);
        result.AmountEur.Should().Be(500m);
        result.Reason.Should().Contain("Buy all EUR");
    }

    [Fact]
    public void HoldingEur_PriceAboveBuyThreshold_ShouldHold()
    {
        var portfolio = new Portfolio { BtcBalance = 0m, EurBalance = 500m, CurrentBtcPrice = 59_000m };
        // target=60000*0.975=58500 → 59000 > 58500
        var result = PacificCalculator.Evaluate(59_000m, portfolio, 60_000m, 57_000m, 61_000m,
            SellThreshold, BuyThreshold, isStale: false, MinTradeEur);

        result.Action.Should().Be(TradeAction.Hold);
    }

    [Fact]
    public void StaleSell_PriceAboveHigh24hThreshold_ShouldSell()
    {
        // FIX: stale sell uses high24h instead of lastTradePrice
        // Bot bought at 60k, price dropped to 50k area. high24h=51000, target=51000*1.025=52275
        var portfolio = new Portfolio { BtcBalance = 0.01m, EurBalance = 10m, CurrentBtcPrice = 52_300m };

        var result = PacificCalculator.Evaluate(52_300m, portfolio, 60_000m, 49_000m, 51_000m,
            SellThreshold, BuyThreshold, isStale: true, MinTradeEur);

        result.Action.Should().Be(TradeAction.Sell);
        result.Reason.Should().Contain("stale-24h-high");
    }

    [Fact]
    public void StaleSell_PriceBelowHigh24hThreshold_ShouldHold()
    {
        // high24h=51000, target=51000*1.025=52275 → 52000 < 52275
        var portfolio = new Portfolio { BtcBalance = 0.01m, EurBalance = 10m, CurrentBtcPrice = 52_000m };

        var result = PacificCalculator.Evaluate(52_000m, portfolio, 60_000m, 49_000m, 51_000m,
            SellThreshold, BuyThreshold, isStale: true, MinTradeEur);

        result.Action.Should().Be(TradeAction.Hold);
        result.Reason.Should().Contain("stale-24h-high");
    }

    [Fact]
    public void StaleBuy_PriceBelowLow24hThreshold_ShouldBuy()
    {
        // Stale buy uses low24h. low24h=57000, target=57000*0.975=55575
        var portfolio = new Portfolio { BtcBalance = 0m, EurBalance = 500m, CurrentBtcPrice = 55_500m };

        var result = PacificCalculator.Evaluate(55_500m, portfolio, 60_000m, 57_000m, 61_000m,
            SellThreshold, BuyThreshold, isStale: true, MinTradeEur);

        result.Action.Should().Be(TradeAction.Buy);
        result.Reason.Should().Contain("stale-24h-low");
    }

    [Fact]
    public void StaleBuy_PriceAboveLow24hThreshold_ShouldHold()
    {
        // low24h=57000, target=57000*0.975=55575 → 56000 > 55575
        var portfolio = new Portfolio { BtcBalance = 0m, EurBalance = 500m, CurrentBtcPrice = 56_000m };

        var result = PacificCalculator.Evaluate(56_000m, portfolio, 60_000m, 57_000m, 61_000m,
            SellThreshold, BuyThreshold, isStale: true, MinTradeEur);

        result.Action.Should().Be(TradeAction.Hold);
        result.Reason.Should().Contain("stale-24h-low");
    }

    [Fact]
    public void EmptyPortfolio_ShouldHold()
    {
        var portfolio = new Portfolio { BtcBalance = 0m, EurBalance = 0m, CurrentBtcPrice = 60_000m };

        var result = PacificCalculator.Evaluate(60_000m, portfolio, 60_000m, 59_000m, 61_000m,
            SellThreshold, BuyThreshold, isStale: false, MinTradeEur);

        result.Action.Should().Be(TradeAction.Hold);
        result.Reason.Should().Contain("zero");
    }

    [Fact]
    public void TinyBalance_BelowMinTrade_ShouldHold()
    {
        // Holding EUR but only €5 — below MinTradeEur
        var portfolio = new Portfolio { BtcBalance = 0m, EurBalance = 5m, CurrentBtcPrice = 58_000m };

        var result = PacificCalculator.Evaluate(58_000m, portfolio, 60_000m, 57_000m, 61_000m,
            SellThreshold, BuyThreshold, isStale: false, MinTradeEur);

        result.Action.Should().Be(TradeAction.Hold);
        result.Reason.Should().Contain("minimum");
    }

    [Fact]
    public void DeadlockScenario_BoughtAt60k_DroppedTo50k_StaleSellUnlocks()
    {
        // THE BUG: Bot bought at 60k. Price crashed to 50k range. Normal sell target = 61.5k (unreachable).
        // With stale fix: high24h=51k, target=51k*1.025=52275. Price=52300 >= target → SELL.
        var portfolio = new Portfolio { BtcBalance = 0.01m, EurBalance = 10m, CurrentBtcPrice = 52_300m };

        // Without stale (would be stuck)
        var normalResult = PacificCalculator.Evaluate(52_300m, portfolio, 60_000m, 49_000m, 51_000m,
            SellThreshold, BuyThreshold, isStale: false, MinTradeEur);
        normalResult.Action.Should().Be(TradeAction.Hold, "normal mode should be stuck — target €61500");

        // With stale (the fix)
        var staleResult = PacificCalculator.Evaluate(52_300m, portfolio, 60_000m, 49_000m, 51_000m,
            SellThreshold, BuyThreshold, isStale: true, MinTradeEur);
        staleResult.Action.Should().Be(TradeAction.Sell, "stale mode should unlock — target €52275");
    }
}
