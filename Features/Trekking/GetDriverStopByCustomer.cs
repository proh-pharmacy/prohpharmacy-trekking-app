using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Trekking.Entities;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class GetDriverStopByCustomer
{
    public class Query : IRequest<Result<StopDeliveryResponse>>
    {
        public Guid Token { get; set; }
        public string? CustomerCode { get; set; }
        public Guid? ClientGeneratedId { get; set; }
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
        public Guid? ClientGeneratedId { get; set; }
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
            var hasCode = !string.IsNullOrWhiteSpace(request.CustomerCode);
            var hasClientId = request.ClientGeneratedId.HasValue && request.ClientGeneratedId.Value != Guid.Empty;

            if (!hasCode && !hasClientId)
                return Result.Failure<StopDeliveryResponse>(
                    Error.BadRequest("Provide either customerCode or clientGeneratedId."));

            if (hasCode && hasClientId)
                return Result.Failure<StopDeliveryResponse>(
                    Error.BadRequest("Provide only one of customerCode or clientGeneratedId."));

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

            var baseQuery = db.TrekkingTripStops
                .Include(s => s.CustomerAccount).ThenInclude(c => c.Region)
                .Include(s => s.Products).ThenInclude(p => p.Product).ThenInclude(p => p.BasicUnit)
                .Include(s => s.Products).ThenInclude(p => p.Product).ThenInclude(p => p.PackagingUnit)
                .AsNoTracking()
                .Where(s => s.TrekkingTripId == trip.Id);

            TrekkingTripStop? stop;
            if (hasCode)
            {
                var code = request.CustomerCode!.Trim().ToLower();
                stop = await baseQuery.FirstOrDefaultAsync(
                    s => s.CustomerAccount.CustomerCode.ToLower() == code, cancellationToken);
            }
            else
            {
                var cgId = request.ClientGeneratedId!.Value;
                stop = await baseQuery.FirstOrDefaultAsync(
                    s => s.CustomerAccount.ClientGeneratedId == cgId, cancellationToken);
            }

            if (stop is null)
            {
                var ident = hasCode
                    ? $"code '{request.CustomerCode}'"
                    : $"client-generated ID '{request.ClientGeneratedId}'";
                return Result.Failure<StopDeliveryResponse>(
                    Error.CreateNotFoundError($"Customer with {ident} is not on this trek."));
            }

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
                        ClientGeneratedId  = stop.CustomerAccount.ClientGeneratedId,
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

public class GetDriverStopByCustomerEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/treks/driver/{token:guid}/stops/by-customer",
            async (
                Guid token,
                [FromQuery] string? customerCode,
                [FromQuery] Guid? clientGeneratedId,
                ISender sender) =>
            {
                var result = await sender.Send(new GetDriverStopByCustomer.Query
                {
                    Token = token,
                    CustomerCode = customerCode,
                    ClientGeneratedId = clientGeneratedId
                });

                if (result.IsFailure)
                {
                    return result.Error.Code == "404"
                        ? Results.NotFound(result.Error)
                        : Results.UnprocessableEntity(result.Error);
                }
                return Results.Ok(result.Value);
            })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Get a trek stop's delivery details by customer code or client-generated ID (driver portal)")
        .WithDescription("Resolves the driver token to its trek, then returns the stop (customer, products, invoice if issued, totals) for the matching customer — intended for client-side invoice generation. Provide exactly one of 'customerCode' or 'clientGeneratedId'. 'customerCode' is a case-insensitive exact match on CustomerAccount.CustomerCode. 'clientGeneratedId' matches CustomerAccount.ClientGeneratedId (populated for customers registered offline).")
        .Produces<GetDriverStopByCustomer.StopDeliveryResponse>(200)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .AllowAnonymous();
    }
}
