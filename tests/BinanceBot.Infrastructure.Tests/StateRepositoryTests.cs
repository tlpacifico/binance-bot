using BinanceBot.Core.Enums;
using BinanceBot.Core.Interfaces;
using BinanceBot.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Infrastructure.Tests;

public class StateRepositoryTests : IDisposable
{
    private readonly BotDbContext _db;
    private readonly StateRepository _repo;

    public StateRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<BotDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new BotDbContext(options);
        _repo = new StateRepository(_db);
    }

    [Fact]
    public async Task Get_WhenEmpty_ShouldReturnNull()
    {
        var result = await _repo.GetAsync();
        result.Should().BeNull();
    }

    [Fact]
    public async Task Save_AndGet_ShouldRoundtrip()
    {
        var state = new BotStateData
        {
            ActiveStrategy = "dca-rebalancing",
            BtcBalance = 0.5m,
            EurBalance = 1000m,
            InitialBalanceEur = 2000m,
            LastTradePrice = 60_000m,
            LastRebalanceTimestamp = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            RunState = BotRunState.Running,
            StrategyStateJson = "{\"test\":1}"
        };

        await _repo.SaveAsync(state);
        var result = await _repo.GetAsync();

        result.Should().NotBeNull();
        result!.ActiveStrategy.Should().Be("dca-rebalancing");
        result.BtcBalance.Should().Be(0.5m);
        result.EurBalance.Should().Be(1000m);
        result.RunState.Should().Be(BotRunState.Running);
        result.StrategyStateJson.Should().Be("{\"test\":1}");
    }

    [Fact]
    public async Task Save_Twice_ShouldUpdateNotDuplicate()
    {
        await _repo.SaveAsync(new BotStateData { ActiveStrategy = "first", BtcBalance = 1m });
        await _repo.SaveAsync(new BotStateData { ActiveStrategy = "second", BtcBalance = 2m });

        var count = await _db.BotState.CountAsync();
        count.Should().Be(1);

        var result = await _repo.GetAsync();
        result!.ActiveStrategy.Should().Be("second");
        result.BtcBalance.Should().Be(2m);
    }

    public void Dispose() => _db.Dispose();
}
