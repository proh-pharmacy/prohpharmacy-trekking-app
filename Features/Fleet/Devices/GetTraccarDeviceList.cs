using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Services.Traccar;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Fleet.Devices;

public static class GetTraccarDeviceList
{
    public class Query : IRequest<Result<List<TraccarDeviceItem>>> { }

    public class TraccarDeviceItem
    {
        public int TraccarDeviceId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string UniqueId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime? LastUpdate { get; set; }
        public bool Disabled { get; set; }
        public bool IsLinked { get; set; }
        public Guid? BackendDeviceId { get; set; }
        public Guid? VehicleId { get; set; }
        public string? VehicleRegistration { get; set; }
        public string? StaffName { get; set; }
    }

    internal sealed class Handler : IRequestHandler<Query, Result<List<TraccarDeviceItem>>>
    {
        private readonly AppDbContext _db;
        private readonly ITraccarService _traccar;

        public Handler(AppDbContext db, ITraccarService traccar)
        {
            _db = db;
            _traccar = traccar;
        }

        public async Task<Result<List<TraccarDeviceItem>>> Handle(Query request, CancellationToken cancellationToken)
        {
            var traccarDevices = await _traccar.GetAllDevicesAsync(cancellationToken);

            var linkedDevices = await _db.TrackingDevices
                .Include(d => d.Vehicle)
                .Include(d => d.StaffMember)
                .Where(d => d.TraccarDeviceId.HasValue)
                .ToListAsync(cancellationToken);

            var linkedByTraccarId = linkedDevices
                .ToDictionary(d => d.TraccarDeviceId!.Value);

            var items = traccarDevices.Select(t =>
            {
                linkedByTraccarId.TryGetValue(t.Id, out var backend);
                return new TraccarDeviceItem
                {
                    TraccarDeviceId = t.Id,
                    Name = t.Name,
                    UniqueId = t.UniqueId,
                    Status = t.Status,
                    LastUpdate = t.LastUpdate,
                    Disabled = t.Disabled,
                    IsLinked = backend is not null,
                    BackendDeviceId = backend?.Id,
                    VehicleId = backend?.VehicleId,
                    VehicleRegistration = backend?.Vehicle?.RegistrationNumber,
                    StaffName = backend?.StaffMember?.FullName
                };
            }).ToList();

            return Result.Success(items);
        }
    }
}

public class GetTraccarDeviceListEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/fleet/devices/traccar", async (ISender sender) =>
        {
            var result = await sender.Send(new GetTraccarDeviceList.Query());
            return result.IsFailure
                ? Results.BadRequest(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Fleet")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Fleet)
        .WithSummary("List all Traccar devices")
        .WithDescription(
            "Returns all devices registered in Traccar with their backend link status. " +
            "Use `isLinked: false` entries to identify unlinked Traccar devices, then call " +
            "`POST api/v1/fleet/devices/link-traccar` to link them to a vehicle.")
        .Produces<List<GetTraccarDeviceList.TraccarDeviceItem>>(200)
        .RequireAuthorization();
    }
}
