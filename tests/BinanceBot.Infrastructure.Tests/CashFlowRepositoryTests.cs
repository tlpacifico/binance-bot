using BinanceBot.Core.Enums;
using BinanceBot.Infrastructure.Persistence;
using BinanceBot.Infrastructure.Persistence.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Infrastructure.Tests;

public class CashFlowRepositoryTests : IDisposable
{
    private readonly BotDbContext _db;
    private readonly CashFlowRepository _repo;

    public CashFlowRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<BotDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new BotDbContext(options);
        _repo = new CashFlowRepository(_db);
    }

    private async Task SeedBaselineAsync(decimal baseline)
    {
        _db.BotState.Add(new BotStateEntity { Id = 1, InitialBalanceEur = baseline });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Apply_Deposit_ShouldAddToBaselineAndRecordRow()
    {
        await SeedBaselineAsync(340m);

        var result = await _repo.ApplyAsync(CashFlowType.Deposit, 100m);

        result.OldBaseline.Should().Be(340m);
        result.NewBaseline.Should().Be(440m);

        var state = await _db.BotState.FindAsync(1);
        state!.InitialBalanceEur.Should().Be(440m);

        var row = await _db.CashFlows.SingleAsync();
        row.Type.Should().Be("Deposit");
        row.AmountEur.Should().Be(100m);
        row.BalanceAfter.Should().Be(440m);
    }

    [Fact]
    public async Task Apply_Withdrawal_ShouldSubtractFromBaseline()
    {
        await SeedBaselineAsync(440m);

        var result = await _repo.ApplyAsync(CashFlowType.Withdrawal, 50m);

        result.NewBaseline.Should().Be(390m);
        (await _db.BotState.FindAsync(1))!.InitialBalanceEur.Should().Be(390m);
        (await _db.CashFlows.SingleAsync()).Type.Should().Be("Withdrawal");
    }

    [Fact]
    public async Task Apply_WhenNoState_ShouldThrow()
    {
        var act = async () => await _repo.ApplyAsync(CashFlowType.Deposit, 100m);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetRecent_ShouldReturnNewestFirstRespectingLimit()
    {
        _db.CashFlows.AddRange(
            new CashFlowEntity { Timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), Type = "Deposit", AmountEur = 10m, BalanceAfter = 10m },
            new CashFlowEntity { Timestamp = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc), Type = "Deposit", AmountEur = 30m, BalanceAfter = 40m },
            new CashFlowEntity { Timestamp = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), Type = "Withdrawal", AmountEur = 20m, BalanceAfter = 20m });
        await _db.SaveChangesAsync();

        var result = await _repo.GetRecentAsync(2);

        result.Should().HaveCount(2);
        result[0].AmountEur.Should().Be(30m);   // 2026-01-03 newest
        result[1].AmountEur.Should().Be(20m);   // 2026-01-02
        result[0].Type.Should().Be(CashFlowType.Deposit);
    }

    public void Dispose() => _db.Dispose();
}
