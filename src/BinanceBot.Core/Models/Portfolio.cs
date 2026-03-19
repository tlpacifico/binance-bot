namespace BinanceBot.Core.Models;

public sealed record Portfolio
{
    public decimal BtcBalance { get; init; }
    public decimal EurBalance { get; init; }
    public decimal CurrentBtcPrice { get; init; }

    public decimal TotalValueEur => (BtcBalance * CurrentBtcPrice) + EurBalance;

    public decimal BtcAllocationPct =>
        TotalValueEur > 0
            ? (BtcBalance * CurrentBtcPrice) / TotalValueEur * 100m
            : 0m;

    public decimal EurAllocationPct =>
        TotalValueEur > 0
            ? EurBalance / TotalValueEur * 100m
            : 0m;
}
