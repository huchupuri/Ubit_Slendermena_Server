using GameServer.Data;
using GameServer.Models;
using Microsoft.EntityFrameworkCore;
using GameServer.Technical;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace GameServer
{
    public class ClientHandler
    {
        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private readonly GameServer _server;
        private volatile bool _isConnected;

        public string PlayerId { get; private set; }
        public string PlayerName { get; private set; } = string.Empty;
        public int Score { get; set; }
        public bool IsConnected => _isConnected && _client.Connected;

        public ClientHandler(TcpClient client, GameServer server)
        {
            _client = client;
            _server = server;
            _stream = client.GetStream();
            _isConnected = true;
            PlayerId = Guid.NewGuid().ToString();
            Score = 0;
        }

        public void Handle()
        {
            byte[] buffer = new byte[4096];

            try
            {
                Console.WriteLine($"Начало обработки клиента {PlayerId}");

                while (_isConnected && _client.Connected)
                {
                    int bytesRead = _stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead == 0)
                    {
                        Console.WriteLine($"Клиент {PlayerName} отключился");
                        break;
                    }

                    string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    Console.WriteLine($"Получено от {PlayerName}: {message}");
                    ProcessMessage(message);
                }
            }
            catch (IOException)
            {
                Console.WriteLine($"Клиент {PlayerName} отключился (IOException)");
            }
            catch (SocketException)
            {
                Console.WriteLine($"Клиент {PlayerName} отключился (SocketException)");
            }
            catch (ObjectDisposedException)
            {
                Console.WriteLine($"Соединение с {PlayerName} уже закрыто");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при обработке клиента {PlayerName}: {ex.Message}");
            }
            finally
            {
                CleanupConnection();
            }
        }
        private void AddUserToDatabase(string username, string passsword)
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
                        Password_hash = passsword,
                        TotalGames = 0,
                        Wins = 0,
                        TotalScore = 0
                    };

                    context.Players.Add(newUser);
                    context.SaveChanges();
                    Console.WriteLine($"Пользователь {username} добавлен в базу данных asd");
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

            var db= new GameDbContext(optionsBuilder.Options);
            var player = db.Players.FirstOrDefault(p => p.Username == username);

            if (player == null)
                return (false, null);
            return (player.Password_hash == PasswordHasher.HashPassword(password), player);
        }

        private void ProcessMessage(string message)
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
                                AddUserToDatabase(Username, password);
                                Console.WriteLine($"Игрок {PlayerName} авторизовался");
                                if (isAuthenticated)
                                {
                                    Console.WriteLine($"Игрок {PlayerName} успешно авторизовался");

                                    SendMessage(JsonSerializer.Serialize(new
                                    {
                                        Type = "LoginSuccess",
                                        Id = player.Id,
                                        Username,
                                        TotalGames = player.TotalGames,
                                        Wins = player.Wins,
                                        TotalScore = player.TotalScore
                                    }));
                                    _server.BroadcastMessage(JsonSerializer.Serialize(new
                                    {
                                        Type = "PlayerJoined",
                                        PlayerId,
                                        PlayerName
                                    }), this);
                                }
                                else
                                {
                                    PlayerName = playerName;
                                    AddUserToDatabase(playerName, password);
                                    Console.WriteLine($"Игрок {PlayerName} успешно авторизовался");

                                    SendMessage(JsonSerializer.Serialize(new
                                    {
                                        Type = "LoginSuccess",
                                        PlayerId,
                                        PlayerName,
                                        TotalGames = player.TotalGames,
                                        Wins = player.Wins,
                                        TotalScore = player.TotalScore
                                    }));

                                    _server.BroadcastMessage(JsonSerializer.Serialize(new
                                    {
                                        Type = "PlayerJoined",
                                        PlayerId,
                                        PlayerName
                                    }), this);

                                    SendMessage(JsonSerializer.Serialize(new
                                    {
                                        Type = "LoginFailed",
                                        Message = "Неверное имя пользователя или пароль"
                                    }));
                                }
                            }
                            break;


                        case "StartGame":
                            Console.WriteLine($"Игрок {PlayerName} запросил начало игры");
                            _server.StartNewGame();
                            break;

                        case "Answer":
                            if (data.TryGetValue("QuestionId", out var questionIdElement) &&
                                data.TryGetValue("Answer", out var answerElement) &&
                                answerElement.GetString() is string answer)
                            {
                                int questionId = questionIdElement.GetInt32();
                                Console.WriteLine($"Игрок {PlayerName} ответил: {answer}");
                                _server.ProcessAnswer(this, questionId, answer);
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

        public void SendMessage(string message)
        {
            if (!IsConnected)
            {
                Console.WriteLine("ун че за хуйня");
                return;
            }

            try
            {
                Console.WriteLine("ун че за хуйня0");
                byte[] buffer = Encoding.UTF8.GetBytes(message);
                Console.WriteLine("ун че за хуйня1");
                _stream.Write(buffer, 0, buffer.Length);
                Console.WriteLine("ун че за хуйня2");
                _stream.Flush();
                Console.WriteLine("ун че за хуйня");
            }
            catch (ObjectDisposedException)
            {
                // Соединение уже закрыто - это нормально
                _isConnected = false;
            }
            catch (IOException)
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

        private void CleanupConnection()
        {
            if (_isConnected)
            {
                _isConnected = false;

                try
                {
                    _stream?.Close();
                    _client?.Close();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка при закрытии соединения с {PlayerName}: {ex.Message}");
                }

                _server.RemoveClient(this);

                if (!string.IsNullOrEmpty(PlayerName))
                {
                    _server.BroadcastMessage(JsonSerializer.Serialize(new { Type = "PlayerLeft", PlayerId, PlayerName }));
                }
            }
        }
    }
}
