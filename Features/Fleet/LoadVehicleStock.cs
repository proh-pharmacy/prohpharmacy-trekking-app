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

public static class LoadVehicleStock
{
    public class StockLoadItem
    {
        public Guid ProductId { get; set; }
        public decimal BasicQty { get; set; }
        public decimal PackagingQty { get; set; }
    }

    public class Command : IRequest<Result<LoadResponse>>
    {
        public Guid VehicleId { get; set; }
        public List<StockLoadItem> Items { get; set; } = [];
    }

    public class LoadResponse
    {
        public int ProductsUpdated { get; set; }
        public List<GetVehicleStock.StockItem> UpdatedStock { get; set; } = [];
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
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
        : IRequestHandler<Command, Result<LoadResponse>>
    {
        public async Task<Result<LoadResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var validation = await validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Result.Failure<LoadResponse>(Error.ValidationError(validation));

            var vehicle = await db.Vehicles.AsNoTracking()
                .FirstOrDefaultAsync(v => v.Id == request.VehicleId, cancellationToken);

            if (vehicle is null)
                return Result.Failure<LoadResponse>(Error.CreateNotFoundError("Vehicle not found."));

            if (!Guid.TryParse(auth.GetStaffId(), out var staffId))
                return Result.Failure<LoadResponse>(
                    Error.BadRequest("Authenticated staff identity is missing or invalid."));

            var productIds = request.Items.Select(i => i.ProductId).Distinct().ToList();

            var existing = await db.VehicleProductStocks
                .Where(s => s.VehicleId == request.VehicleId && productIds.Contains(s.ProductId))
                .ToListAsync(cancellationToken);

            var now = DateTime.UtcNow;
            var ledgerEntries = new List<VehicleStockLedger>();

            foreach (var item in request.Items)
            {
                var stock = existing.FirstOrDefault(s => s.ProductId == item.ProductId);

                if (stock is null)
                {
                    stock = new VehicleProductStock
                    {
                        VehicleId = request.VehicleId,
                        ProductId = item.ProductId,
                        BasicQuantityOnHand = item.BasicQty,
                        PackagingQuantityOnHand = item.PackagingQty,
                        CreatedAt = now
                    };
                    db.VehicleProductStocks.Add(stock);
                }
                else
                {
                    stock.BasicQuantityOnHand += item.BasicQty;
                    stock.PackagingQuantityOnHand += item.PackagingQty;
                    stock.UpdatedAt = now;
                }

                ledgerEntries.Add(new VehicleStockLedger
                {
                    VehicleId = request.VehicleId,
                    ProductId = item.ProductId,
                    ChangeType = StockChangeType.Addition,
                    Source = StockChangeSource.ManualLoad,
                    BasicQtyChange = item.BasicQty,
                    PackagingQtyChange = item.PackagingQty,
                    BasicBalanceAfter = stock.BasicQuantityOnHand,
                    PackagingBalanceAfter = stock.PackagingQuantityOnHand,
                    Reason = "Batch stock addition",
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

            return Result.Success(new LoadResponse
            {
                ProductsUpdated = ledgerEntries.Count,
                UpdatedStock = updatedStock.Select(GetVehicleStock.ToStockItem).ToList()
            });
        }
    }
}

public class LoadVehicleStockEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/vehicles/{vehicleId:guid}/stock/load",
            async (Guid vehicleId, LoadVehicleStock.Command command, ISender sender) =>
            {
                command.VehicleId = vehicleId;
                var result = await sender.Send(command);
                return result.IsFailure
                    ? Results.UnprocessableEntity(result.Error)
                    : Results.Ok(result.Value);
            })
        .WithTags("Fleet")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Fleet)
        .WithSummary("Bulk add products to a vehicle's warehouse stock")
        .WithDescription("Accepts an array of { productId, basicQty, packagingQty }. Creates or increments the stock record and writes a ledger entry per product.")
        .Produces<LoadVehicleStock.LoadResponse>(200)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
