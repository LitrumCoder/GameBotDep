using Telegram.Bot;
using Telegram.Bot.Types.ReplyMarkups;
using System.Collections.Concurrent;

namespace TelegramGameBot.Services.Games
{
    public class WheelGameState
    {
        public int CurrentBet { get; set; }
        public bool IsSpinning { get; set; }
        public DateTime LastSpinTime { get; set; }
        public double WinMultiplier { get; set; }

        public WheelGameState(int bet)
        {
            CurrentBet = bet;
            IsSpinning = false;
            LastSpinTime = DateTime.Now;
            WinMultiplier = 0;
        }
    }

    public class WheelGame : BaseGame
    {
        private readonly Dictionary<string, (string name, string emoji, double multiplier)> _sectors = new()
        {
            { "diamond", ("Бриллиант", "💎", 5.0) },
            { "target", ("Мишень", "🎯", 3.0) },
            { "dice", ("Кубик", "🎲", 2.0) },
            { "star", ("Звезда", "⭐️", 1.5) },
            { "x", ("Проигрыш", "❌", 0.0) }
        };

        private readonly Dictionary<long, int> _pendingBets;

        public WheelGame(ITelegramBotClient bot, UserService userService)
            : base(bot, userService)
        {
            _pendingBets = new Dictionary<long, int>();
        }

        public override async Task ShowRules(long chatId)
        {
            var text = "🎡 Колесо Фортуны\n\n" +
                      "Правила:\n" +
                      "1. Сделайте ставку\n" +
                      "2. Крутите колесо\n" +
                      "3. Получите выигрыш в зависимости от множителя\n\n" +
                      "Множители:\n" +
                      "💎 Бриллиант - x5.0\n" +
                      "🎯 Мишень - x3.0\n" +
                      "🎲 Кубик - x2.0\n" +
                      "⭐️ Звезда - x1.5\n" +
                      "❌ Проигрыш - x0.0";

            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🎡 Сделать ставку", "wheel_bet"),
                    InlineKeyboardButton.WithCallbackData("🔙 Назад", "games_menu")
                }
            });

            await _bot.SendTextMessageAsync(chatId, text, replyMarkup: keyboard);
        }

        public override async Task HandleCommand(long chatId, long userId, string command)
        {
            switch (command)
            {
                case "wheel_bet":
                    await ShowBetMenu(chatId, "wheel_game");
                    break;

                case "wheel_spin":
                    await SpinWheel(chatId, userId);
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
                    InlineKeyboardButton.WithCallbackData("🎡 Крутить колесо", "wheel_spin")
                }
            });

            await _bot.SendTextMessageAsync(
                chatId,
                $"Ставка: {amount} монет\nНажмите кнопку, чтобы крутить колесо!",
                replyMarkup: keyboard
            );
        }

        private async Task SpinWheel(long chatId, long userId)
        {
            if (!_pendingBets.TryGetValue(userId, out var bet))
            {
                await _bot.SendTextMessageAsync(
                    chatId,
                    "❌ Сначала сделайте ставку!"
                );
                return;
            }

            var sectors = _sectors.ToList();
            var result = sectors[_random.Next(sectors.Count)];
            var winAmount = (int)(bet * result.Value.multiplier);

            if (winAmount > 0)
            {
                _userService.UpdateBalance(userId, winAmount);
            }

            _pendingBets.Remove(userId);

            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🎡 Играть снова", "wheel_bet"),
                    InlineKeyboardButton.WithCallbackData("🏠 В меню", "games_menu")
                }
            });

            var resultText = winAmount > 0
                ? $"🎉 Победа!\n" +
                  $"Выпало: {result.Value.emoji} {result.Value.name}\n" +
                  $"Множитель: x{result.Value.multiplier}\n" +
                  $"Выигрыш: {winAmount} монет"
                : $"😢 Проигрыш\n" +
                  $"Выпало: {result.Value.emoji} {result.Value.name}\n" +
                  $"Проигрыш: {bet} монет";

            await _bot.SendTextMessageAsync(
                chatId,
                resultText,
                replyMarkup: keyboard
            );

            _userService.AddGameHistory(
                userId,
                "Колесо Фортуны",
                bet,
                winAmount > 0,
                $"Выпало: {result.Value.name}"
            );
        }
    }
} 