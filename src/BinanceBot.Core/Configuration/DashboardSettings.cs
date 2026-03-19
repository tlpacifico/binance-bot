namespace BinanceBot.Core.Configuration;

public sealed class DashboardSettings
{
    public const string Section = "Dashboard";

    public int Port { get; set; } = 3000;
    public string AuthToken { get; set; } = string.Empty;
}
