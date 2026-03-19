using System.ComponentModel.DataAnnotations;

namespace BinanceBot.Infrastructure.Persistence.Entities;

public class TradeEntity
{
    [Key]
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string Side { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal QuantityBtc { get; set; }
    public decimal QuoteAmountEur { get; set; }
    public decimal Fee { get; set; }
    public string StrategyName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
