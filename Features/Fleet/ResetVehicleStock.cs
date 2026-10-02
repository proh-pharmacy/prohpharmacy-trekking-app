using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Fleet.Entities;
using prohpharmacy_trekking_app.Features.Fleet.Enums;
using prohpharmacy_trekking_app.Features.Trekking.Enums;
using prohpharmacy_trekking_app.Providers;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Fleet;

public static class ResetVehicleStock
{
    public class Command : IRequest<Result<ResetResponse>>
    {
        public Guid VehicleId { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    public class ResetResponse
    {
        public int ProductsRemoved { get; set; }
        public int LedgerEntriesCreated { get; set; }
        public DateTime ResetAt { get; set; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Reason).NotEmpty().MaximumLength(300)
                .WithMessage("A reason is required for a vehicle stock reset.");
        }
    }

    internal sealed class Handler(AppDbContext db, IValidator<Command> validator, AuthProvider auth)
        : IRequestHandler<Command, Result<ResetResponse>>
    {
        public async Task<Result<ResetResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var validation = await validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Result.Failure<ResetResponse>(Error.ValidationError(validation));

            var vehicle = await db.Vehicles.AsNoTracking()
                .FirstOrDefaultAsync(v => v.Id == request.VehicleId, cancellationToken);
            if (vehicle is null)
                return Result.Failure<ResetResponse>(Error.CreateNotFoundError("Vehicle not found."));

            if (!Guid.TryParse(auth.GetStaffId(), out var staffId))
                return Result.Failure<ResetResponse>(
                    Error.BadRequest("Authenticated staff identity is missing or invalid."));

            var blockingTreks = await db.TrekkingTrips.AsNoTracking()
                .Where(t => t.VehicleId == request.VehicleId
                    && (t.Status == TrekStatus.Scheduled || t.Status == TrekStatus.InProgress))
                .Select(t => new { t.TrekNumber, t.Status })
                .ToListAsync(cancellationToken);

            if (blockingTreks.Count > 0)
            {
                var labels = string.Join(", ", blockingTreks.Select(t => $"{t.TrekNumber} ({t.Status})"));
                return Result.Failure<ResetResponse>(Error.BadRequest(
                    $"Cannot reset stock — vehicle has active trek(s): {labels}. Complete or cancel them first."));
            }

            var stockRecords = await db.VehicleProductStocks
                .Where(s => s.VehicleId == request.VehicleId)
                .ToListAsync(cancellationToken);

            if (stockRecords.Count == 0)
                return Result.Failure<ResetResponse>(Error.BadRequest(
                    "This vehicle has no stock records to reset."));

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var now = DateTime.UtcNow;
                var ledgerEntries = stockRecords.Select(stock => new VehicleStockLedger
                {
                    VehicleId = request.VehicleId,
                    ProductId = stock.ProductId,
                    ChangeType = StockChangeType.Reduction,
                    Source = StockChangeSource.StockReset,
                    BasicQtyChange = stock.BasicQuantityOnHand,
                    PackagingQtyChange = stock.PackagingQuantityOnHand,
                    BasicBalanceAfter = 0,
                    PackagingBalanceAfter = 0,
                    Reason = request.Reason.Trim(),
                    AuthorStaffId = staffId,
                    RecordedAt = now
                }).ToList();

                db.VehicleStockLedger.AddRange(ledgerEntries);
                db.VehicleProductStocks.RemoveRange(stockRecords);

                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return Result.Success(new ResetResponse
                {
                    ProductsRemoved = stockRecords.Count,
                    LedgerEntriesCreated = ledgerEntries.Count,
                    ResetAt = now
                });
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
    }
}

public class ResetVehicleStockEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/vehicles/{vehicleId:guid}/stock/reset",
            async (Guid vehicleId, ResetVehicleStock.Command command, ISender sender) =>
            {
                command.VehicleId = vehicleId;
                var result = await sender.Send(command);
                return result.IsFailure
                    ? Results.UnprocessableEntity(result.Error)
                    : Results.Ok(result.Value);
            })
        .WithTags("Fleet")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Fleet)
        .WithSummary("Reset all warehouse stock for a vehicle")
        .WithDescription("Removes every VehicleProductStock row for the vehicle, after writing a Reduction ledger entry per product capturing the pre-reset quantities. Refuses to run if the vehicle has any Scheduled or InProgress trek. The historical stock ledger is preserved. Supply a non-empty reason.")
        .Produces<ResetVehicleStock.ResetResponse>(200)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
