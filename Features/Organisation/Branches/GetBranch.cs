using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;
using static prohpharmacy_trekking_app.Features.Organisation.Branches.CreateBranch;

namespace prohpharmacy_trekking_app.Features.Organisation.Branches;

public static class GetBranch
{
    public class Query : IRequest<Result<BranchResponse>>
    {
        public Guid Id { get; set; }
    }

    internal sealed class Handler : IRequestHandler<Query, Result<BranchResponse>>
    {
        private readonly AppDbContext _db;

        public Handler(AppDbContext db) => _db = db;

        public async Task<Result<BranchResponse>> Handle(Query request, CancellationToken cancellationToken)
        {
            var branch = await _db.Branches
                .Include(b => b.Region)
                .Include(b => b.District)
                .Include(b => b.Locality)
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == request.Id, cancellationToken);

            if (branch is null)
                return Result.Failure<BranchResponse>(Error.CreateNotFoundError("Branch not found."));

            return Result.Success(ToResponse(branch));
        }

        private static BranchResponse ToResponse(Entities.Branch b) => new()
        {
            Id = b.Id,
            Code = b.Code,
            Name = b.Name,
            BranchType = b.BranchType.ToString(),
            RegionId = b.RegionId,
            RegionName = b.Region?.Name ?? string.Empty,
            DistrictId = b.DistrictId,
            DistrictName = b.District?.Name ?? string.Empty,
            LocalityId = b.LocalityId,
            LocalityName = b.Locality?.Name ?? string.Empty,
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

public class GetBranchEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/organisation/branches/{id:guid}", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new GetBranch.Query { Id = id });
            return result.IsFailure
                ? Results.NotFound(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Organisation - Branches")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Organisation)
        .WithSummary("Get a branch by ID")
        .RequireAuthorization();
    }
}
