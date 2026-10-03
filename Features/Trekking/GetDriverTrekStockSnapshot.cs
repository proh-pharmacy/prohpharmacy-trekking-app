using Carter;
using MediatR;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Services.Pdf;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class GetDriverTrekStockSnapshot
{
    public class Query : IRequest<Result<GetTrekStockSnapshot.Response>>
    {
        public Guid Token { get; set; }
    }

    internal sealed class Handler(AppDbContext db) : IRequestHandler<Query, Result<GetTrekStockSnapshot.Response>>
    {
        public Task<Result<GetTrekStockSnapshot.Response>> Handle(Query request, CancellationToken cancellationToken) =>
            GetTrekStockSnapshot.Handler.BuildAsync(db, t => t.DriverToken == request.Token, cancellationToken);
    }
}

public class GetDriverTrekStockSnapshotEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/treks/driver/{token:guid}/stock-snapshot",
            async (Guid token, ISender sender) =>
            {
                var result = await sender.Send(new GetDriverTrekStockSnapshot.Query { Token = token });
                return GetTrekStockSnapshotEndpoint.MapResult(result);
            })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Get the trek stock reconciliation snapshot (driver portal)")
        .WithDescription("Driver-token twin of the admin stock snapshot endpoint. Returns 400 if the trek is not Completed. Same payload shape as /api/v1/treks/{trekId}/stock-snapshot.")
        .Produces<GetTrekStockSnapshot.Response>(200)
        .Produces<Error>(400)
        .Produces<Error>(404)
        .AllowAnonymous();

        app.MapGet("api/v1/treks/driver/{token:guid}/stock-snapshot/pdf",
            async (Guid token, ISender sender) =>
            {
                var result = await sender.Send(new GetDriverTrekStockSnapshot.Query { Token = token });
                if (result.IsFailure) return GetTrekStockSnapshotEndpoint.MapResult(result);

                var bytes = StockSnapshotPdfGenerator.Generate(result.Value);
                return Results.File(bytes, "application/pdf", $"StockSnapshot-{result.Value.TrekNumber}-{result.Value.TrekDate}.pdf");
            })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Download the trek stock reconciliation snapshot as PDF (driver portal)")
        .Produces<Error>(400)
        .Produces<Error>(404)
        .AllowAnonymous();
    }
}
