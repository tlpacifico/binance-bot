using BinanceBot.Core.Enums;
using FluentAssertions;

namespace BinanceBot.Core.Tests;

public class BotControlStateTests
{
    [Fact]
    public void DefaultState_ShouldBeRunning()
    {
        var state = new BotControlState();
        state.IsRunning.Should().BeTrue();
        state.RunState.Should().Be(BotRunState.Running);
    }

    [Fact]
    public void Pause_ShouldSetPaused()
    {
        var state = new BotControlState();
        state.Pause();
        state.IsRunning.Should().BeFalse();
        state.RunState.Should().Be(BotRunState.Paused);
    }

    [Fact]
    public void Start_AfterPause_ShouldResume()
    {
        var state = new BotControlState();
        state.Pause();
        state.Start();
        state.IsRunning.Should().BeTrue();
    }

    [Fact]
    public void RebalanceRequest_ShouldBeConsumedOnce()
    {
        var state = new BotControlState();

        state.ConsumeRebalanceRequest().Should().BeFalse();

        state.RequestRebalance();
        state.ConsumeRebalanceRequest().Should().BeTrue();
        state.ConsumeRebalanceRequest().Should().BeFalse(); // Already consumed
    }
}
