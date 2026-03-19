using BinanceBot.Core.Models;

namespace BinanceBot.Strategies.Pacific;

public static class PacificCalculator
{
    public static TradeDecision Evaluate(
        decimal currentPrice,
        Portfolio portfolio,
        decimal lastTradePrice,
        decimal low24H,
        decimal high24H,
        decimal sellThresholdPct,
        decimal buyThresholdPct,
        bool isStale,
        decimal minTradeEur)
    {
        if (portfolio.TotalValueEur <= 0)
            return TradeDecision.Hold("Portfolio value is zero");

        var btcValueEur = portfolio.BtcBalance * currentPrice;
        var holdingBtc = btcValueEur > portfolio.EurBalance;

        if (holdingBtc)
            return EvaluateSell(currentPrice, portfolio, lastTradePrice, high24H, sellThresholdPct, isStale, minTradeEur);

        return EvaluateBuy(currentPrice, portfolio, lastTradePrice, low24H, buyThresholdPct, isStale, minTradeEur);
    }

    private static TradeDecision EvaluateSell(
        decimal currentPrice,
        Portfolio portfolio,
        decimal lastTradePrice,
        decimal high24H,
        decimal sellThresholdPct,
        bool isStale,
        decimal minTradeEur)
    {
        decimal targetSellPrice;
        string mode;

        if (isStale && high24H > 0)
        {
            targetSellPrice = high24H * (1 + sellThresholdPct);
            mode = "stale-24h-high";
        }
        else
        {
            targetSellPrice = lastTradePrice * (1 + sellThresholdPct);
            mode = "normal";
        }

        if (currentPrice < targetSellPrice)
            return TradeDecision.Hold($"Price €{currentPrice:N2} below sell target €{targetSellPrice:N2} ({mode})");

        var sellValueEur = portfolio.BtcBalance * currentPrice;
        if (sellValueEur < minTradeEur)
            return TradeDecision.Hold($"Sell value €{sellValueEur:N2} below minimum €{minTradeEur:N2}");

        return TradeDecision.Sell(
            portfolio.BtcBalance,
            $"Sell all BTC: price €{currentPrice:N2} >= target €{targetSellPrice:N2} ({mode})");
    }

    private static TradeDecision EvaluateBuy(
        decimal currentPrice,
        Portfolio portfolio,
        decimal lastTradePrice,
        decimal low24H,
        decimal buyThresholdPct,
        bool isStale,
        decimal minTradeEur)
    {
        decimal targetBuyPrice;
        string mode;

        if (isStale && low24H > 0)
        {
            targetBuyPrice = low24H * (1 - buyThresholdPct);
            mode = "stale-24h-low";
        }
        else
        {
            targetBuyPrice = lastTradePrice * (1 - buyThresholdPct);
            mode = "normal";
        }

        if (currentPrice > targetBuyPrice)
            return TradeDecision.Hold($"Price €{currentPrice:N2} above buy target €{targetBuyPrice:N2} ({mode})");

        if (portfolio.EurBalance < minTradeEur)
            return TradeDecision.Hold($"EUR balance €{portfolio.EurBalance:N2} below minimum €{minTradeEur:N2}");

        return TradeDecision.Buy(
            portfolio.EurBalance,
            $"Buy all EUR: price €{currentPrice:N2} <= target €{targetBuyPrice:N2} ({mode})");
    }
}
