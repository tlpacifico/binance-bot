using BinanceBot.Core.Models;

namespace BinanceBot.Strategies.Pacific;

public static class PacificCalculator
{
    public static TradeDecision Evaluate(
        decimal currentPrice,
        Portfolio portfolio,
        decimal lastTradePrice,
        decimal lowSinceTrade,
        decimal highSinceTrade,
        decimal sellThresholdPct,
        decimal buyThresholdPct,
        decimal escapeDrawdownPct,
        decimal escapeRecoveryPct,
        decimal hardStopLossPct,
        decimal minTradeEur)
    {
        if (portfolio.TotalValueEur <= 0)
            return TradeDecision.Hold("Portfolio value is zero");

        var btcValueEur = portfolio.BtcBalance * currentPrice;
        var holdingBtc = btcValueEur > portfolio.EurBalance;

        return holdingBtc
            ? EvaluateSell(currentPrice, portfolio, lastTradePrice, lowSinceTrade,
                sellThresholdPct, escapeDrawdownPct, escapeRecoveryPct, hardStopLossPct, minTradeEur)
            : EvaluateBuy(currentPrice, portfolio, lastTradePrice, highSinceTrade,
                buyThresholdPct, escapeDrawdownPct, escapeRecoveryPct, minTradeEur);
    }

    private static TradeDecision EvaluateSell(
        decimal currentPrice,
        Portfolio portfolio,
        decimal lastTradePrice,
        decimal lowSinceTrade,
        decimal sellThresholdPct,
        decimal escapeDrawdownPct,
        decimal escapeRecoveryPct,
        decimal hardStopLossPct,
        decimal minTradeEur)
    {
        var sellValueEur = portfolio.BtcBalance * currentPrice;

        TradeDecision SellAll(string reason) =>
            sellValueEur < minTradeEur
                ? TradeDecision.Hold($"Sell value €{sellValueEur:N2} below minimum €{minTradeEur:N2}")
                : TradeDecision.Sell(portfolio.BtcBalance, reason);

        // 1. Profit target (preferred)
        var profitTarget = lastTradePrice * (1 + sellThresholdPct);
        if (currentPrice >= profitTarget)
            return SellAll($"Sell all BTC: price €{currentPrice:N2} >= profit target €{profitTarget:N2} (normal)");

        var drawdown = lastTradePrice > 0 ? (lastTradePrice - currentPrice) / lastTradePrice : 0m;

        // 2. Hard stop-loss (if enabled)
        if (hardStopLossPct > 0 && drawdown >= hardStopLossPct)
            return SellAll($"Sell all BTC: drawdown {drawdown:P1} >= hard stop {hardStopLossPct:P1} (hard-stop)");

        // 3. Trailing escape
        if (drawdown >= escapeDrawdownPct)
        {
            var escapeTarget = lowSinceTrade * (1 + escapeRecoveryPct);
            return currentPrice >= escapeTarget
                ? SellAll($"Sell all BTC: price €{currentPrice:N2} >= escape target €{escapeTarget:N2} (trailing-escape)")
                : TradeDecision.Hold($"Escape armed: price €{currentPrice:N2} below escape target €{escapeTarget:N2} (trailing-escape)");
        }

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

        var runup = lastTradePrice > 0 ? (currentPrice - lastTradePrice) / lastTradePrice : 0m;

        // 2. Trailing escape (no hard stop on EUR side — run-up is opportunity cost, not loss)
        if (runup >= escapeDrawdownPct)
        {
            var escapeTarget = highSinceTrade * (1 - escapeRecoveryPct);
            return currentPrice <= escapeTarget
                ? BuyAll($"Buy all EUR: price €{currentPrice:N2} <= escape target €{escapeTarget:N2} (trailing-escape)")
                : TradeDecision.Hold($"Escape armed: price €{currentPrice:N2} above escape target €{escapeTarget:N2} (trailing-escape)");
        }

        return TradeDecision.Hold($"Price €{currentPrice:N2} above profit target €{profitTarget:N2} (normal)");
    }
}
