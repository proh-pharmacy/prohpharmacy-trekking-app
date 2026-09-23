using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Organisation;

public static class ExportRegionalMarkupRules
{
    public class Query : IRequest<Result<ExportResult>> { }

    public class ExportResult
    {
        public byte[] FileBytes { get; set; } = [];
        public string FileName { get; set; } = string.Empty;
    }

    internal sealed class Handler(AppDbContext db) : IRequestHandler<Query, Result<ExportResult>>
    {
        public async Task<Result<ExportResult>> Handle(Query request, CancellationToken cancellationToken)
        {
            var rules = await db.RegionalMarkupRules
                .Include(r => r.Region)
                .Include(r => r.Product)
                .AsNoTracking()
                .OrderBy(r => r.Region.Name)
                .ThenBy(r => r.ProductId == null ? 0 : 1)
                .ThenBy(r => r.Product != null ? r.Product.Name : string.Empty)
                .ToListAsync(cancellationToken);

            using var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add("Regional Markup Rules");

            ws.Cells[1, 1].Value = "Region";
            ws.Cells[1, 2].Value = "Product";
            ws.Cells[1, 3].Value = "Markup %";

            using (var h = ws.Cells[1, 1, 1, 3]) h.Style.Font.Bold = true;

            int row = 2;
            foreach (var rule in rules)
            {
                ws.Cells[row, 1].Value = rule.Region.Name;
                ws.Cells[row, 2].Value = rule.Product?.Name ?? "All Products";
                ws.Cells[row, 3].Value = rule.MarkupPercentage;
                row++;
            }

            ws.Cells[ws.Dimension.Address].AutoFitColumns();

            var fileName = $"regional_markup_rules_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx";
            return Result.Success(new ExportResult { FileBytes = package.GetAsByteArray(), FileName = fileName });
        }
    }
}

public class ExportRegionalMarkupRulesEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/organisation/markups/export", async (ISender sender) =>
        {
            var result = await sender.Send(new ExportRegionalMarkupRules.Query());

            if (result.IsFailure)
                return Results.BadRequest(result.Error);

            return Results.File(
                result.Value.FileBytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                result.Value.FileName);
        })
        .WithTags("Organisation")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Organisation)
        .WithSummary("Export regional markup rules to Excel")
        .WithDescription(
            "Exports all regional markup rules to a single-sheet .xlsx file. " +
            "Columns: Region, Product ('All Products' for region-wide rules), Markup %. " +
            "Ordered by region name, with the region-wide rule listed first before product-specific rules.")
        .Produces<FileResult>(200)
        .Produces<Error>(400)
        .RequireAuthorization();
    }
}
