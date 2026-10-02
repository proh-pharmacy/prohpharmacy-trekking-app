using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class GetInvoice
{
    public class Query : IRequest<Result<InvoiceResponse>>
    {
        public string InvoiceNumber { get; set; } = string.Empty;
    }

    public class InvoiceResponse
    {
        public Guid Id { get; set; }
        public string? InvoiceNumber { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime IssuedAt { get; set; }
        public bool CreatedOffline { get; set; }
        public Guid TrekkingTripId { get; set; }
        public string TrekNumber { get; set; } = string.Empty;
        public DateOnly TrekDate { get; set; }
        public string? DriverName { get; set; }
        public string? SalesStaffName { get; set; }
        public string? VehicleDisplayName { get; set; }
        public string? RegionName { get; set; }
        public Guid CustomerAccountId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public string? CustomerCode { get; set; }
        public string? CustomerTradingName { get; set; }
        public string? CustomerPhone { get; set; }
        public string? CustomerWhatsAppNumber { get; set; }
        public string? CustomerRegionName { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal TotalPaid { get; set; }
        public decimal Balance { get; set; }
        public List<InvoiceLineItem> LineItems { get; set; } = [];
    }

    public class InvoiceLineItem
    {
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string BasicUnitName { get; set; } = string.Empty;
        public string? PackagingUnitName { get; set; }
        public decimal? BasicQtyDelivered { get; set; }
        public decimal? PackagingQtyDelivered { get; set; }
        public decimal BasicUnitPrice { get; set; }
        public decimal? PackagingUnitPrice { get; set; }
        public decimal LineTotal { get; set; }
        public decimal? AmtPaid { get; set; }
        public decimal? Balance { get; set; }
        public string? PaymentMethod { get; set; }
        public bool IsUnplanned { get; set; }
        public DateTime? DeliveredAt { get; set; }
    }

    internal sealed class Handler(AppDbContext db) : IRequestHandler<Query, Result<InvoiceResponse>>
    {
        public async Task<Result<InvoiceResponse>> Handle(Query request, CancellationToken cancellationToken)
        {
            var invoice = await db.SaleInvoices
                .Include(i => i.Stop)
                    .ThenInclude(s => s.Products)
                        .ThenInclude(p => p.Product).ThenInclude(p => p.BasicUnit)
                .Include(i => i.Stop)
                    .ThenInclude(s => s.Products)
                        .ThenInclude(p => p.Product).ThenInclude(p => p.PackagingUnit)
                .Include(i => i.Stop)
                    .ThenInclude(s => s.CustomerAccount).ThenInclude(c => c.Region)
                .Include(i => i.Stop)
                    .ThenInclude(s => s.TrekkingTrip).ThenInclude(t => t.Driver)
                .Include(i => i.Stop)
                    .ThenInclude(s => s.TrekkingTrip).ThenInclude(t => t.SalesStaff)
                .Include(i => i.Stop)
                    .ThenInclude(s => s.TrekkingTrip).ThenInclude(t => t.Vehicle)
                .Include(i => i.Stop)
                    .ThenInclude(s => s.TrekkingTrip).ThenInclude(t => t.Region)
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.InvoiceNumber == request.InvoiceNumber, cancellationToken);

            if (invoice is null)
                return Result.Failure<InvoiceResponse>(Error.CreateNotFoundError("Invoice not found."));

            return Result.Success(ToResponse(invoice));
        }

        internal static InvoiceResponse ToResponse(Entities.SaleInvoice invoice)
        {
            var stop = invoice.Stop;
            var trip = stop.TrekkingTrip;
            var customer = stop.CustomerAccount;

            return new InvoiceResponse
            {
                Id = invoice.Id,
                InvoiceNumber = invoice.InvoiceNumber,
                Status = invoice.Status.ToString(),
                IssuedAt = invoice.IssuedAt,
                CreatedOffline = invoice.CreatedOffline,
                TrekkingTripId = invoice.TrekkingTripId,
                TrekNumber = trip.TrekNumber,
                TrekDate = trip.ScheduledDate,
                DriverName = trip.Driver?.FullName,
                SalesStaffName = trip.SalesStaff?.FullName,
                VehicleDisplayName = trip.Vehicle?.DisplayName,
                RegionName = trip.Region?.Name,
                CustomerAccountId = invoice.CustomerAccountId,
                CustomerName = customer.BusinessName,
                CustomerCode = customer.CustomerCode,
                CustomerTradingName = customer.TradingName,
                CustomerPhone = customer.PrimaryPhoneNumber,
                CustomerWhatsAppNumber = customer.WhatsAppNumber,
                CustomerRegionName = customer.Region?.Name,
                TotalAmount = invoice.TotalAmount,
                TotalPaid = invoice.TotalPaid,
                Balance = invoice.Balance,
                LineItems = stop.Products
                    .Where(p => p.DeliveredAt.HasValue || p.BasicQtyDelivered.HasValue)
                    .Select(p => new InvoiceLineItem
                    {
                        ProductId = p.ProductId,
                        ProductName = p.Product.Name,
                        BasicUnitName = p.Product.BasicUnit?.Name ?? string.Empty,
                        PackagingUnitName = p.Product.PackagingUnit?.Name,
                        BasicQtyDelivered = p.BasicQtyDelivered,
                        PackagingQtyDelivered = p.PackagingQtyDelivered,
                        BasicUnitPrice = p.BasicUnitPrice,
                        PackagingUnitPrice = p.PackagingUnitPrice,
                        LineTotal = (p.BasicQtyDelivered ?? 0) * p.BasicUnitPrice
                                  + (p.PackagingQtyDelivered ?? 0) * (p.PackagingUnitPrice ?? 0),
                        AmtPaid = p.AmtPaid,
                        Balance = p.Balance,
                        PaymentMethod = p.PaymentMethod?.ToString(),
                        IsUnplanned = p.IsUnplanned,
                        DeliveredAt = p.DeliveredAt
                    }).ToList()
            };
        }
    }
}

public class GetInvoiceEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/invoices/{invoiceNumber}", async (string invoiceNumber, ISender sender) =>
        {
            var result = await sender.Send(new GetInvoice.Query { InvoiceNumber = invoiceNumber });
            return result.IsFailure
                ? Results.NotFound(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Get a sale invoice by invoice number")
        .Produces<GetInvoice.InvoiceResponse>(200)
        .Produces<Error>(404)
        .RequireAuthorization();
    }
}
