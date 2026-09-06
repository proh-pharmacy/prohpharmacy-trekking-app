using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Fleet.Entities;
using prohpharmacy_trekking_app.Shared;
using prohpharmacy_trekking_app.Utilities;
using static prohpharmacy_trekking_app.Features.Fleet.Devices.CreateTrackingDevice;

namespace prohpharmacy_trekking_app.Features.Fleet.Devices;

public static class GetTrackingDeviceList
{
    public class Query : IRequest<Result<object>>
    {
        public string? Status { get; set; }
        public string? Search { get; set; }
        public string? Sort { get; set; }
        public int? PageNumber { get; set; }
        public int? PageSize { get; set; }
    }

    internal sealed class Handler : IRequestHandler<Query, Result<object>>
    {
        private readonly AppDbContext _db;

        public Handler(AppDbContext db) => _db = db;

        public async Task<Result<object>> Handle(Query request, CancellationToken cancellationToken)
        {
            var query = _db.TrackingDevices
                .Include(d => d.Assignments.Where(a => a.UnassignedAt == null))
                    .ThenInclude(a => a.StaffMember)
                .AsNoTracking();

            if (!string.IsNullOrWhiteSpace(request.Status))
                query = query.Where(d => d.Status.ToString().ToLower() == request.Status.ToLower());

            var result = await new QueryBuilder<TrackingDevice>(query)
                .WithSearch(request.Search,
                    nameof(TrackingDevice.Name),
                    nameof(TrackingDevice.TraccarUniqueId),
                    nameof(TrackingDevice.PhoneNumber))
                .WithSort(request.Sort)
                .Paginate(request.PageNumber, request.PageSize)
                .BuildAsync(d =>
                {
                    var active = d.Assignments.FirstOrDefault();
                    return (object)CreateTrackingDevice.Handler.ToResponse(
                        d,
                        active?.StaffMemberId,
                        active?.StaffMember?.FullName);
                });

            return Result.Success(result);
        }
    }
}

public class GetTrackingDeviceListEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/fleet/devices", async (
            ISender sender,
            [FromQuery] string? status,
            [FromQuery] string? search,
            [FromQuery] string? sort,
            [FromQuery] int? pageNumber,
            [FromQuery] int? pageSize) =>
        {
            var result = await sender.Send(new GetTrackingDeviceList.Query
            {
                Status = status,
                Search = search,
                Sort = sort,
                PageNumber = pageNumber,
                PageSize = pageSize
            });

            return result.IsFailure
                ? Results.BadRequest(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Fleet")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Fleet)
        .WithSummary("List / search tracking devices")
        .WithDescription("Filter by status (Active | Inactive).")
        .Produces<Paginator.PaginatedData<DeviceResponse>>(200)
        .RequireAuthorization();
    }
}
