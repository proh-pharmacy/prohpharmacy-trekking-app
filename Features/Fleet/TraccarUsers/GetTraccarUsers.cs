using Carter;
using MediatR;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Services.Traccar;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Fleet.TraccarUsers;

public static class GetTraccarUsers
{
    public class Query : IRequest<Result<List<TraccarUserResponse>>> { }

    public class TraccarUserResponse
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public bool Administrator { get; set; }
        public bool Disabled { get; set; }
        public int DeviceLimit { get; set; }
        public DateTime? ExpirationTime { get; set; }
    }

    internal sealed class Handler(ITraccarService traccar)
        : IRequestHandler<Query, Result<List<TraccarUserResponse>>>
    {
        public async Task<Result<List<TraccarUserResponse>>> Handle(Query request, CancellationToken cancellationToken)
        {
            var users = await traccar.GetAllUsersAsync(cancellationToken);
            return Result.Success(users.Select(ToResponse).ToList());
        }

        internal static TraccarUserResponse ToResponse(TraccarUser u) => new()
        {
            Id = u.Id,
            Name = u.Name,
            Email = u.Email,
            Administrator = u.Administrator,
            Disabled = u.Disabled,
            DeviceLimit = u.DeviceLimit,
            ExpirationTime = u.ExpirationTime
        };
    }
}

public class GetTraccarUsersEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/fleet/traccar-users", async (ISender sender) =>
        {
            var result = await sender.Send(new GetTraccarUsers.Query());
            return result.IsFailure
                ? Results.BadRequest(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Fleet")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Fleet)
        .WithSummary("List all Traccar users")
        .WithDescription("Returns users directly from Traccar — no local copy is stored.")
        .Produces<List<GetTraccarUsers.TraccarUserResponse>>(200)
        .RequireAuthorization();
    }
}
