using BinanceBot.Core.Enums;
using BinanceBot.Core.Interfaces;
using BinanceBot.Infrastructure.Telegram.Commands;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace BinanceBot.Worker.Tests;

public class CashFlowCommandTests
{
    private static IServiceScopeFactory ScopeFactory(Action<ServiceCollection> configure)
    {
        var services = new ServiceCollection();
        configure(services);
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IServiceScopeFactory>();
    }

    [Fact]
    public async Task Deposit_ValidAmount_ShouldApplyAndReport()
    {
        var repo = Substitute.For<ICashFlowRepository>();
        repo.ApplyAsync(CashFlowType.Deposit, 100m, Arg.Any<CancellationToken>())
            .Returns(new CashFlowResult(340m, 440m));
        var cmd = new DepositCommand(ScopeFactory(s => s.AddSingleton(repo)));

        var result = await cmd.ExecuteAsync(["100"]);

        await repo.Received(1).ApplyAsync(CashFlowType.Deposit, 100m, Arg.Any<CancellationToken>());
        result.Should().Contain("340").And.Contain("440");
    }

    [Theory]
    [InlineData(new object[] { new string[0] })]
    [InlineData(new object[] { new[] { "abc" } })]
    [InlineData(new object[] { new[] { "0" } })]
    [InlineData(new object[] { new[] { "-5" } })]
    public async Task Deposit_InvalidAmount_ShouldRejectWithoutApplying(string[] args)
    {
        var repo = Substitute.For<ICashFlowRepository>();
        var cmd = new DepositCommand(ScopeFactory(s => s.AddSingleton(repo)));

        var result = await cmd.ExecuteAsync(args);

        result.Should().Contain("Uso:");
        await repo.DidNotReceiveWithAnyArgs().ApplyAsync(default, default, default);
    }

    [Fact]
    public async Task Withdraw_WithinBaseline_ShouldApply()
    {
        var state = Substitute.For<IStateRepository>();
        state.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new BotStateData { InitialBalanceEur = 440m });
        var repo = Substitute.For<ICashFlowRepository>();
        repo.ApplyAsync(CashFlowType.Withdrawal, 50m, Arg.Any<CancellationToken>())
            .Returns(new CashFlowResult(440m, 390m));
        var cmd = new WithdrawCommand(ScopeFactory(s => { s.AddSingleton(state); s.AddSingleton(repo); }));

        var result = await cmd.ExecuteAsync(["50"]);

        await repo.Received(1).ApplyAsync(CashFlowType.Withdrawal, 50m, Arg.Any<CancellationToken>());
        result.Should().Contain("390");
    }

    [Fact]
    public async Task Withdraw_ExceedingBaseline_ShouldRejectWithoutApplying()
    {
        var state = Substitute.For<IStateRepository>();
        state.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new BotStateData { InitialBalanceEur = 40m });
        var repo = Substitute.For<ICashFlowRepository>();
        var cmd = new WithdrawCommand(ScopeFactory(s => { s.AddSingleton(state); s.AddSingleton(repo); }));

        var result = await cmd.ExecuteAsync(["100"]);

        result.Should().Contain("negativo");
        await repo.DidNotReceiveWithAnyArgs().ApplyAsync(default, default, default);
    }
}
