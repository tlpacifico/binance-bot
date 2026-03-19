using BinanceBot.Core.Models;

namespace BinanceBot.Core.Interfaces;

public interface ITradingStrategy
{
    string Name { get; }
    string Description { get; }
    TimeSpan EvaluationInterval { get; }
    Task<TradeDecision> EvaluateAsync(StrategyContext context, CancellationToken ct);
}
