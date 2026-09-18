using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Customers.Entities;
using prohpharmacy_trekking_app.Features.Customers.Enums;
using prohpharmacy_trekking_app.Providers;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Customers;

public static class ImportCustomers
{
    public class Command : IRequest<Result<ImportResult>>
    {
        public byte[] FileBytes { get; set; } = [];
        public string BusinessNameColumn { get; set; } = string.Empty;
        public string CustomerTypeColumn { get; set; } = string.Empty;
        public string RegionNameColumn { get; set; } = string.Empty;
        public string PrimaryPhoneColumn { get; set; } = string.Empty;
        public string RepFirstNameColumn { get; set; } = string.Empty;
        public string RepLastNameColumn { get; set; } = string.Empty;
        public string RepPhoneColumn { get; set; } = string.Empty;
        public string RepRelationshipColumn { get; set; } = string.Empty;
        public string? TradingNameColumn { get; set; }
        public string? WhatsAppColumn { get; set; }
        public string? RepMiddleNameColumn { get; set; }
        public string? GhanaCardColumn { get; set; }
        public string? DistrictNameColumn { get; set; }
        public string? StreetAddressColumn { get; set; }
        public string? LandmarkColumn { get; set; }
    }

    public class ImportResult
    {
        public int Imported { get; set; }
        public int Skipped { get; set; }
        public List<string> SkippedRows { get; set; } = [];
    }

    internal sealed class Handler(AppDbContext db, AuthProvider auth)
        : IRequestHandler<Command, Result<ImportResult>>
    {
        public async Task<Result<ImportResult>> Handle(Command request, CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(auth.GetUserId(), out var userId))
                return Result.Failure<ImportResult>(Error.BadRequest("Invalid user context."));

            var registeredBy = await db.StaffMembers
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.ApplicationUser!.Id == userId, cancellationToken);

            if (registeredBy is null)
                return Result.Failure<ImportResult>(Error.CreateNotFoundError("Staff member not found for the current user."));

            using var stream = new MemoryStream(request.FileBytes);
            using var package = new ExcelPackage(stream);

            var ws = package.Workbook.Worksheets.FirstOrDefault();
            if (ws is null || ws.Dimension is null)
                return Result.Failure<ImportResult>(Error.BadRequest("The uploaded file contains no data."));

            int totalCols = ws.Dimension.Columns;

            int businessNameCol = -1, customerTypeCol = -1, regionNameCol = -1, primaryPhoneCol = -1;
            int repFirstNameCol = -1, repLastNameCol = -1, repPhoneCol = -1, repRelationshipCol = -1;
            int tradingNameCol = -1, whatsAppCol = -1, repMiddleNameCol = -1, ghanaCardCol = -1;
            int districtNameCol = -1, streetAddressCol = -1, landmarkCol = -1;

            for (int c = 1; c <= totalCols; c++)
            {
                var header = ws.Cells[1, c].Text.Trim();
                if (header.Equals(request.BusinessNameColumn, StringComparison.OrdinalIgnoreCase)) businessNameCol = c;
                if (header.Equals(request.CustomerTypeColumn, StringComparison.OrdinalIgnoreCase)) customerTypeCol = c;
                if (header.Equals(request.RegionNameColumn, StringComparison.OrdinalIgnoreCase)) regionNameCol = c;
                if (header.Equals(request.PrimaryPhoneColumn, StringComparison.OrdinalIgnoreCase)) primaryPhoneCol = c;
                if (header.Equals(request.RepFirstNameColumn, StringComparison.OrdinalIgnoreCase)) repFirstNameCol = c;
                if (header.Equals(request.RepLastNameColumn, StringComparison.OrdinalIgnoreCase)) repLastNameCol = c;
                if (header.Equals(request.RepPhoneColumn, StringComparison.OrdinalIgnoreCase)) repPhoneCol = c;
                if (header.Equals(request.RepRelationshipColumn, StringComparison.OrdinalIgnoreCase)) repRelationshipCol = c;

                if (!string.IsNullOrWhiteSpace(request.TradingNameColumn) && header.Equals(request.TradingNameColumn, StringComparison.OrdinalIgnoreCase)) tradingNameCol = c;
                if (!string.IsNullOrWhiteSpace(request.WhatsAppColumn) && header.Equals(request.WhatsAppColumn, StringComparison.OrdinalIgnoreCase)) whatsAppCol = c;
                if (!string.IsNullOrWhiteSpace(request.RepMiddleNameColumn) && header.Equals(request.RepMiddleNameColumn, StringComparison.OrdinalIgnoreCase)) repMiddleNameCol = c;
                if (!string.IsNullOrWhiteSpace(request.GhanaCardColumn) && header.Equals(request.GhanaCardColumn, StringComparison.OrdinalIgnoreCase)) ghanaCardCol = c;
                if (!string.IsNullOrWhiteSpace(request.DistrictNameColumn) && header.Equals(request.DistrictNameColumn, StringComparison.OrdinalIgnoreCase)) districtNameCol = c;
                if (!string.IsNullOrWhiteSpace(request.StreetAddressColumn) && header.Equals(request.StreetAddressColumn, StringComparison.OrdinalIgnoreCase)) streetAddressCol = c;
                if (!string.IsNullOrWhiteSpace(request.LandmarkColumn) && header.Equals(request.LandmarkColumn, StringComparison.OrdinalIgnoreCase)) landmarkCol = c;
            }

