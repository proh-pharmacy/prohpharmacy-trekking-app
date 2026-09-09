using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Services.Traccar;
using prohpharmacy_trekking_app.Shared;
using static prohpharmacy_trekking_app.Features.Fleet.Devices.CreateTrackingDevice;

namespace prohpharmacy_trekking_app.Features.Fleet.Devices;

public static class UpdateTrackingDevice
{
    public class Command : IRequest<Result<DeviceResponse>>
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
        public int? TraccarDeviceId { get; set; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
            RuleFor(x => x.PhoneNumber).MaximumLength(30).When(x => x.PhoneNumber is not null);
        }
    }

    internal sealed class Handler(AppDbContext db, IValidator<Command> validator, ITraccarService traccar)
        : IRequestHandler<Command, Result<DeviceResponse>>
    {
        public async Task<Result<DeviceResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var validation = await validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Result.Failure<DeviceResponse>(Error.ValidationError(validation));

            var device = await db.TrackingDevices
                .Include(d => d.Vehicle)
                .Include(d => d.StaffMember)
                .FirstOrDefaultAsync(d => d.Id == request.Id, cancellationToken);

            if (device is null)
                return Result.Failure<DeviceResponse>(Error.CreateNotFoundError("Tracking device not found."));

            device.Name = request.Name.Trim();
            device.PhoneNumber = request.PhoneNumber?.Trim();
            device.TraccarDeviceId = request.TraccarDeviceId;
            device.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(cancellationToken);

            if (device.TraccarDeviceId is not null)
                _ = traccar.UpdateDeviceAsync(device.TraccarDeviceId.Value, device.Name, cancellationToken);

            return Result.Success(CreateTrackingDevice.Handler.ToResponse(
                device,
                device.Vehicle?.RegistrationNumber,
                device.StaffMember?.FullName));
        }
    }
}

public class UpdateTrackingDeviceEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPut("api/v1/fleet/devices/{id:guid}", async (
            Guid id, UpdateTrackingDevice.Command command, ISender sender) =>
        {
            command.Id = id;
            var result = await sender.Send(command);
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Fleet")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Fleet)
        .WithSummary("Update a tracking device")
        .WithDescription("Updates device name, phone number, and Traccar device ID. Unique ID (IMEI) cannot be changed.")
        .Produces<DeviceResponse>(200)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
