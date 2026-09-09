using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;
using static prohpharmacy_trekking_app.Features.Fleet.Devices.CreateTrackingDevice;

namespace prohpharmacy_trekking_app.Features.Fleet.Devices;

public static class GetTrackingDevice
{
    public class Query : IRequest<Result<DeviceResponse>>
    {
        public Guid Id { get; set; }
    }

    internal sealed class Handler(AppDbContext db) : IRequestHandler<Query, Result<DeviceResponse>>
    {
        public async Task<Result<DeviceResponse>> Handle(Query request, CancellationToken cancellationToken)
        {
            var device = await db.TrackingDevices
                .Include(d => d.Vehicle)
                .Include(d => d.StaffMember)
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == request.Id, cancellationToken);

            if (device is null)
                return Result.Failure<DeviceResponse>(Error.CreateNotFoundError("Tracking device not found."));

            return Result.Success(CreateTrackingDevice.Handler.ToResponse(
                device,
                device.Vehicle?.RegistrationNumber,
                device.StaffMember?.FullName));
        }
    }
}

public class GetTrackingDeviceEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/fleet/devices/{id:guid}", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new GetTrackingDevice.Query { Id = id });
            return result.IsFailure
                ? Results.NotFound(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Fleet")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Fleet)
        .WithSummary("Get a tracking device by ID")
        .Produces<DeviceResponse>(200)
        .Produces<Error>(404)
        .RequireAuthorization();
    }
}
