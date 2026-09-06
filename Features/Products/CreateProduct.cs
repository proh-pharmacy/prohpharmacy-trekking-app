using Carter;
using FluentValidation;
using MediatR;
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
        public string? Unit { get; set; }
        public string? Description { get; set; }
    }

    public class ProductResponse
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Unit { get; set; }
        public string? Description { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
            RuleFor(x => x.Unit).MaximumLength(50).When(x => x.Unit is not null);
            RuleFor(x => x.Description).MaximumLength(500).When(x => x.Description is not null);
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

            var product = new Product
            {
                Name = request.Name.Trim(),
                Unit = request.Unit?.Trim(),
                Description = request.Description?.Trim(),
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            _db.Products.Add(product);
            await _db.SaveChangesAsync(cancellationToken);

            return Result.Success(ToResponse(product));
        }

        internal static ProductResponse ToResponse(Product p) => new()
        {
            Id = p.Id,
            Name = p.Name,
            Unit = p.Unit,
            Description = p.Description,
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
