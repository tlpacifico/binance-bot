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
