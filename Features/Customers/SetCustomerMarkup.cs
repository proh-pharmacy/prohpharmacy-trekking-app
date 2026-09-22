using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Customers.Entities;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Customers;

public static class SetCustomerMarkup
{
    public class Command : IRequest<Result<CustomerMarkupRuleResponse>>
    {
        public Guid CustomerId { get; set; }
        public Guid? ProductId { get; set; }
        public decimal MarkupPercentage { get; set; }
    }

    public class CustomerMarkupRuleResponse
    {
        public Guid Id { get; set; }
        public Guid CustomerId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public Guid? ProductId { get; set; }
        public string? ProductName { get; set; }
        public decimal MarkupPercentage { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.CustomerId).NotEmpty();
            RuleFor(x => x.MarkupPercentage)
                .InclusiveBetween(-99.99m, 500m)
                .WithMessage("Markup percentage must be between -99.99 and 500.");
        }
    }

    internal sealed class Handler : IRequestHandler<Command, Result<CustomerMarkupRuleResponse>>
    {
        private readonly AppDbContext _db;
        private readonly IValidator<Command> _validator;

        public Handler(AppDbContext db, IValidator<Command> validator)
        {
            _db = db;
            _validator = validator;
        }

        public async Task<Result<CustomerMarkupRuleResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var validation = await _validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Result.Failure<CustomerMarkupRuleResponse>(Error.ValidationError(validation));

            var customer = await _db.CustomerAccounts.FindAsync([request.CustomerId], cancellationToken);
            if (customer is null)
                return Result.Failure<CustomerMarkupRuleResponse>(Error.CreateNotFoundError("Customer not found."));

            string? productName = null;
            if (request.ProductId.HasValue)
            {
                var product = await _db.Products.FindAsync([request.ProductId.Value], cancellationToken);
                if (product is null)
                    return Result.Failure<CustomerMarkupRuleResponse>(Error.CreateNotFoundError("Product not found."));
                productName = product.Name;
            }

            var existing = await _db.CustomerMarkupRules
                .FirstOrDefaultAsync(r =>
                    r.CustomerAccountId == request.CustomerId &&
                    r.ProductId == request.ProductId,
                    cancellationToken);

            CustomerMarkupRule rule;
            if (existing is not null)
            {
                existing.MarkupPercentage = request.MarkupPercentage;
                existing.UpdatedAt = DateTime.UtcNow;
                rule = existing;
            }
            else
            {
                rule = new CustomerMarkupRule
                {
                    CustomerAccountId = request.CustomerId,
                    ProductId = request.ProductId,
                    MarkupPercentage = request.MarkupPercentage,
                    CreatedAt = DateTime.UtcNow
                };
                _db.CustomerMarkupRules.Add(rule);
            }

            await _db.SaveChangesAsync(cancellationToken);

            return Result.Success(new CustomerMarkupRuleResponse
            {
                Id = rule.Id,
                CustomerId = customer.Id,
                CustomerName = customer.BusinessName,
                ProductId = rule.ProductId,
                ProductName = productName,
                MarkupPercentage = rule.MarkupPercentage,
                CreatedAt = rule.CreatedAt,
                UpdatedAt = rule.UpdatedAt
            });
        }
    }
}

public class SetCustomerMarkupEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPut("api/v1/customers/{customerId:guid}/markups",
            async (Guid customerId, SetCustomerMarkup.Command command, ISender sender) =>
            {
                command.CustomerId = customerId;
                var result = await sender.Send(command);
                return result.IsFailure
                    ? Results.UnprocessableEntity(result.Error)
                    : Results.Ok(result.Value);
            })
        .WithTags("Customers")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Customers)
        .WithSummary("Set a markup rule for a customer")
        .WithDescription(
            "Creates or updates a markup rule for the given customer. " +
            "If ProductId is omitted, the rule applies to all products sold to this customer. " +
            "If ProductId is provided, the rule applies only to that product. " +
            "Customer markup takes priority over regional markup during price resolution.")
        .Produces<SetCustomerMarkup.CustomerMarkupRuleResponse>(200)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
