using BinanceBot.Core;

namespace BinanceBot.Infrastructure.Telegram.Commands;

public sealed class SettingsCommand : ITelegramCommand
{
    private readonly StrategyResolver _resolver;
    private readonly BotControlState _controlState;

    public string Name => "/settings";
    public string Description => "View current settings";

    public SettingsCommand(StrategyResolver resolver, BotControlState controlState)
    {
        _resolver = resolver;
        _controlState = controlState;
    }

    public Task<string> ExecuteAsync(string[] args, CancellationToken ct = default)
    {
        var strategy = _resolver.CurrentStrategy;
        return Task.FromResult($"""
            ⚙️ *Settings*

            Strategy: {_resolver.ActiveKey}
            Evaluation Interval: {strategy.EvaluationInterval.TotalMinutes:N0} min
            State: {(_controlState.IsRunning ? "Running" : "Paused")}
            """);
    }
}
