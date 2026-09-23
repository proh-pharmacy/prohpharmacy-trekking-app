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

namespace prohpharmacy_trekking_app.Features.Products;

public static class ExportProducts
{
    public class Query : IRequest<Result<ExportResult>>
    {
        public string? Search { get; set; }
        public bool? IsActive { get; set; }
        public string Mode { get; set; } = "catalog";
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
            var isPricing = string.Equals(request.Mode, "pricing", StringComparison.OrdinalIgnoreCase);

            var query = db.Products
                .Include(p => p.BasicUnit)
                .Include(p => p.PackagingUnit)
                .AsNoTracking();

            if (request.IsActive.HasValue)
                query = query.Where(p => p.IsActive == request.IsActive.Value);

            if (!string.IsNullOrWhiteSpace(request.Search))
            {
                var s = request.Search.ToLower();
                query = query.Where(p => p.Name.ToLower().Contains(s));
            }

            var products = await query.OrderBy(p => p.Name).ToListAsync(cancellationToken);

            using var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add("Products");

            var brandGreen = Color.FromArgb(0, 191, 111);

            void ApplyGreenHeader(ExcelWorksheet sheet, string[] labels)
            {
                for (int c = 0; c < labels.Length; c++)
                {
                    var cell = sheet.Cells[1, c + 1];
                    cell.Value = labels[c];
                    cell.Style.Font.Bold = true;
                    cell.Style.Font.Size = 11;
                    cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    cell.Style.Fill.BackgroundColor.SetColor(brandGreen);
                    cell.Style.Font.Color.SetColor(Color.White);
                    cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                }
            }

            if (!isPricing)
            {
                ApplyGreenHeader(ws, new[] { "PRODUCT NAME", "UNIT", "BASIC PRICE", "PACKAGING UNIT", "PACKAGING PRICE", "ACTIVE" });

                int row = 2;
                foreach (var p in products)
                {
                    ws.Cells[row, 1].Value = p.Name;
                    ws.Cells[row, 2].Value = p.BasicUnit.Name;
                    ws.Cells[row, 3].Value = p.BasicUnitPrice;
                    ws.Cells[row, 4].Value = p.PackagingUnit?.Name;
                    ws.Cells[row, 5].Value = p.PackagingUnitPrice;
                    ws.Cells[row, 6].Value = p.IsActive ? "Yes" : "No";

                    var activeCell = ws.Cells[row, 6];
                    activeCell.Style.Font.Bold = true;
                    activeCell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    activeCell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    if (p.IsActive)
                    {
                        activeCell.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(240, 253, 244));
                        activeCell.Style.Font.Color.SetColor(Color.FromArgb(21, 128, 61));
                    }
                    else
                    {
                        activeCell.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(248, 250, 252));
                        activeCell.Style.Font.Color.SetColor(Color.FromArgb(100, 116, 139));
                    }

