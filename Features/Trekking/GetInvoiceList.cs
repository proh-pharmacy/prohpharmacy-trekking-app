using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;
using prohpharmacy_trekking_app.Utilities;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class GetInvoiceList
{
    public class Query : IRequest<Result<object>>
    {
        public string? Search { get; set; }
        public string? Sort { get; set; }
        public int? PageNumber { get; set; }
        public int? PageSize { get; set; }
        public Guid? CustomerId { get; set; }
        public Guid? TrekId { get; set; }
        public string? Status { get; set; }
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
    }

    internal sealed class Handler(AppDbContext db) : IRequestHandler<Query, Result<object>>
    {
        public async Task<Result<object>> Handle(Query request, CancellationToken cancellationToken)
        {
            var query = db.SaleInvoices
                .Include(i => i.Stop)
                    .ThenInclude(s => s.CustomerAccount)
                .Include(i => i.Stop)
                    .ThenInclude(s => s.TrekkingTrip)
                .AsNoTracking();

            if (request.CustomerId.HasValue)
                query = query.Where(i => i.CustomerAccountId == request.CustomerId.Value);

            if (request.TrekId.HasValue)
                query = query.Where(i => i.TrekkingTripId == request.TrekId.Value);

            if (!string.IsNullOrWhiteSpace(request.Status))
                query = query.Where(i => i.Status.ToString().ToLower() == request.Status.ToLower());

            if (request.DateFrom.HasValue)
                query = query.Where(i => i.IssuedAt >= request.DateFrom.Value);

            if (request.DateTo.HasValue)
                query = query.Where(i => i.IssuedAt <= request.DateTo.Value);

            var result = await new QueryBuilder<Entities.SaleInvoice>(query)
                .WithSearch(request.Search, nameof(Entities.SaleInvoice.InvoiceNumber))
                .WithSort(request.Sort)
                .Paginate(request.PageNumber, request.PageSize)
                .BuildAsync(i => (object)GetInvoice.Handler.ToResponse(i));

            return Result.Success(result);
        }
    }
}

public class GetInvoiceListEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/invoices", async (
            ISender sender,
            [FromQuery] string? search,
            [FromQuery] string? sort,
            [FromQuery] int? pageNumber,
            [FromQuery] int? pageSize,
            [FromQuery] Guid? customerId,
            [FromQuery] Guid? trekId,
            [FromQuery] string? status,
            [FromQuery] DateTime? dateFrom,
            [FromQuery] DateTime? dateTo) =>
        {
            var result = await sender.Send(new GetInvoiceList.Query
            {
                Search = search,
                Sort = sort,
                PageNumber = pageNumber,
                PageSize = pageSize,
                CustomerId = customerId,
                TrekId = trekId,
                Status = status,
                DateFrom = dateFrom,
                DateTo = dateTo
            });
            return result.IsFailure ? Results.BadRequest(result.Error) : Results.Ok(result.Value);
        })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("List sale invoices")
        .Produces<Paginator.PaginatedData<GetInvoice.InvoiceResponse>>(200)
        .RequireAuthorization();
    }
}