            var missingColumns = new List<string>();
            if (businessNameCol == -1) missingColumns.Add($"'{request.BusinessNameColumn}'");
            if (customerTypeCol == -1) missingColumns.Add($"'{request.CustomerTypeColumn}'");
            if (regionNameCol == -1) missingColumns.Add($"'{request.RegionNameColumn}'");
            if (primaryPhoneCol == -1) missingColumns.Add($"'{request.PrimaryPhoneColumn}'");
            if (repFirstNameCol == -1) missingColumns.Add($"'{request.RepFirstNameColumn}'");
            if (repLastNameCol == -1) missingColumns.Add($"'{request.RepLastNameColumn}'");
            if (repPhoneCol == -1) missingColumns.Add($"'{request.RepPhoneColumn}'");
            if (repRelationshipCol == -1) missingColumns.Add($"'{request.RepRelationshipColumn}'");
            if (!string.IsNullOrWhiteSpace(request.DistrictNameColumn) && districtNameCol == -1)
                missingColumns.Add($"'{request.DistrictNameColumn}'");

            if (missingColumns.Count > 0)
                return Result.Failure<ImportResult>(Error.BadRequest(
                    $"The following columns were not found in the file header row: {string.Join(", ", missingColumns)}."));

            var regions = (await db.Regions
                .Include(r => r.Districts)
                .AsNoTracking()
                .ToListAsync(cancellationToken))
                .ToDictionary(r => r.Name.ToLower(), r => r);

