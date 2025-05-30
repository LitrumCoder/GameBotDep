using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Microsoft.AspNetCore.HttpOverrides;
using TelegramGameBot.Services;
using TelegramGameBot.Services.Games;
using Telegram.Bot.Polling;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers().AddNewtonsoftJson();

// Подключение BotToken из конфига или переменных окружения
var botToken = Environment.GetEnvironmentVariable("BOT_TOKEN") ?? builder.Configuration["TelegramBot:Token"];
if (string.IsNullOrEmpty(botToken))
    throw new Exception("Не указан токен бота! Укажите BOT_TOKEN в переменных окружения или TelegramBot:Token в appsettings.json");

// Определяем режим работы (Replit или локальный)
var isReplit = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("REPL_ID"));
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";

// Регистрируем сервисы
builder.Services.AddSingleton<UserService>();
builder.Services.AddSingleton<ITelegramBotClient>(_ => new TelegramBotClient(botToken));
builder.Services.AddSingleton<MinesweeperGame>();
builder.Services.AddSingleton<DiceGame>();
builder.Services.AddSingleton<WheelGame>();
builder.Services.AddSingleton<SlotsGame>();
builder.Services.AddSingleton<CoinGame>();
builder.Services.AddSingleton<IGameService, GameService>();

var app = builder.Build();

var botClient = app.Services.GetRequiredService<ITelegramBotClient>();
var gameService = app.Services.GetRequiredService<IGameService>();

try
{
    if (isReplit)
    {
        // Режим Replit с webhook
        await botClient.DeleteWebhookAsync();
        await Task.Delay(1000);

        // Получаем URL для Replit
        var replitSlug = Environment.GetEnvironmentVariable("REPL_SLUG");
        var replitOwner = Environment.GetEnvironmentVariable("REPL_OWNER");
        
        if (string.IsNullOrEmpty(replitSlug) || string.IsNullOrEmpty(replitOwner))
        {
            throw new Exception("Не удалось получить REPL_SLUG или REPL_OWNER из переменных окружения");
        }

        var webhookUrl = $"https://{replitSlug}.{replitOwner}.repl.co/api/webhook";
        Console.WriteLine($"Настраиваю webhook URL: {webhookUrl}");

        await botClient.SetWebhookAsync(
            url: webhookUrl,
            allowedUpdates: new[] { UpdateType.Message, UpdateType.CallbackQuery }
        );

        // Проверяем статус webhook
        var webhookInfo = await botClient.GetWebhookInfoAsync();
        if (!string.IsNullOrEmpty(webhookInfo.LastErrorMessage))
        {
            Console.WriteLine($"Ошибка webhook: {webhookInfo.LastErrorMessage}");
            if (webhookInfo.LastErrorDate.HasValue)
            {
                Console.WriteLine($"Время последней ошибки: {webhookInfo.LastErrorDate.Value:yyyy-MM-dd HH:mm:ss}");
            }
        }
        else
        {
            Console.WriteLine("Webhook успешно установлен");
        }

        app.UseForwardedHeaders(new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
        });

        app.MapControllers();
        Console.WriteLine($"Бот запущен на Replit и слушает на {webhookUrl}");
        await app.RunAsync($"http://0.0.0.0:{port}");
    }
    else
    {
        // Локальный режим с long polling
        await botClient.DeleteWebhookAsync();

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = new[] { UpdateType.Message, UpdateType.CallbackQuery }
        };

        botClient.StartReceiving(
            updateHandler: async (client, update, token) =>
            {
                try
                {
                    await gameService.HandleUpdateAsync(update);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка при обработке обновления: {ex.Message}");
                }
            },
            pollingErrorHandler: (client, exception, token) =>
            {
                Console.WriteLine($"Ошибка получения обновлений: {exception.Message}");
                return Task.CompletedTask;
            },
            receiverOptions: receiverOptions
        );

        Console.WriteLine("Бот запущен локально в режиме Long Polling");
        Console.WriteLine("Нажмите Ctrl+C для остановки...");
        
        // Держим приложение запущенным
        await Task.Delay(-1);
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Ошибка при запуске бота: {ex.Message}");
    return;
}
