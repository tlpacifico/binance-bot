using BinanceBot.Core.Models;

namespace BinanceBot.Core.Interfaces;

public interface IBinanceClient
{
    Task<PriceData> GetPriceAsync(string symbol, CancellationToken ct = default);
    Task<(decimal Btc, decimal Eur)> GetBalancesAsync(CancellationToken ct = default);
    Task<TradeRecord> MarketBuyAsync(string symbol, decimal quoteAmountEur, CancellationToken ct = default);
    Task<TradeRecord> MarketSellAsync(string symbol, decimal quantityBtc, CancellationToken ct = default);
}
