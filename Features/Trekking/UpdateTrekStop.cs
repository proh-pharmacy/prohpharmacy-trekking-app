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

public static class UpdateTrekStop
{
    public class Command : IRequest<Result<TrekStopResponse>>
    {
        public Guid TrekId { get; set; }
        public Guid StopId { get; set; }
        public Guid? CustomerAccountId { get; set; }
        public int? Sequence { get; set; }
        public string? Notes { get; set; }
        public List<AddTrekStop.StopProductInput>? Products { get; set; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Sequence).GreaterThan(0).When(x => x.Sequence.HasValue);
            RuleFor(x => x.Notes).MaximumLength(500).When(x => x.Notes is not null);
            When(x => x.Products is not null, () =>
            {
                RuleFor(x => x.Products).NotEmpty().WithMessage("Products list must not be empty when provided.");
                RuleForEach(x => x.Products).ChildRules(p =>
                {
                    p.RuleFor(x => x.ProductId).NotEmpty();
                    p.RuleFor(x => x.PlannedBasicQuantity).GreaterThanOrEqualTo(0).When(x => x.PlannedBasicQuantity.HasValue);
                    p.RuleFor(x => x.PlannedPackagingQuantity).GreaterThanOrEqualTo(0).When(x => x.PlannedPackagingQuantity.HasValue);
                    p.RuleFor(x => x)
                        .Must(x => (x.PlannedBasicQuantity.HasValue && x.PlannedBasicQuantity > 0) ||
                                   (x.PlannedPackagingQuantity.HasValue && x.PlannedPackagingQuantity > 0))
                        .WithMessage("At least one planned quantity must be greater than 0.");
                });
            });
        }
    }

    internal sealed class Handler(AppDbContext db, IValidator<Command> validator)
        : IRequestHandler<Command, Result<TrekStopResponse>>
    {
        public async Task<Result<TrekStopResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var validation = await validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Result.Failure<TrekStopResponse>(Error.ValidationError(validation));

            var stop = await db.TrekkingTripStops
                .Include(s => s.CustomerAccount)
                    .ThenInclude(ca => ca!.Region)
                .Include(s => s.CustomerAccount)
                    .ThenInclude(ca => ca!.Locations.Where(l => l.IsPrimary))
                        .ThenInclude(l => l.District)
                .Include(s => s.CustomerAccount)
                    .ThenInclude(ca => ca!.People.Where(p => p.IsPrimaryContact && p.IsActive))
                .Include(s => s.Products)
                    .ThenInclude(p => p.Product)
                        .ThenInclude(p => p.BasicUnit)
                .Include(s => s.Products)
                    .ThenInclude(p => p.Product)
                        .ThenInclude(p => p.PackagingUnit)
                .FirstOrDefaultAsync(s => s.Id == request.StopId && s.TrekkingTripId == request.TrekId, cancellationToken);

            if (stop is null)
                return Result.Failure<TrekStopResponse>(Error.CreateNotFoundError("Stop not found on this trek."));

            if (request.Sequence.HasValue)
                stop.Sequence = request.Sequence.Value;

            if (request.Notes is not null)
                stop.Notes = request.Notes.Trim();

            Customers.Entities.CustomerAccount? newCustomer = null;
            if (request.CustomerAccountId.HasValue && request.CustomerAccountId.Value != stop.CustomerAccountId)
            {
                newCustomer = await db.CustomerAccounts
                    .Include(ca => ca.Region)
                    .Include(ca => ca.Locations.Where(l => l.IsPrimary))
                        .ThenInclude(l => l.District)
                    .Include(ca => ca.People.Where(p => p.IsPrimaryContact && p.IsActive))
                    .FirstOrDefaultAsync(ca => ca.Id == request.CustomerAccountId.Value, cancellationToken);

                if (newCustomer is null)
                    return Result.Failure<TrekStopResponse>(Error.CreateNotFoundError("Customer account not found."));

                stop.CustomerAccountId = newCustomer.Id;
                db.TrekkingTripStopProducts.RemoveRange(stop.Products.ToList());
            }

            Dictionary<Guid, Products.Entities.Product> productDict = [];
            List<TrekkingTripStopProduct> productsForResponse;

            if (request.Products is not null)
            {
                var productIds = request.Products.Select(p => p.ProductId).Distinct().ToList();
                var products = await db.Products
                    .Include(p => p.BasicUnit)
                    .Include(p => p.PackagingUnit)
                    .Where(p => productIds.Contains(p.Id))
                    .ToListAsync(cancellationToken);

                var missingId = productIds.FirstOrDefault(id => products.All(p => p.Id != id));
                if (missingId != Guid.Empty)
                    return Result.Failure<TrekStopResponse>(Error.CreateNotFoundError($"Product {missingId} not found."));

                productDict = products.ToDictionary(p => p.Id);

                if (newCustomer is null)
                    db.TrekkingTripStopProducts.RemoveRange(stop.Products.ToList());

                var newProducts = request.Products.Select(input =>
                {
                    productDict.TryGetValue(input.ProductId, out var product);
                    var hasPackaging = product?.PackagingUnitId.HasValue ?? false;
                    var basicQty = input.PlannedBasicQuantity ?? 0;
                    var packagingQty = hasPackaging ? input.PlannedPackagingQuantity : null;
                    var basicPrice = product?.BasicUnitPrice ?? 0;
                    var packagingPrice = hasPackaging ? product?.PackagingUnitPrice : null;
                    return new TrekkingTripStopProduct
                    {
                        TrekkingTripStopId = stop.Id,
                        ProductId = input.ProductId,
                        PlannedBasicQuantity = basicQty,
                        PlannedPackagingQuantity = packagingQty,
                        BasicUnitPrice = basicPrice,
                        PackagingUnitPrice = packagingPrice,
                        AmountDue = basicQty * basicPrice + (packagingQty ?? 0) * (packagingPrice ?? 0)
                    };
                }).ToList();

                db.TrekkingTripStopProducts.AddRange(newProducts);
                productsForResponse = newProducts;
            }
            else if (newCustomer is not null)
            {
                productsForResponse = [];
            }
            else
            {
                foreach (var p in stop.Products)
                    productDict.TryAdd(p.ProductId, p.Product);
                productsForResponse = stop.Products.ToList();
            }

            await db.SaveChangesAsync(cancellationToken);

            var customer = newCustomer ?? stop.CustomerAccount;
            var primaryLocation = (newCustomer?.Locations ?? stop.CustomerAccount?.Locations)?.FirstOrDefault();
            var primaryContact = (newCustomer?.People ?? stop.CustomerAccount?.People)?.FirstOrDefault();

            return Result.Success(new TrekStopResponse
            {
                StopId = stop.Id,
                Sequence = stop.Sequence,
                CustomerAccountId = stop.CustomerAccountId,
                CustomerName = customer?.BusinessName ?? string.Empty,
                CustomerCode = customer?.CustomerCode ?? string.Empty,
                CustomerPhone = customer?.PrimaryPhoneNumber,
                CustomerType = customer?.CustomerType.ToString(),
                RegionName = customer?.Region?.Name,
                DistrictName = primaryLocation?.District?.Name,
                PrimaryLocationLandmark = primaryLocation?.LandmarkAndDirections,
                PrimaryLocationStreet = primaryLocation?.StreetAddress,
                PrimaryContactName = primaryContact?.FullName,
                PrimaryContactPhone = primaryContact?.PrimaryPhoneNumber,
                Notes = stop.Notes,
                Products = productsForResponse.Select(p =>
                {
                    productDict.TryGetValue(p.ProductId, out var prod);
                    return new TrekStopProductResponse
                    {
                        StopProductId = p.Id,
                        ProductId = p.ProductId,
                        ProductName = prod?.Name ?? string.Empty,
                        BasicUnitName = prod?.BasicUnit?.Name,
                        PackagingUnitName = prod?.PackagingUnit?.Name,
                        BasicUnitPrice = p.BasicUnitPrice,
                        PackagingUnitPrice = p.PackagingUnitPrice,
                        PlannedBasicQuantity = p.PlannedBasicQuantity,
                        PlannedPackagingQuantity = p.PlannedPackagingQuantity,
                        BasicQtyDelivered = p.BasicQtyDelivered,
                        PackagingQtyDelivered = p.PackagingQtyDelivered,
                        AmountDue = p.AmountDue,
                        PaymentMethod = p.PaymentMethod?.ToString(),
                        AmtPaid = p.AmtPaid,
                        Balance = p.Balance,
                        IsUnplanned = p.IsUnplanned,
                        Notes = p.Notes,
                        DeliveredAt = p.DeliveredAt
                    };
                }).ToList()
            });
        }
    }
}

public class UpdateTrekStopEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPatch("api/v1/treks/{trekId:guid}/stops/{stopId:guid}", async (
            Guid trekId,
            Guid stopId,
            UpdateTrekStop.Command command,
            ISender sender) =>
        {
            command.TrekId = trekId;
            command.StopId = stopId;
            var result = await sender.Send(command);
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Update a trek stop")
        .WithDescription("Update the sequence, notes, customer, or product list for an existing stop. If customerAccountId is provided and differs from the current customer, the customer is swapped and all existing products are cleared. If products is provided, it replaces the entire product list and re-snapshots prices. Omit products to leave them unchanged.")
        .Produces<TrekStopResponse>(200)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
