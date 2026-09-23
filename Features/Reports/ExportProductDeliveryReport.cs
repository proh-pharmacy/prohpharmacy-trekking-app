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

public static class ExportProductDeliveryReport
{
    public class Query : IRequest<Result<byte[]>>
    {
        public DateOnly? From { get; set; }
        public DateOnly? To { get; set; }
        public Guid? BranchId { get; set; }
        public Guid? ProductId { get; set; }
    }

    internal sealed class Handler(AppDbContext db) : IRequestHandler<Query, Result<byte[]>>
    {
        public async Task<Result<byte[]>> Handle(Query request, CancellationToken cancellationToken)
        {
            var query = db.TrekkingTripStopProducts
                .Where(p => p.TrekkingTripStop.TrekkingTrip.Status == TrekStatus.Completed
                         && (p.BasicQtyDelivered > 0 || p.PackagingQtyDelivered > 0))
                .AsNoTracking();

            if (request.From.HasValue)
                query = query.Where(p => p.TrekkingTripStop.TrekkingTrip.ScheduledDate >= request.From.Value);
            if (request.To.HasValue)
                query = query.Where(p => p.TrekkingTripStop.TrekkingTrip.ScheduledDate <= request.To.Value);
            if (request.BranchId.HasValue)
                query = query.Where(p => p.TrekkingTripStop.TrekkingTrip.BranchId == request.BranchId.Value);
            if (request.ProductId.HasValue)
                query = query.Where(p => p.ProductId == request.ProductId.Value);

            var rows = await query
                .Select(p => new
                {
                    p.ProductId,
                    ProductName = p.Product.Name,
                    ProductUnit = p.Product.BasicUnit.Name,
                    PackagingUnitName = (string?)p.Product.PackagingUnit!.Name,
                    BasicQtyDelivered = p.BasicQtyDelivered ?? 0,
                    PackagingQtyDelivered = p.PackagingQtyDelivered ?? 0,
                    AmtPaid = p.AmtPaid ?? 0,
                    Balance = p.Balance ?? 0,
                    TrekId = p.TrekkingTripStop.TrekkingTripId
                })
                .ToListAsync(cancellationToken);

            var items = rows
                .GroupBy(r => r.ProductId)
                .Select(g => new
                {
                    ProductName = g.First().ProductName,
                    BasicUnit = g.First().ProductUnit,
                    PackagingUnit = g.First().PackagingUnitName,
                    TotalBasicQty = g.Sum(r => r.BasicQtyDelivered),
                    TotalPkgQty = g.Sum(r => r.PackagingQtyDelivered),
                    TotalCollected = g.Sum(r => r.AmtPaid),
                    TotalOutstanding = g.Sum(r => r.Balance),
                    TreksCount = g.Select(r => r.TrekId).Distinct().Count()
                })
                .OrderByDescending(p => p.TotalCollected)
                .ToList();

            var totalCollected = items.Sum(i => i.TotalCollected);
            var totalOutstanding = items.Sum(i => i.TotalOutstanding);
            var totalBasicQty = items.Sum(i => i.TotalBasicQty);
            var totalPkgQty = items.Sum(i => i.TotalPkgQty);

            var brandGreen = Color.FromArgb(0, 191, 111);
            var headerText = Color.White;
            var lightGray = Color.FromArgb(245, 245, 245);
            var borderColor = Color.FromArgb(226, 232, 240);

            using var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add("Product Delivery Report");
            ws.Cells["A:I"].Style.Font.Name = "Calibri";

            ws.Cells["A1:I1"].Merge = true;
            ws.Cells["A1"].Value = "Proh Pharmacy — Product Delivery Report";
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
            ws.Cells["A3"].Value = $"Generated: {DateTime.UtcNow:dd MMM yyyy HH:mm} UTC  |  Products: {items.Count}  |  Collected: GHS {totalCollected:#,##0.00}  |  Outstanding: GHS {totalOutstanding:#,##0.00}";
            ws.Cells["A3"].Style.Font.Size = 9;
            ws.Cells["A3"].Style.Font.Color.SetColor(Color.FromArgb(100, 116, 139));

            var headers = new[] { "#", "Product Name", "Basic Unit", "Basic Qty", "Pkg Unit", "Pkg Qty", "Collected (GHS)", "Outstanding (GHS)", "Treks" };
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

                void SetCell(int col, object? value, bool isNumber = false, bool isBold = false)
                {
                    var cell = ws.Cells[r, col];
                    cell.Value = value;
                    if (isAlt) { cell.Style.Fill.PatternType = ExcelFillStyle.Solid; cell.Style.Fill.BackgroundColor.SetColor(lightGray); }
                    if (isNumber) cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                    if (isBold) cell.Style.Font.Bold = true;
                    cell.Style.Border.BorderAround(ExcelBorderStyle.Thin, borderColor);
                }

                SetCell(1, i + 1);
                SetCell(2, item.ProductName);
                SetCell(3, item.BasicUnit ?? "—");
                SetCell(4, item.TotalBasicQty, isNumber: true);
                ws.Cells[r, 4].Style.Numberformat.Format = "#,##0.###";
                SetCell(5, item.PackagingUnit ?? "—");
                SetCell(6, item.TotalPkgQty > 0 ? item.TotalPkgQty : (object)"—", isNumber: item.TotalPkgQty > 0);
                if (item.TotalPkgQty > 0) ws.Cells[r, 6].Style.Numberformat.Format = "#,##0.###";
                SetCell(7, item.TotalCollected, isNumber: true);
                ws.Cells[r, 7].Style.Numberformat.Format = "#,##0.00";
                SetCell(8, item.TotalOutstanding, isNumber: true, isBold: item.TotalOutstanding > 0);
                ws.Cells[r, 8].Style.Numberformat.Format = "#,##0.00";
                SetCell(9, item.TreksCount);

                if (item.TotalOutstanding > 0)
                    ws.Cells[r, 8].Style.Font.Color.SetColor(Color.FromArgb(185, 28, 28));
            }

