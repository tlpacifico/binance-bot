# Dashboard Pacific Visibility + Trade Timestamp Fix — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Pacific strategy's decision self-explanatory on the web dashboard (mode, plain-language next action, price ladder) and stop trades from persisting a zero (`0001-01-01`) timestamp.

**Architecture:** A single pure function `PacificView.Compute` (grown from the existing `PacificTargetPrice`) produces a display projection that mirrors `PacificCalculator`. `/api/status` returns it as a `pacific` object. The static dashboard renders a new "Pacific Strategy" card from that object. Separately, the Binance client stamps trades with `UtcNow` when the exchange returns no create-time.

**Tech Stack:** .NET 9, ASP.NET Core Minimal APIs, xUnit + FluentAssertions, static HTML/CSS/vanilla JS.

## Global Constraints

- No changes to Pacific trading logic or thresholds — display-only, plus the timestamp-source fix.
- No new frontend framework or build step; dashboard stays static HTML/CSS/vanilla JS.
- `PacificView.Compute` mode logic must mirror `PacificCalculator` exactly: hard-stop only on the BTC side; escape-armed when drawdown/run-up ≥ `EscapeDrawdownPct`; else normal.
- `holdingBtc` is defined as `btcBalance × price > eurBalance` (the calculator's definition).
- The new card renders only when `strategy === "pacific"` and `pacific != null`.
- No retroactive fix of the already-persisted `0001-01-01` trade row.

---

### Task 1: `PacificView` pure function (replaces `PacificTargetPrice`)

**Files:**
- Create: `src/BinanceBot.Worker/Services/PacificView.cs`
- Delete: `src/BinanceBot.Worker/Services/PacificTargetPrice.cs`
- Create: `tests/BinanceBot.Worker.Tests/PacificViewTests.cs`
- Delete: `tests/BinanceBot.Worker.Tests/PacificTargetPriceTests.cs`
- Modify: `src/BinanceBot.Worker/Program.cs` (the `targetPrice` computation block, ~lines 132-150) — minimal update to keep it compiling; full expansion happens in Task 2.

**Interfaces:**
- Produces: `PacificView.Compute(bool holdingBtc, decimal lastTradePrice, decimal currentPrice, decimal lowSinceTrade, decimal highSinceTrade, decimal sellThresholdPct, decimal buyThresholdPct, decimal escapeDrawdownPct, decimal escapeRecoveryPct, decimal hardStopLossPct) → PacificView?` returning the record below, `null` when `lastTradePrice <= 0`.

- [ ] **Step 1: Write the failing tests**

Create `tests/BinanceBot.Worker.Tests/PacificViewTests.cs`:

```csharp
using BinanceBot.Worker.Services;
using FluentAssertions;

namespace BinanceBot.Worker.Tests;

public class PacificViewTests
{
    private static PacificView? Compute(bool holdingBtc, decimal lastTrade, decimal price,
        decimal low, decimal high, decimal hardStop = 0m) =>
        PacificView.Compute(holdingBtc, lastTrade, price, low, high,
            sellThresholdPct: 0.025m, buyThresholdPct: 0.025m,
            escapeDrawdownPct: 0.05m, escapeRecoveryPct: 0.025m, hardStopLossPct: hardStop);

    [Fact]
    public void NoLastTrade_ReturnsNull()
    {
        Compute(true, 0m, 60_000m, 60_000m, 60_000m).Should().BeNull();
    }

    [Fact]
    public void HoldingBtc_SmallDrawdown_IsNormal_ProfitTargetActive()
    {
        // drawdown 1.7% < 5% → normal; profit target 60000*1.025 = 61500
        var v = Compute(true, 60_000m, 59_000m, 59_000m, 60_000m)!;
        v.HoldingBtc.Should().BeTrue();
        v.Mode.Should().Be(PacificView.ModeNormal);
        v.ProfitTarget.Should().Be(61_500m);
        v.EscapeArmPrice.Should().Be(57_000m); // 60000*(1-0.05)
        v.EscapeTarget.Should().BeNull();
        v.ActiveTarget.Should().Be(61_500m);
    }

    [Fact]
    public void HoldingBtc_InEscapeZone_IsArmed_EscapeTargetActive()
    {
        // drawdown 16.7% >= 5% → escape target = low 50000 * 1.025 = 51250
        var v = Compute(true, 60_000m, 50_000m, 50_000m, 60_000m)!;
        v.Mode.Should().Be(PacificView.ModeEscapeArmed);
        v.EscapeTarget.Should().Be(51_250m);
        v.ActiveTarget.Should().Be(51_250m);
    }

    [Fact]
    public void HoldingBtc_HardStopEnabledAndBreached_IsHardStop()
    {
        // drawdown 16.7% >= hardStop 10% → hard-stop mode
        var v = Compute(true, 60_000m, 50_000m, 50_000m, 60_000m, hardStop: 0.10m)!;
        v.Mode.Should().Be(PacificView.ModeHardStop);
    }

    [Fact]
    public void HoldingEur_SmallRunup_IsNormal_ProfitTargetActive()
    {
        // runup 1.7% < 5% → normal; profit target 60000*0.975 = 58500
        var v = Compute(false, 60_000m, 61_000m, 60_000m, 61_000m)!;
        v.HoldingBtc.Should().BeFalse();
        v.Mode.Should().Be(PacificView.ModeNormal);
        v.ProfitTarget.Should().Be(58_500m);
        v.EscapeArmPrice.Should().Be(63_000m); // 60000*(1+0.05)
        v.EscapeTarget.Should().BeNull();
        v.ActiveTarget.Should().Be(58_500m);
    }

    [Fact]
    public void HoldingEur_InEscapeZone_IsArmed_EscapeTargetActive()
    {
        // runup 20% >= 5% → escape target = high 72000 * 0.975 = 70200
        var v = Compute(false, 60_000m, 72_000m, 60_000m, 72_000m)!;
        v.Mode.Should().Be(PacificView.ModeEscapeArmed);
        v.EscapeTarget.Should().Be(70_200m);
        v.ActiveTarget.Should().Be(70_200m);
    }

    [Fact]
    public void MoveFromLastTradePct_IsSigned()
    {
        Compute(false, 60_000m, 63_000m, 60_000m, 63_000m)!.MoveFromLastTradePct.Should().Be(0.05m);
        Compute(true, 60_000m, 57_000m, 57_000m, 60_000m)!.MoveFromLastTradePct.Should().Be(-0.05m);
    }
}
```

- [ ] **Step 2: Delete the old test file so the project compiles against the new API**

```bash
git rm tests/BinanceBot.Worker.Tests/PacificTargetPriceTests.cs
```

- [ ] **Step 3: Run the new tests to verify they fail**

Run: `dotnet test tests/BinanceBot.Worker.Tests --filter PacificViewTests`
Expected: FAIL to build — `PacificView` does not exist.

- [ ] **Step 4: Create `PacificView.cs` and delete `PacificTargetPrice.cs`**

```bash
git rm src/BinanceBot.Worker/Services/PacificTargetPrice.cs
```

Create `src/BinanceBot.Worker/Services/PacificView.cs`:

```csharp
namespace BinanceBot.Worker.Services;

/// <summary>
/// Read-only projection of the Pacific strategy's decision state for the dashboard.
/// Mirrors PacificCalculator's mode logic so the dashboard shows exactly what the
/// engine would do. Display-only: it never drives trades.
/// </summary>
public sealed record PacificView(
    bool HoldingBtc,
    string Mode,
    decimal LastTradePrice,
    decimal ProfitTarget,
    decimal EscapeArmPrice,
    decimal? EscapeTarget,
    decimal ActiveTarget,
    decimal LowSinceTrade,
    decimal HighSinceTrade,
    decimal MoveFromLastTradePct)
{
    public const string ModeNormal = "normal";
    public const string ModeEscapeArmed = "escape-armed";
    public const string ModeHardStop = "hard-stop";

    public static PacificView? Compute(
        bool holdingBtc,
        decimal lastTradePrice,
        decimal currentPrice,
        decimal lowSinceTrade,
        decimal highSinceTrade,
        decimal sellThresholdPct,
        decimal buyThresholdPct,
        decimal escapeDrawdownPct,
        decimal escapeRecoveryPct,
        decimal hardStopLossPct)
    {
        if (lastTradePrice <= 0) return null;

        var movePct = (currentPrice - lastTradePrice) / lastTradePrice;

        if (holdingBtc)
        {
            var profitTarget = lastTradePrice * (1 + sellThresholdPct);
            var escapeArmPrice = lastTradePrice * (1 - escapeDrawdownPct);
            var drawdown = (lastTradePrice - currentPrice) / lastTradePrice;

            string mode;
            decimal? escapeTarget = null;
            if (hardStopLossPct > 0 && drawdown >= hardStopLossPct)
            {
                mode = ModeHardStop;
            }
            else if (drawdown >= escapeDrawdownPct)
            {
                mode = ModeEscapeArmed;
                escapeTarget = lowSinceTrade * (1 + escapeRecoveryPct);
            }
            else
            {
                mode = ModeNormal;
            }

            return new PacificView(true, mode, lastTradePrice, profitTarget,
                escapeArmPrice, escapeTarget, escapeTarget ?? profitTarget,
                lowSinceTrade, highSinceTrade, movePct);
        }
        else
        {
            var profitTarget = lastTradePrice * (1 - buyThresholdPct);
            var escapeArmPrice = lastTradePrice * (1 + escapeDrawdownPct);
            var runup = (currentPrice - lastTradePrice) / lastTradePrice;

            string mode;
            decimal? escapeTarget = null;
            if (runup >= escapeDrawdownPct)
            {
                mode = ModeEscapeArmed;
                escapeTarget = highSinceTrade * (1 - escapeRecoveryPct);
            }
            else
            {
                mode = ModeNormal;
            }

            return new PacificView(false, mode, lastTradePrice, profitTarget,
                escapeArmPrice, escapeTarget, escapeTarget ?? profitTarget,
                lowSinceTrade, highSinceTrade, movePct);
        }
    }
}
```

- [ ] **Step 5: Update `Program.cs` to use the new type (minimal, keeps behavior)**

In `src/BinanceBot.Worker/Program.cs`, replace the existing target-price block:

```csharp
        decimal? targetPrice = null;
        var lastTradePrice = state?.LastTradePrice ?? 0;
        if (strategyResolver.ActiveKey == "pacific" && lastTradePrice > 0)
        {
            var pacificSettings = app.Services.GetRequiredService<IOptions<PacificSettings>>().Value;
            var extremes = PositionExtremes.FromJson(state?.StrategyStateJson)
                ?? PositionExtremes.Initial(price.Last);

            targetPrice = PacificTargetPrice.Compute(
                holdingBtc: btcAllocationPct >= 50,
                lastTradePrice: lastTradePrice,
                currentPrice: price.Last,
                lowSinceTrade: extremes.LowSinceTrade,
                highSinceTrade: extremes.HighSinceTrade,
                sellThresholdPct: pacificSettings.SellThresholdPct,
                buyThresholdPct: pacificSettings.BuyThresholdPct,
                escapeDrawdownPct: pacificSettings.EscapeDrawdownPct,
                escapeRecoveryPct: pacificSettings.EscapeRecoveryPct);
        }
```

with:

```csharp
        decimal? targetPrice = null;
        var lastTradePrice = state?.LastTradePrice ?? 0;
        if (strategyResolver.ActiveKey == "pacific" && lastTradePrice > 0)
        {
            var pacificSettings = app.Services.GetRequiredService<IOptions<PacificSettings>>().Value;
            var extremes = PositionExtremes.FromJson(state?.StrategyStateJson)
                ?? PositionExtremes.Initial(price.Last);

            targetPrice = PacificView.Compute(
                holdingBtc: (balances.Btc * price.Last) > balances.Eur,
                lastTradePrice: lastTradePrice,
                currentPrice: price.Last,
                lowSinceTrade: extremes.LowSinceTrade,
                highSinceTrade: extremes.HighSinceTrade,
                sellThresholdPct: pacificSettings.SellThresholdPct,
                buyThresholdPct: pacificSettings.BuyThresholdPct,
                escapeDrawdownPct: pacificSettings.EscapeDrawdownPct,
                escapeRecoveryPct: pacificSettings.EscapeRecoveryPct,
                hardStopLossPct: pacificSettings.HardStopLossPct)?.ActiveTarget;
        }
```

- [ ] **Step 6: Run tests and build to verify pass**

Run: `dotnet test tests/BinanceBot.Worker.Tests --filter PacificViewTests`
Expected: PASS (7 tests). Then `dotnet build` — expected: succeeds (no remaining `PacificTargetPrice` references).

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "Replace PacificTargetPrice with richer PacificView projection"
```

---

### Task 2: Expose `pacific` object on `/api/status`

**Files:**
- Modify: `src/BinanceBot.Worker/Program.cs` — the `/api/status` handler (the block from Task 1 Step 5, plus the returned `Results.Ok(...)` object).

**Interfaces:**
- Consumes: `PacificView.Compute(...)` from Task 1.
- Produces: `/api/status` JSON gains a `pacific` field (object or null) with: `mode`, `holdingBtc`, `lastTradePrice`, `profitTarget`, `escapeArmPrice`, `escapeTarget` (nullable), `activeTarget`, `lowSinceTrade`, `highSinceTrade`, `moveFromLastTradePct`, `sellThresholdPct`, `buyThresholdPct`, `escapeDrawdownPct`, `escapeRecoveryPct`, `hardStopLossPct`.

- [ ] **Step 1: Replace the target-price block with the full view + pacific object**

In `src/BinanceBot.Worker/Program.cs`, replace the block written in Task 1 Step 5 with:

```csharp
        decimal? targetPrice = null;
        object? pacific = null;
        var lastTradePrice = state?.LastTradePrice ?? 0;
        if (strategyResolver.ActiveKey == "pacific" && lastTradePrice > 0)
        {
            var pacificSettings = app.Services.GetRequiredService<IOptions<PacificSettings>>().Value;
            var extremes = PositionExtremes.FromJson(state?.StrategyStateJson)
                ?? PositionExtremes.Initial(price.Last);

            var view = PacificView.Compute(
                holdingBtc: (balances.Btc * price.Last) > balances.Eur,
                lastTradePrice: lastTradePrice,
                currentPrice: price.Last,
                lowSinceTrade: extremes.LowSinceTrade,
                highSinceTrade: extremes.HighSinceTrade,
                sellThresholdPct: pacificSettings.SellThresholdPct,
                buyThresholdPct: pacificSettings.BuyThresholdPct,
                escapeDrawdownPct: pacificSettings.EscapeDrawdownPct,
                escapeRecoveryPct: pacificSettings.EscapeRecoveryPct,
                hardStopLossPct: pacificSettings.HardStopLossPct);

            if (view is not null)
            {
                targetPrice = view.ActiveTarget;
                pacific = new
                {
                    mode = view.Mode,
                    holdingBtc = view.HoldingBtc,
                    lastTradePrice = view.LastTradePrice,
                    profitTarget = view.ProfitTarget,
                    escapeArmPrice = view.EscapeArmPrice,
                    escapeTarget = view.EscapeTarget,
                    activeTarget = view.ActiveTarget,
                    lowSinceTrade = view.LowSinceTrade,
                    highSinceTrade = view.HighSinceTrade,
                    moveFromLastTradePct = view.MoveFromLastTradePct,
                    sellThresholdPct = pacificSettings.SellThresholdPct,
                    buyThresholdPct = pacificSettings.BuyThresholdPct,
                    escapeDrawdownPct = pacificSettings.EscapeDrawdownPct,
                    escapeRecoveryPct = pacificSettings.EscapeRecoveryPct,
                    hardStopLossPct = pacificSettings.HardStopLossPct,
                };
            }
        }
```

- [ ] **Step 2: Add `pacific` to the response object**

In the same handler, in the `return Results.Ok(new { ... })` object, add the field after `targetPrice,`:

```csharp
            targetPrice,
            pacific,
            uptimeMs = Environment.TickCount64
```

- [ ] **Step 3: Build and smoke-test the endpoint**

Run: `dotnet build`
Expected: succeeds.

Then run the app locally (`dotnet run --project src/BinanceBot.Worker`, uses the user-secrets connection string) and, with a Pacific state that has a last trade, request the status endpoint:

Run: `curl -s -H "Authorization: Bearer <token>" http://localhost:3000/api/status`
Expected: JSON contains a `"pacific": { "mode": ..., "profitTarget": ..., "escapeArmPrice": ... }` object. Stop the app after verifying.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "Return pacific decision object from /api/status"
```

---

### Task 3: Dashboard "Pacific Strategy" card + timestamp display guard

**Files:**
- Modify: `src/BinanceBot.Worker/wwwroot/index.html`
- Modify: `src/BinanceBot.Worker/wwwroot/style.css`

**Interfaces:**
- Consumes: `status.pacific` (Task 2) and `status.currentBtcPrice`.
- Produces: (frontend only — no downstream consumers).

- [ ] **Step 1: Add the card markup**

In `src/BinanceBot.Worker/wwwroot/index.html`, insert this block immediately after the Status `</div>` card (before the Balances card):

```html
  <div class="card" id="pacificCard" style="display:none">
    <h2>Pacific Strategy</h2>
    <div><span id="pacificMode" class="badge">-</span></div>
    <p id="pacificExplain" class="pacific-explain"></p>
    <div class="ladder" id="pacificLadder"></div>
  </div>
```

- [ ] **Step 2: Add the render logic and timestamp guard**

In the `<script>` block: replace `formatTime` with the guarded version, add the helpers and `renderPacific`, and call it from `fetchData`.

Replace:

```javascript
    function formatTime(iso) {
      return new Date(iso).toLocaleString();
    }
```

with:

```javascript
    function formatTime(iso) {
      const d = new Date(iso);
      if (isNaN(d.getTime()) || d.getFullYear() < 2000) return '—';
      return d.toLocaleString();
    }

    function pctStr(x) { return (x * 100).toFixed(1) + '%'; }
    function eur(x) { return '€' + fmt(x, 2); }

    function explainPacific(p) {
      const rec = pctStr(p.escapeRecoveryPct);
      if (p.holdingBtc) {
        if (p.mode === 'hard-stop')
          return 'Hard-stop zone. Drawdown ' + pctStr(-p.moveFromLastTradePct)
            + ' ≥ ' + pctStr(p.hardStopLossPct) + '; sells at market.';
        if (p.mode === 'escape-armed')
          return 'Escape armed. Tracking low ' + eur(p.lowSinceTrade)
            + '; sells at ' + eur(p.escapeTarget) + ' (+' + rec + ' from low).';
        return 'Holding BTC. Sells at ' + eur(p.profitTarget) + ' (+' + pctStr(p.sellThresholdPct)
          + '), or if price falls to ' + eur(p.escapeArmPrice) + ' (−' + pctStr(p.escapeDrawdownPct)
          + ') sells on a ' + rec + ' bounce from the low.';
      } else {
        if (p.mode === 'escape-armed')
          return 'Escape armed. Tracking peak ' + eur(p.highSinceTrade)
            + '; buys back at ' + eur(p.escapeTarget) + ' (−' + rec + ' from peak).';
        return 'Holding EUR. Buys back at ' + eur(p.profitTarget) + ' (−' + pctStr(p.buyThresholdPct)
          + '), or if price rises to ' + eur(p.escapeArmPrice) + ' (+' + pctStr(p.escapeDrawdownPct)
          + ') buys on a ' + rec + ' pullback from the peak.';
      }
    }

    function renderLadder(p, currentPrice) {
      const markers = [
        { label: 'Last', price: p.lastTradePrice, cls: 'last' },
        { label: p.holdingBtc ? 'Sell target' : 'Buy target', price: p.profitTarget, cls: 'profit' },
        { label: 'Escape arm', price: p.escapeArmPrice, cls: 'escape-arm' },
        { label: 'Now', price: currentPrice, cls: 'current' },
      ];
      if (p.escapeTarget != null)
        markers.push({ label: 'Escape target', price: p.escapeTarget, cls: 'escape-target' });

      const prices = markers.map(function (m) { return m.price; });
      const min = Math.min.apply(null, prices);
      const max = Math.max.apply(null, prices);
      const span = (max - min) || 1;
      const lo = min - span * 0.1;
      const range = span * 1.2;

      document.getElementById('pacificLadder').innerHTML =
        '<div class="ladder-track"></div>' + markers.map(function (m) {
          const left = ((m.price - lo) / range * 100).toFixed(2);
          return '<div class="ladder-marker ' + m.cls + '" style="left:' + left + '%">'
            + '<span class="ml">' + m.label + '</span>'
            + '<span class="tick"></span>'
            + '<span class="mp">' + eur(m.price) + '</span></div>';
        }).join('');
    }

    function renderPacific(p, currentPrice) {
      const card = document.getElementById('pacificCard');
      if (!p) { card.style.display = 'none'; return; }
      card.style.display = 'block';

      const modeEl = document.getElementById('pacificMode');
      const labels = { 'normal': 'Normal', 'escape-armed': 'Escape armed', 'hard-stop': 'Hard-stop' };
      modeEl.textContent = labels[p.mode] || p.mode;
      modeEl.className = 'badge ' + p.mode;

      document.getElementById('pacificExplain').textContent = explainPacific(p);
      renderLadder(p, currentPrice);
    }
```

Then, inside `fetchData`, after the `// Status` block that sets price/target/pnl, add:

```javascript
        // Pacific strategy card
        renderPacific(status.pacific, status.currentBtcPrice);
```

- [ ] **Step 3: Add the card styles**

Append to `src/BinanceBot.Worker/wwwroot/style.css`:

```css
.badge.normal { background: #3fb95033; color: #3fb950; }
.badge.escape-armed { background: #d2992233; color: #d29922; }
.badge.hard-stop { background: #f8514933; color: #f85149; }

.pacific-explain { font-size: 0.9rem; color: #c9d1d9; line-height: 1.45; margin: 12px 0 20px; }

.ladder { position: relative; height: 70px; margin: 28px 12px 12px; }
.ladder-track { position: absolute; top: 34px; left: 0; right: 0; height: 4px; background: #30363d; border-radius: 2px; }
.ladder-marker { position: absolute; transform: translateX(-50%); text-align: center; width: 80px; }
.ladder-marker .ml { display: block; font-size: 0.7rem; color: #8b949e; height: 18px; }
.ladder-marker .tick { display: block; width: 2px; height: 16px; margin: 0 auto; background: #8b949e; }
.ladder-marker .mp { display: block; font-size: 0.72rem; color: #c9d1d9; margin-top: 4px; }
.ladder-marker.profit .tick { background: #3fb950; }
.ladder-marker.escape-arm .tick { background: #d29922; }
.ladder-marker.escape-target .tick { background: #f7931a; }
.ladder-marker.current .tick { background: #58a6ff; width: 3px; height: 22px; }
.ladder-marker.current .ml, .ladder-marker.current .mp { color: #f0f6fc; font-weight: 700; }
```

- [ ] **Step 4: Verify in the browser**

Run the app locally (`dotnet run --project src/BinanceBot.Worker`) and open `http://localhost:3000`.
Expected: with the Pacific strategy active and a last trade, a "Pacific Strategy" card appears showing a mode badge, a plain-language sentence, and a price ladder with `Last`, `Buy/Sell target`, `Escape arm`, and `Now` markers (plus `Escape target` when armed). Confirm the trade-history date no longer shows a real value as `01/01/1` style garbage — a pre-2000 timestamp now renders as `—`. Stop the app.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Add Pacific Strategy dashboard card and guard bad trade timestamps in UI"
```

---

### Task 4: Fix trade timestamp source in the Binance client

**Files:**
- Modify: `src/BinanceBot.Infrastructure/Binance/BinanceClientAdapter.cs` (lines ~80 and ~113)

**Interfaces:**
- Consumes: nothing new.
- Produces: `TradeRecord.Timestamp` is never `default(DateTime)` for executed trades.

- [ ] **Step 1: Fix the BUY timestamp**

In `MarketBuyAsync`, replace:

```csharp
            Timestamp = data.CreateTime,
```

with:

```csharp
            // Binance's place-order ack often omits CreateTime; a market order fills now, so fall back to UtcNow.
            Timestamp = data.CreateTime == default ? DateTime.UtcNow : data.CreateTime,
```

- [ ] **Step 2: Fix the SELL timestamp**

In `MarketSellAsync`, replace the remaining occurrence of:

```csharp
            Timestamp = data.CreateTime,
```

with:

```csharp
            // Binance's place-order ack often omits CreateTime; a market order fills now, so fall back to UtcNow.
            Timestamp = data.CreateTime == default ? DateTime.UtcNow : data.CreateTime,
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build`
Expected: succeeds. (The Binance client is not under unit test; correctness is verified by inspection of the two call sites.)

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "Stamp trades with UtcNow when Binance returns no CreateTime"
```

---

## Self-Review

**Spec coverage:**
- `PacificView` pure function → Task 1. ✅
- `/api/status` `pacific` object with settings pcts → Task 2. ✅
- Frontend card: mode badge + plain-language line + price ladder, renders only for Pacific → Task 3. ✅
- `formatTime` pre-2000 guard → Task 3 Step 2. ✅
- Timestamp source fix (both methods) → Task 4. ✅
- Backward-compat top-level `targetPrice` = `ActiveTarget` → Task 1 Step 5 / Task 2 Step 1. ✅
- Unit tests for the six/seven Compute cases → Task 1 Step 1. ✅

**Placeholder scan:** No TBD/TODO/"handle edge cases"; every code step shows complete code. ✅

**Type consistency:** `PacificView.Compute` signature and record fields are identical across Tasks 1 and 2; frontend reads exactly the field names Task 2 emits (`mode`, `holdingBtc`, `escapeTarget`, `moveFromLastTradePct`, the five `*Pct`). Mode string constants (`ModeNormal`/`normal`, etc.) match the JS `labels` map and CSS class names (`.badge.normal`, `.badge.escape-armed`, `.badge.hard-stop`). ✅
