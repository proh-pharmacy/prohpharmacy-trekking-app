using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Fleet.Entities;
using prohpharmacy_trekking_app.Features.Fleet.Enums;
using prohpharmacy_trekking_app.Services.Traccar;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Fleet.Devices;

public static class LinkTraccarDevice
{
    public class Command : IRequest<Result<CreateTrackingDevice.DeviceResponse>>
    {
        public int TraccarDeviceId { get; set; }
        public Guid VehicleId { get; set; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.TraccarDeviceId).GreaterThan(0);
            RuleFor(x => x.VehicleId).NotEmpty();
        }
    }

    internal sealed class Handler : IRequestHandler<Command, Result<CreateTrackingDevice.DeviceResponse>>
    {
        private readonly AppDbContext _db;
        private readonly IValidator<Command> _validator;
        private readonly ITraccarService _traccar;

        public Handler(AppDbContext db, IValidator<Command> validator, ITraccarService traccar)
        {
            _db = db;
            _validator = validator;
            _traccar = traccar;
        }

        public async Task<Result<CreateTrackingDevice.DeviceResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var validation = await _validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Result.Failure<CreateTrackingDevice.DeviceResponse>(Error.ValidationError(validation));

            var traccarDevice = await _traccar.GetDeviceAsync(request.TraccarDeviceId, cancellationToken);
            if (traccarDevice is null)
                return Result.Failure<CreateTrackingDevice.DeviceResponse>(
                    Error.CreateNotFoundError($"Traccar device #{request.TraccarDeviceId} not found."));

            var alreadyLinked = await _db.TrackingDevices
                .AnyAsync(d => d.TraccarDeviceId == request.TraccarDeviceId, cancellationToken);
            if (alreadyLinked)
                return Result.Failure<CreateTrackingDevice.DeviceResponse>(
                    Error.Conflict($"Traccar device #{request.TraccarDeviceId} is already linked to a backend vehicle."));

            var vehicle = await _db.Vehicles
                .Include(v => v.StaffAssignments.Where(a => a.UnassignedAt == null))
                    .ThenInclude(a => a.StaffMember)
                .FirstOrDefaultAsync(v => v.Id == request.VehicleId, cancellationToken);
            if (vehicle is null)
                return Result.Failure<CreateTrackingDevice.DeviceResponse>(
                    Error.CreateNotFoundError("Vehicle not found."));

            var alreadyHasDevice = await _db.TrackingDevices
                .AnyAsync(d => d.VehicleId == request.VehicleId, cancellationToken);
            if (alreadyHasDevice)
                return Result.Failure<CreateTrackingDevice.DeviceResponse>(
                    Error.Conflict("This vehicle already has a tracking device registered."));

            var staff = vehicle.StaffAssignments.FirstOrDefault()?.StaffMember;

            var device = new TrackingDevice
            {
                TraccarDeviceId = traccarDevice.Id,
                TraccarUniqueId = traccarDevice.UniqueId,
                Name = traccarDevice.Name,
                Status = TrackingDeviceStatus.Active,
                VehicleId = vehicle.Id,
                StaffMemberId = staff?.Id,
                CreatedAt = DateTime.UtcNow
            };

            _db.TrackingDevices.Add(device);
            await _db.SaveChangesAsync(cancellationToken);

            return Result.Success(CreateTrackingDevice.Handler.ToResponse(
                device, vehicle.RegistrationNumber, staff?.FullName));
        }
    }
}

public class LinkTraccarDeviceEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/fleet/devices/link-traccar", async (
            LinkTraccarDevice.Command command,
            ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Created($"api/v1/fleet/devices/{result.Value.Id}", result.Value);
        })
        .WithTags("Fleet")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Fleet)
        .WithSummary("Link an existing Traccar device to a vehicle")
        .WithDescription(
            "Links a device already registered in Traccar to a backend vehicle without creating a " +
            "duplicate. Use this for hardware GPS trackers or devices previously set up directly in " +
            "the Traccar dashboard. Call `GET api/v1/fleet/devices/traccar` first to find the " +
            "`traccarDeviceId` of the unlinked device.")
        .Produces<CreateTrackingDevice.DeviceResponse>(201)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
