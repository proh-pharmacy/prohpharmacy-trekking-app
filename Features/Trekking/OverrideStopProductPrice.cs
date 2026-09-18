using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Trekking.Enums;
using prohpharmacy_trekking_app.Shared;
using static prohpharmacy_trekking_app.Features.Trekking.CreateTrek;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class OverrideStopProductPrice
{
    public class Command : IRequest<Result<TrekStopProductResponse>>
    {
        public Guid TrekId { get; set; }
        public Guid StopId { get; set; }
        public Guid StopProductId { get; set; }
        public decimal BasicUnitPrice { get; set; }
        public decimal? PackagingUnitPrice { get; set; }
    }

    internal sealed class Handler(AppDbContext db)
        : IRequestHandler<Command, Result<TrekStopProductResponse>>
    {
        public async Task<Result<TrekStopProductResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var trip = await db.TrekkingTrips
                .Include(t => t.Stops)
                    .ThenInclude(s => s.Products)
                        .ThenInclude(p => p.Product)
                            .ThenInclude(p => p.BasicUnit)
                .Include(t => t.Stops)
                    .ThenInclude(s => s.Products)
                        .ThenInclude(p => p.Product)
                            .ThenInclude(p => p.PackagingUnit)
                .FirstOrDefaultAsync(t => t.Id == request.TrekId, cancellationToken);

            if (trip is null)
                return Result.Failure<TrekStopProductResponse>(Error.CreateNotFoundError("Trekking trip not found."));

            if (trip.Status == TrekStatus.Completed)
                return Result.Failure<TrekStopProductResponse>(Error.BadRequest("Prices cannot be changed on a completed trek."));

            var stop = trip.Stops.FirstOrDefault(s => s.Id == request.StopId);
            if (stop is null)
                return Result.Failure<TrekStopProductResponse>(Error.CreateNotFoundError("Stop not found on this trek."));

            var product = stop.Products.FirstOrDefault(p => p.Id == request.StopProductId);
            if (product is null)
                return Result.Failure<TrekStopProductResponse>(Error.CreateNotFoundError("Stop product not found."));

            product.BasicUnitPrice = request.BasicUnitPrice;
            product.PackagingUnitPrice = request.PackagingUnitPrice;
            product.AmountDue = product.PlannedBasicQuantity * request.BasicUnitPrice
                              + (product.PlannedPackagingQuantity ?? 0) * (request.PackagingUnitPrice ?? 0);

            trip.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            return Result.Success(new TrekStopProductResponse
            {
                StopProductId = product.Id,
                ProductId = product.ProductId,
                ProductName = product.Product?.Name ?? string.Empty,
                BasicUnitName = product.Product?.BasicUnit?.Name,
                PackagingUnitName = product.Product?.PackagingUnit?.Name,
                BasicUnitPrice = product.BasicUnitPrice,
                PackagingUnitPrice = product.PackagingUnitPrice,
                PlannedBasicQuantity = product.PlannedBasicQuantity,
                PlannedPackagingQuantity = product.PlannedPackagingQuantity,
                BasicQtyDelivered = product.BasicQtyDelivered,
                PackagingQtyDelivered = product.PackagingQtyDelivered,
                AmountDue = product.AmountDue,
                PaymentMethod = product.PaymentMethod?.ToString(),
                AmtPaid = product.AmtPaid,
                Balance = product.Balance,
                IsUnplanned = product.IsUnplanned,
                Notes = product.Notes,
                DeliveredAt = product.DeliveredAt
            });
        }
    }
}

public class OverrideStopProductPriceEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPatch("api/v1/treks/{trekId:guid}/stops/{stopId:guid}/products/{stopProductId:guid}/price",
            async (Guid trekId, Guid stopId, Guid stopProductId, OverrideStopProductPrice.Command command, ISender sender) =>
            {
                command.TrekId = trekId;
                command.StopId = stopId;
                command.StopProductId = stopProductId;
                var result = await sender.Send(command);
                return result.IsFailure
                    ? Results.UnprocessableEntity(result.Error)
                    : Results.Ok(result.Value);
            })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Override price for a stop product")
        .WithDescription(
            "Corrects the snapshotted BasicUnitPrice and/or PackagingUnitPrice on a specific stop product. " +
            "AmountDue is recalculated from planned quantities × new prices. " +
            "Not allowed once the trek is Completed.")
        .Produces<TrekStopProductResponse>(200)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
