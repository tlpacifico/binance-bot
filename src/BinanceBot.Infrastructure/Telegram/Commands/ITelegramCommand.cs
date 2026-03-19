namespace BinanceBot.Infrastructure.Telegram.Commands;

public interface ITelegramCommand
{
    string Name { get; }
    string Description { get; }
    Task<string> ExecuteAsync(string[] args, CancellationToken ct = default);
}
