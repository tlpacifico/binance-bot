namespace BinanceBot.Core.Configuration;

public sealed class BinanceSettings
{
    public const string Section = "Binance";

    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;
    public bool UseTestnet { get; set; } = true;
    public string TradingPair { get; set; } = "BTCEUR";
    public decimal InitialBalanceEur { get; set; } = 100m;
}
