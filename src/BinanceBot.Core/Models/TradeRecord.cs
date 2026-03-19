using BinanceBot.Core.Enums;

namespace BinanceBot.Core.Models;

public sealed record TradeRecord
{
    public int Id { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public TradeSide Side { get; init; }
    public decimal Price { get; init; }
    public decimal QuantityBtc { get; init; }
    public decimal QuoteAmountEur { get; init; }
    public decimal Fee { get; init; }
    public string StrategyName { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}
