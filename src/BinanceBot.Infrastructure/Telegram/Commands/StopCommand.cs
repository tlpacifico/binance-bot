using BinanceBot.Core;

namespace BinanceBot.Infrastructure.Telegram.Commands;

public sealed class StopCommand : ITelegramCommand
{
    private readonly BotControlState _controlState;

    public string Name => "/stop";
    public string Description => "Pause trading";

    public StopCommand(BotControlState controlState) => _controlState = controlState;

    public Task<string> ExecuteAsync(string[] args, CancellationToken ct = default)
    {
        _controlState.Pause();
        return Task.FromResult("🔴 Trading paused.");
    }
}
