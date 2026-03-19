using BinanceBot.Core.Enums;
using BinanceBot.Core.Models;
using BinanceBot.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Infrastructure.Tests;

public class TradeRepositoryTests : IDisposable
{
    private readonly BotDbContext _db;
    private readonly TradeRepository _repo;

    public TradeRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<BotDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new BotDbContext(options);
        _repo = new TradeRepository(_db);
    }

    [Fact]
    public async Task Insert_AndGetRecent_ShouldReturnTrade()
    {
        var trade = new TradeRecord
        {
            Timestamp = DateTime.UtcNow,
            Side = TradeSide.Buy,
            Price = 60_000m,
            QuantityBtc = 0.001m,
            QuoteAmountEur = 60m,
            Fee = 0.06m,
            StrategyName = "dca-rebalancing",
            Reason = "Test trade"
        };

        await _repo.InsertAsync(trade);
        var result = await _repo.GetRecentAsync(10);

        result.Should().HaveCount(1);
        result[0].Side.Should().Be(TradeSide.Buy);
        result[0].Price.Should().Be(60_000m);
        result[0].StrategyName.Should().Be("dca-rebalancing");
    }

    [Fact]
    public async Task GetRecent_ShouldRespectLimit()
    {
        for (int i = 0; i < 10; i++)
        {
            await _repo.InsertAsync(new TradeRecord
            {
                Timestamp = DateTime.UtcNow.AddMinutes(-i),
                Side = TradeSide.Buy,
                Price = 60_000m + i,
                QuantityBtc = 0.001m,
                QuoteAmountEur = 60m
            });
        }

        var result = await _repo.GetRecentAsync(3);
        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetRecent_ShouldOrderByTimestampDescending()
    {
        await _repo.InsertAsync(new TradeRecord { Timestamp = DateTime.UtcNow.AddHours(-2), Price = 1 });
        await _repo.InsertAsync(new TradeRecord { Timestamp = DateTime.UtcNow, Price = 3 });
        await _repo.InsertAsync(new TradeRecord { Timestamp = DateTime.UtcNow.AddHours(-1), Price = 2 });

        var result = await _repo.GetRecentAsync(10);

        result[0].Price.Should().Be(3);
        result[1].Price.Should().Be(2);
        result[2].Price.Should().Be(1);
    }

    public void Dispose() => _db.Dispose();
}
