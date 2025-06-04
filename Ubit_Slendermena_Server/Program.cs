using Microsoft.EntityFrameworkCore;
using GameServer.Data;

namespace GameServerApp
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("🎮 Запуск WebSocket сервера 'Своя игра'...");

            string connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING") ??
                "Host=localhost;Port=5432;Database=jeopardy;Username=postgres;Password=postgres";

            if (!int.TryParse(Environment.GetEnvironmentVariable("SERVER_PORT"), out int port))
            {
                port = 5000;
            }

            GameServer.GameServerWebSocket? server = null;

            try
            {
                await WaitForDatabaseAsync(connectionString);

                server = new GameServer.GameServerWebSocket(connectionString);
                server.Start(port);

                Console.WriteLine("WebSocket сервер запущен успешно!");
                Console.WriteLine("Логи сервера:");

                // Keep the application running
                await Task.Delay(Timeout.Infinite);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Критическая ошибка: {ex.Message}");
            }
        }

        static async Task WaitForDatabaseAsync(string connectionString)
        {
            Console.WriteLine("Подключение к базе данных...");

            var optionsBuilder = new DbContextOptionsBuilder<GameDbContext>();
            optionsBuilder.UseNpgsql(connectionString);

            await using var context = new GameDbContext(optionsBuilder.Options);

            for (int i = 0; i < 10; i++)
            {
                try
                {
                    if (await context.Database.CanConnectAsync())
                    {
                        Console.WriteLine("База данных подключена");
                        await context.Database.EnsureCreatedAsync();
                        context.Database.Migrate();
                        DbInitializer.Initialize(context);
                        Console.WriteLine("✅ База данных готова");
                        return;
                    }
                }
                catch
                {
                    Console.WriteLine($"⏳ Попытка {i + 1}/10...");
                    await Task.Delay(2000);
                }
            }

            throw new Exception("Не удалось подключиться к базе данных");
        }
    }
}
