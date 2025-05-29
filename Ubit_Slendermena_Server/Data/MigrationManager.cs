using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using GameServer.Models;
using GameServer.Technical;

namespace GameServer.Data   
{
    // Фабрика для создания DbContext во время разработки (для миграций)
    public class GameDbContextFactory : IDesignTimeDbContextFactory<GameDbContext>
    {
        public GameDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<GameDbContext>();

            // Строка подключения для разработки
            string connectionString = "Host=localhost;Port=5432;Database=jeopardy;Username=postgres;Password=postgres";

            optionsBuilder.UseNpgsql(connectionString);

            return new GameDbContext(optionsBuilder.Options);
        }
    }

    // Класс для инициализации базы данных
    public static class DbInitializer
    {
        public static void Initialize(GameDbContext context)
        {
            context.Database.EnsureCreated();

            // Проверяем, есть ли уже данные в базе
            if (context.Categories.Any())
            {
                return; // База данных уже заполнена
            }

            // Добавляем категории
            var categories = new Category[]
            {
                new Category { Name = "История" },
                new Category { Name = "География" },
                new Category { Name = "Наука" },
                new Category { Name = "Спорт" },
                new Category { Name = "Искусство" }
            };

            context.Categories.AddRange(categories);
            context.SaveChanges();

            // Добавляем вопросы
            var questions = new Question[]
            {
                // История
                new Question { CategoryId = 1, Text = "В каком году началась Вторая мировая война?", Answer = "1939", Price = 100 },
                new Question { CategoryId = 1, Text = "Кто был первым президентом США?", Answer = "Джордж Вашингтон", Price = 200 },
                new Question { CategoryId = 1, Text = "Какой город был столицей Древней Руси?", Answer = "Киев", Price = 300 },
                new Question { CategoryId = 1, Text = "В каком году произошла Октябрьская революция в России?", Answer = "1917", Price = 400 },
                new Question { CategoryId = 1, Text = "Кто был последним императором России?", Answer = "Николай II", Price = 500 },

                // География
                new Question { CategoryId = 2, Text = "Какая самая длинная река в мире?", Answer = "Нил", Price = 100 },
                new Question { CategoryId = 2, Text = "Какая страна самая большая по площади?", Answer = "Россия", Price = 200 },
                new Question { CategoryId = 2, Text = "Какой самый высокий водопад в мире?", Answer = "Анхель", Price = 300 },
                new Question { CategoryId = 2, Text = "Какой самый большой океан?", Answer = "Тихий", Price = 400 },
                new Question { CategoryId = 2, Text = "Какая самая маленькая страна в мире?", Answer = "Ватикан", Price = 500 },

                // Наука
                new Question { CategoryId = 3, Text = "Какой элемент имеет химический символ O?", Answer = "Кислород", Price = 100 },
                new Question { CategoryId = 3, Text = "Кто открыл закон всемирного тяготения?", Answer = "Исаак Ньютон", Price = 200 },
                new Question { CategoryId = 3, Text = "Какая планета находится ближе всего к Солнцу?", Answer = "Меркурий", Price = 300 },
                new Question { CategoryId = 3, Text = "Какое самое распространенное вещество во Вселенной?", Answer = "Водород", Price = 400 },
                new Question { CategoryId = 3, Text = "Кто разработал теорию относительности?", Answer = "Альберт Эйнштейн", Price = 500 },

                // Спорт
                new Question { CategoryId = 4, Text = "В каком виде спорта используется шайба?", Answer = "Хоккей", Price = 100 },
                new Question { CategoryId = 4, Text = "Сколько игроков в футбольной команде на поле?", Answer = "11", Price = 200 },
                new Question { CategoryId = 4, Text = "Какая страна выиграла больше всего чемпионатов мира по футболу?", Answer = "Бразилия", Price = 300 },
                new Question { CategoryId = 4, Text = "Какой вид спорта называют \"королевой спорта\"?", Answer = "Легкая атлетика", Price = 400 },
                new Question { CategoryId = 4, Text = "Кто выиграл больше всего турниров Большого шлема в теннисе?", Answer = "Новак Джокович", Price = 500 },

                // Искусство
                new Question { CategoryId = 5, Text = "Кто написал \"Мону Лизу\"?", Answer = "Леонардо да Винчи", Price = 100 },
                new Question { CategoryId = 5, Text = "Кто автор романа \"Война и мир\"?", Answer = "Лев Толстой", Price = 200 },
                new Question { CategoryId = 5, Text = "Какой художник отрезал себе ухо?", Answer = "Винсент Ван Гог", Price = 300 },
                new Question { CategoryId = 5, Text = "Кто написал \"Евгения Онегина\"?", Answer = "Александр Пушкин", Price = 400 },
                new Question { CategoryId = 5, Text = "Какой музыкант известен как \"Король рок-н-ролла\"?", Answer = "Элвис Пресли", Price = 500 }
            };

            context.Questions.AddRange(questions);
            context.SaveChanges();
            var players = new List<Player>
            {
                new Player
                {
                    Username = "admin",
                    Password_hash = PasswordHasher.HashPassword("admin"),
                    TotalGames = 5,
                    Wins = 3,
                    TotalScore = 1500
                },
                new Player
                {
                    Username = "guest",
                    Password_hash = PasswordHasher.HashPassword("guest"),
                    TotalGames = 2,
                    Wins = 0,
                    TotalScore = 300
                }
            };

            context.Players.AddRange(players);
            context.SaveChanges();
        }
    }
}