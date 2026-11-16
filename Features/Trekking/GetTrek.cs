using prohpharmacy_trekking_app.Services.Pricing;
using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Trekking.Enums;
using prohpharmacy_trekking_app.Shared;
using static prohpharmacy_trekking_app.Features.Trekking.CreateTrek;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class GetTrek
{
    public class Query : IRequest<Result<TrekResponse>>
    {
        public Guid Id { get; set; }
    }

    internal sealed class Handler : IRequestHandler<Query, Result<TrekResponse>>
    {
        private readonly AppDbContext _db;

        public Handler(AppDbContext db) => _db = db;

        public async Task<Result<TrekResponse>> Handle(Query request, CancellationToken cancellationToken)
        {
            var trip = await _db.TrekkingTrips
                .Include(t => t.Region)
                .Include(t => t.Branch)
                .Include(t => t.Driver)
                .Include(t => t.SalesStaff)
                .Include(t => t.Vehicle)
                .Include(t => t.Stops.OrderBy(s => s.Sequence))
                    .ThenInclude(s => s.CustomerAccount)
                        .ThenInclude(ca => ca.Region)
                .Include(t => t.Stops)
                    .ThenInclude(s => s.CustomerAccount)
                        .ThenInclude(ca => ca.Locations.Where(l => l.IsPrimary))
                            .ThenInclude(l => l.District)
                .Include(t => t.Stops)
                    .ThenInclude(s => s.CustomerAccount)
                        .ThenInclude(ca => ca.People.Where(p => p.IsPrimaryContact && p.IsActive))
                .Include(t => t.Stops)
                    .ThenInclude(s => s.Products)
                        .ThenInclude(p => p.Product)
                            .ThenInclude(p => p.BasicUnit)
                .Include(t => t.Stops)
                    .ThenInclude(s => s.Products)
                        .ThenInclude(p => p.Product)
                            .ThenInclude(p => p.PackagingUnit)
                .Include(t => t.Stops)
                    .ThenInclude(s => s.Returns)
                        .ThenInclude(r => r.Product)
                            .ThenInclude(p => p.BasicUnit)
                .Include(t => t.Stops)
                    .ThenInclude(s => s.Returns)
                        .ThenInclude(r => r.Product)
                            .ThenInclude(p => p.PackagingUnit)
                .Include(t => t.Stops)
                    .ThenInclude(s => s.Returns)
                        .ThenInclude(r => r.SaleInvoice)
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken);

            if (trip is null)
                return Result.Failure<TrekResponse>(Error.CreateNotFoundError("Trekking trip not found."));

            var invoices = await _db.SaleInvoices
                .Where(i => i.TrekkingTripId == trip.Id)
                .AsNoTracking()
                .ToDictionaryAsync(i => i.TrekkingTripStopId, cancellationToken);

            var stops = trip.Stops
                .OrderBy(s => s.Sequence)
                .Select(s => MapStop(s, invoices.TryGetValue(s.Id, out var inv) ? inv : null))
                .ToList();

            bool syncRequired;

            if (trip.Status == TrekStatus.Completed || trip.Status == TrekStatus.Cancelled)
            {
                syncRequired = false;
            }
            else
            {
                var candidateProducts = trip.Stops
                    .SelectMany(s => s.Products.Select(p => new { Stop = s, Product = p }))
                    .Where(x => trip.Status != TrekStatus.InProgress || !x.Product.DeliveredAt.HasValue)
                    .ToList();

                var productIds = candidateProducts
                    .Select(x => x.Product.ProductId)
                    .Distinct()
                    .ToList();

                var catalogPrices = await _db.Products
                    .Where(p => productIds.Contains(p.Id))
                    .AsNoTracking()
                    .ToDictionaryAsync(p => p.Id, cancellationToken);

                var pricing = await new TrekPriceReader(_db).ReadAsync(trip.VehicleId, trip.RegionId,
                    productIds, trip.Stops.Select(s => s.CustomerAccountId), cancellationToken);

                syncRequired = candidateProducts.Any(x =>
                {
                    if (!catalogPrices.TryGetValue(x.Product.ProductId, out var cat)) return false;
                    var resolvedBasic = pricing.Basic(cat, x.Stop.CustomerAccountId);
                    if (!x.Product.UsesVehicleStockPrice && !pricing.HasVehiclePrice(x.Product.ProductId) &&
                        x.Product.BasicUnitPrice != resolvedBasic) return true;
                    var hadPackaging = x.Product.PackagingUnitPrice.HasValue;
                    var nowHasPackaging = cat.PackagingUnitId.HasValue;
                    if (hadPackaging != nowHasPackaging) return true;
                    if (hadPackaging && nowHasPackaging)
                    {
                        var resolvedPkg = pricing.Packaging(cat, x.Stop.CustomerAccountId);
                        if (x.Product.PackagingUnitPrice != resolvedPkg) return true;
                    }
                    return false;
                });
            }

            var response = CreateTrek.Handler.ToResponse(
                trip,
                trip.Region?.Name ?? string.Empty,
                trip.Branch?.Name,
                trip.Driver?.FullName ?? string.Empty,
                trip.SalesStaff?.FullName,
                trip.Vehicle?.DisplayName ?? string.Empty,
                stops);

            response.SyncRequired = syncRequired;
            return Result.Success(response);
        }

        internal static TrekStopResponse MapStop(Entities.TrekkingTripStop stop, Entities.SaleInvoice? invoice = null)
        {
            var primaryLocation = stop.CustomerAccount?.Locations.FirstOrDefault();
            var primaryContact = stop.CustomerAccount?.People.FirstOrDefault();
            return new TrekStopResponse
            {
                StopId = stop.Id,
                InvoiceId = invoice?.Id,
                InvoiceNumber = invoice?.InvoiceNumber,
                InvoiceStatus = invoice?.Status.ToString(),
                InvoiceTotal = invoice?.TotalAmount,
                InvoicePaid = invoice?.TotalPaid,
                InvoiceBalance = invoice?.Balance,
                Sequence = stop.Sequence,
                IsWalkIn = stop.IsWalkIn,
                CustomerAccountId = stop.CustomerAccountId,
                CustomerClientGeneratedId = stop.CustomerAccount?.ClientGeneratedId,
                CustomerName = stop.CustomerAccount?.BusinessName ?? string.Empty,
                CustomerCode = stop.CustomerAccount?.CustomerCode ?? string.Empty,
                CustomerPhone = stop.CustomerAccount?.PrimaryPhoneNumber,
                CustomerType = stop.CustomerAccount?.CustomerType.ToString(),
                RegionName = stop.CustomerAccount?.Region?.Name,
                DistrictName = primaryLocation?.District?.Name,
                PrimaryLocationLandmark = primaryLocation?.LandmarkAndDirections,
                PrimaryLocationStreet = primaryLocation?.StreetAddress,
                Latitude = primaryLocation?.Latitude,
                Longitude = primaryLocation?.Longitude,
                AccuracyMetres = primaryLocation?.AccuracyMetres,
                PrimaryContactName = primaryContact?.FullName,
                PrimaryContactPhone = primaryContact?.PrimaryPhoneNumber,
                Notes = stop.Notes,
                Products = stop.Products.Select(p => new TrekStopProductResponse
                {
                    StopProductId = p.Id,
                    ClientGeneratedId = p.ClientGeneratedId,
                    ProductId = p.ProductId,
                    ProductName = p.Product?.Name ?? string.Empty,
                    BasicUnitName = p.Product?.BasicUnit?.Name,
                    PackagingUnitName = p.Product?.PackagingUnit?.Name,
                    BasicUnitPrice = p.BasicUnitPrice,
                    UsesVehicleStockPrice = p.UsesVehicleStockPrice,
                    PackagingUnitPrice = p.PackagingUnitPrice,
                    PlannedBasicQuantity = p.PlannedBasicQuantity,
                    PlannedPackagingQuantity = p.PlannedPackagingQuantity,
                    BasicQtyDelivered = p.BasicQtyDelivered,
                    PackagingQtyDelivered = p.PackagingQtyDelivered,
                    AmountDue = p.AmountDue,
                    PaymentMethod = p.PaymentMethod?.ToString(),
                    AmtPaid = p.AmtPaid,
                    Balance = p.Balance,
                    IsUnplanned = p.IsUnplanned,
                    Notes = p.Notes,
                    AddedAt = p.AddedAt,
                    DeliveredAt = p.DeliveredAt
                }).ToList(),
                Returns = stop.Returns.Select(r => new TrekStopReturnResponse
                {
                    ReturnId = r.Id,
                    SaleInvoiceId = r.SaleInvoiceId,
                    InvoiceNumber = r.SaleInvoice?.InvoiceNumber,
                    ProductId = r.ProductId,
                    ProductName = r.Product?.Name ?? string.Empty,
                    BasicUnitName = r.Product?.BasicUnit?.Name,
                    PackagingUnitName = r.Product?.PackagingUnit?.Name,
                    BasicQtyReturned = r.BasicQtyReturned,
                    PackagingQtyReturned = r.PackagingQtyReturned,
                    BasicUnitPrice = r.BasicUnitPrice,
                    PackagingUnitPrice = r.PackagingUnitPrice,
                    RefundAmount = r.RefundAmount,
                    RefundMethod = r.RefundMethod?.ToString(),
                    Reason = r.Reason,
                    RecordedAt = r.RecordedAt
                }).ToList()
            };
        }
    }
}

public class GetTrekEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/treks/{id:guid}", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new GetTrek.Query { Id = id });
            return result.IsFailure
                ? Results.NotFound(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Get a trekking trip by ID")
        .Produces<TrekResponse>(200)
        .Produces<Error>(404)
        .RequireAuthorization();
    }
}
