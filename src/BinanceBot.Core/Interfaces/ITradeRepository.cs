using BinanceBot.Core.Models;

namespace BinanceBot.Core.Interfaces;

public interface ITradeRepository
{
    Task InsertAsync(TradeRecord trade, CancellationToken ct = default);
    Task<IReadOnlyList<TradeRecord>> GetRecentAsync(int limit = 50, CancellationToken ct = default);
}
