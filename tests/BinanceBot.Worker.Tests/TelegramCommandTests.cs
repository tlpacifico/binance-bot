using BinanceBot.Core;
using BinanceBot.Core.Configuration;
using BinanceBot.Core.Enums;
using BinanceBot.Core.Interfaces;
using BinanceBot.Core.Models;
using BinanceBot.Infrastructure.Telegram.Commands;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace BinanceBot.Worker.Tests;

public class TelegramCommandTests
{
    [Fact]
    public async Task StartCommand_ShouldResumeBot()
    {
        var controlState = new BotControlState();
        controlState.Pause();
        var cmd = new StartCommand(controlState);

        var result = await cmd.ExecuteAsync([], CancellationToken.None);

        controlState.IsRunning.Should().BeTrue();
        result.Should().Contain("resumed");
    }

    [Fact]
    public async Task StopCommand_ShouldPauseBot()
    {
        var controlState = new BotControlState();
        var cmd = new StopCommand(controlState);

        var result = await cmd.ExecuteAsync([], CancellationToken.None);

        controlState.IsRunning.Should().BeFalse();
        result.Should().Contain("paused");
    }

    [Fact]
    public async Task RebalanceCommand_ShouldSetRequest()
    {
        var controlState = new BotControlState();
        var cmd = new RebalanceCommand(controlState);

        await cmd.ExecuteAsync([], CancellationToken.None);

        controlState.ConsumeRebalanceRequest().Should().BeTrue();
    }

    [Fact]
    public async Task StrategyCommand_NoArgs_ShouldListStrategies()
    {
        var resolver = new StrategyResolver();
        var strategy = Substitute.For<ITradingStrategy>();
        strategy.Name.Returns("dca-rebalancing");
        strategy.Description.Returns("DCA strategy");
        resolver.Register("dca-rebalancing", strategy);
        var cmd = new StrategyCommand(resolver);

        var result = await cmd.ExecuteAsync([], CancellationToken.None);

        result.Should().Contain("dca-rebalancing");
        result.Should().Contain("Active");
    }

    [Fact]
    public async Task StrategyCommand_ValidName_ShouldSwitch()
    {
        var resolver = new StrategyResolver();
        var s1 = Substitute.For<ITradingStrategy>();
        s1.Name.Returns("a");
        var s2 = Substitute.For<ITradingStrategy>();
        s2.Name.Returns("b");
        resolver.Register("a", s1);
        resolver.Register("b", s2);
        var cmd = new StrategyCommand(resolver);

        var result = await cmd.ExecuteAsync(["b"], CancellationToken.None);

        result.Should().Contain("switched");
        resolver.ActiveKey.Should().Be("b");
    }

    [Fact]
    public async Task StrategyCommand_InvalidName_ShouldError()
    {
        var resolver = new StrategyResolver();
        resolver.Register("a", Substitute.For<ITradingStrategy>());
        var cmd = new StrategyCommand(resolver);

        var result = await cmd.ExecuteAsync(["nonexistent"], CancellationToken.None);

        result.Should().Contain("Unknown");
    }

    [Fact]
    public async Task HistoryCommand_NoTrades_ShouldShowEmpty()
    {
        var tradeRepo = Substitute.For<ITradeRepository>();
        tradeRepo.GetRecentAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<TradeRecord>>([]));
        var scopeFactory = CreateScopeFactory(services => services.AddSingleton(tradeRepo));
        var cmd = new HistoryCommand(scopeFactory);

        var result = await cmd.ExecuteAsync([]);

        result.Should().Contain("No trades");
    }

    [Fact]
    public async Task HistoryCommand_WithLimit_ShouldPassLimit()
    {
        var tradeRepo = Substitute.For<ITradeRepository>();
        tradeRepo.GetRecentAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<TradeRecord>>([]));
        var scopeFactory = CreateScopeFactory(services => services.AddSingleton(tradeRepo));
        var cmd = new HistoryCommand(scopeFactory);

        await cmd.ExecuteAsync(["10"]);

        await tradeRepo.Received(1).GetRecentAsync(10, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HelpCommand_ShouldListAllCommands()
    {
        var controlState = new BotControlState();
        var services = new ServiceCollection();
        services.AddSingleton<ITelegramCommand>(new StartCommand(controlState));
        services.AddSingleton<ITelegramCommand>(new StopCommand(controlState));
        var sp = services.BuildServiceProvider();
        var help = new HelpCommand(sp);

        var result = await help.ExecuteAsync([], CancellationToken.None);

        result.Should().Contain("/start");
        result.Should().Contain("/stop");
    }

    private static IServiceScopeFactory CreateScopeFactory(Action<ServiceCollection> configure)
    {
        var services = new ServiceCollection();
        configure(services);
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IServiceScopeFactory>();
    }
}
