using Binance.Net;
using Binance.Net.Clients;
using Binance.Net.Enums;
using Binance.Net.Objects;
using BinanceBot.Core.Configuration;
using BinanceBot.Core.Enums;
using BinanceBot.Core.Interfaces;
using BinanceBot.Core.Models;
using CryptoExchange.Net.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BinanceBot.Infrastructure.Binance;

public sealed class BinanceClientAdapter : IBinanceClient, IDisposable
{
    private readonly BinanceRestClient _client;
    private readonly string _symbol;
    private readonly ILogger<BinanceClientAdapter> _logger;

    public BinanceClientAdapter(IOptions<BinanceSettings> settings, ILogger<BinanceClientAdapter> logger)
    {
        _logger = logger;
        var cfg = settings.Value;
        _symbol = cfg.TradingPair;

        _client = new BinanceRestClient(options =>
        {
            options.ApiCredentials = new ApiCredentials(cfg.ApiKey, cfg.ApiSecret);
            if (cfg.UseTestnet)
            {
                options.Environment = BinanceEnvironment.Testnet;
            }
        });
    }

    public async Task<PriceData> GetPriceAsync(string symbol, CancellationToken ct = default)
    {
        var ticker = await _client.SpotApi.ExchangeData.GetTickerAsync(symbol, ct);
        if (!ticker.Success)
            throw new InvalidOperationException($"Failed to get price: {ticker.Error?.Message}");

        return new PriceData(ticker.Data.LastPrice, ticker.Data.LowPrice, ticker.Data.HighPrice);
    }

    public async Task<(decimal Btc, decimal Eur)> GetBalancesAsync(CancellationToken ct = default)
    {
        var account = await _client.SpotApi.Account.GetAccountInfoAsync(ct: ct);
        if (!account.Success)
            throw new InvalidOperationException($"Failed to get balances: {account.Error?.Message}");

        var btc = account.Data.Balances.FirstOrDefault(b => b.Asset == "BTC");
        var eur = account.Data.Balances.FirstOrDefault(b => b.Asset == "EUR");

        return (btc?.Available ?? 0, eur?.Available ?? 0);
    }

    public async Task<TradeRecord> MarketBuyAsync(string symbol, decimal quoteAmountEur, CancellationToken ct = default)
    {
        quoteAmountEur = TruncateDecimal(quoteAmountEur, QuotePrecision);
        _logger.LogInformation("Placing market BUY order: {Amount} EUR on {Symbol}", quoteAmountEur, symbol);

        var order = await _client.SpotApi.Trading.PlaceOrderAsync(
            symbol,
            OrderSide.Buy,
            SpotOrderType.Market,
            quoteQuantity: quoteAmountEur,
            ct: ct);

        if (!order.Success)
            throw new InvalidOperationException($"Market BUY failed: {order.Error?.Message}");

        var data = order.Data;
        var avgPrice = data.QuoteQuantityFilled > 0 && data.QuantityFilled > 0
            ? data.QuoteQuantityFilled / data.QuantityFilled
            : 0;

        return new TradeRecord
        {
            Timestamp = data.CreateTime,
            Side = TradeSide.Buy,
            Price = avgPrice,
            QuantityBtc = data.QuantityFilled,
            QuoteAmountEur = data.QuoteQuantityFilled,
            Fee = 0, // Binance fees are deducted from received asset
            StrategyName = string.Empty,
            Reason = string.Empty
        };
    }

    public async Task<TradeRecord> MarketSellAsync(string symbol, decimal quantityBtc, CancellationToken ct = default)
    {
        quantityBtc = TruncateDecimal(quantityBtc, BasePrecision);
        _logger.LogInformation("Placing market SELL order: {Quantity} BTC on {Symbol}", quantityBtc, symbol);

        var order = await _client.SpotApi.Trading.PlaceOrderAsync(
            symbol,
            OrderSide.Sell,
            SpotOrderType.Market,
            quantity: quantityBtc,
            ct: ct);

        if (!order.Success)
            throw new InvalidOperationException($"Market SELL failed: {order.Error?.Message}");

        var data = order.Data;
        var avgPrice = data.QuoteQuantityFilled > 0 && data.QuantityFilled > 0
            ? data.QuoteQuantityFilled / data.QuantityFilled
            : 0;

        return new TradeRecord
        {
            Timestamp = data.CreateTime,
            Side = TradeSide.Sell,
            Price = avgPrice,
            QuantityBtc = data.QuantityFilled,
            QuoteAmountEur = data.QuoteQuantityFilled,
            Fee = 0,
            StrategyName = string.Empty,
            Reason = string.Empty
        };
    }

    public void Dispose() => _client.Dispose();

    // BTCEUR: base asset (BTC) allows 5 decimals, quote asset (EUR) allows 2
    private const int BasePrecision = 5;
    private const int QuotePrecision = 2;

    private static decimal TruncateDecimal(decimal value, int decimals)
    {
        var factor = (decimal)Math.Pow(10, decimals);
        return Math.Floor(value * factor) / factor;
    }
}
