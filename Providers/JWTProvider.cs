using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace prohpharmacy_trekking_app.Providers
{
    /// <summary>
    /// Generates signed JWT tokens.
    /// Inject as a singleton from Program.cs.
    /// </summary>
    public class JWTProvider
    {
        private readonly string _appKey;

        public JWTProvider(string appKey)
        {
            if (string.IsNullOrWhiteSpace(appKey))
                throw new ArgumentException("AppKey must not be empty.", nameof(appKey));
            _appKey = appKey;
        }

        /// <summary>
        /// Generates a JWT token with the supplied claims and optional expiry (in minutes).
        /// </summary>
        public string GenerateToken(IEnumerable<Claim> claims, int expiryMinutes = 3200)
        {
            var key = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(_appKey));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256Signature);

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddMinutes(expiryMinutes),
                SigningCredentials = credentials
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }
    }
}
