using System.Security.Cryptography;

namespace prohpharmacy_trekking_app.Utilities
{
    public static class StringUtilities
    {
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
