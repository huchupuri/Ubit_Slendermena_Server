using System;
using System.Collections.Generic;
using System.Linq;
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
            var buffer = new byte[16384]; // Увеличиваем буфер для больших сообщений
            var receiveBuffer = new ArraySegment<byte>(buffer);

            try
            {
                Console.WriteLine($"Начало обработки клиента {PlayerId}");

                while (_isConnected && _webSocket.State == WebSocketState.Open)
                {
                    var messageBuilder = new StringBuilder();
                    WebSocketReceiveResult result;

                    do
                    {
                        result = await _webSocket.ReceiveAsync(receiveBuffer, CancellationToken.None);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            Console.WriteLine($"Клиент {PlayerName} отключился");
                            await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Клиент отключился", CancellationToken.None);
                            break;
                        }

                        if (result.MessageType == WebSocketMessageType.Text)
                        {
                            string chunk = Encoding.UTF8.GetString(buffer, 0, result.Count);
                            messageBuilder.Append(chunk);
                        }
                    }
                    while (!result.EndOfMessage);

                    if (messageBuilder.Length > 0)
                    {
                        string message = messageBuilder.ToString();
                        Console.WriteLine($"Получено от {PlayerName}: {message}");
                        await ProcessMessageAsync(message);
                    }
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
                            await HandleLoginAsync(data);
                            break;

                        case "Register":
                            await HandleRegisterAsync(data);
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
                                await _server.StartNewGameAsync(this, PlayerCount);
                            }
                            break;

                        case "CreateGame":
                            await HandleCreateGameAsync(data);
                            break;

                        case "JoinGame":
                            await HandleJoinGameAsync(data);
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

        private async Task HandleCreateGameAsync(Dictionary<string, JsonElement> data)
        {
            if (data.TryGetValue("playerCount", out var playerCountElement) &&
                playerCountElement.ValueKind == JsonValueKind.Number &&
                playerCountElement.TryGetByte(out byte playerCount) &&
                data.TryGetValue("hostName", out var hostNameElement) &&
                hostNameElement.GetString() is string hostName)
            {
                try
                {
                    // Проверяем наличие пользовательских вопросов
                    QuestionFile customQuestions = null;
                    if (data.TryGetValue("customQuestions", out var customQuestionsElement) &&
                        customQuestionsElement.ValueKind == JsonValueKind.Object)
                    {
                        try
                        {
                            string customQuestionsJson = customQuestionsElement.GetRawText();
                            Console.WriteLine($"Получены пользовательские вопросы JSON: {customQuestionsJson}");

                            customQuestions = JsonSerializer.Deserialize<QuestionFile>(customQuestionsJson, new JsonSerializerOptions
                            {
                                PropertyNameCaseInsensitive = true
                            });

                            Console.WriteLine($"Получены пользовательские вопросы: {customQuestions?.Categories?.Count} категорий");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Ошибка десериализации пользовательских вопросов: {ex.Message}");
                        }
                    }

                    bool gameCreated = await _server.CreateGameAsync(this, playerCount, hostName, customQuestions);

                    if (gameCreated)
                    {
                        await SendMessageAsync(JsonSerializer.Serialize(new
                        {
                            Type = "GameCreated",
                            PlayerCount = playerCount,
                            HostName = hostName,
                            HasCustomQuestions = customQuestions != null
                        }));

                        Console.WriteLine($"Игрок {PlayerName} создал игру на {playerCount} игроков" +
                                        (customQuestions != null ? " с пользовательскими вопросами" : ""));

                        if (playerCount == 1)
                        {
                            await _server.StartNewGameAsync(this, playerCount);
                        }
                    }
                    else
                    {
                        await SendMessageAsync(JsonSerializer.Serialize(new
                        {
                            Type = "Error",
                            Message = "Не удалось создать игру. Возможно, игра уже существует."
                        }));
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка при создании игры: {ex.Message}");
                    await SendMessageAsync(JsonSerializer.Serialize(new
                    {
                        Type = "Error",
                        Message = "Ошибка при создании игры"
                    }));
                }
            }
        }

        private async Task HandleJoinGameAsync(Dictionary<string, JsonElement> data)
        {
            if (data.TryGetValue("playerName", out var playerNameElement) &&
                playerNameElement.GetString() is string playerName)
            {
                try
                {
                    var joinResult = await _server.JoinGameAsync(this, playerName);

                    switch (joinResult)
                    {
                        case "Success":
                            Console.WriteLine($"Игрок {PlayerName} присоединился к игре");
                            await SendMessageAsync(JsonSerializer.Serialize(new
                            {
                                Type = "GameJoined",
                                PlayerName = playerName
                            }));
                            break;

                        case "GameFull":
                            await SendMessageAsync(JsonSerializer.Serialize(new
                            {
                                Type = "GameFull"
                            }));
                            break;

                        case "NoGame":
                            await SendMessageAsync(JsonSerializer.Serialize(new
                            {
                                Type = "NoGameAvailable"
                            }));
                            break;

                        default:
                            await SendMessageAsync(JsonSerializer.Serialize(new
                            {
                                Type = "Error",
                                Message = "Не удалось присоединиться к игре"
                            }));
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка при присоединении к игре: {ex.Message}");
                    await SendMessageAsync(JsonSerializer.Serialize(new
                    {
                        Type = "Error",
                        Message = "Ошибка при присоединении к игре"
                    }));
                }
            }
        }

        // Остальные методы остаются без изменений...
        private async Task<bool> RegisterPlayerAsync(string username, string password)
        {
            try
            {
                var optionsBuilder = new DbContextOptionsBuilder<GameDbContext>();
                string connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING") ??
                    "Host=localhost;Port=5432;Database=jeopardy;Username=postgres;Password=postgres";
                optionsBuilder.UseNpgsql(connectionString);

                using var context = new GameDbContext(optionsBuilder.Options);

                // Проверяем, существует ли уже пользователь
                var existingUser = await context.Players.FirstOrDefaultAsync(u => u.Username == username);
                if (existingUser != null)
                {
                    return false; // Пользователь уже существует
                }

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
                await context.SaveChangesAsync();
                Console.WriteLine($"Пользователь {username} успешно зарегистрирован");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при регистрации пользователя: {ex.Message}");
                return false;
            }
        }

        private async Task<(bool isAuthenticated, Player player)> AuthenticatePlayerAsync(string username, string password)
        {
            try
            {
                var optionsBuilder = new DbContextOptionsBuilder<GameDbContext>();
                string connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING") ??
                    "Host=localhost;Port=5432;Database=jeopardy;Username=postgres;Password=postgres";
                optionsBuilder.UseNpgsql(connectionString);

                using var context = new GameDbContext(optionsBuilder.Options);
                var player = await context.Players.FirstOrDefaultAsync(p => p.Username == username);

                if (player == null)
                    return (false, null);

                bool isPasswordValid = PasswordHasher.VerifyPassword(password, player.Password_hash);
                return (isPasswordValid, player);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при аутентификации: {ex.Message}");
                return (false, null);
            }
        }

        private async Task HandleLoginAsync(Dictionary<string, JsonElement> data)
        {
            if (data.TryGetValue("Username", out var nameElement) &&
                nameElement.GetString() is string username &&
                data.TryGetValue("Password", out var passwordElement) &&
                passwordElement.GetString() is string password)
            {
                var (isAuthenticated, player) = await AuthenticatePlayerAsync(username, password);

                if (isAuthenticated && player != null)
                {
                    PlayerName = username;
                    Console.WriteLine($"Игрок {PlayerName} успешно авторизовался");

                    await SendMessageAsync(JsonSerializer.Serialize(new
                    {
                        Type = "LoginSuccess",
                        Id = player.Id,
                        Username = player.Username,
                        TotalGames = player.TotalGames,
                        Wins = player.Wins,
                        TotalScore = player.TotalScore
                    }));

                    await _server.BroadcastMessageAsync(JsonSerializer.Serialize(new
                    {
                        Type = "PlayerJoined",
                        PlayerName = username
                    }));
                }
                else
                {
                    Console.WriteLine($"Неудачная попытка входа для пользователя {username}");
                    await SendMessageAsync(JsonSerializer.Serialize(new
                    {
                        Type = "Error",
                        Message = "Неверное имя пользователя или пароль"
                    }));
                }
            }
        }

        private async Task HandleRegisterAsync(Dictionary<string, JsonElement> data)
        {
            if (data.TryGetValue("Username", out var nameElement) &&
                nameElement.GetString() is string username &&
                data.TryGetValue("Password", out var passwordElement) &&
                passwordElement.GetString() is string password)
            {
                // Валидация данных
                if (string.IsNullOrWhiteSpace(username) || username.Length < 3)
                {
                    await SendMessageAsync(JsonSerializer.Serialize(new
                    {
                        Type = "Error",
                        Message = "Имя пользователя должно содержать минимум 3 символа"
                    }));
                    return;
                }

                if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
                {
                    await SendMessageAsync(JsonSerializer.Serialize(new
                    {
                        Type = "Error",
                        Message = "Пароль должен содержать минимум 6 символов"
                    }));
                    return;
                }

                bool registrationSuccess = await RegisterPlayerAsync(username, password);

                if (registrationSuccess)
                {
                    // После успешной регистрации сразу авторизуем пользователя
                    var (isAuthenticated, player) = await AuthenticatePlayerAsync(username, password);

                    if (isAuthenticated && player != null)
                    {
                        PlayerName = username;
                        Console.WriteLine($"Игрок {username} успешно зарегистрирован и авторизован");

                        await SendMessageAsync(JsonSerializer.Serialize(new
                        {
                            Type = "RegisterSuccess",
                            Id = player.Id,
                            Username = player.Username,
                            TotalGames = player.TotalGames,
                            Wins = player.Wins,
                            TotalScore = player.TotalScore
                        }));

                        await _server.BroadcastMessageAsync(JsonSerializer.Serialize(new
                        {
                            Type = "PlayerJoined",
                            PlayerName = username
                        }));
                    }
                }
                else
                {
                    await SendMessageAsync(JsonSerializer.Serialize(new
                    {
                        Type = "Error",
                        Message = "Пользователь с таким именем уже существует"
                    }));
                }
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

    // Модель для получения пользовательских вопросов на сервере
    public class QuestionFile
    {
        public List<CategoryFile> Categories { get; set; } = new();
    }

    public class CategoryFile
    {
        public string Name { get; set; } = string.Empty;
        public List<QuestionFile_Item> Questions { get; set; } = new();
    }

    public class QuestionFile_Item
    {
        public string Text { get; set; } = string.Empty;
        public string Answer { get; set; } = string.Empty;
        public int Price { get; set; }
    }
}
