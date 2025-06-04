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
                                        PlayerName = Username
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
                                        PlayerName = Username
                                    }));
                                }
                            }
                            break;

                        case "SelectQuestion":
                            if (data.TryGetValue("CategoryId", out var categoryIdElement) &&
                                categoryIdElement.ValueKind == JsonValueKind.Number &&
                                categoryIdElement.TryGetInt32(out int categoryId))
                            {
                                Console.WriteLine($"Игрок {PlayerName} выбрал вопрос из категории {categoryId}");
                                await _server.ProcessQuestionSelectionAsync(this, categoryId);
                            }
                            break;

                        case "StartGame":
                            if (data.TryGetValue("playerCount", out var playerCount) &&
                                playerCount.ValueKind == JsonValueKind.Number &&
                                playerCount.TryGetByte(out byte PlayerCount))
                            {
                                Console.WriteLine($"Игрок {PlayerName} запросил начало игры");
                                await _server.StartNewGameAsync(PlayerCount);
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
                Console.WriteLine($"Ошибка обработки сообщения от {PlayerName}: {ex.Message}");
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
