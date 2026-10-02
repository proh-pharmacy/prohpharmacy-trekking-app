using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Trekking.Enums;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class GetTrekReturns
{
    public class Query : IRequest<Result<List<ReturnListItem>>>
    {
        public Guid TrekId { get; set; }
        public string? ApprovalStatus { get; set; }
    }

    public class ReturnListItem
    {
        public Guid ReturnId { get; set; }
        public Guid StopId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public string InvoiceNumber { get; set; } = string.Empty;
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public decimal BasicQtyReturned { get; set; }
        public decimal? PackagingQtyReturned { get; set; }
        public decimal RefundAmount { get; set; }
        public string? RefundMethod { get; set; }
        public string? Reason { get; set; }
        public string ApprovalStatus { get; set; } = string.Empty;
        public string? RejectionReason { get; set; }
        public DateTime RecordedAt { get; set; }
        public DateTime? ApprovedAt { get; set; }
    }

    internal sealed class Handler(AppDbContext db) : IRequestHandler<Query, Result<List<ReturnListItem>>>
    {
        public async Task<Result<List<ReturnListItem>>> Handle(Query request, CancellationToken cancellationToken)
        {
            var query = db.TrekkingTripStopReturns
                .Include(r => r.TrekkingTripStop)
                    .ThenInclude(s => s.CustomerAccount)
                .Include(r => r.TrekkingTripStop)
                    .ThenInclude(s => s.TrekkingTrip)
                .Include(r => r.SaleInvoice)
                .Include(r => r.Product)
                .Where(r => r.TrekkingTripStop.TrekkingTripId == request.TrekId)
                .Where(r => r.ApprovalStatus != ReturnApprovalStatus.Pending
                         || r.TrekkingTripStop.TrekkingTrip.Status == TrekStatus.Completed)
                .AsNoTracking();

            if (!string.IsNullOrWhiteSpace(request.ApprovalStatus))
                query = query.Where(r => r.ApprovalStatus.ToString().ToLower() == request.ApprovalStatus.ToLower());

            var returns = await query.OrderBy(r => r.RecordedAt).ToListAsync(cancellationToken);

            var items = returns.Select(r => new ReturnListItem
            {
                ReturnId = r.Id,
                StopId = r.TrekkingTripStopId,
                CustomerName = r.TrekkingTripStop.CustomerAccount.BusinessName,
                InvoiceNumber = r.SaleInvoice.InvoiceNumber ?? string.Empty,
                ProductId = r.ProductId,
                ProductName = r.Product.Name,
                BasicQtyReturned = r.BasicQtyReturned,
                PackagingQtyReturned = r.PackagingQtyReturned,
                RefundAmount = r.RefundAmount ?? 0,
                RefundMethod = r.RefundMethod?.ToString(),
                Reason = r.Reason,
                ApprovalStatus = r.ApprovalStatus.ToString(),
                RejectionReason = r.RejectionReason,
                RecordedAt = r.RecordedAt,
                ApprovedAt = r.ApprovedAt
            }).ToList();

            return Result.Success(items);
        }
    }
}

public class GetTrekReturnsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/treks/{trekId:guid}/returns",
            async (Guid trekId, [FromQuery] string? approvalStatus, ISender sender) =>
            {
                var result = await sender.Send(new GetTrekReturns.Query
                {
                    TrekId = trekId,
                    ApprovalStatus = approvalStatus
                });
                return result.IsFailure ? Results.BadRequest(result.Error) : Results.Ok(result.Value);
            })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("List all returns recorded during a trek")
        .WithDescription("Optional filter by approvalStatus: Pending, Approved, Rejected.")
        .Produces<List<GetTrekReturns.ReturnListItem>>(200)
        .RequireAuthorization();
    }
}
