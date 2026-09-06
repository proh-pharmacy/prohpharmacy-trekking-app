using Carter;
using MediatR;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;
using static prohpharmacy_trekking_app.Features.Products.CreateProduct;

namespace prohpharmacy_trekking_app.Features.Products;

public static class ToggleProductStatus
{
    public class Command : IRequest<Result<ProductResponse>>
    {
        public Guid Id { get; set; }
    }

    internal sealed class Handler : IRequestHandler<Command, Result<ProductResponse>>
    {
        private readonly AppDbContext _db;

        public Handler(AppDbContext db) => _db = db;

        public async Task<Result<ProductResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var product = await _db.Products.FindAsync([request.Id], cancellationToken);
            if (product is null)
                return Result.Failure<ProductResponse>(Error.CreateNotFoundError("Product not found."));

            product.IsActive = !product.IsActive;
            product.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(cancellationToken);

            return Result.Success(CreateProduct.Handler.ToResponse(product));
        }
    }
}

public class ToggleProductStatusEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPatch("api/v1/products/{id:guid}/status", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new ToggleProductStatus.Command { Id = id });
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Products")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.General)
        .WithSummary("Toggle product active / inactive status")
        .Produces<ProductResponse>(200)
        .Produces<Error>(404)
        .RequireAuthorization();
    }
}
