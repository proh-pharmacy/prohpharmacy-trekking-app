using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Services.ImageKit;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Customers;

public static class UploadCustomerIdCardFront
{
    public class Command : IRequest<Result<IdCardFrontResponse>>
    {
        public Guid CustomerId { get; set; }
        public IFormFile File { get; set; } = null!;
    }

    public class IdCardFrontResponse
    {
        public Guid CustomerId { get; set; }
        public string IdCardFrontUrl { get; set; } = string.Empty;
    }

    internal sealed class Handler(AppDbContext db, ImageKitService imageKit)
        : IRequestHandler<Command, Result<IdCardFrontResponse>>
    {
        public async Task<Result<IdCardFrontResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var account = await db.CustomerAccounts
                .FirstOrDefaultAsync(a => a.Id == request.CustomerId, cancellationToken);

            if (account is null)
                return Result.Failure<IdCardFrontResponse>(Error.CreateNotFoundError("Customer not found."));

            var allowedTypes = new[] { "image/jpeg", "image/png", "image/webp" };
            if (!allowedTypes.Contains(request.File.ContentType.ToLower()))
                return Result.Failure<IdCardFrontResponse>(Error.BadRequest("Only JPEG, PNG, and WebP images are accepted."));

            if (request.File.Length > 5 * 1024 * 1024)
                return Result.Failure<IdCardFrontResponse>(Error.BadRequest("ID card photo must be 5 MB or less."));

            var url = await imageKit.UploadAsync(request.File, "customers/id-cards");

            account.IdCardFrontUrl = url;
            account.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            return Result.Success(new IdCardFrontResponse { CustomerId = account.Id, IdCardFrontUrl = url });
        }
    }
}

public class UploadCustomerIdCardFrontEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/customers/{customerId:guid}/id-card/front",
            async (Guid customerId, IFormFile file, ISender sender) =>
            {
                var result = await sender.Send(new UploadCustomerIdCardFront.Command
                {
                    CustomerId = customerId,
                    File = file
                });
                return result.IsFailure
                    ? Results.UnprocessableEntity(result.Error)
                    : Results.Ok(result.Value);
            })
        .WithTags("Customers")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Customers)
        .WithSummary("Upload front of ID document for a customer")
        .Produces<UploadCustomerIdCardFront.IdCardFrontResponse>(200)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .DisableAntiforgery()
        .RequireAuthorization();
    }
}
