using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Trekking.Enums;
using prohpharmacy_trekking_app.Services.Pdf;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class GetTrekStockSnapshot
{
    public class Query : IRequest<Result<Response>>
    {
        public Guid TrekId { get; set; }
    }

    public class Response
    {
        public Guid TrekId { get; set; }
        public string TrekNumber { get; set; } = string.Empty;
        public DateOnly TrekDate { get; set; }
        public string TrekStatus { get; set; } = string.Empty;
        public string? RegionName { get; set; }
        public string? VehicleDisplayName { get; set; }
        public DateTime? CapturedAt { get; set; }
        public List<SnapshotItem> Items { get; set; } = new();
        public SnapshotTotals Totals { get; set; } = new();
    }

    public class SnapshotItem
    {
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string? BasicUnitName { get; set; }
        public string? PackagingUnitName { get; set; }
        public decimal BasicUnitPrice { get; set; }
        public decimal? PackagingUnitPrice { get; set; }
        public decimal BasicQtyAtStart { get; set; }
        public decimal PackagingQtyAtStart { get; set; }
        public decimal BasicQtySold { get; set; }
        public decimal PackagingQtySold { get; set; }
        public decimal BasicQtyRemaining { get; set; }
        public decimal PackagingQtyRemaining { get; set; }
        public decimal RevenueAmount { get; set; }
        public bool HasDiscrepancy { get; set; }
    }

    public class SnapshotTotals
    {
        public int ProductCount { get; set; }
        public decimal TotalRevenue { get; set; }
        public int DiscrepancyCount { get; set; }
    }

    internal sealed class Handler(AppDbContext db) : IRequestHandler<Query, Result<Response>>
    {
        public Task<Result<Response>> Handle(Query request, CancellationToken cancellationToken) =>
            BuildAsync(db, t => t.Id == request.TrekId, cancellationToken);

        internal static async Task<Result<Response>> BuildAsync(
            AppDbContext db,
            System.Linq.Expressions.Expression<Func<Entities.TrekkingTrip, bool>> lookup,
            CancellationToken cancellationToken)
        {
            var trip = await db.TrekkingTrips.AsNoTracking()
                .Where(lookup)
                .Select(t => new
                {
                    t.Id,
                    t.TrekNumber,
                    t.ScheduledDate,
                    t.Status,
                    RegionName = t.Region != null ? t.Region.Name : null,
                    VehicleDisplayName = t.Vehicle != null ? t.Vehicle.DisplayName : null
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (trip is null)
                return Result.Failure<Response>(Error.CreateNotFoundError("Trek not found."));

            if (trip.Status != TrekStatus.Completed)
                return Result.Failure<Response>(Error.BadRequest(
                    "Stock snapshot is only available for completed treks."));

            var rows = await db.TrekkingTripStockSnapshots
                .AsNoTracking()
                .Where(s => s.TrekkingTripId == trip.Id)
                .OrderByDescending(s => s.BasicQtySold > 0 || s.PackagingQtySold > 0)
                .ThenBy(s => s.ProductName)
                .ToListAsync(cancellationToken);

            var items = rows.Select(r => new SnapshotItem
            {
                ProductId = r.ProductId,
                ProductName = r.ProductName,
                BasicUnitName = r.BasicUnitName,
                PackagingUnitName = r.PackagingUnitName,
                BasicUnitPrice = r.BasicUnitPrice,
                PackagingUnitPrice = r.PackagingUnitPrice,
                BasicQtyAtStart = r.BasicQtyAtStart,
                PackagingQtyAtStart = r.PackagingQtyAtStart,
                BasicQtySold = r.BasicQtySold,
                PackagingQtySold = r.PackagingQtySold,
                BasicQtyRemaining = r.BasicQtyRemaining,
                PackagingQtyRemaining = r.PackagingQtyRemaining,
                RevenueAmount = r.RevenueAmount,
                HasDiscrepancy = r.BasicQtyRemaining < 0 || r.PackagingQtyRemaining < 0
            }).ToList();

            return Result.Success(new Response
            {
                TrekId = trip.Id,
                TrekNumber = trip.TrekNumber,
                TrekDate = trip.ScheduledDate,
                TrekStatus = trip.Status.ToString(),
                RegionName = trip.RegionName,
                VehicleDisplayName = trip.VehicleDisplayName,
                CapturedAt = rows.Count > 0 ? rows[0].CapturedAt : null,
                Items = items,
                Totals = new SnapshotTotals
                {
                    ProductCount = items.Count,
                    TotalRevenue = items.Sum(i => i.RevenueAmount),
                    DiscrepancyCount = items.Count(i => i.HasDiscrepancy)
                }
            });
        }
    }
}

public class GetTrekStockSnapshotEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/treks/{trekId:guid}/stock-snapshot",
            async (Guid trekId, ISender sender) =>
            {
                var result = await sender.Send(new GetTrekStockSnapshot.Query { TrekId = trekId });
                return MapResult(result);
            })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Get the stock reconciliation snapshot captured when a trek was completed")
        .WithDescription("Immutable per-product reconciliation record frozen at trek completion. Returns 400 if the trek is not Completed. For each product involved in the trek, returns the quantity on the vehicle at trek start, the quantity sold during the trek, the quantity remaining (start − sold), and revenue earned. Rows with negative remaining values indicate a sold-beyond-stock discrepancy.")
        .Produces<GetTrekStockSnapshot.Response>(200)
        .Produces<Error>(400)
        .Produces<Error>(404)
        .RequireAuthorization();

        app.MapGet("api/v1/treks/{trekId:guid}/stock-snapshot/pdf",
            async (Guid trekId, ISender sender) =>
            {
                var result = await sender.Send(new GetTrekStockSnapshot.Query { TrekId = trekId });
                if (result.IsFailure) return MapResult(result);

                var bytes = StockSnapshotPdfGenerator.Generate(result.Value);
                return Results.File(bytes, "application/pdf", $"StockSnapshot-{result.Value.TrekNumber}-{result.Value.TrekDate}.pdf");
            })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Download the trek stock reconciliation snapshot as PDF (admin)")
        .Produces<Error>(400)
        .Produces<Error>(404)
        .RequireAuthorization();
    }

    internal static IResult MapResult(Result<GetTrekStockSnapshot.Response> result)
    {
        if (!result.IsFailure) return Results.Ok(result.Value);
        return result.Error.Code switch
        {
            "404" => Results.NotFound(result.Error),
            "400" => Results.BadRequest(result.Error),
            _ => Results.BadRequest(result.Error)
        };
    }
}
