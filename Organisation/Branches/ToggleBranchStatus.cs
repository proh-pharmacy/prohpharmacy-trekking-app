using Carter;
using MediatR;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Organisation.Branches;

public static class ToggleBranchStatus
{
    public class Command : IRequest<Result<object>>
    {
        public Guid Id { get; set; }
    }

    internal sealed class Handler : IRequestHandler<Command, Result<object>>
    {
        private readonly AppDbContext _db;

        public Handler(AppDbContext db) => _db = db;

        public async Task<Result<object>> Handle(Command request, CancellationToken cancellationToken)
        {
            var branch = await _db.Branches.FindAsync([request.Id], cancellationToken);
            if (branch is null)
                return Result.Failure<object>(Error.CreateNotFoundError("Branch not found."));

            branch.IsActive = !branch.IsActive;
            branch.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(cancellationToken);

            return Result.Success<object>(new
            {
                branch.Id,
                branch.Code,
                branch.Name,
                branch.IsActive,
                Message = branch.IsActive ? "Branch activated." : "Branch deactivated."
            });
        }
    }
}

public class ToggleBranchStatusEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPatch("api/v1/organisation/branches/{id:guid}/toggle-status", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new ToggleBranchStatus.Command { Id = id });
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Organisation - Branches")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Organisation)
        .WithSummary("Toggle branch active / inactive status")
        .RequireAuthorization();
    }
}
