using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Staff.Enums;
using prohpharmacy_trekking_app.Shared;
using System.Drawing;

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

            var brandGreen = Color.FromArgb(0, 191, 111);
            var staffHeaders = new[] { "EMPLOYEE NUMBER", "FIRST NAME", "LAST NAME", "EMAIL", "PHONE", "ROLE", "BRANCH", "EMPLOYMENT STATUS", "JOINED ON", "APP ACCESS" };
            for (int c = 0; c < staffHeaders.Length; c++)
            {
                var cell = ws.Cells[1, c + 1];
                cell.Value = staffHeaders[c];
                cell.Style.Font.Bold = true;
                cell.Style.Font.Size = 11;
                cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                cell.Style.Fill.BackgroundColor.SetColor(brandGreen);
                cell.Style.Font.Color.SetColor(Color.White);
                cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            }

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

                var empCell = ws.Cells[row, 8];
                empCell.Style.Font.Bold = true;
                empCell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                empCell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                (Color bg, Color fg) empColors = s.EmploymentStatus switch
                {
                    EmploymentStatus.Active => (Color.FromArgb(240, 253, 244), Color.FromArgb(21, 128, 61)),
                    EmploymentStatus.Suspended => (Color.FromArgb(255, 251, 235), Color.FromArgb(180, 83, 9)),
                    EmploymentStatus.Offboarded => (Color.FromArgb(254, 242, 242), Color.FromArgb(185, 28, 28)),
                    EmploymentStatus.Pending => (Color.FromArgb(239, 246, 255), Color.FromArgb(29, 78, 216)),
                    _ => (Color.White, Color.FromArgb(71, 85, 105))
                };
                empCell.Style.Fill.BackgroundColor.SetColor(empColors.bg);
                empCell.Style.Font.Color.SetColor(empColors.fg);

                var appAccessValue = s.ApplicationUser is not null ? "Yes" : "No";
                var accessCell = ws.Cells[row, 10];
                accessCell.Style.Font.Bold = true;
                accessCell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                accessCell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                if (appAccessValue == "Yes")
                {
                    accessCell.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(240, 253, 244));
                    accessCell.Style.Font.Color.SetColor(Color.FromArgb(21, 128, 61));
                }
                else
                {
                    accessCell.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(248, 250, 252));
                    accessCell.Style.Font.Color.SetColor(Color.FromArgb(100, 116, 139));
                }

                row++;
            }

            ws.Cells[ws.Dimension.Address].AutoFitColumns();
            for (var i = 1; i <= ws.Dimension.Columns; i++)
                ws.Column(i).Width += 2;

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
