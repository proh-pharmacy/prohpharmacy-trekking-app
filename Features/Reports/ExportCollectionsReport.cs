using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Ledger.Enums;
using prohpharmacy_trekking_app.Shared;
using System.Drawing;

namespace prohpharmacy_trekking_app.Features.Reports;

public static class ExportCollectionsReport
{
    public class Query : IRequest<Result<byte[]>>
    {
        public DateOnly? From { get; set; }
        public DateOnly? To { get; set; }
        public Guid? BranchId { get; set; }
        public Guid? RegionId { get; set; }
    }

    internal sealed class Handler(AppDbContext db) : IRequestHandler<Query, Result<byte[]>>
    {
        public async Task<Result<byte[]>> Handle(Query request, CancellationToken cancellationToken)
        {
            var fromUtc = request.From.HasValue
                ? DateTime.SpecifyKind(request.From.Value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc)
                : (DateTime?)null;
            var toUtc = request.To.HasValue
                ? DateTime.SpecifyKind(request.To.Value.ToDateTime(TimeOnly.MaxValue), DateTimeKind.Utc)
                : (DateTime?)null;

            var query = db.CustomerLedgerEntries
                .Where(e => e.EntryType == LedgerEntryType.Credit)
                .AsNoTracking();

            if (fromUtc.HasValue)
                query = query.Where(e => e.RecordedAt >= fromUtc.Value);
            if (toUtc.HasValue)
                query = query.Where(e => e.RecordedAt <= toUtc.Value);
            if (request.BranchId.HasValue)
                query = query.Where(e => e.CustomerAccount.OwningBranchId == request.BranchId.Value);
            if (request.RegionId.HasValue)
                query = query.Where(e => e.CustomerAccount.RegionId == request.RegionId.Value);

            var rows = await query
                .Select(e => new
                {
                    e.Amount,
                    PaymentMethod = e.PaymentMethod ?? "Unspecified",
                    BranchName = e.CustomerAccount.OwningBranch!.Name
                })
                .ToListAsync(cancellationToken);

            var byPaymentMethod = rows
                .GroupBy(r => r.PaymentMethod)
                .Select(g => new { Method = g.Key, Total = g.Sum(r => r.Amount), Count = g.Count() })
                .OrderByDescending(b => b.Total)
                .ToList();

            var byBranch = rows
                .GroupBy(r => r.BranchName)
                .Select(g => new { Branch = g.Key, Total = g.Sum(r => r.Amount), Count = g.Count() })
                .OrderByDescending(b => b.Total)
                .ToList();

            var totalCollected = rows.Sum(r => r.Amount);

            var brandGreen = Color.FromArgb(0, 191, 111);
            var headerText = Color.White;
            var lightGray = Color.FromArgb(245, 245, 245);
            var borderColor = Color.FromArgb(226, 232, 240);

            using var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add("Collections Report");
            ws.Cells["A:C"].Style.Font.Name = "Calibri";

            ws.Cells["A1:C1"].Merge = true;
            ws.Cells["A1"].Value = "Proh Pharmacy — Collections Report";
            ws.Cells["A1"].Style.Font.Bold = true;
            ws.Cells["A1"].Style.Font.Size = 14;
            ws.Cells["A1"].Style.Font.Color.SetColor(Color.FromArgb(30, 41, 59));

            ws.Cells["A2:C2"].Merge = true;
            var periodLabel = (request.From, request.To) switch
            {
                ({ } f, { } t) => $"Period: {f:dd MMM yyyy} — {t:dd MMM yyyy}",
                ({ } f, null) => $"From: {f:dd MMM yyyy}",
                (null, { } t) => $"Up to: {t:dd MMM yyyy}",
                _ => "All dates"
            };
            ws.Cells["A2"].Value = periodLabel;
            ws.Cells["A2"].Style.Font.Size = 10;
            ws.Cells["A2"].Style.Font.Color.SetColor(Color.FromArgb(71, 85, 105));

            ws.Cells["A3:C3"].Merge = true;
            ws.Cells["A3"].Value = $"Generated: {DateTime.UtcNow:dd MMM yyyy HH:mm} UTC  |  Total Collected: GHS {totalCollected:#,##0.00}  |  Transactions: {rows.Count}";
            ws.Cells["A3"].Style.Font.Size = 9;
            ws.Cells["A3"].Style.Font.Color.SetColor(Color.FromArgb(100, 116, 139));

            void WriteSection(int titleRow, string title, IEnumerable<(string Label, decimal Total, int Count)> data)
            {
                ws.Cells[titleRow, 1, titleRow, 3].Merge = true;
                ws.Cells[titleRow, 1].Value = title;
                ws.Cells[titleRow, 1].Style.Font.Bold = true;
                ws.Cells[titleRow, 1].Style.Font.Color.SetColor(headerText);
                ws.Cells[titleRow, 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
                ws.Cells[titleRow, 1].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(30, 41, 59));
                ws.Cells[titleRow, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Left;

                int colHeaderRow = titleRow + 1;
                string[] colHeaders = { "Name", "Total Collected (GHS)", "Transactions" };
                for (int c = 0; c < colHeaders.Length; c++)
                {
                    var cell = ws.Cells[colHeaderRow, c + 1];
                    cell.Value = colHeaders[c];
                    cell.Style.Font.Bold = true;
                    cell.Style.Font.Color.SetColor(headerText);
                    cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    cell.Style.Fill.BackgroundColor.SetColor(brandGreen);
                    cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    cell.Style.Border.BorderAround(ExcelBorderStyle.Thin, borderColor);
                }

                var rows2 = data.ToList();
                for (int i = 0; i < rows2.Count; i++)
                {
                    var (label, total, count) = rows2[i];
                    int r = colHeaderRow + 1 + i;
                    bool isAlt = i % 2 == 1;

                    void Cell(int col, object? value, bool isNumber = false)
                    {
                        var cell = ws.Cells[r, col];
                        cell.Value = value;
                        if (isAlt) { cell.Style.Fill.PatternType = ExcelFillStyle.Solid; cell.Style.Fill.BackgroundColor.SetColor(lightGray); }
                        if (isNumber) cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                        cell.Style.Border.BorderAround(ExcelBorderStyle.Thin, borderColor);
                    }

                    Cell(1, label);
                    Cell(2, total, isNumber: true);
                    ws.Cells[r, 2].Style.Numberformat.Format = "#,##0.00";
                    Cell(3, count);
                }

                int totalsRow = colHeaderRow + 1 + rows2.Count;
                ws.Cells[totalsRow, 1].Value = "TOTAL";
                ws.Cells[totalsRow, 1].Style.Font.Bold = true;
                ws.Cells[totalsRow, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                ws.Cells[totalsRow, 1].Style.Border.BorderAround(ExcelBorderStyle.Thin, borderColor);

                var totalCell = ws.Cells[totalsRow, 2];
                totalCell.Value = rows2.Sum(x => x.Total);
                totalCell.Style.Font.Bold = true;
                totalCell.Style.Numberformat.Format = "#,##0.00";
                totalCell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                totalCell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                totalCell.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(240, 253, 244));
                totalCell.Style.Border.BorderAround(ExcelBorderStyle.Medium, brandGreen);

                var countCell = ws.Cells[totalsRow, 3];
                countCell.Value = rows2.Sum(x => x.Count);
                countCell.Style.Font.Bold = true;
                countCell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                countCell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                countCell.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(240, 253, 244));
                countCell.Style.Border.BorderAround(ExcelBorderStyle.Medium, brandGreen);
            }

            int pm_titleRow = 5;
            WriteSection(pm_titleRow, "COLLECTIONS BY PAYMENT METHOD",
                byPaymentMethod.Select(b => (b.Method, b.Total, b.Count)));

            int branch_titleRow = pm_titleRow + 2 + 1 + byPaymentMethod.Count + 1 + 2;
            WriteSection(branch_titleRow, "COLLECTIONS BY BRANCH",
                byBranch.Select(b => (b.Branch, b.Total, b.Count)));

            ws.Column(1).Width = 30;
            ws.Column(2).Width = 24;
            ws.Column(3).Width = 16;

            ws.Row(1).Height = 22;

            return Result.Success(package.GetAsByteArray());
        }
    }
}

public class ExportCollectionsReportEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/reports/collections/export", async (
            ISender sender,
            HttpContext ctx,
            [FromQuery] DateOnly? from,
            [FromQuery] DateOnly? to,
            [FromQuery] Guid? branchId,
            [FromQuery] Guid? regionId) =>
        {
            var result = await sender.Send(new ExportCollectionsReport.Query
            {
                From = from,
                To = to,
                BranchId = branchId,
                RegionId = regionId
            });

            if (result.IsFailure)
                return Results.BadRequest(result.Error);

            var filename = $"CollectionsReport-{DateTime.UtcNow:yyyyMMdd}.xlsx";
            ctx.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{filename}\"";
            return Results.File(result.Value,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        })
        .WithTags("Reports")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Reports)
        .WithSummary("Export collections report to Excel")
        .WithDescription("Downloads a .xlsx collections report with breakdowns by payment method and by branch. Filter by from/to (RecordedAt), branchId, regionId.")
        .Produces<Error>(400)
        .RequireAuthorization();
    }
}
