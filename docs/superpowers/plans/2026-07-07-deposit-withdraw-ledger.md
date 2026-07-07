# Deposit/Withdraw Commands + Cash-Flow Ledger Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the user record EUR deposits/withdrawals via Telegram so the P&L baseline (`InitialBalanceEur`) stays correct, with an atomic ledger table viewable on the dashboard.

**Architecture:** New `CashFlows` table + `ICashFlowRepository` whose `ApplyAsync` adjusts `BotState.InitialBalanceEur` and inserts a ledger row in a single transaction. Two new Telegram commands (`/deposit`, `/withdraw`) call it. A new Bearer-auth dashboard endpoint plus an HTML section surface the history; the "Initial Balance" card is relabeled "Capital Aportado".

**Tech Stack:** .NET 9, EF Core (PostgreSQL/Npgsql, InMemory for tests), Telegram.Bot, ASP.NET Core Minimal APIs, xUnit + FluentAssertions + NSubstitute.

## Global Constraints

- **EUR only.** All movements are euro amounts; no BTC.
- **Baseline rule:** deposit → `InitialBalanceEur += amount`; withdrawal → `InitialBalanceEur -= amount`.
- **Atomic:** baseline change and ledger row are written in one `SaveChangesAsync`.
- **Stateless Telegram host:** commands execute immediately, no confirmation flow.
- **Decimal storage:** money columns are `numeric(18,8)` (matches `Trades`/`BotState`).
- **Dashboard auth:** new data endpoint uses the same Bearer check as `/api/trades` (`AuthorizeRequest`).
- **Enum-as-string:** persisted enums stored via `.ToString()` and read via `Enum.Parse<T>` (matches `TradeEntity.Side`).

---

### Task 1: Core contracts + CashFlow repository

**Files:**
- Create: `src/BinanceBot.Core/Enums/CashFlowType.cs`
- Create: `src/BinanceBot.Core/Models/CashFlowRecord.cs`
- Create: `src/BinanceBot.Core/Interfaces/ICashFlowRepository.cs`
- Create: `src/BinanceBot.Infrastructure/Persistence/Entities/CashFlowEntity.cs`
- Create: `src/BinanceBot.Infrastructure/Persistence/CashFlowRepository.cs`
- Modify: `src/BinanceBot.Infrastructure/Persistence/BotDbContext.cs`
- Test: `tests/BinanceBot.Infrastructure.Tests/CashFlowRepositoryTests.cs`

**Interfaces:**
- Produces:
  - `enum CashFlowType { Deposit, Withdrawal }`
  - `record CashFlowRecord { int Id; DateTime Timestamp; CashFlowType Type; decimal AmountEur; decimal BalanceAfter; }`
  - `record CashFlowResult(decimal OldBaseline, decimal NewBaseline)`
  - `interface ICashFlowRepository { Task<CashFlowResult> ApplyAsync(CashFlowType, decimal, CancellationToken); Task<IReadOnlyList<CashFlowRecord>> GetRecentAsync(int, CancellationToken); }`
  - `class CashFlowEntity { int Id; DateTime Timestamp; string Type; decimal AmountEur; decimal BalanceAfter; }`

- [ ] **Step 1: Write the failing tests**

Create `tests/BinanceBot.Infrastructure.Tests/CashFlowRepositoryTests.cs`:

