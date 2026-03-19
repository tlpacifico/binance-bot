using BinanceBot.Core.Configuration;
using BinanceBot.Core.Interfaces;
using BinanceBot.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;

namespace BinanceBot.Infrastructure.Telegram;

public sealed class TelegramService : ITelegramService
{
    private readonly TelegramBotClient _bot;
    private readonly long _chatId;
    private readonly ILogger<TelegramService> _logger;

    public TelegramService(IOptions<TelegramSettings> settings, ILogger<TelegramService> logger)
    {
        _logger = logger;
        var cfg = settings.Value;
        _bot = new TelegramBotClient(cfg.BotToken);
        _chatId = cfg.ChatId;
    }

    public async Task SendTradeAlertAsync(TradeRecord trade, Portfolio portfolio, CancellationToken ct = default)
    {
        var emoji = trade.Side == Core.Enums.TradeSide.Buy ? "🟢" : "🔴";
        var msg = $"""
            {emoji} *{trade.Side}* executed

            Price: €{trade.Price:N2}
            Quantity: {trade.QuantityBtc:N8} BTC
            Amount: €{trade.QuoteAmountEur:N2}
            Strategy: {trade.StrategyName}

            📊 Portfolio: {portfolio.BtcAllocationPct:N1}% BTC / {portfolio.EurAllocationPct:N1}% EUR
            💰 Total: €{portfolio.TotalValueEur:N2}
            """;

        await SendSafeAsync(msg, ct);
    }

    public async Task SendMessageAsync(string message, CancellationToken ct = default)
        => await SendSafeAsync(message, ct);

    public async Task SendErrorAsync(string error, CancellationToken ct = default)
        => await SendSafeAsync($"⚠️ *Error*: {error}", ct);

    public async Task SendStartupAsync(string strategyName, CancellationToken ct = default)
        => await SendSafeAsync($"🤖 Bot started! Strategy: *{strategyName}*", ct);

    private async Task SendSafeAsync(string message, CancellationToken ct)
    {
        var escaped = MarkdownV2Helper.EscapePreservingBold(message);
        try
        {
            await _bot.SendMessage(_chatId, escaped, parseMode: global::Telegram.Bot.Types.Enums.ParseMode.MarkdownV2, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send Telegram message");
            try
            {
                // Fallback without markdown
                await _bot.SendMessage(_chatId, message.Replace("*", ""), cancellationToken: ct);
            }
            catch (Exception ex2)
            {
                _logger.LogWarning(ex2, "Telegram fallback also failed");
            }
        }
    }
}
