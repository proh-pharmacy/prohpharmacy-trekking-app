using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class GenerateTrekDriverLink
{
    public class Command : IRequest<Result<DriverLinkResponse>>
    {
        public Guid TrekId { get; set; }
    }

    public class DriverLinkResponse
    {
        public Guid Token { get; set; }
        public string Url { get; set; } = string.Empty;
    }

    internal sealed class Handler(AppDbContext db, IConfiguration config)
        : IRequestHandler<Command, Result<DriverLinkResponse>>
    {
        public async Task<Result<DriverLinkResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var trip = await db.TrekkingTrips
                .FirstOrDefaultAsync(t => t.Id == request.TrekId, cancellationToken);

            if (trip is null)
                return Result.Failure<DriverLinkResponse>(Error.CreateNotFoundError("Trekking trip not found."));

            if (trip.DriverToken is null)
            {
                trip.DriverToken = Guid.NewGuid();
                trip.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
            }

            var frontendUrl = config["SiteSettings:FrontendUrl"]?.TrimEnd('/') ?? string.Empty;
            var url = $"{frontendUrl}/treks/driver?token={trip.DriverToken}";

            return Result.Success(new DriverLinkResponse
            {
                Token = trip.DriverToken.Value,
                Url = url
            });
        }
    }
}

public class GenerateTrekDriverLinkEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/treks/{id:guid}/generate-link", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new GenerateTrekDriverLink.Command { TrekId = id });
            return result.IsFailure
                ? Results.NotFound(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Generate a shareable driver link for a trek")
        .WithDescription("Generates a permanent token-based URL to share with the driver. The link stays active but becomes read-only once the trek is marked Completed.")
        .Produces<GenerateTrekDriverLink.DriverLinkResponse>(200)
        .Produces<Error>(404)
        .RequireAuthorization();
    }
}
