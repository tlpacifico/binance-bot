using BinanceBot.Core.Enums;
using BinanceBot.Core.Interfaces;
using BinanceBot.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Infrastructure.Persistence;

public sealed class StateRepository : IStateRepository
{
    private readonly BotDbContext _db;

    public StateRepository(BotDbContext db) => _db = db;

    public async Task<BotStateData?> GetAsync(CancellationToken ct = default)
    {
        var entity = await _db.BotState.FindAsync([1], ct);
        if (entity is null) return null;

        return new BotStateData
        {
            ActiveStrategy = entity.ActiveStrategy,
            BtcBalance = entity.BtcBalance,
            EurBalance = entity.EurBalance,
            InitialBalanceEur = entity.InitialBalanceEur,
            LastTradePrice = entity.LastTradePrice,
            LastRebalanceTimestamp = entity.LastRebalanceTimestamp,
            RunState = Enum.Parse<BotRunState>(entity.RunState),
            StrategyStateJson = entity.StrategyStateJson,
            Last24hLowPrice = entity.Last24hLowPrice,
            Last24hHighPrice = entity.Last24hHighPrice,
            Last24hPriceTimestamp = entity.Last24hPriceTimestamp
        };
    }

    public async Task SaveAsync(BotStateData state, CancellationToken ct = default)
    {
        var entity = await _db.BotState.FindAsync([1], ct);
        if (entity is null)
        {
            entity = new BotStateEntity { Id = 1 };
            _db.BotState.Add(entity);
        }

        entity.ActiveStrategy = state.ActiveStrategy;
        entity.BtcBalance = state.BtcBalance;
        entity.EurBalance = state.EurBalance;
        entity.InitialBalanceEur = state.InitialBalanceEur;
        entity.LastTradePrice = state.LastTradePrice;
        entity.LastRebalanceTimestamp = state.LastRebalanceTimestamp;
        entity.RunState = state.RunState.ToString();
        entity.StrategyStateJson = state.StrategyStateJson;
        entity.Last24hLowPrice = state.Last24hLowPrice;
        entity.Last24hHighPrice = state.Last24hHighPrice;
        entity.Last24hPriceTimestamp = state.Last24hPriceTimestamp;

        await _db.SaveChangesAsync(ct);
    }
}
