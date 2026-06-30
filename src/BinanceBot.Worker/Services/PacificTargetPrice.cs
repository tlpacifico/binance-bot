namespace BinanceBot.Worker.Services;

/// <summary>
/// Computes the price the dashboard should display as the active Pacific target,
/// mirroring PacificCalculator: the profit target normally, or the trailing-escape
/// target once the position is in the escape zone.
/// Remarks: this intentionally ignores <c>HardStopLossPct</c> — it only reflects the
/// profit/trailing-escape target, so if a hard stop is enabled the engine may sell
/// below the price shown here.
/// </summary>
public static class PacificTargetPrice
{
    public static decimal? Compute(
        bool holdingBtc,
        decimal lastTradePrice,
        decimal currentPrice,
        decimal lowSinceTrade,
        decimal highSinceTrade,
        decimal sellThresholdPct,
        decimal buyThresholdPct,
        decimal escapeDrawdownPct,
        decimal escapeRecoveryPct)
    {
        if (lastTradePrice <= 0) return null;

        if (holdingBtc)
        {
            var drawdown = (lastTradePrice - currentPrice) / lastTradePrice;
            if (drawdown >= escapeDrawdownPct && lowSinceTrade > 0)
                return lowSinceTrade * (1 + escapeRecoveryPct);
            return lastTradePrice * (1 + sellThresholdPct);
        }
        else
        {
            var runup = (currentPrice - lastTradePrice) / lastTradePrice;
            if (runup >= escapeDrawdownPct && highSinceTrade > 0)
                return highSinceTrade * (1 - escapeRecoveryPct);
            return lastTradePrice * (1 - buyThresholdPct);
        }
    }
}
