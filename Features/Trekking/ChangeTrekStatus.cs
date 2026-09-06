using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Trekking.Enums;
using prohpharmacy_trekking_app.Shared;
using static prohpharmacy_trekking_app.Features.Trekking.CreateTrek;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class ChangeTrekStatus
{
    public class Command : IRequest<Result<TrekResponse>>
    {
        public Guid Id { get; set; }
        public TrekStatus Status { get; set; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Status).IsInEnum();
        }
    }

    internal sealed class Handler : IRequestHandler<Command, Result<TrekResponse>>
    {
        private readonly AppDbContext _db;
        private readonly IValidator<Command> _validator;

        public Handler(AppDbContext db, IValidator<Command> validator)
        {
            _db = db;
            _validator = validator;
        }

        public async Task<Result<TrekResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var validation = await _validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Result.Failure<TrekResponse>(Error.ValidationError(validation));

            var trip = await _db.TrekkingTrips
                .Include(t => t.Branch)
                .Include(t => t.Driver)
                .Include(t => t.Vehicle)
                .FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken);

            if (trip is null)
                return Result.Failure<TrekResponse>(Error.CreateNotFoundError("Trekking trip not found."));

            trip.Status = request.Status;
            trip.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(cancellationToken);

            return Result.Success(CreateTrek.Handler.ToResponse(
                trip,
                trip.Branch?.Name ?? string.Empty,
                trip.Driver?.FullName ?? string.Empty,
                trip.Vehicle?.DisplayName ?? string.Empty,
                []));
        }
    }
}

public class ChangeTrekStatusEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPatch("api/v1/treks/{id:guid}/status", async (Guid id, ChangeTrekStatus.Command command, ISender sender) =>
        {
            command.Id = id;
            var result = await sender.Send(command);
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Change the status of a trekking trip")
        .WithDescription("Valid statuses: Draft, Scheduled, InProgress, Completed, Cancelled.")
        .Produces<TrekResponse>(200)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
