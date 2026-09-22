using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Organisation.Entities;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Organisation.Regions;

public static class SetRegionalMarkup
{
    public class Command : IRequest<Result<MarkupRuleResponse>>
    {
        public Guid RegionId { get; set; }
        public Guid? ProductId { get; set; }
        public decimal MarkupPercentage { get; set; }
    }

    public class MarkupRuleResponse
    {
        public Guid Id { get; set; }
        public Guid RegionId { get; set; }
        public string RegionName { get; set; } = string.Empty;
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
            RuleFor(x => x.RegionId).NotEmpty();
            RuleFor(x => x.MarkupPercentage)
                .InclusiveBetween(-99.99m, 500m)
                .WithMessage("Markup percentage must be between -99.99 and 500.");
        }
    }

    internal sealed class Handler : IRequestHandler<Command, Result<MarkupRuleResponse>>
    {
        private readonly AppDbContext _db;
        private readonly IValidator<Command> _validator;

        public Handler(AppDbContext db, IValidator<Command> validator)
        {
            _db = db;
            _validator = validator;
        }

        public async Task<Result<MarkupRuleResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var validation = await _validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Result.Failure<MarkupRuleResponse>(Error.ValidationError(validation));

            var region = await _db.Regions.FindAsync([request.RegionId], cancellationToken);
            if (region is null)
                return Result.Failure<MarkupRuleResponse>(Error.CreateNotFoundError("Region not found."));

            string? productName = null;
            if (request.ProductId.HasValue)
            {
                var product = await _db.Products.FindAsync([request.ProductId.Value], cancellationToken);
                if (product is null)
                    return Result.Failure<MarkupRuleResponse>(Error.CreateNotFoundError("Product not found."));
                productName = product.Name;
            }

            var existing = await _db.RegionalMarkupRules
                .FirstOrDefaultAsync(r =>
                    r.RegionId == request.RegionId &&
                    r.ProductId == request.ProductId,
                    cancellationToken);

            RegionalMarkupRule rule;
            if (existing is not null)
            {
                existing.MarkupPercentage = request.MarkupPercentage;
                existing.UpdatedAt = DateTime.UtcNow;
                rule = existing;
            }
            else
            {
                rule = new RegionalMarkupRule
                {
                    RegionId = request.RegionId,
                    ProductId = request.ProductId,
                    MarkupPercentage = request.MarkupPercentage,
                    CreatedAt = DateTime.UtcNow
                };
                _db.RegionalMarkupRules.Add(rule);
            }

            await _db.SaveChangesAsync(cancellationToken);

            return Result.Success(new MarkupRuleResponse
            {
                Id = rule.Id,
                RegionId = region.Id,
                RegionName = region.Name,
                ProductId = rule.ProductId,
                ProductName = productName,
                MarkupPercentage = rule.MarkupPercentage,
                CreatedAt = rule.CreatedAt,
                UpdatedAt = rule.UpdatedAt
            });
        }
    }
}

public class SetRegionalMarkupEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPut("api/v1/organisation/regions/{regionId:guid}/markups",
            async (Guid regionId, SetRegionalMarkup.Command command, ISender sender) =>
            {
                command.RegionId = regionId;
                var result = await sender.Send(command);
                return result.IsFailure
                    ? Results.UnprocessableEntity(result.Error)
                    : Results.Ok(result.Value);
            })
        .WithTags("Organisation - Regions")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Organisation)
        .WithSummary("Set a markup rule for a region")
        .WithDescription(
            "Creates or updates a markup rule for the given region. " +
            "If ProductId is omitted, the rule applies to all products in the region. " +
            "If ProductId is provided, the rule applies only to that product. " +
            "MarkupPercentage can be positive (price increase) or negative (price reduction).")
        .Produces<SetRegionalMarkup.MarkupRuleResponse>(200)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
