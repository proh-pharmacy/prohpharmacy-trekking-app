using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Customers;

public static class ExportCustomerMarkupRules
{
    public class Query : IRequest<Result<ExportResult>>
    {
        public Guid? RegionId { get; set; }
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
            var query = db.CustomerMarkupRules
                .Include(r => r.CustomerAccount)
                    .ThenInclude(a => a.Region)
                .Include(r => r.Product)
                .AsNoTracking();

            if (request.RegionId.HasValue)
                query = query.Where(r => r.CustomerAccount.RegionId == request.RegionId.Value);

            var rules = await query
                .OrderBy(r => r.CustomerAccount.Region.Name)
                .ThenBy(r => r.CustomerAccount.BusinessName)
                .ThenBy(r => r.ProductId == null ? 0 : 1)
                .ThenBy(r => r.Product != null ? r.Product.Name : string.Empty)
                .ToListAsync(cancellationToken);

            using var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add("Customer Markup Rules");

            ws.Cells[1, 1].Value = "Customer Code";
            ws.Cells[1, 2].Value = "Customer Name";
            ws.Cells[1, 3].Value = "Region";
            ws.Cells[1, 4].Value = "Product";
            ws.Cells[1, 5].Value = "Markup %";

            using (var h = ws.Cells[1, 1, 1, 5]) h.Style.Font.Bold = true;

            int row = 2;
            foreach (var rule in rules)
            {
                ws.Cells[row, 1].Value = rule.CustomerAccount.CustomerCode;
                ws.Cells[row, 2].Value = rule.CustomerAccount.BusinessName;
                ws.Cells[row, 3].Value = rule.CustomerAccount.Region.Name;
                ws.Cells[row, 4].Value = rule.Product?.Name ?? "All Products";
                ws.Cells[row, 5].Value = rule.MarkupPercentage;
                row++;
            }

            ws.Cells[ws.Dimension.Address].AutoFitColumns();
            for (var i = 1; i <= ws.Dimension.Columns; i++)
                ws.Column(i).Width += 2;

            var fileName = $"customer_markup_rules_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx";
            return Result.Success(new ExportResult { FileBytes = package.GetAsByteArray(), FileName = fileName });
        }
    }
}

public class ExportCustomerMarkupRulesEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/customers/markups/export", async (
            ISender sender,
            [FromQuery] Guid? regionId) =>
        {
            var result = await sender.Send(new ExportCustomerMarkupRules.Query
            {
                RegionId = regionId
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
        .WithSummary("Export customer markup rules to Excel")
        .WithDescription(
            "Exports all customer markup rules to a single-sheet .xlsx file. " +
            "Each row is one rule — a customer with both a general rule and product-specific rules appears on multiple rows. " +
            "Columns: Customer Code, Customer Name, Region, Product ('All Products' for customer-wide rules), Markup %. " +
            "Ordered by region, then customer name, with the general rule listed first before product-specific rules. " +
            "Optional filter: `regionId`.")
        .Produces<FileResult>(200)
        .Produces<Error>(400)
        .RequireAuthorization();
    }
}
