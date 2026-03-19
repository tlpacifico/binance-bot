using BinanceBot.Core;
using BinanceBot.Core.Configuration;
using BinanceBot.Core.Enums;
using BinanceBot.Core.Interfaces;
using BinanceBot.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BinanceBot.Worker.Services;

public sealed class TradingEngineService : IHostedService
{
    private const int MaxRetries = 3;
    private static readonly int[] RetryDelaysMs = [5000, 10000, 20000];

    private readonly IBinanceClient _client;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITelegramService _telegram;
    private readonly StrategyResolver _strategyResolver;
    private readonly BotControlState _controlState;
    private readonly PriceMonitorService _priceMonitor;
    private readonly BinanceSettings _binanceSettings;
    private readonly ILogger<TradingEngineService> _logger;

    private volatile bool _busy;

    public TradingEngineService(
        IBinanceClient client,
        IServiceScopeFactory scopeFactory,
        ITelegramService telegram,
        StrategyResolver strategyResolver,
        BotControlState controlState,
        PriceMonitorService priceMonitor,
        IOptions<BinanceSettings> binanceSettings,
        ILogger<TradingEngineService> logger)
    {
        _client = client;
        _scopeFactory = scopeFactory;
        _telegram = telegram;
        _strategyResolver = strategyResolver;
        _controlState = controlState;
        _priceMonitor = priceMonitor;
        _binanceSettings = binanceSettings.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        _priceMonitor.OnPriceTick += OnPriceTickAsync;

        var strategy = _strategyResolver.CurrentStrategy;
        _logger.LogInformation("Trading engine started with strategy: {Strategy}", strategy.Name);
        await _telegram.SendStartupAsync(strategy.Name, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _priceMonitor.OnPriceTick -= OnPriceTickAsync;
        _logger.LogInformation("Trading engine stopped");
        return Task.CompletedTask;
    }

    private async Task InitializeAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var stateRepo = scope.ServiceProvider.GetRequiredService<IStateRepository>();

        var savedState = await stateRepo.GetAsync(ct);
        if (savedState is not null)
        {
            _controlState.RunState = savedState.RunState;
            if (!string.IsNullOrEmpty(savedState.ActiveStrategy))
                _strategyResolver.TrySetActive(savedState.ActiveStrategy);

            _logger.LogInformation("Resumed from saved state: strategy={Strategy}, runState={RunState}",
                savedState.ActiveStrategy, savedState.RunState);

            await ReconcileAsync(stateRepo, savedState, ct);
        }
        else
        {
            // First run: detect from exchange
            var balances = await _client.GetBalancesAsync(ct);
            var price = await _client.GetPriceAsync(_binanceSettings.TradingPair, ct);

            var initialBalance = _binanceSettings.InitialBalanceEur > 0
                ? _binanceSettings.InitialBalanceEur
                : (balances.Btc * price.Last) + balances.Eur;

            await stateRepo.SaveAsync(new BotStateData
            {
                ActiveStrategy = _strategyResolver.ActiveKey,
                BtcBalance = balances.Btc,
                EurBalance = balances.Eur,
                InitialBalanceEur = initialBalance,
                LastTradePrice = price.Last,
                RunState = BotRunState.Running
            }, ct);

            _logger.LogInformation("First run initialized: BTC={Btc}, EUR={Eur}", balances.Btc, balances.Eur);
        }
    }

