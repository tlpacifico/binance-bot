using BinanceBot.Core.Enums;
using BinanceBot.Core.Models;
using BinanceBot.Strategies.DcaRebalancing;
using FluentAssertions;

namespace BinanceBot.Strategies.Tests;

public class AllocationCalculatorTests
{
    private const decimal TargetBtcPct = 60m;
    private const decimal DeviationThreshold = 5m;
    private const decimal MinTradeEur = 10m;

    [Fact]
    public void Portfolio_AtTarget_ShouldHold()
    {
        // 60% BTC, 40% EUR — deviation 0%
        var portfolio = new Portfolio
        {
            BtcBalance = 0.01m,
            EurBalance = 400m,
            CurrentBtcPrice = 60_000m // 600/1000 = 60%
        };

        var result = AllocationCalculator.Calculate(portfolio, TargetBtcPct, DeviationThreshold, MinTradeEur, periodicTrigger: false);

        result.Action.Should().Be(TradeAction.Hold);
    }

    [Fact]
    public void Portfolio_SmallDeviation_ShouldHold()
    {
        // ~62% BTC — deviation 2%, below 5% threshold
        var portfolio = new Portfolio
        {
            BtcBalance = 0.01m,
            EurBalance = 370m,
            CurrentBtcPrice = 60_000m // 600/(600+370) ≈ 61.9%
        };

        var result = AllocationCalculator.Calculate(portfolio, TargetBtcPct, DeviationThreshold, MinTradeEur, periodicTrigger: false);

        result.Action.Should().Be(TradeAction.Hold);
    }

    [Fact]
    public void BtcOverrepresented_ShouldSell()
    {
        // 70% BTC — deviation 10%, above threshold
        var portfolio = new Portfolio
        {
            BtcBalance = 0.01m,
            EurBalance = 257.1m,
            CurrentBtcPrice = 60_000m // 600/(600+257.1) ≈ 70%
        };

        var result = AllocationCalculator.Calculate(portfolio, TargetBtcPct, DeviationThreshold, MinTradeEur, periodicTrigger: false);

        result.Action.Should().Be(TradeAction.Sell);
        result.QuantityBtc.Should().BeGreaterThan(0);
        result.Reason.Should().Contain("Rebalance");
    }

    [Fact]
    public void BtcUnderrepresented_ShouldBuy()
    {
        // 50% BTC — deviation 10%, above threshold
        var portfolio = new Portfolio
        {
            BtcBalance = 0.01m,
            EurBalance = 600m,
            CurrentBtcPrice = 60_000m // 600/(600+600) = 50%
        };

        var result = AllocationCalculator.Calculate(portfolio, TargetBtcPct, DeviationThreshold, MinTradeEur, periodicTrigger: false);

        result.Action.Should().Be(TradeAction.Buy);
        result.AmountEur.Should().BeGreaterThan(0);
        result.Reason.Should().Contain("Rebalance");
    }

    [Fact]
    public void SmallDeviation_ButPeriodicTrigger_ShouldRebalance()
    {
        // ~62% BTC — deviation 2%, below threshold, but periodic trigger
        var portfolio = new Portfolio
        {
            BtcBalance = 0.01m,
            EurBalance = 370m,
            CurrentBtcPrice = 60_000m // 600/(600+370) ≈ 61.9%
        };

        var result = AllocationCalculator.Calculate(portfolio, TargetBtcPct, DeviationThreshold, MinTradeEur, periodicTrigger: true);

        result.Action.Should().Be(TradeAction.Sell); // BTC slightly over target
        result.Reason.Should().Contain("Periodic");
    }

    [Fact]
    public void TradeAmount_BelowMinimum_ShouldHold()
    {
        // Very small portfolio → trade amount below MinTradeEur
        var portfolio = new Portfolio
        {
            BtcBalance = 0.00001m,
            EurBalance = 0.4m,
            CurrentBtcPrice = 60_000m // 0.6/(0.6+0.4) = 60%
        };

        var result = AllocationCalculator.Calculate(portfolio, TargetBtcPct, DeviationThreshold, MinTradeEur, periodicTrigger: true);

        result.Action.Should().Be(TradeAction.Hold);
    }

    [Fact]
    public void EmptyPortfolio_ShouldHold()
    {
        var portfolio = new Portfolio
        {
            BtcBalance = 0,
            EurBalance = 0,
            CurrentBtcPrice = 60_000m
        };

        var result = AllocationCalculator.Calculate(portfolio, TargetBtcPct, DeviationThreshold, MinTradeEur, periodicTrigger: false);

        result.Action.Should().Be(TradeAction.Hold);
        result.Reason.Should().Contain("zero");
    }

    [Fact]
    public void BuyAmount_ShouldNotExceedAvailableEur()
    {
        // BTC very underrepresented, but limited EUR available
        var portfolio = new Portfolio
        {
            BtcBalance = 0.001m,
            EurBalance = 150m,
            CurrentBtcPrice = 60_000m // 60/(60+150) ≈ 28.6% BTC
        };

        var result = AllocationCalculator.Calculate(portfolio, TargetBtcPct, DeviationThreshold, MinTradeEur, periodicTrigger: false);

        result.Action.Should().Be(TradeAction.Buy);
        result.AmountEur.Should().BeLessThanOrEqualTo(portfolio.EurBalance);
    }
}
