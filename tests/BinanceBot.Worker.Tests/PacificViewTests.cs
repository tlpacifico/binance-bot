using BinanceBot.Worker.Services;
using FluentAssertions;

namespace BinanceBot.Worker.Tests;

public class PacificViewTests
{
    private static PacificView? Compute(bool holdingBtc, decimal lastTrade, decimal price,
        decimal low, decimal high, decimal hardStop = 0m) =>
        PacificView.Compute(holdingBtc, lastTrade, price, low, high,
            sellThresholdPct: 0.025m, buyThresholdPct: 0.025m,
            escapeDrawdownPct: 0.05m, escapeRecoveryPct: 0.025m, hardStopLossPct: hardStop);

    [Fact]
    public void NoLastTrade_ReturnsNull()
    {
        Compute(true, 0m, 60_000m, 60_000m, 60_000m).Should().BeNull();
    }

    [Fact]
    public void HoldingBtc_SmallDrawdown_IsNormal_ProfitTargetActive()
    {
        // drawdown 1.7% < 5% → normal; profit target 60000*1.025 = 61500
        var v = Compute(true, 60_000m, 59_000m, 59_000m, 60_000m)!;
        v.HoldingBtc.Should().BeTrue();
        v.Mode.Should().Be(PacificView.ModeNormal);
        v.ProfitTarget.Should().Be(61_500m);
        v.EscapeArmPrice.Should().Be(57_000m); // 60000*(1-0.05)
        v.EscapeTarget.Should().BeNull();
        v.ActiveTarget.Should().Be(61_500m);
    }

    [Fact]
    public void HoldingBtc_InEscapeZone_IsArmed_EscapeTargetActive()
    {
        // drawdown 16.7% >= 5% → escape target = low 50000 * 1.025 = 51250
        var v = Compute(true, 60_000m, 50_000m, 50_000m, 60_000m)!;
        v.Mode.Should().Be(PacificView.ModeEscapeArmed);
        v.EscapeTarget.Should().Be(51_250m);
        v.ActiveTarget.Should().Be(51_250m);
    }

    [Fact]
    public void HoldingBtc_HardStopEnabledAndBreached_IsHardStop()
    {
        // drawdown 16.7% >= hardStop 10% → hard-stop mode
        var v = Compute(true, 60_000m, 50_000m, 50_000m, 60_000m, hardStop: 0.10m)!;
        v.Mode.Should().Be(PacificView.ModeHardStop);
    }

    [Fact]
    public void HoldingEur_SmallRunup_IsNormal_ProfitTargetActive()
    {
        // runup 1.7% < 5% → normal; profit target 60000*0.975 = 58500
        var v = Compute(false, 60_000m, 61_000m, 60_000m, 61_000m)!;
        v.HoldingBtc.Should().BeFalse();
        v.Mode.Should().Be(PacificView.ModeNormal);
        v.ProfitTarget.Should().Be(58_500m);
        v.EscapeArmPrice.Should().Be(63_000m); // 60000*(1+0.05)
        v.EscapeTarget.Should().BeNull();
        v.ActiveTarget.Should().Be(58_500m);
    }

    [Fact]
    public void HoldingEur_InEscapeZone_IsArmed_EscapeTargetActive()
    {
        // runup 20% >= 5% → escape target = high 72000 * 0.975 = 70200
        var v = Compute(false, 60_000m, 72_000m, 60_000m, 72_000m)!;
        v.Mode.Should().Be(PacificView.ModeEscapeArmed);
        v.EscapeTarget.Should().Be(70_200m);
        v.ActiveTarget.Should().Be(70_200m);
    }

    [Fact]
    public void HoldingEur_PeakArmedThenRetraced_StaysArmed()
    {
        // high 63600 (+6%, past +5% arm) but current 62000 (instantaneous run-up 3.33% < 5%).
        // Latched by the high → still escape-armed; escape target = 63600 * 0.975 = 62010.
        var v = Compute(false, 60_000m, 62_000m, 60_000m, 63_600m)!;
        v.Mode.Should().Be(PacificView.ModeEscapeArmed);
        v.EscapeTarget.Should().Be(62_010m);
    }

    [Fact]
    public void HoldingBtc_TroughArmedThenBounced_StaysArmed()
    {
        // low 56400 (-6%, past -5% arm) but current 58000 (instantaneous drawdown 3.33% < 5%).
        // Latched by the low → still escape-armed; escape target = 56400 * 1.025 = 57810.
        var v = Compute(true, 60_000m, 58_000m, 56_400m, 60_000m)!;
        v.Mode.Should().Be(PacificView.ModeEscapeArmed);
        v.EscapeTarget.Should().Be(57_810m);
    }

    [Fact]
    public void MoveFromLastTradePct_IsSigned()
    {
        Compute(false, 60_000m, 63_000m, 60_000m, 63_000m)!.MoveFromLastTradePct.Should().Be(0.05m);
        Compute(true, 60_000m, 57_000m, 57_000m, 60_000m)!.MoveFromLastTradePct.Should().Be(-0.05m);
    }
}
