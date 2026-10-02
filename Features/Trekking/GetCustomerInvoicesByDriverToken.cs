using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class GetCustomerInvoicesByDriverToken
{
    public class Query : IRequest<Result<List<InvoiceResponse>>>
    {
        public Guid Token { get; set; }
        public Guid CustomerId { get; set; }
        public DateOnly? From { get; set; }
        public DateOnly? To { get; set; }
        public string? InvoiceNumber { get; set; }
    }

    public class InvoiceResponse
    {
        public Guid Id { get; set; }
        public string? InvoiceNumber { get; set; }
        public DateTime IssuedAt { get; set; }
        public Guid TrekkingTripId { get; set; }
        public Guid TrekkingTripStopId { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal TotalPaid { get; set; }
        public decimal Balance { get; set; }
        public List<LineItemResponse> LineItems { get; set; } = [];
    }

    public class LineItemResponse
    {
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string? BasicUnitName { get; set; }
        public string? PackagingUnitName { get; set; }
        public decimal? BasicQtyDelivered { get; set; }
        public decimal? PackagingQtyDelivered { get; set; }
        public decimal BasicUnitPrice { get; set; }
        public decimal? PackagingUnitPrice { get; set; }
    }

    internal sealed class Handler(AppDbContext db)
        : IRequestHandler<Query, Result<List<InvoiceResponse>>>
    {
        public async Task<Result<List<InvoiceResponse>>> Handle(Query request, CancellationToken cancellationToken)
        {
            var tripExists = await db.TrekkingTrips.AnyAsync(t => t.DriverToken == request.Token, cancellationToken);
            if (!tripExists)
                return Result.Failure<List<InvoiceResponse>>(Error.CreateNotFoundError("Trek not found. The token may be invalid."));

            var query = db.SaleInvoices
                .Where(i => i.CustomerAccountId == request.CustomerId);

            if (request.From.HasValue)
            {
                var fromUtc = request.From.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
                query = query.Where(i => i.IssuedAt >= fromUtc);
            }

            if (request.To.HasValue)
            {
                var toUtc = request.To.Value.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
                query = query.Where(i => i.IssuedAt <= toUtc);
            }

            if (!string.IsNullOrWhiteSpace(request.InvoiceNumber))
            {
                var term = request.InvoiceNumber.Trim();
                query = query.Where(i => i.InvoiceNumber != null && EF.Functions.ILike(i.InvoiceNumber, $"%{term}%"));
            }

            var invoices = await query
                .Include(i => i.Stop)
                    .ThenInclude(s => s.Products)
                        .ThenInclude(p => p.Product)
                            .ThenInclude(p => p.BasicUnit)
                .Include(i => i.Stop)
                    .ThenInclude(s => s.Products)
                        .ThenInclude(p => p.Product)
                            .ThenInclude(p => p.PackagingUnit)
                .OrderByDescending(i => i.IssuedAt)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var response = invoices.Select(i => new InvoiceResponse
            {
                Id                 = i.Id,
                InvoiceNumber      = i.InvoiceNumber,
                IssuedAt           = i.IssuedAt,
                TrekkingTripId     = i.TrekkingTripId,
                TrekkingTripStopId = i.TrekkingTripStopId,
                TotalAmount        = i.TotalAmount,
                TotalPaid          = i.TotalPaid,
                Balance            = i.Balance,
                LineItems = i.Stop.Products
                    .Where(p => p.BasicQtyDelivered.HasValue || p.PackagingQtyDelivered.HasValue)
                    .Select(p => new LineItemResponse
                    {
                        ProductId             = p.ProductId,
                        ProductName           = p.Product?.Name ?? string.Empty,
                        BasicUnitName         = p.Product?.BasicUnit?.Name,
                        PackagingUnitName     = p.Product?.PackagingUnit?.Name,
                        BasicQtyDelivered     = p.BasicQtyDelivered,
                        PackagingQtyDelivered = p.PackagingQtyDelivered,
                        BasicUnitPrice        = p.BasicUnitPrice,
                        PackagingUnitPrice    = p.PackagingUnitPrice
                    }).ToList()
            }).ToList();

            return Result.Success(response);
        }
    }
}

public class GetCustomerInvoicesByDriverTokenEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/treks/driver/{token:guid}/customers/{customerId:guid}/invoices",
            async (Guid token, Guid customerId, ISender sender,
                [FromQuery] DateOnly? from,
                [FromQuery] DateOnly? to,
                [FromQuery] string? invoiceNumber) =>
            {
                var result = await sender.Send(new GetCustomerInvoicesByDriverToken.Query
                {
                    Token = token,
                    CustomerId = customerId,
                    From = from,
                    To = to,
                    InvoiceNumber = invoiceNumber
                });
                return result.IsFailure ? Results.NotFound(result.Error) : Results.Ok(result.Value);
            })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("List a customer's invoices for returns (driver portal)")
        .WithDescription("Returns the customer's invoice history, newest first, with delivered line items for the invoice picker. Supports `from` / `to` date filters and case-insensitive `invoiceNumber` substring search.")
        .Produces<List<GetCustomerInvoicesByDriverToken.InvoiceResponse>>(200)
        .Produces<Error>(404)
        .AllowAnonymous();
    }
}
