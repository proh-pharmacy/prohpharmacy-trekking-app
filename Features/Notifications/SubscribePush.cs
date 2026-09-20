using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Notifications.Entities;
using prohpharmacy_trekking_app.Providers;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Notifications;

public static class SubscribePush
{
    public class Command : IRequest<Result<string>>
    {
        public string Endpoint { get; set; } = string.Empty;
        public string P256dh { get; set; } = string.Empty;
        public string Auth { get; set; } = string.Empty;
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Endpoint).NotEmpty().MaximumLength(2048);
            RuleFor(x => x.P256dh).NotEmpty().MaximumLength(100);
            RuleFor(x => x.Auth).NotEmpty().MaximumLength(60);
        }
    }

    internal sealed class Handler(AppDbContext db, IValidator<Command> validator, AuthProvider auth)
        : IRequestHandler<Command, Result<string>>
    {
        public async Task<Result<string>> Handle(Command request, CancellationToken cancellationToken)
        {
            var validation = await validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Result.Failure<string>(Error.ValidationError(validation));

            if (!Guid.TryParse(auth.GetStaffId(), out var staffId))
                return Result.Failure<string>(Error.BadRequest("Invalid user context."));

            var existing = await db.StaffPushSubscriptions
                .FirstOrDefaultAsync(s => s.Endpoint == request.Endpoint, cancellationToken);

            if (existing is not null)
            {
                existing.P256dh = request.P256dh;
                existing.Auth = request.Auth;
                existing.StaffMemberId = staffId;
            }
            else
            {
                db.StaffPushSubscriptions.Add(new StaffPushSubscription
                {
                    StaffMemberId = staffId,
                    Endpoint = request.Endpoint,
                    P256dh = request.P256dh,
                    Auth = request.Auth
                });
            }

            await db.SaveChangesAsync(cancellationToken);
            return Result.Success("Subscribed.");
        }
    }
}

public class SubscribePushEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/notifications/subscribe", async (SubscribePush.Command command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Ok(new { message = result.Value });
        })
        .WithTags("Notifications")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Notifications)
        .WithSummary("Subscribe to push notifications")
        .WithDescription("Registers a browser push subscription for the current staff member. Safe to call on every app load — updates keys if the endpoint already exists.")
        .Produces<object>(200)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
