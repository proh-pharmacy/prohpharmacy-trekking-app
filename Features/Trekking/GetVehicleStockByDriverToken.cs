using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Fleet;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class GetVehicleStockByDriverToken
{
    public class Query : IRequest<Result<List<GetVehicleStock.StockItem>>>
    {
        public Guid Token { get; set; }
    }

    internal sealed class Handler(AppDbContext db)
        : IRequestHandler<Query, Result<List<GetVehicleStock.StockItem>>>
    {
        public async Task<Result<List<GetVehicleStock.StockItem>>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            var trip = await db.TrekkingTrips.AsNoTracking()
                .FirstOrDefaultAsync(t => t.DriverToken == request.Token, cancellationToken);

            if (trip is null)
                return Result.Failure<List<GetVehicleStock.StockItem>>(
                    Error.CreateNotFoundError("Trek not found. The token may be invalid."));

            var stock = await db.VehicleProductStocks
                .Include(s => s.Product)
                    .ThenInclude(p => p.BasicUnit)
                .Include(s => s.Product)
                    .ThenInclude(p => p.PackagingUnit)
                .Where(s => s.VehicleId == trip.VehicleId)
                .AsNoTracking()
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync(cancellationToken);

            return Result.Success(stock.Select(GetVehicleStock.ToStockItem).ToList());
        }
    }
}

public class GetVehicleStockByDriverTokenEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/treks/driver/{token:guid}/vehicle-stock",
            async (Guid token, ISender sender) =>
            {
                var result = await sender.Send(new GetVehicleStockByDriverToken.Query { Token = token });
                return result.IsFailure ? Results.NotFound(result.Error) : Results.Ok(result.Value);
            })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Get the trek vehicle's warehouse stock (driver portal)")
        .WithDescription("Resolves the driver token to its trek, then returns the current VehicleProductStocks for the assigned vehicle — the same shape as the staff route GET /api/v1/vehicles/{vehicleId}/stock. Not the per-trek allocation table.")
        .Produces<List<GetVehicleStock.StockItem>>(200)
        .Produces<Error>(404)
        .AllowAnonymous();
    }
}
