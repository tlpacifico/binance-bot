using BinanceBot.Infrastructure.Telegram;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BinanceBot.Worker.Services;

public sealed class TelegramCommandService : BackgroundService
{
    private readonly TelegramCommandHost _commandHost;
    private readonly ILogger<TelegramCommandService> _logger;

    public TelegramCommandService(TelegramCommandHost commandHost, ILogger<TelegramCommandService> logger)
    {
        _commandHost = commandHost;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Telegram command listener started");

        var backoff = TimeSpan.Zero;
        var maxBackoff = TimeSpan.FromMinutes(2);

        while (!stoppingToken.IsCancellationRequested)
        {
            var success = await _commandHost.PollAsync(stoppingToken);

            if (success)
            {
                backoff = TimeSpan.Zero;
            }
            else
            {
                backoff = backoff == TimeSpan.Zero
                    ? TimeSpan.FromSeconds(5)
                    : TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, maxBackoff.Ticks));

                _logger.LogInformation("Telegram poll failed, retrying in {Backoff}s", backoff.TotalSeconds);
                await Task.Delay(backoff, stoppingToken);
            }
        }
    }
}
