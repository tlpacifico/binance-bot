using BinanceBot.Core.Configuration;
using BinanceBot.Infrastructure.Telegram.Commands;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace BinanceBot.Infrastructure.Telegram;

public sealed class TelegramCommandHost
{
    private readonly TelegramBotClient _bot;
    private readonly long _authorizedChatId;
    private readonly Dictionary<string, ITelegramCommand> _commands;
    private readonly ILogger<TelegramCommandHost> _logger;
    private int _offset;

    public TelegramCommandHost(
        IOptions<TelegramSettings> settings,
        IEnumerable<ITelegramCommand> commands,
        ILogger<TelegramCommandHost> logger)
    {
        var cfg = settings.Value;
        _bot = new TelegramBotClient(cfg.BotToken);
        _authorizedChatId = cfg.ChatId;
        _logger = logger;
        _commands = commands.ToDictionary(c => c.Name, c => c, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<bool> PollAsync(CancellationToken ct)
    {
        try
        {
            var updates = await _bot.GetUpdates(_offset, timeout: 30, allowedUpdates: [UpdateType.Message], cancellationToken: ct);

            foreach (var update in updates)
            {
                _offset = update.Id + 1;

                if (update.Message?.Text is not { } text) continue;
                if (update.Message.Chat.Id != _authorizedChatId)
                {
                    _logger.LogWarning("Unauthorized message from chat {ChatId}", update.Message.Chat.Id);
                    continue;
                }

                await HandleCommandAsync(text, ct);
            }

            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telegram polling error");
            return false;
        }
    }

    private async Task HandleCommandAsync(string text, CancellationToken ct)
    {
        var parts = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !parts[0].StartsWith('/')) return;

        var commandName = parts[0].Split('@')[0]; // Remove @botname suffix
        var args = parts.Length > 1 ? parts[1..] : [];

        if (!_commands.TryGetValue(commandName, out var command))
        {
            await SendReplyAsync($"❓ Unknown command: `{commandName}`. Use /help for available commands.", ct);
            return;
        }

        try
        {
            var response = await command.ExecuteAsync(args, ct);
            await SendReplyAsync(response, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Command {Command} failed", commandName);
            await SendReplyAsync($"❌ Command failed: {ex.Message}", ct);
        }
    }

    private async Task SendReplyAsync(string message, CancellationToken ct)
    {
        var escaped = MarkdownV2Helper.EscapePreservingBold(message);
        try
        {
            await _bot.SendMessage(_authorizedChatId, escaped, parseMode: ParseMode.MarkdownV2, cancellationToken: ct);
        }
        catch
        {
            try
            {
                await _bot.SendMessage(_authorizedChatId, message.Replace("*", ""), cancellationToken: ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send reply");
            }
        }
    }
}
