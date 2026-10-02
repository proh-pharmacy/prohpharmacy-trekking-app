using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Trekking.Enums;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class GetPendingReturnApprovalCount
{
    public class Query : IRequest<Result<CountResponse>> { }

    public class CountResponse
    {
        public int Count { get; set; }
    }

    internal sealed class Handler(AppDbContext db) : IRequestHandler<Query, Result<CountResponse>>
    {
        public async Task<Result<CountResponse>> Handle(Query request, CancellationToken cancellationToken)
        {
            var count = await db.TrekkingTripStopReturns
                .AsNoTracking()
                .Where(r => r.ApprovalStatus == ReturnApprovalStatus.Pending
                         && r.TrekkingTripStop.TrekkingTrip.Status == TrekStatus.Completed)
                .CountAsync(cancellationToken);

            return Result.Success(new CountResponse { Count = count });
        }
    }
}

public class GetPendingReturnApprovalCountEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/returns/pending-count", async (ISender sender) =>
        {
            var result = await sender.Send(new GetPendingReturnApprovalCount.Query());
            return result.IsFailure ? Results.BadRequest(result.Error) : Results.Ok(result.Value);
        })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Count of pending refund approvals")
        .WithDescription("Returns the number of refund records awaiting approval across all treks. Only counts pending returns on treks whose status is Completed, matching the approvals inbox visibility rule. Intended for sidebar badges.")
        .Produces<GetPendingReturnApprovalCount.CountResponse>(200)
        .RequireAuthorization();
    }
}
