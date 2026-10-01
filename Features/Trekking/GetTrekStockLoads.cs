using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class GetTrekStockLoads
{
    public class Query : IRequest<Result<List<StockLoadItem>>>
    {
        public Guid TrekId { get; set; }
    }

    public class StockLoadItem
    {
        public Guid Id { get; set; }
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public decimal BasicQuantityLoaded { get; set; }
        public decimal PackagingQuantityLoaded { get; set; }
        public decimal? VehicleBasicOnHand { get; set; }
        public bool ExceedsVehicleStock { get; set; }
        public string LoadedBy { get; set; } = string.Empty;
        public DateTime LoadedAt { get; set; }
    }

    internal sealed class Handler(AppDbContext db) : IRequestHandler<Query, Result<List<StockLoadItem>>>
    {
        public async Task<Result<List<StockLoadItem>>> Handle(Query request, CancellationToken cancellationToken)
        {
            var trip = await db.TrekkingTrips.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == request.TrekId, cancellationToken);

            if (trip is null)
                return Result.Failure<List<StockLoadItem>>(Error.CreateNotFoundError("Trek not found."));

            var loads = await db.TrekStockLoads
                .Include(l => l.Product)
                .Include(l => l.LoadedBy)
                .Where(l => l.TrekkingTripId == request.TrekId)
                .AsNoTracking()
                .OrderBy(l => l.Product.Name)
                .ToListAsync(cancellationToken);

            var productIds = loads.Select(l => l.ProductId).ToList();

            var vehicleStock = await db.VehicleProductStocks
                .Where(s => s.VehicleId == trip.VehicleId && productIds.Contains(s.ProductId))
                .AsNoTracking()
                .ToDictionaryAsync(s => s.ProductId, s => s.BasicQuantityOnHand, cancellationToken);

            var items = loads.Select(l =>
            {
                vehicleStock.TryGetValue(l.ProductId, out var onHand);
                return new StockLoadItem
                {
                    Id = l.Id,
                    ProductId = l.ProductId,
                    ProductName = l.Product.Name,
                    BasicQuantityLoaded = l.BasicQuantityLoaded,
                    PackagingQuantityLoaded = l.PackagingQuantityLoaded,
                    VehicleBasicOnHand = vehicleStock.ContainsKey(l.ProductId) ? onHand : null,
                    ExceedsVehicleStock = vehicleStock.ContainsKey(l.ProductId) && l.BasicQuantityLoaded > onHand,
                    LoadedBy = l.LoadedBy.FullName,
                    LoadedAt = l.LoadedAt
                };
            }).ToList();

            return Result.Success(items);
        }
    }
}

public class GetTrekStockLoadsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/treks/{trekId:guid}/stock-loads",
            async (Guid trekId, ISender sender) =>
            {
                var result = await sender.Send(new GetTrekStockLoads.Query { TrekId = trekId });
                return result.IsFailure ? Results.NotFound(result.Error) : Results.Ok(result.Value);
            })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Get products loaded for a trek")
        .Produces<List<GetTrekStockLoads.StockLoadItem>>(200)
        .Produces<Error>(404)
        .RequireAuthorization();
    }
}
