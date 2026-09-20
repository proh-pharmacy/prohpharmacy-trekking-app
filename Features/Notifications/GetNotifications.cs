using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Notifications.Entities;
using prohpharmacy_trekking_app.Providers;
using prohpharmacy_trekking_app.Shared;
using prohpharmacy_trekking_app.Utilities;

namespace prohpharmacy_trekking_app.Features.Notifications;

public static class GetNotifications
{
    public class Query : IRequest<Result<object>>
    {
        public int? PageNumber { get; set; }
        public int? PageSize { get; set; }
        public bool? UnreadOnly { get; set; }
    }

    public class NotificationResponse
    {
        public Guid Id { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? Data { get; set; }
        public bool IsRead { get; set; }
        public DateTime? ReadAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class NotificationsListResponse
    {
        public int UnreadCount { get; set; }
        public object Notifications { get; set; } = null!;
    }

    internal sealed class Handler(AppDbContext db, AuthProvider auth) : IRequestHandler<Query, Result<object>>
    {
        public async Task<Result<object>> Handle(Query request, CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(auth.GetStaffId(), out var staffId))
                return Result.Failure<object>(Error.BadRequest("Invalid user context."));

            var query = db.Notifications
                .Where(n => n.NotifiableId == staffId && n.NotifiableType == "StaffMember")
                .AsNoTracking();

            if (request.UnreadOnly == true)
                query = query.Where(n => n.ReadAt == null);

            var unreadCount = await db.Notifications
                .Where(n => n.NotifiableId == staffId && n.NotifiableType == "StaffMember" && n.ReadAt == null)
                .CountAsync(cancellationToken);

            var paged = await new QueryBuilder<AppNotification>(query)
                .WithSort("createdAt_desc")
                .Paginate(request.PageNumber, request.PageSize)
                .BuildAsync(n => (object)new NotificationResponse
                {
                    Id = n.Id,
                    Type = n.Type,
                    Title = n.Title,
                    Message = n.Message,
                    Data = n.Data,
                    IsRead = n.ReadAt.HasValue,
                    ReadAt = n.ReadAt,
                    CreatedAt = n.CreatedAt
                });

            return Result.Success<object>(new NotificationsListResponse
            {
                UnreadCount = unreadCount,
                Notifications = paged
            });
        }
    }
}

public class GetNotificationsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/notifications", async (
            ISender sender,
            AuthProvider auth,
            [FromQuery] int? pageNumber,
            [FromQuery] int? pageSize,
            [FromQuery] bool? unreadOnly) =>
        {
            var result = await sender.Send(new GetNotifications.Query
            {
                PageNumber = pageNumber,
                PageSize = pageSize,
                UnreadOnly = unreadOnly
            });
            return result.IsFailure ? Results.BadRequest(result.Error) : Results.Ok(result.Value);
        })
        .WithTags("Notifications")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Notifications)
        .WithSummary("List notifications for the current user")
        .WithDescription("Returns paginated notifications for the logged-in staff member. Includes an `unreadCount` for the bell badge. Pass `unreadOnly=true` to fetch only unread.")
        .Produces<GetNotifications.NotificationsListResponse>(200)
        .RequireAuthorization();
    }
}