```csharp
using BinanceBot.Core.Enums;
using BinanceBot.Infrastructure.Persistence;
using BinanceBot.Infrastructure.Persistence.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Infrastructure.Tests;

public class CashFlowRepositoryTests : IDisposable
{
    private readonly BotDbContext _db;
    private readonly CashFlowRepository _repo;

    public CashFlowRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<BotDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new BotDbContext(options);
        _repo = new CashFlowRepository(_db);
    }

    private async Task SeedBaselineAsync(decimal baseline)
    {
        _db.BotState.Add(new BotStateEntity { Id = 1, InitialBalanceEur = baseline });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Apply_Deposit_ShouldAddToBaselineAndRecordRow()
    {
        await SeedBaselineAsync(340m);

        var result = await _repo.ApplyAsync(CashFlowType.Deposit, 100m);

        result.OldBaseline.Should().Be(340m);
        result.NewBaseline.Should().Be(440m);

        var state = await _db.BotState.FindAsync(1);
        state!.InitialBalanceEur.Should().Be(440m);

        var row = await _db.CashFlows.SingleAsync();
        row.Type.Should().Be("Deposit");
        row.AmountEur.Should().Be(100m);
        row.BalanceAfter.Should().Be(440m);
    }

    [Fact]
    public async Task Apply_Withdrawal_ShouldSubtractFromBaseline()
    {
        await SeedBaselineAsync(440m);

        var result = await _repo.ApplyAsync(CashFlowType.Withdrawal, 50m);

        result.NewBaseline.Should().Be(390m);
        (await _db.BotState.FindAsync(1))!.InitialBalanceEur.Should().Be(390m);
        (await _db.CashFlows.SingleAsync()).Type.Should().Be("Withdrawal");
    }

    [Fact]
    public async Task Apply_WhenNoState_ShouldThrow()
    {
        var act = async () => await _repo.ApplyAsync(CashFlowType.Deposit, 100m);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetRecent_ShouldReturnNewestFirstRespectingLimit()
    {
        _db.CashFlows.AddRange(
            new CashFlowEntity { Timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), Type = "Deposit", AmountEur = 10m, BalanceAfter = 10m },
            new CashFlowEntity { Timestamp = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc), Type = "Deposit", AmountEur = 30m, BalanceAfter = 40m },
            new CashFlowEntity { Timestamp = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), Type = "Withdrawal", AmountEur = 20m, BalanceAfter = 20m });
        await _db.SaveChangesAsync();

        var result = await _repo.GetRecentAsync(2);

        result.Should().HaveCount(2);
        result[0].AmountEur.Should().Be(30m);   // 2026-01-03 newest
        result[1].AmountEur.Should().Be(20m);   // 2026-01-02
        result[0].Type.Should().Be(CashFlowType.Deposit);
    }

    public void Dispose() => _db.Dispose();
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/BinanceBot.Infrastructure.Tests --filter CashFlowRepositoryTests`
Expected: FAIL — does not compile (`CashFlowRepository`, `CashFlowType`, `CashFlows` unknown).

- [ ] **Step 3: Create the Core enum**

`src/BinanceBot.Core/Enums/CashFlowType.cs`:

```csharp
namespace BinanceBot.Core.Enums;

public enum CashFlowType
{
    Deposit,
    Withdrawal
}
```

- [ ] **Step 4: Create the Core record + repository interface**

`src/BinanceBot.Core/Models/CashFlowRecord.cs`:

```csharp
using BinanceBot.Core.Enums;

namespace BinanceBot.Core.Models;

public sealed record CashFlowRecord
{
    public int Id { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public CashFlowType Type { get; init; }
    public decimal AmountEur { get; init; }
    public decimal BalanceAfter { get; init; }
}
```

`src/BinanceBot.Core/Interfaces/ICashFlowRepository.cs`:

```csharp
using BinanceBot.Core.Enums;
using BinanceBot.Core.Models;

namespace BinanceBot.Core.Interfaces;

public sealed record CashFlowResult(decimal OldBaseline, decimal NewBaseline);

public interface ICashFlowRepository
{
    Task<CashFlowResult> ApplyAsync(CashFlowType type, decimal amountEur, CancellationToken ct = default);
    Task<IReadOnlyList<CashFlowRecord>> GetRecentAsync(int limit = 50, CancellationToken ct = default);
}
```

- [ ] **Step 5: Create the entity**

`src/BinanceBot.Infrastructure/Persistence/Entities/CashFlowEntity.cs`:

```csharp
namespace BinanceBot.Infrastructure.Persistence.Entities;

public class CashFlowEntity
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string Type { get; set; } = string.Empty;
    public decimal AmountEur { get; set; }
    public decimal BalanceAfter { get; set; }
}
```

- [ ] **Step 6: Register the DbSet + column config in `BotDbContext`**

In `src/BinanceBot.Infrastructure/Persistence/BotDbContext.cs`, add the DbSet after the `BotState` one:

```csharp
    public DbSet<CashFlowEntity> CashFlows => Set<CashFlowEntity>();
```

and add this block inside `OnModelCreating` (after the `BotStateEntity` block):

```csharp
        modelBuilder.Entity<CashFlowEntity>(entity =>
        {
            entity.ToTable("CashFlows");
            entity.HasIndex(e => e.Timestamp);
            entity.Property(e => e.AmountEur).HasColumnType("numeric(18,8)");
            entity.Property(e => e.BalanceAfter).HasColumnType("numeric(18,8)");
        });
```

