using Microsoft.EntityFrameworkCore;
using GameServer.Models;

namespace GameServer.Data
{
    public class GameDbContext : DbContext
    {
        public DbSet<Category> Categories { get; set; } = null!;
        public DbSet<Question> Questions { get; set; } = null!;
        public DbSet<Player> Players { get; set; } = null!;  // Изменено с User на Player

        public GameDbContext(DbContextOptions<GameDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Явно указываем имена таблиц
            modelBuilder.Entity<Category>().ToTable("Categories");
            modelBuilder.Entity<Question>().ToTable("Questions");
            modelBuilder.Entity<Player>().ToTable("Players");

            // Настройка связей для вопросов и категорий
            modelBuilder.Entity<Question>()
                .HasOne(q => q.Category)
                .WithMany(c => c.Questions)
                .HasForeignKey(q => q.CategoryId);

            // Настройка сущности Player
            modelBuilder.Entity<Player>(entity =>
            {
                entity.ToTable("Players"); // Явно указываем имя таблицы
                entity.HasKey(p => p.Id).HasName("PlayerId"); // Явно указываем имя первичного ключа

                entity.Property(p => p.Username)
                    .IsRequired()
                    .HasMaxLength(50)
                    .HasColumnName("Username"); // Явно указываем имя столбца

                entity.Property(p => p.Password_hash)
                    .IsRequired()
                    .HasColumnName("Password_hash"); // Согласованное имя столбца

                entity.Property(p => p.TotalGames)
                    .HasDefaultValue(0)
                    .HasColumnName("TotalGames");

                entity.Property(p => p.Wins)
                    .HasDefaultValue(0)
                    .HasColumnName("Wins");

                entity.Property(p => p.TotalScore)
                    .HasDefaultValue(0)
                    .HasColumnName("TotalScore");

                // Уникальный индекс для имени пользователя
                entity.HasIndex(p => p.Username)
                    .IsUnique()
                    .HasDatabaseName("IX_Players_Username");
            });

            // Для PostgreSQL рекомендуется явно указывать схему
            modelBuilder.HasDefaultSchema("public");
        }
    }
}