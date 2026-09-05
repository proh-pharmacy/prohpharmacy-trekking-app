using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Organisation.Entities;
using prohpharmacy_trekking_app.Features.Organisation.Enums;
using prohpharmacy_trekking_app.Shared;
using prohpharmacy_trekking_app.Utilities;

namespace prohpharmacy_trekking_app.Features.Organisation.Branches;

public static class CreateBranch
{
    public class Command : IRequest<Result<BranchResponse>>
    {
        public string Name { get; set; } = string.Empty;
        public BranchType BranchType { get; set; }
        public Guid RegionId { get; set; }
        public Guid DistrictId { get; set; }
        public string Address { get; set; } = string.Empty;
        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }
        public string ContactNumber { get; set; } = string.Empty;
    }

    public class BranchResponse
    {
        public Guid Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string BranchType { get; set; } = string.Empty;
        public Guid RegionId { get; set; }
        public string RegionName { get; set; } = string.Empty;
        public Guid DistrictId { get; set; }
        public string DistrictName { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public decimal Latitude { get; set; }
        public decimal Longitude { get; set; }
        public string ContactNumber { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Name).NotEmpty().MaximumLength(160);
            RuleFor(x => x.BranchType).IsInEnum();
            RuleFor(x => x.RegionId).NotEmpty();
            RuleFor(x => x.DistrictId).NotEmpty();
            RuleFor(x => x.Address).NotEmpty().MaximumLength(300);
            RuleFor(x => x.ContactNumber).NotEmpty().MaximumLength(30);
        }
    }

    internal sealed class Handler : IRequestHandler<Command, Result<BranchResponse>>
    {
        private readonly AppDbContext _db;
        private readonly IValidator<Command> _validator;

        public Handler(AppDbContext db, IValidator<Command> validator)
        {
            _db = db;
            _validator = validator;
        }

        public async Task<Result<BranchResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var validation = await _validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Result.Failure<BranchResponse>(Error.ValidationError(validation));

            var region = await _db.Regions.FindAsync([request.RegionId], cancellationToken);
            if (region is null)
                return Result.Failure<BranchResponse>(Error.CreateNotFoundError("Region not found."));

            var district = await _db.Districts.FindAsync([request.DistrictId], cancellationToken);
            if (district is null)
                return Result.Failure<BranchResponse>(Error.CreateNotFoundError("District not found."));

            if (district.RegionId != request.RegionId)
                return Result.Failure<BranchResponse>(Error.BadRequest("District does not belong to the specified region."));

            var nameExists = await _db.Branches
                .AnyAsync(b => b.DistrictId == request.DistrictId && b.Name.ToLower() == request.Name.Trim().ToLower(), cancellationToken);
            if (nameExists)
                return Result.Failure<BranchResponse>(Error.Conflict("A branch with this name already exists in the district."));

            var code = await StringUtilities.GenerateUniqueCodeAsync(
                request.Name,
                c => _db.Branches.AnyAsync(b => b.Code == c, cancellationToken));

            var branch = new Branch
            {
                Code = code,
                Name = request.Name.Trim(),
                BranchType = request.BranchType,
                RegionId = request.RegionId,
                DistrictId = request.DistrictId,
                Address = request.Address.Trim(),
                Latitude = request.Latitude ?? 0,
                Longitude = request.Longitude ?? 0,
                ContactNumber = request.ContactNumber.Trim(),
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            _db.Branches.Add(branch);
            await _db.SaveChangesAsync(cancellationToken);

            return Result.Success(ToResponse(branch, region.Name, district.Name));
        }

        internal static BranchResponse ToResponse(Branch b, string regionName, string districtName) => new()
        {
            Id = b.Id,
            Code = b.Code,
            Name = b.Name,
            BranchType = b.BranchType.ToString(),
            RegionId = b.RegionId,
            RegionName = regionName,
            DistrictId = b.DistrictId,
            DistrictName = districtName,
            Address = b.Address,
            Latitude = b.Latitude,
            Longitude = b.Longitude,
            ContactNumber = b.ContactNumber,
            IsActive = b.IsActive,
            CreatedAt = b.CreatedAt,
            UpdatedAt = b.UpdatedAt
        };
    }
}

public class CreateBranchEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/organisation/branches", async (CreateBranch.Command command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Created($"api/v1/organisation/branches/{result.Value.Id}", result.Value);
        })
        .WithTags("Organisation - Branches")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Organisation)
        .WithSummary("Create a new branch")
        .WithDescription("Validates the Region → District hierarchy before creating the branch.")
        .RequireAuthorization();
    }
}
