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
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken);

            if (trip is null)
                return Result.Failure<TrekResponse>(Error.CreateNotFoundError("Trekking trip not found."));

            var stops = trip.Stops
                .OrderBy(s => s.Sequence)
                .Select(s => MapStop(s))
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

                var regionMarkupRules = await _db.RegionalMarkupRules
                    .Where(r => r.RegionId == trip.RegionId &&
                                (r.ProductId == null || productIds.Contains(r.ProductId.Value)))
                    .AsNoTracking()
                    .ToListAsync(cancellationToken);
                var regionProductMarkups = regionMarkupRules
                    .Where(r => r.ProductId.HasValue)
                    .ToDictionary(r => r.ProductId!.Value, r => r.MarkupPercentage);
                var regionWideMarkup = regionMarkupRules
                    .FirstOrDefault(r => !r.ProductId.HasValue)?.MarkupPercentage;

                var customerIds = trip.Stops.Select(s => s.CustomerAccountId).Distinct().ToList();
                var allCustomerMarkupRows = await _db.CustomerMarkupRules
                    .Where(r => customerIds.Contains(r.CustomerAccountId) &&
                                (r.ProductId == null || productIds.Contains(r.ProductId.Value)))
                    .AsNoTracking()
                    .ToListAsync(cancellationToken);

                var customerMarkups = allCustomerMarkupRows
                    .GroupBy(r => r.CustomerAccountId)
                    .ToDictionary(
                        g => g.Key,
                        g => (
                            Products: g.Where(r => r.ProductId.HasValue).ToDictionary(r => r.ProductId!.Value, r => r.MarkupPercentage),
                            Wildcard: g.Where(r => !r.ProductId.HasValue).Select(r => r.MarkupPercentage).Cast<decimal?>().FirstOrDefault()
                        ));

                syncRequired = candidateProducts.Any(x =>
                {
                    if (!catalogPrices.TryGetValue(x.Product.ProductId, out var cat)) return false;
                    customerMarkups.TryGetValue(x.Stop.CustomerAccountId, out var cm);
                    var resolvedBasic = ResolvePrice(cat.BasicUnitPrice, x.Product.ProductId,
                        cm.Products ?? [], cm.Wildcard, regionProductMarkups, regionWideMarkup);
                    if (x.Product.BasicUnitPrice != resolvedBasic) return true;
                    var hadPackaging = x.Product.PackagingUnitPrice.HasValue;
                    var nowHasPackaging = cat.PackagingUnitId.HasValue;
                    if (hadPackaging != nowHasPackaging) return true;
                    if (hadPackaging && nowHasPackaging)
                    {
                        var resolvedPkg = ResolvePrice(cat.PackagingUnitPrice!.Value, x.Product.ProductId,
                            cm.Products ?? [], cm.Wildcard, regionProductMarkups, regionWideMarkup);
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

        private static decimal ApplyMarkup(decimal price, decimal pct) =>
            Math.Round(price * (1 + pct / 100m), 2);

        private static decimal ResolvePrice(
            decimal basePrice,
            Guid productId,
            Dictionary<Guid, decimal> customerProductMarkups,
            decimal? customerWideMarkup,
            Dictionary<Guid, decimal> regionProductMarkups,
            decimal? regionWideMarkup)
        {
            if (customerProductMarkups.TryGetValue(productId, out var cm)) return ApplyMarkup(basePrice, cm);
            if (customerWideMarkup.HasValue) return ApplyMarkup(basePrice, customerWideMarkup.Value);
            if (regionProductMarkups.TryGetValue(productId, out var rm)) return ApplyMarkup(basePrice, rm);
            if (regionWideMarkup.HasValue) return ApplyMarkup(basePrice, regionWideMarkup.Value);
            return basePrice;
        }

        internal static TrekStopResponse MapStop(Entities.TrekkingTripStop stop)
        {
            var primaryLocation = stop.CustomerAccount?.Locations.FirstOrDefault();
            var primaryContact = stop.CustomerAccount?.People.FirstOrDefault();
            return new TrekStopResponse
            {
                StopId = stop.Id,
                Sequence = stop.Sequence,
                IsWalkIn = stop.IsWalkIn,
                CustomerAccountId = stop.CustomerAccountId,
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
                    ProductId = p.ProductId,
                    ProductName = p.Product?.Name ?? string.Empty,
                    BasicUnitName = p.Product?.BasicUnit?.Name,
                    PackagingUnitName = p.Product?.PackagingUnit?.Name,
                    BasicUnitPrice = p.BasicUnitPrice,
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
                    DeliveredAt = p.DeliveredAt
                }).ToList(),
                Returns = stop.Returns.Select(r => new TrekStopReturnResponse
                {
                    ReturnId = r.Id,
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
