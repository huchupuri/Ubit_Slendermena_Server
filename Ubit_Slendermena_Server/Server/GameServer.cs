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

        readonly List<WebSocketClientHandler> _clients = new();
        private readonly Dictionary<int, Category> _categories = new();
        private readonly Dictionary<int, Question> _questions = new();
        private readonly string _connectionString;
        public Game _currentGame;
        private readonly object _clientsLock = new();

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
            Console.WriteLine("Поток приема подключений завершен");
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

        public void RemoveClient(WebSocketClientHandler client)
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

        public async Task StartNewGameAsync(byte playerCount)
        {
            List<WebSocketClientHandler> clientsCopy;
            lock (_clientsLock)
            {
                clientsCopy = new List<WebSocketClientHandler>(_clients.Where(c => c.IsConnected));
            }

            if (clientsCopy.Count < playerCount)
            {
                await BroadcastMessageAsync(JsonSerializer.Serialize(new { Type = "Error", Message = "Недостаточно игроков для начала игры" }));
                return;
            }
            if (_currentGame == null)
            {
                _currentGame = new Game(clientsCopy, _categories, _questions, playerCount);
            }
            

            await BroadcastMessageAsync(JsonSerializer.Serialize(new
            {
                Type = "Start"
            }));
        }

        public async Task ProcessAnswerAsync(WebSocketClientHandler client, int questionId, string answer)
        {
            if (_currentGame?.CurrentQuestion?.Id != questionId)
            {
                await client.SendMessageAsync(JsonSerializer.Serialize(new { Type = "Error", Message = "Неверный вопрос" }));
                return;
            }

            bool isCorrect = _currentGame.CheckAnswer(client, answer);

            await BroadcastMessageAsync(JsonSerializer.Serialize(new
            {
                Type = "AnswerResult",
                PlayerId = client.PlayerId,
                PlayerName = client.PlayerName,
                QuestionId = questionId,
                IsCorrect = isCorrect,
                CorrectAnswer = isCorrect ? null : _currentGame.CurrentQuestion.Answer,
                NewScore = client.Score
            }));

            await Task.Delay(3000);
            _currentGame.NextQuestion();
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
}
