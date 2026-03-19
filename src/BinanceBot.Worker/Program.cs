using BinanceBot.Core;
using BinanceBot.Core.Configuration;
using BinanceBot.Core.Interfaces;
using BinanceBot.Infrastructure.Binance;
using BinanceBot.Infrastructure.Persistence;
using BinanceBot.Infrastructure.Telegram;
using BinanceBot.Infrastructure.Telegram.Commands;
using BinanceBot.Strategies.DcaRebalancing;
using BinanceBot.Strategies.Pacific;
using BinanceBot.Worker.Services;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("logs/bot-.log", rollingInterval: RollingInterval.Day)
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((ctx, lc) => lc
        .ReadFrom.Configuration(ctx.Configuration)
        .WriteTo.Console()
        .WriteTo.File("logs/bot-.log", rollingInterval: RollingInterval.Day));

    // Configuration
    builder.Services.Configure<BinanceSettings>(builder.Configuration.GetSection(BinanceSettings.Section));
    builder.Services.Configure<TelegramSettings>(builder.Configuration.GetSection(TelegramSettings.Section));
    builder.Services.Configure<DashboardSettings>(builder.Configuration.GetSection(DashboardSettings.Section));
    builder.Services.Configure<DcaRebalancingSettings>(builder.Configuration.GetSection(DcaRebalancingSettings.Section));
    builder.Services.Configure<PacificSettings>(builder.Configuration.GetSection(PacificSettings.Section));

    // Persistence
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    builder.Services.AddDbContext<BotDbContext>(options =>
        options.UseNpgsql(connectionString));
    builder.Services.AddScoped<ITradeRepository, TradeRepository>();
    builder.Services.AddScoped<IStateRepository, StateRepository>();

    // Binance
    builder.Services.AddSingleton<IBinanceClient, BinanceClientAdapter>();

    // Telegram
    builder.Services.AddSingleton<ITelegramService, TelegramService>();
    builder.Services.AddSingleton<TelegramCommandHost>();

    // Telegram commands
    builder.Services.AddSingleton<ITelegramCommand, StatusCommand>();
    builder.Services.AddSingleton<ITelegramCommand, StartCommand>();
    builder.Services.AddSingleton<ITelegramCommand, StopCommand>();
    builder.Services.AddSingleton<ITelegramCommand, RebalanceCommand>();
    builder.Services.AddSingleton<ITelegramCommand, SettingsCommand>();
    builder.Services.AddSingleton<ITelegramCommand, HistoryCommand>();
    builder.Services.AddSingleton<ITelegramCommand, StrategyCommand>();
    builder.Services.AddSingleton<ITelegramCommand, HelpCommand>();

    // Strategy
    builder.Services.AddSingleton<DcaRebalancingStrategy>();
    builder.Services.AddSingleton<PacificStrategy>();
    builder.Services.AddSingleton<StrategyResolver>(sp =>
    {
        var resolver = new StrategyResolver();
        resolver.Register("pacific", sp.GetRequiredService<PacificStrategy>());
        resolver.Register("dca-rebalancing", sp.GetRequiredService<DcaRebalancingStrategy>());
        return resolver;
    });

    // Bot control
    builder.Services.AddSingleton<BotControlState>();

    // Background services
    builder.Services.AddSingleton<PriceMonitorService>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<PriceMonitorService>());
    builder.Services.AddHostedService<TradingEngineService>();
    builder.Services.AddHostedService<TelegramCommandService>();

    builder.Services.ConfigureHttpJsonOptions(opts =>
        opts.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

    var app = builder.Build();

    // Apply pending migrations
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        db.Database.Migrate();
    }

    // Dashboard: static files
    app.UseDefaultFiles();
    app.UseStaticFiles();

    // Dashboard: API endpoints
    var dashboardSettings = app.Configuration.GetSection(DashboardSettings.Section).Get<DashboardSettings>()
        ?? new DashboardSettings();

    app.MapGet("/api/health", () => Results.Ok(new { ok = true, uptime = Environment.TickCount64 / 1000.0 }));

    app.MapGet("/api/status", async (
        IBinanceClient client,
        IStateRepository stateRepo,
        ITradeRepository tradeRepo,
        StrategyResolver strategyResolver,
        BotControlState controlState,
        HttpContext ctx) =>
    {
        if (!AuthorizeRequest(ctx, dashboardSettings.AuthToken))
            return Results.Unauthorized();

        var binSettings = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<BinanceSettings>>().Value;
        var price = await client.GetPriceAsync(binSettings.TradingPair);
        var balances = await client.GetBalancesAsync();
        var state = await stateRepo.GetAsync();

        var totalValue = (balances.Btc * price.Last) + balances.Eur;
        var initialBalance = state?.InitialBalanceEur ?? 0;
        var pnl = totalValue - initialBalance;
        var pnlPct = initialBalance > 0 ? pnl / initialBalance * 100 : 0;
        var btcAllocationPct = totalValue > 0 ? (balances.Btc * price.Last) / totalValue * 100 : 0;

        decimal? targetPrice = null;
        var lastTradePrice = state?.LastTradePrice ?? 0;
        if (strategyResolver.ActiveKey == "pacific" && lastTradePrice > 0)
        {
            var pacificSettings = app.Services.GetRequiredService<IOptions<PacificSettings>>().Value;

            // Match strategy stale logic: no trades or last trade > StaleTradeDays ago
            var recentTrades = await tradeRepo.GetRecentAsync(1);
            var isStale = pacificSettings.StaleTradeDays > 0
                && (recentTrades.Count == 0
                    || (DateTime.UtcNow - recentTrades[0].Timestamp).TotalDays > pacificSettings.StaleTradeDays);

            if (btcAllocationPct >= 50)
            {
                var high24h = isStale ? state?.Last24hHighPrice ?? 0 : 0;
                targetPrice = isStale && high24h > 0
                    ? high24h * (1 + pacificSettings.SellThresholdPct)
                    : lastTradePrice * (1 + pacificSettings.SellThresholdPct);
            }
            else
            {
                var low24h = isStale ? state?.Last24hLowPrice ?? 0 : 0;
                targetPrice = isStale && low24h > 0
                    ? low24h * (1 - pacificSettings.BuyThresholdPct)
                    : lastTradePrice * (1 - pacificSettings.BuyThresholdPct);
            }
        }

        return Results.Ok(new
        {
            state = controlState.IsRunning ? "RUNNING" : "PAUSED",
            strategy = strategyResolver.ActiveKey,
            lastTradePrice,
            btcBalance = balances.Btc,
            eurBalance = balances.Eur,
            initialBalanceEur = initialBalance,
            currentBtcPrice = price.Last,
            pnlEur = pnl,
            pnlPct,
            btcAllocationPct,
            targetPrice,
            uptimeMs = Environment.TickCount64
        });
    });

    app.MapGet("/api/trades", async (
        ITradeRepository tradeRepo,
        HttpContext ctx,
        int? limit) =>
    {
        if (!AuthorizeRequest(ctx, dashboardSettings.AuthToken))
            return Results.Unauthorized();

        var trades = await tradeRepo.GetRecentAsync(limit ?? 50);
        return Results.Ok(new { trades, total = trades.Count });
    });

    app.Run($"http://0.0.0.0:{dashboardSettings.Port}");
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

static bool AuthorizeRequest(HttpContext ctx, string token)
{
    if (string.IsNullOrEmpty(token)) return true;
    var provided = ctx.Request.Headers.Authorization.ToString().Replace("Bearer ", "");
    return provided == token;
}
