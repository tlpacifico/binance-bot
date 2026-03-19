namespace BinanceBot.Core.Configuration;

public sealed class TelegramSettings
{
    public const string Section = "Telegram";

    public string BotToken { get; set; } = string.Empty;
    public long ChatId { get; set; }
}