- [ ] **Step 7: Implement `CashFlowRepository`**

`src/BinanceBot.Infrastructure/Persistence/CashFlowRepository.cs`:

```csharp
using BinanceBot.Core.Enums;
using BinanceBot.Core.Interfaces;
using BinanceBot.Core.Models;
using BinanceBot.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Infrastructure.Persistence;

public sealed class CashFlowRepository : ICashFlowRepository
{
    private readonly BotDbContext _db;

    public CashFlowRepository(BotDbContext db) => _db = db;

    public async Task<CashFlowResult> ApplyAsync(CashFlowType type, decimal amountEur, CancellationToken ct = default)
    {
        var state = await _db.BotState.FindAsync([1], ct)
            ?? throw new InvalidOperationException("Bot state not initialized; cannot apply a cash flow.");

        var oldBaseline = state.InitialBalanceEur;
        var newBaseline = type == CashFlowType.Deposit
            ? oldBaseline + amountEur
            : oldBaseline - amountEur;

        state.InitialBalanceEur = newBaseline;
        _db.CashFlows.Add(new CashFlowEntity
        {
            Timestamp = DateTime.UtcNow,
            Type = type.ToString(),
            AmountEur = amountEur,
            BalanceAfter = newBaseline
        });

        await _db.SaveChangesAsync(ct);
        return new CashFlowResult(oldBaseline, newBaseline);
    }

    public async Task<IReadOnlyList<CashFlowRecord>> GetRecentAsync(int limit = 50, CancellationToken ct = default)
    {
        return await _db.CashFlows
            .OrderByDescending(c => c.Timestamp)
            .Take(limit)
            .Select(c => new CashFlowRecord
            {
                Id = c.Id,
                Timestamp = c.Timestamp,
                Type = Enum.Parse<CashFlowType>(c.Type),
                AmountEur = c.AmountEur,
                BalanceAfter = c.BalanceAfter
            })
            .ToListAsync(ct);
    }
}
```

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test tests/BinanceBot.Infrastructure.Tests --filter CashFlowRepositoryTests`
Expected: PASS (4 tests).

- [ ] **Step 9: Commit**

```bash
git add src/BinanceBot.Core src/BinanceBot.Infrastructure/Persistence tests/BinanceBot.Infrastructure.Tests/CashFlowRepositoryTests.cs
git commit -m "feat: cash-flow ledger repository with atomic baseline adjustment"
```

---

### Task 2: EF Core migration for `CashFlows`

**Files:**
- Create: `src/BinanceBot.Infrastructure/Migrations/<timestamp>_AddCashFlows.cs` (generated)

**Interfaces:**
- Consumes: `CashFlowEntity` + `BotDbContext.CashFlows` from Task 1.
- Produces: a migration that `db.Database.Migrate()` applies on startup (see `Program.cs:93`).

- [ ] **Step 1: Ensure the EF tools are available**

Run: `dotnet ef --version`
If it errors, install: `dotnet tool install --global dotnet-ef`

- [ ] **Step 2: Generate the migration**

Run:
```bash
dotnet ef migrations add AddCashFlows \
  --project src/BinanceBot.Infrastructure \
  --startup-project src/BinanceBot.Worker
```
Expected: creates `src/BinanceBot.Infrastructure/Migrations/<timestamp>_AddCashFlows.cs` and updates the model snapshot.

- [ ] **Step 3: Verify the generated migration**

Open the new migration file. `Up()` must create a `CashFlows` table with columns `Id` (identity PK), `Timestamp` (timestamp with time zone), `Type` (text), `AmountEur` (numeric(18,8)), `BalanceAfter` (numeric(18,8)), and an index on `Timestamp`. It must NOT alter or drop `Trades`/`BotState`. If it contains unrelated changes, delete the migration (`dotnet ef migrations remove --project src/BinanceBot.Infrastructure --startup-project src/BinanceBot.Worker`) and re-check Task 1's DbContext edit.

- [ ] **Step 4: Build to confirm the migration compiles**

Run: `dotnet build -c Release`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add src/BinanceBot.Infrastructure/Migrations
git commit -m "feat: add CashFlows table migration"
```

---

### Task 3: `/deposit` and `/withdraw` Telegram commands

