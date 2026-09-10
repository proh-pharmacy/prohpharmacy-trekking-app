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

namespace prohpharmacy_trekking_app.Features.Ledger;

public static class ExportLedgerSummary
{
    public class Query : IRequest<Result<byte[]>>
    {
        public DateOnly? From { get; set; }
        public DateOnly? To { get; set; }
        public bool? HasBalance { get; set; }
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

            var baseQuery = db.CustomerAccounts.AsNoTracking();

            if (request.BranchId.HasValue)
                baseQuery = baseQuery.Where(ca => ca.OwningBranchId == request.BranchId.Value);

            if (request.RegionId.HasValue)
                baseQuery = baseQuery.Where(ca => ca.RegionId == request.RegionId.Value);

            var rows = await baseQuery.Select(ca => new
            {
                ca.CustomerCode,
                ca.BusinessName,
                RegionName = (string?)ca.Region!.Name,
                BranchName = (string?)ca.OwningBranch!.Name,
                TotalDebits = db.CustomerLedgerEntries
                    .Where(e => e.CustomerAccountId == ca.Id
                        && e.EntryType == LedgerEntryType.Debit
                        && (fromUtc == null || e.RecordedAt >= fromUtc)
                        && (toUtc == null || e.RecordedAt <= toUtc))
                    .Sum(e => (decimal?)e.Amount) ?? 0,
                TotalCredits = db.CustomerLedgerEntries
                    .Where(e => e.CustomerAccountId == ca.Id
                        && e.EntryType == LedgerEntryType.Credit
                        && (fromUtc == null || e.RecordedAt >= fromUtc)
                        && (toUtc == null || e.RecordedAt <= toUtc))
                    .Sum(e => (decimal?)e.Amount) ?? 0,
            })
            .ToListAsync(cancellationToken);

            var data = rows
                .Select(r => new
                {
                    r.CustomerCode,
                    r.BusinessName,
                    r.RegionName,
                    r.BranchName,
                    r.TotalDebits,
                    r.TotalCredits,
                    CurrentBalance = r.TotalDebits - r.TotalCredits
                })
                .Where(r => request.HasBalance != true || r.CurrentBalance > 0)
                .OrderByDescending(r => r.CurrentBalance)
                .ToList();

            var brandGreen = Color.FromArgb(0, 191, 111);
            var headerText = Color.White;
            var lightGray = Color.FromArgb(245, 245, 245);
            var borderColor = Color.FromArgb(226, 232, 240);

            using var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add("Ledger Summary");

            // ── Title block ──────────────────────────────────────────────────
            ws.Cells["A1:G1"].Merge = true;
            ws.Cells["A1"].Value = "Proh Pharmacy — Customer Ledger Summary";
            ws.Cells["A1"].Style.Font.Bold = true;
            ws.Cells["A1"].Style.Font.Size = 14;
            ws.Cells["A1"].Style.Font.Color.SetColor(Color.FromArgb(30, 41, 59));

            ws.Cells["A2:G2"].Merge = true;
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

            ws.Cells["A3:G3"].Merge = true;
            ws.Cells["A3"].Value = $"Generated: {DateTime.UtcNow:dd MMM yyyy HH:mm} UTC  |  Customers: {data.Count}";
            ws.Cells["A3"].Style.Font.Size = 9;
            ws.Cells["A3"].Style.Font.Color.SetColor(Color.FromArgb(100, 116, 139));

            // ── Column headers ───────────────────────────────────────────────
            var headers = new[] { "#", "Customer Code", "Business Name", "Region", "Branch", "Total Debits (GHS)", "Total Credits (GHS)", "Outstanding Balance (GHS)" };
            ws.Cells["A:H"].Style.Font.Name = "Calibri";

            // extend merge to H for 8 columns
            ws.Cells["A1:H1"].Merge = true;
            ws.Cells["A2:H2"].Merge = true;
            ws.Cells["A3:H3"].Merge = true;

            int headerRow = 5;
            for (int c = 0; c < headers.Length; c++)
            {
                var cell = ws.Cells[headerRow, c + 1];
                cell.Value = headers[c];
                cell.Style.Font.Bold = true;
                cell.Style.Font.Color.SetColor(headerText);
                cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                cell.Style.Fill.BackgroundColor.SetColor(brandGreen);
                cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                cell.Style.Border.BorderAround(ExcelBorderStyle.Thin, borderColor);
            }

