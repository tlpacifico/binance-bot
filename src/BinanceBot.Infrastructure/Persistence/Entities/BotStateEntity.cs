using System.ComponentModel.DataAnnotations;

namespace BinanceBot.Infrastructure.Persistence.Entities;

public class BotStateEntity
{
    [Key]
    public int Id { get; set; } = 1;
    public string ActiveStrategy { get; set; } = string.Empty;
    public decimal BtcBalance { get; set; }
    public decimal EurBalance { get; set; }
    public decimal InitialBalanceEur { get; set; }
    public decimal LastTradePrice { get; set; }
    public DateTime? LastRebalanceTimestamp { get; set; }
    public string RunState { get; set; } = "Running";
    public string? StrategyStateJson { get; set; }
}
