namespace BinanceBot.Worker.Services;

/// <summary>
/// Read-only projection of the Pacific strategy's decision state for the dashboard.
/// Mirrors PacificCalculator's mode logic so the dashboard shows exactly what the
/// engine would do. Display-only: it never drives trades.
/// </summary>
public sealed record PacificView(
    bool HoldingBtc,
    string Mode,
    decimal LastTradePrice,
    decimal ProfitTarget,
    decimal EscapeArmPrice,
    decimal? EscapeTarget,
    decimal ActiveTarget,
    decimal LowSinceTrade,
    decimal HighSinceTrade,
    decimal MoveFromLastTradePct)
{
    public const string ModeNormal = "normal";
    public const string ModeEscapeArmed = "escape-armed";
    public const string ModeHardStop = "hard-stop";

    public static PacificView? Compute(
        bool holdingBtc,
        decimal lastTradePrice,
        decimal currentPrice,
        decimal lowSinceTrade,
        decimal highSinceTrade,
        decimal sellThresholdPct,
        decimal buyThresholdPct,
        decimal escapeDrawdownPct,
        decimal escapeRecoveryPct,
        decimal hardStopLossPct)
    {
        if (lastTradePrice <= 0) return null;

        var movePct = (currentPrice - lastTradePrice) / lastTradePrice;

        if (holdingBtc)
        {
            var profitTarget = lastTradePrice * (1 + sellThresholdPct);
            var escapeArmPrice = lastTradePrice * (1 - escapeDrawdownPct);
            var drawdown = (lastTradePrice - currentPrice) / lastTradePrice;       // current, for hard-stop
            var maxDrawdown = (lastTradePrice - lowSinceTrade) / lastTradePrice;   // latched, arms the escape

            string mode;
            decimal? escapeTarget = null;
            if (hardStopLossPct > 0 && drawdown >= hardStopLossPct)
            {
                mode = ModeHardStop;
            }
            else if (maxDrawdown >= escapeDrawdownPct)
            {
                mode = ModeEscapeArmed;
                escapeTarget = lowSinceTrade * (1 + escapeRecoveryPct);
            }
            else
            {
                mode = ModeNormal;
            }

            return new PacificView(true, mode, lastTradePrice, profitTarget,
                escapeArmPrice, escapeTarget, escapeTarget ?? profitTarget,
                lowSinceTrade, highSinceTrade, movePct);
        }
        else
        {
            var profitTarget = lastTradePrice * (1 - buyThresholdPct);
            var escapeArmPrice = lastTradePrice * (1 + escapeDrawdownPct);
            var maxRunup = (highSinceTrade - lastTradePrice) / lastTradePrice;   // latched, arms the escape

            string mode;
            decimal? escapeTarget = null;
            if (maxRunup >= escapeDrawdownPct)
            {
                mode = ModeEscapeArmed;
                escapeTarget = highSinceTrade * (1 - escapeRecoveryPct);
            }
            else
            {
                mode = ModeNormal;
            }

            return new PacificView(false, mode, lastTradePrice, profitTarget,
                escapeArmPrice, escapeTarget, escapeTarget ?? profitTarget,
                lowSinceTrade, highSinceTrade, movePct);
        }
    }
}
