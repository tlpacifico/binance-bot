using BinanceBot.Core.Enums;
using BinanceBot.Core.Interfaces;
using BinanceBot.Core.Models;
using BinanceBot.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Infrastructure.Persistence;

public sealed class CashFlowRepository : ICashFlowRepository
{
    private readonly BotDbContext _db;

    public CashFlowRepository(BotDbContext db) => _db = db;

    public async Task<CashFlowResult> ApplyAsync(CashFlowType type, decimal amountEur, CancellationToken ct = default)
    {
        var state = await _db.BotState.FindAsync([1], ct)
            ?? throw new InvalidOperationException("Bot state not initialized; cannot apply a cash flow.");

        var oldBaseline = state.InitialBalanceEur;
        var newBaseline = type == CashFlowType.Deposit
            ? oldBaseline + amountEur
            : oldBaseline - amountEur;

        state.InitialBalanceEur = newBaseline;
        _db.CashFlows.Add(new CashFlowEntity
        {
            Timestamp = DateTime.UtcNow,
            Type = type.ToString(),
            AmountEur = amountEur,
            BalanceAfter = newBaseline
        });

        await _db.SaveChangesAsync(ct);
        return new CashFlowResult(oldBaseline, newBaseline);
    }

    public async Task<IReadOnlyList<CashFlowRecord>> GetRecentAsync(int limit = 50, CancellationToken ct = default)
    {
        return await _db.CashFlows
            .OrderByDescending(c => c.Timestamp)
            .ThenByDescending(c => c.Id)
            .Take(limit)
            .Select(c => new CashFlowRecord
            {
                Id = c.Id,
                Timestamp = c.Timestamp,
                Type = Enum.Parse<CashFlowType>(c.Type),
                AmountEur = c.AmountEur,
                BalanceAfter = c.BalanceAfter
            })
            .ToListAsync(ct);
    }
}
