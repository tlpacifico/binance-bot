using BinanceBot.Core;
using BinanceBot.Core.Configuration;
using BinanceBot.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BinanceBot.Infrastructure.Telegram.Commands;

public sealed class StatusCommand : ITelegramCommand
{
    private readonly IBinanceClient _client;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly StrategyResolver _strategyResolver;
    private readonly BotControlState _controlState;
    private readonly BinanceSettings _settings;

    public string Name => "/status";
    public string Description => "Show current bot status";

    public StatusCommand(
        IBinanceClient client,
        IServiceScopeFactory scopeFactory,
        StrategyResolver strategyResolver,
        BotControlState controlState,
        IOptions<BinanceSettings> settings)
    {
        _client = client;
        _scopeFactory = scopeFactory;
        _strategyResolver = strategyResolver;
        _controlState = controlState;
        _settings = settings.Value;
    }

    public async Task<string> ExecuteAsync(string[] args, CancellationToken ct = default)
    {
        var price = await _client.GetPriceAsync(_settings.TradingPair, ct);
        var balances = await _client.GetBalancesAsync(ct);

        using var scope = _scopeFactory.CreateScope();
        var stateRepo = scope.ServiceProvider.GetRequiredService<IStateRepository>();
        var state = await stateRepo.GetAsync(ct);

        var totalValue = (balances.Btc * price.Last) + balances.Eur;
        var btcPct = totalValue > 0 ? (balances.Btc * price.Last) / totalValue * 100 : 0;
        var eurPct = totalValue > 0 ? balances.Eur / totalValue * 100 : 0;
        var initialBalance = state?.InitialBalanceEur ?? 0;
        var pnl = totalValue - initialBalance;
        var pnlPct = initialBalance > 0 ? pnl / initialBalance * 100 : 0;

        return $"""
            📊 *Bot Status*

            State: {(_controlState.IsRunning ? "🟢 Running" : "🔴 Paused")}
            Strategy: {_strategyResolver.ActiveKey}

            💰 BTC: {balances.Btc:N8} ({btcPct:N1}%)
            💶 EUR: €{balances.Eur:N2} ({eurPct:N1}%)
            📈 BTC Price: €{price.Last:N2}
            💎 Total: €{totalValue:N2}

            📉 P&L: €{pnl:N2} ({pnlPct:N2}%)
            """;
    }
}
