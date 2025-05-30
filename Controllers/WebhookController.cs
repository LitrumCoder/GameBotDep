using Microsoft.AspNetCore.Mvc;
using Telegram.Bot.Types;
using TelegramGameBot.Services;
using System.Text.Json;

namespace TelegramGameBot.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WebhookController : ControllerBase
    {
        private readonly IGameService _gameService;
        private readonly ILogger<WebhookController> _logger;

        public WebhookController(IGameService gameService, ILogger<WebhookController> logger)
        {
            _gameService = gameService;
            _logger = logger;
        }

        [HttpPost]
        public async Task<IActionResult> Post([FromBody] Update update)
        {
            try
            {
                // Подробное логирование входящего запроса
                _logger.LogInformation("Получен webhook запрос");
                _logger.LogInformation("Headers: {Headers}", JsonSerializer.Serialize(Request.Headers));
                _logger.LogInformation("Update: {Update}", JsonSerializer.Serialize(update));

                if (update == null)
                {
                    _logger.LogWarning("Получен пустой update");
                    return BadRequest("Update object is null");
                }

                _logger.LogInformation("Тип обновления: {UpdateType}", update.Type);
                await _gameService.HandleUpdateAsync(update);
                
                _logger.LogInformation("Обновление успешно обработано");
                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обработке обновления");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet]
        public IActionResult Get()
        {
            var info = new
            {
                Status = "OK",
                Timestamp = DateTime.UtcNow,
                Headers = Request.Headers,
                Host = Request.Host.Value,
                Scheme = Request.Scheme
            };

            _logger.LogInformation("Проверка webhook endpoint: {Info}", JsonSerializer.Serialize(info));
            return Ok(info);
        }
    }
}