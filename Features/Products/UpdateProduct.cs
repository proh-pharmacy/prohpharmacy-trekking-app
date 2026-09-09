using Carter;
using FluentValidation;
using MediatR;
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
        public string? Unit { get; set; }
        public string? Description { get; set; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
            RuleFor(x => x.Unit).MaximumLength(80).When(x => x.Unit is not null);
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

            var product = await _db.Products.FindAsync([request.Id], cancellationToken);
            if (product is null)
                return Result.Failure<ProductResponse>(Error.CreateNotFoundError("Product not found."));

            product.Name = request.Name.Trim();
            product.Unit = request.Unit?.Trim();
            product.Description = request.Description?.Trim();
            product.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(cancellationToken);

            return Result.Success(CreateProduct.Handler.ToResponse(product));
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
