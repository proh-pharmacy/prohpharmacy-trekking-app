using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Organisation.Enums;
using prohpharmacy_trekking_app.Shared;
using static prohpharmacy_trekking_app.Organisation.Branches.CreateBranch;

namespace prohpharmacy_trekking_app.Organisation.Branches;

public static class UpdateBranch
{
    public class Command : IRequest<Result<BranchResponse>>
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public BranchType BranchType { get; set; }
        public Guid RegionId { get; set; }
        public Guid DistrictId { get; set; }
        public Guid LocalityId { get; set; }
        public string Address { get; set; } = string.Empty;
        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }
        public string ContactNumber { get; set; } = string.Empty;
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Id).NotEmpty();
            RuleFor(x => x.Name).NotEmpty().MaximumLength(160);
            RuleFor(x => x.BranchType).IsInEnum();
            RuleFor(x => x.RegionId).NotEmpty();
            RuleFor(x => x.DistrictId).NotEmpty();
            RuleFor(x => x.LocalityId).NotEmpty();
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

            var branch = await _db.Branches.FindAsync([request.Id], cancellationToken);
            if (branch is null)
                return Result.Failure<BranchResponse>(Error.CreateNotFoundError("Branch not found."));

            var region = await _db.Regions.FindAsync([request.RegionId], cancellationToken);
            if (region is null)
                return Result.Failure<BranchResponse>(Error.CreateNotFoundError("Region not found."));

            var district = await _db.Districts.FindAsync([request.DistrictId], cancellationToken);
            if (district is null)
                return Result.Failure<BranchResponse>(Error.CreateNotFoundError("District not found."));

            if (district.RegionId != request.RegionId)
                return Result.Failure<BranchResponse>(Error.BadRequest("District does not belong to the specified region."));

            var locality = await _db.Localities.FindAsync([request.LocalityId], cancellationToken);
            if (locality is null)
                return Result.Failure<BranchResponse>(Error.CreateNotFoundError("Locality not found."));

            if (locality.DistrictId != request.DistrictId)
                return Result.Failure<BranchResponse>(Error.BadRequest("Locality does not belong to the specified district."));

            branch.Name = request.Name.Trim();
            branch.BranchType = request.BranchType;
            branch.RegionId = request.RegionId;
            branch.DistrictId = request.DistrictId;
            branch.LocalityId = request.LocalityId;
            branch.Address = request.Address.Trim();
            branch.Latitude = request.Latitude ?? branch.Latitude;
            branch.Longitude = request.Longitude ?? branch.Longitude;
            branch.ContactNumber = request.ContactNumber.Trim();
            branch.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(cancellationToken);

            return Result.Success(CreateBranch.Handler.ToResponse(branch, region.Name, district.Name, locality.Name));
        }
    }

    public class Endpoint : ICarterModule
    {
        public void AddRoutes(IEndpointRouteBuilder app)
        {
            app.MapPut("api/v1/organisation/branches/{id:guid}", async (Guid id, Command command, ISender sender) =>
            {
                command.Id = id;
                var result = await sender.Send(command);
                return result.IsFailure
                    ? Results.UnprocessableEntity(result.Error)
                    : Results.Ok(result.Value);
            })
            .WithTags("Organisation - Branches")
            .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Organisation)
            .WithSummary("Update a branch")
            .RequireAuthorization();
        }
    }
}
