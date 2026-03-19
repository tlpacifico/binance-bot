using BinanceBot.Core.Models;
using FluentAssertions;

namespace BinanceBot.Core.Tests;

public class PortfolioTests
{
    [Fact]
    public void TotalValueEur_ShouldCalculateCorrectly()
    {
        var portfolio = new Portfolio
        {
            BtcBalance = 0.001m,
            EurBalance = 40m,
            CurrentBtcPrice = 60_000m
        };

        portfolio.TotalValueEur.Should().Be(100m);
    }

    [Fact]
    public void AllocationPct_ShouldCalculateCorrectly()
    {
        var portfolio = new Portfolio
        {
            BtcBalance = 0.001m,
            EurBalance = 40m,
            CurrentBtcPrice = 60_000m
        };

        portfolio.BtcAllocationPct.Should().Be(60m);
        portfolio.EurAllocationPct.Should().Be(40m);
    }

    [Fact]
    public void ZeroValue_ShouldNotDivideByZero()
    {
        var portfolio = new Portfolio
        {
            BtcBalance = 0,
            EurBalance = 0,
            CurrentBtcPrice = 60_000m
        };

        portfolio.TotalValueEur.Should().Be(0);
        portfolio.BtcAllocationPct.Should().Be(0);
        portfolio.EurAllocationPct.Should().Be(0);
    }
}
