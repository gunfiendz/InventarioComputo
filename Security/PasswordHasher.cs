using System.Security.Cryptography;

namespace InventarioComputo.Security
{
    public static class PasswordHasher
    {
        // Se podrían subir las iteraciones (más seguro pero más lento).
        private const int Iterations = 100_000;
        private const int SaltSize = 16;   // 128 bits
        private const int KeySize = 32;   // 256 bits

        // Devuelve: "iteraciones.saltBase64.hashBase64"
        public static string Hash(string password)
        {
            using var rng = RandomNumberGenerator.Create();
            var salt = new byte[SaltSize];
            rng.GetBytes(salt);

            var hash = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                Iterations,
                HashAlgorithmName.SHA256,
                KeySize
            );

            return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
        }

        public static bool Verify(string password, string hashString)
        {
            var parts = hashString.Split('.', 3);
            if (parts.Length != 3) return false;

            var iterations = int.Parse(parts[0]);
            var salt = Convert.FromBase64String(parts[1]);
            var hash = Convert.FromBase64String(parts[2]);

            var hashToCompare = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                hash.Length
            );

            // comparación en tiempo constante
            return CryptographicOperations.FixedTimeEquals(hashToCompare, hash);
        }
    }
}
