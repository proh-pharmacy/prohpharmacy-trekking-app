using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Trekking.Enums;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class AuthDriverPortal
{
    public class Command : IRequest<Result<PortalSession>>
    {
        public string TrekNumber { get; set; } = string.Empty;
    }

    public class PortalSession
    {
        public Guid DriverToken { get; set; }
        public Guid TrekId { get; set; }
        public string TrekNumber { get; set; } = string.Empty;
        public string RegionName { get; set; } = string.Empty;
        public DateOnly ScheduledDate { get; set; }
        public string Status { get; set; } = string.Empty;
        public StaffInfo Driver { get; set; } = new();
        public StaffInfo? SalesRep { get; set; }
    }

    public class StaffInfo
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.TrekNumber).NotEmpty();
        }
    }

    internal sealed class Handler(AppDbContext db) : IRequestHandler<Command, Result<PortalSession>>
    {
        public async Task<Result<PortalSession>> Handle(Command request, CancellationToken cancellationToken)
        {
            var trip = await db.TrekkingTrips
                .Include(t => t.Region)
                .Include(t => t.Driver)
                .Include(t => t.SalesStaff)
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    t => t.TrekNumber.ToLower() == request.TrekNumber.Trim().ToLower(),
                    cancellationToken);

            if (trip is null)
                return Result.Failure<PortalSession>(Error.CreateNotFoundError("Trek not found. Check the trek number and try again."));

            if (trip.Status == TrekStatus.Cancelled)
                return Result.Failure<PortalSession>(Error.BadRequest("This trek has been cancelled."));

            if (trip.Status == TrekStatus.Completed)
                return Result.Failure<PortalSession>(Error.BadRequest("This trek has already been completed."));

            if (trip.DriverToken is null)
                return Result.Failure<PortalSession>(Error.BadRequest("This trek does not have an active driver link yet. Ask your administrator to generate one."));

            return Result.Success(new PortalSession
            {
                DriverToken = trip.DriverToken.Value,
                TrekId = trip.Id,
                TrekNumber = trip.TrekNumber,
                RegionName = trip.Region.Name,
                ScheduledDate = trip.ScheduledDate,
                Status = trip.Status.ToString(),
                Driver = new StaffInfo
                {
                    Id = trip.DriverStaffId,
                    Name = $"{trip.Driver.FirstName} {trip.Driver.LastName}",
                    Phone = trip.Driver.PhoneNumber
                },
                SalesRep = trip.SalesStaff is null ? null : new StaffInfo
                {
                    Id = trip.SalesStaff.Id,
                    Name = $"{trip.SalesStaff.FirstName} {trip.SalesStaff.LastName}",
                    Phone = trip.SalesStaff.PhoneNumber
                }
            });
        }
    }
}

public class AuthDriverPortalEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/treks/portal/auth",
            async (AuthDriverPortal.Command command, ISender sender) =>
            {
                var result = await sender.Send(command);
                return result.IsFailure
                    ? Results.UnprocessableEntity(result.Error)
                    : Results.Ok(result.Value);
            })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Driver portal login by trek number")
        .WithDescription("Authenticates a driver or sales rep using the trek number (e.g. TR-212121). Returns the driver token and session info for the frontend to cache. No admin credentials required. Returns 422 if the trek is cancelled, completed, or has no active driver link.")
        .Produces<AuthDriverPortal.PortalSession>(200)
        .Produces<Error>(422)
        .AllowAnonymous();
    }
}
