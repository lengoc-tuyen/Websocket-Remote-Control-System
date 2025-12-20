using System;
using System.Security.Cryptography;
using System.Text;

namespace Server.Services
{
    internal static class PasswordHashing
    {
        private const string Scheme = "PBKDF2";
        private const string Prf = "SHA256";
        private const int SaltSizeBytes = 16;
        private const int KeySizeBytes = 32;
        private const int DefaultIterations = 310_000;

        private static string Pepper
            => Environment.GetEnvironmentVariable("AUTH_PASSWORD_PEPPER") ?? string.Empty;

        private static int Iterations
        {
            get
            {
                var raw = Environment.GetEnvironmentVariable("AUTH_PASSWORD_ITERATIONS");
                if (!int.TryParse(raw, out var it)) return DefaultIterations;
                if (it < 50_000) return DefaultIterations;
                if (it > 5_000_000) return DefaultIterations;
                return it;
            }
        }

        public static string HashPassword(string password)
        {
            if (password == null) throw new ArgumentNullException(nameof(password));

            var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
            var derived = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password + Pepper),
                salt,
                Iterations,
                HashAlgorithmName.SHA256,
                KeySizeBytes);

            // Format: PBKDF2$SHA256$<iterations>$<saltB64>$<hashB64>
            return string.Join(
                "$",
                Scheme,
                Prf,
                Iterations.ToString(),
                Convert.ToBase64String(salt),
                Convert.ToBase64String(derived));
        }

        public static bool VerifyPassword(string stored, string password, out bool shouldUpgrade, out string upgradedHash)
        {
            shouldUpgrade = false;
            upgradedHash = string.Empty;

            if (string.IsNullOrWhiteSpace(stored) || password == null) return false;

            // Legacy: plaintext stored in users.txt (auto-upgrade on successful login).
            if (!stored.StartsWith(Scheme + "$", StringComparison.Ordinal))
            {
                var ok = string.Equals(stored, password, StringComparison.Ordinal);
                if (ok)
                {
                    shouldUpgrade = true;
                    upgradedHash = HashPassword(password);
                }
                return ok;
            }

            var parts = stored.Split('$', 5);
            if (parts.Length != 5) return false;
            if (!string.Equals(parts[0], Scheme, StringComparison.Ordinal)) return false;
            if (!string.Equals(parts[1], Prf, StringComparison.Ordinal)) return false;
            if (!int.TryParse(parts[2], out var iters) || iters <= 0) return false;

            byte[] salt;
            byte[] expected;
            try
            {
                salt = Convert.FromBase64String(parts[3]);
                expected = Convert.FromBase64String(parts[4]);
            }
            catch
            {
                return false;
            }

            if (salt.Length < 8 || expected.Length < 16) return false;

            var actual = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password + Pepper),
                salt,
                iters,
                HashAlgorithmName.SHA256,
                expected.Length);

            var okHash = CryptographicOperations.FixedTimeEquals(actual, expected);
            if (!okHash) return false;

            if (iters != Iterations || salt.Length != SaltSizeBytes || expected.Length != KeySizeBytes)
            {
                shouldUpgrade = true;
                upgradedHash = HashPassword(password);
            }

            return true;
        }
    }
}

