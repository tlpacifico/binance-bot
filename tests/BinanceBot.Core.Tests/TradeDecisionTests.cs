using BinanceBot.Core.Enums;
using BinanceBot.Core.Models;
using FluentAssertions;

namespace BinanceBot.Core.Tests;

public class TradeDecisionTests
{
    [Fact]
    public void Hold_ShouldSetAction()
    {
        var decision = TradeDecision.Hold("test reason");
        decision.Action.Should().Be(TradeAction.Hold);
        decision.Reason.Should().Be("test reason");
    }

    [Fact]
    public void Buy_ShouldSetAmountEur()
    {
        var decision = TradeDecision.Buy(100m, "buy reason");
        decision.Action.Should().Be(TradeAction.Buy);
        decision.AmountEur.Should().Be(100m);
        decision.Reason.Should().Be("buy reason");
    }

    [Fact]
    public void Sell_ShouldSetQuantityBtc()
    {
        var decision = TradeDecision.Sell(0.5m, "sell reason");
        decision.Action.Should().Be(TradeAction.Sell);
        decision.QuantityBtc.Should().Be(0.5m);
        decision.Reason.Should().Be("sell reason");
    }
}
