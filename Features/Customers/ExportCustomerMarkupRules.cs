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

namespace prohpharmacy_trekking_app.Features.Customers;

public static class ExportCustomerMarkupRules
{
    public class Query : IRequest<Result<ExportResult>>
    {
        public Guid? RegionId { get; set; }
    }

    public class ExportResult
    {
        public byte[] FileBytes { get; set; } = [];
        public string FileName { get; set; } = string.Empty;
    }

    internal sealed class Handler(AppDbContext db) : IRequestHandler<Query, Result<ExportResult>>
    {
        public async Task<Result<ExportResult>> Handle(Query request, CancellationToken cancellationToken)
        {
            var query = db.CustomerMarkupRules
                .Include(r => r.CustomerAccount)
                    .ThenInclude(a => a.Region)
                .Include(r => r.Product)
                .AsNoTracking();

            if (request.RegionId.HasValue)
                query = query.Where(r => r.CustomerAccount.RegionId == request.RegionId.Value);

            var rules = await query
                .OrderBy(r => r.CustomerAccount.Region.Name)
                .ThenBy(r => r.CustomerAccount.BusinessName)
                .ThenBy(r => r.ProductId == null ? 0 : 1)
                .ThenBy(r => r.Product != null ? r.Product.Name : string.Empty)
                .ToListAsync(cancellationToken);

            using var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add("Customer Markup Rules");

            var brandGreen = Color.FromArgb(0, 191, 111);
            var custMarkupHeaders = new[] { "CUSTOMER CODE", "CUSTOMER NAME", "REGION", "PRODUCT", "MARKUP %" };
            for (int c = 0; c < custMarkupHeaders.Length; c++)
            {
                var cell = ws.Cells[1, c + 1];
                cell.Value = custMarkupHeaders[c];
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
                ws.Cells[row, 1].Value = rule.CustomerAccount.CustomerCode;
                ws.Cells[row, 2].Value = rule.CustomerAccount.BusinessName;
                ws.Cells[row, 3].Value = rule.CustomerAccount.Region.Name;
                ws.Cells[row, 4].Value = rule.Product?.Name ?? "All Products";
                ws.Cells[row, 5].Value = rule.MarkupPercentage;

                var markupCell = ws.Cells[row, 5];
                markupCell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                markupCell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                (Color bg, Color fg) custMarkupColors = rule.MarkupPercentage <= 5
                    ? (Color.FromArgb(240, 253, 244), Color.FromArgb(21, 128, 61))
                    : rule.MarkupPercentage <= 10
                        ? (Color.FromArgb(255, 251, 235), Color.FromArgb(180, 83, 9))
                        : (Color.FromArgb(254, 242, 242), Color.FromArgb(185, 28, 28));
                markupCell.Style.Fill.BackgroundColor.SetColor(custMarkupColors.bg);
                markupCell.Style.Font.Color.SetColor(custMarkupColors.fg);

                row++;
            }

            ws.Cells[ws.Dimension.Address].AutoFitColumns();
            for (var i = 1; i <= ws.Dimension.Columns; i++)
                ws.Column(i).Width += 2;

            var fileName = $"customer_markup_rules_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx";
            return Result.Success(new ExportResult { FileBytes = package.GetAsByteArray(), FileName = fileName });
        }
    }
}

public class ExportCustomerMarkupRulesEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/customers/markups/export", async (
            ISender sender,
            [FromQuery] Guid? regionId) =>
        {
            var result = await sender.Send(new ExportCustomerMarkupRules.Query
            {
                RegionId = regionId
            });

            if (result.IsFailure)
                return Results.BadRequest(result.Error);

            return Results.File(
                result.Value.FileBytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                result.Value.FileName);
        })
        .WithTags("Customers")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Customers)
        .WithSummary("Export customer markup rules to Excel")
        .WithDescription(
            "Exports all customer markup rules to a single-sheet .xlsx file. " +
            "Each row is one rule — a customer with both a general rule and product-specific rules appears on multiple rows. " +
            "Columns: Customer Code, Customer Name, Region, Product ('All Products' for customer-wide rules), Markup %. " +
            "Ordered by region, then customer name, with the general rule listed first before product-specific rules. " +
            "Optional filter: `regionId`.")
        .Produces<FileResult>(200)
        .Produces<Error>(400)
        .RequireAuthorization();
    }
}
