using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Customers.Enums;
using prohpharmacy_trekking_app.Shared;

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

                ws.Cells[1, 1].Value = "Customer Code";
                ws.Cells[1, 2].Value = "Business Name";
                ws.Cells[1, 3].Value = "Trading Name";
                ws.Cells[1, 4].Value = "Customer Type";
                ws.Cells[1, 5].Value = "District";
                ws.Cells[1, 6].Value = "Primary Phone";
                ws.Cells[1, 7].Value = "WhatsApp";
                ws.Cells[1, 8].Value = "Representative";
                ws.Cells[1, 9].Value = "Rep Phone";
                ws.Cells[1, 10].Value = "Status";

                // Bold header row
                using (var headerRange = ws.Cells[1, 1, 1, 10])
                {
                    headerRange.Style.Font.Bold = true;
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
                    row++;
                }

                ws.Cells[ws.Dimension.Address].AutoFitColumns();
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
