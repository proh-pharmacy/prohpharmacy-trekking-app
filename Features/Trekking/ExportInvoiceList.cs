using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Trekking.Enums;
using prohpharmacy_trekking_app.Services.Pdf;
using prohpharmacy_trekking_app.Shared;
using System.Drawing;

namespace prohpharmacy_trekking_app.Features.Trekking;

public static class ExportInvoiceList
{
    public class Query : IRequest<Result<(byte[] File, string ContentType, string FileName)>>
    {
        public string? Format { get; set; }
        public string? Search { get; set; }
        public string? Sort { get; set; }
        public Guid? CustomerId { get; set; }
        public Guid? TrekId { get; set; }
        public Guid? RegionId { get; set; }
        public string? Status { get; set; }
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
    }

    internal sealed class Handler(AppDbContext db)
        : IRequestHandler<Query, Result<(byte[] File, string ContentType, string FileName)>>
    {
        public async Task<Result<(byte[] File, string ContentType, string FileName)>> Handle(Query request, CancellationToken cancellationToken)
        {
            var format = (request.Format ?? "excel").Trim().ToLowerInvariant();
            if (format != "excel" && format != "pdf")
                return Result.Failure<(byte[], string, string)>(Error.BadRequest("format must be 'excel' or 'pdf'."));

            var query = db.SaleInvoices
                .Include(i => i.Stop).ThenInclude(s => s.CustomerAccount).ThenInclude(c => c.Region)
                .Include(i => i.Stop).ThenInclude(s => s.TrekkingTrip).ThenInclude(t => t.Region)
                .AsNoTracking();

            if (request.CustomerId.HasValue)
                query = query.Where(i => i.CustomerAccountId == request.CustomerId.Value);

            if (request.TrekId.HasValue)
                query = query.Where(i => i.TrekkingTripId == request.TrekId.Value);

            if (request.RegionId.HasValue)
                query = query.Where(i => i.Stop.TrekkingTrip.RegionId == request.RegionId.Value);

            if (!string.IsNullOrWhiteSpace(request.Status))
                query = query.Where(i => i.Status.ToString().ToLower() == request.Status.ToLower());

            if (request.DateFrom.HasValue)
                query = query.Where(i => i.IssuedAt >= request.DateFrom.Value);

            if (request.DateTo.HasValue)
                query = query.Where(i => i.IssuedAt <= request.DateTo.Value);

            if (!string.IsNullOrWhiteSpace(request.Search))
            {
                var s = request.Search.Trim();
                query = query.Where(i => i.InvoiceNumber != null && EF.Functions.ILike(i.InvoiceNumber, $"%{s}%"));
            }

            query = ApplySort(query, request.Sort);

            var invoices = await query.ToListAsync(cancellationToken);

            Guid? customerIdForLabel = request.CustomerId;
            string? customerLabel = null;
            if (customerIdForLabel.HasValue)
            {
                customerLabel = invoices.FirstOrDefault()?.Stop?.CustomerAccount is { } ca
                    ? $"{ca.BusinessName} ({ca.CustomerCode})"
                    : (await db.CustomerAccounts.AsNoTracking()
                        .Where(c => c.Id == customerIdForLabel.Value)
                        .Select(c => c.BusinessName + " (" + c.CustomerCode + ")")
                        .FirstOrDefaultAsync(cancellationToken));
            }

            string? trekLabel = null;
            if (request.TrekId.HasValue)
            {
                trekLabel = invoices.FirstOrDefault()?.Stop?.TrekkingTrip?.TrekNumber
                    ?? (await db.TrekkingTrips.AsNoTracking()
                        .Where(t => t.Id == request.TrekId.Value)
                        .Select(t => t.TrekNumber)
                        .FirstOrDefaultAsync(cancellationToken));
            }

            string? regionLabel = null;
            if (request.RegionId.HasValue)
            {
                regionLabel = invoices.FirstOrDefault()?.Stop?.TrekkingTrip?.Region?.Name
                    ?? (await db.Regions.AsNoTracking()
                        .Where(r => r.Id == request.RegionId.Value)
                        .Select(r => r.Name)
                        .FirstOrDefaultAsync(cancellationToken));
            }

            var filters = BuildFilterLines(request, customerLabel, trekLabel, regionLabel);
            var sumTotal = invoices.Sum(i => i.TotalAmount);
            var sumPaid = invoices.Sum(i => i.TotalPaid);
            var sumBalance = invoices.Sum(i => i.Balance);
            var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmm");

            if (format == "excel")
            {
                var bytes = BuildExcel(invoices, filters, sumTotal, sumPaid, sumBalance);
                return Result.Success((bytes,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    $"Invoices-{stamp}.xlsx"));
            }

            var pdfBytes = BuildPdf(invoices, filters, sumTotal, sumPaid, sumBalance);
            return Result.Success((pdfBytes, "application/pdf", $"Invoices-{stamp}.pdf"));
        }

        private static IQueryable<Entities.SaleInvoice> ApplySort(IQueryable<Entities.SaleInvoice> q, string? sort)
        {
            if (string.IsNullOrWhiteSpace(sort))
                return q.OrderByDescending(i => i.IssuedAt);

            var parts = sort.Split('_');
            if (parts.Length != 2)
                return q.OrderByDescending(i => i.IssuedAt);

            var field = parts[0];
            var asc = parts[1].Equals("asc", StringComparison.OrdinalIgnoreCase);

            return asc
                ? q.OrderBy(x => EF.Property<object>(x, field))
                : q.OrderByDescending(x => EF.Property<object>(x, field));
        }

        private static List<InvoiceListPdfGenerator.InvoiceListData.FilterLine> BuildFilterLines(
            Query r, string? customerLabel, string? trekLabel, string? regionLabel)
        {
            var lines = new List<InvoiceListPdfGenerator.InvoiceListData.FilterLine>();

            lines.Add(new() { Label = "Customer", Value = customerLabel ?? "All customers" });
            lines.Add(new() { Label = "Trek", Value = trekLabel ?? "All treks" });
            lines.Add(new() { Label = "Region", Value = regionLabel ?? "All regions" });
            lines.Add(new() { Label = "Status", Value = string.IsNullOrWhiteSpace(r.Status) ? "All statuses" : r.Status! });

            var period = (r.DateFrom, r.DateTo) switch
            {
                ({ } f, { } t) => $"{f:dd MMM yyyy} — {t:dd MMM yyyy}",
                ({ } f, null) => $"From {f:dd MMM yyyy}",
                (null, { } t) => $"Up to {t:dd MMM yyyy}",
                _ => "All dates"
            };
            lines.Add(new() { Label = "Period", Value = period });

            if (!string.IsNullOrWhiteSpace(r.Search))
                lines.Add(new() { Label = "Search", Value = r.Search! });

            lines.Add(new() { Label = "Sort", Value = string.IsNullOrWhiteSpace(r.Sort) ? "issuedAt_desc" : r.Sort! });

            return lines;
        }

        // ── Excel ────────────────────────────────────────────────────────────────

        private static byte[] BuildExcel(
            List<Entities.SaleInvoice> invoices,
            List<InvoiceListPdfGenerator.InvoiceListData.FilterLine> filters,
            decimal sumTotal, decimal sumPaid, decimal sumBalance)
        {
            var brandGreen = Color.FromArgb(0, 191, 111);
            var headerText = Color.White;
            var altGray = Color.FromArgb(248, 250, 252);
            var borderColor = Color.FromArgb(226, 232, 240);
            var slateText = Color.FromArgb(71, 85, 105);
            var darkSlate = Color.FromArgb(30, 41, 59);
            var redTint = Color.FromArgb(185, 28, 28);
            var totalBg = Color.FromArgb(240, 253, 244);

            using var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add("Sale Invoices");
            ws.Cells["A:K"].Style.Font.Name = "Calibri";

            ws.Cells["A1:K1"].Merge = true;
            ws.Cells["A1"].Value = "Proh Pharmacy — Sale Invoices Export";
            ws.Cells["A1"].Style.Font.Bold = true;
            ws.Cells["A1"].Style.Font.Size = 14;
            ws.Cells["A1"].Style.Font.Color.SetColor(darkSlate);

            int row = 2;
            foreach (var f in filters)
            {
                ws.Cells[row, 1].Value = $"{f.Label}:";
                ws.Cells[row, 1].Style.Font.Bold = true;
                ws.Cells[row, 1].Style.Font.Color.SetColor(slateText);
                ws.Cells[row, 2, row, 6].Merge = true;
                ws.Cells[row, 2].Value = f.Value;
                ws.Cells[row, 2].Style.Font.Color.SetColor(darkSlate);
                row++;
            }

            ws.Cells[row, 1].Value = "Generated:";
            ws.Cells[row, 1].Style.Font.Bold = true;
            ws.Cells[row, 1].Style.Font.Color.SetColor(slateText);
            ws.Cells[row, 2, row, 6].Merge = true;
            ws.Cells[row, 2].Value = $"{DateTime.UtcNow:dd MMM yyyy HH:mm} UTC";
            ws.Cells[row, 2].Style.Font.Color.SetColor(darkSlate);
            row++;

            ws.Cells[row, 1].Value = "Rows:";
            ws.Cells[row, 1].Style.Font.Bold = true;
            ws.Cells[row, 1].Style.Font.Color.SetColor(slateText);
            ws.Cells[row, 2, row, 6].Merge = true;
            ws.Cells[row, 2].Value = invoices.Count;
            ws.Cells[row, 2].Style.Font.Color.SetColor(darkSlate);
            row += 2;

            int headerRow = row;
            var headers = new[]
            {
                "INVOICE NO.", "ISSUED AT", "STATUS", "TREK NO.", "TREK DATE",
                "CUSTOMER CODE", "CUSTOMER NAME", "REGION",
                "TOTAL AMOUNT (GHS)", "TOTAL PAID (GHS)", "BALANCE (GHS)"
            };

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

            for (int i = 0; i < invoices.Count; i++)
            {
                var inv = invoices[i];
                int r = headerRow + 1 + i;
                bool isAlt = i % 2 == 1;
                var ca = inv.Stop?.CustomerAccount;
                var trip = inv.Stop?.TrekkingTrip;

                void Cell(int col, object? value, bool isNumber = false, Color? font = null)
                {
                    var cell = ws.Cells[r, col];
                    cell.Value = value;
                    if (isAlt)
                    {
                        cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                        cell.Style.Fill.BackgroundColor.SetColor(altGray);
                    }
                    if (isNumber)
                    {
                        cell.Style.Numberformat.Format = "#,##0.00";
                        cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                    }
                    if (font.HasValue) cell.Style.Font.Color.SetColor(font.Value);
                    cell.Style.Border.BorderAround(ExcelBorderStyle.Thin, borderColor);
                }

                Cell(1, inv.InvoiceNumber ?? "—");
                Cell(2, inv.IssuedAt.ToString("dd MMM yyyy HH:mm"));
                Cell(3, inv.Status.ToString(),
                    font: inv.Status switch
                    {
                        SaleInvoiceStatus.Paid => Color.FromArgb(21, 128, 61),
                        SaleInvoiceStatus.PartiallyPaid => Color.FromArgb(180, 83, 9),
                        SaleInvoiceStatus.Voided => redTint,
                        _ => slateText
                    });
                Cell(4, trip?.TrekNumber ?? "—");
                Cell(5, trip?.ScheduledDate.ToString("dd MMM yyyy") ?? "—");
                Cell(6, ca?.CustomerCode ?? "—");
                Cell(7, ca?.BusinessName ?? "—");
                Cell(8, ca?.Region?.Name ?? "—");
                Cell(9, inv.TotalAmount, isNumber: true);
                Cell(10, inv.TotalPaid, isNumber: true);
                Cell(11, inv.Balance, isNumber: true, font: inv.Balance > 0 ? redTint : (Color?)null);
            }

            int totalsRow = headerRow + invoices.Count + 1;
            ws.Cells[totalsRow, 1, totalsRow, 8].Merge = true;
            ws.Cells[totalsRow, 1].Value = "TOTAL";
            ws.Cells[totalsRow, 1].Style.Font.Bold = true;
            ws.Cells[totalsRow, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
            ws.Cells[totalsRow, 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
            ws.Cells[totalsRow, 1].Style.Fill.BackgroundColor.SetColor(totalBg);
            ws.Cells[totalsRow, 1].Style.Font.Color.SetColor(darkSlate);

            void TotalCell(int col, decimal value, bool highlightRed = false)
            {
                var cell = ws.Cells[totalsRow, col];
                cell.Value = value;
                cell.Style.Font.Bold = true;
                cell.Style.Numberformat.Format = "#,##0.00";
                cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                cell.Style.Fill.BackgroundColor.SetColor(totalBg);
                cell.Style.Border.BorderAround(ExcelBorderStyle.Medium, brandGreen);
                if (highlightRed && value > 0) cell.Style.Font.Color.SetColor(redTint);
                else cell.Style.Font.Color.SetColor(darkSlate);
            }

            TotalCell(9, sumTotal);
            TotalCell(10, sumPaid);
            TotalCell(11, sumBalance, highlightRed: true);

            if (ws.Dimension is not null)
            {
                ws.Cells[ws.Dimension.Address].AutoFitColumns();
                for (var c = 1; c <= ws.Dimension.Columns; c++)
                    ws.Column(c).Width += 2;
            }

            ws.Row(1).Height = 22;
            ws.View.FreezePanes(headerRow + 1, 1);

            return package.GetAsByteArray();
        }

        // ── PDF ──────────────────────────────────────────────────────────────────

        private static byte[] BuildPdf(
            List<Entities.SaleInvoice> invoices,
            List<InvoiceListPdfGenerator.InvoiceListData.FilterLine> filters,
            decimal sumTotal, decimal sumPaid, decimal sumBalance)
        {
            var data = new InvoiceListPdfGenerator.InvoiceListData
            {
                Filters = filters,
                GeneratedAtUtc = DateTime.UtcNow,
                TotalCount = invoices.Count,
                SumTotalAmount = sumTotal,
                SumTotalPaid = sumPaid,
                SumBalance = sumBalance,
                Rows = invoices.Select(inv => new InvoiceListPdfGenerator.InvoiceListData.Row
                {
                    InvoiceNumber = inv.InvoiceNumber ?? "—",
                    IssuedAt = inv.IssuedAt,
                    Status = inv.Status.ToString(),
                    TrekNumber = inv.Stop?.TrekkingTrip?.TrekNumber ?? "—",
                    TrekDate = inv.Stop?.TrekkingTrip?.ScheduledDate ?? default,
                    CustomerCode = inv.Stop?.CustomerAccount?.CustomerCode ?? "—",
                    CustomerName = inv.Stop?.CustomerAccount?.BusinessName ?? "—",
                    CustomerRegion = inv.Stop?.CustomerAccount?.Region?.Name ?? "—",
                    TotalAmount = inv.TotalAmount,
                    TotalPaid = inv.TotalPaid,
                    Balance = inv.Balance
                }).ToList()
            };

            return InvoiceListPdfGenerator.Generate(data);
        }
    }
}

public class ExportInvoiceListEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/invoices/export", async (
            ISender sender,
            HttpContext ctx,
            [FromQuery] string? format,
            [FromQuery] string? search,
            [FromQuery] string? sort,
            [FromQuery] Guid? customerId,
            [FromQuery] Guid? trekId,
            [FromQuery] Guid? regionId,
            [FromQuery] string? status,
            [FromQuery] DateTime? dateFrom,
            [FromQuery] DateTime? dateTo) =>
        {
            var result = await sender.Send(new ExportInvoiceList.Query
            {
                Format = format,
                Search = search,
                Sort = sort,
                CustomerId = customerId,
                TrekId = trekId,
                RegionId = regionId,
                Status = status,
                DateFrom = dateFrom,
                DateTo = dateTo
            });

            if (result.IsFailure)
                return Results.UnprocessableEntity(result.Error);

            var (file, contentType, fileName) = result.Value;
            ctx.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{fileName}\"";
            return Results.File(file, contentType);
        })
        .WithTags("Trekking")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Trekking)
        .WithSummary("Export sale invoices as Excel or PDF")
        .WithDescription("Streams all invoices matching the filters (same filters as GET /api/v1/invoices, pagination ignored). format=excel|pdf.")
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