**Files:**
- Create: `src/BinanceBot.Infrastructure/Telegram/Commands/CashFlowArgs.cs` (shared amount parser)
- Create: `src/BinanceBot.Infrastructure/Telegram/Commands/DepositCommand.cs`
- Create: `src/BinanceBot.Infrastructure/Telegram/Commands/WithdrawCommand.cs`
- Modify: `src/BinanceBot.Worker/Program.cs` (register repo scoped + two commands)
- Test: `tests/BinanceBot.Worker.Tests/CashFlowCommandTests.cs`

**Interfaces:**
- Consumes: `ICashFlowRepository`, `IStateRepository`, `CashFlowType`, `CashFlowResult` from Task 1; `ITelegramCommand` (existing); the `CreateScopeFactory` test helper pattern from `TelegramCommandTests.cs`.
- Produces: `CashFlowArgs.TryParseAmount(string[], out decimal)`; `DepositCommand` (`Name => "/deposit"`) and `WithdrawCommand` (`Name => "/withdraw"`), auto-listed by the existing `HelpCommand`.

- [ ] **Step 1: Write the failing tests**

Create `tests/BinanceBot.Worker.Tests/CashFlowCommandTests.cs`:

```csharp
using BinanceBot.Core.Enums;
using BinanceBot.Core.Interfaces;
using BinanceBot.Infrastructure.Telegram.Commands;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace BinanceBot.Worker.Tests;

public class CashFlowCommandTests
{
    private static IServiceScopeFactory ScopeFactory(Action<ServiceCollection> configure)
    {
        var services = new ServiceCollection();
        configure(services);
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IServiceScopeFactory>();
    }

    [Fact]
    public async Task Deposit_ValidAmount_ShouldApplyAndReport()
    {
        var repo = Substitute.For<ICashFlowRepository>();
        repo.ApplyAsync(CashFlowType.Deposit, 100m, Arg.Any<CancellationToken>())
            .Returns(new CashFlowResult(340m, 440m));
        var cmd = new DepositCommand(ScopeFactory(s => s.AddSingleton(repo)));

        var result = await cmd.ExecuteAsync(["100"]);

        await repo.Received(1).ApplyAsync(CashFlowType.Deposit, 100m, Arg.Any<CancellationToken>());
        result.Should().Contain("340").And.Contain("440");
    }

    [Theory]
    [InlineData(new string[0])]
    [InlineData(new[] { "abc" })]
    [InlineData(new[] { "0" })]
    [InlineData(new[] { "-5" })]
    public async Task Deposit_InvalidAmount_ShouldRejectWithoutApplying(string[] args)
    {
        var repo = Substitute.For<ICashFlowRepository>();
        var cmd = new DepositCommand(ScopeFactory(s => s.AddSingleton(repo)));

        var result = await cmd.ExecuteAsync(args);

        result.Should().Contain("Uso:");
        await repo.DidNotReceiveWithAnyArgs().ApplyAsync(default, default, default);
    }

    [Fact]
    public async Task Withdraw_WithinBaseline_ShouldApply()
    {
        var state = Substitute.For<IStateRepository>();
        state.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new BotStateData { InitialBalanceEur = 440m });
        var repo = Substitute.For<ICashFlowRepository>();
        repo.ApplyAsync(CashFlowType.Withdrawal, 50m, Arg.Any<CancellationToken>())
            .Returns(new CashFlowResult(440m, 390m));
        var cmd = new WithdrawCommand(ScopeFactory(s => { s.AddSingleton(state); s.AddSingleton(repo); }));

        var result = await cmd.ExecuteAsync(["50"]);

        await repo.Received(1).ApplyAsync(CashFlowType.Withdrawal, 50m, Arg.Any<CancellationToken>());
        result.Should().Contain("390");
    }

    [Fact]
    public async Task Withdraw_ExceedingBaseline_ShouldRejectWithoutApplying()
    {
        var state = Substitute.For<IStateRepository>();
        state.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new BotStateData { InitialBalanceEur = 40m });
        var repo = Substitute.For<ICashFlowRepository>();
        var cmd = new WithdrawCommand(ScopeFactory(s => { s.AddSingleton(state); s.AddSingleton(repo); }));

        var result = await cmd.ExecuteAsync(["100"]);

        result.Should().Contain("negativo");
        await repo.DidNotReceiveWithAnyArgs().ApplyAsync(default, default, default);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/BinanceBot.Worker.Tests --filter CashFlowCommandTests`
