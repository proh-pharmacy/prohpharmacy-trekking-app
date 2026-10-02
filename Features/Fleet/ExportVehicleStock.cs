using System.Drawing;
using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Fleet;

public static class ExportVehicleStock
{
    public class Query : IRequest<Result<ExportResult>>
    {
        public Guid VehicleId { get; set; }
        public Guid? ProductId { get; set; }
        public bool IncludeOutOfStock { get; set; } = true;
    }

    public class ExportResult
    {
        public byte[] FileBytes { get; set; } = [];
        public string FileName { get; set; } = string.Empty;
    }

    internal sealed class ExportRow
    {
        public string ProductName { get; set; } = string.Empty;
        public string BasicUnitName { get; set; } = string.Empty;
        public decimal BasicQuantityOnHand { get; set; }
        public string? PackagingUnitName { get; set; }
        public decimal PackagingQuantityOnHand { get; set; }
        public decimal BasicUnitPrice { get; set; }
        public decimal? PackagingUnitPrice { get; set; }
        public decimal? LowStockThreshold { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    internal sealed class Handler(AppDbContext db) : IRequestHandler<Query, Result<ExportResult>>
    {
        public async Task<Result<ExportResult>> Handle(Query request, CancellationToken cancellationToken)
        {
            var vehicle = await db.Vehicles.AsNoTracking()
                .Where(v => v.Id == request.VehicleId)
                .Select(v => new
                {
                    v.DisplayName,
                    v.RegistrationNumber,
                    RegionName = v.Region.Name
                })
                .SingleOrDefaultAsync(cancellationToken);
            if (vehicle is null)
                return Result.Failure<ExportResult>(Error.CreateNotFoundError("Vehicle not found."));

            var query = db.VehicleProductStocks
                .Where(s => s.VehicleId == request.VehicleId)
                .AsNoTracking();

            if (request.ProductId.HasValue)
                query = query.Where(s => s.ProductId == request.ProductId.Value);

            if (!request.IncludeOutOfStock)
                query = query.Where(s => s.BasicQuantityOnHand > 0 || s.PackagingQuantityOnHand > 0);

            var rows = await query
                .OrderBy(s => s.Product.Name)
                .Select(s => new ExportRow
                {
                    ProductName = s.Product.Name,
                    BasicUnitName = s.Product.BasicUnit.Name,
                    BasicQuantityOnHand = s.BasicQuantityOnHand,
                    PackagingUnitName = s.Product.PackagingUnit != null
                        ? s.Product.PackagingUnit.Name
                        : null,
                    PackagingQuantityOnHand = s.PackagingQuantityOnHand,
                    BasicUnitPrice = s.Product.BasicUnitPrice,
                    PackagingUnitPrice = s.Product.PackagingUnitPrice,
                    LowStockThreshold = s.LowStockThreshold,
                    UpdatedAt = s.UpdatedAt
                })
                .ToListAsync(cancellationToken);

            using var package = new ExcelPackage();
            var vehicleInfo = $"{vehicle.RegionName} - {vehicle.DisplayName} ({vehicle.RegistrationNumber})";
            var sheet = package.Workbook.Worksheets.Add("Vehicle Stock");
            WriteSheet(sheet, rows, vehicleInfo);

            var fileName = $"vehicle_stock_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx";
            return Result.Success(new ExportResult
            {
                FileBytes = package.GetAsByteArray(),
                FileName = fileName
            });
        }

        private static void WriteSheet(
            ExcelWorksheet sheet,
            IReadOnlyCollection<ExportRow> rows,
            string vehicleInfo)
        {
            const int columnCount = 7;
            var brandGreen = Color.FromArgb(0, 191, 111);
            var darkText = Color.FromArgb(30, 41, 59);
            var borderColor = Color.FromArgb(226, 232, 240);
            var lowStockText = Color.FromArgb(185, 28, 28);
            var zeroText = Color.FromArgb(100, 116, 139);

            sheet.Cells[1, 1, 1, columnCount].Merge = true;
            sheet.Cells[1, 1].Value = "Proh Pharmacy - Vehicle Stock Snapshot";
            sheet.Cells[1, 1].Style.Font.Bold = true;
            sheet.Cells[1, 1].Style.Font.Size = 15;
            sheet.Cells[1, 1].Style.Font.Color.SetColor(darkText);

            sheet.Cells[2, 1, 2, columnCount].Merge = true;
            sheet.Cells[2, 1].Value = $"Vehicle: {vehicleInfo}";
            sheet.Cells[3, 1, 3, columnCount].Merge = true;
            sheet.Cells[3, 1].Value = $"Generated: {DateTime.UtcNow:dd MMM yyyy, hh:mm tt} GMT | Products: {rows.Count}";

            string[] headers =
            [
                "PRODUCT", "ON HAND", "BASIC UNIT PRICE (GHS)", "PACKAGING UNIT PRICE (GHS)",
                "LOW STOCK THRESHOLD", "LAST UPDATED (GMT)", "STATUS"
            ];

            const int headerRow = 5;
            for (var column = 0; column < headers.Length; column++)
            {
                var cell = sheet.Cells[headerRow, column + 1];
                cell.Value = headers[column];
                cell.Style.Font.Bold = true;
                cell.Style.Font.Color.SetColor(Color.White);
                cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                cell.Style.Fill.BackgroundColor.SetColor(brandGreen);
                cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                cell.Style.Border.BorderAround(ExcelBorderStyle.Thin, borderColor);
            }

            var rowNumber = headerRow + 1;
            foreach (var row in rows)
            {
                var onHand = FormatQuantities(
                    row.BasicQuantityOnHand, row.BasicUnitName,
                    row.PackagingQuantityOnHand, row.PackagingUnitName);
                var isZero = row.BasicQuantityOnHand == 0 && row.PackagingQuantityOnHand == 0;
                var isLow = !isZero && row.LowStockThreshold.HasValue &&
                    row.BasicQuantityOnHand <= row.LowStockThreshold.Value;

                sheet.Cells[rowNumber, 1].Value = row.ProductName;
                sheet.Cells[rowNumber, 2].Value = onHand;
                sheet.Cells[rowNumber, 3].Value = row.BasicUnitPrice;
                sheet.Cells[rowNumber, 3].Style.Numberformat.Format = "#,##0.00";
                if (row.PackagingUnitPrice.HasValue)
                {
                    sheet.Cells[rowNumber, 4].Value = row.PackagingUnitPrice.Value;
                    sheet.Cells[rowNumber, 4].Style.Numberformat.Format = "#,##0.00";
                }
                else
                {
                    sheet.Cells[rowNumber, 4].Value = "-";
                }
                sheet.Cells[rowNumber, 5].Value = row.LowStockThreshold.HasValue
                    ? FormatQuantity(row.LowStockThreshold.Value, row.BasicUnitName)
                    : "-";
                if (row.UpdatedAt.HasValue)
                {
                    sheet.Cells[rowNumber, 6].Value = row.UpdatedAt.Value;
                    sheet.Cells[rowNumber, 6].Style.Numberformat.Format = "dd mmm yyyy, hh:mm AM/PM";
                }
                else
                {
                    sheet.Cells[rowNumber, 6].Value = "-";
                }
                sheet.Cells[rowNumber, 7].Value = isZero ? "Out of stock" : isLow ? "Low stock" : "In stock";

                if (isZero)
                {
                    sheet.Cells[rowNumber, 2].Style.Font.Color.SetColor(zeroText);
                    sheet.Cells[rowNumber, 7].Style.Font.Color.SetColor(zeroText);
                }
                else if (isLow)
                {
                    sheet.Cells[rowNumber, 2].Style.Font.Bold = true;
                    sheet.Cells[rowNumber, 2].Style.Font.Color.SetColor(lowStockText);
                    sheet.Cells[rowNumber, 7].Style.Font.Bold = true;
                    sheet.Cells[rowNumber, 7].Style.Font.Color.SetColor(lowStockText);
                }

                rowNumber++;
            }

            sheet.Cells[headerRow, 1, Math.Max(headerRow, rowNumber - 1), columnCount].AutoFilter = true;
            sheet.View.FreezePanes(headerRow + 1, 1);
            sheet.Cells[sheet.Dimension.Address].AutoFitColumns();
            for (var column = 1; column <= columnCount; column++)
                sheet.Column(column).Width = Math.Min(sheet.Column(column).Width + 2, 45);
        }

        private static string FormatQuantities(
            decimal basicQuantity,
            string basicUnitName,
            decimal packagingQuantity,
            string? packagingUnitName)
        {
            var parts = new List<string>();
            if (packagingQuantity != 0 && !string.IsNullOrWhiteSpace(packagingUnitName))
                parts.Add(FormatQuantity(packagingQuantity, packagingUnitName));
            if (basicQuantity != 0)
                parts.Add(FormatQuantity(basicQuantity, basicUnitName));
            if (parts.Count == 0)
                parts.Add(FormatQuantity(0, basicUnitName));
            return string.Join(", ", parts);
        }

        private static string FormatQuantity(decimal quantity, string unitName) =>
            $"{quantity:#,##0.###} {PluralizeUnitName(unitName, quantity)}";

        private static string PluralizeUnitName(string unitName, decimal quantity)
        {
            if (quantity == 1)
                return unitName;

            var ofIndex = unitName.IndexOf(" of ", StringComparison.OrdinalIgnoreCase);
            var unitPart = ofIndex >= 0 ? unitName[..ofIndex] : unitName;
            var suffix = ofIndex >= 0 ? unitName[ofIndex..] : string.Empty;
            var lastSpace = unitPart.LastIndexOf(' ');
            var prefix = lastSpace >= 0 ? unitPart[..(lastSpace + 1)] : string.Empty;
            var word = lastSpace >= 0 ? unitPart[(lastSpace + 1)..] : unitPart;

            if (word.EndsWith("s", StringComparison.OrdinalIgnoreCase))
                return unitName;

            var plural = word.EndsWith("y", StringComparison.OrdinalIgnoreCase) &&
                         word.Length > 1 && !"aeiou".Contains(char.ToLowerInvariant(word[^2]))
                ? word[..^1] + "ies"
                : word.EndsWith("ch", StringComparison.OrdinalIgnoreCase) ||
                  word.EndsWith("sh", StringComparison.OrdinalIgnoreCase) ||
                  word.EndsWith("x", StringComparison.OrdinalIgnoreCase) ||
                  word.EndsWith("z", StringComparison.OrdinalIgnoreCase)
                    ? word + "es"
                    : word + "s";

            return prefix + plural + suffix;
        }
    }
}

public class ExportVehicleStockEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/vehicles/{vehicleId:guid}/stock/export", async (
            Guid vehicleId,
            ISender sender,
            [FromQuery] Guid? productId,
            [FromQuery] bool? includeOutOfStock) =>
        {
            var result = await sender.Send(new ExportVehicleStock.Query
            {
                VehicleId = vehicleId,
                ProductId = productId,
                IncludeOutOfStock = includeOutOfStock ?? true
            });

            if (result.IsFailure)
                return result.Error.Code == "404"
                    ? Results.NotFound(result.Error)
                    : Results.BadRequest(result.Error);

            return Results.File(
                result.Value.FileBytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                result.Value.FileName);
        })
        .WithTags("Fleet")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Fleet)
        .WithSummary("Export current vehicle warehouse stock to Excel")
        .WithDescription("One row per VehicleProductStock entry: product, on-hand quantities (basic + packaging), unit prices, low-stock threshold, last-updated timestamp and derived status. Optional filters: productId, includeOutOfStock (default true).")
        .Produces<FileResult>(200)
        .Produces<Error>(400)
        .Produces<Error>(404)
        .RequireAuthorization();
    }
}