            var regionCounts = (await db.CustomerAccounts
                .GroupBy(c => c.RegionId)
                .Select(g => new { RegionId = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken))
                .ToDictionary(g => g.RegionId, g => g.Count);

            static string? TrimNull(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

            var result = new ImportResult();
            var now = DateTime.UtcNow;
            int totalRows = ws.Dimension.Rows;

            for (int r = 2; r <= totalRows; r++)
            {
                var businessName = ws.Cells[r, businessNameCol].Text.Trim();
                if (string.IsNullOrWhiteSpace(businessName)) continue;

                var rowLabel = $"Row {r} ({businessName})";

                var customerTypeRaw = ws.Cells[r, customerTypeCol].Text.Trim();
                if (!Enum.TryParse<CustomerType>(customerTypeRaw, ignoreCase: true, out var customerType))
                {
                    result.Skipped++;
                    result.SkippedRows.Add($"{rowLabel}: invalid CustomerType '{customerTypeRaw}'.");
                    continue;
                }

                var regionNameRaw = ws.Cells[r, regionNameCol].Text.Trim();
                if (!regions.TryGetValue(regionNameRaw.ToLower(), out var region))
                {
                    result.Skipped++;
                    result.SkippedRows.Add($"{rowLabel}: region '{regionNameRaw}' not found.");
                    continue;
                }

                var primaryPhone = ws.Cells[r, primaryPhoneCol].Text.Trim();
                if (string.IsNullOrWhiteSpace(primaryPhone))
                {
                    result.Skipped++;
                    result.SkippedRows.Add($"{rowLabel}: primary phone is empty.");
                    continue;
                }

                var repFirstName = ws.Cells[r, repFirstNameCol].Text.Trim();
                var repLastName = ws.Cells[r, repLastNameCol].Text.Trim();
                var repPhone = ws.Cells[r, repPhoneCol].Text.Trim();

                if (string.IsNullOrWhiteSpace(repFirstName) || string.IsNullOrWhiteSpace(repLastName) || string.IsNullOrWhiteSpace(repPhone))
                {
                    result.Skipped++;
                    result.SkippedRows.Add($"{rowLabel}: representative first name, last name, and phone are required.");
                    continue;
                }

                var repRelationshipRaw = ws.Cells[r, repRelationshipCol].Text.Trim();
                if (!Enum.TryParse<RelationshipType>(repRelationshipRaw, ignoreCase: true, out var repRelationship))
                    repRelationship = RelationshipType.Owner;

                Guid? districtId = null;
                if (districtNameCol != -1)
                {
                    var districtNameRaw = ws.Cells[r, districtNameCol].Text.Trim();
                    if (!string.IsNullOrWhiteSpace(districtNameRaw))
                    {
                        var district = region.Districts.FirstOrDefault(d =>
                            d.Name.Equals(districtNameRaw, StringComparison.OrdinalIgnoreCase));
                        if (district is null)
                        {
                            result.Skipped++;
                            result.SkippedRows.Add($"{rowLabel}: district '{districtNameRaw}' not found in region '{region.Name}'.");
                            continue;
                        }
                        districtId = district.Id;
                    }
                }

                regionCounts.TryGetValue(region.Id, out var currentCount);
                var customerCode = $"{region.Code.ToUpper()}-{(currentCount + 1):D5}";
                regionCounts[region.Id] = currentCount + 1;

                var tradingName = tradingNameCol != -1 ? TrimNull(ws.Cells[r, tradingNameCol].Text) : null;
                var whatsApp = whatsAppCol != -1 ? TrimNull(ws.Cells[r, whatsAppCol].Text) : null;
                var repMiddleName = repMiddleNameCol != -1 ? TrimNull(ws.Cells[r, repMiddleNameCol].Text) : null;
                var ghanaCard = ghanaCardCol != -1 ? TrimNull(ws.Cells[r, ghanaCardCol].Text) : null;
                var streetAddress = streetAddressCol != -1 ? TrimNull(ws.Cells[r, streetAddressCol].Text) : null;
                var landmark = landmarkCol != -1 ? TrimNull(ws.Cells[r, landmarkCol].Text) : null;

                var account = new CustomerAccount
                {
                    CustomerCode = customerCode,
                    BusinessName = businessName,
                    TradingName = tradingName,
                    CustomerType = customerType,
                    RegionId = region.Id,
                    PrimaryPhoneNumber = primaryPhone,
                    WhatsAppNumber = whatsApp,
                    OwningBranchId = registeredBy.BranchId,
                    RegistrationStatus = RegistrationStatus.Active,
                    RegisteredByStaffId = registeredBy.Id,
                    CreatedOffline = false,
                    RecordedAt = now,
                    CreatedAt = now
                };

                var person = new CustomerPerson
                {
                    CustomerAccountId = account.Id,
                    FirstName = repFirstName,
                    MiddleName = repMiddleName,
                    LastName = repLastName,
                    RelationshipType = repRelationship,
                    PrimaryPhoneNumber = repPhone,
                    GhanaCardNumber = ghanaCard,
                    IsPrimaryContact = true,
                    IsCreditResponsiblePerson = true,
                    IsActive = true,
                    CreatedAt = now
                };

                var location = new CustomerLocation
                {
                    CustomerAccountId = account.Id,
                    LocationType = LocationType.BusinessPremises,
                    RegionId = region.Id,
                    DistrictId = districtId,
                    StreetAddress = streetAddress,
                    LandmarkAndDirections = landmark,
                    CaptureMethod = CaptureMethod.ManualLocationSelection,
                    VerificationStatus = LocationVerificationStatus.Unverified,
                    IsPrimary = true,
                    CapturedByStaffId = registeredBy.Id,
                    CreatedAt = now
                };

                db.CustomerAccounts.Add(account);
                db.CustomerPersons.Add(person);
                db.CustomerLocations.Add(location);
                result.Imported++;
            }

            if (result.Imported > 0)
                await db.SaveChangesAsync(cancellationToken);

            return Result.Success(result);
        }
    }
}