                    row++;
                }
            }
            else
            {
                var regions = await db.Regions
                    .OrderBy(r => r.Name)
                    .AsNoTracking()
                    .ToListAsync(cancellationToken);

                var allMarkups = await db.RegionalMarkupRules
                    .AsNoTracking()
                    .ToListAsync(cancellationToken);

                // region-wide markups: regionId → markup%
                var regionWide = allMarkups
                    .Where(r => r.ProductId == null)
                    .ToDictionary(r => r.RegionId, r => r.MarkupPercentage);

                // product-specific markups: regionId → productId → markup%
                var productSpecific = allMarkups
                    .Where(r => r.ProductId != null)
                    .GroupBy(r => r.RegionId)
                    .ToDictionary(
                        g => g.Key,
                        g => g.ToDictionary(r => r.ProductId!.Value, r => r.MarkupPercentage));

                // Headers
                var pricingSheet1Labels = new string[3 + regions.Count];
                pricingSheet1Labels[0] = "PRODUCT NAME";
                pricingSheet1Labels[1] = "UNIT";
                pricingSheet1Labels[2] = "BASIC PRICE";
                for (int i = 0; i < regions.Count; i++)
                {
                    var region = regions[i];
                    pricingSheet1Labels[3 + i] = regionWide.TryGetValue(region.Id, out var pct)
                        ? $"{region.Name.ToUpper()} ({pct:0.##}%)"
                        : region.Name.ToUpper();
                }
                ApplyGreenHeader(ws, pricingSheet1Labels);

                int row = 2;
                foreach (var p in products)
                {
                    ws.Cells[row, 1].Value = p.Name;
                    ws.Cells[row, 2].Value = p.BasicUnit.Name;
                    ws.Cells[row, 3].Value = p.BasicUnitPrice;

                    for (int i = 0; i < regions.Count; i++)
                    {
                        var region = regions[i];

                        decimal? markup = null;
                        if (productSpecific.TryGetValue(region.Id, out var productRules) &&
                            productRules.TryGetValue(p.Id, out var productMarkup))
                            markup = productMarkup;
                        else if (regionWide.TryGetValue(region.Id, out var regionMarkup))
                            markup = regionMarkup;

                        ws.Cells[row, 4 + i].Value = markup.HasValue
                            ? Math.Round(p.BasicUnitPrice * (1 + markup.Value / 100m), 2)
                            : p.BasicUnitPrice;
                    }
                    row++;
                }

                ws.Cells[ws.Dimension.Address].AutoFitColumns();
                for (var i = 1; i <= ws.Dimension.Columns; i++)
                    ws.Column(i).Width += 2;

                // Sheet 2 — packaging unit pricing (only products with a packaging unit)
                var packagingProducts = products.Where(p => p.PackagingUnit != null && p.PackagingUnitPrice.HasValue).ToList();
                if (packagingProducts.Count > 0)
                {
                    var ws2 = package.Workbook.Worksheets.Add("Packaging Unit Pricing");

                    var pricingSheet2Labels = new string[3 + regions.Count];
                    pricingSheet2Labels[0] = "PRODUCT NAME";
                    pricingSheet2Labels[1] = "PACKAGING UNIT";
                    pricingSheet2Labels[2] = "PACKAGING PRICE";
                    for (int i = 0; i < regions.Count; i++)
                    {
                        var region = regions[i];
                        pricingSheet2Labels[3 + i] = regionWide.TryGetValue(region.Id, out var pct)
                            ? $"{region.Name.ToUpper()} ({pct:0.##}%)"
                            : region.Name.ToUpper();
                    }
                    ApplyGreenHeader(ws2, pricingSheet2Labels);

                    int row2 = 2;
                    foreach (var p in packagingProducts)
                    {
                        var basePrice = p.PackagingUnitPrice!.Value;
                        ws2.Cells[row2, 1].Value = p.Name;
                        ws2.Cells[row2, 2].Value = p.PackagingUnit!.Name;
                        ws2.Cells[row2, 3].Value = basePrice;

                        for (int i = 0; i < regions.Count; i++)
                        {
                            var region = regions[i];

                            decimal? markup = null;
                            if (productSpecific.TryGetValue(region.Id, out var productRules) &&
                                productRules.TryGetValue(p.Id, out var productMarkup))
                                markup = productMarkup;
                            else if (regionWide.TryGetValue(region.Id, out var regionMarkup))
                                markup = regionMarkup;

                            ws2.Cells[row2, 4 + i].Value = markup.HasValue
                                ? Math.Round(basePrice * (1 + markup.Value / 100m), 2)
                                : basePrice;
                        }
                        row2++;
                    }

                    ws2.Cells[ws2.Dimension.Address].AutoFitColumns();
                    for (var i = 1; i <= ws2.Dimension.Columns; i++)
                        ws2.Column(i).Width += 2;
                }

                // early return — column widths already applied per sheet above
                var pricingFileName = $"products_pricing_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx";
                return Result.Success(new ExportResult { FileBytes = package.GetAsByteArray(), FileName = pricingFileName });
            }

            ws.Cells[ws.Dimension.Address].AutoFitColumns();
            for (var i = 1; i <= ws.Dimension.Columns; i++)
                ws.Column(i).Width += 2;

            var mode = isPricing ? "pricing" : "catalog";
            var fileName = $"products_{mode}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx";
            return Result.Success(new ExportResult { FileBytes = package.GetAsByteArray(), FileName = fileName });
        }
    }
}

public class ExportProductsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/products/export", async (
            ISender sender,
            [FromQuery] string? search,
            [FromQuery] bool? isActive,
            [FromQuery] string? mode) =>
        {
            var result = await sender.Send(new ExportProducts.Query
            {
                Search = search,
                IsActive = isActive,
                Mode = mode ?? "catalog"
            });

            if (result.IsFailure)
                return Results.BadRequest(result.Error);

            return Results.File(
                result.Value.FileBytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                result.Value.FileName);
        })
        .WithTags("Products")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.General)
        .WithSummary("Export products to Excel")
        .WithDescription(
            "Exports products to an .xlsx file. " +
            "`mode=catalog` (default) — single sheet with product name, unit, basic price, packaging unit, packaging price, and active status. " +
            "`mode=pricing` — single sheet with basic price plus one column per region showing the resolved regional price based on configured markup rules. Region columns are headed with the region name and markup percentage. " +
            "Optional filters: `search` (product name), `isActive` (true/false).")
        .Produces<FileResult>(200)
        .Produces<Error>(400)
        .RequireAuthorization();
    }
}
