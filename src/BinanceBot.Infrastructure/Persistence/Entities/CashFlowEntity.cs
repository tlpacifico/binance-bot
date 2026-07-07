namespace BinanceBot.Infrastructure.Persistence.Entities;

public class CashFlowEntity
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string Type { get; set; } = string.Empty;
    public decimal AmountEur { get; set; }
    public decimal BalanceAfter { get; set; }
}
