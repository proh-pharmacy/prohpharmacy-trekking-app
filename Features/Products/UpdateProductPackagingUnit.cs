using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;
using static prohpharmacy_trekking_app.Features.Products.CreateProduct;

namespace prohpharmacy_trekking_app.Features.Products;

public static class UpdateProductPackagingUnit
{
    public class Command : IRequest<Result<ProductResponse>>
    {
        public Guid Id { get; set; }
        public Guid? PackagingUnitId { get; set; }
        public decimal? PackagingUnitPrice { get; set; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.PackagingUnitPrice)
                .NotNull().GreaterThanOrEqualTo(0)
                .When(x => x.PackagingUnitId.HasValue)
                .WithMessage("Packaging unit price is required when a packaging unit is provided.");
        }
    }

    internal sealed class Handler(AppDbContext db, IValidator<Command> validator)
        : IRequestHandler<Command, Result<ProductResponse>>
    {
        public async Task<Result<ProductResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var validation = await validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Result.Failure<ProductResponse>(Error.ValidationError(validation));

            var product = await db.Products
                .Include(p => p.BasicUnit)
                .Include(p => p.PackagingUnit)
                .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);

            if (product is null)
                return Result.Failure<ProductResponse>(Error.CreateNotFoundError("Product not found."));

            string? packagingUnitName = null;
            if (request.PackagingUnitId.HasValue)
            {
                if (request.PackagingUnitId == product.BasicUnitId)
                    return Result.Failure<ProductResponse>(Error.BadRequest("Packaging unit must be different from the basic unit."));

                var packagingUnit = await db.Units.FindAsync([request.PackagingUnitId.Value], cancellationToken);
                if (packagingUnit is null)
                    return Result.Failure<ProductResponse>(Error.CreateNotFoundError("Packaging unit not found."));
                packagingUnitName = packagingUnit.Name;
            }

            product.PackagingUnitId = request.PackagingUnitId;
            product.PackagingUnitPrice = request.PackagingUnitId.HasValue ? request.PackagingUnitPrice : null;
            product.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(cancellationToken);

            return Result.Success(CreateProduct.Handler.ToResponse(product, product.BasicUnit.Name, packagingUnitName));
        }
    }
}

public class UpdateProductPackagingUnitEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPatch("api/v1/products/{id:guid}/packaging-unit", async (
            Guid id,
            UpdateProductPackagingUnit.Command command,
            ISender sender) =>
        {
            command.Id = id;
            var result = await sender.Send(command);
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Products")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.General)
        .WithSummary("Set or clear a product's packaging unit")
        .WithDescription("Send packagingUnitId + packagingUnitPrice to set the packaging unit. Send both as null to clear it.")
        .Produces<ProductResponse>(200)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
