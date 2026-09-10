using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Trekking.Enums;
using prohpharmacy_trekking_app.Shared;
using System.Drawing;

namespace prohpharmacy_trekking_app.Features.Reports;

public static class ExportTrekReport
{
    public class Query : IRequest<Result<byte[]>>
    {
        public DateOnly? From { get; set; }
        public DateOnly? To { get; set; }
        public Guid? BranchId { get; set; }
        public Guid? DriverId { get; set; }
        public string? Status { get; set; }
    }

    internal sealed class Handler(AppDbContext db) : IRequestHandler<Query, Result<byte[]>>
    {
        public async Task<Result<byte[]>> Handle(Query request, CancellationToken cancellationToken)
        {
            var query = db.TrekkingTrips
                .Include(t => t.Driver)
                .Include(t => t.Branch)
                .Include(t => t.Stops).ThenInclude(s => s.Products)
                .AsNoTracking();

            if (request.From.HasValue)
                query = query.Where(t => t.ScheduledDate >= request.From.Value);
            if (request.To.HasValue)
                query = query.Where(t => t.ScheduledDate <= request.To.Value);
            if (request.BranchId.HasValue)
                query = query.Where(t => t.BranchId == request.BranchId.Value);
            if (request.DriverId.HasValue)
                query = query.Where(t => t.DriverStaffId == request.DriverId.Value);
            if (!string.IsNullOrWhiteSpace(request.Status) &&
                Enum.TryParse<TrekStatus>(request.Status, true, out var parsedStatus))
                query = query.Where(t => t.Status == parsedStatus);

            var trips = await query.OrderByDescending(t => t.ScheduledDate).ToListAsync(cancellationToken);

            var items = trips.Select(t => new
            {
                t.TrekNumber,
                t.ScheduledDate,
                DriverName = $"{t.Driver.FirstName} {t.Driver.LastName}",
                BranchName = t.Branch.Name,
                t.Status,
                StopsCount = t.Stops.Count,
                TotalCollected = t.Stops.Sum(s => s.Products.Sum(p => p.AmtPaid ?? 0)),
                TotalOutstanding = t.Stops.Sum(s => s.Products.Sum(p => p.Balance ?? 0))
            }).ToList();

            var totalCollected = items.Sum(i => i.TotalCollected);
            var totalOutstanding = items.Sum(i => i.TotalOutstanding);
            var completedCount = items.Count(i => i.Status == TrekStatus.Completed);
            var cancelledCount = items.Count(i => i.Status == TrekStatus.Cancelled);
            var inProgressCount = items.Count(i => i.Status == TrekStatus.InProgress);

            var brandGreen = Color.FromArgb(0, 191, 111);
            var headerText = Color.White;
            var lightGray = Color.FromArgb(245, 245, 245);
            var borderColor = Color.FromArgb(226, 232, 240);

            using var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add("Trek Report");
            ws.Cells["A:I"].Style.Font.Name = "Calibri";

            ws.Cells["A1:I1"].Merge = true;
            ws.Cells["A1"].Value = "Proh Pharmacy — Trek Performance Report";
            ws.Cells["A1"].Style.Font.Bold = true;
            ws.Cells["A1"].Style.Font.Size = 14;
            ws.Cells["A1"].Style.Font.Color.SetColor(Color.FromArgb(30, 41, 59));

            ws.Cells["A2:I2"].Merge = true;
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

            ws.Cells["A3:I3"].Merge = true;
            ws.Cells["A3"].Value = $"Generated: {DateTime.UtcNow:dd MMM yyyy HH:mm} UTC  |  Treks: {items.Count}  |  Completed: {completedCount}  |  Cancelled: {cancelledCount}  |  Collected: GHS {totalCollected:#,##0.00}  |  Outstanding: GHS {totalOutstanding:#,##0.00}";
            ws.Cells["A3"].Style.Font.Size = 9;
            ws.Cells["A3"].Style.Font.Color.SetColor(Color.FromArgb(100, 116, 139));

            var headers = new[] { "#", "Trek No.", "Scheduled Date", "Driver", "Branch", "Status", "Stops", "Collected (GHS)", "Outstanding (GHS)" };
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

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                int r = headerRow + 1 + i;
                bool isAlt = i % 2 == 1;

                void SetCell(int col, object? value, bool isNumber = false)
                {
                    var cell = ws.Cells[r, col];
                    cell.Value = value;
                    if (isAlt)
                    {
                        cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                        cell.Style.Fill.BackgroundColor.SetColor(lightGray);
                    }
                    if (isNumber) cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                    cell.Style.Border.BorderAround(ExcelBorderStyle.Thin, borderColor);
                }

                SetCell(1, i + 1);
                SetCell(2, item.TrekNumber);
                SetCell(3, item.ScheduledDate.ToString("dd MMM yyyy"));
                SetCell(4, item.DriverName);
                SetCell(5, item.BranchName);

                var statusCell = ws.Cells[r, 6];
                statusCell.Value = item.Status.ToString();
                statusCell.Style.Font.Bold = true;
                statusCell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                statusCell.Style.Border.BorderAround(ExcelBorderStyle.Thin, borderColor);
                statusCell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                statusCell.Style.Fill.BackgroundColor.SetColor(item.Status switch
                {
                    TrekStatus.Completed => Color.FromArgb(240, 253, 244),
                    TrekStatus.Cancelled => Color.FromArgb(254, 242, 242),
                    TrekStatus.InProgress => Color.FromArgb(255, 251, 235),
                    _ => isAlt ? lightGray : Color.White
                });
                statusCell.Style.Font.Color.SetColor(item.Status switch
                {
                    TrekStatus.Completed => Color.FromArgb(21, 128, 61),
                    TrekStatus.Cancelled => Color.FromArgb(185, 28, 28),
                    TrekStatus.InProgress => Color.FromArgb(180, 83, 9),
                    _ => Color.FromArgb(71, 85, 105)
                });

                SetCell(7, item.StopsCount);
                SetCell(8, item.TotalCollected, isNumber: true);
                SetCell(9, item.TotalOutstanding, isNumber: true);
                ws.Cells[r, 8].Style.Numberformat.Format = "#,##0.00";
                ws.Cells[r, 9].Style.Numberformat.Format = "#,##0.00";

                if (item.TotalOutstanding > 0)
                    ws.Cells[r, 9].Style.Font.Color.SetColor(Color.FromArgb(185, 28, 28));
            }

