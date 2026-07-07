using BinanceBot.Core.Enums;
using BinanceBot.Core.Models;

namespace BinanceBot.Core.Interfaces;

public sealed record CashFlowResult(decimal OldBaseline, decimal NewBaseline);

public interface ICashFlowRepository
{
    Task<CashFlowResult> ApplyAsync(CashFlowType type, decimal amountEur, CancellationToken ct = default);
    Task<IReadOnlyList<CashFlowRecord>> GetRecentAsync(int limit = 50, CancellationToken ct = default);
}
