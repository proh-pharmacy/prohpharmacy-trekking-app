using Carter;
using FluentValidation;
using MediatR;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Services.Traccar;
using prohpharmacy_trekking_app.Shared;
using static prohpharmacy_trekking_app.Features.Fleet.TraccarUsers.GetTraccarUsers;

namespace prohpharmacy_trekking_app.Features.Fleet.TraccarUsers;

public static class UpdateTraccarUser
{
    public class Command : IRequest<Result<TraccarUserResponse>>
    {
        public int TraccarUserId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? Password { get; set; }
        public bool Administrator { get; set; }
        public bool Disabled { get; set; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
            RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(200);
            RuleFor(x => x.Password).MinimumLength(6).When(x => x.Password is not null);
        }
    }

    internal sealed class Handler(ITraccarService traccar, IValidator<Command> validator)
        : IRequestHandler<Command, Result<TraccarUserResponse>>
    {
        public async Task<Result<TraccarUserResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var validation = await validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Result.Failure<TraccarUserResponse>(Error.ValidationError(validation));

            var user = await traccar.UpdateUserAsync(
                request.TraccarUserId,
                request.Name.Trim(),
                request.Email.Trim().ToLower(),
                request.Password,
                request.Administrator,
                request.Disabled,
                cancellationToken);

            if (user is null)
                return Result.Failure<TraccarUserResponse>(Error.CreateNotFoundError("Traccar user not found or update failed."));

            return Result.Success(GetTraccarUsers.Handler.ToResponse(user));
        }
    }
}

public class UpdateTraccarUserEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPut("api/v1/fleet/traccar-users/{traccarUserId:int}", async (
            int traccarUserId, UpdateTraccarUser.Command command, ISender sender) =>
        {
            command.TraccarUserId = traccarUserId;
            var result = await sender.Send(command);
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Fleet")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Fleet)
        .WithSummary("Update a Traccar user")
        .WithDescription("Updates a Traccar user's details. Omit `password` to leave it unchanged.")
        .Produces<GetTraccarUsers.TraccarUserResponse>(200)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
