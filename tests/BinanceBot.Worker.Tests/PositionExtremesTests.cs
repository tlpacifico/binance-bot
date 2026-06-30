using BinanceBot.Worker.Services;
using FluentAssertions;

namespace BinanceBot.Worker.Tests;

public class PositionExtremesTests
{
    [Fact]
    public void Initial_SetsBothToPrice()
    {
        var e = PositionExtremes.Initial(60_000m);
        e.LowSinceTrade.Should().Be(60_000m);
        e.HighSinceTrade.Should().Be(60_000m);
    }

    [Fact]
    public void Observe_LowerPrice_UpdatesLowOnly()
    {
        var e = PositionExtremes.Initial(60_000m).Observe(55_000m);
        e.LowSinceTrade.Should().Be(55_000m);
        e.HighSinceTrade.Should().Be(60_000m);
    }

    [Fact]
    public void Observe_HigherPrice_UpdatesHighOnly()
    {
        var e = PositionExtremes.Initial(60_000m).Observe(63_000m);
        e.LowSinceTrade.Should().Be(60_000m);
        e.HighSinceTrade.Should().Be(63_000m);
    }

    [Fact]
    public void Observe_WithinRange_NoChange()
    {
        var start = new PositionExtremes(50_000m, 70_000m);
        var e = start.Observe(60_000m);
        e.Should().Be(start);
    }

    [Fact]
    public void JsonRoundTrip_Preserves()
    {
        var original = new PositionExtremes(50_000m, 70_000m);
        var restored = PositionExtremes.FromJson(original.ToJson());
        restored.Should().Be(original);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-json")]
    public void FromJson_NullOrInvalid_ReturnsNull(string? json)
    {
        PositionExtremes.FromJson(json).Should().BeNull();
    }
}
