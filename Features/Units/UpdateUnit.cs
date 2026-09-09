using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;
using static prohpharmacy_trekking_app.Features.Units.CreateUnit;

namespace prohpharmacy_trekking_app.Features.Units;

public static class UpdateUnit
{
    public class Command : IRequest<Result<UnitResponse>>
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Name).NotEmpty().MaximumLength(80);
        }
    }

    internal sealed class Handler : IRequestHandler<Command, Result<UnitResponse>>
    {
        private readonly AppDbContext _db;
        private readonly IValidator<Command> _validator;

        public Handler(AppDbContext db, IValidator<Command> validator)
        {
            _db = db;
            _validator = validator;
        }

        public async Task<Result<UnitResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var validation = await _validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Result.Failure<UnitResponse>(Error.ValidationError(validation));

            var unit = await _db.Units.FindAsync([request.Id], cancellationToken);
            if (unit is null)
                return Result.Failure<UnitResponse>(Error.CreateNotFoundError("Unit not found."));

            var nameTaken = await _db.Units
                .AnyAsync(u => u.Id != request.Id && u.Name.ToLower() == request.Name.Trim().ToLower(), cancellationToken);
            if (nameTaken)
                return Result.Failure<UnitResponse>(Error.Conflict("A unit with this name already exists."));

            unit.Name = request.Name.Trim();
            unit.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(cancellationToken);

            return Result.Success(CreateUnit.Handler.ToResponse(unit));
        }
    }
}

public class UpdateUnitEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPut("api/v1/units/{id:guid}", async (Guid id, UpdateUnit.Command command, ISender sender) =>
        {
            command.Id = id;
            var result = await sender.Send(command);
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Units")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.General)
        .WithSummary("Update a unit of measure")
        .Produces<UnitResponse>(200)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
