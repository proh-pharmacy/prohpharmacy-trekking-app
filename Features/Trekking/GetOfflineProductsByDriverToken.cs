using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class GetOfflineProductsByDriverToken
{
    public class Query : IRequest<Result<List<OfflineProductItem>>>
    {
        public Guid Token { get; set; }
        public DateTime? Since { get; set; }
    }

    public class OfflineProductItem
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public decimal BasicUnitPrice { get; set; }
        public decimal? PackagingUnitPrice { get; set; }
        public string BasicUnitName { get; set; } = string.Empty;
        public Guid BasicUnitId { get; set; }
        public string? PackagingUnitName { get; set; }
        public Guid? PackagingUnitId { get; set; }
        public bool IsActive { get; set; }
    }

    internal sealed class Handler(AppDbContext db) : IRequestHandler<Query, Result<List<OfflineProductItem>>>
    {
        public async Task<Result<List<OfflineProductItem>>> Handle(Query request, CancellationToken cancellationToken)
        {
            var tripExists = await db.TrekkingTrips
                .AnyAsync(t => t.DriverToken == request.Token, cancellationToken);
            if (!tripExists)
                return Result.Failure<List<OfflineProductItem>>(Error.CreateNotFoundError("Trek not found. The token may be invalid."));

            var query = db.Products
                .Include(p => p.BasicUnit)
                .Include(p => p.PackagingUnit)
                .AsNoTracking();

            if (request.Since.HasValue)
                query = query.Where(p => p.CreatedAt >= request.Since.Value || (p.UpdatedAt.HasValue && p.UpdatedAt >= request.Since.Value));

            var products = await query.ToListAsync(cancellationToken);

            return Result.Success(products.Select(p => new OfflineProductItem
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                BasicUnitPrice = p.BasicUnitPrice,
                PackagingUnitPrice = p.PackagingUnitPrice,
                BasicUnitName = p.BasicUnit?.Name ?? string.Empty,
                BasicUnitId = p.BasicUnitId,
                PackagingUnitName = p.PackagingUnit?.Name,
                PackagingUnitId = p.PackagingUnitId,
                IsActive = p.IsActive
            }).ToList());
        }
    }
}

public class GetOfflineProductsByDriverTokenEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/treks/driver/{token:guid}/offline/products",
            async (Guid token, DateTime? since, ISender sender) =>
            {
                var result = await sender.Send(new GetOfflineProductsByDriverToken.Query { Token = token, Since = since });
                return result.IsFailure
                    ? Results.NotFound(result.Error)
                    : Results.Ok(result.Value);
            })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Get product catalogue for offline use (driver portal)")
        .WithDescription("Returns full product catalogue. Pass ?since=ISO8601 to get only records modified after that timestamp.")
        .Produces<List<GetOfflineProductsByDriverToken.OfflineProductItem>>(200)
        .Produces<Error>(404)
        .AllowAnonymous();
    }
}
