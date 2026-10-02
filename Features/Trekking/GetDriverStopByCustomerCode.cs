using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class GetDriverStopByCustomerCode
{
    public class Query : IRequest<Result<StopDeliveryResponse>>
    {
        public Guid Token { get; set; }
        public string CustomerCode { get; set; } = string.Empty;
    }

    public class StopDeliveryResponse
    {
        public string TrekNumber { get; set; } = string.Empty;
        public DateOnly ScheduledDate { get; set; }
        public string TrekStatus { get; set; } = string.Empty;
        public string RegionName { get; set; } = string.Empty;
        public string DriverName { get; set; } = string.Empty;
        public string? SalesStaffName { get; set; }
        public string VehicleDisplayName { get; set; } = string.Empty;
        public StopDetail Stop { get; set; } = new();
    }

    public class StopDetail
    {
        public Guid StopId { get; set; }
        public int Sequence { get; set; }
        public bool IsWalkIn { get; set; }
        public CustomerDetail Customer { get; set; } = new();
        public InvoiceDetail? Invoice { get; set; }
        public List<ProductLine> Products { get; set; } = [];
        public StopTotals Totals { get; set; } = new();
    }

    public class CustomerDetail
    {
        public Guid Id { get; set; }
        public string CustomerCode { get; set; } = string.Empty;
        public string BusinessName { get; set; } = string.Empty;
        public string? TradingName { get; set; }
        public string PrimaryPhoneNumber { get; set; } = string.Empty;
        public string? WhatsAppNumber { get; set; }
        public string RegionName { get; set; } = string.Empty;
    }

    public class InvoiceDetail
    {
        public Guid Id { get; set; }
        public string? InvoiceNumber { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime IssuedAt { get; set; }
    }

    public class ProductLine
    {
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string BasicUnitName { get; set; } = string.Empty;
        public string? PackagingUnitName { get; set; }
        public decimal BasicQtyDelivered { get; set; }
        public decimal? PackagingQtyDelivered { get; set; }
        public decimal BasicUnitPrice { get; set; }
        public decimal? PackagingUnitPrice { get; set; }
        public decimal LineTotal { get; set; }
        public decimal? AmtPaid { get; set; }
        public decimal? Balance { get; set; }
        public string? PaymentMethod { get; set; }
        public DateTime? DeliveredAt { get; set; }
    }

    public class StopTotals
    {
        public decimal AmountDue { get; set; }
        public decimal AmtPaid { get; set; }
        public decimal Balance { get; set; }
    }

    internal sealed class Handler(AppDbContext db) : IRequestHandler<Query, Result<StopDeliveryResponse>>
    {
        public async Task<Result<StopDeliveryResponse>> Handle(Query request, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.CustomerCode))
                return Result.Failure<StopDeliveryResponse>(Error.BadRequest("Customer code is required."));

            var trip = await db.TrekkingTrips
                .Include(t => t.Region)
                .Include(t => t.Driver)
                .Include(t => t.SalesStaff)
                .Include(t => t.Vehicle)
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.DriverToken == request.Token, cancellationToken);

            if (trip is null)
                return Result.Failure<StopDeliveryResponse>(
                    Error.CreateNotFoundError("Trek not found. The token may be invalid."));

            var code = request.CustomerCode.Trim();

            var stop = await db.TrekkingTripStops
                .Include(s => s.CustomerAccount).ThenInclude(c => c.Region)
                .Include(s => s.Products).ThenInclude(p => p.Product).ThenInclude(p => p.BasicUnit)
                .Include(s => s.Products).ThenInclude(p => p.Product).ThenInclude(p => p.PackagingUnit)
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    s => s.TrekkingTripId == trip.Id
                      && s.CustomerAccount.CustomerCode.ToLower() == code.ToLower(),
                    cancellationToken);

            if (stop is null)
                return Result.Failure<StopDeliveryResponse>(
                    Error.CreateNotFoundError($"Customer '{code}' is not on this trek."));

            var invoice = await db.SaleInvoices
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.TrekkingTripStopId == stop.Id, cancellationToken);

            var products = stop.Products
                .Where(p => p.DeliveredAt.HasValue || p.BasicQtyDelivered.HasValue)
                .OrderBy(p => p.Product.Name)
                .Select(p => new ProductLine
                {
                    ProductId             = p.ProductId,
                    ProductName           = p.Product.Name,
                    BasicUnitName         = p.Product.BasicUnit?.Name ?? string.Empty,
                    PackagingUnitName     = p.Product.PackagingUnit?.Name,
                    BasicQtyDelivered     = p.BasicQtyDelivered ?? 0,
                    PackagingQtyDelivered = p.PackagingQtyDelivered,
                    BasicUnitPrice        = p.BasicUnitPrice,
                    PackagingUnitPrice    = p.PackagingUnitPrice,
                    LineTotal             = (p.BasicQtyDelivered ?? 0) * p.BasicUnitPrice
                                          + (p.PackagingQtyDelivered ?? 0) * (p.PackagingUnitPrice ?? 0),
                    AmtPaid               = p.AmtPaid,
                    Balance               = p.Balance,
                    PaymentMethod         = p.PaymentMethod?.ToString(),
                    DeliveredAt           = p.DeliveredAt
                })
                .ToList();

            var totals = new StopTotals
            {
                AmountDue = products.Sum(p => p.LineTotal),
                AmtPaid   = products.Sum(p => p.AmtPaid ?? 0),
                Balance   = products.Sum(p => p.Balance ?? 0)
            };

            var response = new StopDeliveryResponse
            {
                TrekNumber         = trip.TrekNumber ?? string.Empty,
                ScheduledDate      = trip.ScheduledDate,
                TrekStatus         = trip.Status.ToString(),
                RegionName         = trip.Region?.Name ?? string.Empty,
                DriverName         = trip.Driver?.FullName ?? string.Empty,
                SalesStaffName     = trip.SalesStaff?.FullName,
                VehicleDisplayName = trip.Vehicle?.DisplayName ?? string.Empty,
                Stop = new StopDetail
                {
                    StopId    = stop.Id,
                    Sequence  = stop.Sequence,
                    IsWalkIn  = stop.IsWalkIn,
                    Customer  = new CustomerDetail
                    {
                        Id                 = stop.CustomerAccount.Id,
                        CustomerCode       = stop.CustomerAccount.CustomerCode,
                        BusinessName       = stop.CustomerAccount.BusinessName,
                        TradingName        = stop.CustomerAccount.TradingName,
                        PrimaryPhoneNumber = stop.CustomerAccount.PrimaryPhoneNumber,
                        WhatsAppNumber     = stop.CustomerAccount.WhatsAppNumber,
                        RegionName         = stop.CustomerAccount.Region?.Name ?? string.Empty
                    },
                    Invoice = invoice is null ? null : new InvoiceDetail
                    {
                        Id            = invoice.Id,
                        InvoiceNumber = invoice.InvoiceNumber,
                        Status        = invoice.Status.ToString(),
                        IssuedAt      = invoice.IssuedAt
                    },
                    Products = products,
                    Totals   = totals
                }
            };

            return Result.Success(response);
        }
    }
}

public class GetDriverStopByCustomerCodeEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/treks/driver/{token:guid}/stops/by-customer/{customerCode}",
            async (Guid token, string customerCode, ISender sender) =>
            {
                var result = await sender.Send(new GetDriverStopByCustomerCode.Query
                {
                    Token = token,
                    CustomerCode = customerCode
                });
                return result.IsFailure ? Results.NotFound(result.Error) : Results.Ok(result.Value);
            })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Get a trek stop's delivery details by customer code (driver portal)")
        .WithDescription("Resolves the driver token to its trek, then returns the stop (customer, products, invoice if issued, totals) for the matching customer code — intended for client-side invoice generation. Case-insensitive exact match on CustomerAccount.CustomerCode.")
        .Produces<GetDriverStopByCustomerCode.StopDeliveryResponse>(200)
        .Produces<Error>(404)
        .AllowAnonymous();
    }
}
