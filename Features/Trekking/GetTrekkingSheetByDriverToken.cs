using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Services.Pdf;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class GetTrekkingSheetByDriverToken
{
    public class Query : IRequest<Result<TrekkingSheetPdfGenerator.TrekkingSheetData>>
    {
        public Guid Token { get; set; }
    }

    internal sealed class Handler(AppDbContext db)
        : IRequestHandler<Query, Result<TrekkingSheetPdfGenerator.TrekkingSheetData>>
    {
        public async Task<Result<TrekkingSheetPdfGenerator.TrekkingSheetData>> Handle(Query request, CancellationToken cancellationToken)
        {
            var trip = await db.TrekkingTrips
                .Include(t => t.Branch)
                .Include(t => t.Driver)
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
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.DriverToken == request.Token, cancellationToken);

            if (trip is null)
                return Result.Failure<TrekkingSheetPdfGenerator.TrekkingSheetData>(
                    Error.CreateNotFoundError("Trek not found. The link may be invalid."));

            var sheetData = new TrekkingSheetPdfGenerator.TrekkingSheetData
            {
                TrekNumber = trip.TrekNumber,
                ScheduledDate = trip.ScheduledDate,
                DriverName = trip.Driver?.FullName ?? string.Empty,
                VehicleDisplayName = trip.Vehicle?.DisplayName ?? string.Empty,
                BranchName = trip.Branch?.Name ?? string.Empty,
                Stops = trip.Stops.OrderBy(s => s.Sequence).Select(s =>
                {
                    var primaryLocation = s.CustomerAccount?.Locations.FirstOrDefault();
                    var primaryContact = s.CustomerAccount?.People.FirstOrDefault();
                    return new TrekkingSheetPdfGenerator.TrekkingSheetData.StopData
                    {
                        Sequence = s.Sequence,
                        CustomerName = s.CustomerAccount?.BusinessName ?? string.Empty,
                        CustomerCode = s.CustomerAccount?.CustomerCode ?? string.Empty,
                        PrimaryPhoneNumber = s.CustomerAccount?.PrimaryPhoneNumber,
                        DistrictName = primaryLocation?.District?.Name,
                        RegionName = s.CustomerAccount?.Region?.Name,
                        PrimaryLocationLandmark = primaryLocation?.LandmarkAndDirections,
                        PrimaryLocationStreet = primaryLocation?.StreetAddress,
                        PrimaryContactName = primaryContact?.FullName,
                        PrimaryContactPhone = primaryContact?.PrimaryPhoneNumber,
                        Products = s.Products.Select(p => new TrekkingSheetPdfGenerator.TrekkingSheetData.ProductData
                        {
                            ProductName = p.Product?.Name ?? string.Empty,
                            Unit = p.Product?.Unit,
                            PlannedQuantity = p.PlannedQuantity,
                            QtyDelivered = p.QtyDelivered,
                            PaymentMethod = p.PaymentMethod?.ToString(),
                            AmtPaid = p.AmtPaid,
                            Balance = p.Balance,
                            Notes = p.Notes
                        }).ToList()
                    };
                }).ToList()
            };

            return Result.Success(sheetData);
        }
    }
}

public class GetTrekkingSheetByDriverTokenEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/treks/driver/{token:guid}/sheet/pdf", async (Guid token, ISender sender) =>
        {
            var result = await sender.Send(new GetTrekkingSheetByDriverToken.Query { Token = token });
            if (result.IsFailure)
                return Results.NotFound(result.Error);

            var data = result.Value;
            var bytes = TrekkingSheetPdfGenerator.Generate(data);
            return Results.File(bytes, "application/pdf", $"TrekkingSheet-{data.TrekNumber}-{data.ScheduledDate}.pdf");
        })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Download trekking sheet PDF via driver token")
        .WithDescription("Allows the driver to download the delivery sheet PDF using the shared link token. No authentication required.")
        .Produces<Error>(404)
        .AllowAnonymous();
    }
}
