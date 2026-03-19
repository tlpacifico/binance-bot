using BinanceBot.Core.Enums;
using BinanceBot.Core.Models;
using BinanceBot.Strategies.DcaRebalancing;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BinanceBot.Strategies.Tests;

public class DcaRebalancingStrategyTests
{
    private readonly DcaRebalancingStrategy _strategy;

    public DcaRebalancingStrategyTests()
    {
        var settings = Options.Create(new DcaRebalancingSettings
        {
            TargetBtcAllocationPct = 60,
            DeviationThresholdPct = 5,
            PeriodicRebalanceIntervalDays = 30,
            CheckIntervalMinutes = 60,
            MinTradeEur = 10
        });
        _strategy = new DcaRebalancingStrategy(settings, NullLogger<DcaRebalancingStrategy>.Instance);
    }

    [Fact]
    public void Name_ShouldBeDcaRebalancing()
    {
        _strategy.Name.Should().Be("dca-rebalancing");
    }

    [Fact]
    public void EvaluationInterval_ShouldMatch()
    {
        _strategy.EvaluationInterval.Should().Be(TimeSpan.FromMinutes(60));
    }

    [Fact]
    public async Task Evaluate_BalancedPortfolio_ShouldHold()
    {
        var context = CreateContext(btcBalance: 0.01m, eurBalance: 400m, price: 60_000m, lastRebalance: DateTime.UtcNow.AddDays(-10));

        var decision = await _strategy.EvaluateAsync(context, CancellationToken.None);

        decision.Action.Should().Be(TradeAction.Hold);
    }

    [Fact]
    public async Task Evaluate_PeriodicDue_ShouldTrigger()
    {
        // Balanced but last rebalance was 31 days ago
        var context = CreateContext(btcBalance: 0.01m, eurBalance: 370m, price: 60_000m, lastRebalance: DateTime.UtcNow.AddDays(-31));

        var decision = await _strategy.EvaluateAsync(context, CancellationToken.None);

        decision.Action.Should().NotBe(TradeAction.Hold);
    }

    [Fact]
    public async Task Evaluate_NullLastRebalance_ShouldTriggerPeriodic()
    {
        var context = CreateContext(btcBalance: 0.01m, eurBalance: 370m, price: 60_000m, lastRebalance: null);

        var decision = await _strategy.EvaluateAsync(context, CancellationToken.None);

        // Null last rebalance = periodic trigger fires
        decision.Action.Should().NotBe(TradeAction.Hold);
    }

    private static StrategyContext CreateContext(decimal btcBalance, decimal eurBalance, decimal price, DateTime? lastRebalance)
    {
        return new StrategyContext
        {
            CurrentPrice = new PriceData(price, price * 0.98m, price * 1.02m),
            Portfolio = new Portfolio
            {
                BtcBalance = btcBalance,
                EurBalance = eurBalance,
                CurrentBtcPrice = price
            },
            RecentTrades = [],
            Timestamp = DateTime.UtcNow,
            LastRebalanceTimestamp = lastRebalance
        };
    }
}
