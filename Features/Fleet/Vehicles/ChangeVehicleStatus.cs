using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Fleet.Enums;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Fleet.Vehicles;

public static class ChangeVehicleStatus
{
    public class Command : IRequest<Result<StatusResponse>>
    {
        public Guid Id { get; set; }
        public VehicleOperationalStatus Status { get; set; }
    }

    public class StatusResponse
    {
        public Guid VehicleId { get; set; }
        public string OperationalStatus { get; set; } = string.Empty;
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Status).IsInEnum()
                .WithMessage("Valid statuses: Active, UnderMaintenance, Decommissioned.");
        }
    }

    internal sealed class Handler : IRequestHandler<Command, Result<StatusResponse>>
    {
        private readonly AppDbContext _db;
        private readonly IValidator<Command> _validator;

        public Handler(AppDbContext db, IValidator<Command> validator)
        {
            _db = db;
            _validator = validator;
        }

        public async Task<Result<StatusResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var validation = await _validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Result.Failure<StatusResponse>(Error.ValidationError(validation));

            var vehicle = await _db.Vehicles
                .FirstOrDefaultAsync(v => v.Id == request.Id, cancellationToken);

            if (vehicle is null)
                return Result.Failure<StatusResponse>(Error.CreateNotFoundError("Vehicle not found."));

            if (vehicle.OperationalStatus == request.Status)
                return Result.Failure<StatusResponse>(
                    Error.BadRequest($"Vehicle is already {request.Status}."));

            vehicle.OperationalStatus = request.Status;
            vehicle.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(cancellationToken);

            return Result.Success(new StatusResponse
            {
                VehicleId = vehicle.Id,
                OperationalStatus = vehicle.OperationalStatus.ToString()
            });
        }
    }
}

public class ChangeVehicleStatusEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPatch("api/v1/fleet/vehicles/{id:guid}/status", async (
            Guid id, ChangeVehicleStatus.Command command, ISender sender) =>
        {
            command.Id = id;
            var result = await sender.Send(command);
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Fleet")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Fleet)
        .WithSummary("Change vehicle operational status")
        .WithDescription("Valid statuses: Active, UnderMaintenance, Decommissioned.")
        .RequireAuthorization();
    }
}
