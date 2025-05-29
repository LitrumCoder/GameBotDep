using Telegram.Bot;
using Telegram.Bot.Types.ReplyMarkups;
using System.Collections.Concurrent;

namespace TelegramGameBot.Services.Games
{
    public class DiceGame : BaseGame
    {
        private readonly ConcurrentDictionary<long, (int bet, string? betType, int? selectedNumber)> _games = new();
        private readonly Dictionary<string, (string name, string emoji, double multiplier)> _betTypes = new()
        {
            { "even", ("Чёт", "2️⃣", 2.0) },
            { "odd", ("Нечет", "1️⃣", 2.0) },
            { "high", ("Больше 3", "⬆️", 2.0) },
            { "low", ("Меньше 4", "⬇️", 2.0) },
            { "exact", ("Точное число", "🎯", 6.0) }
        };

        private readonly Dictionary<long, (string BetType, int Amount)> _pendingBets;

        public DiceGame(ITelegramBotClient bot, UserService userService)
            : base(bot, userService)
        {
            _pendingBets = new Dictionary<long, (string, int)>();
        }

        public override async Task ShowRules(long chatId)
        {
            var text = "🎲 Кости\n\n" +
                      "Правила:\n" +
                      "1. Сделайте ставку\n" +
                      "2. Выберите тип ставки\n" +
                      "3. Бросьте кости\n\n" +
                      "Типы ставок:\n" +
                      "2️⃣ Чёт (x2.0) - выпадет чётное число\n" +
                      "1️⃣ Нечет (x2.0) - выпадет нечётное число\n" +
                      "⬆️ Больше 3 (x2.0) - выпадет 4, 5 или 6\n" +
                      "⬇️ Меньше 4 (x2.0) - выпадет 1, 2 или 3\n" +
                      "🎯 Точное число (x6.0) - угадайте точное число";

            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🎲 Сделать ставку", "dice_bet"),
                    InlineKeyboardButton.WithCallbackData("🔙 Назад", "games_menu")
                }
            });

            await _bot.SendTextMessageAsync(chatId, text, replyMarkup: keyboard);
        }

        public override async Task HandleCommand(long chatId, long userId, string command)
        {
            switch (command)
            {
                case "dice_bet":
                    await ShowBetMenu(chatId, "dice_game");
                    break;

                case var c when c.StartsWith("dice_type_"):
                    if (_pendingBets.TryGetValue(userId, out var bet))
                    {
                        var betType = command.Replace("dice_type_", "");
                        await ProcessBetType(chatId, userId, betType, bet.Amount);
                    }
                    else
                    {
                        await _bot.SendTextMessageAsync(
                            chatId,
                            "❌ Сначала сделайте ставку!"
                        );
                    }
                    break;

                case "dice_roll":
                    await RollDice(chatId, userId);
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

            _pendingBets[userId] = ("", amount);

            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("2️⃣ Чёт", "dice_type_even"),
                    InlineKeyboardButton.WithCallbackData("1️⃣ Нечет", "dice_type_odd")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("⬆️ Больше 3", "dice_type_high"),
                    InlineKeyboardButton.WithCallbackData("⬇️ Меньше 4", "dice_type_low")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("1️⃣", "dice_type_1"),
                    InlineKeyboardButton.WithCallbackData("2️⃣", "dice_type_2"),
                    InlineKeyboardButton.WithCallbackData("3️⃣", "dice_type_3")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("4️⃣", "dice_type_4"),
                    InlineKeyboardButton.WithCallbackData("5️⃣", "dice_type_5"),
                    InlineKeyboardButton.WithCallbackData("6️⃣", "dice_type_6")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🔙 Отмена", "dice_game")
                }
            });

            await _bot.SendTextMessageAsync(
                chatId,
                $"Ставка: {amount} монет\nВыберите тип ставки:",
                replyMarkup: keyboard
            );
        }

        private async Task ProcessBetType(long chatId, long userId, string betType, int amount)
        {
            var user = _userService.GetOrCreateUser(userId);
            _userService.UpdateBalance(userId, -amount);
            _pendingBets[userId] = (betType, amount);

            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🎲 Бросить кости", "dice_roll")
                }
            });

            string betDescription = betType switch
            {
                "even" => "Чёт (x2.0)",
                "odd" => "Нечет (x2.0)",
                "high" => "Больше 3 (x2.0)",
                "low" => "Меньше 4 (x2.0)",
                _ when betType.Length == 1 => $"Точное число {betType} (x6.0)",
                _ => "Неизвестная ставка"
            };

            await _bot.SendTextMessageAsync(
                chatId,
                $"Ставка: {amount} монет\n" +
                $"Тип: {betDescription}\n\n" +
                "Нажмите кнопку, чтобы бросить кости!",
                replyMarkup: keyboard
            );
        }

        private async Task RollDice(long chatId, long userId)
        {
            if (!_pendingBets.TryGetValue(userId, out var bet))
            {
                await _bot.SendTextMessageAsync(
                    chatId,
                    "❌ Сначала сделайте ставку!"
                );
                return;
            }

            var (betType, amount) = bet;
            var roll = _random.Next(1, 7);
            var win = false;
            var multiplier = 1.0;

            switch (betType)
            {
                case "even":
                    win = roll % 2 == 0;
                    multiplier = 2.0;
                    break;
                case "odd":
                    win = roll % 2 != 0;
                    multiplier = 2.0;
                    break;
                case "high":
                    win = roll > 3;
                    multiplier = 2.0;
                    break;
                case "low":
                    win = roll < 4;
                    multiplier = 2.0;
                    break;
                default:
                    if (int.TryParse(betType, out int exactNumber))
                    {
                        win = roll == exactNumber;
                        multiplier = 6.0;
                    }
                    break;
            }

            var winAmount = win ? (int)(amount * multiplier) : 0;
            if (win)
            {
                _userService.UpdateBalance(userId, winAmount);
            }

            _pendingBets.Remove(userId);

            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🎲 Играть снова", "dice_bet"),
                    InlineKeyboardButton.WithCallbackData("🏠 В меню", "games_menu")
                }
            });

            var diceEmoji = roll switch
            {
                1 => "1️⃣",
                2 => "2️⃣",
                3 => "3️⃣",
                4 => "4️⃣",
                5 => "5️⃣",
                6 => "6️⃣",
                _ => "🎲"
            };

            var resultText = win
                ? $"🎉 Победа!\nВыпало: {diceEmoji}\nВыигрыш: {winAmount} монет"
                : $"😢 Проигрыш\nВыпало: {diceEmoji}\nПроигрыш: {amount} монет";

            await _bot.SendTextMessageAsync(
                chatId,
                resultText,
                replyMarkup: keyboard
            );

            _userService.AddGameHistory(
                userId,
                "Кости",
                amount,
                win,
                $"Выпало: {roll}, ставка: {betType}"
            );
        }
    }
} 