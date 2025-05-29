using Telegram.Bot;
using Telegram.Bot.Types.ReplyMarkups;
using System.Collections.Concurrent;

namespace TelegramGameBot.Services.Games
{
    public class SlotsGameState
    {
        public int CurrentBet { get; set; }
        public bool IsSpinning { get; set; }
        public DateTime LastSpinTime { get; set; }
        public string[] CurrentSymbols { get; set; }

        public SlotsGameState(int bet)
        {
            CurrentBet = bet;
            IsSpinning = false;
            LastSpinTime = DateTime.Now;
            CurrentSymbols = new string[3];
        }
    }

    public class SlotsGame : BaseGame
    {
        private readonly Dictionary<string, (string name, string emoji, double multiplier)> _symbols = new()
        {
            { "seven", ("Семерка", "7️⃣", 10.0) },
            { "diamond", ("Бриллиант", "💎", 5.0) },
            { "grape", ("Виноград", "🍇", 4.0) },
            { "orange", ("Апельсин", "🍊", 3.0) },
            { "lemon", ("Лимон", "🍋", 2.5) },
            { "cherry", ("Вишня", "🍒", 2.0) }
        };

        private readonly Dictionary<long, int> _pendingBets;

        public SlotsGame(ITelegramBotClient bot, UserService userService)
            : base(bot, userService)
        {
            _pendingBets = new Dictionary<long, int>();
        }

        public override async Task ShowRules(long chatId)
        {
            var text = "🎰 Слоты\n\n" +
                      "Правила:\n" +
                      "1. Сделайте ставку\n" +
                      "2. Крутите слоты\n" +
                      "3. Соберите три одинаковых символа\n\n" +
                      "Множители:\n" +
                      "7️⃣ Семерка - x10.0\n" +
                      "💎 Бриллиант - x5.0\n" +
                      "🍇 Виноград - x4.0\n" +
                      "🍊 Апельсин - x3.0\n" +
                      "🍋 Лимон - x2.5\n" +
                      "🍒 Вишня - x2.0";

            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🎰 Сделать ставку", "slots_bet"),
                    InlineKeyboardButton.WithCallbackData("🔙 Назад", "games_menu")
                }
            });

            await _bot.SendTextMessageAsync(chatId, text, replyMarkup: keyboard);
        }

        public override async Task HandleCommand(long chatId, long userId, string command)
        {
            switch (command)
            {
                case "slots_bet":
                    await ShowBetMenu(chatId, "slots_game");
                    break;

                case "slots_spin":
                    await SpinSlots(chatId, userId);
                    break;
            }
        }

        public override async Task ProcessBet(long chatId, long userId, int amount)
        {
            var user = _userService.GetOrCreateUser(userId);
            
            if (user.Balance < amount)
            {
                await _bot.SendTextMessageAsync(
                    chatId,
                    $"❌ Недостаточно средств! Ваш баланс: {user.Balance} монет"
                );
                return;
            }

            _userService.UpdateBalance(userId, -amount);
            _pendingBets[userId] = amount;

            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🎰 Крутить слоты", "slots_spin")
                }
            });

            await _bot.SendTextMessageAsync(
                chatId,
                $"Ставка: {amount} монет\nНажмите кнопку, чтобы крутить слоты!",
                replyMarkup: keyboard
            );
        }

        private async Task SpinSlots(long chatId, long userId)
        {
            if (!_pendingBets.TryGetValue(userId, out var bet))
            {
                await _bot.SendTextMessageAsync(
                    chatId,
                    "❌ Сначала сделайте ставку!"
                );
                return;
            }

            var symbols = _symbols.ToList();
            var result = new (string key, (string name, string emoji, double multiplier) value)[3];
            
            for (int i = 0; i < 3; i++)
            {
                var randomIndex = _random.Next(symbols.Count);
                var symbol = symbols[randomIndex];
                result[i] = (symbol.Key, symbol.Value);
            }

            var isWin = result[0].value.emoji == result[1].value.emoji && 
                       result[1].value.emoji == result[2].value.emoji;

            var winAmount = isWin ? (int)(bet * result[0].value.multiplier) : 0;

            if (winAmount > 0)
            {
                _userService.UpdateBalance(userId, winAmount);
            }

            _pendingBets.Remove(userId);

            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🎰 Играть снова", "slots_bet"),
                    InlineKeyboardButton.WithCallbackData("🏠 В меню", "games_menu")
                }
            });

            var slotsLine = $"{result[0].value.emoji} {result[1].value.emoji} {result[2].value.emoji}";
            var resultText = isWin
                ? $"🎉 Победа!\n" +
                  $"Комбинация: {slotsLine}\n" +
                  $"Множитель: x{result[0].value.multiplier}\n" +
                  $"Выигрыш: {winAmount} монет"
                : $"😢 Проигрыш\n" +
                  $"Комбинация: {slotsLine}\n" +
                  $"Проигрыш: {bet} монет";

            await _bot.SendTextMessageAsync(
                chatId,
                resultText,
                replyMarkup: keyboard
            );

            _userService.AddGameHistory(
                userId,
                "Слоты",
                bet,
                isWin,
                $"Комбинация: {slotsLine}"
            );
        }
    }
} 