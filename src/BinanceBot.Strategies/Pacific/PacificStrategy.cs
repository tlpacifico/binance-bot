using BinanceBot.Core.Enums;
using BinanceBot.Core.Interfaces;
using BinanceBot.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BinanceBot.Strategies.Pacific;

public sealed class PacificStrategy : ITradingStrategy
{
    private readonly PacificSettings _settings;
    private readonly ILogger<PacificStrategy> _logger;
    private readonly List<decimal> _confirmationPrices = [];
    private readonly object _lock = new();

    public string Name => "pacific";
    public string Description => "Threshold-based all-in buy/sell with trailing-escape recovery and confirmation ticks";
    public TimeSpan EvaluationInterval => TimeSpan.FromSeconds(_settings.CheckIntervalSeconds);

    public PacificStrategy(IOptions<PacificSettings> settings, ILogger<PacificStrategy> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public Task<TradeDecision> EvaluateAsync(StrategyContext context, CancellationToken ct)
    {
        var price = context.CurrentPrice.Last;
        var lastTradePrice = GetLastTradePrice(context);
        var lowSinceTrade = context.LowSinceTrade ?? price;
        var highSinceTrade = context.HighSinceTrade ?? price;

        var decision = Evaluate(price, context.Portfolio, lastTradePrice, lowSinceTrade, highSinceTrade);

        _logger.LogDebug(
            "Pacific eval: price=€{Price:N2}, lastTrade=€{LastTrade:N2}, low=€{Low:N2}, high=€{High:N2}, decision={Action}",
            price, lastTradePrice, lowSinceTrade, highSinceTrade, decision.Action);

        if (decision.Action == TradeAction.Hold)
        {
            lock (_lock)
            {
                if (_confirmationPrices.Count > 0)
                {
                    _logger.LogDebug("Price back inside threshold, resetting {Ticks} confirmation ticks",
                        _confirmationPrices.Count);
                    _confirmationPrices.Clear();
                }
            }
            return Task.FromResult(decision);
        }

        // Threshold crossed — handle confirmation
        if (_settings.ConfirmationTicks <= 0)
            return Task.FromResult(decision);

        lock (_lock)
        {
            _confirmationPrices.Add(price);

            _logger.LogDebug("Confirmation tick {Count}/{Required}",
                _confirmationPrices.Count, _settings.ConfirmationTicks);

            if (_confirmationPrices.Count < _settings.ConfirmationTicks)
                return Task.FromResult(TradeDecision.Hold(
                    $"Confirmation {_confirmationPrices.Count}/{_settings.ConfirmationTicks} — waiting for more ticks"));

            // Enough ticks — verify average price also crosses threshold
            var avgPrice = _confirmationPrices.Average();
            _confirmationPrices.Clear();

            var avgDecision = Evaluate(avgPrice, context.Portfolio, lastTradePrice, lowSinceTrade, highSinceTrade);

            if (avgDecision.Action != TradeAction.Hold)
            {
                _logger.LogInformation(
                    "Confirmation complete: avg €{AvgPrice:N2} beyond threshold — executing {Action}",
                    avgPrice, avgDecision.Action);
                return Task.FromResult(avgDecision);
            }

            _logger.LogInformation(
                "Confirmation complete but avg €{AvgPrice:N2} NOT beyond threshold — resetting",
                avgPrice);
            return Task.FromResult(TradeDecision.Hold(
                $"Confirmation avg €{avgPrice:N2} did not pass threshold — reset"));
        }
    }

    private TradeDecision Evaluate(decimal price, Portfolio portfolio, decimal lastTradePrice,
        decimal lowSinceTrade, decimal highSinceTrade) =>
        PacificCalculator.Evaluate(
            price, portfolio, lastTradePrice, highSinceTrade,
            _settings.SellThresholdPct, _settings.BuyThresholdPct,
            _settings.EscapeDrawdownPct, _settings.EscapeRecoveryPct,
            _settings.MinTradeEur);

    private static decimal GetLastTradePrice(StrategyContext context)
    {
        if (context.LastTradePrice is > 0)
            return context.LastTradePrice.Value;
        if (context.RecentTrades.Count > 0)
            return context.RecentTrades[0].Price;
        return context.CurrentPrice.Last;
    }
}
