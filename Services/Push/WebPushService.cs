using WebPush;

namespace prohpharmacy_trekking_app.Services.Push;

public class WebPushService : IWebPushService
{
    private readonly WebPushClient _client = new();
    private readonly VapidDetails _vapid;
    private readonly string _publicKey;

    public WebPushService(IConfiguration config)
    {
        _publicKey = config["VapidSettings:PublicKey"]!;
        _vapid = new VapidDetails(
            config["VapidSettings:Subject"]!,
            _publicKey,
            config["VapidSettings:PrivateKey"]!);
    }

    public async Task SendAsync(string endpoint, string p256dh, string auth, string payload)
    {
        var subscription = new PushSubscription(endpoint, p256dh, auth);
        await _client.SendNotificationAsync(subscription, payload, _vapid);
    }

    public string GetPublicKey() => _publicKey;
}
