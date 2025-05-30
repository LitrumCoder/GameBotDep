using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Microsoft.AspNetCore.HttpOverrides;
using TelegramGameBot.Services;
using TelegramGameBot.Services.Games;
using Telegram.Bot.Polling;
using System.Net.Http;
using System.Text.Json;

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

        // Настраиваем ngrok
        Console.WriteLine("Настройка ngrok...");
        var ngrokUrl = await GetNgrokUrl();
        
        if (string.IsNullOrEmpty(ngrokUrl))
        {
            throw new Exception("Не удалось получить URL от ngrok");
        }

        var webhookUrl = $"{ngrokUrl.TrimEnd('/')}/api/webhook";
        Console.WriteLine($"Настраиваю webhook URL: {webhookUrl}");

        try 
        {
            // Проверяем доступность URL
            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(10);
                try
                {
                    Console.WriteLine($"Проверка доступности базового URL...");
                    var response = await client.GetAsync(ngrokUrl);
                    Console.WriteLine($"Ответ сервера: {response.StatusCode}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка при проверке URL: {ex.Message}");
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
            Console.WriteLine($"Webhook info: {JsonSerializer.Serialize(webhookInfo)}");
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

// Функция для получения URL от ngrok
async Task<string> GetNgrokUrl()
{
    try
    {
        using var client = new HttpClient();
        var response = await client.GetAsync("http://localhost:4040/api/tunnels");
        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            var tunnels = JsonSerializer.Deserialize<NgrokTunnels>(content);
            var httpsUrl = tunnels?.Tunnels?.FirstOrDefault(t => t.Proto == "https")?.PublicUrl;
            if (!string.IsNullOrEmpty(httpsUrl))
            {
                return httpsUrl;
            }
        }
        
        Console.WriteLine("Не удалось получить URL от ngrok API, использую переменную окружения...");
        return Environment.GetEnvironmentVariable("NGROK_URL");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Ошибка при получении URL от ngrok: {ex.Message}");
        return Environment.GetEnvironmentVariable("NGROK_URL");
    }
}

// Классы для десериализации ответа ngrok API
public class NgrokTunnels
{
    public List<NgrokTunnel> Tunnels { get; set; }
}

public class NgrokTunnel
{
    public string Name { get; set; }
    public string PublicUrl { get; set; }
    public string Proto { get; set; }
}
