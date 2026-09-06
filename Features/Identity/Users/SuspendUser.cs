using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Identity.Users;

public static class SuspendUser
{
    public class Command : IRequest<Result<SuspendResponse>>
    {
        public Guid UserId { get; set; }
    }

    public class SuspendResponse
    {
        public Guid UserId { get; set; }
        public bool IsActive { get; set; }
        public int TokensRevoked { get; set; }
    }

    internal sealed class Handler : IRequestHandler<Command, Result<SuspendResponse>>
    {
        private readonly AppDbContext _db;

        public Handler(AppDbContext db) => _db = db;

        public async Task<Result<SuspendResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var user = await _db.ApplicationUsers
                .Include(u => u.RefreshTokens.Where(rt => !rt.IsRevoked))
                .FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);

            if (user is null)
                return Result.Failure<SuspendResponse>(Error.CreateNotFoundError("User not found."));

            if (!user.IsActive)
                return Result.Failure<SuspendResponse>(Error.BadRequest("User is already suspended."));

            user.IsActive = false;
            user.UpdatedAt = DateTime.UtcNow;

            foreach (var token in user.RefreshTokens)
            {
                token.IsRevoked = true;
                token.RevokedAt = DateTime.UtcNow;
                token.RevokedReason = "Account suspended";
            }

            await _db.SaveChangesAsync(cancellationToken);

            return Result.Success(new SuspendResponse
            {
                UserId = user.Id,
                IsActive = user.IsActive,
                TokensRevoked = user.RefreshTokens.Count
            });
        }
    }
}

public class SuspendUserEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/users/{id:guid}/suspend", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new SuspendUser.Command { UserId = id });
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Auth")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Auth)
        .WithSummary("Suspend user access")
        .WithDescription("Deactivates the user account and immediately revokes all active refresh tokens.")
        .Produces<SuspendUser.SuspendResponse>(200)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
