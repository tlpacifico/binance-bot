using BinanceBot.Core.Models;

namespace BinanceBot.Core.Interfaces;

public interface ITelegramService
{
    Task SendTradeAlertAsync(TradeRecord trade, Portfolio portfolio, CancellationToken ct = default);
    Task SendMessageAsync(string message, CancellationToken ct = default);
    Task SendErrorAsync(string error, CancellationToken ct = default);
    Task SendStartupAsync(string strategyName, CancellationToken ct = default);
}
