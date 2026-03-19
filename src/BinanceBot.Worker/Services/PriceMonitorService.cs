using BinanceBot.Core;
using BinanceBot.Core.Configuration;
using BinanceBot.Core.Interfaces;
using BinanceBot.Core.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BinanceBot.Worker.Services;

public sealed class PriceMonitorService : BackgroundService
{
    private readonly IBinanceClient _client;
    private readonly StrategyResolver _strategyResolver;
    private readonly BinanceSettings _settings;
    private readonly ILogger<PriceMonitorService> _logger;
    private PriceData? _currentPrice;
    private int _consecutiveFailures;

    public PriceData? CurrentPrice => _currentPrice;

    public event Func<PriceData, CancellationToken, Task>? OnPriceTick;

    public PriceMonitorService(
        IBinanceClient client,
        StrategyResolver strategyResolver,
        IOptions<BinanceSettings> settings,
        ILogger<PriceMonitorService> logger)
    {
        _client = client;
        _strategyResolver = strategyResolver;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Price monitor started for {Symbol}", _settings.TradingPair);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var price = await _client.GetPriceAsync(_settings.TradingPair, stoppingToken);
                _currentPrice = price;
                _consecutiveFailures = 0;

                if (OnPriceTick is not null)
                    await OnPriceTick(price, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _consecutiveFailures++;
                _logger.LogWarning(ex, "Price fetch failed ({Failures} consecutive)", _consecutiveFailures);

                if (_consecutiveFailures >= 10)
                    _logger.LogError("10 consecutive price fetch failures");
            }

            var interval = _strategyResolver.CurrentStrategy.EvaluationInterval;
            await Task.Delay(interval, stoppingToken);
        }
    }
}
