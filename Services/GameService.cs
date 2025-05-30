using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramGameBot.Services.Games;
using System.Collections.Concurrent;

namespace TelegramGameBot.Services
{
    public class MinesGameState
    {
        public int Dimensions { get; set; }
        public int MinesCount { get; set; }
        public bool[,] Mines { get; set; }
        public bool[,] OpenedCells { get; set; }
        public int CurrentBet { get; set; }
        public double CurrentMultiplier { get; set; }
        public bool IsGameOver { get; set; }
        public bool IsFirstClick { get; set; }

        public MinesGameState(int dimensions, int minesCount, int bet)
        {
            Dimensions = dimensions;
            MinesCount = minesCount;
            Mines = new bool[dimensions, dimensions];
            OpenedCells = new bool[dimensions, dimensions];
            CurrentBet = bet;
            CurrentMultiplier = 1.0;
            IsGameOver = false;
            IsFirstClick = true;
        }

        public void PlaceMines(int excludeRow, int excludeCol)
        {
            var random = new Random();

            // Создаем список всех возможных позиций, кроме безопасной зоны вокруг первого клика
            var availablePositions = new List<(int row, int col)>();
            for (int i = 0; i < Dimensions; i++)
            {
                for (int j = 0; j < Dimensions; j++)
                {
                    if (Math.Abs(i - excludeRow) > 1 || Math.Abs(j - excludeCol) > 1)
                    {
                        availablePositions.Add((i, j));
                    }
                }
            }

            // Перемешиваем список доступных позиций
            for (int i = availablePositions.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                var temp = availablePositions[i];
                availablePositions[i] = availablePositions[j];
                availablePositions[j] = temp;
            }

            // Размещаем мины в первых MinesCount позициях
            for (int i = 0; i < Math.Min(MinesCount, availablePositions.Count); i++)
            {
                var (row, col) = availablePositions[i];
                Mines[row, col] = true;
            }
        }
    }

    public class GameService : IGameService
    {
        private readonly ITelegramBotClient _botClient;
        private readonly UserService _userService;
        private readonly DiceGame _diceGame;
        private readonly WheelGame _wheelGame;
        private readonly CoinGame _coinGame;
        private readonly SlotsGame _slotsGame;
        private readonly MinesweeperGame _minesweeperGame;

        public GameService(
            ITelegramBotClient botClient,
            UserService userService,
            DiceGame diceGame,
            WheelGame wheelGame,
            CoinGame coinGame,
            SlotsGame slotsGame,
            MinesweeperGame minesweeperGame)
        {
            _botClient = botClient;
            _userService = userService;
            _diceGame = diceGame;
            _wheelGame = wheelGame;
            _coinGame = coinGame;
            _slotsGame = slotsGame;
            _minesweeperGame = minesweeperGame;
        }

        public async Task HandleUpdateAsync(Update update)
        {
            try
            {
                switch (update.Type)
                {
                    case UpdateType.Message:
                        await HandleMessageAsync(update.Message!);
                        break;
                    case UpdateType.CallbackQuery:
                        await HandleCallbackQueryAsync(update.CallbackQuery!);
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при обработке обновления: {ex}");
                
                if (update.Message != null)
                {
                    await _botClient.SendTextMessageAsync(
                        update.Message.Chat.Id,
                        "❌ Произошла ошибка. Попробуйте еще раз или вернитесь в главное меню.",
                        replyMarkup: GetMainMenuKeyboard()
                    );
                }
            }
        }

        private async Task HandleMessageAsync(Message message)
        {
            if (message?.Text == null) return;

            var chatId = message.Chat.Id;
            var userId = message.From?.Id;

            if (userId == null) return;

            var commandText = message.Text.ToLower();
            var user = _userService.GetOrCreateUser(userId.Value);

            switch (commandText)
            {
                case "/start":
                    await ShowMainMenu(chatId);
                    break;

                case "/balance":
                    await ShowBalance(chatId, userId.Value);
                    break;

                case "/help":
                    await ShowHelp(chatId);
                    break;

                default:
                    if (commandText.StartsWith("/promo_"))
                    {
                        var code = commandText.Replace("/promo_", "").ToUpper();
                        var (success, amount, promoMessage) = _userService.UsePromoCode(userId.Value, code);
                        await _botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: promoMessage,
                            replyMarkup: GetMainMenuKeyboard()
                        );
                    }
                    break;
            }
        }

