import { Bot } from 'grammy';
import { BotStatus, TradeRecord } from './types';

export class TelegramNotifier {
  private bot: Bot;
  private chatId: string;

  constructor(token: string, chatId: string) {
    this.bot = new Bot(token);
    this.chatId = chatId;
  }

  private escapeMarkdown(text: string): string {
    return text.replace(/([_*[\]()~`>#+\-=|{}.!])/g, '\\$1');
  }

  async sendTradeAlert(trade: TradeRecord, pnl: { pnlEur: number; pnlPct: number }): Promise<void> {
    const esc = this.escapeMarkdown.bind(this);
    const emoji = trade.side === 'BUY' ? '🟢' : '🔴';
    const msg = [
      `${emoji} *${esc(trade.side)}* BTC/EUR`,
      `Price: €${esc(trade.price.toFixed(2))}`,
      `Amount: ${esc(trade.quantity.toFixed(8))} BTC`,
      `Value: €${esc(trade.quoteAmount.toFixed(2))}`,
      `P&L: €${esc(pnl.pnlEur.toFixed(2))} \\(${esc(pnl.pnlPct.toFixed(2))}%\\)`,
    ].join('\n');

    await this.bot.api.sendMessage(this.chatId, msg, { parse_mode: 'MarkdownV2' });
  }

  async sendDailySummary(status: BotStatus, todayTrades: TradeRecord[]): Promise<void> {
    const esc = this.escapeMarkdown.bind(this);
    const stateLabel = status.state === 'HOLDING_BTC' ? '₿ Holding BTC' : '💶 Holding EUR';
    const msg = [
      `📊 *Daily Summary*`,
      ``,
      `State: ${esc(stateLabel)}`,
      `BTC Price: €${esc(status.currentBtcPrice.toFixed(2))}`,
      `BTC Balance: ${esc(status.btcBalance.toFixed(8))}`,
      `EUR Balance: €${esc(status.eurBalance.toFixed(2))}`,
      `P&L: €${esc(status.pnlEur.toFixed(2))} \\(${esc(status.pnlPct.toFixed(2))}%\\)`,
      `Target: €${esc(status.targetPrice.toFixed(2))}`,
      `Trades today: ${todayTrades.length}`,
    ].join('\n');

    await this.bot.api.sendMessage(this.chatId, msg, { parse_mode: 'MarkdownV2' });
  }

  async sendError(message: string): Promise<void> {
    await this.bot.api.sendMessage(this.chatId, `⚠️ *Error:* ${this.escapeMarkdown(message)}`, { parse_mode: 'MarkdownV2' });
  }

  async sendStartup(state: string, price: number): Promise<void> {
    const esc = this.escapeMarkdown.bind(this);
    await this.bot.api.sendMessage(
      this.chatId,
      `🤖 *Bot started*\nState: ${esc(state)}\nBTC/EUR: €${esc(price.toFixed(2))}`,
      { parse_mode: 'MarkdownV2' },
    );
  }
}
