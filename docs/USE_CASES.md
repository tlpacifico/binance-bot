# Use Cases

## UC-01: Automated BTC/EUR Trading

**Actor:** Bot (autonomous)

The bot continuously monitors the BTC/EUR price on Binance and executes trades based on percentage thresholds relative to the last trade price.

- When holding EUR and the price drops by the configured `BUY_THRESHOLD_PCT` (default 2.5%), the bot places a market buy order spending all available EUR.
- When holding BTC and the price rises by the configured `SELL_THRESHOLD_PCT` (default 2.5%), the bot places a market sell order selling all held BTC.

Failed trades are retried up to 3 times with increasing delays (5s, 10s, 20s). If all retries fail, the bot sends an error notification via Telegram and continues monitoring.

---

## UC-02: Real-Time Trade Notifications

**Actor:** User (via Telegram)

The user receives instant Telegram messages whenever a trade is executed, including:
- Trade side (BUY/SELL)
- Execution price
- BTC quantity and EUR value
- Current P&L (absolute and percentage)

---

## UC-03: Daily Portfolio Summary

**Actor:** User (via Telegram)

Every day at 20:00 UTC, the bot sends a summary message with:
- Current state (holding BTC or EUR)
- Current BTC/EUR price
- Portfolio balances
- P&L since initial investment
- Target price for the next trade
- Number of trades executed that day

---

## UC-04: Startup Notification

**Actor:** User (via Telegram)

When the bot starts (or restarts), the user receives a message with the current state and BTC/EUR price, confirming the bot is online.

---

## UC-05: Error and Outage Alerts

**Actor:** User (via Telegram)

If the price feed fails 10 consecutive times, the bot sends an alert informing the user that it is unable to fetch prices and is not trading. When the feed recovers, normal operation resumes automatically.

---

## UC-06: Monitor Bot Status via Dashboard

**Actor:** User (via browser)

The user accesses the web dashboard at `http://<server>:3000` and authenticates with the `DASHBOARD_AUTH_TOKEN`. The dashboard displays:
- Current state (HOLDING_BTC / HOLDING_EUR)
- Live BTC/EUR price
- Target price for the next trade
- BTC and EUR balances
- P&L (absolute and percentage)
- Trade history

---

## UC-07: Health Check

**Actor:** External monitoring system or user

The `/api/health` endpoint (no authentication required) returns the bot's uptime and a simple OK status. This can be used by uptime monitors (e.g., UptimeRobot) to detect if the bot process is down.

---

## UC-08: Automatic State Recovery on Restart

**Actor:** Bot (autonomous)

When the bot restarts, it resumes from the last saved state in the SQLite database. If the saved state disagrees with the actual Binance wallet balances (e.g., a manual trade was made on the exchange), the bot reconciles by trusting the exchange balances and updating the local state accordingly.

---

## UC-09: First-Run Auto-Detection

**Actor:** Bot (autonomous)

On the very first run (no saved state), the bot inspects the Binance wallet to determine whether it is holding BTC or EUR, fetches the last trade from Binance as a price reference, and auto-calculates the initial balance for P&L tracking if not configured.

---

## UC-10: Testnet Trading

**Actor:** Developer / User

By setting `BINANCE_TESTNET=true`, the bot connects to the Binance testnet sandbox instead of production. This allows testing the full trading flow with fake funds before going live.
