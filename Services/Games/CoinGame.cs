using Telegram.Bot;
using Telegram.Bot.Types.ReplyMarkups;

namespace TelegramGameBot.Services.Games
{
    public class CoinGame : BaseGame
    {
        private readonly Dictionary<string, (string name, string emoji)> _sides = new()
        {
            { "heads", ("Орёл", "🦅") },
            { "tails", ("Решка", "👑") }
        };

        private readonly Dictionary<long, (string Side, int Amount)> _pendingBets;

        public CoinGame(ITelegramBotClient bot, UserService userService)
            : base(bot, userService)
        {
            _pendingBets = new Dictionary<long, (string, int)>();
        }

        public override async Task ShowRules(long chatId)
        {
            var text = "🪙 Монетка\n\n" +
                      "Правила:\n" +
                      "1. Сделайте ставку\n" +
                      "2. Выберите сторону монеты\n" +
                      "3. Подбросьте монету\n\n" +
                      "Выигрыш:\n" +
                      "🦅 Орёл - x2.0\n" +
                      "👑 Решка - x2.0";

            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🪙 Сделать ставку", "coin_bet"),
                    InlineKeyboardButton.WithCallbackData("🔙 Назад", "games_menu")
                }
            });

            await _bot.SendTextMessageAsync(chatId, text, replyMarkup: keyboard);
        }

        public override async Task HandleCommand(long chatId, long userId, string command)
        {
            switch (command)
            {
                case "coin_bet":
                    await ShowBetMenu(chatId, "coin_game");
                    break;

                case "coin_heads":
                case "coin_tails":
                    if (_pendingBets.TryGetValue(userId, out var bet) && string.IsNullOrEmpty(bet.Side))
                    {
                        await ProcessSideSelection(chatId, userId, command.Replace("coin_", ""));
                    }
                    else
                    {
                        await _bot.SendTextMessageAsync(
                            chatId,
                            "❌ Сначала сделайте ставку!"
                        );
                    }
                    break;

                case "coin_flip":
                    await FlipCoin(chatId, userId);
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
            _pendingBets[userId] = ("", amount);

            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🦅 Орёл", "coin_heads"),
                    InlineKeyboardButton.WithCallbackData("👑 Решка", "coin_tails")
                }
            });

            await _bot.SendTextMessageAsync(
                chatId,
                $"Ставка: {amount} монет\nВыберите сторону монеты:",
                replyMarkup: keyboard
            );
        }

        private async Task ProcessSideSelection(long chatId, long userId, string side)
        {
            if (!_pendingBets.TryGetValue(userId, out var bet))
            {
                await _bot.SendTextMessageAsync(
                    chatId,
                    "❌ Сначала сделайте ставку!"
                );
                return;
            }

            _pendingBets[userId] = (side, bet.Amount);

            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🪙 Подбросить монету", "coin_flip")
                }
            });

            await _bot.SendTextMessageAsync(
                chatId,
                $"Ставка: {bet.Amount} монет\n" +
                $"Выбрана сторона: {_sides[side].emoji} {_sides[side].name}\n\n" +
                "Нажмите кнопку, чтобы подбросить монету!",
                replyMarkup: keyboard
            );
        }

        private async Task FlipCoin(long chatId, long userId)
        {
            if (!_pendingBets.TryGetValue(userId, out var bet))
            {
                await _bot.SendTextMessageAsync(
                    chatId,
                    "❌ Сначала сделайте ставку!"
                );
                return;
            }

            if (string.IsNullOrEmpty(bet.Side))
            {
                await _bot.SendTextMessageAsync(
                    chatId,
                    "❌ Сначала выберите сторону монеты!"
                );
                return;
            }

            var result = _random.Next(2) == 0 ? "heads" : "tails";
            var isWin = result == bet.Side;
            var winAmount = isWin ? bet.Amount * 2 : 0;

            if (winAmount > 0)
            {
                _userService.UpdateBalance(userId, winAmount);
            }

            _pendingBets.Remove(userId);

            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🪙 Играть снова", "coin_bet"),
                    InlineKeyboardButton.WithCallbackData("🏠 В меню", "games_menu")
                }
            });

            var resultText = isWin
                ? $"🎉 Победа!\n" +
                  $"Выпало: {_sides[result].emoji} {_sides[result].name}\n" +
                  $"Множитель: x2.0\n" +
                  $"Выигрыш: {winAmount} монет"
                : $"😢 Проигрыш\n" +
                  $"Выпало: {_sides[result].emoji} {_sides[result].name}\n" +
                  $"Проигрыш: {bet.Amount} монет";

            await _bot.SendTextMessageAsync(
                chatId,
                resultText,
                replyMarkup: keyboard
            );

            _userService.AddGameHistory(
                userId,
                "Монетка",
                bet.Amount,
                isWin,
                $"Выпало: {_sides[result].name}"
            );
        }
    }
} 