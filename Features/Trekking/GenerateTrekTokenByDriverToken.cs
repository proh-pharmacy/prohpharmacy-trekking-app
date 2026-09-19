using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class GenerateTrekTokenByDriverToken
{
    public class Command : IRequest<Result<GenerateTrekDriverLink.DriverLinkResponse>>
    {
        public Guid Token { get; set; }
        public Guid TrekId { get; set; }
    }

    internal sealed class Handler(AppDbContext db, IConfiguration config)
        : IRequestHandler<Command, Result<GenerateTrekDriverLink.DriverLinkResponse>>
    {
        public async Task<Result<GenerateTrekDriverLink.DriverLinkResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var currentTrip = await db.TrekkingTrips
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.DriverToken == request.Token, cancellationToken);

            if (currentTrip is null)
                return Result.Failure<GenerateTrekDriverLink.DriverLinkResponse>(
                    Error.CreateNotFoundError("Trek not found. The token may be invalid."));

            var targetTrip = await db.TrekkingTrips
                .FirstOrDefaultAsync(t => t.Id == request.TrekId, cancellationToken);

            if (targetTrip is null)
                return Result.Failure<GenerateTrekDriverLink.DriverLinkResponse>(
                    Error.CreateNotFoundError("Target trek not found."));

            var isAssignedDriver = targetTrip.DriverStaffId == currentTrip.DriverStaffId;
            var isSameRegion = targetTrip.RegionId == currentTrip.RegionId;

            if (!isAssignedDriver && !isSameRegion)
                return Result.Failure<GenerateTrekDriverLink.DriverLinkResponse>(
                    Error.BadRequest("You can only generate tokens for treks assigned to you or treks in your region."));

            if (targetTrip.DriverToken is null)
            {
                targetTrip.DriverToken = Guid.NewGuid();
                targetTrip.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
            }

            var frontendUrl = config["SiteSettings:FrontendUrl"]?.TrimEnd('/') ?? string.Empty;
            var url = $"{frontendUrl}/treks/driver?token={targetTrip.DriverToken}";

            return Result.Success(new GenerateTrekDriverLink.DriverLinkResponse
            {
                Token = targetTrip.DriverToken.Value,
                Url = url
            });
        }
    }
}

public class GenerateTrekTokenByDriverTokenEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/treks/driver/{token:guid}/treks/{trekId:guid}/generate-token",
            async (Guid token, Guid trekId, ISender sender) =>
            {
                var result = await sender.Send(new GenerateTrekTokenByDriverToken.Command
                {
                    Token = token,
                    TrekId = trekId
                });
                return result.IsFailure
                    ? Results.UnprocessableEntity(result.Error)
                    : Results.Ok(result.Value);
            })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Generate or retrieve a driver token for another trek (driver portal)")
        .WithDescription("Returns the existing token if one is already set, otherwise generates a new one. The trek must be assigned to this driver or be active in the same region. Use the returned token URL to switch the driver's current working trek.")
        .Produces<GenerateTrekDriverLink.DriverLinkResponse>(200)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .AllowAnonymous();
    }
}
