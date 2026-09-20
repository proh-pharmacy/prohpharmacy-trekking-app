using Carter;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Services.Push;

namespace prohpharmacy_trekking_app.Features.Notifications;

public class GetVapidPublicKeyEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/notifications/public-key", (IWebPushService push) =>
            Results.Ok(new { publicKey = push.GetPublicKey() }))
        .WithTags("Notifications")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Notifications)
        .WithSummary("Get VAPID public key")
        .WithDescription("Returns the VAPID public key the frontend uses to subscribe to push notifications.")
        .Produces<object>(200)
        .RequireAuthorization();
    }
}
