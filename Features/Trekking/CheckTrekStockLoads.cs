using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class CheckTrekStockLoads
{
    public class StockLoadLineItem
    {
        public Guid ProductId { get; set; }
        public decimal BasicQty { get; set; }
        public decimal PackagingQty { get; set; }
    }

    public class Query : IRequest<Result<CheckResult>>
    {
        public Guid TrekId { get; set; }
        public List<StockLoadLineItem> Items { get; set; } = [];
    }

    public class StockWarning
    {
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public decimal RequestedQty { get; set; }
        public decimal AvailableQty { get; set; }
        public decimal Shortfall { get; set; }
    }

    public class CheckResult
    {
        public bool HasWarnings { get; set; }
        public List<StockWarning> Warnings { get; set; } = [];
    }

    internal sealed class Handler(AppDbContext db) : IRequestHandler<Query, Result<CheckResult>>
    {
        public async Task<Result<CheckResult>> Handle(Query request, CancellationToken cancellationToken)
        {
            var trip = await db.TrekkingTrips.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == request.TrekId, cancellationToken);

            if (trip is null)
                return Result.Failure<CheckResult>(Error.CreateNotFoundError("Trek not found."));

            var productIds = request.Items.Select(i => i.ProductId).Distinct().ToList();

            var vehicleStock = await db.VehicleProductStocks
                .Include(s => s.Product)
                .Where(s => s.VehicleId == trip.VehicleId && productIds.Contains(s.ProductId))
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var warnings = new List<StockWarning>();

            foreach (var item in request.Items)
            {
                var stock = vehicleStock.FirstOrDefault(s => s.ProductId == item.ProductId);
                if (stock is null) continue;

                if (item.BasicQty > stock.BasicQuantityOnHand)
                {
                    warnings.Add(new StockWarning
                    {
                        ProductId = item.ProductId,
                        ProductName = stock.Product.Name,
                        RequestedQty = item.BasicQty,
                        AvailableQty = stock.BasicQuantityOnHand,
                        Shortfall = item.BasicQty - stock.BasicQuantityOnHand
                    });
                }
            }

            return Result.Success(new CheckResult
            {
                HasWarnings = warnings.Count > 0,
                Warnings = warnings
            });
        }
    }
}

public class CheckTrekStockLoadsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/treks/{trekId:guid}/stock-loads/check",
            async (Guid trekId, ISender sender,
                [Microsoft.AspNetCore.Mvc.FromBody] List<CheckTrekStockLoads.StockLoadLineItem> items) =>
            {
                var result = await sender.Send(new CheckTrekStockLoads.Query { TrekId = trekId, Items = items });
                return result.IsFailure ? Results.NotFound(result.Error) : Results.Ok(result.Value);
            })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Check if trek stock load exceeds vehicle warehouse stock")
        .WithDescription("Dry-run — no changes made. Returns warnings for any product where the requested quantity exceeds what is on the vehicle. Frontend should call this before confirming the load.")
        .Produces<CheckTrekStockLoads.CheckResult>(200)
        .Produces<Error>(404)
        .RequireAuthorization();
    }
}
