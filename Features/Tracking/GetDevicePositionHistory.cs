using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Services.Traccar;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Tracking;

public static class GetDevicePositionHistory
{
    public class Query : IRequest<Result<List<HistoryPointResponse>>>
    {
        public Guid DeviceId { get; set; }
        public DateTime From { get; set; }
        public DateTime To { get; set; }
    }

    public class HistoryPointResponse
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double Speed { get; set; }
        public double Course { get; set; }
        public string? Address { get; set; }
        public bool? Ignition { get; set; }
        public bool? Motion { get; set; }
        public double? BatteryLevel { get; set; }
        public DateTime FixTime { get; set; }
        public bool Valid { get; set; }
    }

    internal sealed class Handler : IRequestHandler<Query, Result<List<HistoryPointResponse>>>
    {
        private readonly AppDbContext _db;
        private readonly ITraccarService _traccar;

        public Handler(AppDbContext db, ITraccarService traccar)
        {
            _db = db;
            _traccar = traccar;
        }

        public async Task<Result<List<HistoryPointResponse>>> Handle(Query request, CancellationToken cancellationToken)
        {
            if (request.To <= request.From)
                return Result.Failure<List<HistoryPointResponse>>(Error.BadRequest("'to' must be after 'from'."));

            if ((request.To - request.From).TotalDays > 31)
                return Result.Failure<List<HistoryPointResponse>>(Error.BadRequest("Date range cannot exceed 31 days."));

            var device = await _db.TrackingDevices
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == request.DeviceId, cancellationToken);

            if (device is null)
                return Result.Failure<List<HistoryPointResponse>>(Error.CreateNotFoundError("Tracking device not found."));

            if (device.TraccarDeviceId is null)
                return Result.Failure<List<HistoryPointResponse>>(Error.BadRequest("Device is not registered in Traccar."));

            var positions = await _traccar.GetPositionHistoryAsync(
                device.TraccarDeviceId.Value, request.From, request.To, cancellationToken);

            var response = positions.Select(p => new HistoryPointResponse
            {
                Latitude = p.Latitude,
                Longitude = p.Longitude,
                Speed = p.Speed,
                Course = p.Course,
                Address = p.Address,
                Ignition = p.Ignition,
                Motion = p.Motion,
                BatteryLevel = p.BatteryLevel,
                FixTime = p.FixTime,
                Valid = p.Valid
            }).ToList();

            return Result.Success(response);
        }
    }
}

public class GetDevicePositionHistoryEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/fleet/devices/{id:guid}/position/history", async (
            Guid id,
            [FromQuery] DateTime from,
            [FromQuery] DateTime to,
            ISender sender) =>
        {
            var result = await sender.Send(new GetDevicePositionHistory.Query
            {
                DeviceId = id,
                From = from,
                To = to
            });
            return result.IsFailure ? Results.BadRequest(result.Error) : Results.Ok(result.Value);
        })
        .WithTags("Tracking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Tracking)
        .WithSummary("Get position history for a device")
        .WithDescription("Returns GPS position history from Traccar for the given device and time range. Maximum range is 31 days.")
        .RequireAuthorization();
    }
}
