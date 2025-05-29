using Telegram.Bot;
using Telegram.Bot.Types.ReplyMarkups;
using System.Threading.Tasks;
using Telegram.Bot.Types;

namespace TelegramGameBot.Services.Games
{
    public interface IGame
    {
        Task HandleCommand(long chatId, long userId, string command);
        Task ProcessBet(long chatId, long userId, int amount);
        Task ShowRules(long chatId);
    }
} 