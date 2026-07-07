using BinanceBot.Core.Enums;
using BinanceBot.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace BinanceBot.Infrastructure.Telegram.Commands;

public sealed class DepositCommand : ITelegramCommand
{
    private readonly IServiceScopeFactory _scopeFactory;

    public string Name => "/deposit";
    public string Description => "Registrar aporte de capital (EUR)";

    public DepositCommand(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public async Task<string> ExecuteAsync(string[] args, CancellationToken ct = default)
    {
        if (!CashFlowArgs.TryParseAmount(args, out var amount))
            return "❌ Uso: /deposit <valor em EUR maior que 0>";

        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ICashFlowRepository>();
        var result = await repo.ApplyAsync(CashFlowType.Deposit, amount, ct);

        return $"✅ Depósito de €{amount:N2} registrado.\n"
             + $"Capital aportado: €{result.OldBaseline:N2} → €{result.NewBaseline:N2}";
    }
}
