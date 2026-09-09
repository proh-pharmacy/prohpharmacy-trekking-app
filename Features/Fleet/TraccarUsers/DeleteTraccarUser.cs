using Carter;
using MediatR;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Services.Traccar;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Fleet.TraccarUsers;

public static class DeleteTraccarUser
{
    public class Command : IRequest<Result<bool>>
    {
        public int TraccarUserId { get; set; }
    }

    internal sealed class Handler(ITraccarService traccar)
        : IRequestHandler<Command, Result<bool>>
    {
        public async Task<Result<bool>> Handle(Command request, CancellationToken cancellationToken)
        {
            var deleted = await traccar.DeleteUserAsync(request.TraccarUserId, cancellationToken);
            return deleted
                ? Result.Success(true)
                : Result.Failure<bool>(Error.CreateNotFoundError("Traccar user not found or deletion failed."));
        }
    }
}

public class DeleteTraccarUserEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/v1/fleet/traccar-users/{traccarUserId:int}", async (
            int traccarUserId, ISender sender) =>
        {
            var result = await sender.Send(new DeleteTraccarUser.Command { TraccarUserId = traccarUserId });
            return result.IsFailure
                ? Results.NotFound(result.Error)
                : Results.NoContent();
        })
        .WithTags("Fleet")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Fleet)
        .WithSummary("Delete a Traccar user")
        .Produces(204)
        .Produces<Error>(404)
        .RequireAuthorization();
    }
}
