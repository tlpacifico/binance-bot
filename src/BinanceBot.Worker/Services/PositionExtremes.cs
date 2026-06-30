using System.Text.Json;

namespace BinanceBot.Worker.Services;

/// <summary>
/// Tracks the lowest and highest observed price since the last executed trade.
/// Persisted in BotState.StrategyStateJson; consumed by the Pacific trailing-escape logic.
/// </summary>
public sealed record PositionExtremes(decimal LowSinceTrade, decimal HighSinceTrade)
{
    public static PositionExtremes Initial(decimal price) => new(price, price);

    public PositionExtremes Observe(decimal price) =>
        new(Math.Min(LowSinceTrade, price), Math.Max(HighSinceTrade, price));

    public string ToJson() => JsonSerializer.Serialize(this);

    public static PositionExtremes? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<PositionExtremes>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
