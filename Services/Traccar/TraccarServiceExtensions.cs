using System.Net.Http.Headers;
using System.Text;

namespace prohpharmacy_trekking_app.Services.Traccar;

public static class TraccarServiceExtensions
{
    public static IServiceCollection AddTraccarServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TraccarSettings>(configuration.GetSection("TraccarSettings"));

        var settings = configuration.GetSection("TraccarSettings").Get<TraccarSettings>();

        if (string.IsNullOrWhiteSpace(settings?.BaseUrl))
        {
            services.AddSingleton<ITraccarService, NullTraccarService>();
            return services;
        }

        services.AddHttpClient<ITraccarService, TraccarService>(client =>
        {
            client.BaseAddress = new Uri(settings.BaseUrl.TrimEnd('/') + "/");
            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{settings.Username}:{settings.Password}"));
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", credentials);
        });

        return services;
    }
}
