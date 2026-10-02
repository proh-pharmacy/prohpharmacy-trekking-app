using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class GetReturnableInvoicesByDriverToken
{
    public class Query : IRequest<Result<List<InvoiceResponse>>>
    {
        public Guid Token { get; set; }
        public Guid StopId { get; set; }
    }

    public class InvoiceResponse
    {
        public Guid Id { get; set; }
        public string? InvoiceNumber { get; set; }
        public DateTime IssuedAt { get; set; }
        public Guid TrekkingTripId { get; set; }
        public Guid TrekkingTripStopId { get; set; }
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
            var trip = await db.TrekkingTrips.AsNoTracking()
                .FirstOrDefaultAsync(t => t.DriverToken == request.Token, cancellationToken);

            if (trip is null)
                return Result.Failure<List<InvoiceResponse>>(Error.CreateNotFoundError("Trek not found. The token may be invalid."));

            var stop = await db.TrekkingTripStops.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == request.StopId && s.TrekkingTripId == trip.Id, cancellationToken);

            if (stop is null)
                return Result.Failure<List<InvoiceResponse>>(Error.CreateNotFoundError("Stop not found on this trek."));

            var invoices = await db.SaleInvoices
                .Where(i => i.CustomerAccountId == stop.CustomerAccountId)
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

public class GetReturnableInvoicesByDriverTokenEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/treks/driver/{token:guid}/stops/{stopId:guid}/returnable-invoices",
            async (Guid token, Guid stopId, ISender sender) =>
            {
                var result = await sender.Send(new GetReturnableInvoicesByDriverToken.Query
                {
                    Token = token,
                    StopId = stopId
                });
                return result.IsFailure ? Results.NotFound(result.Error) : Results.Ok(result.Value);
            })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("List returnable invoices for the stop's customer (driver portal)")
        .WithDescription("Returns the stop customer's invoice history across all treks, ordered newest first, with delivered line items. Use these invoices when recording a return on the current stop.")
        .Produces<List<GetReturnableInvoicesByDriverToken.InvoiceResponse>>(200)
        .Produces<Error>(404)
        .AllowAnonymous();
    }
}
