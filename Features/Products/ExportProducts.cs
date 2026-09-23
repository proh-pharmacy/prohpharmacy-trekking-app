using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;

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

            if (!isPricing)
            {
                ws.Cells[1, 1].Value = "Product Name";
                ws.Cells[1, 2].Value = "Unit";
                ws.Cells[1, 3].Value = "Basic Price";
                ws.Cells[1, 4].Value = "Packaging Unit";
                ws.Cells[1, 5].Value = "Packaging Price";
                ws.Cells[1, 6].Value = "Active";

                using (var h = ws.Cells[1, 1, 1, 6]) h.Style.Font.Bold = true;

                int row = 2;
                foreach (var p in products)
                {
                    ws.Cells[row, 1].Value = p.Name;
                    ws.Cells[row, 2].Value = p.BasicUnit.Name;
                    ws.Cells[row, 3].Value = p.BasicUnitPrice;
                    ws.Cells[row, 4].Value = p.PackagingUnit?.Name;
                    ws.Cells[row, 5].Value = p.PackagingUnitPrice;
                    ws.Cells[row, 6].Value = p.IsActive ? "Yes" : "No";
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
                ws.Cells[1, 1].Value = "Product Name";
                ws.Cells[1, 2].Value = "Unit";
                ws.Cells[1, 3].Value = "Basic Price";

                for (int i = 0; i < regions.Count; i++)
                {
                    var region = regions[i];
                    var header = regionWide.TryGetValue(region.Id, out var pct)
                        ? $"{region.Name} ({pct:0.##}%)"
                        : region.Name;
                    ws.Cells[1, 4 + i].Value = header;
                }

                using (var h = ws.Cells[1, 1, 1, 3 + regions.Count]) h.Style.Font.Bold = true;

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

                    ws2.Cells[1, 1].Value = "Product Name";
                    ws2.Cells[1, 2].Value = "Packaging Unit";
                    ws2.Cells[1, 3].Value = "Packaging Price";

                    for (int i = 0; i < regions.Count; i++)
                    {
                        var region = regions[i];
                        var header = regionWide.TryGetValue(region.Id, out var pct)
                            ? $"{region.Name} ({pct:0.##}%)"
                            : region.Name;
                        ws2.Cells[1, 4 + i].Value = header;
                    }

                    using (var h = ws2.Cells[1, 1, 1, 3 + regions.Count]) h.Style.Font.Bold = true;

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
