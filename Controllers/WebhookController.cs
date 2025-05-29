using Microsoft.AspNetCore.Mvc;
using Telegram.Bot.Types;
using TelegramGameBot.Services;

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
                _logger.LogInformation("Получено обновление: {UpdateType}", update.Type);
                await _gameService.HandleUpdateAsync(update);
                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обработке обновления");
                return StatusCode(500);
            }
        }

        [HttpGet]
        public IActionResult Get()
        {
            return Ok("Webhook endpoint is working!");
        }
    }
}