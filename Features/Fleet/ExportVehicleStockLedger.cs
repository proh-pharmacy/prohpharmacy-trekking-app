using System.Drawing;
using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Fleet.Enums;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Fleet;

public static class ExportVehicleStockLedger
{
    public class Query : IRequest<Result<ExportResult>>
    {
        public Guid VehicleId { get; set; }
        public Guid? ProductId { get; set; }
        public string? Source { get; set; }
        public DateOnly? From { get; set; }
        public DateOnly? To { get; set; }
        public string ExportStyle { get; set; } = "worksheet";
    }

    public class ExportResult
    {
        public byte[] FileBytes { get; set; } = [];
        public string FileName { get; set; } = string.Empty;
    }

    internal sealed class ExportRow
    {
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string BasicUnitName { get; set; } = string.Empty;
        public string? PackagingUnitName { get; set; }
        public string ChangeType { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public decimal BasicQtyChange { get; set; }
        public decimal PackagingQtyChange { get; set; }
        public decimal BasicBalanceAfter { get; set; }
        public decimal PackagingBalanceAfter { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string? AuthorName { get; set; }
        public DateTime RecordedAt { get; set; }
    }

    internal sealed class Handler(AppDbContext db) : IRequestHandler<Query, Result<ExportResult>>
    {
        public async Task<Result<ExportResult>> Handle(Query request, CancellationToken cancellationToken)
        {
            var exportStyle = request.ExportStyle.Trim().ToLowerInvariant();
            if (exportStyle is not ("worksheet" or "workbook"))
                return Result.Failure<ExportResult>(
                    Error.BadRequest("exportStyle must be 'worksheet' or 'workbook'."));

            if (request.From.HasValue && request.To.HasValue && request.From.Value > request.To.Value)
                return Result.Failure<ExportResult>(Error.BadRequest("from cannot be later than to."));

            StockChangeSource? source = null;
            if (!string.IsNullOrWhiteSpace(request.Source))
            {
                if (!Enum.TryParse<StockChangeSource>(request.Source, true, out var parsedSource))
                    return Result.Failure<ExportResult>(Error.BadRequest(
                        "source must be ManualLoad, TrekCompletion, ReturnApproval, or StockReset."));
                source = parsedSource;
            }

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

            var fromUtc = request.From.HasValue
                ? DateTime.SpecifyKind(request.From.Value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc)
                : (DateTime?)null;
            var toUtc = request.To.HasValue
                ? DateTime.SpecifyKind(request.To.Value.ToDateTime(TimeOnly.MaxValue), DateTimeKind.Utc)
                : (DateTime?)null;

            var query = db.VehicleStockLedger
                .Where(l => l.VehicleId == request.VehicleId)
                .AsNoTracking();

            if (request.ProductId.HasValue)
                query = query.Where(l => l.ProductId == request.ProductId.Value);
            if (source.HasValue)
                query = query.Where(l => l.Source == source.Value);
            if (fromUtc.HasValue)
                query = query.Where(l => l.RecordedAt >= fromUtc.Value);
            if (toUtc.HasValue)
                query = query.Where(l => l.RecordedAt <= toUtc.Value);

            var rows = await query
                .OrderByDescending(l => l.RecordedAt)
                .Select(l => new ExportRow
                {
                    ProductId = l.ProductId,
                    ProductName = l.Product.Name,
                    BasicUnitName = l.Product.BasicUnit.Name,
                    PackagingUnitName = l.Product.PackagingUnit != null
                        ? l.Product.PackagingUnit.Name
                        : null,
                    ChangeType = l.ChangeType.ToString(),
                    Source = l.Source.ToString(),
                    BasicQtyChange = l.BasicQtyChange,
                    PackagingQtyChange = l.PackagingQtyChange,
                    BasicBalanceAfter = l.BasicBalanceAfter,
                    PackagingBalanceAfter = l.PackagingBalanceAfter,
                    Reason = l.Reason,
                    AuthorName = l.Author != null ? l.Author.FullName : null,
                    RecordedAt = l.RecordedAt
                })
                .ToListAsync(cancellationToken);

            using var package = new ExcelPackage();
            var vehicleInfo = $"{vehicle.RegionName} - {vehicle.DisplayName} ({vehicle.RegistrationNumber})";
            var period = (request.From, request.To) switch
            {
                ({ } from, { } to) => $"{from:dd MMM yyyy} - {to:dd MMM yyyy}",
                ({ } from, null) => $"From {from:dd MMM yyyy}",
                (null, { } to) => $"Up to {to:dd MMM yyyy}",
                _ => "All dates"
            };

            if (exportStyle == "worksheet")
            {
                var sheet = package.Workbook.Worksheets.Add("Stock Ledger");
                WriteSheet(sheet, rows, vehicleInfo, period, "All matching products");
            }
            else
            {
                var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var group in rows.GroupBy(r => new { r.ProductId, r.ProductName }))
                {
                    var sheetName = UniqueSheetName(group.Key.ProductName, usedNames);
                    var sheet = package.Workbook.Worksheets.Add(sheetName);
                    WriteSheet(sheet, group.ToList(), vehicleInfo, period, group.Key.ProductName);
                }

                if (rows.Count == 0)
                {
                    var sheet = package.Workbook.Worksheets.Add("Stock Ledger");
                    WriteSheet(sheet, rows, vehicleInfo, period, "No matching products");
                }
            }

            var fileName = $"vehicle_stock_ledger_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx";
            return Result.Success(new ExportResult
            {
                FileBytes = package.GetAsByteArray(),
                FileName = fileName
            });
        }

        private static void WriteSheet(
            ExcelWorksheet sheet,
            IReadOnlyCollection<ExportRow> rows,
            string vehicleInfo,
            string period,
            string productLabel)
        {
            const int columnCount = 8;
            var brandGreen = Color.FromArgb(0, 191, 111);
            var darkText = Color.FromArgb(30, 41, 59);
            var borderColor = Color.FromArgb(226, 232, 240);
            var additionText = Color.FromArgb(21, 128, 61);
            var reductionText = Color.FromArgb(185, 28, 28);

            sheet.Cells[1, 1, 1, columnCount].Merge = true;
            sheet.Cells[1, 1].Value = "Proh Pharmacy - Vehicle Stock Ledger";
            sheet.Cells[1, 1].Style.Font.Bold = true;
            sheet.Cells[1, 1].Style.Font.Size = 15;
            sheet.Cells[1, 1].Style.Font.Color.SetColor(darkText);

            sheet.Cells[2, 1, 2, columnCount].Merge = true;
            sheet.Cells[2, 1].Value = $"Vehicle: {vehicleInfo}";
            sheet.Cells[3, 1, 3, columnCount].Merge = true;
            sheet.Cells[3, 1].Value = $"Product: {productLabel} | Period: {period} | Entries: {rows.Count}";

            string[] headers =
            [
                "RECORDED AT (GMT)", "PRODUCT", "STOCK ACTION", "SOURCE",
                "QUANTITY", "BALANCE", "REASON", "AUTHOR"
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
                sheet.Cells[rowNumber, 1].Value = row.RecordedAt;
                sheet.Cells[rowNumber, 1].Style.Numberformat.Format = "dd mmm yyyy, hh:mm AM/PM";
                sheet.Cells[rowNumber, 2].Value = row.ProductName;
                sheet.Cells[rowNumber, 3].Value = row.ChangeType;
                sheet.Cells[rowNumber, 4].Value = row.Source;
                sheet.Cells[rowNumber, 5].Value = FormatQuantities(
                    row.BasicQtyChange,
                    row.BasicUnitName,
                    row.PackagingQtyChange,
                    row.PackagingUnitName);
                sheet.Cells[rowNumber, 6].Value = FormatQuantities(
                    row.BasicBalanceAfter,
                    row.BasicUnitName,
                    row.PackagingBalanceAfter,
                    row.PackagingUnitName,
                    includeZeroBasicWhenEmpty: true);
                sheet.Cells[rowNumber, 7].Value = row.Reason;
                sheet.Cells[rowNumber, 8].Value = row.AuthorName ?? "System";

                var isReduction = row.ChangeType.Equals("Reduction", StringComparison.OrdinalIgnoreCase);
                foreach (var column in new[] { 3, 5, 6 })
                {
                    sheet.Cells[rowNumber, column].Style.Font.Bold = true;
                    sheet.Cells[rowNumber, column].Style.Font.Color.SetColor(
                        isReduction ? reductionText : additionText);
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
            string? packagingUnitName,
            bool includeZeroBasicWhenEmpty = false)
        {
            var parts = new List<string>();
            if (packagingQuantity != 0 && !string.IsNullOrWhiteSpace(packagingUnitName))
                parts.Add(FormatQuantity(packagingQuantity, packagingUnitName));
            if (basicQuantity != 0)
                parts.Add(FormatQuantity(basicQuantity, basicUnitName));
            if (parts.Count == 0 && includeZeroBasicWhenEmpty)
                parts.Add(FormatQuantity(0, basicUnitName));

            return parts.Count == 0 ? "0" : string.Join(", ", parts);
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

        private static string UniqueSheetName(string productName, HashSet<string> usedNames)
        {
            var invalid = new HashSet<char>([':', '\\', '/', '?', '*', '[', ']']);
            var cleaned = new string(productName.Select(c => invalid.Contains(c) ? '-' : c).ToArray())
                .Trim()
                .Trim('\'');
            if (string.IsNullOrWhiteSpace(cleaned))
                cleaned = "Product";

            var baseName = cleaned[..Math.Min(cleaned.Length, 31)];
            var candidate = baseName;
            var suffix = 2;
            while (!usedNames.Add(candidate))
            {
                var suffixText = $" ({suffix++})";
                var prefixLength = Math.Min(baseName.Length, 31 - suffixText.Length);
                candidate = baseName[..prefixLength] + suffixText;
            }

            return candidate;
        }
    }
}

public class ExportVehicleStockLedgerEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/vehicles/{vehicleId:guid}/stock/ledger/export", async (
            Guid vehicleId,
            ISender sender,
            [FromQuery] Guid? productId,
            [FromQuery] string? source,
            [FromQuery] DateOnly? from,
            [FromQuery] DateOnly? to,
            [FromQuery] string? exportStyle) =>
        {
            var result = await sender.Send(new ExportVehicleStockLedger.Query
            {
                VehicleId = vehicleId,
                ProductId = productId,
                Source = source,
                From = from,
                To = to,
                ExportStyle = exportStyle ?? "worksheet"
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
        .WithSummary("Export vehicle stock ledger to Excel")
        .WithDescription("Exports the filtered stock cycle. Use exportStyle=worksheet for one combined sheet or exportStyle=workbook for one sheet per product. Optional filters: productId, source, and inclusive from/to dates (YYYY-MM-DD).")
        .Produces<FileResult>(200)
        .Produces<Error>(400)
        .Produces<Error>(404)
        .RequireAuthorization();
    }
}
