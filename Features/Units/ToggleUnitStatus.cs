using Carter;
using MediatR;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;
using static prohpharmacy_trekking_app.Features.Units.CreateUnit;

namespace prohpharmacy_trekking_app.Features.Units;

public static class ToggleUnitStatus
{
    public class Command : IRequest<Result<UnitResponse>>
    {
        public Guid Id { get; set; }
    }

    internal sealed class Handler : IRequestHandler<Command, Result<UnitResponse>>
    {
        private readonly AppDbContext _db;

        public Handler(AppDbContext db) => _db = db;

        public async Task<Result<UnitResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var unit = await _db.Units.FindAsync([request.Id], cancellationToken);
            if (unit is null)
                return Result.Failure<UnitResponse>(Error.CreateNotFoundError("Unit not found."));

            unit.IsActive = !unit.IsActive;
            unit.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(cancellationToken);

            return Result.Success(CreateUnit.Handler.ToResponse(unit));
        }
    }
}

public class ToggleUnitStatusEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPatch("api/v1/units/{id:guid}/status", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new ToggleUnitStatus.Command { Id = id });
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Units")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.General)
        .WithSummary("Toggle unit active / inactive")
        .Produces<UnitResponse>(200)
        .Produces<Error>(404)
        .RequireAuthorization();
    }
}
