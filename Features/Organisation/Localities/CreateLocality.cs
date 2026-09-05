using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Organisation.Entities;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Organisation.Localities;

public static class CreateLocality
{
    public class Command : IRequest<Result<LocalityResponse>>
    {
        public Guid DistrictId { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    public class LocalityResponse
    {
        public Guid Id { get; set; }
        public Guid DistrictId { get; set; }
        public string DistrictName { get; set; } = string.Empty;
        public string RegionName { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.DistrictId).NotEmpty().WithMessage("DistrictId is required.");
            RuleFor(x => x.Code)
                .NotEmpty().WithMessage("Locality code is required.")
                .MaximumLength(20);
            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Locality name is required.")
                .MaximumLength(120);
        }
    }

    internal sealed class Handler : IRequestHandler<Command, Result<LocalityResponse>>
    {
        private readonly AppDbContext _db;
        private readonly IValidator<Command> _validator;

        public Handler(AppDbContext db, IValidator<Command> validator)
        {
            _db = db;
            _validator = validator;
        }

        public async Task<Result<LocalityResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var validation = await _validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Result.Failure<LocalityResponse>(Error.ValidationError(validation));

            var district = await _db.Districts
                .Include(d => d.Region)
                .FirstOrDefaultAsync(d => d.Id == request.DistrictId, cancellationToken);

            if (district is null)
                return Result.Failure<LocalityResponse>(Error.CreateNotFoundError("District not found."));

            var duplicate = await _db.Localities.AnyAsync(
                l => l.DistrictId == request.DistrictId && l.Code.ToLower() == request.Code.Trim().ToLower(),
                cancellationToken);
            if (duplicate)
                return Result.Failure<LocalityResponse>(Error.Conflict("A locality with this code already exists in the district."));

            var locality = new Locality
            {
                DistrictId = request.DistrictId,
                Code = request.Code.Trim().ToUpper(),
                Name = request.Name.Trim(),
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            _db.Localities.Add(locality);
            await _db.SaveChangesAsync(cancellationToken);

            return Result.Success(ToResponse(locality, district.Name, district.Region?.Name ?? string.Empty));
        }

        internal static LocalityResponse ToResponse(Locality l, string districtName, string regionName) => new()
        {
            Id = l.Id,
            DistrictId = l.DistrictId,
            DistrictName = districtName,
            RegionName = regionName,
            Code = l.Code,
            Name = l.Name,
            IsActive = l.IsActive,
            CreatedAt = l.CreatedAt
        };
    }
}

public class CreateLocalityEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/organisation/localities", async (CreateLocality.Command command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Created($"api/v1/organisation/localities/{result.Value.Id}", result.Value);
        })
        .WithTags("Organisation - Localities")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Organisation)
        .WithSummary("Create a locality under a district")
        .RequireAuthorization();
    }
}