            // ── Data rows ────────────────────────────────────────────────────
            for (int i = 0; i < data.Count; i++)
            {
                var row = data[i];
                int r = headerRow + 1 + i;
                bool isAlt = i % 2 == 1;

                void SetCell(int col, object? value, bool isNumber = false, bool isBold = false)
                {
                    var cell = ws.Cells[r, col];
                    cell.Value = value;
                    if (isAlt)
                    {
                        cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                        cell.Style.Fill.BackgroundColor.SetColor(lightGray);
                    }
                    if (isNumber) cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                    if (isBold) cell.Style.Font.Bold = true;
                    cell.Style.Border.BorderAround(ExcelBorderStyle.Thin, borderColor);
                }

                SetCell(1, i + 1);
                SetCell(2, row.CustomerCode);
                SetCell(3, row.BusinessName);
                SetCell(4, row.RegionName ?? "—");
                SetCell(5, row.BranchName ?? "—");
                SetCell(6, row.TotalDebits, isNumber: true);
                SetCell(7, row.TotalCredits, isNumber: true);
                SetCell(8, row.CurrentBalance, isNumber: true, isBold: row.CurrentBalance != 0);

                if (row.CurrentBalance > 0)
                {
                    ws.Cells[r, 8].Style.Font.Color.SetColor(Color.FromArgb(21, 128, 61));
                    ws.Cells[r, 8].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    ws.Cells[r, 8].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(240, 253, 244));
                }
                else if (row.CurrentBalance < 0)
                {
                    ws.Cells[r, 8].Style.Font.Color.SetColor(Color.FromArgb(185, 28, 28));
                    ws.Cells[r, 8].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    ws.Cells[r, 8].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(254, 242, 242));
                }

                // Format currency columns
                ws.Cells[r, 6].Style.Numberformat.Format = "#,##0.00";
                ws.Cells[r, 7].Style.Numberformat.Format = "#,##0.00";
                ws.Cells[r, 8].Style.Numberformat.Format = "#,##0.00";
            }

            // ── Totals row ───────────────────────────────────────────────────
            int totalsRow = headerRow + 1 + data.Count;
            ws.Cells[totalsRow, 1, totalsRow, 5].Merge = true;
            ws.Cells[totalsRow, 1].Value = "TOTAL";
            ws.Cells[totalsRow, 1].Style.Font.Bold = true;
            ws.Cells[totalsRow, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;

            void TotalCell(int col, decimal value, bool applyBalanceColor = false)
            {
                var cell = ws.Cells[totalsRow, col];
                cell.Value = value;
                cell.Style.Font.Bold = true;
                cell.Style.Numberformat.Format = "#,##0.00";
                cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                cell.Style.Border.BorderAround(ExcelBorderStyle.Medium, brandGreen);

                if (applyBalanceColor && value > 0)
                {
                    cell.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(240, 253, 244));
                    cell.Style.Font.Color.SetColor(Color.FromArgb(21, 128, 61));
                }
                else if (applyBalanceColor && value < 0)
                {
                    cell.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(254, 242, 242));
                    cell.Style.Font.Color.SetColor(Color.FromArgb(185, 28, 28));
                }
                else
                {
                    cell.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(240, 253, 244));
                }
            }

            TotalCell(6, data.Sum(r => r.TotalDebits));
            TotalCell(7, data.Sum(r => r.TotalCredits));
            TotalCell(8, data.Sum(r => r.CurrentBalance), applyBalanceColor: true);

            // ── Column widths ────────────────────────────────────────────────
            ws.Column(1).Width = 5;
            ws.Column(2).Width = 16;
            ws.Column(3).Width = 32;
            ws.Column(4).Width = 20;
            ws.Column(5).Width = 20;
            ws.Column(6).Width = 22;
            ws.Column(7).Width = 22;
            ws.Column(8).Width = 26;

            ws.Row(1).Height = 22;
            ws.View.FreezePanes(headerRow + 1, 1);

            return Result.Success(package.GetAsByteArray());
        }
    }
}

public class ExportLedgerSummaryEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/ledger/export", async (
            ISender sender,
            HttpContext ctx,
            [FromQuery] DateOnly? from,
            [FromQuery] DateOnly? to,
            [FromQuery] bool? hasBalance,
            [FromQuery] Guid? branchId,
            [FromQuery] Guid? regionId) =>
        {
            var result = await sender.Send(new ExportLedgerSummary.Query
            {
                From = from,
                To = to,
                HasBalance = hasBalance,
                BranchId = branchId,
                RegionId = regionId
            });

            if (result.IsFailure)
                return Results.BadRequest(result.Error);

            var filename = $"LedgerSummary-{DateTime.UtcNow:yyyyMMdd}.xlsx";
            ctx.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{filename}\"";
            return Results.File(result.Value,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        })
        .WithTags("Ledger")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Ledger)
        .WithSummary("Export ledger summary to Excel")
        .WithDescription("Downloads a .xlsx report of customer balances. Filter by date range (from/to), hasBalance=true for debtors only, branchId, or regionId.")
        .Produces<Error>(400)
        .RequireAuthorization();
    }
}
