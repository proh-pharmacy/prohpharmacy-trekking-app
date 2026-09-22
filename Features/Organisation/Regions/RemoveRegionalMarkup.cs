using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Organisation.Regions;

public static class RemoveRegionalMarkup
{
    public class Command : IRequest<Result>
    {
        public Guid RegionId { get; set; }
        public Guid MarkupId { get; set; }
    }

    internal sealed class Handler(AppDbContext db)
        : IRequestHandler<Command, Result>
    {
        public async Task<Result> Handle(Command request, CancellationToken cancellationToken)
        {
            var rule = await db.RegionalMarkupRules
                .FirstOrDefaultAsync(r => r.Id == request.MarkupId && r.RegionId == request.RegionId, cancellationToken);

            if (rule is null)
                return Result.Failure(Error.CreateNotFoundError("Markup rule not found."));

            db.RegionalMarkupRules.Remove(rule);
            await db.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
    }
}

public class RemoveRegionalMarkupEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/v1/organisation/regions/{regionId:guid}/markups/{markupId:guid}",
            async (Guid regionId, Guid markupId, ISender sender) =>
            {
                var result = await sender.Send(new RemoveRegionalMarkup.Command
                {
                    RegionId = regionId,
                    MarkupId = markupId
                });
                return result.IsFailure
                    ? Results.NotFound(result.Error)
                    : Results.NoContent();
            })
        .WithTags("Organisation - Regions")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Organisation)
        .WithSummary("Remove a markup rule from a region")
        .Produces(204)
        .Produces<Error>(404)
        .RequireAuthorization();
    }
}
