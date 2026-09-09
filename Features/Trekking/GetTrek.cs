using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
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
                .Include(t => t.Branch)
                .Include(t => t.Driver)
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
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken);

            if (trip is null)
                return Result.Failure<TrekResponse>(Error.CreateNotFoundError("Trekking trip not found."));

            var stops = trip.Stops
                .OrderBy(s => s.Sequence)
                .Select(s => MapStop(s))
                .ToList();

            return Result.Success(CreateTrek.Handler.ToResponse(
                trip,
                trip.Branch?.Name ?? string.Empty,
                trip.Driver?.FullName ?? string.Empty,
                trip.Vehicle?.DisplayName ?? string.Empty,
                stops));
        }

        internal static TrekStopResponse MapStop(Entities.TrekkingTripStop stop)
        {
            var primaryLocation = stop.CustomerAccount?.Locations.FirstOrDefault();
            var primaryContact = stop.CustomerAccount?.People.FirstOrDefault();
            return new TrekStopResponse
            {
                StopId = stop.Id,
                Sequence = stop.Sequence,
                CustomerAccountId = stop.CustomerAccountId,
                CustomerName = stop.CustomerAccount?.BusinessName ?? string.Empty,
                CustomerCode = stop.CustomerAccount?.CustomerCode ?? string.Empty,
                CustomerPhone = stop.CustomerAccount?.PrimaryPhoneNumber,
                CustomerType = stop.CustomerAccount?.CustomerType.ToString(),
                RegionName = stop.CustomerAccount?.Region?.Name,
                DistrictName = primaryLocation?.District?.Name,
                PrimaryLocationLandmark = primaryLocation?.LandmarkAndDirections,
                PrimaryLocationStreet = primaryLocation?.StreetAddress,
                PrimaryContactName = primaryContact?.FullName,
                PrimaryContactPhone = primaryContact?.PrimaryPhoneNumber,
                Notes = stop.Notes,
                Products = stop.Products.Select(p => new TrekStopProductResponse
                {
                    StopProductId = p.Id,
                    ProductId = p.ProductId,
                    ProductName = p.Product?.Name ?? string.Empty,
                    Unit = p.Product?.Unit,
                    PlannedQuantity = p.PlannedQuantity,
                    QtyDelivered = p.QtyDelivered,
                    PaymentMethod = p.PaymentMethod?.ToString(),
                    AmtPaid = p.AmtPaid,
                    Balance = p.Balance,
                    Notes = p.Notes,
                    DeliveredAt = p.DeliveredAt
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
