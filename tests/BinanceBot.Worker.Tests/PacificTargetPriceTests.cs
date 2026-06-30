using BinanceBot.Worker.Services;
using FluentAssertions;

namespace BinanceBot.Worker.Tests;

public class PacificTargetPriceTests
{
    private static decimal? Compute(bool holdingBtc, decimal lastTrade, decimal price,
        decimal low, decimal high) =>
        PacificTargetPrice.Compute(holdingBtc, lastTrade, price, low, high,
            sellThresholdPct: 0.025m, buyThresholdPct: 0.025m,
            escapeDrawdownPct: 0.05m, escapeRecoveryPct: 0.025m);

    [Fact]
    public void NoLastTrade_ReturnsNull()
    {
        Compute(true, 0m, 60_000m, 60_000m, 60_000m).Should().BeNull();
    }

    [Fact]
    public void HoldingBtc_SmallDrawdown_ReturnsProfitTarget()
    {
        // drawdown 1.7% < 5% → profit target 60000*1.025 = 61500
        Compute(true, 60_000m, 59_000m, 59_000m, 60_000m).Should().Be(61_500m);
    }

    [Fact]
    public void HoldingBtc_InEscapeZone_ReturnsEscapeTarget()
    {
        // drawdown 16.7% >= 5% → escape target = low 50000 * 1.025 = 51250
        Compute(true, 60_000m, 50_000m, 50_000m, 60_000m).Should().Be(51_250m);
    }

    [Fact]
    public void HoldingEur_SmallRunup_ReturnsProfitTarget()
    {
        // runup 1.7% < 5% → profit target 60000*0.975 = 58500
        Compute(false, 60_000m, 61_000m, 60_000m, 61_000m).Should().Be(58_500m);
    }

    [Fact]
    public void HoldingEur_InEscapeZone_ReturnsEscapeTarget()
    {
        // runup 20% >= 5% → escape target = high 72000 * 0.975 = 70200
        Compute(false, 60_000m, 72_000m, 60_000m, 72_000m).Should().Be(70_200m);
    }
}
