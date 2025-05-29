using System;
using System.Security.Cryptography;
using System.Text;
namespace GameServer.Technical
{
    /// <summary>
    /// класс для хэширования пароля
    /// </summary>
    public static class PasswordHasher
    {
        private const int SaltSize = 16;

        private const int HashSize = 20;

        private const int Iterations = 10000;

        /// <summary>A
        /// Хеширует пароль с использованием алгоритма PBKDF2.
        /// </summary>
        /// <param name="password">Пароль, который нужно хешировать.</param>
        /// <returns>Хешированный пароль в виде строки Base64, содержащей соль и хеш.</returns>
        /// <remarks>
        /// Метод генерирует случайную соль, использует PBKDF2 для создания хеша пароля,
        /// затем объединяет соль и хеш в один массив байтов и возвращает его как строку Base64.
        /// </remarks>
        public static string HashPassword(string password)
        {
            var salt = new byte[SaltSize];
            using (var rng = new RNGCryptoServiceProvider())
            {
                rng.GetBytes(salt);
            }
            using (var pbkdf2 = new Rfc2898DeriveBytes(password, salt, Iterations))
            {
                var hash = pbkdf2.GetBytes(HashSize);

                var hashBytes = new byte[SaltSize + HashSize];
                Array.Copy(salt, 0, hashBytes, 0, SaltSize);
                Array.Copy(hash, 0, hashBytes, SaltSize, HashSize);

                return Convert.ToBase64String(hashBytes);
            }
        }

        /// <summary>
        /// Проверяет, соответствует ли предоставленный пароль хешированному паролю.
        /// </summary>
        /// <param name="password">Пароль, который нужно проверить.</param>
        /// <param name="hashedPassword">Хешированный пароль (включая соль), с которым выполняется сравнение.</param>
        /// <returns>True, если пароль совпадает с хешированным паролем; иначе — false.</returns>
        /// <remarks>
        /// Метод извлекает соль из хешированного пароля, создает новый хеш для предоставленного пароля
        /// с использованием этой соли, а затем сравнивает полученный хеш с исходным хешем.
        /// </remarks>
        public static bool VerifyPassword(string password, string hashedPassword)
        {
            var hashBytes = Convert.FromBase64String(hashedPassword);

            var salt = new byte[SaltSize];
            Array.Copy(hashBytes, 0, salt, 0, SaltSize);

            using (var pbkdf2 = new Rfc2898DeriveBytes(password, salt, Iterations))
            {
                var hash = pbkdf2.GetBytes(HashSize);

                for (var index = 0; index < HashSize; index++)
                {
                    if (hashBytes[index + SaltSize] != hash[index])
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }
}