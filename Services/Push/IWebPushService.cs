namespace prohpharmacy_trekking_app.Services.Push;

public interface IWebPushService
{
    Task SendAsync(string endpoint, string p256dh, string auth, string payload);
    string GetPublicKey();
}