    private async Task ReconcileAsync(IStateRepository stateRepo, BotStateData savedState, CancellationToken ct)
    {
        try
        {
            var balances = await _client.GetBalancesAsync(ct);

            if (Math.Abs(savedState.BtcBalance - balances.Btc) > 0.00000001m ||
                Math.Abs(savedState.EurBalance - balances.Eur) > 0.01m)
            {
                _logger.LogWarning("Balance mismatch detected. Saved: BTC={SavedBtc} EUR={SavedEur}, Exchange: BTC={ExBtc} EUR={ExEur}",
                    savedState.BtcBalance, savedState.EurBalance, balances.Btc, balances.Eur);

                await stateRepo.SaveAsync(savedState with
                {
                    BtcBalance = balances.Btc,
                    EurBalance = balances.Eur
                }, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reconciliation failed, using saved state");
        }
    }

    private async Task OnPriceTickAsync(PriceData price, CancellationToken ct)
    {
        if (_busy || !_controlState.IsRunning) return;

        _busy = true;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var stateRepo = scope.ServiceProvider.GetRequiredService<IStateRepository>();
            var tradeRepo = scope.ServiceProvider.GetRequiredService<ITradeRepository>();

            var state = await stateRepo.GetAsync(ct);
            var balances = await _client.GetBalancesAsync(ct);

            var portfolio = new Portfolio
            {
                BtcBalance = balances.Btc,
                EurBalance = balances.Eur,
                CurrentBtcPrice = price.Last
            };

            var forceRebalance = _controlState.ConsumeRebalanceRequest();

            var recentTrades = await tradeRepo.GetRecentAsync(10, ct);
            var context = new StrategyContext
            {
                CurrentPrice = price,
                Portfolio = portfolio,
                RecentTrades = recentTrades,
                Timestamp = DateTime.UtcNow,
                LastRebalanceTimestamp = state?.LastRebalanceTimestamp
            };

            var strategy = _strategyResolver.CurrentStrategy;
            var decision = forceRebalance
                ? await ForceRebalanceAsync(context, strategy, ct)
                : await strategy.EvaluateAsync(context, ct);

            _logger.LogDebug("Strategy decision: {Action} — {Reason}", decision.Action, decision.Reason);

            if (decision.Action == TradeAction.Hold) return;

            await ExecuteTradeAsync(decision, portfolio, strategy.Name, tradeRepo, stateRepo, ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in trading engine tick");
            await _telegram.SendErrorAsync(ex.Message, ct);
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task<TradeDecision> ForceRebalanceAsync(StrategyContext context, ITradingStrategy strategy, CancellationToken ct)
    {
        _logger.LogInformation("Forced rebalance requested");
        // Evaluate normally but the strategy should treat periodic trigger as true
        var decision = await strategy.EvaluateAsync(
            context with { LastRebalanceTimestamp = null }, // Force periodic trigger
            ct);
        return decision;
    }

    private async Task ExecuteTradeAsync(TradeDecision decision, Portfolio portfolio, string strategyName,
        ITradeRepository tradeRepo, IStateRepository stateRepo, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                TradeRecord trade;
                if (decision.Action == TradeAction.Buy)
                {
                    trade = await _client.MarketBuyAsync(_binanceSettings.TradingPair, decision.AmountEur, ct);
                }
                else
                {
                    trade = await _client.MarketSellAsync(_binanceSettings.TradingPair, decision.QuantityBtc, ct);
                }

                trade = trade with { StrategyName = strategyName, Reason = decision.Reason };

                await tradeRepo.InsertAsync(trade, ct);

                // Update state
                var balances = await _client.GetBalancesAsync(ct);
                var state = await stateRepo.GetAsync(ct) ?? new BotStateData();
                await stateRepo.SaveAsync(state with
                {
                    ActiveStrategy = _strategyResolver.ActiveKey,
                    BtcBalance = balances.Btc,
                    EurBalance = balances.Eur,
                    LastTradePrice = trade.Price,
                    LastRebalanceTimestamp = DateTime.UtcNow,
                    RunState = _controlState.RunState
                }, ct);

                var updatedPortfolio = new Portfolio
                {
                    BtcBalance = balances.Btc,
                    EurBalance = balances.Eur,
                    CurrentBtcPrice = trade.Price
                };

                _logger.LogInformation("{Side} executed: {Qty} BTC @ €{Price} — {Reason}",
                    trade.Side, trade.QuantityBtc, trade.Price, decision.Reason);

                await _telegram.SendTradeAlertAsync(trade, updatedPortfolio, ct);
                return;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Trade attempt {Attempt}/{MaxRetries} failed", attempt, MaxRetries);
                if (attempt < MaxRetries)
                    await Task.Delay(RetryDelaysMs[attempt - 1], ct);
                else
                    await _telegram.SendErrorAsync($"Trade failed after {MaxRetries} attempts: {ex.Message}", ct);
            }
        }
    }
}
