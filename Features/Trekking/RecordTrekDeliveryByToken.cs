using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Trekking.Enums;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class RecordTrekDeliveryByToken
{
    public class Command : IRequest<Result<RecordTrekDelivery.RecordResponse>>
    {
        public Guid Token { get; set; }
        public List<RecordTrekDelivery.ProductRecord> Products { get; set; } = [];
    }

    internal sealed class Handler(AppDbContext db)
        : IRequestHandler<Command, Result<RecordTrekDelivery.RecordResponse>>
    {
        public async Task<Result<RecordTrekDelivery.RecordResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var trip = await db.TrekkingTrips
                .Include(t => t.Stops)
                    .ThenInclude(s => s.Products)
                .FirstOrDefaultAsync(t => t.DriverToken == request.Token, cancellationToken);

            if (trip is null)
                return Result.Failure<RecordTrekDelivery.RecordResponse>(Error.CreateNotFoundError("Trek not found. The link may be invalid."));

            if (trip.Status == TrekStatus.Completed)
                return Result.Failure<RecordTrekDelivery.RecordResponse>(Error.BadRequest("This trek is completed and can no longer be modified."));

            var productMap = trip.Stops
                .SelectMany(s => s.Products)
                .ToDictionary(p => p.Id);

            var productToStop = new Dictionary<Guid, TrekkingTripStop>();
            foreach (var stop in trip.Stops)
                foreach (var product in stop.Products)
                    productToStop[product.Id] = stop;

            var affectedStops = new HashSet<TrekkingTripStop>();
            var recorded = 0;
            foreach (var record in request.Products)
            {
                if (!productMap.TryGetValue(record.StopProductId, out var product)) continue;

                product.QtyDelivered = record.QtyDelivered;
                product.PaymentMethod = record.PaymentMethod;
                product.AmtPaid = record.AmtPaid;
                product.Balance = record.Balance;
                product.Notes = record.Notes?.Trim();
                product.DeliveredAt = DateTime.UtcNow;

                if (productToStop.TryGetValue(record.StopProductId, out var affectedStop))
                    affectedStops.Add(affectedStop);

                recorded++;
            }

            trip.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            return Result.Success(new RecordTrekDelivery.RecordResponse
            {
                TrekId = trip.Id,
                TrekNumber = trip.TrekNumber,
                Status = trip.Status.ToString(),
                Recorded = recorded
            });
        }
    }
}

public class RecordTrekDeliveryByTokenEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/treks/driver/{token:guid}/record", async (Guid token, RecordTrekDeliveryByToken.Command command, ISender sender) =>
        {
            command.Token = token;
            var result = await sender.Send(command);
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Record delivery results via driver token")
        .WithDescription("Allows a driver to submit delivery quantities, payment, and balance per product using the shared link token. No authentication required. Rejected if trek is Completed.")
        .Produces<RecordTrekDelivery.RecordResponse>(200)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .AllowAnonymous();
    }
}
