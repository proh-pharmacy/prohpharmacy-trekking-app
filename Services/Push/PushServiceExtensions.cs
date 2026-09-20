namespace prohpharmacy_trekking_app.Services.Push;

public static class PushServiceExtensions
{
    public static IServiceCollection AddPushServices(this IServiceCollection services, IConfiguration config)
    {
        var publicKey = config["VapidSettings:PublicKey"];
        if (string.IsNullOrWhiteSpace(publicKey))
            services.AddScoped<IWebPushService, NullWebPushService>();
        else
            services.AddScoped<IWebPushService, WebPushService>();

        services.AddScoped<NotificationDispatcher>();
        return services;
    }
}
