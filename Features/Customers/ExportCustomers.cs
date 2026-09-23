using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Customers.Enums;
using prohpharmacy_trekking_app.Shared;
using System.Drawing;

namespace prohpharmacy_trekking_app.Features.Customers;

public static class ExportCustomers
{
    public class Query : IRequest<Result<ExportResult>>
    {
        public string? Search { get; set; }
        public Guid? RegionId { get; set; }
        public Guid? BranchId { get; set; }
        public string? CustomerType { get; set; }
        public string? Status { get; set; }
    }

    public class ExportResult
    {
        public byte[] FileBytes { get; set; } = [];
        public string FileName { get; set; } = string.Empty;
    }

    internal sealed class Handler(AppDbContext db) : IRequestHandler<Query, Result<ExportResult>>
    {
        public async Task<Result<ExportResult>> Handle(Query request, CancellationToken cancellationToken)
        {
            var query = db.CustomerAccounts
                .Include(a => a.Region)
                .Include(a => a.People.Where(p => p.IsPrimaryContact))
                .Include(a => a.Locations.Where(l => l.IsPrimary))
                    .ThenInclude(l => l.District)
                .AsNoTracking();

            if (request.RegionId.HasValue)
                query = query.Where(a => a.RegionId == request.RegionId.Value);

            if (request.BranchId.HasValue)
                query = query.Where(a => a.OwningBranchId == request.BranchId.Value);

            if (!string.IsNullOrWhiteSpace(request.CustomerType) &&
                Enum.TryParse<CustomerType>(request.CustomerType, ignoreCase: true, out var ct))
                query = query.Where(a => a.CustomerType == ct);

            if (!string.IsNullOrWhiteSpace(request.Status) &&
                Enum.TryParse<RegistrationStatus>(request.Status, ignoreCase: true, out var rs))
                query = query.Where(a => a.RegistrationStatus == rs);

            if (!string.IsNullOrWhiteSpace(request.Search))
            {
                var s = request.Search.ToLower();
                query = query.Where(a =>
                    a.BusinessName.ToLower().Contains(s) ||
                    a.CustomerCode.ToLower().Contains(s) ||
                    a.PrimaryPhoneNumber.Contains(s));
            }

            var customers = await query
                .OrderBy(a => a.Region.Name)
                .ThenBy(a => a.BusinessName)
                .ToListAsync(cancellationToken);

            using var package = new ExcelPackage();

            var byRegion = customers
                .GroupBy(a => a.Region.Name)
                .OrderBy(g => g.Key);

            foreach (var group in byRegion)
            {
                var ws = package.Workbook.Worksheets.Add(group.Key);

                var brandGreen = Color.FromArgb(0, 191, 111);
                var headerLabels = new[] { "CUSTOMER CODE", "BUSINESS NAME", "TRADING NAME", "CUSTOMER TYPE", "DISTRICT", "PRIMARY PHONE", "WHATSAPP", "REPRESENTATIVE", "REP PHONE", "STATUS" };
                for (int c = 0; c < headerLabels.Length; c++)
                {
                    var cell = ws.Cells[1, c + 1];
                    cell.Value = headerLabels[c];
                    cell.Style.Font.Bold = true;
                    cell.Style.Font.Size = 11;
                    cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    cell.Style.Fill.BackgroundColor.SetColor(brandGreen);
                    cell.Style.Font.Color.SetColor(Color.White);
                    cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                }

                int row = 2;
                foreach (var customer in group.OrderBy(c => c.BusinessName))
                {
                    var person = customer.People.FirstOrDefault();
                    var location = customer.Locations.FirstOrDefault();

                    ws.Cells[row, 1].Value = customer.CustomerCode;
                    ws.Cells[row, 2].Value = customer.BusinessName;
                    ws.Cells[row, 3].Value = customer.TradingName;
                    ws.Cells[row, 4].Value = customer.CustomerType.ToString();
                    ws.Cells[row, 5].Value = location?.District?.Name;
                    ws.Cells[row, 6].Value = customer.PrimaryPhoneNumber;
                    ws.Cells[row, 7].Value = customer.WhatsAppNumber;
                    ws.Cells[row, 8].Value = person is not null
                        ? $"{person.FirstName} {person.LastName}".Trim()
                        : null;
                    ws.Cells[row, 9].Value = person?.PrimaryPhoneNumber;
                    ws.Cells[row, 10].Value = customer.RegistrationStatus.ToString();

                    var statusCell = ws.Cells[row, 10];
                    statusCell.Style.Font.Bold = true;
                    statusCell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    statusCell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    (Color bg, Color fg) statusColors = customer.RegistrationStatus switch
                    {
                        RegistrationStatus.Active => (Color.FromArgb(240, 253, 244), Color.FromArgb(21, 128, 61)),
                        RegistrationStatus.Suspended => (Color.FromArgb(255, 251, 235), Color.FromArgb(180, 83, 9)),
                        RegistrationStatus.Inactive => (Color.FromArgb(248, 250, 252), Color.FromArgb(100, 116, 139)),
                        RegistrationStatus.PendingReview => (Color.FromArgb(239, 246, 255), Color.FromArgb(29, 78, 216)),
                        _ => (Color.White, Color.FromArgb(71, 85, 105))
                    };
                    statusCell.Style.Fill.BackgroundColor.SetColor(statusColors.bg);
                    statusCell.Style.Font.Color.SetColor(statusColors.fg);

                    row++;
                }

                ws.Cells[ws.Dimension.Address].AutoFitColumns();
                for (var i = 1; i <= ws.Dimension.Columns; i++)
                    ws.Column(i).Width += 2;
            }

            var fileName = $"customers_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx";
            return Result.Success(new ExportResult { FileBytes = package.GetAsByteArray(), FileName = fileName });
        }
    }
}

public class ExportCustomersEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/customers/export", async (
            ISender sender,
            [FromQuery] string? search,
            [FromQuery] Guid? regionId,
            [FromQuery] Guid? branchId,
            [FromQuery] string? customerType,
            [FromQuery] string? status) =>
        {
            var result = await sender.Send(new ExportCustomers.Query
            {
                Search = search,
                RegionId = regionId,
                BranchId = branchId,
                CustomerType = customerType,
                Status = status
            });

            if (result.IsFailure)
                return Results.BadRequest(result.Error);

            return Results.File(
                result.Value.FileBytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                result.Value.FileName);
        })
        .WithTags("Customers")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Customers)
        .WithSummary("Export customers to Excel")
        .WithDescription(
            "Exports customers to an .xlsx workbook with one sheet per region. " +
            "Optional filters: `search` (business name, code, phone), `regionId`, `branchId`, `customerType`, `status`. " +
            "Columns: Customer Code, Business Name, Trading Name, Customer Type, District, Primary Phone, WhatsApp, Representative, Rep Phone, Status.")
        .Produces<FileResult>(200)
        .Produces<Error>(400)
        .RequireAuthorization();
    }
}
