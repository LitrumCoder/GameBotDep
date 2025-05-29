using Telegram.Bot.Types;

namespace TelegramGameBot.Services
{
    public interface IGameService
    {
        Task HandleUpdateAsync(Update update);
    }
}