using BinanceBot.Core.Enums;
using BinanceBot.Core.Interfaces;
using BinanceBot.Core.Models;
using BinanceBot.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Infrastructure.Persistence;

public sealed class TradeRepository : ITradeRepository
{
    private readonly BotDbContext _db;

    public TradeRepository(BotDbContext db) => _db = db;

    public async Task InsertAsync(TradeRecord trade, CancellationToken ct = default)
    {
        _db.Trades.Add(new TradeEntity
        {
            Timestamp = trade.Timestamp,
            Side = trade.Side.ToString(),
            Price = trade.Price,
            QuantityBtc = trade.QuantityBtc,
            QuoteAmountEur = trade.QuoteAmountEur,
            Fee = trade.Fee,
            StrategyName = trade.StrategyName,
            Reason = trade.Reason
        });
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<TradeRecord>> GetRecentAsync(int limit = 50, CancellationToken ct = default)
    {
        return await _db.Trades
            .OrderByDescending(t => t.Timestamp)
            .Take(limit)
            .Select(t => new TradeRecord
            {
                Id = t.Id,
                Timestamp = t.Timestamp,
                Side = Enum.Parse<TradeSide>(t.Side),
                Price = t.Price,
                QuantityBtc = t.QuantityBtc,
                QuoteAmountEur = t.QuoteAmountEur,
                Fee = t.Fee,
                StrategyName = t.StrategyName,
                Reason = t.Reason
            })
            .ToListAsync(ct);
    }
}
