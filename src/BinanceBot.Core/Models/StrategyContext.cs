namespace BinanceBot.Core.Models;

public sealed record StrategyContext
{
    public required PriceData CurrentPrice { get; init; }
    public required Portfolio Portfolio { get; init; }
    public IReadOnlyList<TradeRecord> RecentTrades { get; init; } = [];
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public DateTime? LastRebalanceTimestamp { get; init; }
}
