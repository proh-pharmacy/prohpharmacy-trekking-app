using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;
using System.Drawing;

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

            var brandGreen = Color.FromArgb(0, 191, 111);
            var markupHeaders = new[] { "REGION", "PRODUCT", "MARKUP %" };
            for (int c = 0; c < markupHeaders.Length; c++)
            {
                var cell = ws.Cells[1, c + 1];
                cell.Value = markupHeaders[c];
                cell.Style.Font.Bold = true;
                cell.Style.Font.Size = 11;
                cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                cell.Style.Fill.BackgroundColor.SetColor(brandGreen);
                cell.Style.Font.Color.SetColor(Color.White);
                cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            }

            int row = 2;
            foreach (var rule in rules)
            {
                ws.Cells[row, 1].Value = rule.Region.Name;
                ws.Cells[row, 2].Value = rule.Product?.Name ?? "All Products";
                ws.Cells[row, 3].Value = rule.MarkupPercentage;

                var markupCell = ws.Cells[row, 3];
                markupCell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                markupCell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                (Color bg, Color fg) markupColors = rule.MarkupPercentage <= 5
                    ? (Color.FromArgb(240, 253, 244), Color.FromArgb(21, 128, 61))
                    : rule.MarkupPercentage <= 10
                        ? (Color.FromArgb(255, 251, 235), Color.FromArgb(180, 83, 9))
                        : (Color.FromArgb(254, 242, 242), Color.FromArgb(185, 28, 28));
                markupCell.Style.Fill.BackgroundColor.SetColor(markupColors.bg);
                markupCell.Style.Font.Color.SetColor(markupColors.fg);

                row++;
            }

            ws.Cells[ws.Dimension.Address].AutoFitColumns();
            for (var i = 1; i <= ws.Dimension.Columns; i++)
                ws.Column(i).Width += 2;

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
