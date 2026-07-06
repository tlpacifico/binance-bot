namespace BinanceBot.Worker.Services;

/// <summary>
/// Read-only projection of the Pacific strategy's decision state for the dashboard.
/// Mirrors PacificCalculator: the sell side (holding BTC) only ever sells at the profit
/// target — never below buy — so it has no escape and is always Normal. The buy side keeps
/// the latched trailing escape. Display-only: it never drives trades.
/// </summary>
public sealed record PacificView(
    bool HoldingBtc,
    string Mode,
    decimal LastTradePrice,
    decimal ProfitTarget,
    decimal? EscapeArmPrice,
    decimal? EscapeTarget,
    decimal ActiveTarget,
    decimal LowSinceTrade,
    decimal HighSinceTrade,
    decimal MoveFromLastTradePct)
{
    public const string ModeNormal = "normal";
    public const string ModeEscapeArmed = "escape-armed";

    public static PacificView? Compute(
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

        var movePct = (currentPrice - lastTradePrice) / lastTradePrice;

        if (holdingBtc)
        {
            // Sell side: profit target only, never below buy → always Normal, no escape.
            var profitTarget = lastTradePrice * (1 + sellThresholdPct);
            return new PacificView(true, ModeNormal, lastTradePrice, profitTarget,
                null, null, profitTarget,
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
