using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Products.Entities;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Products;

public static class CreateProduct
{
    public class Command : IRequest<Result<ProductResponse>>
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public Guid BasicUnitId { get; set; }
        public decimal BasicUnitPrice { get; set; }
        public Guid? PackagingUnitId { get; set; }
        public decimal? PackagingUnitPrice { get; set; }
    }

    public class ProductResponse
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public Guid BasicUnitId { get; set; }
        public string BasicUnitName { get; set; } = string.Empty;
        public decimal BasicUnitPrice { get; set; }
        public Guid? PackagingUnitId { get; set; }
        public string? PackagingUnitName { get; set; }
        public decimal? PackagingUnitPrice { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
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
                .AnyAsync(p => p.Name.ToLower() == request.Name.Trim().ToLower(), cancellationToken);
            if (nameTaken)
                return Result.Failure<ProductResponse>(Error.Conflict("A product with this name already exists."));

            var product = new Product
            {
                Name = request.Name.Trim(),
                Description = request.Description?.Trim(),
                BasicUnitId = request.BasicUnitId,
                BasicUnitPrice = request.BasicUnitPrice,
                PackagingUnitId = request.PackagingUnitId,
                PackagingUnitPrice = request.PackagingUnitPrice,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            _db.Products.Add(product);
            await _db.SaveChangesAsync(cancellationToken);

            return Result.Success(ToResponse(product, basicUnit.Name, packagingUnitName));
        }

        internal static ProductResponse ToResponse(Product p, string basicUnitName, string? packagingUnitName) => new()
        {
            Id = p.Id,
            Name = p.Name,
            Description = p.Description,
            BasicUnitId = p.BasicUnitId,
            BasicUnitName = basicUnitName,
            BasicUnitPrice = p.BasicUnitPrice,
            PackagingUnitId = p.PackagingUnitId,
            PackagingUnitName = packagingUnitName,
            PackagingUnitPrice = p.PackagingUnitPrice,
            IsActive = p.IsActive,
            CreatedAt = p.CreatedAt,
            UpdatedAt = p.UpdatedAt
        };
    }
}

public class CreateProductEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/products", async (CreateProduct.Command command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Created($"api/v1/products/{result.Value.Id}", result.Value);
        })
        .WithTags("Products")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.General)
        .WithSummary("Create a new product")
        .Produces<CreateProduct.ProductResponse>(201)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
