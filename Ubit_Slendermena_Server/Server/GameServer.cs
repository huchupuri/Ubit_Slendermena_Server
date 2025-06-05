using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameServer.Data;
using GameServer.Models;
using Microsoft.EntityFrameworkCore;

namespace GameServer
{
    public class GameServerWebSocket
    {
        private HttpListener _listener;
        private bool _isRunning;
        public readonly List<WebSocketClientHandler> _clients = new();
        private readonly Dictionary<int, Category> _categories = new();
        private readonly Dictionary<int, Question> _questions = new();
        private readonly string _connectionString;
        public Game _currentGame;
        private readonly object _clientsLock = new();

        // Добавляем управление игровыми сессиями
        private GameSession _gameSession = null;
        private readonly object _gameSessionLock = new();

        public GameServerWebSocket(string connectionString)
        {
            _connectionString = connectionString;
            LoadGameData();
        }

        public void Start(int port)
        {
            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://*:{port}/");
                _listener.Start();
                _isRunning = true;
                Console.WriteLine($"WebSocket сервер запущен на порту {port}");

                Task.Run(AcceptClientsAsync);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка запуска сервера: {ex.Message}");
                throw;
            }
        }

        private async Task AcceptClientsAsync()
        {
            while (_isRunning)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    if (context.Request.IsWebSocketRequest)
                    {
                        ProcessWebSocketRequest(context);
                    }
                    else
                    {
                        context.Response.StatusCode = 400;
                        context.Response.Close();
                    }
                }
                catch (Exception ex) when (!_isRunning)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка при подключении клиента: {ex.Message}");
                }
            }
        }

        private async void ProcessWebSocketRequest(HttpListenerContext context)
        {
            WebSocketContext webSocketContext = null;
            WebSocket webSocket = null;

            try
            {
                webSocketContext = await context.AcceptWebSocketAsync(subProtocol: null);
                webSocket = webSocketContext.WebSocket;

                Console.WriteLine($"Новое WebSocket подключение от {context.Request.RemoteEndPoint}");

                var clientHandler = new WebSocketClientHandler(webSocket, this);

                lock (_clientsLock)
                {
                    _clients.Add(clientHandler);
                }
                Console.WriteLine($"Всего клиентов: {_clients.Count}");

                await clientHandler.HandleAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при обработке WebSocket запроса: {ex.Message}");

                if (webSocket != null && webSocket.State == WebSocketState.Open)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.InternalServerError,
                        "Ошибка сервера", CancellationToken.None);
                }
            }
        }

        // ОБНОВЛЕННЫЙ МЕТОД: Создание игры с поддержкой пользовательских вопросов
        public async Task<bool> CreateGameAsync(WebSocketClientHandler host, int playerCount, string hostName, QuestionFile customQuestions = null)
        {
            lock (_gameSessionLock)
            {
                // Проверяем, не создана ли уже игра
                if (_gameSession != null)
                {
                    Console.WriteLine($"Игра уже создана хостом {_gameSession.HostName}");
                    return false;
                }

                // Создаем новую игровую сессию
                _gameSession = new GameSession
                {
                    Host = host,
                    HostName = hostName,
                    MaxPlayers = playerCount,
                    Players = new List<WebSocketClientHandler> { host },
                    IsStarted = false,
                    CreatedAt = DateTime.Now,
                    CustomQuestions = customQuestions // Сохраняем пользовательские вопросы
                };

                string questionsInfo = customQuestions != null ? " с пользовательскими вопросами" : "";
                Console.WriteLine($"Игра создана хостом {hostName} на {playerCount} игроков{questionsInfo}");

                if (customQuestions != null)
                {
                    Console.WriteLine($"Пользовательские вопросы: {customQuestions.Categories.Count} категорий, {customQuestions.Categories.Sum(c => c.Questions.Count)} вопросов");
                }

                return true;
            }
        }

        // НОВЫЙ МЕТОД: Присоединение к игре
        public async Task<string> JoinGameAsync(WebSocketClientHandler client, string playerName)
        {
            // Проверяем, есть ли созданная игра
            if (_gameSession == null)
            {
                Console.WriteLine($"Игрок {playerName} пытается присоединиться, но игра не создана");
                return "NoGame";
            }

            // Проверяем, не началась ли уже игра
            if (_gameSession.IsStarted)
            {
                Console.WriteLine($"Игрок {playerName} пытается присоединиться к уже начатой игре");
                return "GameStarted";
            }

            // Проверяем, не заполнена ли игра
            if (_gameSession.Players.Count >= _gameSession.MaxPlayers)
            {
                Console.WriteLine($"Игрок {playerName} пытается присоединиться к заполненной игре");
                return "GameFull";
            }

            // Проверяем, не присоединен ли уже этот игрок
            if (_gameSession.Players.Any(p => p.PlayerName == playerName))
            {
                Console.WriteLine($"Игрок {playerName} уже присоединен к игре");
                return "AlreadyJoined";
            }

            // Добавляем игрока в игру
            _gameSession.Players.Add(client);
            Console.WriteLine($"Игрок {playerName} присоединился к игре. Игроков: {_gameSession.Players.Count}/{_gameSession.MaxPlayers}");

            // Уведомляем всех игроков о новом участнике
            await BroadcastToGamePlayersAsync(JsonSerializer.Serialize(new
            {
                Type = "PlayerJoined",
                PlayerName = playerName,
                CurrentPlayers = _gameSession.Players.Count,
                MaxPlayers = _gameSession.MaxPlayers
            }));

            // Если игра заполнена, автоматически начинаем её
            if (_gameSession.Players.Count == _gameSession.MaxPlayers)
            {
                await StartGameSessionAsync();
            }

            return "Success";
        }

        // НОВЫЙ МЕТОД: Отправка сообщения только игрокам в текущей игре
        private async Task BroadcastToGamePlayersAsync(string message)
        {
            if (_gameSession?.Players != null)
            {
                foreach (var player in _gameSession.Players.Where(p => p.IsConnected))
                {
                    await player.SendMessageAsync(message);
                }
            }
        }

        // ОБНОВЛЕННЫЙ МЕТОД: Начало игровой сессии с поддержкой пользовательских вопросов
        private async Task StartGameSessionAsync()
        {
            if (_gameSession == null) return;

            lock (_gameSessionLock)
            {
                _gameSession.IsStarted = true;
            }

            Console.WriteLine($"Начинаем игру с {_gameSession.Players.Count} игроками");

            // Определяем, какие вопросы использовать
            Dictionary<int, Category> gameCategories;
            Dictionary<int, Question> gameQuestions;

            if (_gameSession.CustomQuestions != null)
            {
                // Используем пользовательские вопросы
                (gameCategories, gameQuestions) = ConvertCustomQuestions(_gameSession.CustomQuestions);
                Console.WriteLine($"ИСПОЛЬЗУЮТСЯ ПОЛЬЗОВАТЕЛЬСКИЕ ВОПРОСЫ: {gameCategories.Count} категорий, {gameQuestions.Count} вопросов");

                // Выводим список категорий для отладки
                foreach (var cat in gameCategories.Values)
                {
                    Console.WriteLine($"Категория: {cat.Name} (ID: {cat.Id})");
                }
            }
            else
            {
                // Используем стандартные вопросы из базы данных
                gameCategories = _categories;
                gameQuestions = _questions;
                Console.WriteLine("ИСПОЛЬЗУЮТСЯ СТАНДАРТНЫЕ ВОПРОСЫ из базы данных");
            }

            // Создаем игру с участниками
            _currentGame = new Game(_gameSession.Players, gameCategories, gameQuestions, (byte)_gameSession.MaxPlayers, this);
            _currentGame.Start();

            // Уведомляем всех игроков о начале игры
            await BroadcastToGamePlayersAsync(JsonSerializer.Serialize(new
            {
                Type = "GameStarted",
                Players = _gameSession.Players.Select(p => new { p.PlayerId, p.PlayerName, p.Score }).ToList()
            }));
        }

        // НОВЫЙ МЕТОД: Конвертация пользовательских вопросов в формат игры
        private (Dictionary<int, Category> categories, Dictionary<int, Question> questions) ConvertCustomQuestions(QuestionFile customQuestions)
        {
            var categories = new Dictionary<int, Category>();
            var questions = new Dictionary<int, Question>();

            int categoryId = 1;
            int questionId = 1;

            Console.WriteLine($"=== НАЧАЛО КОНВЕРТАЦИИ ПОЛЬЗОВАТЕЛЬСКИХ ВОПРОСОВ ===");
            Console.WriteLine($"Входящие категории: {customQuestions.Categories.Count}");

            foreach (var customCategory in customQuestions.Categories)
            {
                var category = new Category
                {
                    Id = categoryId,
                    Name = customCategory.Name
                };

                categories[categoryId] = category;
                Console.WriteLine($"Создана категория {categoryId}: '{customCategory.Name}' ({customCategory.Questions.Count} вопросов)");

                foreach (var customQuestion in customCategory.Questions)
                {
                    var question = new Question
                    {
                        Id = questionId,
                        CategoryId = categoryId,
                        Text = customQuestion.Text,
                        Answer = customQuestion.Answer,
                        Price = customQuestion.Price,
                        Category = category
                    };

                    questions[questionId] = question;
                    Console.WriteLine($"  Вопрос {questionId}: '{customQuestion.Text}' -> '{customQuestion.Answer}' ({customQuestion.Price} очков)");
                    questionId++;
                }

                categoryId++;
            }

            Console.WriteLine($"=== КОНВЕРТАЦИЯ ЗАВЕРШЕНА ===");
            Console.WriteLine($"Создано категорий: {categories.Count}");
            Console.WriteLine($"Создано вопросов: {questions.Count}");

            return (categories, questions);
        }

        public async Task BroadcastMessageAsync(string message)
        {
            List<WebSocketClientHandler> clientsCopy;

            lock (_clientsLock)
            {
                clientsCopy = new List<WebSocketClientHandler>(_clients);
            }

            foreach (var client in clientsCopy)
            {
                await client.SendMessageAsync(message);
            }
        }

        // ОБНОВЛЕННЫЙ МЕТОД: Удаление клиента с учетом игровых сессий
        public void RemoveClient(WebSocketClientHandler client)
        {
            lock (_clientsLock)
            {
                if (_clients.Remove(client))
                {
                    Console.WriteLine($"Клиент {client.PlayerName} отключен. Всего клиентов: {_clients.Count}");
                }
            }

            // Удаляем игрока из игровой сессии, если он в ней участвует
            lock (_gameSessionLock)
            {
                if (_gameSession?.Players != null)
                {
                    if (_gameSession.Players.Remove(client))
                    {
                        Console.WriteLine($"Игрок {client.PlayerName} удален из игровой сессии");

                        // Если это был хост, завершаем игру
                        if (_gameSession.Host == client)
                        {
                            Console.WriteLine("Хост покинул игру. Завершаем игровую сессию.");
                            EndGameSession();
                        }
                        // Если игроков не осталось, завершаем игру
                        else if (_gameSession.Players.Count == 0)
                        {
                            Console.WriteLine("Все игроки покинули игру. Завершаем игровую сессию.");
                            EndGameSession();
                        }
                        else
                        {
                            // Уведомляем оставшихся игроков
                            BroadcastToGamePlayersAsync(JsonSerializer.Serialize(new
                            {
                                Type = "PlayerLeft",
                                PlayerName = client.PlayerName,
                                CurrentPlayers = _gameSession.Players.Count,
                                MaxPlayers = _gameSession.MaxPlayers
                            }));
                        }
                    }
                }
            }
        }

        // НОВЫЙ МЕТОД: Завершение игровой сессии
        private void EndGameSession()
        {
            lock (_gameSessionLock)
            {
                if (_gameSession != null)
                {
                    // Уведомляем всех оставшихся игроков о завершении игры
                    BroadcastToGamePlayersAsync(JsonSerializer.Serialize(new
                    {
                        Type = "GameEnded",
                        Reason = "Host disconnected or no players left"
                    }));

                    _gameSession = null;
                    _currentGame = null;
                    Console.WriteLine("Игровая сессия завершена");
                }
            }
        }

        private void LoadGameData()
        {
            try
            {
                var optionsBuilder = new DbContextOptionsBuilder<GameDbContext>();
                optionsBuilder.UseNpgsql(_connectionString);

                using var context = new GameDbContext(optionsBuilder.Options);
                context.Database.EnsureCreated();

                var categories = context.Categories.Include(c => c.Questions).ToList();

                foreach (var category in categories)
                {
                    _categories[category.Id] = category;
                    foreach (var question in category.Questions)
                    {
                        _questions[question.Id] = question;
                    }
                }

                Console.WriteLine($"Загружено {_categories.Count} категорий и {_questions.Count} вопросов");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при загрузке данных: {ex.Message}");
                throw;
            }
        }

        public async Task StartNewGameAsync(WebSocketClientHandler client, byte playerCount)
        {
            // Если есть активная игровая сессия, используем её
            if (_gameSession != null && !_gameSession.IsStarted)
            {
                Console.WriteLine("Запуск существующей игровой сессии");
                await StartGameSessionAsync();
                return;
            }

            // Проверяем, достаточно ли игроков
            List<WebSocketClientHandler> availablePlayers;
            lock (_clientsLock)
            {
                availablePlayers = _clients.Where(c => c.IsConnected).ToList();
            }

            if (availablePlayers.Count < playerCount)
            {
                await client.SendMessageAsync(JsonSerializer.Serialize(new
                {
                    Type = "Error",
                    Message = $"Недостаточно игроков для начала игры. Подключено: {availablePlayers.Count}, требуется: {playerCount}"
                }));
                return;
            }

            // Определяем, какие вопросы использовать
            Dictionary<int, Category> gameCategories = _categories;
            Dictionary<int, Question> gameQuestions = _questions;

            // Если есть игровая сессия с пользовательскими вопросами
            if (_gameSession?.CustomQuestions != null)
            {
                (gameCategories, gameQuestions) = ConvertCustomQuestions(_gameSession.CustomQuestions);
                Console.WriteLine($"ИСПОЛЬЗУЮТСЯ ПОЛЬЗОВАТЕЛЬСКИЕ ВОПРОСЫ: {gameCategories.Count} категорий, {gameQuestions.Count} вопросов");
            }
            else
            {
                Console.WriteLine("ИСПОЛЬЗУЮТСЯ СТАНДАРТНЫЕ ВОПРОСЫ из базы данных");
            }

            _currentGame = new Game(availablePlayers.Take(playerCount).ToList(), gameCategories, gameQuestions, playerCount, this);
            _currentGame.Start();

            await client.SendMessageAsync(JsonSerializer.Serialize(new
            {
                Type = "GameStarted",
                Players = availablePlayers.Take(playerCount).Select(c => new { c.PlayerId, c.PlayerName, c.Score }).ToList()
            }));
        }

        public async Task ProcessQuestionSelectionAsync(WebSocketClientHandler client, int categoryId)
        {
            if (_currentGame == null)
            {
                await client.SendMessageAsync(JsonSerializer.Serialize(new { Type = "Error", Message = "Игра не начата" }));
                return;
            }

            var question = _currentGame.GetAvailableQuestionByCategory(categoryId);

            if (question == null)
            {
                await client.SendMessageAsync(JsonSerializer.Serialize(new { Type = "Error", Message = "Вопрос не найден или уже отвечен" }));
                return;
            }

            Console.WriteLine($"Выбран вопрос из категории {categoryId}: {question.Text}");
            await _currentGame.ShowQuestionAsync(question);
        }

        public async Task ProcessAnswerAsync(WebSocketClientHandler client, int questionId, string answer)
        {
            if (_currentGame?.CurrentQuestion?.Id != questionId)
            {
                await client.SendMessageAsync(JsonSerializer.Serialize(new { Type = "Error", Message = "Неверный вопрос" }));
                return;
            }

            await _currentGame.ProcessAnswerAsync(client, answer);
        }

        public void Stop()
        {
            Console.WriteLine("Остановка сервера...");
            _isRunning = false;

            try
            {
                _listener?.Stop();
                _listener?.Close();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при остановке сервера: {ex.Message}");
            }

            Console.WriteLine("Сервер остановлен");
        }
    }

    // ОБНОВЛЕННЫЙ КЛАСС: Игровая сессия с поддержкой пользовательских вопросов
    public class GameSession
    {
        public WebSocketClientHandler Host { get; set; }
        public string HostName { get; set; }
        public int MaxPlayers { get; set; }
        public List<WebSocketClientHandler> Players { get; set; }
        public bool IsStarted { get; set; }
        public DateTime CreatedAt { get; set; }
        public QuestionFile CustomQuestions { get; set; } // Используем ваши модели
    }
}
