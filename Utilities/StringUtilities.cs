using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace prohpharmacy_trekking_app.Utilities
{
    public static class StringUtilities
    {
        public static string Slugify(string name)
        {
            var slug = name.Trim().ToUpper();
            slug = Regex.Replace(slug, @"[^A-Z0-9\s]", "");
            slug = Regex.Replace(slug, @"\s+", "-");
            return slug;
        }

        public static async Task<string> GenerateUniqueCodeAsync(
            string name,
            Func<string, Task<bool>> existsAsync)
        {
            var baseCode = Slugify(name);
            if (!await existsAsync(baseCode)) return baseCode;

            for (var i = 2; i <= 999; i++)
            {
                var candidate = $"{baseCode}-{i}";
                if (!await existsAsync(candidate)) return candidate;
            }

            return $"{baseCode}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}";
        }
        /// <summary>Generates a random 4-digit OTP.</summary>
        public static string GenerateRandomOtp()
        {
            var random = new Random();
            int otpNumber = random.Next(1000, 9999);
            return otpNumber.ToString("D4");
        }

        /// <summary>Generates a cryptographically random hex string of the given byte length.</summary>
        public static string GenerateRandomHexString(int byteLength = 16)
        {
            var bytes = GenerateRandomBytes(byteLength);
            return ConvertBytesToHexString(bytes);
        }

        /// <summary>Generates a secure random token (URL-safe Base64).</summary>
        public static string GenerateSecureToken()
        {
            var bytes = GenerateRandomBytes(32);
            return Convert.ToBase64String(bytes)
                .Replace("+", "-")
                .Replace("/", "_")
                .TrimEnd('=');
        }

        public static byte[] GenerateRandomBytes(int length)
        {
            byte[] rndBytes = new byte[length];
            RandomNumberGenerator.Fill(rndBytes);
            return rndBytes;
        }

        public static string ConvertBytesToHexString(byte[] bytes)
            => BitConverter.ToString(bytes).Replace("-", "");

        /// <summary>
        /// Replaces common template placeholders. E.g., "[Name]" and "[Link]".
        /// </summary>
        public static string FillTemplate(string template, Dictionary<string, string> replacements)
        {
            foreach (var kv in replacements)
                template = template.Replace($"[{kv.Key}]", kv.Value);
            return template;
        }
    }
}
