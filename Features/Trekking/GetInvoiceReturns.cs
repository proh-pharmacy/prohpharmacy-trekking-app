using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class GetInvoiceReturns
{
    public class Query : IRequest<Result<List<GetTrekReturns.ReturnListItem>>>
    {
        public string InvoiceNumber { get; set; } = string.Empty;
    }

    internal sealed class Handler(AppDbContext db) : IRequestHandler<Query, Result<List<GetTrekReturns.ReturnListItem>>>
    {
        public async Task<Result<List<GetTrekReturns.ReturnListItem>>> Handle(Query request, CancellationToken cancellationToken)
        {
            var invoice = await db.SaleInvoices
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.InvoiceNumber == request.InvoiceNumber, cancellationToken);

            if (invoice is null)
                return Result.Failure<List<GetTrekReturns.ReturnListItem>>(Error.CreateNotFoundError("Invoice not found."));

            var returns = await db.TrekkingTripStopReturns
                .Include(r => r.TrekkingTripStop)
                    .ThenInclude(s => s.CustomerAccount)
                .Include(r => r.SaleInvoice)
                .Include(r => r.Product)
                .Where(r => r.SaleInvoiceId == invoice.Id)
                .AsNoTracking()
                .OrderBy(r => r.RecordedAt)
                .ToListAsync(cancellationToken);

            var items = returns.Select(r => new GetTrekReturns.ReturnListItem
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

public class GetInvoiceReturnsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/invoices/{invoiceNumber}/returns",
            async (string invoiceNumber, ISender sender) =>
            {
                var result = await sender.Send(new GetInvoiceReturns.Query { InvoiceNumber = invoiceNumber });
                return result.IsFailure ? Results.NotFound(result.Error) : Results.Ok(result.Value);
            })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("List all returns recorded against a specific invoice")
        .Produces<List<GetTrekReturns.ReturnListItem>>(200)
        .Produces<Error>(404)
        .RequireAuthorization();
    }
}
