using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BinanceBot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BotState",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ActiveStrategy = table.Column<string>(type: "text", nullable: false),
                    BtcBalance = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                    EurBalance = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                    InitialBalanceEur = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                    LastTradePrice = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                    LastRebalanceTimestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RunState = table.Column<string>(type: "text", nullable: false),
                    StrategyStateJson = table.Column<string>(type: "text", nullable: true),
                    Last24hLowPrice = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                    Last24hHighPrice = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                    Last24hPriceTimestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BotState", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Trades",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Side = table.Column<string>(type: "text", nullable: false),
                    Price = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                    QuantityBtc = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                    QuoteAmountEur = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                    Fee = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                    StrategyName = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Trades", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Trades_Timestamp",
                table: "Trades",
                column: "Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BotState");

            migrationBuilder.DropTable(
                name: "Trades");
        }
    }
}
