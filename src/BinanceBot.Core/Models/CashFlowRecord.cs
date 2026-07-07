using BinanceBot.Core.Enums;

namespace BinanceBot.Core.Models;

public sealed record CashFlowRecord
{
    public int Id { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public CashFlowType Type { get; init; }
    public decimal AmountEur { get; init; }
    public decimal BalanceAfter { get; init; }
}
