using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;
using static prohpharmacy_trekking_app.Features.Products.CreateProduct;

namespace prohpharmacy_trekking_app.Features.Products;

public static class UpdateProduct
{
    public class Command : IRequest<Result<ProductResponse>>
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public Guid BasicUnitId { get; set; }
        public decimal BasicUnitPrice { get; set; }
        public Guid? PackagingUnitId { get; set; }
        public decimal? PackagingUnitPrice { get; set; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
            RuleFor(x => x.Description).MaximumLength(500).When(x => x.Description is not null);
            RuleFor(x => x.BasicUnitId).NotEmpty();
            RuleFor(x => x.BasicUnitPrice).GreaterThanOrEqualTo(0);
            RuleFor(x => x.PackagingUnitPrice)
                .NotNull().GreaterThanOrEqualTo(0)
                .When(x => x.PackagingUnitId.HasValue)
                .WithMessage("Packaging unit price is required when a packaging unit is provided.");
            RuleFor(x => x.PackagingUnitId)
                .NotEqual(x => x.BasicUnitId)
                .When(x => x.PackagingUnitId.HasValue)
                .WithMessage("Packaging unit must be different from the basic unit.");
        }
    }

    internal sealed class Handler : IRequestHandler<Command, Result<ProductResponse>>
    {
        private readonly AppDbContext _db;
        private readonly IValidator<Command> _validator;

        public Handler(AppDbContext db, IValidator<Command> validator)
        {
            _db = db;
            _validator = validator;
        }

        public async Task<Result<ProductResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var validation = await _validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Result.Failure<ProductResponse>(Error.ValidationError(validation));

            var product = await _db.Products.FindAsync([request.Id], cancellationToken);
            if (product is null)
                return Result.Failure<ProductResponse>(Error.CreateNotFoundError("Product not found."));

            var basicUnit = await _db.Units.FindAsync([request.BasicUnitId], cancellationToken);
            if (basicUnit is null)
                return Result.Failure<ProductResponse>(Error.CreateNotFoundError("Basic unit not found."));

            string? packagingUnitName = null;
            if (request.PackagingUnitId.HasValue)
            {
                var packagingUnit = await _db.Units.FindAsync([request.PackagingUnitId.Value], cancellationToken);
                if (packagingUnit is null)
                    return Result.Failure<ProductResponse>(Error.CreateNotFoundError("Packaging unit not found."));
                packagingUnitName = packagingUnit.Name;
            }

            var nameTaken = await _db.Products
                .AnyAsync(p => p.Name.ToLower() == request.Name.Trim().ToLower() && p.Id != request.Id, cancellationToken);
            if (nameTaken)
                return Result.Failure<ProductResponse>(Error.Conflict("A product with this name already exists."));

            product.Name = request.Name.Trim();
            product.Description = request.Description?.Trim();
            product.BasicUnitId = request.BasicUnitId;
            product.BasicUnitPrice = request.BasicUnitPrice;
            product.PackagingUnitId = request.PackagingUnitId;
            product.PackagingUnitPrice = request.PackagingUnitId.HasValue ? request.PackagingUnitPrice : null;
            product.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(cancellationToken);

            return Result.Success(CreateProduct.Handler.ToResponse(product, basicUnit.Name, packagingUnitName));
        }
    }
}

public class UpdateProductEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPut("api/v1/products/{id:guid}", async (Guid id, UpdateProduct.Command command, ISender sender) =>
        {
            command.Id = id;
            var result = await sender.Send(command);
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Products")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.General)
        .WithSummary("Update a product")
        .Produces<ProductResponse>(200)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
