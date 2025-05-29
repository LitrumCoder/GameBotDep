using Telegram.Bot;
using Telegram.Bot.Types.ReplyMarkups;
using System.Collections.Generic;

namespace TelegramGameBot.Services.Games
{
    public abstract class BaseGame : IGame
    {
        protected readonly ITelegramBotClient _bot;
        protected readonly UserService _userService;
        protected readonly Random _random;

        protected BaseGame(ITelegramBotClient bot, UserService userService)
        {
            _bot = bot;
            _userService = userService;
            _random = new Random();
        }

        public abstract Task HandleCommand(long chatId, long userId, string command);
        public abstract Task ProcessBet(long chatId, long userId, int amount);
        public abstract Task ShowRules(long chatId);
        public virtual Task HandleCallback(long chatId, long userId, string command) => Task.CompletedTask;

        protected int[] GetDefaultBetOptions()
        {
            return new[] { 10, 50, 100, 250, 500, 1000, 2500, 5000 };
        }

        protected async Task ShowBetMenu(long chatId, string backCommand)
        {
            var betOptions = GetDefaultBetOptions();
            var buttons = new List<List<InlineKeyboardButton>>();

            foreach (var row in betOptions.Chunk(3))
            {
                buttons.Add(row.Select(amount =>
                    InlineKeyboardButton.WithCallbackData(
                        $"{amount} 💰",
                        $"bet_{amount}"
                    )).ToList());
            }

            buttons.Add(new List<InlineKeyboardButton>
            {
                InlineKeyboardButton.WithCallbackData("🔙 Назад", backCommand)
            });

            await _bot.SendTextMessageAsync(
                chatId: chatId,
                text: "Выберите сумму ставки:",
                replyMarkup: new InlineKeyboardMarkup(buttons)
            );
        }
    }
} 