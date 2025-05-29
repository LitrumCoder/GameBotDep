using Telegram.Bot;
using System.Collections.Generic;

namespace TelegramGameBot.Services
{
    public static class TelegramBotClientExtensions
    {
        private static readonly Dictionary<long, int> _lastMessageIds = new();

        public static void SaveLastMessageId(this ITelegramBotClient bot, int messageId)
        {
            var botId = bot.BotId ?? 0;
            _lastMessageIds[botId] = messageId;
        }

        public static int LoadLastMessageId(this ITelegramBotClient bot)
        {
            var botId = bot.BotId ?? 0;
            return _lastMessageIds.TryGetValue(botId, out var messageId) ? messageId : 0;
        }
    }
} 