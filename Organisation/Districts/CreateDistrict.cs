using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Organisation.Entities;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Organisation.Districts;

public static class CreateDistrict
{
    public class Command : IRequest<Result<DistrictResponse>>
    {
        public Guid RegionId { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    public class DistrictResponse
    {
        public Guid Id { get; set; }
        public Guid RegionId { get; set; }
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
            RuleFor(x => x.RegionId).NotEmpty().WithMessage("RegionId is required.");
            RuleFor(x => x.Code)
                .NotEmpty().WithMessage("District code is required.")
                .MaximumLength(20);
            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("District name is required.")
                .MaximumLength(120);
        }
    }

    internal sealed class Handler : IRequestHandler<Command, Result<DistrictResponse>>
    {
        private readonly AppDbContext _db;
        private readonly IValidator<Command> _validator;

        public Handler(AppDbContext db, IValidator<Command> validator)
        {
            _db = db;
            _validator = validator;
        }

        public async Task<Result<DistrictResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var validation = await _validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Result.Failure<DistrictResponse>(Error.ValidationError(validation));

            var region = await _db.Regions.FindAsync([request.RegionId], cancellationToken);
            if (region is null)
                return Result.Failure<DistrictResponse>(Error.CreateNotFoundError("Region not found."));

            var duplicate = await _db.Districts.AnyAsync(
                d => d.RegionId == request.RegionId && d.Code.ToLower() == request.Code.Trim().ToLower(),
                cancellationToken);
            if (duplicate)
                return Result.Failure<DistrictResponse>(Error.Conflict("A district with this code already exists in the region."));

            var district = new District
            {
                RegionId = request.RegionId,
                Code = request.Code.Trim().ToUpper(),
                Name = request.Name.Trim(),
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            _db.Districts.Add(district);
            await _db.SaveChangesAsync(cancellationToken);

            return Result.Success(ToResponse(district, region.Name));
        }

        internal static DistrictResponse ToResponse(District d, string regionName) => new()
        {
            Id = d.Id,
            RegionId = d.RegionId,
            RegionName = regionName,
            Code = d.Code,
            Name = d.Name,
            IsActive = d.IsActive,
            CreatedAt = d.CreatedAt
        };
    }
}

public class CreateDistrictEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/organisation/districts", async (CreateDistrict.Command command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Created($"api/v1/organisation/districts/{result.Value.Id}", result.Value);
        })
        .WithTags("Organisation - Districts")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Organisation)
        .WithSummary("Create a district under a region")
        .RequireAuthorization();
    }
}
