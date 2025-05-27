using Microsoft.EntityFrameworkCore;
using GameServer.Models;

namespace GameServer.Data
{
    public class GameDbContext : DbContext
    {
        public DbSet<Category> Categories { get; set; } = null!;
        public DbSet<Question> Questions { get; set; } = null!;
        public DbSet<User> Users { get; set; } = null!;  // Добавлено

        public GameDbContext(DbContextOptions<GameDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Настройка связей
            modelBuilder.Entity<Question>()
                .HasOne(q => q.Category)
                .WithMany(c => c.Questions)
                .HasForeignKey(q => q.CategoryId);

            // Дополнительные настройки для User (если нужны)
            modelBuilder.Entity<User>(entity =>
            {
                entity.HasKey(u => u.Id);  // Указываем первичный ключ
                entity.Property(u => u.Username).IsRequired();  // Обязательное поле
                // Можно добавить индексы, например:
                entity.HasIndex(u => u.Username).IsUnique();  // Уникальный логин
            });

        }
    }
}