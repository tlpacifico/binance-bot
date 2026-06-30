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
