using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Fleet.Entities;
using prohpharmacy_trekking_app.Features.Fleet.Enums;
using prohpharmacy_trekking_app.Providers;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Fleet;

public static class RemoveVehicleStock
{
    public class StockRemoveItem
    {
        public Guid ProductId { get; set; }
        public decimal BasicQty { get; set; }
        public decimal PackagingQty { get; set; }
    }

    public class Command : IRequest<Result<RemoveResponse>>
    {
        public Guid VehicleId { get; set; }
        public string Reason { get; set; } = string.Empty;
        public List<StockRemoveItem> Items { get; set; } = [];
    }

    public class RemoveResponse
    {
        public int ProductsUpdated { get; set; }
        public List<GetVehicleStock.StockItem> UpdatedStock { get; set; } = [];
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Reason).NotEmpty().WithMessage("A reason is required for manual stock removal.");
            RuleFor(x => x.Items).NotEmpty().WithMessage("At least one product is required.");
            RuleForEach(x => x.Items).ChildRules(item =>
            {
                item.RuleFor(i => i.ProductId).NotEmpty();
                item.RuleFor(i => i.BasicQty).GreaterThan(0).WithMessage("BasicQty must be greater than 0.");
                item.RuleFor(i => i.PackagingQty).GreaterThanOrEqualTo(0);
            });
        }
    }

    internal sealed class Handler(AppDbContext db, IValidator<Command> validator, AuthProvider auth)
        : IRequestHandler<Command, Result<RemoveResponse>>
    {
        public async Task<Result<RemoveResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var validation = await validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Result.Failure<RemoveResponse>(Error.ValidationError(validation));

            var vehicle = await db.Vehicles.AsNoTracking()
                .FirstOrDefaultAsync(v => v.Id == request.VehicleId, cancellationToken);

            if (vehicle is null)
                return Result.Failure<RemoveResponse>(Error.CreateNotFoundError("Vehicle not found."));

            if (!Guid.TryParse(auth.GetStaffId(), out var staffId))
                return Result.Failure<RemoveResponse>(
                    Error.BadRequest("Authenticated staff identity is missing or invalid."));

            var productIds = request.Items.Select(i => i.ProductId).Distinct().ToList();

            var stockRecords = await db.VehicleProductStocks
                .Where(s => s.VehicleId == request.VehicleId && productIds.Contains(s.ProductId))
                .ToListAsync(cancellationToken);

            var missing = productIds.Except(stockRecords.Select(s => s.ProductId)).ToList();
            if (missing.Count > 0)
                return Result.Failure<RemoveResponse>(
                    Error.BadRequest($"{missing.Count} product(s) have no stock record on this vehicle."));

            var now = DateTime.UtcNow;
            var ledgerEntries = new List<VehicleStockLedger>();

            foreach (var item in request.Items)
            {
                var stock = stockRecords.First(s => s.ProductId == item.ProductId);

                var basicDeducted = Math.Min(item.BasicQty, stock.BasicQuantityOnHand);
                var packagingDeducted = Math.Min(item.PackagingQty, stock.PackagingQuantityOnHand);

                stock.BasicQuantityOnHand = Math.Max(0, stock.BasicQuantityOnHand - item.BasicQty);
                stock.PackagingQuantityOnHand = Math.Max(0, stock.PackagingQuantityOnHand - item.PackagingQty);
                stock.UpdatedAt = now;

                ledgerEntries.Add(new VehicleStockLedger
                {
                    VehicleId = request.VehicleId,
                    ProductId = item.ProductId,
                    ChangeType = StockChangeType.Reduction,
                    Source = StockChangeSource.ManualLoad,
                    BasicQtyChange = basicDeducted,
                    PackagingQtyChange = packagingDeducted,
                    BasicBalanceAfter = stock.BasicQuantityOnHand,
                    PackagingBalanceAfter = stock.PackagingQuantityOnHand,
                    Reason = request.Reason,
                    AuthorStaffId = staffId,
                    RecordedAt = now
                });
            }

            db.VehicleStockLedger.AddRange(ledgerEntries);
            await db.SaveChangesAsync(cancellationToken);

            var updatedStock = await db.VehicleProductStocks
                .Include(s => s.Product)
                    .ThenInclude(p => p.BasicUnit)
                .Include(s => s.Product)
                    .ThenInclude(p => p.PackagingUnit)
                .Where(s => s.VehicleId == request.VehicleId && productIds.Contains(s.ProductId))
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            return Result.Success(new RemoveResponse
            {
                ProductsUpdated = ledgerEntries.Count,
                UpdatedStock = updatedStock.Select(GetVehicleStock.ToStockItem).ToList()
            });
        }
    }
}

public class RemoveVehicleStockEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/vehicles/{vehicleId:guid}/stock/remove",
            async (Guid vehicleId, RemoveVehicleStock.Command command, ISender sender) =>
            {
                command.VehicleId = vehicleId;
                var result = await sender.Send(command);
                return result.IsFailure
                    ? Results.UnprocessableEntity(result.Error)
                    : Results.Ok(result.Value);
            })
        .WithTags("Fleet")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Fleet)
        .WithSummary("Manually remove products from a vehicle's warehouse stock")
        .WithDescription("Accepts an array of { productId, basicQty, packagingQty } and a required reason (e.g. damaged goods, physical count correction). Stock floors at 0. Each removal is written to the ledger with the author and reason.")
        .Produces<RemoveVehicleStock.RemoveResponse>(200)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
