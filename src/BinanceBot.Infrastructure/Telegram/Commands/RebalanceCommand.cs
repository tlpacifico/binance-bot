using BinanceBot.Core;

namespace BinanceBot.Infrastructure.Telegram.Commands;

public sealed class RebalanceCommand : ITelegramCommand
{
    private readonly BotControlState _controlState;

    public string Name => "/rebalance";
    public string Description => "Force immediate rebalance";

    public RebalanceCommand(BotControlState controlState) => _controlState = controlState;

    public Task<string> ExecuteAsync(string[] args, CancellationToken ct = default)
    {
        _controlState.RequestRebalance();
        return Task.FromResult("🔄 Rebalance requested. Will execute on next evaluation cycle.");
    }
}
