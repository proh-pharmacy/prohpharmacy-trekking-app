using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class GetStopInvoice
{
    public class Query : IRequest<Result<GetInvoice.InvoiceResponse>>
    {
        public Guid TrekId { get; set; }
        public Guid StopId { get; set; }
    }

    internal sealed class Handler(AppDbContext db) : IRequestHandler<Query, Result<GetInvoice.InvoiceResponse>>
    {
        public async Task<Result<GetInvoice.InvoiceResponse>> Handle(Query request, CancellationToken cancellationToken)
        {
            var invoice = await db.SaleInvoices
                .Include(i => i.Stop)
                    .ThenInclude(s => s.Products)
                        .ThenInclude(p => p.Product).ThenInclude(p => p.BasicUnit)
                .Include(i => i.Stop)
                    .ThenInclude(s => s.Products)
                        .ThenInclude(p => p.Product).ThenInclude(p => p.PackagingUnit)
                .Include(i => i.Stop)
                    .ThenInclude(s => s.CustomerAccount).ThenInclude(c => c.Region)
                .Include(i => i.Stop)
                    .ThenInclude(s => s.TrekkingTrip).ThenInclude(t => t.Driver)
                .Include(i => i.Stop)
                    .ThenInclude(s => s.TrekkingTrip).ThenInclude(t => t.SalesStaff)
                .Include(i => i.Stop)
                    .ThenInclude(s => s.TrekkingTrip).ThenInclude(t => t.Vehicle)
                .Include(i => i.Stop)
                    .ThenInclude(s => s.TrekkingTrip).ThenInclude(t => t.Region)
                .AsNoTracking()
                .FirstOrDefaultAsync(i =>
                    i.TrekkingTripId == request.TrekId &&
                    i.TrekkingTripStopId == request.StopId, cancellationToken);

            if (invoice is null)
                return Result.Failure<GetInvoice.InvoiceResponse>(Error.CreateNotFoundError("No invoice found for this stop."));

            return Result.Success(GetInvoice.Handler.ToResponse(invoice));
        }
    }
}

public class GetStopInvoiceEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/treks/{trekId:guid}/stops/{stopId:guid}/invoice",
            async (Guid trekId, Guid stopId, ISender sender) =>
            {
                var result = await sender.Send(new GetStopInvoice.Query
                {
                    TrekId = trekId,
                    StopId = stopId
                });
                return result.IsFailure
                    ? Results.NotFound(result.Error)
                    : Results.Ok(result.Value);
            })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Get the sale invoice for a specific trek stop")
        .Produces<GetInvoice.InvoiceResponse>(200)
        .Produces<Error>(404)
        .RequireAuthorization();
    }
}
