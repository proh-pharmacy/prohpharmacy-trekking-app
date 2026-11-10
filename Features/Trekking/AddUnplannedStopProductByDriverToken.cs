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

public static class AddUnplannedStopProductByDriverToken
{
    public class Command : IRequest<Result<TrekStopProductResponse>>
    {
        public Guid Token { get; set; }
        public Guid StopId { get; set; }
        public Guid ProductId { get; set; }
        public decimal BasicQtyDelivered { get; set; }
        public decimal? PackagingQtyDelivered { get; set; }
        public PaymentMethod? PaymentMethod { get; set; }
        public decimal? AmtPaid { get; set; }
        public decimal? Balance { get; set; }
        public string? Notes { get; set; }
        public Guid? ClientGeneratedId { get; set; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.ProductId).NotEmpty();
            RuleFor(x => x.BasicQtyDelivered).GreaterThanOrEqualTo(0);
            RuleFor(x => x).Must(x => x.BasicQtyDelivered > 0 || x.PackagingQtyDelivered > 0)
                .WithMessage("At least one delivered quantity must be greater than zero.");
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
            if (request.AmtPaid.HasValue || request.PaymentMethod.HasValue || request.Balance.HasValue)
                return Result.Failure<TrekStopProductResponse>(Error.BadRequest("Add the invoice item first, then record payment using the invoice payments endpoint."));

            var validation = await _validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Result.Failure<TrekStopProductResponse>(Error.ValidationError(validation));

            var trip = await _db.TrekkingTrips
                .Include(t => t.Stops.Where(s => s.Id == request.StopId))
                .FirstOrDefaultAsync(t => t.DriverToken == request.Token, cancellationToken);
            if (trip is null)
                return Result.Failure<TrekStopProductResponse>(Error.CreateNotFoundError("Trek not found. The token may be invalid."));

            var stop = trip.Stops.FirstOrDefault();
            if (stop is null)
                return Result.Failure<TrekStopProductResponse>(Error.CreateNotFoundError("Stop not found on this trek."));

            if (trip.Status is not (TrekStatus.Scheduled or TrekStatus.InProgress))
                return Result.Failure<TrekStopProductResponse>(Error.BadRequest(
                    $"Unplanned delivered products can only be added to a Scheduled or InProgress trek. This trek is {trip.Status}."));

            if (trip.Status == TrekStatus.Scheduled)
            {
                trip.Status = TrekStatus.InProgress;
                trip.UpdatedAt = DateTime.UtcNow;
            }

            if (request.ClientGeneratedId.HasValue)
            {
                var dup = await _db.TrekkingTripStopProducts
                    .Include(p => p.Product).ThenInclude(p => p.BasicUnit)
                    .Include(p => p.Product).ThenInclude(p => p.PackagingUnit)
                    .FirstOrDefaultAsync(p => p.TrekkingTripStopId == request.StopId
                        && p.ProductId == request.ProductId
                        && p.IsUnplanned, cancellationToken);
                if (dup is not null)
                    return Result.Success(MapProduct(dup));
            }

            var product = await _db.Products
                .Include(p => p.BasicUnit)
                .Include(p => p.PackagingUnit)
                .FirstOrDefaultAsync(p => p.Id == request.ProductId, cancellationToken);
            if (product is null)
                return Result.Failure<TrekStopProductResponse>(Error.CreateNotFoundError("Product not found."));

            var hasPackaging = product.PackagingUnitId.HasValue;
            if (!hasPackaging && request.PackagingQtyDelivered > 0)
                return Result.Failure<TrekStopProductResponse>(Error.BadRequest("This product has no packaging unit."));
            var pkgDelivered = hasPackaging ? request.PackagingQtyDelivered : null;
            var pkgPrice = hasPackaging ? product.PackagingUnitPrice : null;
            var amountDue = request.BasicQtyDelivered * product.BasicUnitPrice
                + (pkgDelivered ?? 0) * (pkgPrice ?? 0);

            var stopProduct = new TrekkingTripStopProduct
            {
                TrekkingTripStopId = request.StopId,
                ProductId = request.ProductId,
                PlannedBasicQuantity = 0,
                PlannedPackagingQuantity = null,
                BasicUnitPrice = product.BasicUnitPrice,
                PackagingUnitPrice = pkgPrice,
                BasicQtyDelivered = request.BasicQtyDelivered,
                PackagingQtyDelivered = pkgDelivered,
                AmountDue = amountDue,
                IsUnplanned = true,
                Notes = request.Notes?.Trim(),
                DeliveredAt = DateTime.UtcNow
            };

            _db.TrekkingTripStopProducts.Add(stopProduct);
            await _db.SaveChangesAsync(cancellationToken);

            return Result.Success(MapProduct(stopProduct, product));
        }

        private static TrekStopProductResponse MapProduct(TrekkingTripStopProduct p,
            Features.Products.Entities.Product? prod = null) => new()
        {
            StopProductId = p.Id,
            ProductId = p.ProductId,
            ProductName = (prod ?? p.Product)?.Name ?? string.Empty,
            BasicUnitName = (prod ?? p.Product)?.BasicUnit?.Name,
            PackagingUnitName = (prod ?? p.Product)?.PackagingUnit?.Name,
            BasicUnitPrice = p.BasicUnitPrice,
            PackagingUnitPrice = p.PackagingUnitPrice,
            PlannedBasicQuantity = p.PlannedBasicQuantity,
            PlannedPackagingQuantity = p.PlannedPackagingQuantity,
            BasicQtyDelivered = p.BasicQtyDelivered,
            PackagingQtyDelivered = p.PackagingQtyDelivered,
            PaymentMethod = p.PaymentMethod?.ToString(),
            AmtPaid = p.AmtPaid,
            Balance = p.Balance,
            IsUnplanned = p.IsUnplanned,
            Notes = p.Notes,
            DeliveredAt = p.DeliveredAt
        };
    }
}

public class AddUnplannedStopProductByDriverTokenEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/treks/driver/{token:guid}/stops/{stopId:guid}/products/unplanned",
            async (Guid token, Guid stopId, AddUnplannedStopProductByDriverToken.Command command, ISender sender) =>
            {
                command.Token = token;
                command.StopId = stopId;
                var result = await sender.Send(command);
                return result.IsFailure
                    ? Results.UnprocessableEntity(result.Error)
                    : Results.Created($"api/v1/treks/driver/{token}/stops/{stopId}/products/{result.Value.StopProductId}", result.Value);
            })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Add an unplanned product sale at a stop (driver portal)")
        .Produces<TrekStopProductResponse>(201)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .AllowAnonymous();
    }
}
