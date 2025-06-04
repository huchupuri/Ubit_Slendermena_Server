using GameServer.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace GameServer
{
    public class Game
    {
        private readonly byte _maxPlayers;
        private readonly List<WebSocketClientHandler> _players;
        private readonly Dictionary<int, Category> _categories;
        private readonly Dictionary<int, Question> _questions;
        private readonly HashSet<int> _answeredQuestions = new();
        private readonly GameServerWebSocket _server;
        private System.Threading.Timer _questionTimer;

        public Question? CurrentQuestion { get; private set; }
        public bool IsQuestionActive { get; private set; }
        private readonly HashSet<string> _playersWhoAnswered = new();

        public Game(List<WebSocketClientHandler> players, Dictionary<int, Category> categories,
                   Dictionary<int, Question> questions, byte maxPlayers, GameServerWebSocket server)
        {
            _players = players;
            _categories = categories;
            _questions = questions;
            _maxPlayers = maxPlayers;
            _server = server;
        }

        public void Start()
        {
            Console.WriteLine("Начало новой игры");

            foreach (var player in _players)
            {
                player.Score = 0;
            }

            // Отправляем данные игры всем игрокам
            _ = SendGameDataToAllPlayers();
        }

        private async Task SendGameDataToAllPlayers()
        {
            var gameData = JsonSerializer.Serialize(new
            {
                Type = "GameData",
                Categories = _categories.Values.Select(c => new { c.Id, c.Name }).ToList(),
                Players = _players.Select(p => new { p.PlayerId, p.PlayerName, p.Score }).ToList()
            });

            foreach (var player in _players.Where(p => p.IsConnected))
            {
                await player.SendMessageAsync(gameData);
            }
        }

        public bool IsQuestionAnswered(int questionId)
        {
            return _answeredQuestions.Contains(questionId);
        }

        public async Task ShowQuestionAsync(Question question)
        {
            if (IsQuestionActive)
            {
                return; // Уже есть активный вопрос
            }

            CurrentQuestion = question;
            IsQuestionActive = true;
            _playersWhoAnswered.Clear();

            Console.WriteLine($"Показываем вопрос: {question.Text}");

            // Отправляем вопрос всем игрокам
            var questionMessage = JsonSerializer.Serialize(new
            {
                Type = "Question",
                Question = new
                {
                    Id = question.Id,
                    CategoryId = question.CategoryId,
                    CategoryName = _categories[question.CategoryId].Name,
                    Text = question.Text,
                    Price = question.Price
                }
            });

            foreach (var player in _players.Where(p => p.IsConnected))
            {
                await player.SendMessageAsync(questionMessage);
            }

            // Запускаем таймер на 60 секунд
            _questionTimer = new System.Threading.Timer(OnQuestionTimeout, null, TimeSpan.FromSeconds(60), Timeout.InfiniteTimeSpan);
        }

        private async void OnQuestionTimeout(object state)
        {
            if (IsQuestionActive && CurrentQuestion != null)
            {
                Console.WriteLine("Время на вопрос истекло");

                await _server.BroadcastMessageAsync(JsonSerializer.Serialize(new
                {
                    Type = "QuestionTimeout",
                    CorrectAnswer = CurrentQuestion.Answer
                }));

                await CompleteQuestionAsync();
            }
        }

        public async Task ProcessAnswerAsync(WebSocketClientHandler player, string answer)
        {
            if (!IsQuestionActive || CurrentQuestion == null)
            {
                return;
            }

            // Проверяем, не отвечал ли уже этот игрок
            if (_playersWhoAnswered.Contains(player.PlayerId))
            {
                await player.SendMessageAsync(JsonSerializer.Serialize(new
                {
                    Type = "Error",
                    Message = "Вы уже ответили на этот вопрос"
                }));
                return;
            }

            _playersWhoAnswered.Add(player.PlayerId);

            bool isCorrect = string.Equals(answer.Trim(), CurrentQuestion.Answer.Trim(), StringComparison.OrdinalIgnoreCase);

            Console.WriteLine($"Ответ {player.PlayerName}: '{answer}' - {(isCorrect ? "✅" : "❌")}");

            if (isCorrect)
            {
                player.Score += CurrentQuestion.Price;

                // Останавливаем таймер
                _questionTimer?.Dispose();

                // Отправляем результат всем игрокам
                await _server.BroadcastMessageAsync(JsonSerializer.Serialize(new
                {
                    Type = "AnswerResult",
                    PlayerId = player.PlayerId,
                    PlayerName = player.PlayerName,
                    QuestionId = CurrentQuestion.Id,
                    IsCorrect = true,
                    NewScore = player.Score,
                    Answer = answer
                }));

                // Завершаем вопрос
                await CompleteQuestionAsync();
            }
            else
            {
                player.Score -= CurrentQuestion.Price;

                // Отправляем результат всем игрокам
                await _server.BroadcastMessageAsync(JsonSerializer.Serialize(new
                {
                    Type = "AnswerResult",
                    PlayerId = player.PlayerId,
                    PlayerName = player.PlayerName,
                    QuestionId = CurrentQuestion.Id,
                    IsCorrect = false,
                    NewScore = player.Score,
                    CorrectAnswer = CurrentQuestion.Answer,
                    Answer = answer
                }));

                // Проверяем, все ли игроки ответили
                if (_playersWhoAnswered.Count >= _players.Count(p => p.IsConnected))
                {
                    _questionTimer?.Dispose();
                    await CompleteQuestionAsync();
                }
            }
        }

        private async Task CompleteQuestionAsync()
        {
            if (CurrentQuestion == null) return;

            _answeredQuestions.Add(CurrentQuestion.Id);
            IsQuestionActive = false;

            // Отправляем сообщение о завершении вопроса
            await _server.BroadcastMessageAsync(JsonSerializer.Serialize(new
            {
                Type = "QuestionCompleted",
                QuestionId = CurrentQuestion.Id
            }));

            CurrentQuestion = null;
            _playersWhoAnswered.Clear();

            // Проверяем, закончилась ли игра
            if (_answeredQuestions.Count >= _questions.Count)
            {
                await EndGameAsync();
            }
        }

        private async Task EndGameAsync()
        {
            var winner = _players.Where(p => p.IsConnected).OrderByDescending(p => p.Score).FirstOrDefault();

            Console.WriteLine($"Игра окончена. Победитель: {winner?.PlayerName} ({winner?.Score} очков)");

            var gameOverMessage = JsonSerializer.Serialize(new
            {
                Type = "GameOver",
                Winner = winner != null ? new { winner.PlayerId, winner.PlayerName, winner.Score } : null,
                Players = _players.Where(p => p.IsConnected)
                    .OrderByDescending(p => p.Score)
                    .Select(p => new { p.PlayerId, p.PlayerName, p.Score })
                    .ToList()
            });

            foreach (var player in _players.Where(p => p.IsConnected))
            {
                await player.SendMessageAsync(gameOverMessage);
            }
        }
    }
}
