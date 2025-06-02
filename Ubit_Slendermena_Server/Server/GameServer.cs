using GameServer.Data;
using GameServer.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace GameServer
{
    public class GameServer
    {
        private TcpListener? _server;
        private bool _isRunning;
        private readonly List<ClientHandler> _clients = [];
        private readonly Dictionary<int, Category> _categories = [];
        private readonly Dictionary<int, Question> _questions = [];
        private readonly string _connectionString;
        private Game? _currentGame;
        private readonly object _clientsLock = new();
        
        public GameServer(string connectionString)
        {
            _connectionString = connectionString;
            LoadGameData();
        }

        public void Start(int port)
        {
            try
            {
                _server = new TcpListener(IPAddress.Any, port);
                _server.Start();
                _isRunning = true;
                Console.WriteLine($"Сервер запущен на порту {port}");

                Thread acceptThread = new(AcceptClients) { IsBackground = true };
                acceptThread.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка запуска сервера: {ex.Message}");
                throw;
            }
        }

        private void AcceptClients()
        {
            while (_isRunning)
            {
                try
                {
                    if (_server is null) break;

                    Console.WriteLine("Ожидание подключений...");
                    TcpClient client = _server.AcceptTcpClient();
                    Console.WriteLine($"Новое подключение от {client.Client.RemoteEndPoint}");

                    ClientHandler clientHandler = new(client, this);
                    

                    lock (_clientsLock)
                    {
                        _clients.Add(clientHandler);
                    }
                    foreach (var clientAcc in _clients)
                        Console.WriteLine(client);

                    Thread clientThread = new(clientHandler.Handle) { IsBackground = true };
                    clientThread.Start();
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (SocketException ex) when (!_isRunning)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка при подключении клиента: {ex.Message}");
                }
            }
            Console.WriteLine("Поток приема подключений завершен");
        }

        public void BroadcastMessage(string message)
        {
            foreach (var client in _clients)
            {
                client.SendMessage(message);
                
            }
        }

        public void RemoveClient(ClientHandler client)
        {
            lock (_clientsLock)
            {
                if (_clients.Remove(client))
                {
                    Console.WriteLine($"Клиент {client.PlayerName} отключен. Всего клиентов: {_clients.Count}");
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
                        Console.WriteLine();
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

        public void StartNewGame(byte playerCount)
        {
            List<ClientHandler> clientsCopy;
            lock (_clientsLock)
            {
                clientsCopy = new List<ClientHandler>(_clients.Where(c => c.IsConnected));
            }

            if (clientsCopy.Count < playerCount)
            {
                BroadcastMessage(JsonSerializer.Serialize(new { Type = "Error", Message = "Недостаточно игроков для начала игры" }));
                return;
            }

            _currentGame = new Game(clientsCopy, _categories, _questions, playerCount);
            _currentGame.Start();

            BroadcastMessage(JsonSerializer.Serialize(new
            {
                Type = "Start",
                //Categories = _categories.Values,
                //Players = clientsCopy.Select(c => new { c.PlayerId, c.PlayerName, c.Score }).ToList()
            }));
        }

        public void ProcessAnswer(ClientHandler client, int questionId, string answer)
        {
            if (_currentGame?.CurrentQuestion?.Id != questionId)
            {
                client.SendMessage(JsonSerializer.Serialize(new { Type = "Error", Message = "Неверный вопрос" }));
                return;
            }

            bool isCorrect = _currentGame.CheckAnswer(client, answer);

            BroadcastMessage(JsonSerializer.Serialize(new
            {
                Type = "AnswerResult",
                PlayerId = client.PlayerId,
                PlayerName = client.PlayerName,
                QuestionId = questionId,
                IsCorrect = isCorrect,
                CorrectAnswer = isCorrect ? null : _currentGame.CurrentQuestion.Answer,
                NewScore = client.Score
            }));

            Task.Delay(3000).ContinueWith(_ => _currentGame.NextQuestion());
        }

        public void Stop()
        {
            Console.WriteLine("Остановка сервера...");
            _isRunning = false;

            try
            {
                _server?.Stop();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при остановке сервера: {ex.Message}");
            }

            Console.WriteLine("Сервер остановлен");
        }

    }
}
