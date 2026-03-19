using BinanceBot.Core;

namespace BinanceBot.Infrastructure.Telegram.Commands;

public sealed class StartCommand : ITelegramCommand
{
    private readonly BotControlState _controlState;

    public string Name => "/start";
    public string Description => "Resume trading";

    public StartCommand(BotControlState controlState) => _controlState = controlState;

    public Task<string> ExecuteAsync(string[] args, CancellationToken ct = default)
    {
        _controlState.Start();
        return Task.FromResult("🟢 Trading resumed.");
    }
}
