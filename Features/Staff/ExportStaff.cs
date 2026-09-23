using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Staff.Enums;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Staff;

public static class ExportStaff
{
    public class Query : IRequest<Result<ExportResult>>
    {
        public string? Search { get; set; }
        public Guid? BranchId { get; set; }
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
            var query = db.StaffMembers
                .Include(s => s.Branch)
                .Include(s => s.ApplicationUser)
                    .ThenInclude(u => u!.UserRoles)
                    .ThenInclude(ur => ur.Role)
                .AsNoTracking();

            if (request.BranchId.HasValue)
                query = query.Where(s => s.BranchId == request.BranchId.Value);

            if (!string.IsNullOrWhiteSpace(request.Status) &&
                Enum.TryParse<EmploymentStatus>(request.Status, ignoreCase: true, out var status))
                query = query.Where(s => s.EmploymentStatus == status);

            if (!string.IsNullOrWhiteSpace(request.Search))
            {
                var s = request.Search.ToLower();
                query = query.Where(m =>
                    m.FirstName.ToLower().Contains(s) ||
                    m.LastName.ToLower().Contains(s) ||
                    m.EmailAddress.ToLower().Contains(s) ||
                    (m.EmployeeNumber != null && m.EmployeeNumber.ToLower().Contains(s)));
            }

            var staff = await query
                .OrderBy(s => s.Branch.Name)
                .ThenBy(s => s.LastName)
                .ThenBy(s => s.FirstName)
                .ToListAsync(cancellationToken);

            using var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add("Staff");

            ws.Cells[1, 1].Value = "Employee Number";
            ws.Cells[1, 2].Value = "First Name";
            ws.Cells[1, 3].Value = "Last Name";
            ws.Cells[1, 4].Value = "Email";
            ws.Cells[1, 5].Value = "Phone";
            ws.Cells[1, 6].Value = "Role";
            ws.Cells[1, 7].Value = "Branch";
            ws.Cells[1, 8].Value = "Employment Status";
            ws.Cells[1, 9].Value = "Joined On";
            ws.Cells[1, 10].Value = "App Access";

            using (var h = ws.Cells[1, 1, 1, 10]) h.Style.Font.Bold = true;

            int row = 2;
            foreach (var s in staff)
            {
                var roles = s.ApplicationUser?.UserRoles
                    .Select(ur => ur.Role.Name)
                    .ToList() ?? [];

                ws.Cells[row, 1].Value = s.EmployeeNumber;
                ws.Cells[row, 2].Value = s.FirstName;
                ws.Cells[row, 3].Value = s.LastName;
                ws.Cells[row, 4].Value = s.EmailAddress;
                ws.Cells[row, 5].Value = s.PhoneNumber;
                ws.Cells[row, 6].Value = roles.Count > 0 ? string.Join(", ", roles) : s.Role;
                ws.Cells[row, 7].Value = s.Branch.Name;
                ws.Cells[row, 8].Value = s.EmploymentStatus.ToString();
                ws.Cells[row, 9].Value = s.JoinedOn.ToString("yyyy-MM-dd");
                ws.Cells[row, 10].Value = s.ApplicationUser is not null ? "Yes" : "No";
                row++;
            }

            ws.Cells[ws.Dimension.Address].AutoFitColumns();

            var fileName = $"staff_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx";
            return Result.Success(new ExportResult { FileBytes = package.GetAsByteArray(), FileName = fileName });
        }
    }
}

public class ExportStaffEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/staff/export", async (
            ISender sender,
            [FromQuery] string? search,
            [FromQuery] Guid? branchId,
            [FromQuery] string? status) =>
        {
            var result = await sender.Send(new ExportStaff.Query
            {
                Search = search,
                BranchId = branchId,
                Status = status
            });

            if (result.IsFailure)
                return Results.BadRequest(result.Error);

            return Results.File(
                result.Value.FileBytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                result.Value.FileName);
        })
        .WithTags("Staff")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Staff)
        .WithSummary("Export staff list to Excel")
        .WithDescription(
            "Exports the staff list to a single-sheet .xlsx file. " +
            "Columns: Employee Number, First Name, Last Name, Email, Phone, Role, Branch, Employment Status, Joined On, App Access. " +
            "Optional filters: `search` (name or email), `branchId`, `status`.")
        .Produces<FileResult>(200)
        .Produces<Error>(400)
        .RequireAuthorization();
    }
}
