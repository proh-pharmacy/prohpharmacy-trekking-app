using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Trekking.Entities;
using prohpharmacy_trekking_app.Shared;
using static prohpharmacy_trekking_app.Features.Trekking.CreateTrek;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class AddTrekStop
{
    public class Command : IRequest<Result<TrekStopResponse>>
    {
        public Guid TrekId { get; set; }
        public Guid CustomerAccountId { get; set; }
        public int Sequence { get; set; }
        public string? Notes { get; set; }
        public List<StopProductInput> Products { get; set; } = [];
    }

    public class StopProductInput
    {
        public Guid ProductId { get; set; }
        public decimal PlannedQuantity { get; set; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.CustomerAccountId).NotEmpty();
            RuleFor(x => x.Sequence).GreaterThan(0);
            RuleFor(x => x.Notes).MaximumLength(500).When(x => x.Notes is not null);
            RuleForEach(x => x.Products).ChildRules(p =>
            {
                p.RuleFor(x => x.ProductId).NotEmpty();
                p.RuleFor(x => x.PlannedQuantity).GreaterThan(0);
            });
        }
    }

    internal sealed class Handler : IRequestHandler<Command, Result<TrekStopResponse>>
    {
        private readonly AppDbContext _db;
        private readonly IValidator<Command> _validator;

        public Handler(AppDbContext db, IValidator<Command> validator)
        {
            _db = db;
            _validator = validator;
        }

        public async Task<Result<TrekStopResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var validation = await _validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Result.Failure<TrekStopResponse>(Error.ValidationError(validation));

            var trip = await _db.TrekkingTrips.FindAsync([request.TrekId], cancellationToken);
            if (trip is null)
                return Result.Failure<TrekStopResponse>(Error.CreateNotFoundError("Trekking trip not found."));

            var customer = await _db.CustomerAccounts
                .Include(ca => ca.Locations.Where(l => l.IsPrimary))
                .FirstOrDefaultAsync(ca => ca.Id == request.CustomerAccountId, cancellationToken);
            if (customer is null)
                return Result.Failure<TrekStopResponse>(Error.CreateNotFoundError("Customer account not found."));

            var productIds = request.Products.Select(p => p.ProductId).Distinct().ToList();
            var products = await _db.Products
                .Where(p => productIds.Contains(p.Id))
                .ToListAsync(cancellationToken);

            var missingProductId = productIds.FirstOrDefault(id => products.All(p => p.Id != id));
            if (missingProductId != Guid.Empty)
                return Result.Failure<TrekStopResponse>(Error.CreateNotFoundError($"Product {missingProductId} not found."));

            var stop = new TrekkingTripStop
            {
                TrekkingTripId = request.TrekId,
                CustomerAccountId = request.CustomerAccountId,
                Sequence = request.Sequence,
                Notes = request.Notes?.Trim(),
                Products = request.Products.Select(p => new TrekkingTripStopProduct
                {
                    ProductId = p.ProductId,
                    PlannedQuantity = p.PlannedQuantity
                }).ToList()
            };

            _db.TrekkingTripStops.Add(stop);
            await _db.SaveChangesAsync(cancellationToken);

            var productDict = products.ToDictionary(p => p.Id);
            var primaryLocation = customer.Locations.FirstOrDefault();

            var response = new TrekStopResponse
            {
                StopId = stop.Id,
                Sequence = stop.Sequence,
                CustomerAccountId = stop.CustomerAccountId,
                CustomerName = customer.BusinessName,
                CustomerCode = customer.CustomerCode,
                PrimaryLocationLandmark = primaryLocation?.LandmarkAndDirections,
                PrimaryLocationStreet = primaryLocation?.StreetAddress,
                Notes = stop.Notes,
                Products = stop.Products.Select(p => new TrekStopProductResponse
                {
                    ProductId = p.ProductId,
                    ProductName = productDict.TryGetValue(p.ProductId, out var prod) ? prod.Name : string.Empty,
                    Unit = productDict.TryGetValue(p.ProductId, out var prod2) ? prod2.Unit : null,
                    PlannedQuantity = p.PlannedQuantity
                }).ToList()
            };

            return Result.Success(response);
        }
    }
}

public class AddTrekStopEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/treks/{trekId:guid}/stops", async (Guid trekId, AddTrekStop.Command command, ISender sender) =>
        {
            command.TrekId = trekId;
            var result = await sender.Send(command);
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Created($"api/v1/treks/{trekId}/stops/{result.Value.StopId}", result.Value);
        })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Add a stop to a trekking trip")
        .Produces<TrekStopResponse>(201)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
