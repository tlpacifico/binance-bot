using BinanceBot.Core.Enums;

namespace BinanceBot.Core.Interfaces;

public sealed record BotStateData
{
    public string ActiveStrategy { get; init; } = string.Empty;
    public decimal BtcBalance { get; init; }
    public decimal EurBalance { get; init; }
    public decimal InitialBalanceEur { get; init; }
    public decimal LastTradePrice { get; init; }
    public DateTime? LastRebalanceTimestamp { get; init; }
    public BotRunState RunState { get; init; } = BotRunState.Running;
    public string? StrategyStateJson { get; init; }
    public decimal Last24hLowPrice { get; init; }
    public decimal Last24hHighPrice { get; init; }
    public DateTime? Last24hPriceTimestamp { get; init; }
}

public interface IStateRepository
{
    Task<BotStateData?> GetAsync(CancellationToken ct = default);
    Task SaveAsync(BotStateData state, CancellationToken ct = default);
}
