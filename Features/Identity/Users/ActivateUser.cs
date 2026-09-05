using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Identity.Users;

public static class ActivateUser
{
    public class Command : IRequest<Result<ActivateResponse>>
    {
        public Guid UserId { get; set; }
    }

    public class ActivateResponse
    {
        public Guid UserId { get; set; }
        public bool IsActive { get; set; }
    }

    internal sealed class Handler : IRequestHandler<Command, Result<ActivateResponse>>
    {
        private readonly AppDbContext _db;

        public Handler(AppDbContext db) => _db = db;

        public async Task<Result<ActivateResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var user = await _db.ApplicationUsers
                .Include(u => u.StaffMember)
                .FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);

            if (user is null)
                return Result.Failure<ActivateResponse>(Error.CreateNotFoundError("User not found."));

            if (user.IsActive)
                return Result.Failure<ActivateResponse>(Error.BadRequest("User is already active."));

            if (user.StaffMember.EmploymentStatus == Staff.Enums.EmploymentStatus.Offboarded)
                return Result.Failure<ActivateResponse>(
                    Error.BadRequest("Cannot reactivate a user whose staff record is offboarded."));

            user.IsActive = true;
            user.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(cancellationToken);

            return Result.Success(new ActivateResponse { UserId = user.Id, IsActive = user.IsActive });
        }
    }
}

public class ActivateUserEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/users/{id:guid}/activate", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new ActivateUser.Command { UserId = id });
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Auth")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Auth)
        .WithSummary("Reactivate a suspended user")
        .RequireAuthorization();
    }
}
