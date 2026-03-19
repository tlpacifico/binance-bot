using BinanceBot.Core.Interfaces;
using BinanceBot.Core.Models;
using FluentAssertions;
using NSubstitute;

namespace BinanceBot.Core.Tests;

public class StrategyResolverTests
{
    [Fact]
    public void Register_FirstStrategy_BecomesActive()
    {
        var resolver = new StrategyResolver();
        var strategy = CreateMockStrategy("test-strategy", "Test");

        resolver.Register("test", strategy);

        resolver.ActiveKey.Should().Be("test");
        resolver.CurrentStrategy.Should().Be(strategy);
    }

    [Fact]
    public void TrySetActive_ValidKey_ShouldSwitch()
    {
        var resolver = new StrategyResolver();
        resolver.Register("a", CreateMockStrategy("a", "Strategy A"));
        resolver.Register("b", CreateMockStrategy("b", "Strategy B"));

        var result = resolver.TrySetActive("b");

        result.Should().BeTrue();
        resolver.ActiveKey.Should().Be("b");
    }

    [Fact]
    public void TrySetActive_InvalidKey_ShouldReturnFalse()
    {
        var resolver = new StrategyResolver();
        resolver.Register("a", CreateMockStrategy("a", "Strategy A"));

        var result = resolver.TrySetActive("nonexistent");

        result.Should().BeFalse();
        resolver.ActiveKey.Should().Be("a");
    }

    [Fact]
    public void GetAvailable_ShouldReturnAll()
    {
        var resolver = new StrategyResolver();
        resolver.Register("a", CreateMockStrategy("a", "Desc A"));
        resolver.Register("b", CreateMockStrategy("b", "Desc B"));

        var available = resolver.GetAvailable();

        available.Should().HaveCount(2);
        available["a"].Should().Be("Desc A");
        available["b"].Should().Be("Desc B");
    }

    [Fact]
    public void CurrentStrategy_NoRegistration_ShouldThrow()
    {
        var resolver = new StrategyResolver();

        var act = () => resolver.CurrentStrategy;

        act.Should().Throw<InvalidOperationException>();
    }

    private static ITradingStrategy CreateMockStrategy(string name, string description)
    {
        var strategy = Substitute.For<ITradingStrategy>();
        strategy.Name.Returns(name);
        strategy.Description.Returns(description);
        return strategy;
    }
}
