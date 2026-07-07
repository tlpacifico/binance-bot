using System.Globalization;

namespace BinanceBot.Infrastructure.Telegram.Commands;

internal static class CashFlowArgs
{
    /// <summary>Parses args[0] as a positive EUR amount (accepts ',' or '.' as the decimal separator).</summary>
    public static bool TryParseAmount(string[] args, out decimal amount)
    {
        amount = 0m;
        if (args.Length == 0) return false;
        var raw = args[0].Replace(',', '.');
        return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out amount) && amount > 0m;
    }
}