            int totalsRow = headerRow + 1 + items.Count;
            ws.Cells[totalsRow, 1, totalsRow, 3].Merge = true;
            ws.Cells[totalsRow, 1].Value = "TOTAL";
            ws.Cells[totalsRow, 1].Style.Font.Bold = true;
            ws.Cells[totalsRow, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
            ws.Cells[totalsRow, 1].Style.Border.BorderAround(ExcelBorderStyle.Thin, borderColor);

            void TotalCell(int col, decimal value, bool applyRedIfPositive = false)
            {
                var cell = ws.Cells[totalsRow, col];
                cell.Value = value;
                cell.Style.Font.Bold = true;
                cell.Style.Numberformat.Format = "#,##0.00";
                cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                cell.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(240, 253, 244));
                cell.Style.Border.BorderAround(ExcelBorderStyle.Medium, brandGreen);
                if (applyRedIfPositive && value > 0)
                    cell.Style.Font.Color.SetColor(Color.FromArgb(185, 28, 28));
            }

            void TotalQtyCell(int col, decimal value)
            {
                var cell = ws.Cells[totalsRow, col];
                cell.Value = value;
                cell.Style.Font.Bold = true;
                cell.Style.Numberformat.Format = "#,##0.###";
                cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                cell.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(240, 253, 244));
                cell.Style.Border.BorderAround(ExcelBorderStyle.Medium, brandGreen);
            }

            ws.Cells[totalsRow, 5].Style.Border.BorderAround(ExcelBorderStyle.Thin, borderColor);
            TotalQtyCell(4, totalBasicQty);
            TotalQtyCell(6, totalPkgQty);
            TotalCell(7, totalCollected);
            TotalCell(8, totalOutstanding, applyRedIfPositive: true);
            ws.Cells[totalsRow, 9].Style.Border.BorderAround(ExcelBorderStyle.Thin, borderColor);

            ws.Cells[ws.Dimension.Address].AutoFitColumns();
            for (var i = 1; i <= ws.Dimension.Columns; i++)
                ws.Column(i).Width += 2;

            ws.Row(1).Height = 22;
            ws.View.FreezePanes(headerRow + 1, 1);

            return Result.Success(package.GetAsByteArray());
        }
    }
}

public class ExportProductDeliveryReportEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/reports/products/export", async (
            ISender sender,
            HttpContext ctx,
            [FromQuery] DateOnly? from,
            [FromQuery] DateOnly? to,
            [FromQuery] Guid? branchId,
            [FromQuery] Guid? productId) =>
        {
            var result = await sender.Send(new ExportProductDeliveryReport.Query
            {
                From = from,
                To = to,
                BranchId = branchId,
                ProductId = productId
            });

            if (result.IsFailure)
                return Results.BadRequest(result.Error);

            var filename = $"ProductDeliveryReport-{DateTime.UtcNow:yyyyMMdd}.xlsx";
            ctx.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{filename}\"";
            return Results.File(result.Value,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        })
        .WithTags("Reports")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Reports)
        .WithSummary("Export product delivery report to Excel")
        .WithDescription("Downloads a .xlsx product delivery report grouped by product across completed treks. Filter by from/to (ScheduledDate) or branchId.")
        .Produces<Error>(400)
        .RequireAuthorization();
    }
}
