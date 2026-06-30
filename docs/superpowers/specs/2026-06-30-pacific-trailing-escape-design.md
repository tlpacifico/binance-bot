# Design: Pacific v2 — Ping-pong de lucro com escape por trailing

**Data:** 2026-06-30
**Estratégia afetada:** `BinanceBot.Strategies.Pacific`
**Status:** Aprovado para planeamento

---

## 1. Problema

A estratégia Pacific é um "ping-pong" all-in: segurando EUR compra tudo quando o preço cai
`BuyThresholdPct` (2.5%) abaixo de uma referência; segurando BTC vende tudo quando sobe
`SellThresholdPct` (2.5%) acima da referência. A referência é o preço do último trade (modo
normal) ou o high/low de 24h (modo "stale", após `StaleTradeDays` = 2 dias sem trades).

**Falha:** no modo stale o alvo de venda = `high24h × 1.025` e o de compra = `low24h × 0.975`.
Esses alvos ficam **fora da faixa dos últimos 24h** — para vender, o preço precisa romper o topo
de 24h em +2.5%; para comprar, furar o fundo em −2.5%. Em mercado lateral ou em queda (o caso
comum depois de 2 dias parado), o preço oscila *dentro* da faixa e o alvo **nunca é alcançado**.
O deadlock que o stale deveria resolver persiste. Pior: `high24h`/`low24h` fica congelado por 24h
(`TradingEngineService` só os atualiza a cada 24h), agravando.

## 2. Objetivo

Ping-pong com **lucro garantido**: a referência é sempre o último trade, travando ~2.5% por
ciclo. Quando o preço vai contra a posição e não volta, um **escape controlado por trailing**
destrava — vendendo/comprando num repique a partir do extremo local, com alvo *sempre alcançável*.

Tensão aceite: "lucro garantido" e "nunca trava" são incompatíveis; o escape realiza perda
ocasional para retomar o ciclo, minimizando-a ao vender num repique (não num nível fixo de perda).

## 3. Configurações (`PacificSettings`)

**Remover:** `StaleTradeDays` (mecanismo stale-24h sai inteiro).

**Manter:** `SellThresholdPct` (0.025), `BuyThresholdPct` (0.025), `ConfirmationTicks` (10),
`CheckIntervalSeconds` (30), `MinTradeEur` (10).

**Adicionar:**

| Campo | Default | Significado |
|---|---|---|
| `EscapeDrawdownPct` | `0.05` | Quão longe (contra) o preço precisa ir do último trade para o trailing ativar |
| `EscapeRecoveryPct` | `0.025` | Repique a partir do extremo local que dispara o trade de escape |
| `HardStopLossPct` | `0` | `0` = desligado. Se > 0 (ex. `0.20`), vende a mercado quando a perda passa disso (só lado BTC) |

`appsettings.json` e `appsettings.Production.json` actualizados com os novos campos; o bloco
`StaleTradeDays` é removido.

## 4. Lógica do `PacificCalculator` (núcleo)

Nova assinatura — substitui `low24H`, `high24H`, `isStale` por `lowSinceTrade`, `highSinceTrade`,
e os novos parâmetros de escape:

```
Evaluate(
    decimal currentPrice,
    Portfolio portfolio,
    decimal lastTradePrice,      // = preço de entrada da posição actual
    decimal lowSinceTrade,       // min do preço desde o último trade (escape do lado BTC)
    decimal highSinceTrade,      // max do preço desde o último trade (escape do lado EUR)
    decimal sellThresholdPct,    // 0.025
    decimal buyThresholdPct,     // 0.025
    decimal escapeDrawdownPct,   // 0.05
    decimal escapeRecoveryPct,   // 0.025
    decimal hardStopLossPct,     // 0 = desligado
    decimal minTradeEur)
```

Determinação do lado (inalterada): `holdingBtc = (BtcBalance × currentPrice) > EurBalance`.

### Segurando BTC (lado venda) — avaliado nesta ordem

1. **Lucro (preferido):** se `currentPrice ≥ lastTradePrice × (1 + sellThresholdPct)` → **SELL**
   (`mode: normal`). Lucro travado.
2. **Hard stop (se ligado):** seja `drawdown = (lastTradePrice − currentPrice) / lastTradePrice`.
   Se `hardStopLossPct > 0` e `drawdown ≥ hardStopLossPct` → **SELL** (`mode: hard-stop`).
3. **Trailing escape:** se `drawdown ≥ escapeDrawdownPct` **e**
   `currentPrice ≥ lowSinceTrade × (1 + escapeRecoveryPct)` → **SELL** (`mode: trailing-escape`).
4. Senão → **HOLD**.

### Segurando EUR (lado compra) — simétrico

1. **Lucro:** se `currentPrice ≤ lastTradePrice × (1 − buyThresholdPct)` → **BUY** (`mode: normal`).
   (`lastTradePrice` aqui = preço da última venda = entrada da posição EUR.)
2. **Trailing escape:** seja `runup = (currentPrice − lastTradePrice) / lastTradePrice`.
   Se `runup ≥ escapeDrawdownPct` **e** `currentPrice ≤ highSinceTrade × (1 − escapeRecoveryPct)`
   → **BUY** (`mode: trailing-escape`).
