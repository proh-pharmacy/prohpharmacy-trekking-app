using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Trekking.Entities;
using prohpharmacy_trekking_app.Features.Trekking.Enums;
using prohpharmacy_trekking_app.Providers;
using prohpharmacy_trekking_app.Shared;
using static prohpharmacy_trekking_app.Features.Trekking.CreateTrek;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class RecordStopReturn
{
    public class Command : IRequest<Result<TrekStopReturnResponse>>
    {
        public Guid TrekId { get; set; }
        public Guid StopId { get; set; }
        public Guid ProductId { get; set; }
        public decimal BasicQtyReturned { get; set; }
        public decimal? PackagingQtyReturned { get; set; }
        public decimal? RefundAmount { get; set; }
        public PaymentMethod? RefundMethod { get; set; }
        public string? Reason { get; set; }
        public Guid? ClientGeneratedId { get; set; }
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
        }
    }

    internal sealed class Handler : IRequestHandler<Command, Result<TrekStopReturnResponse>>
    {
        private readonly AppDbContext _db;
        private readonly IValidator<Command> _validator;
        private readonly AuthProvider _auth;

        public Handler(AppDbContext db, IValidator<Command> validator, AuthProvider auth)
        {
            _db = db;
            _validator = validator;
            _auth = auth;
        }

        public async Task<Result<TrekStopReturnResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var validation = await _validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Result.Failure<TrekStopReturnResponse>(Error.ValidationError(validation));

            var stop = await _db.TrekkingTripStops
                .Include(s => s.TrekkingTrip)
                .FirstOrDefaultAsync(s => s.Id == request.StopId && s.TrekkingTripId == request.TrekId, cancellationToken);
            if (stop is null)
                return Result.Failure<TrekStopReturnResponse>(Error.CreateNotFoundError("Stop not found on this trek."));

            if (stop.TrekkingTrip.Status == TrekStatus.Completed || stop.TrekkingTrip.Status == TrekStatus.Cancelled)
                return Result.Failure<TrekStopReturnResponse>(Error.BadRequest($"Cannot record a return on a {stop.TrekkingTrip.Status} trek."));

            if (request.ClientGeneratedId.HasValue)
            {
                var existing = await _db.TrekkingTripStopReturns
                    .FirstOrDefaultAsync(r => r.ClientGeneratedId == request.ClientGeneratedId, cancellationToken);
                if (existing is not null)
                    return Result.Failure<TrekStopReturnResponse>(Error.Conflict("A return with this client ID already exists."));
            }

            var product = await _db.Products
                .Include(p => p.BasicUnit)
                .Include(p => p.PackagingUnit)
                .FirstOrDefaultAsync(p => p.Id == request.ProductId, cancellationToken);
            if (product is null)
                return Result.Failure<TrekStopReturnResponse>(Error.CreateNotFoundError("Product not found."));

            var userId = _auth.GetUserId();
            Guid? staffId = userId is not null && Guid.TryParse(userId, out var uid) ? uid : null;

            var ret = new TrekkingTripStopReturn
            {
                TrekkingTripStopId = request.StopId,
                ProductId = request.ProductId,
                BasicQtyReturned = request.BasicQtyReturned,
                PackagingQtyReturned = product.PackagingUnitId.HasValue ? request.PackagingQtyReturned : null,
                BasicUnitPrice = product.BasicUnitPrice,
                PackagingUnitPrice = product.PackagingUnitId.HasValue ? product.PackagingUnitPrice : null,
                RefundAmount = request.RefundAmount,
                RefundMethod = request.RefundMethod,
                Reason = request.Reason?.Trim(),
                RecordedByStaffId = staffId,
                ClientGeneratedId = request.ClientGeneratedId,
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
    }
}

public class RecordStopReturnEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/treks/{trekId:guid}/stops/{stopId:guid}/returns",
            async (Guid trekId, Guid stopId, RecordStopReturn.Command command, ISender sender) =>
            {
                command.TrekId = trekId;
                command.StopId = stopId;
                var result = await sender.Send(command);
                return result.IsFailure
                    ? Results.UnprocessableEntity(result.Error)
                    : Results.Created($"api/v1/treks/{trekId}/stops/{stopId}/returns/{result.Value.ReturnId}", result.Value);
            })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Record a product return at a stop (admin)")
        .Produces<CreateTrek.TrekStopReturnResponse>(201)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
