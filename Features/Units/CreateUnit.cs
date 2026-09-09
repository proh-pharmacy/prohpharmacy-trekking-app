using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Units.Entities;
using UnitEntity = prohpharmacy_trekking_app.Features.Units.Entities.Unit;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Units;

public static class CreateUnit
{
    public class Command : IRequest<Result<UnitResponse>>
    {
        public string Name { get; set; } = string.Empty;
    }

    public class UnitResponse
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
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

            var exists = await _db.Units
                .AnyAsync(u => u.Name.ToLower() == request.Name.Trim().ToLower(), cancellationToken);
            if (exists)
                return Result.Failure<UnitResponse>(Error.Conflict("A unit with this name already exists."));

            var unit = new UnitEntity
            {
                Name = request.Name.Trim(),
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            _db.Units.Add(unit);
            await _db.SaveChangesAsync(cancellationToken);

            return Result.Success(ToResponse(unit));
        }

        internal static UnitResponse ToResponse(UnitEntity u) => new()
        {
            Id = u.Id,
            Name = u.Name,
            IsActive = u.IsActive,
            CreatedAt = u.CreatedAt,
            UpdatedAt = u.UpdatedAt
        };
    }
}

public class CreateUnitEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/units", async (CreateUnit.Command command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Created($"api/v1/units/{result.Value.Id}", result.Value);
        })
        .WithTags("Units")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.General)
        .WithSummary("Create a unit of measure")
        .Produces<CreateUnit.UnitResponse>(201)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
