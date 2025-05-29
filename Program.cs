using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Microsoft.AspNetCore.HttpOverrides;
using TelegramGameBot.Services;
using TelegramGameBot.Services.Games;

var builder = WebApplication.CreateBuilder(args);

// Отключаем HTTPS перенаправление
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

builder.Services.AddControllers().AddNewtonsoftJson();

// Подключение BotToken из конфига или переменных окружения
var botToken = Environment.GetEnvironmentVariable("BOT_TOKEN") ?? builder.Configuration["TelegramBot:Token"];
if (string.IsNullOrEmpty(botToken))
    throw new Exception("Не указан токен бота! Укажите BOT_TOKEN в переменных окружения или TelegramBot:Token в appsettings.json");

// Получаем URL для вебхука
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
var baseUrl = Environment.GetEnvironmentVariable("BASE_URL") ?? builder.Configuration["TelegramBot:BaseUrl"];
if (string.IsNullOrEmpty(baseUrl))
{
    // Для локальной разработки используем ngrok или аналогичный сервис
    Console.WriteLine("ВНИМАНИЕ: URL для вебхука не указан!");
    Console.WriteLine("Для локальной разработки используйте ngrok или аналогичный сервис");
    Console.WriteLine("и укажите URL в appsettings.json или переменной окружения BASE_URL");
    return;
}

// Регистрируем сервисы как синглтоны
builder.Services.AddSingleton<UserService>();
builder.Services.AddSingleton<ITelegramBotClient>(_ => new TelegramBotClient(botToken));

// Регистрируем игры как синглтоны
builder.Services.AddSingleton<MinesweeperGame>();
builder.Services.AddSingleton<DiceGame>();
builder.Services.AddSingleton<WheelGame>();
builder.Services.AddSingleton<SlotsGame>();
builder.Services.AddSingleton<CoinGame>();

// Регистрируем GameService как синглтон
builder.Services.AddSingleton<IGameService, GameService>();

var app = builder.Build();

// Используем ForwardedHeaders
app.UseForwardedHeaders();

try
{
    // Настраиваем вебхук при запуске приложения
    var botClient = app.Services.GetRequiredService<ITelegramBotClient>();
    await botClient.DeleteWebhookAsync(); // Удаляем старый вебхук
    await Task.Delay(1000); // Ждем секунду
    
    await botClient.SetWebhookAsync(
        url: $"{baseUrl}/api/webhook",
        allowedUpdates: new[] 
        { 
            UpdateType.Message,
            UpdateType.CallbackQuery
        }
    );

    Console.WriteLine($"Бот запущен и слушает на {baseUrl}/api/webhook");
}
catch (Exception ex)
{
    Console.WriteLine($"Ошибка при настройке вебхука: {ex.Message}");
    Console.WriteLine("Проверьте доступность URL и правильность настроек");
    return;
}

app.MapControllers();

// Добавляем простой эндпоинт для проверки работоспособности
app.MapGet("/", () => "Бот работает!");

// Запускаем приложение без HTTPS
await app.RunAsync($"http://0.0.0.0:{port}");
