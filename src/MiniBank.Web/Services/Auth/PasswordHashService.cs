using System.Security.Cryptography;

namespace MiniBank.Web.Services.Auth
{
    public class PasswordHashService
    {
        private const string Prefix = "PBKDF2$";
        private const int SaltSize = 16;
        private const int HashSize = 32;
        private const int Iterations = 150_000;

        public string HashPassword(string password)
        {
            password ??= string.Empty;

            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var hash = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                Iterations,
                HashAlgorithmName.SHA256,
                HashSize);

            return Prefix
                + Iterations.ToString()
                + "$"
                + Convert.ToBase64String(salt)
                + "$"
                + Convert.ToBase64String(hash);
        }

        public bool CanVerify(string stored)
        {
            stored ??= string.Empty;
            return stored.StartsWith(Prefix, StringComparison.Ordinal);
        }

        public bool Verify(string stored, string password)
        {
            stored ??= string.Empty;
            password ??= string.Empty;

            if (!stored.StartsWith(Prefix, StringComparison.Ordinal))
                return false;

            var parts = stored.Split('$');
            if (parts.Length != 4)
                return false;

            if (!int.TryParse(parts[1], out var iterations) || iterations <= 0)
                return false;

            byte[] salt;
            byte[] expected;
            try
            {
                salt = Convert.FromBase64String(parts[2]);
                expected = Convert.FromBase64String(parts[3]);
            }
            catch
            {
                return false;
            }

            var actual = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                expected.Length);

            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
    }
}
