using System.Text;
using BinanceBot.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace BinanceBot.Infrastructure.Telegram.Commands;

public sealed class HistoryCommand : ITelegramCommand
{
    private readonly IServiceScopeFactory _scopeFactory;

    public string Name => "/history";
    public string Description => "Show recent trades";

    public HistoryCommand(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public async Task<string> ExecuteAsync(string[] args, CancellationToken ct = default)
    {
        var limit = 5;
        if (args.Length > 0 && int.TryParse(args[0], out var n) && n is > 0 and <= 50)
            limit = n;

        using var scope = _scopeFactory.CreateScope();
        var tradeRepo = scope.ServiceProvider.GetRequiredService<ITradeRepository>();
        var trades = await tradeRepo.GetRecentAsync(limit, ct);
        if (trades.Count == 0)
            return "📜 No trades yet.";

        var sb = new StringBuilder("📜 *Recent Trades*\n\n");
        foreach (var t in trades)
        {
            var emoji = t.Side == Core.Enums.TradeSide.Buy ? "🟢" : "🔴";
            sb.AppendLine($"{emoji} {t.Side} | €{t.Price:N2} | {t.QuantityBtc:N6} BTC | {t.Timestamp:g}");
        }

        return sb.ToString();
    }
}
