using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Products.Entities;
using UnitEntity = prohpharmacy_trekking_app.Features.Units.Entities.Unit;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Products;

public static class ImportProducts
{
    public class Command : IRequest<Result<ImportResult>>
    {
        public byte[] FileBytes { get; set; } = [];
        public string ProductNameColumn { get; set; } = string.Empty;
        public string UnitColumn { get; set; } = string.Empty;
    }

    public class ImportResult
    {
        public int Imported { get; set; }
        public int Skipped { get; set; }
        public int UnitsCreated { get; set; }
        public List<string> SkippedNames { get; set; } = [];
    }

    internal sealed class Handler(AppDbContext db) : IRequestHandler<Command, Result<ImportResult>>
    {
        public async Task<Result<ImportResult>> Handle(Command request, CancellationToken cancellationToken)
        {
            using var stream = new MemoryStream(request.FileBytes);
            using var package = new ExcelPackage(stream);

            var ws = package.Workbook.Worksheets.FirstOrDefault();
            if (ws is null || ws.Dimension is null)
                return Result.Failure<ImportResult>(Error.BadRequest("The uploaded file contains no data."));

            // ── Locate the specified columns by header text ─────────────────
            int productNameCol = -1, unitCol = -1;
            int totalCols = ws.Dimension.Columns;

            for (int c = 1; c <= totalCols; c++)
            {
                var header = ws.Cells[1, c].Text.Trim();
                if (header.Equals(request.ProductNameColumn, StringComparison.OrdinalIgnoreCase))
                    productNameCol = c;
                if (header.Equals(request.UnitColumn, StringComparison.OrdinalIgnoreCase))
                    unitCol = c;
            }

            if (productNameCol == -1)
                return Result.Failure<ImportResult>(Error.BadRequest(
                    $"Column '{request.ProductNameColumn}' was not found in the file header row."));

            if (unitCol == -1)
                return Result.Failure<ImportResult>(Error.BadRequest(
                    $"Column '{request.UnitColumn}' was not found in the file header row."));

            // ── Load existing names once for O(1) duplicate checks ──────────
            var existingProductNames = (await db.Products
                .AsNoTracking()
                .Select(p => p.Name.ToLower())
                .ToListAsync(cancellationToken))
                .ToHashSet();

            var existingUnitNames = (await db.Units
                .AsNoTracking()
                .Select(u => u.Name.ToLower())
                .ToListAsync(cancellationToken))
                .ToHashSet();

            // ── Process rows ────────────────────────────────────────────────
            var result = new ImportResult();
            var now = DateTime.UtcNow;
            int totalRows = ws.Dimension.Rows;

            for (int r = 2; r <= totalRows; r++)
            {
                var productName = ws.Cells[r, productNameCol].Text.Trim();
                if (string.IsNullOrWhiteSpace(productName)) continue;

                // Skip if a product with this name already exists
                if (existingProductNames.Contains(productName.ToLower()))
                {
                    result.Skipped++;
                    result.SkippedNames.Add(productName);
                    continue;
                }

                // Create unit if it doesn't exist yet
                var unitName = ws.Cells[r, unitCol].Text.Trim();
                string? unitValue = null;

                if (!string.IsNullOrWhiteSpace(unitName))
                {
                    unitValue = unitName;

                    if (!existingUnitNames.Contains(unitName.ToLower()))
                    {
                        db.Units.Add(new UnitEntity { Name = unitName, CreatedAt = now });
                        existingUnitNames.Add(unitName.ToLower());
                        result.UnitsCreated++;
                    }
                }

                db.Products.Add(new Product
                {
                    Name = productName,
                    Unit = unitValue,
                    IsActive = true,
                    CreatedAt = now
                });

                // Track within this batch so duplicate rows in the same file are also caught
                existingProductNames.Add(productName.ToLower());
                result.Imported++;
            }

            if (result.Imported > 0 || result.UnitsCreated > 0)
                await db.SaveChangesAsync(cancellationToken);

            return Result.Success(result);
        }
    }
}

public class ImportProductsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/products/import", async (HttpRequest req, ISender sender) =>
        {
            if (!req.HasFormContentType)
                return Results.UnprocessableEntity(Error.BadRequest("Request must be multipart/form-data."));

            var form = await req.ReadFormAsync();
            var file = form.Files.GetFile("file");
            var productNameColumn = form["productNameColumn"].FirstOrDefault()?.Trim() ?? string.Empty;
            var unitColumn = form["unitColumn"].FirstOrDefault()?.Trim() ?? string.Empty;

            if (file is null || file.Length == 0)
                return Results.UnprocessableEntity(Error.BadRequest("No file provided."));

            if (string.IsNullOrWhiteSpace(productNameColumn))
                return Results.UnprocessableEntity(Error.BadRequest("'productNameColumn' form field is required."));

            if (string.IsNullOrWhiteSpace(unitColumn))
                return Results.UnprocessableEntity(Error.BadRequest("'unitColumn' form field is required."));

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (ext != ".xlsx" && ext != ".xls")
                return Results.UnprocessableEntity(Error.BadRequest("Only .xlsx and .xls files are accepted."));

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);

            var result = await sender.Send(new ImportProducts.Command
            {
                FileBytes = ms.ToArray(),
                ProductNameColumn = productNameColumn,
                UnitColumn = unitColumn
            });

            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Ok(result.Value);
        })
        .DisableAntiforgery()
        .WithTags("Products")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.General)
        .WithSummary("Bulk import products from Excel")
        .WithDescription(
            "Upload an .xlsx/.xls file and specify which column headers map to product name and unit. " +
            "Form fields: `file` (the Excel file), `productNameColumn` (exact header text for product names), `unitColumn` (exact header text for units). " +
            "Rows whose product name already exists in the database are skipped (case-insensitive). " +
            "Units that do not yet exist are created automatically. " +
            "Duplicate product names within the same file are also skipped after the first occurrence.")
        .Produces<ImportProducts.ImportResult>(200)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
