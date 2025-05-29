using Telegram.Bot;
using Telegram.Bot.Types.ReplyMarkups;
using System.Collections.Concurrent;

namespace TelegramGameBot.Services.Games
{
    public class MinesweeperGameState
    {
        public int GridSize { get; set; }
        public int MinesCount { get; set; }
        public bool[,] Mines { get; set; }
        public bool[,] OpenedCells { get; set; }
        public int Bet { get; set; }
        public double Multiplier { get; set; }
        public bool IsGameOver { get; set; }
        public bool IsFirstMove { get; set; }
        public int OpenedSafeCells { get; set; }
        public int TotalSafeCells { get; set; }

        public MinesweeperGameState(int size, int mines, int bet)
        {
            GridSize = size;
            MinesCount = mines;
            Mines = new bool[size, size];
            OpenedCells = new bool[size, size];
            Bet = bet;
            Multiplier = 1.0;
            IsGameOver = false;
            IsFirstMove = true;
            OpenedSafeCells = 0;
            TotalSafeCells = size * size - mines;
        }

        public void PlaceMines(int firstX, int firstY)
        {
            var random = new Random();
            var positions = new List<(int x, int y)>();

            // Собираем все возможные позиции, кроме первого клика и области вокруг него
            for (int i = 0; i < GridSize; i++)
            {
                for (int j = 0; j < GridSize; j++)
                {
                    if (Math.Abs(i - firstX) > 1 || Math.Abs(j - firstY) > 1)
                    {
                        positions.Add((i, j));
                    }
                }
            }

            // Перемешиваем позиции
            for (int i = positions.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                var temp = positions[i];
                positions[i] = positions[j];
                positions[j] = temp;
            }

            // Размещаем мины
            for (int i = 0; i < Math.Min(MinesCount, positions.Count); i++)
            {
                var (x, y) = positions[i];
                Mines[x, y] = true;
            }
        }

        public void UpdateMultiplier()
        {
            double baseMultiplier = (double)(GridSize * GridSize) / (MinesCount * 2);
            double progressMultiplier = (double)OpenedSafeCells / (TotalSafeCells * 1.5);
            double riskFactor = Math.Pow(1.05, OpenedSafeCells);
            
            Multiplier = Math.Round(baseMultiplier * (1 + progressMultiplier) * riskFactor, 2);
        }
    }

    public class MinesweeperGame : BaseGame
    {
        private readonly ConcurrentDictionary<long, MinesweeperGameState> _games;

        public MinesweeperGame(ITelegramBotClient bot, UserService userService) 
            : base(bot, userService)
        {
            _games = new ConcurrentDictionary<long, MinesweeperGameState>();
        }

        public override async Task HandleCommand(long chatId, long userId, string command)
        {
            switch (command)
            {
                case "minesweeper":
                    await ShowRules(chatId);
                    break;
                case var c when c.StartsWith("minesweeper_size_"):
                    await HandleSizeSelection(chatId, userId, command);
                    break;
                case var c when c.StartsWith("minesweeper_mines_"):
                    await HandleMinesSelection(chatId, userId, command);
                    break;
                case var c when c.StartsWith("minesweeper_cell_"):
                    await HandleCellClick(chatId, userId, command);
                    break;
                case "minesweeper_collect":
                    await HandleCollect(chatId, userId);
                    break;
            }
        }

