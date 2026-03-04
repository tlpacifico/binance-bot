# Deploy Guide - Binance Trading Bot

## Pre-requisitos

- VPS com Ubuntu 22.04+ (ex: Contabo Cloud VPS)
- Acesso root via SSH
- API Key da Binance com Spot Trading ativo
- Bot do Telegram criado via BotFather

---

## 1. Primeiro Deploy

### 1.1 Configurar o .env

Antes de copiar para o servidor, cria o ficheiro `.env` a partir do exemplo:

```bash
cp .env.example .env
```

Preenche os valores:

```env
BINANCE_API_KEY=a_tua_api_key
BINANCE_API_SECRET=o_teu_api_secret
BINANCE_TESTNET=false
TRADING_PAIR=BTC/EUR
SELL_THRESHOLD_PCT=0.025
BUY_THRESHOLD_PCT=0.025
INITIAL_BALANCE_EUR=0
POLL_INTERVAL_MS=30000
TELEGRAM_BOT_TOKEN=o_teu_bot_token
TELEGRAM_CHAT_ID=o_teu_chat_id
DASHBOARD_PORT=3000
DASHBOARD_AUTH_TOKEN=um_token_seguro_com_pelo_menos_16_caracteres
LOG_LEVEL=info
```

> **Nota:** Se `INITIAL_BALANCE_EUR=0`, o bot calcula automaticamente a partir do saldo da carteira.

### 1.2 Copiar projeto para o servidor (Windows PowerShell)

```powershell
# Copiar sem node_modules (pesado e desnecessario)
robocopy C:\Repos\Thacio\binance-bot C:\Temp\binance-bot /E /XD node_modules .git dist
scp -r C:\Temp\binance-bot root@<IP>:/root/
```

Ou diretamente (mais lento, copia tudo):

```powershell
scp -r C:\Repos\Thacio\binance-bot root@<IP>:/root/
```

### 1.3 SSH e executar setup

```bash
ssh root@<IP>
cd /root/binance-bot
bash deploy/setup.sh
```

O script faz tudo automaticamente:
- Cria user dedicado `botuser`
- Instala Node.js 20 LTS
- Instala dependencias e compila TypeScript
- Configura systemd (auto-restart + boot automatico)
- Abre porta 3000 no firewall
- Mostra o IP do servidor para whitelist da Binance

### 1.4 Verificar

```bash
# Status do servico
systemctl status binance-bot

# Health check
curl http://localhost:3000/api/health

# Logs em tempo real
journalctl -u binance-bot -f

# Ultimas 50 linhas de log
journalctl -u binance-bot --no-pager -n 50
```

### 1.5 Configurar Binance API

1. Binance > Account > API Management > Edit
2. "Restrict access to trusted IPs only" > adicionar o IP do servidor
3. Ativar "Enable Spot & Margin Trading"

---

## 2. Atualizar o Codigo

Quando fizeres alteracoes no codigo local:

### 2.1 Copiar ficheiros atualizados (Windows PowerShell)

```powershell
# Copiar src/ e public/ atualizados
scp -r C:\Repos\Thacio\binance-bot\src root@<IP>:/home/botuser/binance-bot/
scp -r C:\Repos\Thacio\binance-bot\public root@<IP>:/home/botuser/binance-bot/
```

Se mudaste `package.json` ou `tsconfig.json`:

```powershell
scp C:\Repos\Thacio\binance-bot\package.json root@<IP>:/home/botuser/binance-bot/
scp C:\Repos\Thacio\binance-bot\package-lock.json root@<IP>:/home/botuser/binance-bot/
scp C:\Repos\Thacio\binance-bot\tsconfig.json root@<IP>:/home/botuser/binance-bot/
```

### 2.2 Recompilar e reiniciar no servidor

```bash
ssh root@<IP>
cd /home/botuser/binance-bot

# Corrigir permissoes (porque scp como root muda o owner)
chown -R botuser:botuser .

# Recompilar
sudo -u botuser npm ci
sudo -u botuser npm run build
sudo -u botuser npm prune --omit=dev

# Reiniciar
systemctl restart binance-bot

# Verificar
systemctl status binance-bot
```

### 2.3 Atualizar .env

```bash
ssh root@<IP>
nano /home/botuser/binance-bot/.env
# Editar valores, guardar com Ctrl+O, sair com Ctrl+X

systemctl restart binance-bot
```

---

## 3. Comandos do Dia-a-Dia

```bash
# Ver status
systemctl status binance-bot

# Ver logs em tempo real
journalctl -u binance-bot -f

# Ver logs de erros
journalctl -u binance-bot -p err --no-pager -n 50

# Reiniciar
systemctl restart binance-bot

# Parar
systemctl stop binance-bot

# Iniciar
systemctl start binance-bot

# Health check
curl http://localhost:3000/api/health
```

---

## 4. Dashboard

Aceder no browser:

```
http://<IP_DO_SERVIDOR>:3000
```

Introduzir o `DASHBOARD_AUTH_TOKEN` quando pedido.

O dashboard mostra:
- Estado atual (HOLDING_BTC / HOLDING_EUR)
- Preco atual BTC/EUR
- Preco alvo (compra ou venda)
- P&L (lucro/perda)
- Saldos
- Historico de trades

---

## 5. Troubleshooting

### Bot nao inicia
```bash
journalctl -u binance-bot --no-pager -n 100
```
Verificar se o `.env` esta preenchido corretamente.

### Erro "InvalidNonce" / Timestamp
O relogio do servidor esta dessincronizado. Corrigir:
```bash
timedatectl set-ntp true
systemctl restart systemd-timesyncd
systemctl restart binance-bot
```

### Dashboard nao abre no browser
Verificar firewall:
```bash
ufw status
# Deve mostrar porta 3000 ALLOW
```
Se nao estiver aberta:
```bash
ufw allow 3000/tcp
```

### Binance rejeita requests
- Verificar se o IP do servidor esta na whitelist da API key
- Verificar se "Enable Spot & Margin Trading" esta ativo
- Verificar se as API keys no .env estao corretas

### Sem espaco em disco
```bash
df -h
# Limpar logs antigos se necessario
journalctl --vacuum-time=7d
```

---

## 6. Estrutura no Servidor

```
/home/botuser/binance-bot/
  ├── dist/          # Codigo compilado (gerado pelo build)
  ├── data/          # SQLite database + WAL files
  ├── deploy/        # Scripts de deploy
  ├── public/        # Dashboard HTML/CSS
  ├── src/           # Codigo fonte TypeScript
  ├── node_modules/  # Dependencias
  ├── .env           # Configuracao (NUNCA partilhar!)
  ├── package.json
  └── tsconfig.json
```

Servico systemd: `/etc/systemd/system/binance-bot.service`
