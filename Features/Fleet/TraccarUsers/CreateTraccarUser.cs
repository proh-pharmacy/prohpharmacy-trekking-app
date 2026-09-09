using Carter;
using FluentValidation;
using MediatR;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Services.Traccar;
using prohpharmacy_trekking_app.Shared;
using static prohpharmacy_trekking_app.Features.Fleet.TraccarUsers.GetTraccarUsers;

namespace prohpharmacy_trekking_app.Features.Fleet.TraccarUsers;

public static class CreateTraccarUser
{
    public class Command : IRequest<Result<TraccarUserResponse>>
    {
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public bool Administrator { get; set; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
            RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(200);
            RuleFor(x => x.Password).NotEmpty().MinimumLength(6);
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

            var user = await traccar.CreateUserAsync(
                request.Name.Trim(),
                request.Email.Trim().ToLower(),
                request.Password,
                request.Administrator,
                cancellationToken);

            if (user is null)
                return Result.Failure<TraccarUserResponse>(Error.BadRequest("Failed to create user in Traccar."));

            return Result.Success(GetTraccarUsers.Handler.ToResponse(user));
        }
    }
}

public class CreateTraccarUserEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/fleet/traccar-users", async (CreateTraccarUser.Command command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Created(string.Empty, result.Value);
        })
        .WithTags("Fleet")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Fleet)
        .WithSummary("Create a Traccar user")
        .WithDescription("Creates a user who can log into Traccar directly. Data is stored in Traccar only.")
        .Produces<GetTraccarUsers.TraccarUserResponse>(201)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
