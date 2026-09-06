using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Identity.Users;

public static class RevokeUserSessions
{
    public class Command : IRequest<Result<RevokeResponse>>
    {
        public Guid UserId { get; set; }
    }

    public class RevokeResponse
    {
        public Guid UserId { get; set; }
        public int TokensRevoked { get; set; }
    }

    internal sealed class Handler : IRequestHandler<Command, Result<RevokeResponse>>
    {
        private readonly AppDbContext _db;

        public Handler(AppDbContext db) => _db = db;

        public async Task<Result<RevokeResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var user = await _db.ApplicationUsers
                .Include(u => u.RefreshTokens.Where(rt => !rt.IsRevoked))
                .FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);

            if (user is null)
                return Result.Failure<RevokeResponse>(Error.CreateNotFoundError("User not found."));

            foreach (var token in user.RefreshTokens)
            {
                token.IsRevoked = true;
                token.RevokedAt = DateTime.UtcNow;
                token.RevokedReason = "Admin revoked sessions";
            }

            await _db.SaveChangesAsync(cancellationToken);

            return Result.Success(new RevokeResponse
            {
                UserId = user.Id,
                TokensRevoked = user.RefreshTokens.Count
            });
        }
    }
}

public class RevokeUserSessionsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/users/{id:guid}/revoke-sessions", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new RevokeUserSessions.Command { UserId = id });
            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Auth")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Auth)
        .WithSummary("Revoke all active sessions for a user")
        .WithDescription("Forces the user to log in again on all devices.")
        .Produces<RevokeUserSessions.RevokeResponse>(200)
        .Produces<Error>(404)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