        private async Task HandleCallbackQueryAsync(CallbackQuery callbackQuery)
        {
            var chatId = callbackQuery.Message?.Chat.Id;
            if (chatId == null) return;

            try
            {
                var command = callbackQuery.Data?.ToLower();
                var userId = callbackQuery.From.Id;
                var user = _userService.GetOrCreateUser(userId);

                if (command?.StartsWith("bet_") == true)
                {
                    var betAmount = int.Parse(command.Substring(4));
                    if (user.Balance < betAmount)
                    {
                        await _botClient.AnswerCallbackQueryAsync(
                            callbackQuery.Id,
                            "❌ Недостаточно средств!",
                            true
                        );
                        return;
                    }

                    switch (user.CurrentGame)
                    {
                        case "dice":
                            await _diceGame.ProcessBet(chatId.Value, userId, betAmount);
                            break;
                        case var game when game?.StartsWith("minesweeper_") == true:
                            await _minesweeperGame.ProcessBet(chatId.Value, userId, betAmount);
                            break;
                        case "wheel":
                            await _wheelGame.ProcessBet(chatId.Value, userId, betAmount);
                            break;
                        case "slots":
                            await _slotsGame.ProcessBet(chatId.Value, userId, betAmount);
                            break;
                        case "coin":
                            await _coinGame.ProcessBet(chatId.Value, userId, betAmount);
                            break;
                        default:
                            await _botClient.SendTextMessageAsync(
                                chatId: chatId.Value,
                                text: "❌ Сначала выберите игру!"
                            );
                            break;
                    }
                }
                else
                {
                    switch (command)
                    {
                        case "games_menu":
                            await ShowGamesMenu(chatId.Value);
                            break;
                        case "dice_game":
                            _userService.SetCurrentGame(userId, "dice");
                            await _diceGame.ShowRules(chatId.Value);
                            break;
                        case "dice_bet":
                            await ShowBetOptions(chatId.Value, "dice");
                            break;
                        case var c when c?.StartsWith("dice_type_") == true:
                        case "dice_roll":
                            if (command != null)
                            {
                                await _diceGame.HandleCommand(chatId.Value, userId, command);
                            }
                            break;
                        case "wheel_game":
                            _userService.SetCurrentGame(userId, "wheel");
                            await _wheelGame.ShowRules(chatId.Value);
                            break;
                        case "wheel_bet":
                            await ShowBetOptions(chatId.Value, "wheel");
                            break;
                        case "wheel_spin":
                            await _wheelGame.HandleCommand(chatId.Value, userId, command);
                            break;
                        case "slots_game":
                            _userService.SetCurrentGame(userId, "slots");
                            await _slotsGame.ShowRules(chatId.Value);
                            break;
                        case "slots_bet":
                            await ShowBetOptions(chatId.Value, "slots");
                            break;
                        case "slots_spin":
                            await _slotsGame.HandleCommand(chatId.Value, userId, command);
                            break;
                        case "coin_game":
                            _userService.SetCurrentGame(userId, "coin");
                            await _coinGame.ShowRules(chatId.Value);
                            break;
                        case "coin_bet":
                            await ShowBetOptions(chatId.Value, "coin");
                            break;
                        case "coin_heads":
                        case "coin_tails":
                        case "coin_flip":
                            await _coinGame.HandleCommand(chatId.Value, userId, command);
                            break;
                        case "minesweeper":
                        case var c when c?.StartsWith("minesweeper_") == true:
                            if (command != null)
                            {
                                _userService.SetCurrentGame(userId, command);
                                await _minesweeperGame.HandleCommand(chatId.Value, userId, command);
                            }
                            break;
                        case "balance":
                            await ShowBalance(chatId.Value, userId);
                            break;
                        case "promocodes":
                            await ShowPromoCodes(chatId.Value);
                            break;
                        case "history":
                            await ShowHistory(chatId.Value, userId);
                            break;
                        case "back_to_menu":
                            await ShowMainMenu(chatId.Value);
                            break;
                        case "place_bet":
                            await ShowBetOptions(chatId.Value, user.CurrentGame ?? "dice");
                            break;
                        default:
                            if (user.CurrentGame?.StartsWith("minesweeper") == true)
                            {
                                await _minesweeperGame.HandleCommand(chatId.Value, userId, command ?? "");
                            }
                            else if (user.CurrentGame == "wheel")
                            {
                                await _wheelGame.HandleCallback(chatId.Value, userId, command ?? "");
                            }
                            else if (user.CurrentGame == "slots")
                            {
                                await _slotsGame.HandleCallback(chatId.Value, userId, command ?? "");
                            }
                            else if (user.CurrentGame == "coin")
                            {
                                await _coinGame.HandleCallback(chatId.Value, userId, command ?? "");
                            }
                            break;
                    }
                }

                await _botClient.AnswerCallbackQueryAsync(callbackQuery.Id);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при обработке callback query: {ex}");
                await _botClient.AnswerCallbackQueryAsync(
                    callbackQuery.Id,
                    "❌ Произошла ошибка. Попробуйте еще раз.",
                    true
                );
            }
        }

