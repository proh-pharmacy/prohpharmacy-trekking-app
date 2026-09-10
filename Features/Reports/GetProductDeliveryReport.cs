using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Trekking.Enums;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Reports;

public static class GetProductDeliveryReport
{
    public class Query : IRequest<Result<ProductDeliveryResponse>>
    {
        public DateOnly? From { get; set; }
        public DateOnly? To { get; set; }
        public Guid? BranchId { get; set; }
        public Guid? ProductId { get; set; }
        public string? Search { get; set; }
        public int? PageNumber { get; set; }
        public int? PageSize { get; set; }
    }

    public class ProductDeliveryResponse
    {
        public int TotalProductLines { get; set; }
        public decimal TotalAmountCollected { get; set; }
        public decimal TotalOutstanding { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
        public int TotalPages { get; set; }
        public List<ProductDeliveryItem> Products { get; set; } = [];
    }

    public class ProductDeliveryItem
    {
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string? Unit { get; set; }
        public decimal TotalQtyDelivered { get; set; }
        public decimal TotalCollected { get; set; }
        public decimal TotalOutstanding { get; set; }
        public int TreksCount { get; set; }
    }

    internal sealed class Handler(AppDbContext db) : IRequestHandler<Query, Result<ProductDeliveryResponse>>
    {
        public async Task<Result<ProductDeliveryResponse>> Handle(Query request, CancellationToken cancellationToken)
        {
            var query = db.TrekkingTripStopProducts
                .Where(p => p.TrekkingTripStop.TrekkingTrip.Status == TrekStatus.Completed
                         && p.QtyDelivered != null && p.QtyDelivered > 0)
                .AsNoTracking();

            if (request.From.HasValue)
                query = query.Where(p => p.TrekkingTripStop.TrekkingTrip.ScheduledDate >= request.From.Value);
            if (request.To.HasValue)
                query = query.Where(p => p.TrekkingTripStop.TrekkingTrip.ScheduledDate <= request.To.Value);
            if (request.BranchId.HasValue)
                query = query.Where(p => p.TrekkingTripStop.TrekkingTrip.BranchId == request.BranchId.Value);
            if (request.ProductId.HasValue)
                query = query.Where(p => p.ProductId == request.ProductId.Value);

            var rows = await query
                .Select(p => new
                {
                    p.ProductId,
                    ProductName = p.Product.Name,
                    ProductUnit = p.Product.Unit,
                    QtyDelivered = p.QtyDelivered ?? 0,
                    AmtPaid = p.AmtPaid ?? 0,
                    Balance = p.Balance ?? 0,
                    TrekId = p.TrekkingTripStop.TrekkingTripId
                })
                .ToListAsync(cancellationToken);

            var grouped = rows
                .GroupBy(r => r.ProductId)
                .Select(g => new ProductDeliveryItem
                {
                    ProductId = g.Key,
                    ProductName = g.First().ProductName,
                    Unit = g.First().ProductUnit,
                    TotalQtyDelivered = g.Sum(r => r.QtyDelivered),
                    TotalCollected = g.Sum(r => r.AmtPaid),
                    TotalOutstanding = g.Sum(r => r.Balance),
                    TreksCount = g.Select(r => r.TrekId).Distinct().Count()
                })
                .OrderByDescending(p => p.TotalCollected)
                .ToList();

            if (!string.IsNullOrWhiteSpace(request.Search))
            {
                var s = request.Search.ToLower();
                grouped = grouped.Where(p => p.ProductName.ToLower().Contains(s)).ToList();
            }

            var totalCollected = grouped.Sum(p => p.TotalCollected);
            var totalOutstanding = grouped.Sum(p => p.TotalOutstanding);
            int pageNumber = Math.Max(1, request.PageNumber ?? 1);
            int pageSize = Math.Clamp(request.PageSize ?? 20, 1, 100);
            int totalCount = grouped.Count;
            int totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

            return Result.Success(new ProductDeliveryResponse
            {
                TotalProductLines = totalCount,
                TotalAmountCollected = totalCollected,
                TotalOutstanding = totalOutstanding,
                Page = pageNumber,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = totalPages,
                Products = grouped.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList()
            });
        }
    }
}

public class GetProductDeliveryReportEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/reports/products", async (
            ISender sender,
            [FromQuery] DateOnly? from,
            [FromQuery] DateOnly? to,
            [FromQuery] Guid? branchId,
            [FromQuery] Guid? productId,
            [FromQuery] string? search,
            [FromQuery] int? pageNumber,
            [FromQuery] int? pageSize) =>
        {
            var result = await sender.Send(new GetProductDeliveryReport.Query
            {
                From = from,
                To = to,
                BranchId = branchId,
                ProductId = productId,
                Search = search,
                PageNumber = pageNumber,
                PageSize = pageSize
            });
            return result.IsFailure ? Results.BadRequest(result.Error) : Results.Ok(result.Value);
        })
        .WithTags("Reports")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Reports)
        .WithSummary("Product delivery report")
        .WithDescription("Returns products delivered across completed treks, grouped by product with total qty delivered, collected, and outstanding. Only includes treks with Status=Completed. Filter by date range (ScheduledDate), branchId, or search by product name.")
        .Produces<GetProductDeliveryReport.ProductDeliveryResponse>(200)
        .RequireAuthorization();
    }
}