Expected: FAIL — `DepositCommand`/`WithdrawCommand` unknown.

- [ ] **Step 3: Implement the shared amount parser**

`src/BinanceBot.Infrastructure/Telegram/Commands/CashFlowArgs.cs`:

```csharp
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
```

- [ ] **Step 4: Implement `DepositCommand`**

`src/BinanceBot.Infrastructure/Telegram/Commands/DepositCommand.cs`:

```csharp
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
```

- [ ] **Step 5: Implement `WithdrawCommand`**

`src/BinanceBot.Infrastructure/Telegram/Commands/WithdrawCommand.cs`:

```csharp
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
```

- [ ] **Step 6: Register the repository and commands in `Program.cs`**

In `src/BinanceBot.Worker/Program.cs`, after `builder.Services.AddScoped<IStateRepository, StateRepository>();` (line 43) add:

```csharp
    builder.Services.AddScoped<ICashFlowRepository, CashFlowRepository>();
```

and after `builder.Services.AddSingleton<ITelegramCommand, HelpCommand>();` (line 60) add:

```csharp
    builder.Services.AddSingleton<ITelegramCommand, DepositCommand>();
    builder.Services.AddSingleton<ITelegramCommand, WithdrawCommand>();
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests/BinanceBot.Worker.Tests --filter CashFlowCommandTests`
Expected: PASS (7 test cases: 1 deposit-ok, 4 deposit-invalid, 1 withdraw-ok, 1 withdraw-guard).

- [ ] **Step 8: Commit**

```bash
git add src/BinanceBot.Infrastructure/Telegram/Commands/CashFlowArgs.cs src/BinanceBot.Infrastructure/Telegram/Commands/DepositCommand.cs src/BinanceBot.Infrastructure/Telegram/Commands/WithdrawCommand.cs src/BinanceBot.Worker/Program.cs tests/BinanceBot.Worker.Tests/CashFlowCommandTests.cs
git commit -m "feat: /deposit and /withdraw Telegram commands"
```

---

### Task 4: Dashboard endpoint + ledger section + card relabel

**Files:**
- Modify: `src/BinanceBot.Worker/Program.cs` (add `/api/cashflows` endpoint)
- Modify: `src/BinanceBot.Worker/wwwroot/index.html` (ledger section, fetch, relabel)

**Interfaces:**
- Consumes: `ICashFlowRepository.GetRecentAsync` from Task 1; `AuthorizeRequest` + `dashboardSettings` (existing in `Program.cs`). The HTTP JSON layer already serializes enums as strings (`JsonStringEnumConverter`, `Program.cs:82`), so `type` arrives as `"Deposit"`/`"Withdrawal"`.

- [ ] **Step 1: Add the `/api/cashflows` endpoint**

In `src/BinanceBot.Worker/Program.cs`, immediately after the `/api/trades` endpoint block (ends line 203, before `app.Run(...)`), add:

```csharp
    app.MapGet("/api/cashflows", async (
        ICashFlowRepository cashFlowRepo,
        HttpContext ctx,
        int? limit) =>
    {
        if (!AuthorizeRequest(ctx, dashboardSettings.AuthToken))
            return Results.Unauthorized();

        var cashFlows = await cashFlowRepo.GetRecentAsync(limit ?? 50);
        return Results.Ok(new { cashFlows, total = cashFlows.Count });
    });
```

- [ ] **Step 2: Relabel the "Initial Balance" card**

In `src/BinanceBot.Worker/wwwroot/index.html`, change the label on line 65:

```html
        <div class="label">Initial Balance</div>
```
to:
```html
        <div class="label">Capital Aportado</div>
```

- [ ] **Step 3: Add the ledger card to the HTML**

In `src/BinanceBot.Worker/wwwroot/index.html`, after the Trade History card (the `</div>` closing the card that ends at line 84), add a new card:

```html
  <div class="card">
    <h2>Aportes / Saques</h2>
    <table>
      <thead>
        <tr>
          <th>Time</th>
          <th>Type</th>
          <th>Amount</th>
          <th>Balance After</th>
        </tr>
      </thead>
      <tbody id="cashflows"></tbody>
    </table>
  </div>
```

- [ ] **Step 4: Fetch and render the ledger**

In `src/BinanceBot.Worker/wwwroot/index.html`, update the `Promise.all` in `fetchData` (lines 176-186) to also fetch cash flows:

