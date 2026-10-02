using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Trekking.Enums;
using prohpharmacy_trekking_app.Shared;
using prohpharmacy_trekking_app.Utilities;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class GetReturnList
{
    public class Query : IRequest<Result<object>>
    {
        public string? ApprovalStatus { get; set; }
        public Guid? RegionId { get; set; }
        public Guid? TrekId { get; set; }
        public Guid? CustomerId { get; set; }
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
        public string? Search { get; set; }
        public string? Sort { get; set; }
        public int? PageNumber { get; set; }
        public int? PageSize { get; set; }
    }

    public class ReturnListItem
    {
        public Guid ReturnId { get; set; }
        public Guid StopId { get; set; }
        public Guid TrekId { get; set; }
        public string TrekNumber { get; set; } = string.Empty;
        public DateOnly TrekDate { get; set; }
        public string TrekStatus { get; set; } = string.Empty;
        public string? RegionName { get; set; }
        public string? DriverName { get; set; }
        public Guid CustomerId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public string? CustomerCode { get; set; }
        public Guid? InvoiceId { get; set; }
        public string? InvoiceNumber { get; set; }
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string? BasicUnitName { get; set; }
        public string? PackagingUnitName { get; set; }
        public decimal BasicQtyReturned { get; set; }
        public decimal? PackagingQtyReturned { get; set; }
        public decimal BasicUnitPrice { get; set; }
        public decimal? PackagingUnitPrice { get; set; }
        public decimal RefundAmount { get; set; }
        public string? RefundMethod { get; set; }
        public string? Reason { get; set; }
        public string ApprovalStatus { get; set; } = string.Empty;
        public string? RejectionReason { get; set; }
        public DateTime RecordedAt { get; set; }
        public DateTime? ApprovedAt { get; set; }
        public string? RecordedByName { get; set; }
        public string? ApprovedByName { get; set; }
    }

    internal sealed class Handler(AppDbContext db) : IRequestHandler<Query, Result<object>>
    {
        public async Task<Result<object>> Handle(Query request, CancellationToken cancellationToken)
        {
            var query = db.TrekkingTripStopReturns
                .Include(r => r.TrekkingTripStop).ThenInclude(s => s.CustomerAccount)
                .Include(r => r.TrekkingTripStop).ThenInclude(s => s.TrekkingTrip).ThenInclude(t => t.Region)
                .Include(r => r.TrekkingTripStop).ThenInclude(s => s.TrekkingTrip).ThenInclude(t => t.Driver)
                .Include(r => r.SaleInvoice)
                .Include(r => r.Product).ThenInclude(p => p.BasicUnit)
                .Include(r => r.Product).ThenInclude(p => p.PackagingUnit)
                .Include(r => r.RecordedBy)
                .Include(r => r.ApprovedBy)
                .Where(r => r.ApprovalStatus != ReturnApprovalStatus.Pending
                         || r.TrekkingTripStop.TrekkingTrip.Status == TrekStatus.Completed)
                .AsNoTracking();

            if (!string.IsNullOrWhiteSpace(request.ApprovalStatus))
            {
                if (!Enum.TryParse<ReturnApprovalStatus>(request.ApprovalStatus, ignoreCase: true, out var parsedStatus))
                    return Result.Failure<object>(Error.BadRequest($"Invalid approvalStatus '{request.ApprovalStatus}'. Expected one of: Pending, Approved, Rejected."));
                query = query.Where(r => r.ApprovalStatus == parsedStatus);
            }

            if (request.RegionId.HasValue)
                query = query.Where(r => r.TrekkingTripStop.TrekkingTrip.RegionId == request.RegionId.Value);

            if (request.TrekId.HasValue)
                query = query.Where(r => r.TrekkingTripStop.TrekkingTripId == request.TrekId.Value);

            if (request.CustomerId.HasValue)
                query = query.Where(r => r.TrekkingTripStop.CustomerAccountId == request.CustomerId.Value);

            if (request.DateFrom.HasValue)
                query = query.Where(r => r.RecordedAt >= request.DateFrom.Value);

            if (request.DateTo.HasValue)
                query = query.Where(r => r.RecordedAt <= request.DateTo.Value);

            if (!string.IsNullOrWhiteSpace(request.Search))
            {
                var s = request.Search.Trim();
                query = query.Where(r =>
                    (r.SaleInvoice.InvoiceNumber != null && EF.Functions.ILike(r.SaleInvoice.InvoiceNumber, $"%{s}%")) ||
                    EF.Functions.ILike(r.TrekkingTripStop.CustomerAccount.BusinessName, $"%{s}%") ||
                    EF.Functions.ILike(r.Product.Name, $"%{s}%"));
            }

            var result = await new QueryBuilder<Entities.TrekkingTripStopReturn>(query)
                .WithSort(request.Sort ?? "recordedAt_desc")
                .Paginate(request.PageNumber, request.PageSize)
                .BuildAsync(r => (object)ToItem(r));

            return Result.Success(result);
        }

        private static ReturnListItem ToItem(Entities.TrekkingTripStopReturn r)
        {
            var stop = r.TrekkingTripStop;
            var trip = stop.TrekkingTrip;
            var customer = stop.CustomerAccount;

            return new ReturnListItem
            {
                ReturnId = r.Id,
                StopId = r.TrekkingTripStopId,
                TrekId = trip.Id,
                TrekNumber = trip.TrekNumber,
                TrekDate = trip.ScheduledDate,
                TrekStatus = trip.Status.ToString(),
                RegionName = trip.Region?.Name,
                DriverName = trip.Driver?.FullName,
                CustomerId = customer.Id,
                CustomerName = customer.BusinessName,
                CustomerCode = customer.CustomerCode,
                InvoiceId = r.SaleInvoiceId,
                InvoiceNumber = r.SaleInvoice?.InvoiceNumber,
                ProductId = r.ProductId,
                ProductName = r.Product.Name,
                BasicUnitName = r.Product.BasicUnit?.Name,
                PackagingUnitName = r.Product.PackagingUnit?.Name,
                BasicQtyReturned = r.BasicQtyReturned,
                PackagingQtyReturned = r.PackagingQtyReturned,
                BasicUnitPrice = r.BasicUnitPrice,
                PackagingUnitPrice = r.PackagingUnitPrice,
                RefundAmount = r.RefundAmount ?? 0,
                RefundMethod = r.RefundMethod?.ToString(),
                Reason = r.Reason,
                ApprovalStatus = r.ApprovalStatus.ToString(),
                RejectionReason = r.RejectionReason,
                RecordedAt = r.RecordedAt,
                ApprovedAt = r.ApprovedAt,
                RecordedByName = r.RecordedBy?.FullName,
                ApprovedByName = r.ApprovedBy?.FullName
            };
        }
    }
}

