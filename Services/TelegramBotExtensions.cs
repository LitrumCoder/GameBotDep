using Telegram.Bot;

namespace TelegramGameBot.Services
{
    public static class TelegramBotExtensions
    {
        private static int _lastMessageId;

        public static void SetLastMessageId(this ITelegramBotClient bot, int messageId)
        {
            _lastMessageId = messageId;
        }

        public static int GetLastMessageId(this ITelegramBotClient bot)
        {
            return _lastMessageId;
        }
    }
} 