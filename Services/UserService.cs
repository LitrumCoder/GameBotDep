using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using System.IO;

namespace TelegramGameBot.Services
{
    public class User
    {
        public long Id { get; set; }
        public decimal Balance { get; set; }
        public string? CurrentGame { get; set; }
        public int? CurrentBet { get; set; }
        public HashSet<string> UsedPromoCodes { get; set; }
        public List<GameHistory> GameHistory { get; set; }

        public User(long id)
        {
            Id = id;
            Balance = 1000; // Начальный баланс
            UsedPromoCodes = new HashSet<string>();
            GameHistory = new List<GameHistory>();
        }
    }

    public class GameHistory
    {
        public string GameName { get; set; }
        public int Bet { get; set; }
        public bool IsWin { get; set; }
        public string Result { get; set; }
        public DateTime Timestamp { get; set; }

        public GameHistory(string gameName, int bet, bool isWin, string result)
        {
            GameName = gameName;
            Bet = bet;
            IsWin = isWin;
            Result = result;
            Timestamp = DateTime.Now;
        }
    }

    public class UserService
    {
        private readonly ConcurrentDictionary<long, User> _users;
        private readonly Dictionary<string, (int amount, string message)> _promoCodes;
        private readonly string _dataFile = "userdata.json";

        public UserService()
        {
            _users = new ConcurrentDictionary<long, User>();
            _promoCodes = new Dictionary<string, (int amount, string message)>
            {
                { "START", (1000, "🎉 Вы получили 1000 монет за использование промокода START!") },
                { "BONUS", (500, "🎁 Вы получили 500 монет за использование промокода BONUS!") },
                { "LUCKY", (2000, "🍀 Вы получили 2000 монет за использование промокода LUCKY!") },
                { "FORTUNE", (3000, "💫 Вы получили 3000 монет за использование промокода FORTUNE!") },
                { "JACKPOT", (5000, "🎰 Вы получили 5000 монет за использование промокода JACKPOT!") },
                { "WELCOME", (1500, "👋 Вы получили 1500 монет за использование промокода WELCOME!") },
                { "ARTEMNEDAUN", (10000, "🌟 Вы получили 10000 монет за использование промокода ARTEMNEDAUN!") },
                { "BESTBRO", (1000000, "🔥 Вы получили 1000000 монет за использование промокода BESTBRO!") }
            };

            LoadData();
        }

        private void LoadData()
        {
            if (File.Exists(_dataFile))
            {
                try
                {
                    var json = File.ReadAllText(_dataFile);
                    var users = JsonSerializer.Deserialize<Dictionary<long, User>>(json);
                    if (users != null)
                    {
                        foreach (var user in users)
                        {
                            _users.TryAdd(user.Key, user.Value);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка при загрузке данных: {ex.Message}");
                }
            }
        }

        private void SaveData()
        {
            try
            {
                var json = JsonSerializer.Serialize(_users);
                File.WriteAllText(_dataFile, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при сохранении данных: {ex.Message}");
            }
        }

        public decimal GetBalance(long userId)
        {
            return _users.TryGetValue(userId, out var user) ? user.Balance : 0;
        }

        public void UpdateBalance(long userId, decimal amount)
        {
            if (_users.TryGetValue(userId, out var user))
            {
                user.Balance += amount;
                if (user.Balance < 0) user.Balance = 0;
                SaveData();
            }
        }

        public User GetOrCreateUser(long userId)
        {
            return _users.GetOrAdd(userId, id => 
            {
                var user = new User(id);
                SaveData();
                return user;
            });
        }

        public (bool success, int amount, string message) UsePromoCode(long userId, string code)
        {
            var user = GetOrCreateUser(userId);

            if (!_promoCodes.ContainsKey(code))
            {
                return (false, 0, "❌ Неверный промокод");
            }

            if (user.UsedPromoCodes.Contains(code))
            {
                return (false, 0, "❌ Вы уже использовали этот промокод");
            }

            var (amount, message) = _promoCodes[code];
            UpdateBalance(userId, amount);
            user.UsedPromoCodes.Add(code);
            SaveData();

            return (true, amount, message);
        }

        public void AddGameHistory(long userId, string gameName, int bet, bool isWin, string result)
        {
            if (_users.TryGetValue(userId, out var user))
            {
                user.GameHistory.Add(new GameHistory(gameName, bet, isWin, result));
                if (user.GameHistory.Count > 10)
                {
                    user.GameHistory.RemoveAt(0);
                }
                SaveData();
            }
        }

        public List<GameHistory> GetUserHistory(long userId)
        {
            return _users.TryGetValue(userId, out var user) 
                ? user.GameHistory 
                : new List<GameHistory>();
        }

        public void SetCurrentGame(long userId, string game)
        {
            if (_users.TryGetValue(userId, out var user))
            {
                user.CurrentGame = game;
                SaveData();
            }
        }

        public void SetCurrentBet(long userId, int bet)
        {
            if (_users.TryGetValue(userId, out var user))
            {
                user.CurrentBet = bet;
                SaveData();
            }
        }

        public void ClearGameState(long userId)
        {
            if (_users.TryGetValue(userId, out var user))
            {
                user.CurrentGame = null;
                user.CurrentBet = null;
                SaveData();
            }
        }
    }
}