public class GetReturnListEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/returns", async (
            ISender sender,
            [FromQuery] string? approvalStatus,
            [FromQuery] Guid? regionId,
            [FromQuery] Guid? trekId,
            [FromQuery] Guid? customerId,
            [FromQuery] DateTime? dateFrom,
            [FromQuery] DateTime? dateTo,
            [FromQuery] string? search,
            [FromQuery] string? sort,
            [FromQuery] int? pageNumber,
            [FromQuery] int? pageSize) =>
        {
            var result = await sender.Send(new GetReturnList.Query
            {
                ApprovalStatus = approvalStatus,
                RegionId = regionId,
                TrekId = trekId,
                CustomerId = customerId,
                DateFrom = dateFrom,
                DateTo = dateTo,
                Search = search,
                Sort = sort,
                PageNumber = pageNumber,
                PageSize = pageSize
            });
            return result.IsFailure ? Results.BadRequest(result.Error) : Results.Ok(result.Value);
        })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("List all returns across treks")
        .WithDescription("Cross-trek return list for the approvals inbox. Pending returns are visible only after their parent trek is Completed, matching the per-trek endpoint's rule.")
        .Produces<Paginator.PaginatedData<GetReturnList.ReturnListItem>>(200)
        .RequireAuthorization();
    }
}
