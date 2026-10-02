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
        public Guid BasicUnitId { get; set; }
        public string BasicUnitName { get; set; } = string.Empty;
        public Guid? PackagingUnitId { get; set; }
        public string? PackagingUnitName { get; set; }
        public decimal RequestedBasicQty { get; set; }
        public decimal AvailableBasicQty { get; set; }
        public decimal BasicShortfall { get; set; }
        public decimal RequestedPackagingQty { get; set; }
        public decimal AvailablePackagingQty { get; set; }
        public decimal PackagingShortfall { get; set; }
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

            return Result.Success(await CheckAsync(db, trip.VehicleId, request.Items, cancellationToken));
        }
    }

    internal static async Task<CheckResult> CheckAsync(
        AppDbContext db,
        Guid vehicleId,
        List<StockLoadLineItem> items,
        CancellationToken cancellationToken)
    {
        var productIds = items.Select(i => i.ProductId).Distinct().ToList();

        var vehicleStock = await db.VehicleProductStocks
            .Include(s => s.Product)
                .ThenInclude(p => p.BasicUnit)
            .Include(s => s.Product)
                .ThenInclude(p => p.PackagingUnit)
            .Where(s => s.VehicleId == vehicleId && productIds.Contains(s.ProductId))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var warnings = new List<StockWarning>();

        foreach (var item in items)
        {
            var stock = vehicleStock.FirstOrDefault(s => s.ProductId == item.ProductId);
            if (stock is null) continue;

            var basicShortfall = Math.Max(0, item.BasicQty - stock.BasicQuantityOnHand);
            var packagingShortfall = Math.Max(0, item.PackagingQty - stock.PackagingQuantityOnHand);
            if (basicShortfall > 0 || packagingShortfall > 0)
            {
                warnings.Add(new StockWarning
                {
                    ProductId = item.ProductId,
                    ProductName = stock.Product.Name,
                    BasicUnitId = stock.Product.BasicUnitId,
                    BasicUnitName = stock.Product.BasicUnit.Name,
                    PackagingUnitId = stock.Product.PackagingUnitId,
                    PackagingUnitName = stock.Product.PackagingUnit?.Name,
                    RequestedBasicQty = item.BasicQty,
                    AvailableBasicQty = stock.BasicQuantityOnHand,
                    BasicShortfall = basicShortfall,
                    RequestedPackagingQty = item.PackagingQty,
                    AvailablePackagingQty = stock.PackagingQuantityOnHand,
                    PackagingShortfall = packagingShortfall
                });
            }
        }

        return new CheckResult
        {
            HasWarnings = warnings.Count > 0,
            Warnings = warnings
        };
    }
}

public class CheckTrekStockLoadsEndpoint : ICarterModule
{
    public sealed class Request
    {
        public List<CheckTrekStockLoads.StockLoadLineItem> Items { get; set; } = [];
    }

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
        .WithDescription("Dry-run — no changes made. Checks basic and packaging quantities and returns their unit names plus any shortfall. Frontend should call this before confirming the load.")
        .Produces<CheckTrekStockLoads.CheckResult>(200)
        .Produces<Error>(404)
        .RequireAuthorization();

        app.MapPost("api/v1/treks/{trekId:guid}/stock-loads/check",
            async (Guid trekId, Request request, ISender sender) =>
            {
                var result = await sender.Send(new CheckTrekStockLoads.Query
                {
                    TrekId = trekId,
                    Items = request.Items
                });
                return result.IsFailure ? Results.NotFound(result.Error) : Results.Ok(result.Value);
            })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Check trek stock availability")
        .WithDescription("Browser-compatible dry run. No changes are made. Checks basic and packaging quantities against the trek vehicle's stock.")
        .Produces<CheckTrekStockLoads.CheckResult>(200)
        .Produces<Error>(404)
        .RequireAuthorization();
    }
}

public static class CheckTrekStockLoadsByDriverToken
{
    public class Command : IRequest<Result<CheckTrekStockLoads.CheckResult>>
    {
        public Guid Token { get; set; }
        public List<CheckTrekStockLoads.StockLoadLineItem> Items { get; set; } = [];
    }

    internal sealed class Handler(AppDbContext db)
        : IRequestHandler<Command, Result<CheckTrekStockLoads.CheckResult>>
    {
        public async Task<Result<CheckTrekStockLoads.CheckResult>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            var trip = await db.TrekkingTrips.AsNoTracking()
                .FirstOrDefaultAsync(t => t.DriverToken == request.Token, cancellationToken);
            if (trip is null)
                return Result.Failure<CheckTrekStockLoads.CheckResult>(
                    Error.CreateNotFoundError("Trek not found. The token may be invalid."));

            var result = await CheckTrekStockLoads.CheckAsync(
                db, trip.VehicleId, request.Items, cancellationToken);
            return Result.Success(result);
        }
    }
}

public class CheckTrekStockLoadsByDriverTokenEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/treks/driver/{token:guid}/stock-loads/check",
            async (Guid token, CheckTrekStockLoadsByDriverToken.Command command, ISender sender) =>
            {
                command.Token = token;
                var result = await sender.Send(command);
                return result.IsFailure ? Results.NotFound(result.Error) : Results.Ok(result.Value);
            })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Check trek stock availability by driver token")
        .WithDescription("Driver-token dry run. No staff JWT is required and no data is changed. Checks basic and packaging quantities against the token trek's vehicle stock.")
        .Produces<CheckTrekStockLoads.CheckResult>(200)
        .Produces<Error>(404)
        .AllowAnonymous();
    }
}
