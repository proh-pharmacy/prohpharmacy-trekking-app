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
        public string? Description { get; set; }
        public Guid BasicUnitId { get; set; }
        public string BasicUnitName { get; set; } = string.Empty;
        public decimal BasicUnitPrice { get; set; }
        public Guid? PackagingUnitId { get; set; }
        public string? PackagingUnitName { get; set; }
        public decimal? PackagingUnitPrice { get; set; }
        public bool IsActive { get; set; }
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
                    .ThenInclude(p => p.BasicUnit)
                .Include(s => s.Product)
                    .ThenInclude(p => p.PackagingUnit)
                .Where(s => s.VehicleId == request.VehicleId)
                .AsNoTracking()
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync(cancellationToken);

            var items = stock.Select(ToStockItem).ToList();

            return Result.Success(items);
        }
    }

    internal static StockItem ToStockItem(Entities.VehicleProductStock stock) => new()
    {
        StockId = stock.Id,
        ProductId = stock.ProductId,
        ProductName = stock.Product.Name,
        Description = stock.Product.Description,
        BasicUnitId = stock.Product.BasicUnitId,
        BasicUnitName = stock.Product.BasicUnit.Name,
        BasicUnitPrice = stock.Product.BasicUnitPrice,
        PackagingUnitId = stock.Product.PackagingUnitId,
        PackagingUnitName = stock.Product.PackagingUnit?.Name,
        PackagingUnitPrice = stock.Product.PackagingUnitPrice,
        IsActive = stock.Product.IsActive,
        BasicQuantityOnHand = stock.BasicQuantityOnHand,
        PackagingQuantityOnHand = stock.PackagingQuantityOnHand,
        LowStockThreshold = stock.LowStockThreshold,
        IsLowStock = stock.LowStockThreshold.HasValue &&
            stock.BasicQuantityOnHand <= stock.LowStockThreshold.Value,
        UpdatedAt = stock.UpdatedAt
    };
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
        .WithDescription("Returns every tracked product ordered by stock-record createdAt descending, with its product metadata, basic and packaging units, prices, active status, and current vehicle quantities.")
        .Produces<List<GetVehicleStock.StockItem>>(200)
        .Produces<Error>(404)
        .RequireAuthorization();
    }
}

public static class GetVehicleStockSummary
{
    public class Query : IRequest<Result<Response>>
    {
        public Guid VehicleId { get; set; }
    }

    public class Response
    {
        public Guid VehicleId { get; set; }
        public string VehicleInfo { get; set; } = string.Empty;
        public int TrackedProductCount { get; set; }
        public int InStockProductCount { get; set; }
        public int OutOfStockProductCount { get; set; }
    }

    internal sealed class Handler(AppDbContext db) : IRequestHandler<Query, Result<Response>>
    {
        public async Task<Result<Response>> Handle(Query request, CancellationToken cancellationToken)
        {
            var vehicle = await db.Vehicles.AsNoTracking()
                .Where(v => v.Id == request.VehicleId)
                .Select(v => new { v.Id, v.DisplayName, RegionName = v.Region.Name })
                .SingleOrDefaultAsync(cancellationToken);
            if (vehicle is null)
                return Result.Failure<Response>(Error.CreateNotFoundError("Vehicle not found."));

            var vehicleInfo = $"{vehicle.RegionName} - {vehicle.DisplayName}";

            var summary = await db.VehicleProductStocks
                .AsNoTracking()
                .Where(s => s.VehicleId == request.VehicleId)
                .GroupBy(_ => 1)
                .Select(group => new Response
                {
                    VehicleId = request.VehicleId,
                    VehicleInfo = vehicleInfo,
                    TrackedProductCount = group.Count(),
                    InStockProductCount = group.Count(s =>
                        s.BasicQuantityOnHand > 0 || s.PackagingQuantityOnHand > 0),
                    OutOfStockProductCount = group.Count(s =>
                        s.BasicQuantityOnHand == 0 && s.PackagingQuantityOnHand == 0)
                })
                .SingleOrDefaultAsync(cancellationToken);

            return Result.Success(summary ?? new Response
            {
                VehicleId = request.VehicleId,
                VehicleInfo = vehicleInfo
            });
        }
    }
}

public class GetVehicleStockSummaryEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/vehicles/{vehicleId:guid}/stock/summary",
            async (Guid vehicleId, ISender sender) =>
            {
                var result = await sender.Send(new GetVehicleStockSummary.Query { VehicleId = vehicleId });
                return result.IsFailure ? Results.NotFound(result.Error) : Results.Ok(result.Value);
            })
        .WithTags("Fleet")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Fleet)
        .WithSummary("Get vehicle warehouse product counts")
        .WithDescription("Returns the vehicle label in 'Region - Display Name' format plus tracked, in-stock and out-of-stock product counts. A product is out of stock only when both its basic and packaging quantities are zero.")
        .Produces<GetVehicleStockSummary.Response>(200)
        .Produces<Error>(404)
        .RequireAuthorization();
    }
}

public static class GetVehicleProductStock
{
    public class Query : IRequest<Result<GetVehicleStock.StockItem>>
    {
        public Guid VehicleId { get; set; }
        public Guid ProductId { get; set; }
    }

    internal sealed class Handler(AppDbContext db) : IRequestHandler<Query, Result<GetVehicleStock.StockItem>>
    {
        public async Task<Result<GetVehicleStock.StockItem>> Handle(Query request, CancellationToken cancellationToken)
        {
            var vehicleExists = await db.Vehicles.AsNoTracking()
                .AnyAsync(v => v.Id == request.VehicleId, cancellationToken);
            if (!vehicleExists)
                return Result.Failure<GetVehicleStock.StockItem>(Error.CreateNotFoundError("Vehicle not found."));

            var stock = await db.VehicleProductStocks
                .Include(s => s.Product)
                    .ThenInclude(p => p.BasicUnit)
                .Include(s => s.Product)
                    .ThenInclude(p => p.PackagingUnit)
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.VehicleId == request.VehicleId && s.ProductId == request.ProductId,
                    cancellationToken);
            if (stock is null)
                return Result.Failure<GetVehicleStock.StockItem>(
                    Error.CreateNotFoundError("Product is not tracked in this vehicle's stock catalogue."));

            return Result.Success(GetVehicleStock.ToStockItem(stock));
        }
    }
}

public class GetVehicleProductStockEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/vehicles/{vehicleId:guid}/stock/{productId:guid}",
            async (Guid vehicleId, Guid productId, ISender sender) =>
            {
                var result = await sender.Send(new GetVehicleProductStock.Query
                {
                    VehicleId = vehicleId,
                    ProductId = productId
                });
                return result.IsFailure ? Results.NotFound(result.Error) : Results.Ok(result.Value);
            })
        .WithTags("Fleet")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Fleet)
        .WithSummary("Get current stock for one product on a vehicle")
        .WithDescription("Returns product metadata, basic and packaging units, prices, active status, and current vehicle quantities for one tracked product.")
        .Produces<GetVehicleStock.StockItem>(200)
        .Produces<Error>(404)
        .RequireAuthorization();
    }
}
