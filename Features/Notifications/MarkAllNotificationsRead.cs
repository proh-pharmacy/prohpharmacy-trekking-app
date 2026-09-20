using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Providers;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Notifications;

public static class MarkAllNotificationsRead
{
    public class Command : IRequest<Result<string>>;

    internal sealed class Handler(AppDbContext db, AuthProvider auth) : IRequestHandler<Command, Result<string>>
    {
        public async Task<Result<string>> Handle(Command request, CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(auth.GetStaffId(), out var staffId))
                return Result.Failure<string>(Error.BadRequest("Invalid user context."));

            var now = DateTime.UtcNow;
            await db.Notifications
                .Where(n => n.NotifiableId == staffId && n.NotifiableType == "StaffMember" && n.ReadAt == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(n => n.ReadAt, now)
                    .SetProperty(n => n.UpdatedAt, now),
                    cancellationToken);

            return Result.Success("All notifications marked as read.");
        }
    }
}

public class MarkAllNotificationsReadEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPatch("api/v1/notifications/read-all", async (ISender sender) =>
        {
            var result = await sender.Send(new MarkAllNotificationsRead.Command());
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Ok(new { message = result.Value });
        })
        .WithTags("Notifications")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Notifications)
        .WithSummary("Mark all notifications as read")
        .Produces<object>(200)
        .RequireAuthorization();
    }
}