public class ImportCustomersEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/customers/import", async (HttpRequest req, ISender sender) =>
        {
            if (!req.HasFormContentType)
                return Results.UnprocessableEntity(Error.BadRequest("Request must be multipart/form-data."));

            static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

            var form = await req.ReadFormAsync();
            var file = form.Files.GetFile("file");

            if (file is null || file.Length == 0)
                return Results.UnprocessableEntity(Error.BadRequest("No file provided."));

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (ext != ".xlsx" && ext != ".xls")
                return Results.UnprocessableEntity(Error.BadRequest("Only .xlsx and .xls files are accepted."));

            var businessNameColumn = form["businessNameColumn"].FirstOrDefault()?.Trim() ?? string.Empty;
            var customerTypeColumn = form["customerTypeColumn"].FirstOrDefault()?.Trim() ?? string.Empty;
            var regionNameColumn = form["regionNameColumn"].FirstOrDefault()?.Trim() ?? string.Empty;
            var primaryPhoneColumn = form["primaryPhoneColumn"].FirstOrDefault()?.Trim() ?? string.Empty;
            var repFirstNameColumn = form["repFirstNameColumn"].FirstOrDefault()?.Trim() ?? string.Empty;
            var repLastNameColumn = form["repLastNameColumn"].FirstOrDefault()?.Trim() ?? string.Empty;
            var repPhoneColumn = form["repPhoneColumn"].FirstOrDefault()?.Trim() ?? string.Empty;
            var repRelationshipColumn = form["repRelationshipColumn"].FirstOrDefault()?.Trim() ?? string.Empty;

            var requiredFields = new Dictionary<string, string>
            {
                ["businessNameColumn"] = businessNameColumn,
                ["customerTypeColumn"] = customerTypeColumn,
                ["regionNameColumn"] = regionNameColumn,
                ["primaryPhoneColumn"] = primaryPhoneColumn,
                ["repFirstNameColumn"] = repFirstNameColumn,
                ["repLastNameColumn"] = repLastNameColumn,
                ["repPhoneColumn"] = repPhoneColumn,
                ["repRelationshipColumn"] = repRelationshipColumn
            };

            var emptyFields = requiredFields.Where(kv => string.IsNullOrWhiteSpace(kv.Value)).Select(kv => $"'{kv.Key}'").ToList();
            if (emptyFields.Count > 0)
                return Results.UnprocessableEntity(Error.BadRequest(
                    $"The following form fields are required: {string.Join(", ", emptyFields)}."));

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);

            var result = await sender.Send(new ImportCustomers.Command
            {
                FileBytes = ms.ToArray(),
                BusinessNameColumn = businessNameColumn,
                CustomerTypeColumn = customerTypeColumn,
                RegionNameColumn = regionNameColumn,
                PrimaryPhoneColumn = primaryPhoneColumn,
                RepFirstNameColumn = repFirstNameColumn,
                RepLastNameColumn = repLastNameColumn,
                RepPhoneColumn = repPhoneColumn,
                RepRelationshipColumn = repRelationshipColumn,
                TradingNameColumn = Trim(form["tradingNameColumn"].FirstOrDefault()),
                WhatsAppColumn = Trim(form["whatsAppColumn"].FirstOrDefault()),
                RepMiddleNameColumn = Trim(form["repMiddleNameColumn"].FirstOrDefault()),
                GhanaCardColumn = Trim(form["ghanaCardColumn"].FirstOrDefault()),
                DistrictNameColumn = Trim(form["districtNameColumn"].FirstOrDefault()),
                StreetAddressColumn = Trim(form["streetAddressColumn"].FirstOrDefault()),
                LandmarkColumn = Trim(form["landmarkColumn"].FirstOrDefault())
            });

            return result.IsFailure
                ? Results.UnprocessableEntity(result.Error)
                : Results.Ok(result.Value);
        })
        .DisableAntiforgery()
        .WithTags("Customers")
        .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Customers)
        .WithSummary("Bulk import customers from Excel")
        .WithDescription(
            "Upload an .xlsx/.xls file and specify column header names via form fields. " +
            "Required form fields: `file`, `businessNameColumn`, `customerTypeColumn`, `regionNameColumn`, " +
            "`primaryPhoneColumn`, `repFirstNameColumn`, `repLastNameColumn`, `repPhoneColumn`, `repRelationshipColumn`. " +
            "Optional form fields: `tradingNameColumn`, `whatsAppColumn`, `repMiddleNameColumn`, `ghanaCardColumn`, " +
            "`districtNameColumn`, `streetAddressColumn`, `landmarkColumn`. " +
            "Regions are matched by name (case-insensitive). Districts are matched by name within the region. " +
            "Valid CustomerType values: RetailPharmacy, WholesalePharmacy, OTCMedicineSeller, Clinic, Hospital, ChemicalShop, LicensedHealthFacility, Other. " +
            "Valid RepRelationship values: Owner, Proprietor, Director, Manager, PrimaryContact, CreditResponsiblePerson, Guarantor, Other (defaults to Owner if blank or unrecognised). " +
            "Rows missing required fields are skipped and reported in SkippedRows.")
        .Produces<ImportCustomers.ImportResult>(200)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
