namespace BinanceBot.Strategies.DcaRebalancing;

public sealed class DcaRebalancingSettings
{
    public const string Section = "Strategy:DcaRebalancing";

    public decimal TargetBtcAllocationPct { get; set; } = 60m;
    public decimal DeviationThresholdPct { get; set; } = 5m;
    public int PeriodicRebalanceIntervalDays { get; set; } = 30;
    public int CheckIntervalMinutes { get; set; } = 60;
    public decimal MinTradeEur { get; set; } = 10m;
}
