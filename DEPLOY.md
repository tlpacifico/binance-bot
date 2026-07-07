# Deploy Guide - Binance Trading Bot

O deploy é **automático** via GitHub Actions (`.github/workflows/deploy.yml`).
Não há deploy manual — não é preciso SSH para lançar novas versões.

## Como funciona

Qualquer push para `master` dispara o pipeline (também dá para rodar à mão em
**Actions → Deploy Binance Bot → Run workflow**, com opção de pular os testes
para hotfixes urgentes). O pipeline tem 3 jobs:

1. **Test** — `dotnet test` em Release.
2. **Build & Push** — builda a imagem Docker e publica em
   `ghcr.io/<repo>:latest` (e com tag do short SHA).
3. **Deploy VPS** — por SSH: copia o `deploy/docker-compose.yml` para
   `/opt/binance-bot`, escreve o `.env` a partir dos GitHub Secrets, faz
   `docker compose pull` + `docker compose up -d`, remove imagens antigas e
   faz health check em `http://localhost:3000/api/health` (falha o deploy se
   não subir em 60s).

## Configuração (GitHub Secrets)

Toda a configuração vem dos **Secrets do repositório** (Settings → Secrets and
variables → Actions). O `.env` na VPS é **reescrito a cada deploy** a partir
deles — editar o `.env` na VPS à mão não adianta, some no próximo deploy.

Secrets usados:

| Secret | Uso |
|---|---|
| `VPS_HOST`, `VPS_USER`, `VPS_SSH_KEY` | Acesso SSH à VPS |
| `CONNECTION_STRING` | Connection string do PostgreSQL |
| `BINANCE_API_KEY`, `BINANCE_API_SECRET` | API da Binance (Spot Trading) |
| `TELEGRAM_BOT_TOKEN`, `TELEGRAM_CHAT_ID` | Bot do Telegram |
| `DASHBOARD_AUTH_TOKEN` | Token de acesso ao dashboard |

Para mudar qualquer valor de config: **edite o Secret e re-rode o deploy**.

## Estrutura na VPS

```
/opt/binance-bot/
  ├── docker-compose.yml   # copiado pelo CI
  └── .env                 # gerado pelo CI a partir dos Secrets (NUNCA commitar)
```

O container roda como `binance-bot` (imagem `ghcr.io/tlpacifico/binance-bot:latest`).
O PostgreSQL fica no host (ver `CONNECTION_STRING`), não em container do compose.

## Operação do dia-a-dia (SSH na VPS)

```bash
cd /opt/binance-bot

docker compose ps                 # estado do container
docker logs binance-bot -f        # logs em tempo real
docker logs binance-bot --tail 50 # últimas 50 linhas
docker compose restart            # reiniciar
docker compose pull && docker compose up -d   # forçar atualização manual da imagem
```

Health check: `curl http://localhost:3000/api/health`

## Base de dados (PostgreSQL)

O estado (tabelas `BotState` e `Trades`) fica no PostgreSQL. Para inspecionar/
corrigir valores, conecte com os dados da `CONNECTION_STRING`:

```bash
psql -h <host> -U <user> -d <database>
```

Exemplo — ajustar o capital aportado (baseline do P&L) após um depósito:

```sql
UPDATE "BotState" SET "InitialBalanceEur" = "InitialBalanceEur" + 100;  -- depósito de €100
SELECT "Id", "InitialBalanceEur" FROM "BotState";
```

## Dashboard

Acessar em `http://<VPS_HOST>:3000` e informar o `DASHBOARD_AUTH_TOKEN`.
Mostra estado atual, preço BTC/EUR, preço alvo, P&L, saldos e histórico de trades.

## Troubleshooting

**Deploy falhou no health check** — ver os logs no final do job de deploy
(ele imprime `docker logs binance-bot --tail 50`) ou na VPS:
`docker logs binance-bot --tail 100`.

**Erro "InvalidNonce" / Timestamp** — relógio da VPS dessincronizado:
```bash
timedatectl set-ntp true && systemctl restart systemd-timesyncd
docker compose -f /opt/binance-bot/docker-compose.yml restart
```

**Binance rejeita requests** — conferir se o IP da VPS está na whitelist da API
key e se "Enable Spot & Margin Trading" está ativo.
