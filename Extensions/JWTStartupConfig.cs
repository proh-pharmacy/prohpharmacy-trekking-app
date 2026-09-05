using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace prohpharmacy_trekking_app.Extensions
{
    public static class JWTStartupConfig
    {
        internal static void ConfigureJwt(IServiceCollection services, IConfiguration configuration)
        {
            var appKey = configuration.GetValue<string>("SiteSettings:AppKey")
                ?? throw new InvalidOperationException("SiteSettings:AppKey is not configured.");

            var jwtSettings = configuration.GetSection("JwtSettings");

            services.AddAuthentication(x =>
            {
                x.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                x.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            }).AddJwtBearer(x =>
            {
                x.RequireHttpsMetadata = false;
                x.SaveToken = true;
                x.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(appKey)),
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidAudience = jwtSettings["validAudience"],
                    ValidIssuer = jwtSettings["validIssuer"]
                };
            });
        }
    }
}