3. Senão → **HOLD**.

> O hard-stop é só do lado BTC (perda real). Do lado EUR, subida é custo de oportunidade, não
> perda; só o trailing age.

Guardas existentes mantidas: portfólio com `TotalValueEur ≤ 0` → HOLD; valor do trade abaixo de
`minTradeEur` → HOLD. As `reason` strings incluem o `mode` para diagnóstico.

### Exemplo (cenário de deadlock)

Comprou a €60.000; preço estacionou em ~€50.000.
- Alvo de lucro = €61.500 → inalcançável (onde travava antes).
- `drawdown` = 16,7% ≥ 5% → trailing activo.
- `lowSinceTrade` = €50.000 → alvo de escape = €50.000 × 1.025 = **€51.250**.
- Preço repica para €51.250 → **SELL** (escape), realiza −14,6%, liberta capital, recomeça o ciclo.

## 5. Estado e fluxo (`TradingEngineService` + `StrategyStateJson`)

Abordagem escolhida: persistir o extremo local em `StrategyStateJson` (coluna já existente em
`BotStateEntity`), sem migração de schema. Sobrevive a restart/redeploy (o deploy Docker reinicia
o container a cada push em `master`, e uma posição presa por dias não pode perder o rastreio).

`StrategyStateJson` passa a guardar `{ "lowSinceTrade": <decimal>, "highSinceTrade": <decimal> }`,
gerido pelo engine (rastreio mecânico de min/máx, agnóstico à estratégia):

- **A cada tick (`OnPriceTickAsync`):** desserializa o JSON (se vazio/primeiro run, inicializa
  ambos = `currentPrice`); actualiza `lowSinceTrade = min(…, currentPrice)`,
  `highSinceTrade = max(…, currentPrice)`; persiste **apenas quando um extremo muda**. Injecta
  ambos no `StrategyContext`.
- **Ao executar trade (`ExecuteTradeAsync`, após sucesso):** reseta
  `lowSinceTrade = highSinceTrade = trade.Price` (posição nova começa limpa) e persiste.
- **Remove** o bloco de actualização de preços de 24h (linhas ~149-168 de `TradingEngineService`).

`StrategyContext`: os campos `Last24hLowPrice`/`Last24hHighPrice` são substituídos por
`LowSinceTrade`/`HighSinceTrade` (DTO não persistido — sem migração).

As colunas `Last24hLowPrice`/`Last24hHighPrice`/`Last24hPriceTimestamp` de `BotStateEntity` ficam
órfãs; permanecem no schema (Abordagem A, sem migração) marcadas como deprecadas em comentário.

A `PacificStrategy` continua pura: lê `context.LowSinceTrade`/`context.HighSinceTrade`, remove o
cálculo de `isStale`/`StaleTradeDays`, e mantém o wrapper de **confirmation ticks** envolvendo
também as decisões de escape (10 ticks acumulados + verificação de que a média ainda cruza o alvo
antes de executar), como hoje.

## 6. Dashboard (`/api/status` em `Program.cs`)

O cálculo de `targetPrice` (hoje espelha a lógica stale) passa a espelhar a nova lógica, lendo
`lowSinceTrade`/`highSinceTrade` do `StrategyStateJson` do estado:

- Segurando BTC (`btcAllocationPct ≥ 50`): mostra o alvo de lucro `lastTradePrice × (1 + sell)`;
  se em zona de escape (`drawdown ≥ EscapeDrawdownPct`), mostra o alvo activo
  `lowSinceTrade × (1 + EscapeRecoveryPct)`.
- Simétrico para EUR.

Assim o dashboard nunca mostra um alvo "fantasma" inalcançável.

## 7. Testes

**Mantidos:** modo normal (lucro) compra/venda; guarda de `MinTradeEur`; portfólio zerado.

**Removidos:** todos os testes `Stale*` e o `DeadlockScenario` antigo de `PacificCalculatorTests`.

**Novos (`PacificCalculatorTests`):**
- Drawdown < `EscapeDrawdownPct` → escape inativo, segura esperando lucro.
- Drawdown ≥ `EscapeDrawdownPct` + repique `EscapeRecoveryPct` do fundo → venda `trailing-escape`.
- Drawdown ≥ `EscapeDrawdownPct` sem repique suficiente → HOLD.
- `HardStopLossPct` = 0 → sem venda forçada mesmo a −30%.
- `HardStopLossPct` = 0.20 → venda forçada a −20% sem repique (`mode: hard-stop`).
- Simétrico EUR: runup ≥ `EscapeDrawdownPct` + recuo `EscapeRecoveryPct` do topo → compra escape.
- Novo deadlock: comprou 60k → fundo 50k → repique 51,25k → escape vende.

**`PacificStrategyTests`:** confirmation ticks envolvem decisões de escape (acumula até N ticks,
verifica média, executa/reseta).

**Engine:** rastreio de `lowSinceTrade`/`highSinceTrade` por tick e reset no trade
(persistência em `StrategyStateJson`).

## 8. Fora de escopo (YAGNI)

- Migração para remover as colunas `Last24h*` (ficam órfãs sem custo).
- Stop-loss do lado EUR / "FOMO buy" forçado.
- Tornar `EscapeRecoveryPct` independente por lado (um único valor serve aos dois).
