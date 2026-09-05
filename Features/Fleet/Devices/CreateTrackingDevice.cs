using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Fleet.Entities;
using prohpharmacy_trekking_app.Features.Fleet.Enums;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Fleet.Devices;

public static class CreateTrackingDevice
{
    public class Command : IRequest<Result<DeviceResponse>>
    {
        public string TraccarUniqueId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
        public int? TraccarDeviceId { get; set; }
    }

    public class DeviceResponse
    {
        public Guid Id { get; set; }
        public int? TraccarDeviceId { get; set; }
        public string TraccarUniqueId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime? LastReportedAt { get; set; }
        public decimal? LastLatitude { get; set; }
        public decimal? LastLongitude { get; set; }
        public Guid? CurrentStaffId { get; set; }
        public string? CurrentStaffName { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.TraccarUniqueId).NotEmpty().MaximumLength(100);
            RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
            RuleFor(x => x.PhoneNumber).MaximumLength(30).When(x => x.PhoneNumber is not null);
        }
    }

    internal sealed class Handler : IRequestHandler<Command, Result<DeviceResponse>>
    {
        private readonly AppDbContext _db;
        private readonly IValidator<Command> _validator;

        public Handler(AppDbContext db, IValidator<Command> validator)
        {
            _db = db;
            _validator = validator;
        }

        public async Task<Result<DeviceResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var validation = await _validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Result.Failure<DeviceResponse>(Error.ValidationError(validation));

            var uniqueIdTaken = await _db.TrackingDevices
                .AnyAsync(d => d.TraccarUniqueId == request.TraccarUniqueId.Trim(), cancellationToken);
            if (uniqueIdTaken)
                return Result.Failure<DeviceResponse>(Error.Conflict("A device with this Traccar unique ID already exists."));

            var device = new TrackingDevice
            {
                TraccarUniqueId = request.TraccarUniqueId.Trim(),
                Name = request.Name.Trim(),
                PhoneNumber = request.PhoneNumber?.Trim(),
                TraccarDeviceId = request.TraccarDeviceId,
                Status = TrackingDeviceStatus.Active,
                CreatedAt = DateTime.UtcNow
            };

            _db.TrackingDevices.Add(device);
            await _db.SaveChangesAsync(cancellationToken);

            return Result.Success(ToResponse(device, null, null));
        }

        internal static DeviceResponse ToResponse(
            TrackingDevice d, Guid? currentStaffId, string? currentStaffName) => new()
        {
            Id = d.Id,
            TraccarDeviceId = d.TraccarDeviceId,
            TraccarUniqueId = d.TraccarUniqueId,
            Name = d.Name,
            PhoneNumber = d.PhoneNumber,
            Status = d.Status.ToString(),
            LastReportedAt = d.LastReportedAt,
            LastLatitude = d.LastLatitude,
            LastLongitude = d.LastLongitude,
            CurrentStaffId = currentStaffId,
            CurrentStaffName = currentStaffName,
            CreatedAt = d.CreatedAt,
            UpdatedAt = d.UpdatedAt
        };
    }
}

public class CreateTrackingDeviceEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/fleet/devices", async (CreateTrackingDevice.Command command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Created($"api/v1/fleet/devices/{result.Value.Id}", result.Value);
        })
        .WithTags("Fleet")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Fleet)
        .WithSummary("Register a new tracking device")
        .WithDescription(
            "Registers a GPS tracking device. " +
            "`traccarUniqueId` is the IMEI or unique identifier used in Traccar. " +
            "`traccarDeviceId` is the numeric ID assigned by Traccar (can be set later).")
        .RequireAuthorization();
    }
}
