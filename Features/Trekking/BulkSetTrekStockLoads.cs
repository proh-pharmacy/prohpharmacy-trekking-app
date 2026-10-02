using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Trekking.Entities;
using prohpharmacy_trekking_app.Features.Trekking.Enums;
using prohpharmacy_trekking_app.Providers;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class BulkSetTrekStockLoads
{
    public class StockLoadLineItem
    {
        public Guid ProductId { get; set; }
        public decimal BasicQty { get; set; }
        public decimal PackagingQty { get; set; }
    }

    public class Command : IRequest<Result<List<GetTrekStockLoads.StockLoadItem>>>
    {
        public Guid TrekId { get; set; }
        public List<StockLoadLineItem> Items { get; set; } = [];
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Items).NotEmpty().WithMessage("At least one product is required.");
            RuleForEach(x => x.Items).ChildRules(item =>
            {
                item.RuleFor(i => i.ProductId).NotEmpty();
                item.RuleFor(i => i.BasicQty).GreaterThanOrEqualTo(0);
                item.RuleFor(i => i.PackagingQty).GreaterThanOrEqualTo(0);
            });
        }
    }

    internal sealed class Handler(AppDbContext db, IValidator<Command> validator, AuthProvider auth)
        : IRequestHandler<Command, Result<List<GetTrekStockLoads.StockLoadItem>>>
    {
        public async Task<Result<List<GetTrekStockLoads.StockLoadItem>>> Handle(Command request, CancellationToken cancellationToken)
        {
            var validation = await validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Result.Failure<List<GetTrekStockLoads.StockLoadItem>>(Error.ValidationError(validation));

            var trip = await db.TrekkingTrips.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == request.TrekId, cancellationToken);

            if (trip is null)
                return Result.Failure<List<GetTrekStockLoads.StockLoadItem>>(Error.CreateNotFoundError("Trek not found."));

            if (trip.Status == TrekStatus.Completed || trip.Status == TrekStatus.Cancelled)
                return Result.Failure<List<GetTrekStockLoads.StockLoadItem>>(
                    Error.BadRequest($"Cannot modify stock loads on a {trip.Status} trek."));

            if (!Guid.TryParse(auth.GetStaffId(), out var staffId))
                return Result.Failure<List<GetTrekStockLoads.StockLoadItem>>(
                    Error.BadRequest("Authenticated staff identity is missing or invalid."));

            var productIds = request.Items.Select(i => i.ProductId).Distinct().ToList();

            var existing = await db.TrekStockLoads
                .Where(l => l.TrekkingTripId == request.TrekId && productIds.Contains(l.ProductId))
                .ToListAsync(cancellationToken);

            var now = DateTime.UtcNow;

            foreach (var item in request.Items)
            {
                var load = existing.FirstOrDefault(l => l.ProductId == item.ProductId);

                if (load is null)
                {
                    db.TrekStockLoads.Add(new TrekStockLoad
                    {
                        TrekkingTripId = request.TrekId,
                        VehicleId = trip.VehicleId,
                        ProductId = item.ProductId,
                        BasicQuantityLoaded = item.BasicQty,
                        PackagingQuantityLoaded = item.PackagingQty,
                        LoadedByStaffId = staffId,
                        LoadedAt = now
                    });
                }
                else
                {
                    load.BasicQuantityLoaded = item.BasicQty;
                    load.PackagingQuantityLoaded = item.PackagingQty;
                    load.LoadedByStaffId = staffId;
                    load.LoadedAt = now;
                }
            }

            await db.SaveChangesAsync(cancellationToken);

            var updatedLoads = await db.TrekStockLoads
                .Include(l => l.Product)
                    .ThenInclude(p => p.BasicUnit)
                .Include(l => l.Product)
                    .ThenInclude(p => p.PackagingUnit)
                .Include(l => l.LoadedBy)
                .Where(l => l.TrekkingTripId == request.TrekId && productIds.Contains(l.ProductId))
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var vehicleStock = await db.VehicleProductStocks
                .Where(s => s.VehicleId == trip.VehicleId && productIds.Contains(s.ProductId))
                .AsNoTracking()
                .ToDictionaryAsync(s => s.ProductId, cancellationToken);

            return Result.Success(updatedLoads.Select(l =>
            {
                vehicleStock.TryGetValue(l.ProductId, out var stock);
                return new GetTrekStockLoads.StockLoadItem
                {
                    Id = l.Id,
                    ProductId = l.ProductId,
                    ProductName = l.Product.Name,
                    BasicUnitId = l.Product.BasicUnitId,
                    BasicUnitName = l.Product.BasicUnit.Name,
                    PackagingUnitId = l.Product.PackagingUnitId,
                    PackagingUnitName = l.Product.PackagingUnit?.Name,
                    BasicQuantityLoaded = l.BasicQuantityLoaded,
                    PackagingQuantityLoaded = l.PackagingQuantityLoaded,
                    VehicleBasicOnHand = stock?.BasicQuantityOnHand,
                    VehiclePackagingOnHand = stock?.PackagingQuantityOnHand,
                    ExceedsVehicleStock = stock is not null &&
                        (l.BasicQuantityLoaded > stock.BasicQuantityOnHand ||
                         l.PackagingQuantityLoaded > stock.PackagingQuantityOnHand),
                    LoadedBy = l.LoadedBy.FullName,
                    LoadedAt = l.LoadedAt
                };
            }).ToList());
        }
    }
}

public class BulkSetTrekStockLoadsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/treks/{trekId:guid}/stock-loads",
            async (Guid trekId, BulkSetTrekStockLoads.Command command, ISender sender) =>
            {
                command.TrekId = trekId;
                var result = await sender.Send(command);
                return result.IsFailure
                    ? Results.UnprocessableEntity(result.Error)
                    : Results.Ok(result.Value);
            })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Bulk add or update trek stock loads")
        .WithDescription("Upserts product load records for the trek. Existing products are updated; new ones are created. Run the /check endpoint first to surface any stock warnings before calling this.")
        .Produces<List<GetTrekStockLoads.StockLoadItem>>(200)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
