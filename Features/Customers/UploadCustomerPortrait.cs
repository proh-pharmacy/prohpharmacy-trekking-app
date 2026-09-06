using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Services.ImageKit;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Customers;

public static class UploadCustomerPortrait
{
    public class Command : IRequest<Result<PortraitResponse>>
    {
        public Guid CustomerId { get; set; }
        public Guid PersonId { get; set; }
        public IFormFile File { get; set; } = null!;
    }

    public class PortraitResponse
    {
        public Guid PersonId { get; set; }
        public string PortraitUrl { get; set; } = string.Empty;
    }

    internal sealed class Handler(AppDbContext db, ImageKitService imageKit)
        : IRequestHandler<Command, Result<PortraitResponse>>
    {
        public async Task<Result<PortraitResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var person = await db.CustomerPersons
                .FirstOrDefaultAsync(
                    p => p.Id == request.PersonId && p.CustomerAccountId == request.CustomerId,
                    cancellationToken);

            if (person is null)
                return Result.Failure<PortraitResponse>(Error.CreateNotFoundError("Customer person not found."));

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

public class UploadCustomerPortraitEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/customers/{customerId:guid}/people/{personId:guid}/portrait",
            async (Guid customerId, Guid personId, IFormFile file, ISender sender) =>
            {
                var result = await sender.Send(new UploadCustomerPortrait.Command
                {
                    CustomerId = customerId,
                    PersonId = personId,
                    File = file
                });

                return result.IsFailure
                    ? Results.UnprocessableEntity(result.Error)
                    : Results.Ok(result.Value);
            })
            .WithTags("Customers")
            .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Customers)
            .WithSummary("Upload customer person portrait")
            .WithDescription("Uploads a portrait photo for a customer person to ImageKit. Accepts JPEG, PNG, or WebP up to 5 MB.")
            .Produces<UploadCustomerPortrait.PortraitResponse>(200)
            .Produces<Error>(404)
            .Produces<Error>(422)
            .DisableAntiforgery()
            .RequireAuthorization();
    }
}
