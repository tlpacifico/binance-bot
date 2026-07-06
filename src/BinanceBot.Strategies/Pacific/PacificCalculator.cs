using BinanceBot.Core.Models;

namespace BinanceBot.Strategies.Pacific;

public static class PacificCalculator
{
    public static TradeDecision Evaluate(
        decimal currentPrice,
        Portfolio portfolio,
        decimal lastTradePrice,
        decimal highSinceTrade,
        decimal sellThresholdPct,
        decimal buyThresholdPct,
        decimal escapeDrawdownPct,
        decimal escapeRecoveryPct,
        decimal minTradeEur)
    {
        if (portfolio.TotalValueEur <= 0)
            return TradeDecision.Hold("Portfolio value is zero");

        var btcValueEur = portfolio.BtcBalance * currentPrice;
        var holdingBtc = btcValueEur > portfolio.EurBalance;

        return holdingBtc
            ? EvaluateSell(currentPrice, portfolio, lastTradePrice, sellThresholdPct, minTradeEur)
            : EvaluateBuy(currentPrice, portfolio, lastTradePrice, highSinceTrade,
                buyThresholdPct, escapeDrawdownPct, escapeRecoveryPct, minTradeEur);
    }

    private static TradeDecision EvaluateSell(
        decimal currentPrice,
        Portfolio portfolio,
        decimal lastTradePrice,
        decimal sellThresholdPct,
        decimal minTradeEur)
    {
        var sellValueEur = portfolio.BtcBalance * currentPrice;

        TradeDecision SellAll(string reason) =>
            sellValueEur < minTradeEur
                ? TradeDecision.Hold($"Sell value €{sellValueEur:N2} below minimum €{minTradeEur:N2}")
                : TradeDecision.Sell(portfolio.BtcBalance, reason);

        // Only ever sell at a profit — never below the purchase price. No escape, no hard stop.
        var profitTarget = lastTradePrice * (1 + sellThresholdPct);
        if (currentPrice >= profitTarget)
            return SellAll($"Sell all BTC: price €{currentPrice:N2} >= profit target €{profitTarget:N2} (normal)");

        return TradeDecision.Hold($"Price €{currentPrice:N2} below profit target €{profitTarget:N2} (normal)");
    }

    private static TradeDecision EvaluateBuy(
        decimal currentPrice,
        Portfolio portfolio,
        decimal lastTradePrice,
        decimal highSinceTrade,
        decimal buyThresholdPct,
        decimal escapeDrawdownPct,
        decimal escapeRecoveryPct,
        decimal minTradeEur)
    {
        TradeDecision BuyAll(string reason) =>
            portfolio.EurBalance < minTradeEur
                ? TradeDecision.Hold($"EUR balance €{portfolio.EurBalance:N2} below minimum €{minTradeEur:N2}")
                : TradeDecision.Buy(portfolio.EurBalance, reason);

        // 1. Profit target (preferred)
        var profitTarget = lastTradePrice * (1 - buyThresholdPct);
        if (currentPrice <= profitTarget)
            return BuyAll($"Buy all EUR: price €{currentPrice:N2} <= profit target €{profitTarget:N2} (normal)");

        // 2. Trailing escape (no hard stop on EUR side — run-up is opportunity cost, not loss).
        // LATCHED by the high since the trade (not the current price), so the escape stays armed
        // while price falls back and reliably fires on the pullback.
        var maxRunup = lastTradePrice > 0 ? (highSinceTrade - lastTradePrice) / lastTradePrice : 0m;
        if (maxRunup >= escapeDrawdownPct)
        {
            var escapeTarget = highSinceTrade * (1 - escapeRecoveryPct);
            return currentPrice <= escapeTarget
                ? BuyAll($"Buy all EUR: price €{currentPrice:N2} <= escape target €{escapeTarget:N2} (trailing-escape)")
                : TradeDecision.Hold($"Escape armed: price €{currentPrice:N2} above escape target €{escapeTarget:N2} (trailing-escape)");
        }

        return TradeDecision.Hold($"Price €{currentPrice:N2} above profit target €{profitTarget:N2} (normal)");
    }
}
