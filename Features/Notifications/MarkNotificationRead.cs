using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Providers;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Notifications;

public static class MarkNotificationRead
{
    public class Command : IRequest<Result<string>>
    {
        public Guid Id { get; set; }
    }

    internal sealed class Handler(AppDbContext db, AuthProvider auth) : IRequestHandler<Command, Result<string>>
    {
        public async Task<Result<string>> Handle(Command request, CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(auth.GetStaffId(), out var staffId))
                return Result.Failure<string>(Error.BadRequest("Invalid user context."));

            var notification = await db.Notifications
                .FirstOrDefaultAsync(n => n.Id == request.Id && n.NotifiableId == staffId, cancellationToken);

            if (notification is null)
                return Result.Failure<string>(Error.CreateNotFoundError("Notification not found."));

            if (notification.ReadAt is null)
            {
                notification.ReadAt = DateTime.UtcNow;
                notification.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
            }

            return Result.Success("Marked as read.");
        }
    }
}

public class MarkNotificationReadEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPatch("api/v1/notifications/{id:guid}/read", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new MarkNotificationRead.Command { Id = id });
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Ok(new { message = result.Value });
        })
        .WithTags("Notifications")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Notifications)
        .WithSummary("Mark a notification as read")
        .Produces<object>(200)
        .Produces<Error>(404)
        .RequireAuthorization();
    }
}
