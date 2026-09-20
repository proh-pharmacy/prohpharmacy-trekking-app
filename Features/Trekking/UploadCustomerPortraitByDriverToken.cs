using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Services.ImageKit;
using prohpharmacy_trekking_app.Shared;
using static prohpharmacy_trekking_app.Features.Customers.UploadCustomerPortrait;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class UploadCustomerPortraitByDriverToken
{
    public class Command : IRequest<Result<PortraitResponse>>
    {
        public Guid Token { get; set; }
        public Guid CustomerId { get; set; }
        public Guid PersonId { get; set; }
        public IFormFile File { get; set; } = null!;
    }

    internal sealed class Handler(AppDbContext db, ImageKitService imageKit)
        : IRequestHandler<Command, Result<PortraitResponse>>
    {
        public async Task<Result<PortraitResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var trip = await db.TrekkingTrips
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.DriverToken == request.Token, cancellationToken);

            if (trip is null)
                return Result.Failure<PortraitResponse>(Error.CreateNotFoundError("Trek not found. The token may be invalid."));

            var person = await db.CustomerPersons
                .Include(p => p.CustomerAccount)
                .FirstOrDefaultAsync(
                    p => p.Id == request.PersonId
                      && p.CustomerAccountId == request.CustomerId
                      && p.CustomerAccount!.RegionId == trip.RegionId,
                    cancellationToken);

            if (person is null)
                return Result.Failure<PortraitResponse>(Error.CreateNotFoundError("Customer person not found in this trek's region."));

            var allowedTypes = new[] { "image/jpeg", "image/png", "image/webp" };
            if (!allowedTypes.Contains(request.File.ContentType.ToLower()))
                return Result.Failure<PortraitResponse>(Error.BadRequest("Only JPEG, PNG, and WebP images are accepted."));

            if (request.File.Length > 5 * 1024 * 1024)
                return Result.Failure<PortraitResponse>(Error.BadRequest("Portrait image must be 5 MB or less."));

            var url = await imageKit.UploadAsync(request.File, "customers/portraits");

            person.PortraitUrl = url;
            person.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            return Result.Success(new PortraitResponse { PersonId = person.Id, PortraitUrl = url });
        }
    }
}

public class UploadCustomerPortraitByDriverTokenEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/treks/driver/{token:guid}/customers/{customerId:guid}/people/{personId:guid}/portrait",
            async (HttpContext ctx, Guid token, Guid customerId, Guid personId, IFormFile? file, ISender sender,
                   ILogger<UploadCustomerPortraitByDriverTokenEndpoint> logger) =>
            {
                if (file is null)
                {
                    logger.LogWarning(
                        "Portrait upload rejected — file binding failed. " +
                        "ContentType={ContentType} HasFormContentType={HasForm} FormKeys={FormKeys}",
                        ctx.Request.ContentType,
                        ctx.Request.HasFormContentType,
                        ctx.Request.HasFormContentType
                            ? string.Join(", ", ctx.Request.Form.Files.Select(f => f.Name))
                            : "n/a");
                    return Results.BadRequest(new { error = "No file received. Ensure Content-Type is multipart/form-data (set by the browser automatically) and the field name is 'file'." });
                }

                var result = await sender.Send(new UploadCustomerPortraitByDriverToken.Command
                {
                    Token = token,
                    CustomerId = customerId,
                    PersonId = personId,
                    File = file
                });
                return result.IsFailure
                    ? Results.UnprocessableEntity(result.Error)
                    : Results.Ok(result.Value);
            })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Upload representative portrait for a customer (driver portal)")
        .WithDescription("Uploads a portrait photo for the customer's primary representative. The customer must belong to the trek's region. Accepts JPEG, PNG, or WebP up to 5 MB. personId comes from primaryPerson.id in the customer registration response.")
        .Produces<Customers.UploadCustomerPortrait.PortraitResponse>(200)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .DisableAntiforgery()
        .AllowAnonymous();
    }
}
