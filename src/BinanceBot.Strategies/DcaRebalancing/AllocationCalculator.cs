using BinanceBot.Core.Models;

namespace BinanceBot.Strategies.DcaRebalancing;

public static class AllocationCalculator
{
    public static TradeDecision Calculate(
        Portfolio portfolio,
        decimal targetBtcPct,
        decimal deviationThresholdPct,
        decimal minTradeEur,
        bool periodicTrigger)
    {
        if (portfolio.TotalValueEur <= 0)
            return TradeDecision.Hold("Portfolio value is zero");

        var currentBtcPct = portfolio.BtcAllocationPct;
        var deviation = Math.Abs(currentBtcPct - targetBtcPct);

        var shouldRebalance = deviation > deviationThresholdPct || periodicTrigger;
        if (!shouldRebalance)
            return TradeDecision.Hold($"Deviation {deviation:N1}% within threshold {deviationThresholdPct:N1}%");

        var targetEurPct = 100m - targetBtcPct;

        if (currentBtcPct > targetBtcPct)
        {
            // BTC overrepresented → SELL BTC
            var excessPct = currentBtcPct - targetBtcPct;
            var sellValueEur = excessPct / 100m * portfolio.TotalValueEur;

            if (sellValueEur < minTradeEur)
                return TradeDecision.Hold($"Sell amount €{sellValueEur:N2} below minimum €{minTradeEur:N2}");

            var sellQtyBtc = portfolio.CurrentBtcPrice > 0
                ? sellValueEur / portfolio.CurrentBtcPrice
                : 0;

            var reason = periodicTrigger && deviation <= deviationThresholdPct
                ? $"Periodic rebalance: selling {excessPct:N1}% excess BTC"
                : $"Rebalance: BTC at {currentBtcPct:N1}% (target {targetBtcPct:N1}%), selling {excessPct:N1}% excess";

            return TradeDecision.Sell(sellQtyBtc, reason);
        }
        else
        {
            // BTC underrepresented → BUY BTC
            var deficitPct = targetBtcPct - currentBtcPct;
            var buyAmountEur = deficitPct / 100m * portfolio.TotalValueEur;

            if (buyAmountEur < minTradeEur)
                return TradeDecision.Hold($"Buy amount €{buyAmountEur:N2} below minimum €{minTradeEur:N2}");

            // Don't buy more than available EUR
            buyAmountEur = Math.Min(buyAmountEur, portfolio.EurBalance);

            var reason = periodicTrigger && deviation <= deviationThresholdPct
                ? $"Periodic rebalance: buying {deficitPct:N1}% deficit BTC"
                : $"Rebalance: BTC at {currentBtcPct:N1}% (target {targetBtcPct:N1}%), buying {deficitPct:N1}% deficit";

            return TradeDecision.Buy(buyAmountEur, reason);
        }
    }
}
