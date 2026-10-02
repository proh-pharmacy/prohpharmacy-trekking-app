using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Trekking.Enums;
using prohpharmacy_trekking_app.Services.Pdf;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class GetDriverTrekReport
{
    public class Query : IRequest<Result<TrekReportData>>
    {
        public Guid Token { get; set; }
    }

    // ── Report models ──────────────────────────────────────────────────────────

    public class TrekReportData
    {
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
        public string TrekNumber { get; set; } = string.Empty;
        public DateOnly ScheduledDate { get; set; }
        public string Status { get; set; } = string.Empty;
        public string DriverName { get; set; } = string.Empty;
        public string? SalesStaffName { get; set; }
        public string VehicleDisplayName { get; set; } = string.Empty;
        public string RegionName { get; set; } = string.Empty;
        public ReportSummary Summary { get; set; } = new();
        public List<CollectionByMethod> CollectionsByMethod { get; set; } = [];
        public List<StopReport> Stops { get; set; } = [];
        public List<StockSummaryItem> StockSummary { get; set; } = [];
    }

    public class ReportSummary
    {
        public decimal TotalSalesValue { get; set; }
        public decimal TotalCollected { get; set; }
        public decimal TotalOutstanding { get; set; }
        public decimal TotalApprovedRefunds { get; set; }
        public decimal NetCashOnHand { get; set; }
        public int TotalStops { get; set; }
        public int StopsVisited { get; set; }
    }

    public class CollectionByMethod
    {
        public string Method { get; set; } = string.Empty;
        public decimal Amount { get; set; }
    }

    public class StopReport
    {
        public int Sequence { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public Guid? InvoiceId { get; set; }
        public string? InvoiceNumber { get; set; }
        public decimal AmountDue { get; set; }
        public decimal AmtPaid { get; set; }
        public decimal Balance { get; set; }
        public List<string> PaymentMethods { get; set; } = [];
        public List<ReturnReport> Returns { get; set; } = [];
    }

    public class ReturnReport
    {
        public string ProductName { get; set; } = string.Empty;
        public decimal BasicQtyReturned { get; set; }
        public decimal? RefundAmount { get; set; }
        public string? RefundMethod { get; set; }
        public string ApprovalStatus { get; set; } = string.Empty;
    }

    public class StockSummaryItem
    {
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public decimal BasicQtyLoaded { get; set; }
        public decimal BasicQtyDelivered { get; set; }
        public decimal BasicQtyApprovedReturns { get; set; }
        public decimal BasicQtyRemaining { get; set; }
    }

    // ── Handler ────────────────────────────────────────────────────────────────

    internal sealed class Handler(AppDbContext db) : IRequestHandler<Query, Result<TrekReportData>>
    {
        public async Task<Result<TrekReportData>> Handle(Query request, CancellationToken cancellationToken)
        {
            var trip = await db.TrekkingTrips
                .Include(t => t.Region)
                .Include(t => t.Driver)
                .Include(t => t.SalesStaff)
                .Include(t => t.Vehicle)
                .Include(t => t.Stops.OrderBy(s => s.Sequence))
                    .ThenInclude(s => s.CustomerAccount)
                .Include(t => t.Stops)
                    .ThenInclude(s => s.Products)
                        .ThenInclude(p => p.Product)
                .Include(t => t.Stops)
                    .ThenInclude(s => s.Returns)
                        .ThenInclude(r => r.Product)
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.DriverToken == request.Token, cancellationToken);

            if (trip is null)
                return Result.Failure<TrekReportData>(Error.CreateNotFoundError("Trek not found. The token may be invalid."));

            var invoices = await db.SaleInvoices
                .Where(i => i.TrekkingTripId == trip.Id)
                .AsNoTracking()
                .ToDictionaryAsync(i => i.TrekkingTripStopId, cancellationToken);

            var stockLoads = await db.TrekStockLoads
                .Include(l => l.Product)
                .Where(l => l.TrekkingTripId == trip.Id)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            return Result.Success(BuildReport(trip, invoices, stockLoads));
        }

        internal static TrekReportData BuildReport(
            Entities.TrekkingTrip trip,
            Dictionary<Guid, Entities.SaleInvoice> invoices,
            List<Entities.TrekStockLoad> stockLoads)
        {
            var allProducts = trip.Stops.SelectMany(s => s.Products).ToList();
            var deliveredProducts = allProducts
                .Where(p => p.DeliveredAt.HasValue || p.BasicQtyDelivered.HasValue)
                .ToList();
            var allReturns  = trip.Stops.SelectMany(s => s.Returns).ToList();
            var approvedReturns = allReturns.Where(r => r.ApprovalStatus == ReturnApprovalStatus.Approved).ToList();

            var totalSales      = deliveredProducts.Sum(p =>
                (p.BasicQtyDelivered ?? 0) * p.BasicUnitPrice
              + (p.PackagingQtyDelivered ?? 0) * (p.PackagingUnitPrice ?? 0));
            var totalCollected  = allProducts.Sum(p => p.AmtPaid ?? 0);
            var totalOutstanding= allProducts.Sum(p => p.Balance ?? 0);
            var totalRefunds    = approvedReturns.Sum(r => r.RefundAmount ?? 0);

            var cashCollected   = allProducts
                .Where(p => p.PaymentMethod == PaymentMethod.Cash)
                .Sum(p => p.AmtPaid ?? 0);
            var momoCollected   = allProducts
                .Where(p => p.PaymentMethod == PaymentMethod.MobileMoney)
                .Sum(p => p.AmtPaid ?? 0);
            var cashRefunds     = approvedReturns
                .Where(r => r.RefundMethod == PaymentMethod.Cash)
                .Sum(r => r.RefundAmount ?? 0);
            var netCash = cashCollected + momoCollected - cashRefunds;

            var collectionsByMethod = allProducts
                .Where(p => p.PaymentMethod.HasValue && p.AmtPaid > 0)
                .GroupBy(p => p.PaymentMethod!.Value)
                .Select(g => new CollectionByMethod
                {
                    Method = g.Key.ToString(),
                    Amount = g.Sum(p => p.AmtPaid ?? 0)
                })
                .OrderByDescending(c => c.Amount)
                .ToList();

            var stopsVisited = trip.Stops
                .Count(s => s.Products.Any(p => p.BasicQtyDelivered > 0 || p.AmtPaid > 0));

            var stopReports = trip.Stops.OrderBy(s => s.Sequence).Select(s =>
            {
                invoices.TryGetValue(s.Id, out var invoice);
                return new StopReport
                {
                    Sequence     = s.Sequence,
                    CustomerName = s.CustomerAccount?.BusinessName ?? string.Empty,
                    InvoiceId    = invoice?.Id,
                    InvoiceNumber= invoice?.InvoiceNumber,
                    AmountDue    = s.Products
                                    .Where(p => p.DeliveredAt.HasValue || p.BasicQtyDelivered.HasValue)
                                    .Sum(p => (p.BasicQtyDelivered ?? 0) * p.BasicUnitPrice
                                            + (p.PackagingQtyDelivered ?? 0) * (p.PackagingUnitPrice ?? 0)),
                    AmtPaid      = s.Products.Sum(p => p.AmtPaid ?? 0),
                    Balance      = s.Products.Sum(p => p.Balance ?? 0),
                    PaymentMethods = s.Products
                                        .Where(p => p.PaymentMethod.HasValue && (p.AmtPaid ?? 0) > 0)
                                        .Select(p => p.PaymentMethod!.Value.ToString())
                                        .Distinct()
                                        .OrderBy(m => m)
                                        .ToList(),
                    Returns      = s.Returns.Select(r => new ReturnReport
                    {
                        ProductName     = r.Product?.Name ?? string.Empty,
                        BasicQtyReturned= r.BasicQtyReturned,
                        RefundAmount    = r.RefundAmount,
                        RefundMethod    = r.RefundMethod?.ToString(),
                        ApprovalStatus  = r.ApprovalStatus.ToString()
                    }).ToList()
                };
            }).ToList();

            var deliveredByProduct = allProducts
                .Where(p => p.BasicQtyDelivered > 0)
                .GroupBy(p => p.ProductId)
                .ToDictionary(g => g.Key, g => g.Sum(p => p.BasicQtyDelivered ?? 0));

            var approvedReturnsByProduct = approvedReturns
                .GroupBy(r => r.ProductId)
                .ToDictionary(g => g.Key, g => g.Sum(r => r.BasicQtyReturned));

            var stockSummary = stockLoads.Select(l =>
            {
                deliveredByProduct.TryGetValue(l.ProductId, out var delivered);
                approvedReturnsByProduct.TryGetValue(l.ProductId, out var returned);
                var remaining = Math.Max(0, l.BasicQuantityLoaded - delivered + returned);
                return new StockSummaryItem
                {
                    ProductId              = l.ProductId,
                    ProductName            = l.Product?.Name ?? string.Empty,
                    BasicQtyLoaded         = l.BasicQuantityLoaded,
                    BasicQtyDelivered      = delivered,
                    BasicQtyApprovedReturns= returned,
                    BasicQtyRemaining      = remaining
                };
            }).OrderBy(s => s.ProductName).ToList();

            return new TrekReportData
            {
                GeneratedAt        = DateTime.UtcNow,
                TrekNumber         = trip.TrekNumber ?? string.Empty,
                ScheduledDate      = trip.ScheduledDate,
                Status             = trip.Status.ToString(),
                DriverName         = trip.Driver?.FullName ?? string.Empty,
                SalesStaffName     = trip.SalesStaff?.FullName,
                VehicleDisplayName = trip.Vehicle?.DisplayName ?? string.Empty,
                RegionName         = trip.Region?.Name ?? string.Empty,
                Summary = new ReportSummary
                {
                    TotalSalesValue      = totalSales,
                    TotalCollected       = totalCollected,
                    TotalOutstanding     = totalOutstanding,
                    TotalApprovedRefunds = totalRefunds,
                    NetCashOnHand        = netCash,
                    TotalStops           = trip.Stops.Count,
                    StopsVisited         = stopsVisited
                },
                CollectionsByMethod = collectionsByMethod,
                Stops               = stopReports,
                StockSummary        = stockSummary
            };
        }
    }
}