        public override async Task ShowRules(long chatId)
        {
            var text = "💣 Сапёр\n\n" +
                      "Правила:\n" +
                      "1. Выберите размер поля\n" +
                      "2. Выберите количество мин\n" +
                      "3. Сделайте ставку\n" +
                      "4. Открывайте клетки и собирайте множители\n" +
                      "5. Если попадёте на мину - проигрыш\n" +
                      "6. Можете забрать выигрыш в любой момент\n\n" +
                      "Множитель растёт с каждой открытой безопасной клеткой!";

            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("5x5 (Легко)", "minesweeper_size_5"),
                    InlineKeyboardButton.WithCallbackData("7x7 (Средне)", "minesweeper_size_7")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("10x10 (Сложно)", "minesweeper_size_10")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🔙 Назад", "games_menu")
                }
            });

            await _bot.SendTextMessageAsync(chatId, text, replyMarkup: keyboard);
        }

        private async Task HandleSizeSelection(long chatId, long userId, string command)
        {
            var size = int.Parse(command.Split('_').Last());
            var mineOptions = size switch
            {
                5 => new[] { 3, 5, 7 },
                7 => new[] { 5, 8, 12 },
                10 => new[] { 8, 12, 16 },
                _ => new[] { 3, 5, 7 }
            };

            var buttons = mineOptions.Select(count =>
                InlineKeyboardButton.WithCallbackData(
                    $"{count} мин",
                    $"minesweeper_mines_{size}_{count}"
                )).ToArray();

            var keyboard = new InlineKeyboardMarkup(new[]
            {
                buttons,
                new[] { InlineKeyboardButton.WithCallbackData("🔙 Назад", "minesweeper") }
            });

            await _bot.SendTextMessageAsync(
                chatId,
                $"Выбран размер поля {size}x{size}\nВыберите количество мин:",
                replyMarkup: keyboard
            );
        }

        private async Task HandleMinesSelection(long chatId, long userId, string command)
        {
            var parts = command.Split('_');
            var size = int.Parse(parts[2]);
            var mines = int.Parse(parts[3]);

            var user = _userService.GetOrCreateUser(userId);
            user.CurrentGame = $"minesweeper_{size}x{size}_{mines}";

            await ShowBetMenu(chatId, "minesweeper");
        }

        public override async Task ProcessBet(long chatId, long userId, int amount)
        {
            var user = _userService.GetOrCreateUser(userId);
            
            if (user.Balance < amount)
            {
                await _bot.SendTextMessageAsync(
                    chatId,
                    $"❌ Недостаточно средств! Ваш баланс: {user.Balance} монет"
                );
                return;
            }

            if (string.IsNullOrEmpty(user.CurrentGame) || !user.CurrentGame.StartsWith("minesweeper_"))
            {
                await _bot.SendTextMessageAsync(
                    chatId,
                    "❌ Сначала выберите размер поля и количество мин"
                );
                return;
            }

            var parts = user.CurrentGame.Split('_')[1].Split('x');
            var size = int.Parse(parts[0]);
            var mines = int.Parse(user.CurrentGame.Split('_')[2]);

            var game = new MinesweeperGameState(size, mines, amount);
            
            _games.TryRemove(userId, out _);
            
            if (!_games.TryAdd(userId, game))
            {
                await _bot.SendTextMessageAsync(
                    chatId,
                    "❌ Ошибка при создании игры. Попробуйте еще раз"
                );
                return;
            }

            _userService.UpdateBalance(userId, -amount);
            await ShowGameField(chatId, game);
        }

        private async Task HandleCellClick(long chatId, long userId, string command)
        {
            if (!_games.TryGetValue(userId, out var game))
            {
                await _bot.SendTextMessageAsync(
                    chatId,
                    "❌ Игра не найдена. Начните новую игру",
                    replyMarkup: new InlineKeyboardMarkup(
                        InlineKeyboardButton.WithCallbackData("🎮 Новая игра", "minesweeper")
                    )
                );
                return;
            }

            if (game.IsGameOver)
            {
                await _bot.SendTextMessageAsync(
                    chatId,
                    "❌ Игра завершена. Начните новую"
                );
                return;
            }

            var parts = command.Split('_');
            var x = int.Parse(parts[2]);
            var y = int.Parse(parts[3]);

            if (game.OpenedCells[x, y])
            {
                await _bot.SendTextMessageAsync(
                    chatId,
                    "⚠️ Эта клетка уже открыта!"
                );
                return;
            }

            if (game.IsFirstMove)
            {
                game.PlaceMines(x, y);
                game.IsFirstMove = false;
            }

            game.OpenedCells[x, y] = true;

            if (game.Mines[x, y])
            {
                await HandleGameOver(chatId, userId, game, x, y);
                return;
            }

            game.OpenedSafeCells++;
            game.UpdateMultiplier();

            await ShowGameField(chatId, game, true);
        }

        private async Task HandleGameOver(long chatId, long userId, MinesweeperGameState game, int hitX, int hitY)
        {
            game.IsGameOver = true;
            _games.TryRemove(userId, out _);

            var buttons = new List<List<InlineKeyboardButton>>();
            for (int i = 0; i < game.GridSize; i++)
            {
                var row = new List<InlineKeyboardButton>();
                for (int j = 0; j < game.GridSize; j++)
                {
                    string symbol;
                    if (i == hitX && j == hitY)
                        symbol = "💥";
                    else if (game.OpenedCells[i, j])
                        symbol = "✅";
                    else if (game.Mines[i, j])
                        symbol = "💣";
                    else
                        symbol = "⬜";

                    row.Add(InlineKeyboardButton.WithCallbackData(symbol, $"minesweeper_cell_{i}_{j}"));
                }
                buttons.Add(row);
            }

            buttons.Add(new List<InlineKeyboardButton>
            {
                InlineKeyboardButton.WithCallbackData("🎮 Играть снова", "minesweeper"),
                InlineKeyboardButton.WithCallbackData("🏠 В меню", "games_menu")
            });

            await _bot.SendTextMessageAsync(
                chatId,
                $"💣 БУМ! Вы подорвались на мине!\n" +
                $"Проигрыш: {game.Bet} монет\n" +
                $"Открыто клеток: {game.OpenedSafeCells}\n" +
                $"Множитель: x{game.Multiplier:F2}",
                replyMarkup: new InlineKeyboardMarkup(buttons)
            );

            _userService.AddGameHistory(
                userId,
                "Сапёр",
                game.Bet,
                false,
                $"Подрыв на мине. Открыто {game.OpenedSafeCells} клеток"
            );
        }

        private async Task HandleCollect(long chatId, long userId)
        {
            if (!_games.TryGetValue(userId, out var game))
            {
                await _bot.SendTextMessageAsync(
                    chatId,
                    "❌ Игра не найдена"
                );
                return;
            }

            if (_games.TryRemove(userId, out game))
            {
                var winAmount = (int)(game.Bet * game.Multiplier);
                _userService.UpdateBalance(userId, winAmount);

                await _bot.SendTextMessageAsync(
                    chatId,
                    $"🎉 Поздравляем! Вы забрали выигрыш!\n" +
                    $"Ставка: {game.Bet}\n" +
                    $"Множитель: x{game.Multiplier:F2}\n" +
                    $"Выигрыш: {winAmount} монет\n" +
                    $"Открыто клеток: {game.OpenedSafeCells}",
                    replyMarkup: new InlineKeyboardMarkup(new[]
                    {
                        new[]
                        {
                            InlineKeyboardButton.WithCallbackData("🎮 Играть снова", "minesweeper"),
                            InlineKeyboardButton.WithCallbackData("🏠 В меню", "games_menu")
                        }
                    })
                );

                _userService.AddGameHistory(
                    userId,
                    "Сапёр",
                    game.Bet,
                    true,
                    $"Выигрыш x{game.Multiplier:F2}. Открыто {game.OpenedSafeCells} клеток"
                );
            }
        }

        private async Task ShowGameField(long chatId, MinesweeperGameState game, bool showStats = false)
        {
            var buttons = new List<List<InlineKeyboardButton>>();
            
            for (int i = 0; i < game.GridSize; i++)
            {
                var row = new List<InlineKeyboardButton>();
                for (int j = 0; j < game.GridSize; j++)
                {
                    string symbol = game.OpenedCells[i, j] ? "✅" : "⬜";
                    row.Add(InlineKeyboardButton.WithCallbackData(symbol, $"minesweeper_cell_{i}_{j}"));
                }
                buttons.Add(row);
            }

            buttons.Add(new List<InlineKeyboardButton>
            {
                InlineKeyboardButton.WithCallbackData(
                    $"💰 {game.Bet} | 📈 x{game.Multiplier:F2} | ✅ {game.OpenedSafeCells}/{game.TotalSafeCells}",
                    "info_mines"
                )
            });

            buttons.Add(new List<InlineKeyboardButton>
            {
                InlineKeyboardButton.WithCallbackData("💰 Забрать выигрыш", "minesweeper_collect"),
                InlineKeyboardButton.WithCallbackData("🔙 Назад", "minesweeper")
            });

            var text = showStats
                ? $"💎 Клетка безопасна!\n" +
                  $"Текущий множитель: x{game.Multiplier:F2}\n" +
                  $"Открыто клеток: {game.OpenedSafeCells}\n" +
                  $"Возможный выигрыш: {(int)(game.Bet * game.Multiplier)} монет"
                : "🎮 Выберите клетку:";

            try
            {
                await _bot.EditMessageTextAsync(
                    chatId: chatId,
                    messageId: _bot.LoadLastMessageId(),
                    text: text,
                    replyMarkup: new InlineKeyboardMarkup(buttons)
                );
            }
            catch
            {
                await _bot.SendTextMessageAsync(
                    chatId: chatId,
                    text: text,
                    replyMarkup: new InlineKeyboardMarkup(buttons)
                );
            }
        }
    }
} 