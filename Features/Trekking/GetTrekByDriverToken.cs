using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class GetTrekByDriverToken
{
    public class Query : IRequest<Result<DriverTrekResponse>>
    {
        public Guid Token { get; set; }
    }

    public class DriverTrekResponse
    {
        public Guid TrekId { get; set; }
        public string TrekNumber { get; set; } = string.Empty;
        public DateOnly ScheduledDate { get; set; }
        public string DriverName { get; set; } = string.Empty;
        public string VehicleDisplayName { get; set; } = string.Empty;
        public string RegionName { get; set; } = string.Empty;
        public Guid? SalesStaffId { get; set; }
        public string? SalesStaffName { get; set; }
        public string Status { get; set; } = string.Empty;
        public bool IsLocked { get; set; }
        public List<DriverStopResponse> Stops { get; set; } = [];
    }

    public class DriverStopResponse
    {
        public Guid StopId { get; set; }
        public int Sequence { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public string CustomerCode { get; set; } = string.Empty;
        public string? PrimaryPhoneNumber { get; set; }
        public string? CustomerType { get; set; }
        public string? RegionName { get; set; }
        public string? DistrictName { get; set; }
        public string? PrimaryLocationLandmark { get; set; }
        public string? PrimaryLocationStreet { get; set; }
        public string? PrimaryContactName { get; set; }
        public string? PrimaryContactPhone { get; set; }
        public string? Notes { get; set; }
        public List<DriverProductResponse> Products { get; set; } = [];
    }

    public class DriverProductResponse
    {
        public Guid StopProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string? BasicUnitName { get; set; }
        public string? PackagingUnitName { get; set; }
        public decimal PlannedBasicQuantity { get; set; }
        public decimal? PlannedPackagingQuantity { get; set; }
        public decimal? BasicQtyDelivered { get; set; }
        public decimal? PackagingQtyDelivered { get; set; }
        public string? PaymentMethod { get; set; }
        public decimal? AmtPaid { get; set; }
        public decimal? Balance { get; set; }
        public string? Notes { get; set; }
        public DateTime? DeliveredAt { get; set; }
    }

    internal sealed class Handler(AppDbContext db)
        : IRequestHandler<Query, Result<DriverTrekResponse>>
    {
        public async Task<Result<DriverTrekResponse>> Handle(Query request, CancellationToken cancellationToken)
        {
            var trip = await db.TrekkingTrips
                .Include(t => t.Region)
                .Include(t => t.Driver)
                .Include(t => t.SalesStaff)
                .Include(t => t.Vehicle)
                .Include(t => t.Stops.OrderBy(s => s.Sequence))
                    .ThenInclude(s => s.CustomerAccount)
                        .ThenInclude(ca => ca.Locations.Where(l => l.IsPrimary))
                            .ThenInclude(l => l.District)
                .Include(t => t.Stops)
                    .ThenInclude(s => s.CustomerAccount)
                        .ThenInclude(ca => ca.Region)
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
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.DriverToken == request.Token, cancellationToken);

            if (trip is null)
                return Result.Failure<DriverTrekResponse>(Error.CreateNotFoundError("Trek not found. The link may be invalid."));

            return Result.Success(new DriverTrekResponse
            {
                TrekId = trip.Id,
                TrekNumber = trip.TrekNumber,
                ScheduledDate = trip.ScheduledDate,
                DriverName = trip.Driver?.FullName ?? string.Empty,
                VehicleDisplayName = trip.Vehicle?.DisplayName ?? string.Empty,
                RegionName = trip.Region?.Name ?? string.Empty,
                SalesStaffId = trip.SalesStaffId,
                SalesStaffName = trip.SalesStaff?.FullName,
                Status = trip.Status.ToString(),
                IsLocked = trip.Status == Enums.TrekStatus.Completed,
                Stops = trip.Stops.OrderBy(s => s.Sequence).Select(s =>
                {
                    var loc = s.CustomerAccount?.Locations.FirstOrDefault();
                    var contact = s.CustomerAccount?.People.FirstOrDefault();
                    return new DriverStopResponse
                    {
                        StopId = s.Id,
                        Sequence = s.Sequence,
                        CustomerName = s.CustomerAccount?.BusinessName ?? string.Empty,
                        CustomerCode = s.CustomerAccount?.CustomerCode ?? string.Empty,
                        PrimaryPhoneNumber = s.CustomerAccount?.PrimaryPhoneNumber,
                        CustomerType = s.CustomerAccount?.CustomerType.ToString(),
                        RegionName = s.CustomerAccount?.Region?.Name,
                        DistrictName = loc?.District?.Name,
                        PrimaryLocationLandmark = loc?.LandmarkAndDirections,
                        PrimaryLocationStreet = loc?.StreetAddress,
                        PrimaryContactName = contact?.FullName,
                        PrimaryContactPhone = contact?.PrimaryPhoneNumber,
                        Notes = s.Notes,
                        Products = s.Products.Select(p => new DriverProductResponse
                        {
                            StopProductId = p.Id,
                            ProductName = p.Product?.Name ?? string.Empty,
                            BasicUnitName = p.Product?.BasicUnit?.Name,
                            PackagingUnitName = p.Product?.PackagingUnit?.Name,
                            PlannedBasicQuantity = p.PlannedBasicQuantity,
                            PlannedPackagingQuantity = p.PlannedPackagingQuantity,
                            BasicQtyDelivered = p.BasicQtyDelivered,
                            PackagingQtyDelivered = p.PackagingQtyDelivered,
                            PaymentMethod = p.PaymentMethod?.ToString(),
                            AmtPaid = p.AmtPaid,
                            Balance = p.Balance,
                            Notes = p.Notes,
                            DeliveredAt = p.DeliveredAt
                        }).ToList()
                    };
                }).ToList()
            });
        }
    }
}

public class GetTrekByDriverTokenEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/treks/driver/{token:guid}", async (Guid token, ISender sender) =>
        {
            var result = await sender.Send(new GetTrekByDriverToken.Query { Token = token });
            return result.IsFailure
                ? Results.NotFound(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Get trek data via driver token")
        .WithDescription("Returns full trek data for the driver form. No authentication required — the token acts as the access key. IsLocked=true when the trek is Completed.")
        .Produces<GetTrekByDriverToken.DriverTrekResponse>(200)
        .Produces<Error>(404)
        .AllowAnonymous();
    }
}