public class GetDriverTrekReportEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/treks/driver/{token:guid}/report",
            async (Guid token, ISender sender) =>
            {
                var result = await sender.Send(new GetDriverTrekReport.Query { Token = token });
                return result.IsFailure ? Results.NotFound(result.Error) : Results.Ok(result.Value);
            })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Get driver trek financial report (driver portal)")
        .WithDescription("Returns a live financial summary for the trek — sales value, collections, outstanding balances, refunds, net cash on hand, and stock reconciliation. Available at any trek status.")
        .Produces<GetDriverTrekReport.TrekReportData>(200)
        .Produces<Error>(404)
        .AllowAnonymous();

        app.MapGet("api/v1/treks/driver/{token:guid}/report/pdf",
            async (Guid token, ISender sender) =>
            {
                var result = await sender.Send(new GetDriverTrekReport.Query { Token = token });
                if (result.IsFailure)
                    return Results.NotFound(result.Error);

                var bytes = DriverReportPdfGenerator.Generate(result.Value);
                return Results.File(bytes, "application/pdf", $"TrekReport-{result.Value.TrekNumber}-{result.Value.ScheduledDate}.pdf");
            })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Download driver trek financial report as PDF (driver portal)")
        .Produces<Error>(404)
        .AllowAnonymous();
    }
}
