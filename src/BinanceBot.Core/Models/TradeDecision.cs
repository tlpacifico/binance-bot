using BinanceBot.Core.Enums;

namespace BinanceBot.Core.Models;

public sealed record TradeDecision
{
    public TradeAction Action { get; init; } = TradeAction.Hold;
    public decimal QuantityBtc { get; init; }
    public decimal AmountEur { get; init; }
    public string Reason { get; init; } = string.Empty;

    public static TradeDecision Hold(string reason = "No action needed") =>
        new() { Action = TradeAction.Hold, Reason = reason };

    public static TradeDecision Buy(decimal amountEur, string reason) =>
        new() { Action = TradeAction.Buy, AmountEur = amountEur, Reason = reason };

    public static TradeDecision Sell(decimal quantityBtc, string reason) =>
        new() { Action = TradeAction.Sell, QuantityBtc = quantityBtc, Reason = reason };
}
