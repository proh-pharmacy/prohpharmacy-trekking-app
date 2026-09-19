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

public static class RecordStopReturnByDriverToken
{
    public class Command : IRequest<Result<TrekStopReturnResponse>>
    {
        public Guid Token { get; set; }
        public Guid StopId { get; set; }
        public Guid ProductId { get; set; }
        public decimal BasicQtyReturned { get; set; }
        public decimal? PackagingQtyReturned { get; set; }
        public decimal? RefundAmount { get; set; }
        public PaymentMethod? RefundMethod { get; set; }
        public string? Reason { get; set; }
        public Guid? ClientGeneratedId { get; set; }
        public GpsInput? Gps { get; set; }

        public class GpsInput
        {
            public decimal Latitude { get; set; }
            public decimal Longitude { get; set; }
            public decimal AccuracyMetres { get; set; }
        }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.ProductId).NotEmpty();
            RuleFor(x => x.BasicQtyReturned).GreaterThan(0);
            RuleFor(x => x.PackagingQtyReturned).GreaterThan(0).When(x => x.PackagingQtyReturned.HasValue);
            RuleFor(x => x.RefundAmount).GreaterThanOrEqualTo(0).When(x => x.RefundAmount.HasValue);
            RuleFor(x => x.Reason).MaximumLength(500).When(x => x.Reason is not null);
            RuleFor(x => x.Gps!.Latitude).InclusiveBetween(-90, 90).When(x => x.Gps is not null);
            RuleFor(x => x.Gps!.Longitude).InclusiveBetween(-180, 180).When(x => x.Gps is not null);
        }
    }

    internal sealed class Handler : IRequestHandler<Command, Result<TrekStopReturnResponse>>
    {
        private readonly AppDbContext _db;
        private readonly IValidator<Command> _validator;

        public Handler(AppDbContext db, IValidator<Command> validator)
        {
            _db = db;
            _validator = validator;
        }

        public async Task<Result<TrekStopReturnResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var validation = await _validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Result.Failure<TrekStopReturnResponse>(Error.ValidationError(validation));

            if (request.ClientGeneratedId.HasValue)
            {
                var existing = await _db.TrekkingTripStopReturns
                    .Include(r => r.Product).ThenInclude(p => p.BasicUnit)
                    .Include(r => r.Product).ThenInclude(p => p.PackagingUnit)
                    .FirstOrDefaultAsync(r => r.ClientGeneratedId == request.ClientGeneratedId, cancellationToken);
                if (existing is not null)
                    return Result.Success(MapReturn(existing));
            }

            var trip = await _db.TrekkingTrips
                .Include(t => t.Stops.Where(s => s.Id == request.StopId))
                .FirstOrDefaultAsync(t => t.DriverToken == request.Token, cancellationToken);
            if (trip is null)
                return Result.Failure<TrekStopReturnResponse>(Error.CreateNotFoundError("Trek not found. The token may be invalid."));

            var stop = trip.Stops.FirstOrDefault();
            if (stop is null)
                return Result.Failure<TrekStopReturnResponse>(Error.CreateNotFoundError("Stop not found on this trek."));

            if (trip.Status == TrekStatus.Completed || trip.Status == TrekStatus.Cancelled)
                return Result.Failure<TrekStopReturnResponse>(Error.BadRequest($"Cannot record a return on a {trip.Status} trek."));

            var product = await _db.Products
                .Include(p => p.BasicUnit)
                .Include(p => p.PackagingUnit)
                .FirstOrDefaultAsync(p => p.Id == request.ProductId, cancellationToken);
            if (product is null)
                return Result.Failure<TrekStopReturnResponse>(Error.CreateNotFoundError("Product not found."));

            var attributedStaffId = trip.SalesStaffId ?? trip.DriverStaffId;

            var basicUnitPrice = product.BasicUnitPrice;
            var packagingUnitPrice = product.PackagingUnitId.HasValue ? product.PackagingUnitPrice : null;
            var packagingQtyReturned = product.PackagingUnitId.HasValue ? request.PackagingQtyReturned : null;

            var refundAmount = request.RefundAmount
                ?? (request.BasicQtyReturned * basicUnitPrice
                    + (packagingQtyReturned ?? 0) * (packagingUnitPrice ?? 0));

            var ret = new TrekkingTripStopReturn
            {
                TrekkingTripStopId = request.StopId,
                ProductId = request.ProductId,
                BasicQtyReturned = request.BasicQtyReturned,
                PackagingQtyReturned = packagingQtyReturned,
                BasicUnitPrice = basicUnitPrice,
                PackagingUnitPrice = packagingUnitPrice,
                RefundAmount = refundAmount,
                RefundMethod = request.RefundMethod,
                Reason = request.Reason?.Trim(),
                RecordedByStaffId = attributedStaffId,
                ClientGeneratedId = request.ClientGeneratedId,
                Latitude = request.Gps?.Latitude,
                Longitude = request.Gps?.Longitude,
                GpsAccuracyMetres = request.Gps?.AccuracyMetres,
                RecordedAt = DateTime.UtcNow
            };

            _db.TrekkingTripStopReturns.Add(ret);
            await _db.SaveChangesAsync(cancellationToken);

            return Result.Success(new TrekStopReturnResponse
            {
                ReturnId = ret.Id,
                ProductId = ret.ProductId,
                ProductName = product.Name,
                BasicUnitName = product.BasicUnit?.Name,
                PackagingUnitName = product.PackagingUnit?.Name,
                BasicQtyReturned = ret.BasicQtyReturned,
                PackagingQtyReturned = ret.PackagingQtyReturned,
                BasicUnitPrice = ret.BasicUnitPrice,
                PackagingUnitPrice = ret.PackagingUnitPrice,
                RefundAmount = ret.RefundAmount,
                RefundMethod = ret.RefundMethod?.ToString(),
                Reason = ret.Reason,
                RecordedAt = ret.RecordedAt
            });
        }

        private static TrekStopReturnResponse MapReturn(TrekkingTripStopReturn r) => new()
        {
            ReturnId = r.Id,
            ProductId = r.ProductId,
            ProductName = r.Product?.Name ?? string.Empty,
            BasicUnitName = r.Product?.BasicUnit?.Name,
            PackagingUnitName = r.Product?.PackagingUnit?.Name,
            BasicQtyReturned = r.BasicQtyReturned,
            PackagingQtyReturned = r.PackagingQtyReturned,
            BasicUnitPrice = r.BasicUnitPrice,
            PackagingUnitPrice = r.PackagingUnitPrice,
            RefundAmount = r.RefundAmount,
            RefundMethod = r.RefundMethod?.ToString(),
            Reason = r.Reason,
            RecordedAt = r.RecordedAt
        };
    }
}

public class RecordStopReturnByDriverTokenEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/treks/driver/{token:guid}/stops/{stopId:guid}/returns",
            async (Guid token, Guid stopId, RecordStopReturnByDriverToken.Command command, ISender sender) =>
            {
                command.Token = token;
                command.StopId = stopId;
                var result = await sender.Send(command);
                return result.IsFailure
                    ? Results.UnprocessableEntity(result.Error)
                    : Results.Created($"api/v1/treks/driver/{token}/stops/{stopId}/returns/{result.Value.ReturnId}", result.Value);
            })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Record a product return at a stop (driver portal)")
        .WithDescription("Idempotent via clientGeneratedId. GPS coordinates are optional.")
        .Produces<TrekStopReturnResponse>(201)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .AllowAnonymous();
    }
}
