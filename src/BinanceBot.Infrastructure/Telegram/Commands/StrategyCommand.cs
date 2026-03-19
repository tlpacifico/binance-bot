using System.Text;
using BinanceBot.Core;

namespace BinanceBot.Infrastructure.Telegram.Commands;

public sealed class StrategyCommand : ITelegramCommand
{
    private readonly StrategyResolver _resolver;

    public string Name => "/strategy";
    public string Description => "Switch or list strategies";

    public StrategyCommand(StrategyResolver resolver) => _resolver = resolver;

    public Task<string> ExecuteAsync(string[] args, CancellationToken ct = default)
    {
        if (args.Length == 0)
        {
            var available = _resolver.GetAvailable();
            var sb = new StringBuilder($"📋 *Active*: {_resolver.ActiveKey}\n\n*Available:*\n");
            foreach (var (key, desc) in available)
            {
                var marker = key.Equals(_resolver.ActiveKey, StringComparison.OrdinalIgnoreCase) ? "▶️" : "  ";
                sb.AppendLine($"{marker} `{key}` — {desc}");
            }
            return Task.FromResult(sb.ToString());
        }

        var name = args[0];
        if (_resolver.TrySetActive(name))
            return Task.FromResult($"✅ Strategy switched to *{name}*");

        return Task.FromResult($"❌ Unknown strategy: `{name}`. Use /strategy to see available options.");
    }
}
