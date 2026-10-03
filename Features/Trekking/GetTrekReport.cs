using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Services.Pdf;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class GetTrekReport
{
    public class Query : IRequest<Result<GetDriverTrekReport.TrekReportData>>
    {
        public Guid TrekId { get; set; }
    }

    internal sealed class Handler(AppDbContext db) : IRequestHandler<Query, Result<GetDriverTrekReport.TrekReportData>>
    {
        public async Task<Result<GetDriverTrekReport.TrekReportData>> Handle(Query request, CancellationToken cancellationToken)
        {
            var trip = await db.TrekkingTrips
                .Include(t => t.Region)
                .Include(t => t.Driver)
                .Include(t => t.SalesStaff)
                .Include(t => t.Vehicle)
                .Include(t => t.Stops.OrderBy(s => s.Sequence))
                    .ThenInclude(s => s.CustomerAccount)
                .Include(t => t.Stops)
                    .ThenInclude(s => s.Products)
                        .ThenInclude(p => p.Product)
                .Include(t => t.Stops)
                    .ThenInclude(s => s.Returns)
                        .ThenInclude(r => r.Product)
                .Include(t => t.Stops)
                    .ThenInclude(s => s.Returns)
                        .ThenInclude(r => r.SaleInvoice)
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == request.TrekId, cancellationToken);

            if (trip is null)
                return Result.Failure<GetDriverTrekReport.TrekReportData>(Error.CreateNotFoundError("Trek not found."));

            var invoices = await db.SaleInvoices
                .Where(i => i.TrekkingTripId == trip.Id)
                .AsNoTracking()
                .ToDictionaryAsync(i => i.TrekkingTripStopId, cancellationToken);

            var stockLoads = await db.TrekStockLoads
                .Include(l => l.Product)
                .Where(l => l.TrekkingTripId == trip.Id)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            return Result.Success(GetDriverTrekReport.Handler.BuildReport(trip, invoices, stockLoads));
        }
    }
}

public class GetTrekReportEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/treks/{trekId:guid}/report",
            async (Guid trekId, ISender sender) =>
            {
                var result = await sender.Send(new GetTrekReport.Query { TrekId = trekId });
                return result.IsFailure ? Results.NotFound(result.Error) : Results.Ok(result.Value);
            })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Get trek financial report (admin)")
        .WithDescription("Staff-authenticated twin of the driver financial report. Returns the same TrekReportData shape — sales value, collections, outstanding balances, refunds, net cash on hand, and stock reconciliation. Available at any trek status.")
        .Produces<GetDriverTrekReport.TrekReportData>(200)
        .Produces<Error>(404)
        .RequireAuthorization();

        app.MapGet("api/v1/treks/{trekId:guid}/report/pdf",
            async (Guid trekId, ISender sender) =>
            {
                var result = await sender.Send(new GetTrekReport.Query { TrekId = trekId });
                if (result.IsFailure)
                    return Results.NotFound(result.Error);

                var bytes = DriverReportPdfGenerator.Generate(result.Value);
                return Results.File(bytes, "application/pdf", $"TrekReport-{result.Value.TrekNumber}-{result.Value.ScheduledDate}.pdf");
            })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Download trek financial report as PDF (admin)")
        .Produces<Error>(404)
        .RequireAuthorization();
    }
}
