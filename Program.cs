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

        // Получаем и проверяем данные Replit
        var replitId = Environment.GetEnvironmentVariable("REPL_ID");
        var replitOwner = Environment.GetEnvironmentVariable("REPL_OWNER")?.ToLower();
        var replitSlug = Environment.GetEnvironmentVariable("REPL_SLUG")?.ToLower() ?? "telegramgamebot";

        if (string.IsNullOrEmpty(replitOwner))
        {
            throw new Exception("REPL_OWNER не установлен в переменных окружения");
        }

        Console.WriteLine($"REPL_ID: {replitId}");
        Console.WriteLine($"REPL_OWNER: {replitOwner}");
        Console.WriteLine($"REPL_SLUG: {replitSlug}");

        // Пробуем разные форматы URL для webhook
        var possibleUrls = new[]
        {
            $"https://{replitSlug.ToLower()}-{replitOwner.ToLower()}.repl.co/api/webhook",
            $"https://telegramgamebot-{replitOwner.ToLower()}.repl.co/api/webhook",
            $"https://{replitId}.id.repl.co/api/webhook"
        };

        Console.WriteLine("Доступные переменные окружения:");
        foreach (var env in Environment.GetEnvironmentVariables().Keys)
        {
            Console.WriteLine($"{env}: {Environment.GetEnvironmentVariable(env?.ToString() ?? "")}");
        }

        Exception lastException = null;
        bool webhookSet = false;

        foreach (var url in possibleUrls)
        {
            try
            {
                Console.WriteLine($"\nПробую установить webhook на URL: {url}");
                
                // Проверяем DNS резолвинг
                try
                {
                    var host = new Uri(url).Host;
                    Console.WriteLine($"Проверка DNS для {host}...");
                    var addresses = await System.Net.Dns.GetHostAddressesAsync(host);
                    Console.WriteLine($"DNS резолвинг успешен. IP адреса: {string.Join(", ", addresses.Select(a => a.ToString()))}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка DNS резолвинга: {ex.Message}");
                    continue;
                }

                // Проверяем базовый URL
                var baseUrl = url.Replace("/api/webhook", "");
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(10);
                    try
                    {
                        Console.WriteLine($"Проверка доступности: {baseUrl}");
                        var response = await client.GetAsync(baseUrl);
                        Console.WriteLine($"Ответ сервера: {response.StatusCode}");
                        
                        // Читаем тело ответа для дополнительной диагностики
                        var content = await response.Content.ReadAsStringAsync();
                        Console.WriteLine($"Тело ответа: {content.Substring(0, Math.Min(content.Length, 200))}...");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Предупреждение при проверке {baseUrl}: {ex.Message}");
                        continue;
                    }
                }

                // Пытаемся установить webhook
                Console.WriteLine($"Попытка установки webhook на {url}...");
                await botClient.SetWebhookAsync(
                    url: url,
                    allowedUpdates: new[] { UpdateType.Message, UpdateType.CallbackQuery },
                    dropPendingUpdates: true
                );

                // Проверяем установку
                var webhookInfo = await botClient.GetWebhookInfoAsync();
                if (string.IsNullOrEmpty(webhookInfo.LastErrorMessage))
                {
                    Console.WriteLine($"Webhook успешно установлен на {url}");
                    webhookSet = true;
                    break;
                }
                else
                {
                    Console.WriteLine($"Ошибка webhook для {url}: {webhookInfo.LastErrorMessage}");
                    if (webhookInfo.LastErrorDate.HasValue)
                    {
                        Console.WriteLine($"Время последней ошибки: {webhookInfo.LastErrorDate.Value:yyyy-MM-dd HH:mm:ss}");
                    }
                    lastException = new Exception(webhookInfo.LastErrorMessage);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при установке webhook на {url}: {ex.Message}");
                lastException = ex;
            }
        }

        if (!webhookSet)
        {
            throw new Exception($"Не удалось установить webhook ни на один из URL. Последняя ошибка: {lastException?.Message}");
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
            Console.WriteLine($"Ответ отправлен: {context.Response.StatusCode}");
        });

        app.MapControllers();
        Console.WriteLine("Бот запущен на Replit");
        
        // Запускаем приложение
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
