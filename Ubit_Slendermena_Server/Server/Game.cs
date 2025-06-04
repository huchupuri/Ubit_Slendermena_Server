using GameServer.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
        private readonly List<int> _availableQuestions;
        public Question? CurrentQuestion { get; private set; }

        public Game(List<WebSocketClientHandler> players, Dictionary<int, Category> categories, Dictionary<int, Question> questions, byte maxPlayers)
        {
            _players = players;
            _categories = categories;
            _questions = questions;
            _availableQuestions = new List<int>(questions.Keys);
            _maxPlayers = maxPlayers;
        }
        public List<WebSocketClientHandler> GetPlayers()
        {
            return _players;
        }
        public void Start()
        {
            Console.WriteLine("Начало новой игры");

            foreach (var player in _players)
            {
                player.Score = 0;
            }

            NextQuestion();
        }

        public void NextQuestion()
        {
            if (_availableQuestions.Count == 0)
            {
                Console.WriteLine("Игра окончена");
                EndGame();
                return;
            }

            Random random = new();
            int index = random.Next(_availableQuestions.Count);
            int questionId = _availableQuestions[index];
            _availableQuestions.RemoveAt(index);

            CurrentQuestion = _questions[questionId];
            Console.WriteLine($"Новый вопрос: {CurrentQuestion.Text}");

            string message = JsonSerializer.Serialize(new
            {
                Type = "Question",
                Question = new
                {
                    Id = CurrentQuestion.Id,
                    CategoryId = CurrentQuestion.CategoryId,
                    CategoryName = _categories[CurrentQuestion.CategoryId].Name,
                    Text = CurrentQuestion.Text,
                    Price = CurrentQuestion.Price
                }
            });

            foreach (var player in _players.Where(p => p.IsConnected))
            {
                _ = player.SendMessageAsync(message);
            }
        }

        public bool CheckAnswer(WebSocketClientHandler player, string answer)
        {
            if (CurrentQuestion is null)
                return false;

            bool isCorrect = string.Equals(answer.Trim(), CurrentQuestion.Answer.Trim(), StringComparison.OrdinalIgnoreCase);

            Console.WriteLine($"Ответ {player.PlayerName}: '{answer}' - {(isCorrect ? "✅" : "❌")}");

            if (isCorrect)
            {
                player.Score += CurrentQuestion.Price;
            }
            else
            {
                player.Score -= CurrentQuestion.Price;
            }

            return isCorrect;
        }

        private void EndGame()
        {
            WebSocketClientHandler? winner = _players.Where(p => p.IsConnected).MaxBy(p => p.Score);

            Console.WriteLine($"Игра окончена. Победитель: {winner?.PlayerName} ({winner?.Score} очков)");

            string message = JsonSerializer.Serialize(new
            {
                Type = "GameOver",
                Winner = winner is not null ? new { winner.PlayerId, winner.PlayerName, winner.Score } : null,
                Players = _players.Where(p => p.IsConnected).Select(p => new { p.PlayerId, p.PlayerName, p.Score }).ToList()
            });

            foreach (var player in _players.Where(p => p.IsConnected))
            {
                _ = player.SendMessageAsync(message);
            }
        }
    }
}
