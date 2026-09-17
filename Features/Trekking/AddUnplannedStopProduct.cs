using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Trekking.Entities;
using prohpharmacy_trekking_app.Features.Trekking.Enums;
using prohpharmacy_trekking_app.Shared;
using static prohpharmacy_trekking_app.Features.Trekking.CreateTrek;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class AddUnplannedStopProduct
{
    public class Command : IRequest<Result<TrekStopProductResponse>>
    {
        public Guid TrekId { get; set; }
        public Guid StopId { get; set; }
        public Guid ProductId { get; set; }
        public decimal BasicQtyDelivered { get; set; }
        public decimal? PackagingQtyDelivered { get; set; }
        public PaymentMethod? PaymentMethod { get; set; }
        public decimal? AmtPaid { get; set; }
        public decimal? Balance { get; set; }
        public string? Notes { get; set; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.ProductId).NotEmpty();
            RuleFor(x => x.BasicQtyDelivered).GreaterThan(0);
            RuleFor(x => x.PackagingQtyDelivered).GreaterThan(0).When(x => x.PackagingQtyDelivered.HasValue);
            RuleFor(x => x.AmtPaid).GreaterThanOrEqualTo(0).When(x => x.AmtPaid.HasValue);
            RuleFor(x => x.Balance).GreaterThanOrEqualTo(0).When(x => x.Balance.HasValue);
            RuleFor(x => x.Notes).MaximumLength(500).When(x => x.Notes is not null);
        }
    }

    internal sealed class Handler : IRequestHandler<Command, Result<TrekStopProductResponse>>
    {
        private readonly AppDbContext _db;
        private readonly IValidator<Command> _validator;

        public Handler(AppDbContext db, IValidator<Command> validator)
        {
            _db = db;
            _validator = validator;
        }

        public async Task<Result<TrekStopProductResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var validation = await _validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Result.Failure<TrekStopProductResponse>(Error.ValidationError(validation));

            var stop = await _db.TrekkingTripStops
                .Include(s => s.TrekkingTrip)
                .FirstOrDefaultAsync(s => s.Id == request.StopId && s.TrekkingTripId == request.TrekId, cancellationToken);
            if (stop is null)
                return Result.Failure<TrekStopProductResponse>(Error.CreateNotFoundError("Stop not found on this trek."));

            if (stop.TrekkingTrip.Status == TrekStatus.Completed || stop.TrekkingTrip.Status == TrekStatus.Cancelled)
                return Result.Failure<TrekStopProductResponse>(Error.BadRequest($"Cannot add products to a {stop.TrekkingTrip.Status} trek."));

            var product = await _db.Products
                .Include(p => p.BasicUnit)
                .Include(p => p.PackagingUnit)
                .FirstOrDefaultAsync(p => p.Id == request.ProductId, cancellationToken);
            if (product is null)
                return Result.Failure<TrekStopProductResponse>(Error.CreateNotFoundError("Product not found."));

            var hasPackaging = product.PackagingUnitId.HasValue;

            var stopProduct = new TrekkingTripStopProduct
            {
                TrekkingTripStopId = request.StopId,
                ProductId = request.ProductId,
                PlannedBasicQuantity = 0,
                PlannedPackagingQuantity = null,
                BasicUnitPrice = product.BasicUnitPrice,
                PackagingUnitPrice = hasPackaging ? product.PackagingUnitPrice : null,
                BasicQtyDelivered = request.BasicQtyDelivered,
                PackagingQtyDelivered = hasPackaging ? request.PackagingQtyDelivered : null,
                PaymentMethod = request.PaymentMethod,
                AmtPaid = request.AmtPaid,
                Balance = request.Balance,
                IsUnplanned = true,
                Notes = request.Notes?.Trim(),
                DeliveredAt = DateTime.UtcNow
            };

            _db.TrekkingTripStopProducts.Add(stopProduct);
            await _db.SaveChangesAsync(cancellationToken);

            return Result.Success(new TrekStopProductResponse
            {
                StopProductId = stopProduct.Id,
                ProductId = stopProduct.ProductId,
                ProductName = product.Name,
                BasicUnitName = product.BasicUnit?.Name,
                PackagingUnitName = product.PackagingUnit?.Name,
                BasicUnitPrice = stopProduct.BasicUnitPrice,
                PackagingUnitPrice = stopProduct.PackagingUnitPrice,
                PlannedBasicQuantity = stopProduct.PlannedBasicQuantity,
                PlannedPackagingQuantity = stopProduct.PlannedPackagingQuantity,
                BasicQtyDelivered = stopProduct.BasicQtyDelivered,
                PackagingQtyDelivered = stopProduct.PackagingQtyDelivered,
                PaymentMethod = stopProduct.PaymentMethod?.ToString(),
                AmtPaid = stopProduct.AmtPaid,
                Balance = stopProduct.Balance,
                IsUnplanned = true,
                Notes = stopProduct.Notes,
                DeliveredAt = stopProduct.DeliveredAt
            });
        }
    }
}

public class AddUnplannedStopProductEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/treks/{trekId:guid}/stops/{stopId:guid}/products/unplanned",
            async (Guid trekId, Guid stopId, AddUnplannedStopProduct.Command command, ISender sender) =>
            {
                command.TrekId = trekId;
                command.StopId = stopId;
                var result = await sender.Send(command);
                return result.IsFailure
                    ? Results.UnprocessableEntity(result.Error)
                    : Results.Created($"api/v1/treks/{trekId}/stops/{stopId}/products/{result.Value.StopProductId}", result.Value);
            })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Add an unplanned product sale at a stop (admin)")
        .Produces<CreateTrek.TrekStopProductResponse>(201)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
