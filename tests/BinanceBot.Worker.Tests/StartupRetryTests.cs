using BinanceBot.Worker;
using FluentAssertions;

namespace BinanceBot.Worker.Tests;

public class StartupRetryTests
{
    private static readonly Func<int, TimeSpan> NoBackoff = _ => TimeSpan.Zero;

    [Fact]
    public void SucceedsFirstTry_RunsOnce_NoRetry()
    {
        var calls = 0;
        var retries = 0;
        var sleeps = 0;

        StartupRetry.Run(
            () => calls++,
            maxAttempts: 5,
            backoff: NoBackoff,
            onRetry: (_, _, _) => retries++,
            sleep: _ => sleeps++);

        calls.Should().Be(1);
        retries.Should().Be(0);
        sleeps.Should().Be(0);
    }

    [Fact]
    public void ThrowsTwiceThenSucceeds_RetriesThenSucceeds()
    {
        var calls = 0;
        var retries = 0;
        var sleeps = 0;

        StartupRetry.Run(
            () =>
            {
                calls++;
                if (calls < 3) throw new InvalidOperationException("db not ready");
            },
            maxAttempts: 5,
            backoff: NoBackoff,
            onRetry: (_, _, _) => retries++,
            sleep: _ => sleeps++);

        calls.Should().Be(3);
        retries.Should().Be(2);
        sleeps.Should().Be(2);
    }

    [Fact]
    public void AlwaysThrows_RethrowsAfterMaxAttempts()
    {
        var calls = 0;
        var retries = 0;

        var act = () => StartupRetry.Run(
            () => { calls++; throw new InvalidOperationException("db down"); },
            maxAttempts: 4,
            backoff: NoBackoff,
            onRetry: (_, _, _) => retries++,
            sleep: _ => { });

        act.Should().Throw<InvalidOperationException>().WithMessage("db down");
        calls.Should().Be(4);       // one call per attempt
        retries.Should().Be(3);     // onRetry fires on every attempt except the last
    }

    [Fact]
    public void PassesAttemptNumberAndDelayToCallbacks()
    {
        var seenAttempts = new List<int>();
        var seenDelays = new List<TimeSpan>();

        StartupRetry.Run(
            () =>
            {
                if (seenAttempts.Count < 2) throw new Exception("retry");
            },
            maxAttempts: 5,
            backoff: attempt => TimeSpan.FromSeconds(attempt),
            onRetry: (_, attempt, delay) => { seenAttempts.Add(attempt); seenDelays.Add(delay); },
            sleep: _ => { });

        seenAttempts.Should().Equal(1, 2);
        seenDelays.Should().Equal(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
    }
}
