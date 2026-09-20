using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Notifications;

public static class UnsubscribePush
{
    public class Command : IRequest<Result<string>>
    {
        public string Endpoint { get; set; } = string.Empty;
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Endpoint).NotEmpty();
        }
    }

    internal sealed class Handler(AppDbContext db, IValidator<Command> validator)
        : IRequestHandler<Command, Result<string>>
    {
        public async Task<Result<string>> Handle(Command request, CancellationToken cancellationToken)
        {
            var validation = await validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Result.Failure<string>(Error.ValidationError(validation));

            await db.StaffPushSubscriptions
                .Where(s => s.Endpoint == request.Endpoint)
                .ExecuteDeleteAsync(cancellationToken);

            return Result.Success("Unsubscribed.");
        }
    }
}

public class UnsubscribePushEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/notifications/unsubscribe", async (UnsubscribePush.Command command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Ok(new { message = result.Value });
        })
        .WithTags("Notifications")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Notifications)
        .WithSummary("Unsubscribe from push notifications")
        .WithDescription("Removes a push subscription by endpoint. Call on logout or when the user disables notifications.")
        .Produces<object>(200)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
