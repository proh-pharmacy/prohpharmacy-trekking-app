using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Services.Pdf;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class ExportTrekkingSheet
{
    public class Query : IRequest<Result<TrekkingSheetPdfGenerator.TrekkingSheetData>>
    {
        public Guid Id { get; set; }
    }

    internal sealed class Handler : IRequestHandler<Query, Result<TrekkingSheetPdfGenerator.TrekkingSheetData>>
    {
        private readonly AppDbContext _db;

        public Handler(AppDbContext db) => _db = db;

        public async Task<Result<TrekkingSheetPdfGenerator.TrekkingSheetData>> Handle(Query request, CancellationToken cancellationToken)
        {
            var trip = await _db.TrekkingTrips
                .Include(t => t.Branch)
                .Include(t => t.Driver)
                .Include(t => t.Vehicle)
                .Include(t => t.Stops.OrderBy(s => s.Sequence))
                    .ThenInclude(s => s.CustomerAccount)
                        .ThenInclude(ca => ca.Locations.Where(l => l.IsPrimary))
                .Include(t => t.Stops)
                    .ThenInclude(s => s.Products)
                        .ThenInclude(p => p.Product)
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken);

            if (trip is null)
                return Result.Failure<TrekkingSheetPdfGenerator.TrekkingSheetData>(
                    Error.CreateNotFoundError("Trekking trip not found."));

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
                    return new TrekkingSheetPdfGenerator.TrekkingSheetData.StopData
                    {
                        Sequence = s.Sequence,
                        CustomerName = s.CustomerAccount?.BusinessName ?? string.Empty,
                        CustomerCode = s.CustomerAccount?.CustomerCode ?? string.Empty,
                        PrimaryLocationLandmark = primaryLocation?.LandmarkAndDirections,
                        PrimaryLocationStreet = primaryLocation?.StreetAddress,
                        Products = s.Products.Select(p => new TrekkingSheetPdfGenerator.TrekkingSheetData.ProductData
                        {
                            ProductName = p.Product?.Name ?? string.Empty,
                            Unit = p.Product?.Unit,
                            PlannedQuantity = p.PlannedQuantity
                        }).ToList()
                    };
                }).ToList()
            };

            return Result.Success(sheetData);
        }
    }
}

public class ExportTrekkingSheetEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/treks/{id:guid}/sheet/pdf", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new ExportTrekkingSheet.Query { Id = id });
            if (result.IsFailure)
                return Results.NotFound(result.Error);

            var data = result.Value;
            var bytes = TrekkingSheetPdfGenerator.Generate(data);
            return Results.File(bytes, "application/pdf", $"TrekkingSheet-{data.TrekNumber}-{data.ScheduledDate}.pdf");
        })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Export trekking sheet as PDF")
        .Produces<Error>(404)
        .RequireAuthorization();
    }
}
