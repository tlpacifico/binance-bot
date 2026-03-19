using System.Text;
using Microsoft.Extensions.DependencyInjection;

namespace BinanceBot.Infrastructure.Telegram.Commands;

public sealed class HelpCommand : ITelegramCommand
{
    private readonly IServiceProvider _serviceProvider;

    public string Name => "/help";
    public string Description => "Show available commands";

    public HelpCommand(IServiceProvider serviceProvider) => _serviceProvider = serviceProvider;

    public Task<string> ExecuteAsync(string[] args, CancellationToken ct = default)
    {
        var commands = _serviceProvider.GetServices<ITelegramCommand>();
        var sb = new StringBuilder("🤖 *Available Commands*\n\n");
        foreach (var cmd in commands.OrderBy(c => c.Name))
        {
            sb.AppendLine($"`{cmd.Name}` — {cmd.Description}");
        }
        return Task.FromResult(sb.ToString());
    }
}
