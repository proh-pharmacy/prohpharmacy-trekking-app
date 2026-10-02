using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;
using prohpharmacy_trekking_app.Utilities;
using static prohpharmacy_trekking_app.Features.Products.CreateProduct;

namespace prohpharmacy_trekking_app.Features.Products;

public static class GetProductList
{
    public class Query : IRequest<Result<object>>
    {
        public string? Search { get; set; }
        public string? Sort { get; set; }
        public int? PageNumber { get; set; }
        public int? PageSize { get; set; }
        public bool? IsActive { get; set; }
        public Guid? VehicleId { get; set; }
        public bool InStockOnly { get; set; }
        public bool ExcludeVehicleStock { get; set; }
    }

    public class ProductListResponse
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public Guid BasicUnitId { get; set; }
        public string BasicUnitName { get; set; } = string.Empty;
        public decimal BasicUnitPrice { get; set; }
        public Guid? PackagingUnitId { get; set; }
        public string? PackagingUnitName { get; set; }
        public decimal? PackagingUnitPrice { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public VehicleStockSummary? VehicleStock { get; set; }
    }

    public class VehicleStockSummary
    {
        public Guid StockId { get; set; }
        public decimal BasicQuantityOnHand { get; set; }
        public decimal PackagingQuantityOnHand { get; set; }
        public decimal? LowStockThreshold { get; set; }
        public bool IsLowStock { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    internal sealed class Handler : IRequestHandler<Query, Result<object>>
    {
        private readonly AppDbContext _db;

        public Handler(AppDbContext db) => _db = db;

        public async Task<Result<object>> Handle(Query request, CancellationToken cancellationToken)
        {
            if (request.InStockOnly && !request.VehicleId.HasValue)
                return Result.Failure<object>(Error.BadRequest("vehicleId is required when inStockOnly is true."));
            if (request.ExcludeVehicleStock && !request.VehicleId.HasValue)
                return Result.Failure<object>(Error.BadRequest("vehicleId is required when excludeVehicleStock is true."));
            if (request.ExcludeVehicleStock && request.InStockOnly)
                return Result.Failure<object>(Error.BadRequest("excludeVehicleStock and inStockOnly cannot both be true."));

            Dictionary<Guid, Features.Fleet.Entities.VehicleProductStock>? vehicleStock = null;
            if (request.VehicleId.HasValue)
            {
                var vehicleExists = await _db.Vehicles.AsNoTracking()
                    .AnyAsync(v => v.Id == request.VehicleId.Value, cancellationToken);
                if (!vehicleExists)
                    return Result.Failure<object>(Error.CreateNotFoundError("Vehicle not found."));

                if (!request.ExcludeVehicleStock)
                {
                    vehicleStock = await _db.VehicleProductStocks.AsNoTracking()
                        .Where(s => s.VehicleId == request.VehicleId.Value)
                        .ToDictionaryAsync(s => s.ProductId, cancellationToken);
                }
            }

            var query = _db.Products
                .Include(p => p.BasicUnit)
                .Include(p => p.PackagingUnit)
                .AsNoTracking();

            if (request.IsActive.HasValue)
                query = query.Where(p => p.IsActive == request.IsActive.Value);

            if (request.VehicleId.HasValue)
            {
                var vehicleId = request.VehicleId.Value;
                query = request.ExcludeVehicleStock
                    ? query.Where(p => !_db.VehicleProductStocks.Any(s =>
                        s.VehicleId == vehicleId && s.ProductId == p.Id))
                    : query.Where(p => _db.VehicleProductStocks.Any(s =>
                        s.VehicleId == vehicleId &&
                        s.ProductId == p.Id &&
                        (!request.InStockOnly || s.BasicQuantityOnHand > 0 || s.PackagingQuantityOnHand > 0)));
            }

            var result = await new QueryBuilder<Entities.Product>(query)
                .WithSearch(request.Search, nameof(Entities.Product.Name))
                .WithSort(request.Sort ?? "createdAt_desc")
                .Paginate(request.PageNumber, request.PageSize)
                .BuildAsync(p =>
                {
                    Features.Fleet.Entities.VehicleProductStock? stock = null;
                    vehicleStock?.TryGetValue(p.Id, out stock);
                    return (object)new ProductListResponse
                    {
                        Id = p.Id,
                        Name = p.Name,
                        Description = p.Description,
                        BasicUnitId = p.BasicUnitId,
                        BasicUnitName = p.BasicUnit.Name,
                        BasicUnitPrice = p.BasicUnitPrice,
                        PackagingUnitId = p.PackagingUnitId,
                        PackagingUnitName = p.PackagingUnit?.Name,
                        PackagingUnitPrice = p.PackagingUnitPrice,
                        IsActive = p.IsActive,
                        CreatedAt = p.CreatedAt,
                        UpdatedAt = p.UpdatedAt,
                        VehicleStock = stock is null ? null : new VehicleStockSummary
                        {
                            StockId = stock.Id,
                            BasicQuantityOnHand = stock.BasicQuantityOnHand,
                            PackagingQuantityOnHand = stock.PackagingQuantityOnHand,
                            LowStockThreshold = stock.LowStockThreshold,
                            IsLowStock = stock.LowStockThreshold.HasValue &&
                                stock.BasicQuantityOnHand <= stock.LowStockThreshold.Value,
                            UpdatedAt = stock.UpdatedAt
                        }
                    };
                });

            return Result.Success(result);
        }
    }
}

public class GetProductListEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/products", async (
            ISender sender,
            [FromQuery] string? search,
            [FromQuery] string? sort,
            [FromQuery] int? pageNumber,
            [FromQuery] int? pageSize,
            [FromQuery] bool? isActive,
            [FromQuery] Guid? vehicleId,
            [FromQuery] bool inStockOnly = false,
            [FromQuery] bool excludeVehicleStock = false) =>
        {
            var result = await sender.Send(new GetProductList.Query
            {
                Search = search,
                Sort = sort,
                PageNumber = pageNumber,
                PageSize = pageSize,
                IsActive = isActive,
                VehicleId = vehicleId,
                InStockOnly = inStockOnly,
                ExcludeVehicleStock = excludeVehicleStock
            });

            return result.IsFailure
                ? result.Error.Code == "404" ? Results.NotFound(result.Error) : Results.BadRequest(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Products")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.General)
        .WithSummary("List / search products")
        .WithDescription("Optionally filter by vehicleId. With vehicleId, only products tracked on that vehicle are returned and each item includes vehicleStock. Add inStockOnly=true to require a positive balance, or excludeVehicleStock=true to return catalogue products not tracked on the vehicle.")
        .Produces<Paginator.PaginatedData<GetProductList.ProductListResponse>>(200)
        .Produces<Error>(400)
        .Produces<Error>(404)
        .RequireAuthorization();
    }
}
