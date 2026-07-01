namespace BinanceBot.Worker;

/// <summary>
/// Runs a startup action with bounded retries and backoff. Used to tolerate a
/// database that is not yet reachable when the container boots, so a transient
/// blip does not crash-loop the host and take the dashboard down with it.
/// </summary>
public static class StartupRetry
{
    public static void Run(
        Action action,
        int maxAttempts,
        Func<int, TimeSpan> backoff,
        Action<Exception, int, TimeSpan> onRetry,
        Action<TimeSpan>? sleep = null)
    {
        sleep ??= Thread.Sleep;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                var delay = backoff(attempt);
                onRetry(ex, attempt, delay);
                sleep(delay);
            }
        }
    }
}
