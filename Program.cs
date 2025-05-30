using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Microsoft.AspNetCore.HttpOverrides;
using TelegramGameBot.Services;
using TelegramGameBot.Services.Games;
using Telegram.Bot.Polling;
using System.Net.Http;

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
        Console.WriteLine("Запуск в режиме Replit...");
        
        await botClient.DeleteWebhookAsync();
        await Task.Delay(1000);

        // Получаем данные Replit
        var replitId = Environment.GetEnvironmentVariable("REPL_ID");
        var replitOwner = Environment.GetEnvironmentVariable("REPL_OWNER");
        var replitSlug = Environment.GetEnvironmentVariable("REPL_SLUG") ?? "TelegramGameBot";

        Console.WriteLine($"REPL_ID: {replitId}");
        Console.WriteLine($"REPL_OWNER: {replitOwner}");
        Console.WriteLine($"REPL_SLUG: {replitSlug}");

        // Формируем URL для webhook (новый формат Replit)
        var webhookUrl = $"https://{replitSlug.ToLower()}-{replitOwner.ToLower()}.repl.co/api/webhook";
        Console.WriteLine($"Настраиваю webhook URL: {webhookUrl}");

        try 
        {
            // Проверяем доступность URL перед установкой webhook
            using (var client = new HttpClient())
            {
                try
                {
                    var baseUrl = webhookUrl.Replace("/api/webhook", "");
                    Console.WriteLine($"Проверка доступности базового URL: {baseUrl}");
                    var response = await client.GetAsync(baseUrl);
                    Console.WriteLine($"Статус ответа: {response.StatusCode}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Предупреждение при проверке URL: {ex.Message}");
                    // Продолжаем выполнение, так как ошибка может быть связана с тем,
                    // что сервер еще не полностью запущен
                }
            }

            Console.WriteLine("Попытка установки webhook...");
            await botClient.SetWebhookAsync(
                url: webhookUrl,
                allowedUpdates: new[] { UpdateType.Message, UpdateType.CallbackQuery },
                dropPendingUpdates: true
            );

            // Проверяем статус webhook
            var webhookInfo = await botClient.GetWebhookInfoAsync();
            Console.WriteLine($"Webhook info: {webhookInfo.Url}");
            
            if (!string.IsNullOrEmpty(webhookInfo.LastErrorMessage))
            {
                Console.WriteLine($"Последняя ошибка webhook: {webhookInfo.LastErrorMessage}");
                if (webhookInfo.LastErrorDate.HasValue)
                {
                    Console.WriteLine($"Время ошибки: {webhookInfo.LastErrorDate.Value:yyyy-MM-dd HH:mm:ss}");
                }
            }
            else
            {
                Console.WriteLine("Webhook успешно установлен");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка при установке webhook: {ex.Message}");
            throw;
        }

        app.UseForwardedHeaders(new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
        });

        // Добавляем middleware для логирования запросов
        app.Use(async (context, next) =>
        {
            Console.WriteLine($"Получен запрос: {context.Request.Method} {context.Request.Path}");
            await next();
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
        
        await Task.Delay(-1);
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Ошибка при запуске бота: {ex.Message}");
    return;
}
