using System;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameServer.Data;
using GameServer.Models;
using GameServer.Technical;
using Microsoft.EntityFrameworkCore;
using Ubit_Slendermena_Server.Models;

namespace GameServer
{
    public class WebSocketClientHandler
    {
        private readonly WebSocket _webSocket;
        private readonly GameServerWebSocket _server;
        private volatile bool _isConnected;

        public string PlayerId { get; private set; }
        public string PlayerName { get; private set; } = string.Empty;
        public int Score { get; set; }
        public bool IsConnected => _isConnected && _webSocket.State == WebSocketState.Open;

        public WebSocketClientHandler(WebSocket webSocket, GameServerWebSocket server)
        {
            _webSocket = webSocket;
            _server = server;
            _isConnected = true;
            PlayerId = Guid.NewGuid().ToString();
            Score = 0;
        }
        public ClientPlayer ToClientPlayer(this WebSocketClientHandler handler)
        {
            return new ClientPlayer
            {
                Id = Guid.Parse(handler.PlayerId),
                Username = handler.PlayerName,
                TotalGames = 0, // Если у вас есть доступ к количеству игр
                Wins = 0,       // Если у вас есть доступ к победам
                TotalScore = handler.Score // Баллы игрока
            };
        }

        public async Task HandleAsync()
        {
            var buffer = new byte[4096];
            var receiveBuffer = new ArraySegment<byte>(buffer);

            try
            {
                Console.WriteLine($"Начало обработки клиента {PlayerId}");

                while (_isConnected && _webSocket.State == WebSocketState.Open)
                {
                    WebSocketReceiveResult result = await _webSocket.ReceiveAsync(receiveBuffer, CancellationToken.None);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Console.WriteLine($"Клиент {PlayerName} отключился");
                        await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Клиент отключился", CancellationToken.None);
                        break;
                    }

                    string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    Console.WriteLine($"Получено от {PlayerName}: {message}");
                    await ProcessMessageAsync(message);
                }
            }
            catch (WebSocketException)
            {
                Console.WriteLine($"Клиент {PlayerName} отключился (WebSocketException)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при обработке клиента {PlayerName}: {ex.Message}");
            }
            finally
            {
                await CleanupConnectionAsync();
            }
        }

