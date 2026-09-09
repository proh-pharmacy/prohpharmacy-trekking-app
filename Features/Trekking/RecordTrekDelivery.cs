using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Trekking.Entities;
using prohpharmacy_trekking_app.Features.Trekking.Enums;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class RecordTrekDelivery
{
    public class Command : IRequest<Result<RecordResponse>>
    {
        public Guid TrekId { get; set; }
        public List<ProductRecord> Products { get; set; } = [];
    }

    public class ProductRecord
    {
        public Guid StopProductId { get; set; }
        public decimal? QtyDelivered { get; set; }
        public PaymentMethod? PaymentMethod { get; set; }
        public decimal? AmtPaid { get; set; }
        public decimal? Balance { get; set; }
        public string? Notes { get; set; }
    }

    public class RecordResponse
    {
        public Guid TrekId { get; set; }
        public string TrekNumber { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int Recorded { get; set; }
    }

    internal sealed class Handler(AppDbContext db)
        : IRequestHandler<Command, Result<RecordResponse>>
    {
        public async Task<Result<RecordResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var trip = await db.TrekkingTrips
                .Include(t => t.Stops)
                    .ThenInclude(s => s.Products)
                .FirstOrDefaultAsync(t => t.Id == request.TrekId, cancellationToken);

            if (trip is null)
                return Result.Failure<RecordResponse>(Error.CreateNotFoundError("Trekking trip not found."));

            if (trip.Status == TrekStatus.Completed)
                return Result.Failure<RecordResponse>(Error.BadRequest("This trek is completed and can no longer be modified."));

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

            return Result.Success(new RecordResponse
            {
                TrekId = trip.Id,
                TrekNumber = trip.TrekNumber,
                Status = trip.Status.ToString(),
                Recorded = recorded
            });
        }
    }
}

public class RecordTrekDeliveryEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/treks/{id:guid}/record", async (Guid id, RecordTrekDelivery.Command command, ISender sender) =>
        {
            command.TrekId = id;
            var result = await sender.Send(command);
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Record delivery results for a trek (admin)")
        .WithDescription("Allows an admin to enter delivery quantities, payment method, amount paid, and balance for each product in the trek. Rejected if the trek is already Completed.")
        .Produces<RecordTrekDelivery.RecordResponse>(200)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