```javascript
        const [statusRes, tradesRes, cashflowsRes] = await Promise.all([
          fetch('/api/status', { headers: headers }),
          fetch('/api/trades?limit=20', { headers: headers }),
          fetch('/api/cashflows?limit=20', { headers: headers }),
        ]);

        if (!statusRes.ok || !tradesRes.ok || !cashflowsRes.ok) {
          throw new Error('Unauthorized or server error');
        }

        const status = await statusRes.json();
        const { trades } = await tradesRes.json();
        const { cashFlows } = await cashflowsRes.json();
```

Then, right after the trades-rendering block (after line 232, before the `lastUpdate` line), add:

```javascript
        // Cash flows
        const cfBody = document.getElementById('cashflows');
        cfBody.innerHTML = cashFlows.map(function(c) {
          var type = c.type || '';
          return '<tr>'
            + '<td>' + formatTime(c.timestamp) + '</td>'
            + '<td class="' + (type === 'Deposit' ? 'buy' : 'sell') + '">' + type + '</td>'
            + '<td>€' + fmt(c.amountEur, 2) + '</td>'
            + '<td>€' + fmt(c.balanceAfter, 2) + '</td>'
            + '</tr>';
        }).join('');
```

- [ ] **Step 5: Build and verify**

Run: `dotnet build -c Release`
Expected: Build succeeded.

Manual check (optional, requires a running DB): start the Worker, open the dashboard, confirm the "Capital Aportado" label shows and an "Aportes / Saques" table renders (empty until a `/deposit` is issued). Because there is no endpoint test harness in this repo (consistent with `/api/trades`), verification is build + eyeball.

- [ ] **Step 6: Commit**

```bash
git add src/BinanceBot.Worker/Program.cs src/BinanceBot.Worker/wwwroot/index.html
git commit -m "feat: dashboard cash-flow ledger section and Capital Aportado relabel"
```

---

### Task 5: Full-suite check

- [ ] **Step 1: Run the entire test suite**

Run: `dotnet test -c Release`
Expected: PASS — all pre-existing tests plus the new `CashFlowRepositoryTests` (4) and `CashFlowCommandTests` (7 cases).

- [ ] **Step 2: Update CLAUDE.md Telegram command list**

In `CLAUDE.md`, the "Telegram Commands" line lists the commands. Add `/deposit`, `/withdraw`:

```
`/status`, `/start`, `/stop`, `/rebalance`, `/settings`, `/history [n]`, `/strategy [name]`, `/deposit <eur>`, `/withdraw <eur>`, `/help`. Authorization via ChatId. Commands implement `ITelegramCommand`.
```

- [ ] **Step 3: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: document /deposit and /withdraw commands"
```

---

## Self-Review

**Spec coverage:**
- CashFlows table → Task 1 (entity + DbContext) + Task 2 (migration). ✓
- `ICashFlowRepository.ApplyAsync` atomic baseline+ledger → Task 1. ✓
- `GetRecentAsync` → Task 1. ✓
- `/deposit` + `/withdraw`, EUR, immediate, reply old→new, negative guard → Task 3. ✓
- `/help` update → automatic (HelpCommand iterates registered commands); no task needed. ✓
- `/api/cashflows` Bearer-auth → Task 4. ✓
- Dashboard ledger section + "Initial Balance"→"Capital Aportado" relabel → Task 4. ✓
- P&L consumers unchanged (baseline stays correct) → no code change needed; covered by Task 1's baseline math. ✓
- Tests (repo + commands) → Tasks 1 and 3. ✓

**Placeholder scan:** No TBD/TODO; all steps contain concrete code or exact commands.

**Type consistency:** `CashFlowType`, `CashFlowRecord`, `CashFlowResult(OldBaseline, NewBaseline)`, `ICashFlowRepository.ApplyAsync(CashFlowType, decimal, CancellationToken)` and `GetRecentAsync(int, CancellationToken)` are used identically across Tasks 1, 3, 4. Entity fields (`Type`, `AmountEur`, `BalanceAfter`) match between entity, repo, and JSON (`type`, `amountEur`, `balanceAfter`) used in the HTML.

**Note on `/help`:** `HelpCommand` (`HelpCommand.cs`) enumerates `IServiceProvider.GetServices<ITelegramCommand>()`, so registering the two commands in Task 3 makes them appear in `/help` automatically — the spec's "help updated" requirement needs no dedicated code change.
