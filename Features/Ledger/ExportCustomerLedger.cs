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

public static class ExportCustomerLedger
{
    public class Query : IRequest<Result<(byte[] File, string CustomerCode)>>
    {
        public Guid CustomerId { get; set; }
        public DateOnly? From { get; set; }
        public DateOnly? To { get; set; }
    }

    internal sealed class Handler(AppDbContext db)
        : IRequestHandler<Query, Result<(byte[] File, string CustomerCode)>>
    {
        public async Task<Result<(byte[] File, string CustomerCode)>> Handle(Query request, CancellationToken cancellationToken)
        {
            var customer = await db.CustomerAccounts
                .Include(ca => ca.Region)
                .Include(ca => ca.OwningBranch)
                .AsNoTracking()
                .FirstOrDefaultAsync(ca => ca.Id == request.CustomerId, cancellationToken);

            if (customer is null)
                return Result.Failure<(byte[], string)>(Error.CreateNotFoundError("Customer not found."));

            var fromUtc = request.From.HasValue
                ? DateTime.SpecifyKind(request.From.Value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc)
                : (DateTime?)null;

            var toUtc = request.To.HasValue
                ? DateTime.SpecifyKind(request.To.Value.ToDateTime(TimeOnly.MaxValue), DateTimeKind.Utc)
                : (DateTime?)null;

            var entries = await db.CustomerLedgerEntries
                .Include(e => e.TrekkingTrip)
                .Include(e => e.CreatedBy)
                .Where(e => e.CustomerAccountId == request.CustomerId
                    && (fromUtc == null || e.RecordedAt >= fromUtc)
                    && (toUtc == null || e.RecordedAt <= toUtc))
                .OrderBy(e => e.RecordedAt)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var totalDebits = entries.Where(e => e.EntryType == LedgerEntryType.Debit).Sum(e => e.Amount);
            var totalCredits = entries.Where(e => e.EntryType == LedgerEntryType.Credit).Sum(e => e.Amount);
            var balance = totalDebits - totalCredits;

            var brandGreen = Color.FromArgb(0, 191, 111);
            var headerText = Color.White;
            var lightGray = Color.FromArgb(245, 245, 245);
            var borderColor = Color.FromArgb(226, 232, 240);
            var slateText = Color.FromArgb(71, 85, 105);
            var darkSlate = Color.FromArgb(30, 41, 59);

            using var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add("Ledger Statement");
            ws.Cells["A:H"].Style.Font.Name = "Calibri";

            // ── Customer info block ──────────────────────────────────────────
            ws.Cells["A1:H1"].Merge = true;
            ws.Cells["A1"].Value = "Proh Pharmacy — Customer Ledger Statement";
            ws.Cells["A1"].Style.Font.Bold = true;
            ws.Cells["A1"].Style.Font.Size = 14;
            ws.Cells["A1"].Style.Font.Color.SetColor(darkSlate);

            void InfoRow(int row, string label, string value)
            {
                ws.Cells[row, 1].Value = label;
                ws.Cells[row, 1].Style.Font.Bold = true;
                ws.Cells[row, 1].Style.Font.Color.SetColor(slateText);
                ws.Cells[row, 2, row, 5].Merge = true;
                ws.Cells[row, 2].Value = value;
                ws.Cells[row, 2].Style.Font.Color.SetColor(darkSlate);
            }

            InfoRow(2, "Customer:", $"{customer.BusinessName} ({customer.CustomerCode})");
            InfoRow(3, "Phone:", customer.PrimaryPhoneNumber ?? "—");
            InfoRow(4, "Region:", customer.Region?.Name ?? "—");
            InfoRow(5, "Branch:", customer.OwningBranch?.Name ?? "—");

            var periodLabel = (request.From, request.To) switch
            {
                ({ } f, { } t) => $"{f:dd MMM yyyy} — {t:dd MMM yyyy}",
                ({ } f, null) => $"From {f:dd MMM yyyy}",
                (null, { } t) => $"Up to {t:dd MMM yyyy}",
                _ => "All dates"
            };
            InfoRow(6, "Period:", periodLabel);
            InfoRow(7, "Generated:", $"{DateTime.UtcNow:dd MMM yyyy HH:mm} UTC");

            // ── Column headers ───────────────────────────────────────────────
            int headerRow = 9;
            var headers = new[] { "Date", "Type", "Payment Method", "Description", "Trek No.", "Debit (GHS)", "Credit (GHS)", "Recorded By" };

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

            // ── Entry rows ───────────────────────────────────────────────────
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                int r = headerRow + 1 + i;
                bool isAlt = i % 2 == 1;

                void Cell(int col, object? value, bool isNumber = false, bool isBold = false)
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

                Cell(1, entry.RecordedAt.ToString("dd MMM yyyy"));
                Cell(2, entry.EntryType.ToString());
                Cell(3, entry.PaymentMethod ?? "—");
                Cell(4, entry.Description);
                Cell(5, entry.TrekkingTrip?.TrekNumber ?? "—");

                if (entry.EntryType == LedgerEntryType.Debit)
                {
                    ws.Cells[r, 6].Value = entry.Amount;
                    ws.Cells[r, 6].Style.Numberformat.Format = "#,##0.00";
                    ws.Cells[r, 6].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                    ws.Cells[r, 6].Style.Font.Color.SetColor(Color.FromArgb(185, 28, 28));
                    if (isAlt) { ws.Cells[r, 6].Style.Fill.PatternType = ExcelFillStyle.Solid; ws.Cells[r, 6].Style.Fill.BackgroundColor.SetColor(lightGray); }
                    ws.Cells[r, 6].Style.Border.BorderAround(ExcelBorderStyle.Thin, borderColor);
                    Cell(7, null);
                }
                else
                {
                    Cell(6, null);
                    ws.Cells[r, 7].Value = entry.Amount;
                    ws.Cells[r, 7].Style.Numberformat.Format = "#,##0.00";
                    ws.Cells[r, 7].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                    ws.Cells[r, 7].Style.Font.Color.SetColor(Color.FromArgb(21, 128, 61));
                    if (isAlt) { ws.Cells[r, 7].Style.Fill.PatternType = ExcelFillStyle.Solid; ws.Cells[r, 7].Style.Fill.BackgroundColor.SetColor(lightGray); }
                    ws.Cells[r, 7].Style.Border.BorderAround(ExcelBorderStyle.Thin, borderColor);
                }

                Cell(8, entry.CreatedBy?.FullName ?? "—");
            }

            // ── Summary block ────────────────────────────────────────────────
            int summaryRow = headerRow + entries.Count + 2;

            void SummaryRow(int row, string label, decimal value, bool highlight = false)
            {
                ws.Cells[row, 1, row, 5].Merge = true;
                ws.Cells[row, 1].Value = label;
                ws.Cells[row, 1].Style.Font.Bold = true;
                ws.Cells[row, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                ws.Cells[row, 1].Style.Font.Color.SetColor(slateText);

                var cell = ws.Cells[row, highlight ? 6 : 7];
                cell.Value = value;
                cell.Style.Font.Bold = true;
                cell.Style.Numberformat.Format = "#,##0.00";
                cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                cell.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(240, 253, 244));
                cell.Style.Border.BorderAround(ExcelBorderStyle.Medium, brandGreen);
                if (highlight && value > 0) cell.Style.Font.Color.SetColor(Color.FromArgb(185, 28, 28));
            }

            SummaryRow(summaryRow, "Total Debits:", totalDebits, highlight: true);
            SummaryRow(summaryRow + 1, "Total Credits:", totalCredits);

            ws.Cells[summaryRow + 2, 1, summaryRow + 2, 5].Merge = true;
            ws.Cells[summaryRow + 2, 1].Value = "Outstanding Balance:";
            ws.Cells[summaryRow + 2, 1].Style.Font.Bold = true;
            ws.Cells[summaryRow + 2, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
            ws.Cells[summaryRow + 2, 1].Style.Font.Color.SetColor(darkSlate);
            var balCell = ws.Cells[summaryRow + 2, 6, summaryRow + 2, 7];
            balCell.Merge = true;
            balCell.Value = balance;
            balCell.Style.Font.Bold = true;
            balCell.Style.Font.Size = 12;
            balCell.Style.Numberformat.Format = "#,##0.00";
            balCell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
            balCell.Style.Fill.PatternType = ExcelFillStyle.Solid;
            balCell.Style.Fill.BackgroundColor.SetColor(balance > 0 ? Color.FromArgb(254, 242, 242) : Color.FromArgb(240, 253, 244));
            balCell.Style.Font.Color.SetColor(balance > 0 ? Color.FromArgb(185, 28, 28) : Color.FromArgb(21, 128, 61));
            balCell.Style.Border.BorderAround(ExcelBorderStyle.Medium, balance > 0 ? Color.FromArgb(185, 28, 28) : brandGreen);

            // ── Column widths ────────────────────────────────────────────────
            ws.Column(1).Width = 18;
            ws.Column(2).Width = 12;
            ws.Column(3).Width = 18;
            ws.Column(4).Width = 38;
            ws.Column(5).Width = 14;
            ws.Column(6).Width = 18;
            ws.Column(7).Width = 18;
            ws.Column(8).Width = 22;

            ws.Row(1).Height = 22;
            ws.View.FreezePanes(headerRow + 1, 1);

            return Result.Success((package.GetAsByteArray(), customer.CustomerCode));
        }
    }
}

public class ExportCustomerLedgerEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/customers/{customerId:guid}/ledger/export", async (
            Guid customerId,
            ISender sender,
            HttpContext ctx,
            [FromQuery] DateOnly? from,
            [FromQuery] DateOnly? to) =>
        {
            var result = await sender.Send(new ExportCustomerLedger.Query
            {
                CustomerId = customerId,
                From = from,
                To = to
            });

            if (result.IsFailure)
                return Results.NotFound(result.Error);

            var (file, code) = result.Value;
            var filename = $"Ledger-{code}-{DateTime.UtcNow:yyyyMMdd}.xlsx";
            ctx.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{filename}\"";
            return Results.File(file,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        })
        .WithTags("Ledger")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Ledger)
        .WithSummary("Export individual customer ledger as Excel")
        .WithDescription("Downloads a .xlsx statement for a single customer. Optionally filter by from/to date range (filters by RecordedAt). Includes all entries, debit/credit totals, and outstanding balance.")
        .Produces<Error>(404)
        .RequireAuthorization();
    }
}