        private void AddUserToDatabase(string username, string password)
        {
            try
            {
                var optionsBuilder = new DbContextOptionsBuilder<GameDbContext>();
                string connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING") ??
                    "Host=localhost;Port=5432;Database=jeopardy;Username=postgres;Password=postgres";
                optionsBuilder.UseNpgsql(connectionString);

                using var context = new GameDbContext(optionsBuilder.Options);

                var existingUser = context.Players.FirstOrDefault(u => u.Username == username);

                if (existingUser == null)
                {
                    // Создаем нового пользователя
                    var newUser = new Player
                    {
                        Username = username,
                        Password_hash = PasswordHasher.HashPassword(password),
                        TotalGames = 0,
                        Wins = 0,
                        TotalScore = 0
                    };

                    context.Players.Add(newUser);
                    context.SaveChanges();
                    Console.WriteLine($"Пользователь {username} добавлен в базу данных");
                }
                else
                {
                    Console.WriteLine($"Пользователь {username} уже существует в базе данных");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при добавлении пользователя в базу данных: {ex.Message}");
            }
        }

        private (bool isAuthenticated, Player player) AuthenticatePlayer(string username, string password)
        {
            var optionsBuilder = new DbContextOptionsBuilder<GameDbContext>();
            string connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING") ??
                "Host=localhost;Port=5432;Database=jeopardy;Username=postgres;Password=postgres";
            optionsBuilder.UseNpgsql(connectionString);

            using var db = new GameDbContext(optionsBuilder.Options);
            var player = db.Players.FirstOrDefault(p => p.Username == username);

            if (player == null)
                return (false, null);
            return (player.Password_hash == PasswordHasher.HashPassword(password), player);
        }

        public Question ShowQuestionDetails(int questionId)
        {
            string connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING") ??
                "Host=localhost;Port=5432;Database=jeopardy;Username=postgres;Password=postgres";

            var optionsBuilder = new DbContextOptionsBuilder<GameDbContext>();
            optionsBuilder.UseNpgsql(connectionString);

            using var context = new GameDbContext(optionsBuilder.Options);

            var question = context.Questions.FirstOrDefault(q => q.Id == questionId);

            if (question == null)
            {
                Console.WriteLine($"Вопрос с Id={questionId} не найден.");
                return null;
            }

            return question;
        }

        private async Task ProcessMessageAsync(string message)
        {
            try
            {
                var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(message);

                if (data?.TryGetValue("Type", out var typeElement) == true &&
                    typeElement.GetString() is string type)
                {
                    switch (type)
                    {
                        case "Login":
                            if (data.TryGetValue("Username", out var nameElement) &&
                                nameElement.GetString() is string Username &&
                                data.TryGetValue("Password", out var passwordElement) &&
                                passwordElement.GetString() is string password)
                            {
                                var (isAuthenticated, player) = AuthenticatePlayer(Username, password);

                                PlayerName = Username;
                                if (isAuthenticated)
                                {
                                    Console.WriteLine($"Игрок {PlayerName} успешно авторизовался");

                                    await SendMessageAsync(JsonSerializer.Serialize(new
                                    {
                                        Type = "LoginSuccess",
                                        Id = player.Id,
                                        Username,
                                        TotalGames = player.TotalGames,
                                        Wins = player.Wins,
                                        TotalScore = player.TotalScore
                                    }));

                                    await _server.BroadcastMessageAsync(JsonSerializer.Serialize(new
                                    {
                                        Type = "PlayerJoined",
                                        PlayerName
                                    }));
                                }
                                else
                                {
                                    AddUserToDatabase(Username, password);
                                    Console.WriteLine($"Игрок {Username} успешно зарегистрирован");

                                    var (isAuthenticated1, player2) = AuthenticatePlayer(Username, password);
                                    await SendMessageAsync(JsonSerializer.Serialize(new
                                    {
                                        Type = "LoginSuccess",
                                        Id = player2.Id,
                                        Username = player2.Username,
                                        TotalGames = player2.TotalGames,
                                        Wins = player2.Wins,
                                        TotalScore = player2.TotalScore
                                    }));

                                    await _server.BroadcastMessageAsync(JsonSerializer.Serialize(new
                                    {
                                        Type = "PlayerJoined",
                                        Username
                                    }));
                                }
                            }
                            break;

                        case "SelectQuestion":
                            if (data.TryGetValue("CategoryId", out var questionIdElement) &&
                                questionIdElement.ValueKind == JsonValueKind.Number &&
                                questionIdElement.TryGetInt32(out int questionId))
                            {
                                var selectedQuestion = ShowQuestionDetails(questionId);
                                await _server.BroadcastMessageAsync(JsonSerializer.Serialize(new
                                {
                                    Type = "Question",
                                    Message = selectedQuestion.Text
                                }));
                            }
                            break;

                        case "StartGame":
                            if (data.TryGetValue("playerCount", out var playerCount) &&
                                playerCount.ValueKind == JsonValueKind.Number &&
                                playerCount.TryGetByte(out byte PlayerCount))
                            {
                                
                                await _server.StartNewGameAsync(PlayerCount);
                                await SendMessageAsync(JsonSerializer.Serialize(new
                                {
                                    Type = "GameStarted",
                                    Players = _server._currentGame.GetPlayers()
                                }));
                                Console.WriteLine($"Игрок {PlayerName} запросил начало игры");
                            }
                            else
                            {
                                Console.WriteLine("Ошибка при старте игры");
                            }
                            break;

                        case "Answer":
                            if (data.TryGetValue("QuestionId", out var answerQuestionIdElement) &&
                                answerQuestionIdElement.ValueKind == JsonValueKind.Number &&
                                answerQuestionIdElement.TryGetInt32(out int answerQuestionId) &&
                                data.TryGetValue("Answer", out var answerElement) &&
                                answerElement.GetString() is string answer)
                            {
                                Console.WriteLine($"Игрок {PlayerName} ответил: {answer}");
                                await _server.ProcessAnswerAsync(this, answerQuestionId, answer);
                            }
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{message}");
            }
        }

        public async Task SendMessageAsync(string message)
        {
            try
            {
                if (_webSocket.State != WebSocketState.Open)
                {
                    _isConnected = false;
                    return;
                }

                byte[] buffer = Encoding.UTF8.GetBytes(message);
                await _webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch (WebSocketException)
            {
                // Клиент отключился
                _isConnected = false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка отправки сообщения {PlayerName}: {ex.Message}");
                _isConnected = false;
            }
        }

        private async Task CleanupConnectionAsync()
        {
            if (_isConnected)
            {
                _isConnected = false;

                try
                {
                    if (_webSocket.State == WebSocketState.Open)
                    {
                        await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Закрытие соединения", CancellationToken.None);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка при закрытии соединения с {PlayerName}: {ex.Message}");
                }

                _server.RemoveClient(this);

                if (!string.IsNullOrEmpty(PlayerName))
                {
                    await _server.BroadcastMessageAsync(JsonSerializer.Serialize(new { Type = "PlayerLeft", PlayerId, PlayerName }));
                }
            }
        }
    }
}
