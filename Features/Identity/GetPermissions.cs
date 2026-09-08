using Carter;
using MediatR;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Identity;

public static class GetPermissions
{
    public class Query : IRequest<Result<List<PermissionGroup>>> { }

    public class PermissionGroup
    {
        public string Module { get; set; } = string.Empty;
        public List<string> Permissions { get; set; } = [];
    }

    internal sealed class Handler : IRequestHandler<Query, Result<List<PermissionGroup>>>
    {
        public Task<Result<List<PermissionGroup>>> Handle(Query request, CancellationToken cancellationToken)
        {
            var groups = Permissions.All
                .GroupBy(p => p.Split('.')[0])
                .OrderBy(g => g.Key)
                .Select(g => new PermissionGroup
                {
                    Module = g.Key,
                    Permissions = g.OrderBy(p => p).ToList()
                })
                .ToList();

            return Task.FromResult(Result.Success(groups));
        }
    }
}

public class GetPermissionsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/permissions", async (ISender sender) =>
        {
            var result = await sender.Send(new GetPermissions.Query());
            return result.IsFailure
                ? Results.BadRequest(result.Error)
                : Results.Ok(result.Value);
        })
        .WithTags("Auth")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Auth)
        .WithSummary("List all available permissions grouped by module")
        .WithDescription("Returns every permission key in the system, grouped by module. Use this to build a permission picker when creating or editing a role.")
        .Produces<List<GetPermissions.PermissionGroup>>(200)
        .RequireAuthorization();
    }
}
