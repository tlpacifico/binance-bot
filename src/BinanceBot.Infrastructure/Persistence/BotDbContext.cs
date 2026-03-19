using BinanceBot.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Infrastructure.Persistence;

public class BotDbContext : DbContext
{
    public DbSet<TradeEntity> Trades => Set<TradeEntity>();
    public DbSet<BotStateEntity> BotState => Set<BotStateEntity>();

    public BotDbContext(DbContextOptions<BotDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TradeEntity>(entity =>
        {
            entity.ToTable("Trades");
            entity.HasIndex(e => e.Timestamp);
            entity.Property(e => e.Price).HasColumnType("decimal(18,8)");
            entity.Property(e => e.QuantityBtc).HasColumnType("decimal(18,8)");
            entity.Property(e => e.QuoteAmountEur).HasColumnType("decimal(18,8)");
            entity.Property(e => e.Fee).HasColumnType("decimal(18,8)");
        });

        modelBuilder.Entity<BotStateEntity>(entity =>
        {
            entity.ToTable("BotState");
            entity.Property(e => e.BtcBalance).HasColumnType("decimal(18,8)");
            entity.Property(e => e.EurBalance).HasColumnType("decimal(18,8)");
            entity.Property(e => e.InitialBalanceEur).HasColumnType("decimal(18,8)");
            entity.Property(e => e.LastTradePrice).HasColumnType("decimal(18,8)");
        });
    }
}
