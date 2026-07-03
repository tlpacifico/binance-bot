namespace BinanceBot.Strategies.Pacific;

public sealed class PacificSettings
{
    public const string Section = "Strategy:Pacific";

    public decimal SellThresholdPct { get; set; } = 0.025m;
    public decimal BuyThresholdPct { get; set; } = 0.025m;
    public int ConfirmationTicks { get; set; } = 10;
    public int CheckIntervalSeconds { get; set; } = 30;
    public decimal MinTradeEur { get; set; } = 10m;

    // Trailing-escape (Pacific v2)
    public decimal EscapeDrawdownPct { get; set; } = 0.04m;
    public decimal EscapeRecoveryPct { get; set; } = 0.025m;
    public decimal HardStopLossPct { get; set; } = 0m; // 0 = disabled (BTC side only)
}