        private async Task ShowMainMenu(long chatId)
        {
            var text = "🎰 Добро пожаловать в игровой бот!\n\n" +
                      "Выберите действие из меню ниже:";

            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: text,
                replyMarkup: GetMainMenuKeyboard()
            );
        }

        private async Task ShowGamesMenu(long chatId)
        {
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new []
                {
                    InlineKeyboardButton.WithCallbackData("🎲 Кости", "dice_game"),
                    InlineKeyboardButton.WithCallbackData("💣 Сапёр", "minesweeper")
                },
                new []
                {
                    InlineKeyboardButton.WithCallbackData("🎡 Колесо", "wheel_game"),
                    InlineKeyboardButton.WithCallbackData("🎰 Слоты", "slots_game")
                },
                new []
                {
                    InlineKeyboardButton.WithCallbackData("🪙 Монетка", "coin_game")
                },
                new []
                {
                    InlineKeyboardButton.WithCallbackData("🔙 Назад", "back_to_menu")
                }
            });

            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "🎮 Выберите игру:",
                replyMarkup: keyboard
            );
        }

        private async Task ShowPromoCodes(long chatId)
        {
            var text = "🎁 Промокоды\n\n" +
                      "Чтобы использовать промокод, отправьте его в формате:\n" +
                      "/promo_КОД\n\n" +
                      "Например: /promo_START";

            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new []
                {
                    InlineKeyboardButton.WithCallbackData("🔙 Назад", "back_to_menu")
                }
            });

            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: text,
                replyMarkup: keyboard
            );
        }

        private async Task ShowHistory(long chatId, long userId)
        {
            var history = _userService.GetUserHistory(userId);
            var messageText = "📊 История последних игр:\n\n";

            if (history.Count == 0)
            {
                messageText += "История пуста. Сыграйте первую игру!";
            }
            else
            {
                foreach (var game in history)
                {
                    var result = game.IsWin ? "✅ Выигрыш" : "❌ Проигрыш";
                    messageText += $"{game.GameName} - Ставка: {game.Bet} 💰 - {result}\n" +
                                 $"Результат: {game.Result} - {game.Timestamp:HH:mm:ss}\n\n";
                }
            }

            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new []
                {
                    InlineKeyboardButton.WithCallbackData("🎮 Играть", "games_menu"),
                    InlineKeyboardButton.WithCallbackData("🔙 Назад", "back_to_menu")
                }
            });

            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: messageText,
                replyMarkup: keyboard
            );
        }

        private async Task ShowBalance(long chatId, long userId)
        {
            var user = _userService.GetOrCreateUser(userId);
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new []
                {
                    InlineKeyboardButton.WithCallbackData("🎮 Играть", "games_menu"),
                    InlineKeyboardButton.WithCallbackData("🔙 Назад", "back_to_menu")
                }
            });

            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: $"💰 Ваш баланс: {user.Balance} монет",
                replyMarkup: keyboard
            );
        }

        private async Task ShowHelp(long chatId)
        {
            var text = "ℹ️ Помощь\n\n" +
                      "Доступные команды:\n" +
                      "/start - Главное меню\n" +
                      "/balance - Проверить баланс\n" +
                      "/promo_КОД - Активировать промокод\n" +
                      "/help - Это сообщение\n\n" +
                      "По всем вопросам обращайтесь к @admin";

            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new []
                {
                    InlineKeyboardButton.WithCallbackData("🔙 В меню", "back_to_menu")
                }
            });

            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: text,
                replyMarkup: keyboard
            );
        }

        private InlineKeyboardMarkup GetMainMenuKeyboard()
        {
            return new InlineKeyboardMarkup(new[]
            {
                new []
                {
                    InlineKeyboardButton.WithCallbackData("🎮 Играть", "games_menu"),
                    InlineKeyboardButton.WithCallbackData("💰 Баланс", "balance")
                },
                new []
                {
                    InlineKeyboardButton.WithCallbackData("🎁 Промокоды", "promocodes"),
                    InlineKeyboardButton.WithCallbackData("📊 История", "history")
                }
            });
        }

        private async Task ShowBetOptions(long chatId, string game)
        {
            var betOptions = new[] { 10, 50, 100, 250, 500, 1000, 2500, 5000 };
            var buttons = new List<List<InlineKeyboardButton>>();

            foreach (var row in betOptions.Chunk(3))
            {
                buttons.Add(row.Select(amount =>
                    InlineKeyboardButton.WithCallbackData(
                        $"{amount} 💰",
                        $"bet_{amount}"
                    )).ToList());
            }

            buttons.Add(new List<InlineKeyboardButton>
            {
                InlineKeyboardButton.WithCallbackData("🔙 Назад", "games_menu")
            });

            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "Выберите сумму ставки:",
                replyMarkup: new InlineKeyboardMarkup(buttons)
            );
        }
    }
}