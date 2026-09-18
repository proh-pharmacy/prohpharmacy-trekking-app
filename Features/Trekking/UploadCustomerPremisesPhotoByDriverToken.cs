using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Services.ImageKit;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class UploadCustomerPremisesPhotoByDriverToken
{
    public class Command : IRequest<Result<PremisesPhotoResponse>>
    {
        public Guid Token { get; set; }
        public Guid CustomerId { get; set; }
        public IFormFile File { get; set; } = null!;
    }

    public class PremisesPhotoResponse
    {
        public Guid CustomerId { get; set; }
        public string PremisesPhotoUrl { get; set; } = string.Empty;
    }

    internal sealed class Handler(AppDbContext db, ImageKitService imageKit)
        : IRequestHandler<Command, Result<PremisesPhotoResponse>>
    {
        public async Task<Result<PremisesPhotoResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var trip = await db.TrekkingTrips
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.DriverToken == request.Token, cancellationToken);

            if (trip is null)
                return Result.Failure<PremisesPhotoResponse>(Error.CreateNotFoundError("Trek not found. The token may be invalid."));

            var account = await db.CustomerAccounts
                .FirstOrDefaultAsync(
                    a => a.Id == request.CustomerId && a.RegionId == trip.RegionId,
                    cancellationToken);

            if (account is null)
                return Result.Failure<PremisesPhotoResponse>(Error.CreateNotFoundError("Customer not found in this trek's region."));

            var allowedTypes = new[] { "image/jpeg", "image/png", "image/webp" };
            if (!allowedTypes.Contains(request.File.ContentType.ToLower()))
                return Result.Failure<PremisesPhotoResponse>(Error.BadRequest("Only JPEG, PNG, and WebP images are accepted."));

            if (request.File.Length > 5 * 1024 * 1024)
                return Result.Failure<PremisesPhotoResponse>(Error.BadRequest("Premises photo must be 5 MB or less."));

            var url = await imageKit.UploadAsync(request.File, "customers/premises");

            account.PremisesPhotoUrl = url;
            account.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            return Result.Success(new PremisesPhotoResponse { CustomerId = account.Id, PremisesPhotoUrl = url });
        }
    }
}

public class UploadCustomerPremisesPhotoByDriverTokenEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/treks/driver/{token:guid}/customers/{customerId:guid}/premises-photo",
            async (Guid token, Guid customerId, IFormFile file, ISender sender) =>
            {
                var result = await sender.Send(new UploadCustomerPremisesPhotoByDriverToken.Command
                {
                    Token = token,
                    CustomerId = customerId,
                    File = file
                });
                return result.IsFailure
                    ? Results.UnprocessableEntity(result.Error)
                    : Results.Ok(result.Value);
            })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Upload premises photo for a customer (driver portal)")
        .WithDescription("Uploads a photo of the customer's business premises. The customer must belong to the trek's region. Accepts JPEG, PNG, or WebP up to 5 MB. Call this after customer registration — online only (multipart cannot be queued in the offline batch).")
        .Produces<UploadCustomerPremisesPhotoByDriverToken.PremisesPhotoResponse>(200)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .DisableAntiforgery()
        .AllowAnonymous();
    }
}
