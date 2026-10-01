using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Services.ImageKit;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class UploadCustomerIdCardByDriverToken
{
    public class Command : IRequest<Result<IdCardResponse>>
    {
        public Guid Token { get; set; }
        public Guid CustomerId { get; set; }
        public bool IsFront { get; set; }
        public IFormFile? File { get; set; }
    }

    public class IdCardResponse
    {
        public Guid CustomerId { get; set; }
        public string? IdCardFrontUrl { get; set; }
        public string? IdCardBackUrl { get; set; }
    }

    internal sealed class Handler(AppDbContext db, ImageKitService imageKit) : IRequestHandler<Command, Result<IdCardResponse>>
    {
        public async Task<Result<IdCardResponse>> Handle(Command request, CancellationToken ct)
        {
            var trip = await db.TrekkingTrips.AsNoTracking().FirstOrDefaultAsync(t => t.DriverToken == request.Token, ct);
            if (trip is null)
                return Result.Failure<IdCardResponse>(Error.CreateNotFoundError("Trek not found. The token may be invalid."));
            var account = await db.CustomerAccounts.FirstOrDefaultAsync(c => c.Id == request.CustomerId && c.RegionId == trip.RegionId, ct);
            if (account is null)
                return Result.Failure<IdCardResponse>(Error.CreateNotFoundError("Customer not found in this trek's region."));

            if (request.File is null || request.File.Length == 0)
                return Result.Failure<IdCardResponse>(Error.BadRequest("A non-empty file is required in the multipart field 'file'."));
            if (!new[] { "image/jpeg", "image/png", "image/webp" }.Contains(request.File.ContentType, StringComparer.OrdinalIgnoreCase))
                return Result.Failure<IdCardResponse>(Error.BadRequest("Only JPEG, PNG, and WebP images are accepted."));
            if (request.File.Length > 5 * 1024 * 1024)
                return Result.Failure<IdCardResponse>(Error.BadRequest("ID card photo must be 5 MB or less."));

            var url = await imageKit.UploadAsync(request.File, "customers/id-cards");
            if (request.IsFront) account.IdCardFrontUrl = url;
            else account.IdCardBackUrl = url;
            account.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            return Result.Success(new IdCardResponse
            {
                CustomerId = account.Id,
                IdCardFrontUrl = account.IdCardFrontUrl,
                IdCardBackUrl = account.IdCardBackUrl
            });
        }
    }
}

public class UploadCustomerIdCardByDriverTokenEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        foreach (var side in new[] { "front", "back" })
        {
            var isFront = side == "front";
            app.MapPost($"api/v1/treks/driver/{{token:guid}}/customers/{{customerId:guid}}/id-card/{side}",
                async (Guid token, Guid customerId, IFormFile? file, ISender sender) =>
                {
                    var result = await sender.Send(new UploadCustomerIdCardByDriverToken.Command
                    {
                        Token = token, CustomerId = customerId, File = file, IsFront = isFront
                    });
                    return result.IsFailure ? Results.UnprocessableEntity(result.Error) : Results.Ok(result.Value);
                })
                .WithTags("Trekking")
                .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
                .WithSummary($"Upload or replace customer ID card {side} (driver portal)")
                .WithDescription("Customer must belong to the trek region. Upload pending offline files after customer sync resolves the server ID. Multipart field: file; JPEG, PNG or WebP, up to 5 MB.")
                .Produces<UploadCustomerIdCardByDriverToken.IdCardResponse>(200)
                .Produces<Error>(422)
                .DisableAntiforgery()
                .AllowAnonymous();
        }
    }
}
