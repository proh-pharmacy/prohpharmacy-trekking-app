using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Fleet;

public static class GetVehicleStock
{
    public class Query : IRequest<Result<List<StockItem>>>
    {
        public Guid VehicleId { get; set; }
    }

    public class StockItem
    {
        public Guid StockId { get; set; }
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public decimal BasicQuantityOnHand { get; set; }
        public decimal PackagingQuantityOnHand { get; set; }
        public decimal? LowStockThreshold { get; set; }
        public bool IsLowStock { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    internal sealed class Handler(AppDbContext db) : IRequestHandler<Query, Result<List<StockItem>>>
    {
        public async Task<Result<List<StockItem>>> Handle(Query request, CancellationToken cancellationToken)
        {
            var vehicle = await db.Vehicles.AsNoTracking()
                .FirstOrDefaultAsync(v => v.Id == request.VehicleId, cancellationToken);

            if (vehicle is null)
                return Result.Failure<List<StockItem>>(Error.CreateNotFoundError("Vehicle not found."));

            var stock = await db.VehicleProductStocks
                .Include(s => s.Product)
                .Where(s => s.VehicleId == request.VehicleId)
                .AsNoTracking()
                .OrderBy(s => s.Product.Name)
                .ToListAsync(cancellationToken);

            var items = stock.Select(s => new StockItem
            {
                StockId = s.Id,
                ProductId = s.ProductId,
                ProductName = s.Product.Name,
                BasicQuantityOnHand = s.BasicQuantityOnHand,
                PackagingQuantityOnHand = s.PackagingQuantityOnHand,
                LowStockThreshold = s.LowStockThreshold,
                IsLowStock = s.LowStockThreshold.HasValue && s.BasicQuantityOnHand <= s.LowStockThreshold.Value,
                UpdatedAt = s.UpdatedAt
            }).ToList();

            return Result.Success(items);
        }
    }
}

public class GetVehicleStockEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/vehicles/{vehicleId:guid}/stock",
            async (Guid vehicleId, ISender sender) =>
            {
                var result = await sender.Send(new GetVehicleStock.Query { VehicleId = vehicleId });
                return result.IsFailure ? Results.NotFound(result.Error) : Results.Ok(result.Value);
            })
        .WithTags("Fleet")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Fleet)
        .WithSummary("Get current warehouse stock for a vehicle")
        .Produces<List<GetVehicleStock.StockItem>>(200)
        .Produces<Error>(404)
        .RequireAuthorization();
    }
}
