using BinanceBot.Core.Enums;
using BinanceBot.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace BinanceBot.Infrastructure.Telegram.Commands;

public sealed class WithdrawCommand : ITelegramCommand
{
    private readonly IServiceScopeFactory _scopeFactory;

    public string Name => "/withdraw";
    public string Description => "Registrar saque de capital (EUR)";

    public WithdrawCommand(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public async Task<string> ExecuteAsync(string[] args, CancellationToken ct = default)
    {
        if (!CashFlowArgs.TryParseAmount(args, out var amount))
            return "❌ Uso: /withdraw <valor em EUR maior que 0>";

        using var scope = _scopeFactory.CreateScope();
        var stateRepo = scope.ServiceProvider.GetRequiredService<IStateRepository>();
        var state = await stateRepo.GetAsync(ct);
        if (state is null)
            return "❌ Bot ainda não inicializado; nenhum estado para ajustar.";

        if (state.InitialBalanceEur - amount < 0m)
            return $"❌ Saque de €{amount:N2} deixaria o capital aportado negativo (atual: €{state.InitialBalanceEur:N2}).";

        var repo = scope.ServiceProvider.GetRequiredService<ICashFlowRepository>();
        var result = await repo.ApplyAsync(CashFlowType.Withdrawal, amount, ct);

        return $"✅ Saque de €{amount:N2} registrado.\n"
             + $"Capital aportado: €{result.OldBaseline:N2} → €{result.NewBaseline:N2}";
    }
}