            int totalsRow = headerRow + 1 + items.Count;
            ws.Cells[totalsRow, 1, totalsRow, 7].Merge = true;
            ws.Cells[totalsRow, 1].Value = "TOTAL";
            ws.Cells[totalsRow, 1].Style.Font.Bold = true;
            ws.Cells[totalsRow, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;

            void TotalCell(int col, decimal value)
            {
                var cell = ws.Cells[totalsRow, col];
                cell.Value = value;
                cell.Style.Font.Bold = true;
                cell.Style.Numberformat.Format = "#,##0.00";
                cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                cell.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(240, 253, 244));
                cell.Style.Border.BorderAround(ExcelBorderStyle.Medium, brandGreen);
            }

            TotalCell(8, totalCollected);
            TotalCell(9, totalOutstanding);

            ws.Column(1).Width = 5;
            ws.Column(2).Width = 16;
            ws.Column(3).Width = 18;
            ws.Column(4).Width = 26;
            ws.Column(5).Width = 22;
            ws.Column(6).Width = 14;
            ws.Column(7).Width = 8;
            ws.Column(8).Width = 20;
            ws.Column(9).Width = 20;

            ws.Row(1).Height = 22;
            ws.View.FreezePanes(headerRow + 1, 1);

            return Result.Success(package.GetAsByteArray());
        }
    }
}

public class ExportTrekReportEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/reports/treks/export", async (
            ISender sender,
            HttpContext ctx,
            [FromQuery] DateOnly? from,
            [FromQuery] DateOnly? to,
            [FromQuery] Guid? branchId,
            [FromQuery] Guid? driverId,
            [FromQuery] string? status) =>
        {
            var result = await sender.Send(new ExportTrekReport.Query
            {
                From = from,
                To = to,
                BranchId = branchId,
                DriverId = driverId,
                Status = status
            });

            if (result.IsFailure)
                return Results.BadRequest(result.Error);

            var filename = $"TrekReport-{DateTime.UtcNow:yyyyMMdd}.xlsx";
            ctx.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{filename}\"";
            return Results.File(result.Value,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        })
        .WithTags("Reports")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Reports)
        .WithSummary("Export trek performance report to Excel")
        .WithDescription("Downloads a .xlsx trek report. Filter by from/to (ScheduledDate), branchId, driverId, or status.")
        .Produces<Error>(400)
        .RequireAuthorization();
    }
}
