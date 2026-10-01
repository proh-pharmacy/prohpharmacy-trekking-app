using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;
using prohpharmacy_trekking_app.Utilities;

namespace prohpharmacy_trekking_app.Features.Fleet;

public static class GetVehicleStockLedger
{
    public class Query : IRequest<Result<object>>
    {
        public Guid VehicleId { get; set; }
        public Guid? ProductId { get; set; }
        public string? Source { get; set; }
        public int? PageNumber { get; set; }
        public int? PageSize { get; set; }
    }

    public class LedgerEntry
    {
        public Guid Id { get; set; }
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string ChangeType { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public decimal BasicQtyChange { get; set; }
        public decimal PackagingQtyChange { get; set; }
        public decimal BalanceAfter { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string? AuthorName { get; set; }
        public DateTime RecordedAt { get; set; }
    }

    internal sealed class Handler(AppDbContext db) : IRequestHandler<Query, Result<object>>
    {
        public async Task<Result<object>> Handle(Query request, CancellationToken cancellationToken)
        {
            var vehicle = await db.Vehicles.AsNoTracking()
                .FirstOrDefaultAsync(v => v.Id == request.VehicleId, cancellationToken);

            if (vehicle is null)
                return Result.Failure<object>(Error.CreateNotFoundError("Vehicle not found."));

            var query = db.VehicleStockLedger
                .Include(l => l.Product)
                .Include(l => l.Author)
                .Where(l => l.VehicleId == request.VehicleId)
                .AsNoTracking();

            if (request.ProductId.HasValue)
                query = query.Where(l => l.ProductId == request.ProductId.Value);

            if (!string.IsNullOrWhiteSpace(request.Source))
                query = query.Where(l => l.Source.ToString().ToLower() == request.Source.ToLower());

            var result = await new QueryBuilder<global::prohpharmacy_trekking_app.Features.Fleet.Entities.VehicleStockLedger>(query)
                .WithSort("recordedAt_desc")
                .Paginate(request.PageNumber, request.PageSize)
                .BuildAsync(l => (object)new LedgerEntry
                {
                    Id = l.Id,
                    ProductId = l.ProductId,
                    ProductName = l.Product.Name,
                    ChangeType = l.ChangeType.ToString(),
                    Source = l.Source.ToString(),
                    BasicQtyChange = l.BasicQtyChange,
                    PackagingQtyChange = l.PackagingQtyChange,
                    BalanceAfter = l.BalanceAfter,
                    Reason = l.Reason,
                    AuthorName = l.Author?.FullName,
                    RecordedAt = l.RecordedAt
                });

            return Result.Success(result);
        }
    }
}

public class GetVehicleStockLedgerEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/vehicles/{vehicleId:guid}/stock/ledger",
            async (Guid vehicleId, ISender sender,
                [Microsoft.AspNetCore.Mvc.FromQuery] Guid? productId,
                [Microsoft.AspNetCore.Mvc.FromQuery] string? source,
                [Microsoft.AspNetCore.Mvc.FromQuery] int? pageNumber,
                [Microsoft.AspNetCore.Mvc.FromQuery] int? pageSize) =>
            {
                var result = await sender.Send(new GetVehicleStockLedger.Query
                {
                    VehicleId = vehicleId,
                    ProductId = productId,
                    Source = source,
                    PageNumber = pageNumber,
                    PageSize = pageSize
                });
                return result.IsFailure ? Results.NotFound(result.Error) : Results.Ok(result.Value);
            })
        .WithTags("Fleet")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Fleet)
        .WithSummary("Get stock cycle (ledger) for a vehicle")
        .WithDescription("Returns paginated history of all stock changes. Filter by productId or source (ManualLoad, TrekCompletion, ReturnApproval).")
        .Produces<Paginator.PaginatedData<GetVehicleStockLedger.LedgerEntry>>(200)
        .Produces<Error>(404)
        .RequireAuthorization();
    }
}
