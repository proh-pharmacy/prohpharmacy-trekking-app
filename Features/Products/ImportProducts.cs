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
        public string BasicUnitColumn { get; set; } = string.Empty;
        public string? BasicUnitPriceColumn { get; set; }
        public string? PackagingUnitColumn { get; set; }
        public string? PackagingUnitPriceColumn { get; set; }
        public bool AllowUpdate { get; set; } = false;
    }

    public class ImportResult
    {
        public int Imported { get; set; }
        public int Updated { get; set; }
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

            int productNameCol = -1, basicUnitCol = -1, basicUnitPriceCol = -1;
            int packagingUnitCol = -1, packagingUnitPriceCol = -1;
            int totalCols = ws.Dimension.Columns;

            for (int c = 1; c <= totalCols; c++)
            {
                var header = ws.Cells[1, c].Text.Trim();
                if (header.Equals(request.ProductNameColumn, StringComparison.OrdinalIgnoreCase))
                    productNameCol = c;
                if (header.Equals(request.BasicUnitColumn, StringComparison.OrdinalIgnoreCase))
                    basicUnitCol = c;
                if (!string.IsNullOrWhiteSpace(request.BasicUnitPriceColumn) &&
                    header.Equals(request.BasicUnitPriceColumn, StringComparison.OrdinalIgnoreCase))
                    basicUnitPriceCol = c;
                if (!string.IsNullOrWhiteSpace(request.PackagingUnitColumn) &&
                    header.Equals(request.PackagingUnitColumn, StringComparison.OrdinalIgnoreCase))
                    packagingUnitCol = c;
                if (!string.IsNullOrWhiteSpace(request.PackagingUnitPriceColumn) &&
                    header.Equals(request.PackagingUnitPriceColumn, StringComparison.OrdinalIgnoreCase))
                    packagingUnitPriceCol = c;
            }

            if (productNameCol == -1)
                return Result.Failure<ImportResult>(Error.BadRequest(
                    $"Column '{request.ProductNameColumn}' was not found in the file header row."));

            if (basicUnitCol == -1)
                return Result.Failure<ImportResult>(Error.BadRequest(
                    $"Column '{request.BasicUnitColumn}' was not found in the file header row."));

            if (!string.IsNullOrWhiteSpace(request.BasicUnitPriceColumn) && basicUnitPriceCol == -1)
                return Result.Failure<ImportResult>(Error.BadRequest(
                    $"Column '{request.BasicUnitPriceColumn}' was not found in the file header row."));

            if (!string.IsNullOrWhiteSpace(request.PackagingUnitColumn) && packagingUnitCol == -1)
                return Result.Failure<ImportResult>(Error.BadRequest(
                    $"Column '{request.PackagingUnitColumn}' was not found in the file header row."));

            if (!string.IsNullOrWhiteSpace(request.PackagingUnitPriceColumn) && packagingUnitPriceCol == -1)
                return Result.Failure<ImportResult>(Error.BadRequest(
                    $"Column '{request.PackagingUnitPriceColumn}' was not found in the file header row."));

            var existingProductNames = (await db.Products
                .AsNoTracking()
                .Select(p => p.Name.ToLower())
                .ToListAsync(cancellationToken))
                .ToHashSet();

            var existingUnits = (await db.Units
                .AsNoTracking()
                .ToListAsync(cancellationToken))
                .ToDictionary(u => u.Name.ToLower(), u => u);

            var result = new ImportResult();
            var now = DateTime.UtcNow;
            int totalRows = ws.Dimension.Rows;

            for (int r = 2; r <= totalRows; r++)
            {
                var productName = ws.Cells[r, productNameCol].Text.Trim();
                if (string.IsNullOrWhiteSpace(productName)) continue;

                bool isExisting = existingProductNames.Contains(productName.ToLower());

                if (isExisting && !request.AllowUpdate)
                {
                    result.Skipped++;
                    result.SkippedNames.Add(productName);
                    continue;
                }

                var basicUnitName = ws.Cells[r, basicUnitCol].Text.Trim();
                if (string.IsNullOrWhiteSpace(basicUnitName))
                {
                    result.Skipped++;
                    result.SkippedNames.Add(productName);
                    continue;
                }

                if (!existingUnits.TryGetValue(basicUnitName.ToLower(), out var basicUnit))
                {
                    basicUnit = new UnitEntity { Name = basicUnitName, CreatedAt = now };
                    db.Units.Add(basicUnit);
                    existingUnits[basicUnitName.ToLower()] = basicUnit;
                    result.UnitsCreated++;
                }

                decimal basicUnitPrice = 0;
                if (basicUnitPriceCol != -1)
                    decimal.TryParse(ws.Cells[r, basicUnitPriceCol].Text.Trim(), out basicUnitPrice);

                Guid? packagingUnitId = null;
                decimal? packagingUnitPrice = null;

                if (packagingUnitCol != -1)
                {
                    var packagingUnitName = ws.Cells[r, packagingUnitCol].Text.Trim();
                    if (!string.IsNullOrWhiteSpace(packagingUnitName))
                    {
                        if (!existingUnits.TryGetValue(packagingUnitName.ToLower(), out var packagingUnit))
                        {
                            packagingUnit = new UnitEntity { Name = packagingUnitName, CreatedAt = now };
                            db.Units.Add(packagingUnit);
                            existingUnits[packagingUnitName.ToLower()] = packagingUnit;
                            result.UnitsCreated++;
                        }

                        if (packagingUnit.Id != basicUnit.Id)
                        {
                            packagingUnitId = packagingUnit.Id;
                            packagingUnitPrice = packagingUnitPriceCol != -1 &&
                                decimal.TryParse(ws.Cells[r, packagingUnitPriceCol].Text.Trim(), out var p) ? p : null;
                        }
                    }
                }

                if (isExisting)
                {
                    var existingProduct = await db.Products
                        .FirstOrDefaultAsync(p => p.Name.ToLower() == productName.ToLower(), cancellationToken);

                    if (existingProduct is null)
                    {
                        result.Skipped++;
                        result.SkippedNames.Add(productName);
                        continue;
                    }

                    existingProduct.BasicUnitId = basicUnit.Id;
                    existingProduct.BasicUnitPrice = basicUnitPrice;
                    if (packagingUnitCol != -1)
                    {
                        existingProduct.PackagingUnitId = packagingUnitId;
                        existingProduct.PackagingUnitPrice = packagingUnitPrice;
                    }
                    existingProduct.UpdatedAt = now;
                    result.Updated++;
                }
                else
                {
                    db.Products.Add(new Product
                    {
                        Name = productName,
                        BasicUnitId = basicUnit.Id,
                        BasicUnitPrice = basicUnitPrice,
                        PackagingUnitId = packagingUnitId,
                        PackagingUnitPrice = packagingUnitPrice,
                        IsActive = true,
                        CreatedAt = now
                    });

                    existingProductNames.Add(productName.ToLower());
                    result.Imported++;
                }
            }

            if (result.Imported > 0 || result.Updated > 0 || result.UnitsCreated > 0)
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
            var basicUnitColumn = form["basicUnitColumn"].FirstOrDefault()?.Trim() ?? string.Empty;
            var basicUnitPriceColumn = form["basicUnitPriceColumn"].FirstOrDefault()?.Trim();
            var packagingUnitColumn = form["packagingUnitColumn"].FirstOrDefault()?.Trim();
            var packagingUnitPriceColumn = form["packagingUnitPriceColumn"].FirstOrDefault()?.Trim();
            bool.TryParse(form["allowUpdate"].FirstOrDefault(), out var allowUpdate);

            if (file is null || file.Length == 0)
                return Results.UnprocessableEntity(Error.BadRequest("No file provided."));

            if (string.IsNullOrWhiteSpace(productNameColumn))
                return Results.UnprocessableEntity(Error.BadRequest("'productNameColumn' form field is required."));

            if (string.IsNullOrWhiteSpace(basicUnitColumn))
                return Results.UnprocessableEntity(Error.BadRequest("'basicUnitColumn' form field is required."));

var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (ext != ".xlsx" && ext != ".xls")
                return Results.UnprocessableEntity(Error.BadRequest("Only .xlsx and .xls files are accepted."));

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);

            var result = await sender.Send(new ImportProducts.Command
            {
                FileBytes = ms.ToArray(),
                ProductNameColumn = productNameColumn,
                BasicUnitColumn = basicUnitColumn,
                BasicUnitPriceColumn = string.IsNullOrWhiteSpace(basicUnitPriceColumn) ? null : basicUnitPriceColumn,
                PackagingUnitColumn = string.IsNullOrWhiteSpace(packagingUnitColumn) ? null : packagingUnitColumn,
                PackagingUnitPriceColumn = string.IsNullOrWhiteSpace(packagingUnitPriceColumn) ? null : packagingUnitPriceColumn,
                AllowUpdate = allowUpdate
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
            "Upload an .xlsx/.xls file and specify which column headers map to each field. " +
            "Required form fields: `file`, `productNameColumn`, `basicUnitColumn`. " +
            "Optional form fields: `basicUnitPriceColumn`, `packagingUnitColumn`, `packagingUnitPriceColumn`, `allowUpdate`. " +
            "Set `allowUpdate=true` to update existing products instead of skipping them. " +
            "Units that do not yet exist are created automatically. " +
            "Rows missing a product name or basic unit are skipped.")
        .Produces<ImportProducts.ImportResult>(200)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
