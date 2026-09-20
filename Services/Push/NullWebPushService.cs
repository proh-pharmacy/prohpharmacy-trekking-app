namespace prohpharmacy_trekking_app.Services.Push;

public class NullWebPushService(ILogger<NullWebPushService> logger) : IWebPushService
{
    public Task SendAsync(string endpoint, string p256dh, string auth, string payload)
    {
        logger.LogWarning("NullWebPushService: push skipped — VapidSettings not configured. Payload: {Payload}", payload);
        return Task.CompletedTask;
    }

    public string GetPublicKey() => string.Empty;
}
