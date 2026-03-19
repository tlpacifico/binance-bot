using BinanceBot.Core.Interfaces;
using BinanceBot.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BinanceBot.Strategies.DcaRebalancing;

public sealed class DcaRebalancingStrategy : ITradingStrategy
{
    private readonly DcaRebalancingSettings _settings;
    private readonly ILogger<DcaRebalancingStrategy> _logger;

    public string Name => "dca-rebalancing";
    public string Description => "DCA with portfolio rebalancing (target 60% BTC / 40% EUR)";
    public TimeSpan EvaluationInterval => TimeSpan.FromMinutes(_settings.CheckIntervalMinutes);

    public DcaRebalancingStrategy(IOptions<DcaRebalancingSettings> settings, ILogger<DcaRebalancingStrategy> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public Task<TradeDecision> EvaluateAsync(StrategyContext context, CancellationToken ct)
    {
        var periodicTrigger = IsPeriodicRebalanceDue(context.LastRebalanceTimestamp, context.Timestamp);

        _logger.LogDebug(
            "Evaluating DCA: BTC={BtcPct:N1}% (target {Target}%), deviation={Dev:N1}%, periodic={Periodic}",
            context.Portfolio.BtcAllocationPct,
            _settings.TargetBtcAllocationPct,
            Math.Abs(context.Portfolio.BtcAllocationPct - _settings.TargetBtcAllocationPct),
            periodicTrigger);

        var decision = AllocationCalculator.Calculate(
            context.Portfolio,
            _settings.TargetBtcAllocationPct,
            _settings.DeviationThresholdPct,
            _settings.MinTradeEur,
            periodicTrigger);

        return Task.FromResult(decision);
    }

    private bool IsPeriodicRebalanceDue(DateTime? lastRebalance, DateTime now)
    {
        if (_settings.PeriodicRebalanceIntervalDays <= 0) return false;
        if (lastRebalance is null) return true;
        return (now - lastRebalance.Value).TotalDays >= _settings.PeriodicRebalanceIntervalDays;
    }
}
