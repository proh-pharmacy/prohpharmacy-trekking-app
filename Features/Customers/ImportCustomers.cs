using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Customers.Entities;
using prohpharmacy_trekking_app.Features.Customers.Enums;
using prohpharmacy_trekking_app.Features.Ledger.Entities;
using prohpharmacy_trekking_app.Features.Ledger.Enums;
using prohpharmacy_trekking_app.Providers;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Customers;

public static class ImportCustomers
{
    public class Command : IRequest<Result<ImportResult>>
    {
        public byte[] FileBytes { get; set; } = [];
        public string BusinessNameColumn { get; set; } = string.Empty;
        public string? CustomerTypeColumn { get; set; }
        public string? PrimaryPhoneColumn { get; set; }
        public string? RepFirstNameColumn { get; set; }
        public string? RepLastNameColumn { get; set; }
        public string? RepPhoneColumn { get; set; }
        public string? RepRelationshipColumn { get; set; }
        public string? TradingNameColumn { get; set; }
        public string? WhatsAppColumn { get; set; }
        public string? RepMiddleNameColumn { get; set; }
        public string? GhanaCardColumn { get; set; }
        public string? DistrictNameColumn { get; set; }
        public string? StreetAddressColumn { get; set; }
        public string? LandmarkColumn { get; set; }
        public string? OpeningBalanceColumn { get; set; }
    }

    public class ImportResult
    {
        public int Imported { get; set; }
        public int Updated { get; set; }
        public int Skipped { get; set; }
        public int OpeningBalancesCreated { get; set; }
        public List<string> SkippedSheets { get; set; } = [];
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

            if (package.Workbook.Worksheets.Count == 0)
                return Result.Failure<ImportResult>(Error.BadRequest("The uploaded file contains no worksheets."));

            var primarySheet = package.Workbook.Worksheets[0];
            if (primarySheet.Dimension is null)
                return Result.Failure<ImportResult>(Error.BadRequest("The first worksheet contains no data."));

            // Build column index map from the primary sheet header row
            int primaryColCount = primarySheet.Dimension.Columns;
            var primaryHeaders = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int c = 1; c <= primaryColCount; c++)
            {
                var h = primarySheet.Cells[1, c].Text.Trim();
                if (!string.IsNullOrEmpty(h))
                    primaryHeaders[h] = c;
            }

            int businessNameCol = ResolveCol(primaryHeaders, request.BusinessNameColumn);
            if (businessNameCol == -1)
                return Result.Failure<ImportResult>(Error.BadRequest(
                    $"Column '{request.BusinessNameColumn}' not found in the first worksheet header row."));

            int customerTypeCol    = ResolveCol(primaryHeaders, request.CustomerTypeColumn);
            int primaryPhoneCol    = ResolveCol(primaryHeaders, request.PrimaryPhoneColumn);
            int repFirstNameCol    = ResolveCol(primaryHeaders, request.RepFirstNameColumn);
            int repLastNameCol     = ResolveCol(primaryHeaders, request.RepLastNameColumn);
            int repPhoneCol        = ResolveCol(primaryHeaders, request.RepPhoneColumn);
            int repRelationshipCol = ResolveCol(primaryHeaders, request.RepRelationshipColumn);
            int tradingNameCol     = ResolveCol(primaryHeaders, request.TradingNameColumn);
            int whatsAppCol        = ResolveCol(primaryHeaders, request.WhatsAppColumn);
            int repMiddleNameCol   = ResolveCol(primaryHeaders, request.RepMiddleNameColumn);
            int ghanaCardCol       = ResolveCol(primaryHeaders, request.GhanaCardColumn);
            int districtNameCol    = ResolveCol(primaryHeaders, request.DistrictNameColumn);
            int streetAddressCol   = ResolveCol(primaryHeaders, request.StreetAddressColumn);
            int landmarkCol        = ResolveCol(primaryHeaders, request.LandmarkColumn);
            int openingBalanceCol  = ResolveCol(primaryHeaders, request.OpeningBalanceColumn);

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
            var primaryHeaderSet = new HashSet<string>(primaryHeaders.Keys, StringComparer.OrdinalIgnoreCase);

            for (int wsIndex = 0; wsIndex < package.Workbook.Worksheets.Count; wsIndex++)
            {
                var ws = package.Workbook.Worksheets[wsIndex];

                if (ws.Dimension is null) continue;

                var sheetName = ws.Name.Trim();

                // Validate headers match primary sheet for non-primary sheets
                if (wsIndex != 0)
                {
                    var sheetHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    for (int c = 1; c <= ws.Dimension.Columns; c++)
                    {
                        var h = ws.Cells[1, c].Text.Trim();
                        if (!string.IsNullOrEmpty(h)) sheetHeaders.Add(h);
                    }

                    if (!primaryHeaderSet.SetEquals(sheetHeaders))
                    {
                        result.SkippedSheets.Add($"Sheet '{sheetName}': headers do not match the first sheet — skipped.");
                        continue;
                    }
                }

                // Region is derived from the sheet name
                if (!regions.TryGetValue(sheetName.ToLower(), out var region))
                {
                    result.SkippedSheets.Add($"Sheet '{sheetName}': no matching region found — skipped.");
                    continue;
                }

                for (int r = 2; r <= ws.Dimension.Rows; r++)
                {
                    var businessName = ws.Cells[r, businessNameCol].Text.Trim();
                    if (string.IsNullOrWhiteSpace(businessName)) continue;

                    var rowLabel = $"Sheet '{sheetName}' Row {r} ({businessName})";

                    // Customer type — default Other
                    var customerType = CustomerType.Other;
                    if (customerTypeCol != -1)
                    {
                        var raw = ws.Cells[r, customerTypeCol].Text.Trim();
                        if (!string.IsNullOrWhiteSpace(raw))
                            Enum.TryParse(raw, ignoreCase: true, out customerType);
                    }

                    // District — optional, but skip row if provided and not found
                    Guid? districtId = null;
                    if (districtNameCol != -1)
                    {
                        var districtRaw = ws.Cells[r, districtNameCol].Text.Trim();
                        if (!string.IsNullOrWhiteSpace(districtRaw))
                        {
                            var district = region.Districts.FirstOrDefault(d =>
                                d.Name.Equals(districtRaw, StringComparison.OrdinalIgnoreCase));
                            if (district is null)
                            {
                                result.Skipped++;
                                result.SkippedRows.Add($"{rowLabel}: district '{districtRaw}' not found in region '{region.Name}'.");
                                continue;
                            }
                            districtId = district.Id;
                        }
                    }

                    var primaryPhone  = primaryPhoneCol != -1  ? TrimNull(ws.Cells[r, primaryPhoneCol].Text)  : null;
                    var tradingName   = tradingNameCol != -1   ? TrimNull(ws.Cells[r, tradingNameCol].Text)   : null;
                    var whatsApp      = whatsAppCol != -1      ? TrimNull(ws.Cells[r, whatsAppCol].Text)      : null;
                    var streetAddress = streetAddressCol != -1 ? TrimNull(ws.Cells[r, streetAddressCol].Text) : null;
                    var landmark      = landmarkCol != -1      ? TrimNull(ws.Cells[r, landmarkCol].Text)      : null;

                    var repFirstName  = repFirstNameCol != -1  ? TrimNull(ws.Cells[r, repFirstNameCol].Text)  : null;
                    var repLastName   = repLastNameCol != -1   ? TrimNull(ws.Cells[r, repLastNameCol].Text)   : null;
                    var repPhone      = repPhoneCol != -1      ? TrimNull(ws.Cells[r, repPhoneCol].Text)      : null;
                    var repMiddleName = repMiddleNameCol != -1 ? TrimNull(ws.Cells[r, repMiddleNameCol].Text) : null;
                    var ghanaCard     = ghanaCardCol != -1     ? TrimNull(ws.Cells[r, ghanaCardCol].Text)     : null;

                    RelationshipType repRelationship = RelationshipType.Owner;
                    if (repRelationshipCol != -1)
                    {
                        var relRaw = ws.Cells[r, repRelationshipCol].Text.Trim();
                        if (!string.IsNullOrWhiteSpace(relRaw))
                            Enum.TryParse(relRaw, ignoreCase: true, out repRelationship);
                    }

                    var hasRepData = repFirstName is not null && repLastName is not null && repPhone is not null;

                    // Upsert: match by exact BusinessName (case-insensitive) within the same region
                    var existing = await db.CustomerAccounts
                        .Include(a => a.People)
                        .Include(a => a.Locations)
                        .FirstOrDefaultAsync(a =>
                            a.BusinessName.ToLower() == businessName.ToLower() &&
                            a.RegionId == region.Id, cancellationToken);

                    if (existing is not null)
                    {
                        existing.CustomerType = customerType;
                        if (tradingName is not null) existing.TradingName = tradingName;
                        if (primaryPhone is not null) existing.PrimaryPhoneNumber = primaryPhone;
                        if (whatsApp is not null) existing.WhatsAppNumber = whatsApp;
                        existing.UpdatedAt = now;

                        var primaryLocation = existing.Locations.FirstOrDefault(l => l.IsPrimary);
                        if (primaryLocation is not null)
                        {
                            if (districtId.HasValue) primaryLocation.DistrictId = districtId;
                            if (streetAddress is not null) primaryLocation.StreetAddress = streetAddress;
                            if (landmark is not null) primaryLocation.LandmarkAndDirections = landmark;
                        }

                        if (hasRepData)
                        {
                            var primaryPerson = existing.People.FirstOrDefault(p => p.IsPrimaryContact);
                            if (primaryPerson is not null)
                            {
                                primaryPerson.FirstName = repFirstName!;
                                primaryPerson.LastName = repLastName!;
                                primaryPerson.PrimaryPhoneNumber = repPhone!;
                                primaryPerson.RelationshipType = repRelationship;
                                if (repMiddleName is not null) primaryPerson.MiddleName = repMiddleName;
                                if (ghanaCard is not null) primaryPerson.GhanaCardNumber = ghanaCard;
                            }
                            else
                            {
                                db.CustomerPersons.Add(new CustomerPerson
                                {
                                    CustomerAccountId = existing.Id,
                                    FirstName = repFirstName!,
                                    MiddleName = repMiddleName,
                                    LastName = repLastName!,
                                    RelationshipType = repRelationship,
                                    PrimaryPhoneNumber = repPhone!,
                                    GhanaCardNumber = ghanaCard,
                                    IsPrimaryContact = true,
                                    IsCreditResponsiblePerson = true,
                                    IsActive = true,
                                    CreatedAt = now
                                });
                            }
                        }

                        result.Updated++;
                    }
                    else
                    {
                        regionCounts.TryGetValue(region.Id, out var currentCount);
                        var customerCode = $"{region.Code.ToUpper()}-{(currentCount + 1):D5}";
                        regionCounts[region.Id] = currentCount + 1;

                        var account = new CustomerAccount
                        {
                            CustomerCode = customerCode,
                            BusinessName = businessName,
                            TradingName = tradingName,
                            CustomerType = customerType,
                            RegionId = region.Id,
                            PrimaryPhoneNumber = primaryPhone ?? "N/A",
                            WhatsAppNumber = whatsApp,
                            OwningBranchId = registeredBy.BranchId,
                            RegistrationStatus = RegistrationStatus.Active,
                            RegisteredByStaffId = registeredBy.Id,
                            CreatedOffline = false,
                            RecordedAt = now,
                            CreatedAt = now
                        };
                        db.CustomerAccounts.Add(account);

                        if (hasRepData)
                        {
                            db.CustomerPersons.Add(new CustomerPerson
                            {
                                CustomerAccountId = account.Id,
                                FirstName = repFirstName!,
                                MiddleName = repMiddleName,
                                LastName = repLastName!,
                                RelationshipType = repRelationship,
                                PrimaryPhoneNumber = repPhone!,
                                GhanaCardNumber = ghanaCard,
                                IsPrimaryContact = true,
                                IsCreditResponsiblePerson = true,
                                IsActive = true,
                                CreatedAt = now
                            });
                        }

                        db.CustomerLocations.Add(new CustomerLocation
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
                        });

                        if (openingBalanceCol != -1)
                        {
                            var balanceText = ws.Cells[r, openingBalanceCol].Text.Trim();
                            if (decimal.TryParse(balanceText, out var openingBalance) && openingBalance > 0)
                            {
                                db.CustomerLedgerEntries.Add(new CustomerLedgerEntry
                                {
                                    CustomerAccountId = account.Id,
                                    EntryType = LedgerEntryType.Debit,
                                    Amount = openingBalance,
                                    Description = "Opening Balance",
                                    IsAutoGenerated = true,
                                    RecordedAt = now,
                                    CreatedByStaffId = registeredBy.Id,
                                    CreatedAt = now
                                });
                                result.OpeningBalancesCreated++;
                            }
                        }

                        result.Imported++;
                    }
                }
            }

            if (result.Imported > 0 || result.Updated > 0)
                await db.SaveChangesAsync(cancellationToken);

            return Result.Success(result);
        }

        private static int ResolveCol(Dictionary<string, int> headers, string? colName)
        {
            if (string.IsNullOrWhiteSpace(colName)) return -1;
            return headers.TryGetValue(colName, out var idx) ? idx : -1;
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

            var businessNameColumn = Trim(form["businessNameColumn"].FirstOrDefault());
            if (string.IsNullOrWhiteSpace(businessNameColumn))
                return Results.UnprocessableEntity(Error.BadRequest("'businessNameColumn' is required."));

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);

            var result = await sender.Send(new ImportCustomers.Command
            {
                FileBytes = ms.ToArray(),
                BusinessNameColumn = businessNameColumn,
                CustomerTypeColumn    = Trim(form["customerTypeColumn"].FirstOrDefault()),
                PrimaryPhoneColumn    = Trim(form["primaryPhoneColumn"].FirstOrDefault()),
                RepFirstNameColumn    = Trim(form["repFirstNameColumn"].FirstOrDefault()),
                RepLastNameColumn     = Trim(form["repLastNameColumn"].FirstOrDefault()),
                RepPhoneColumn        = Trim(form["repPhoneColumn"].FirstOrDefault()),
                RepRelationshipColumn = Trim(form["repRelationshipColumn"].FirstOrDefault()),
                TradingNameColumn     = Trim(form["tradingNameColumn"].FirstOrDefault()),
                WhatsAppColumn        = Trim(form["whatsAppColumn"].FirstOrDefault()),
                RepMiddleNameColumn   = Trim(form["repMiddleNameColumn"].FirstOrDefault()),
                GhanaCardColumn       = Trim(form["ghanaCardColumn"].FirstOrDefault()),
                DistrictNameColumn    = Trim(form["districtNameColumn"].FirstOrDefault()),
                StreetAddressColumn   = Trim(form["streetAddressColumn"].FirstOrDefault()),
                LandmarkColumn        = Trim(form["landmarkColumn"].FirstOrDefault()),
                OpeningBalanceColumn  = Trim(form["openingBalanceColumn"].FirstOrDefault())
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
            "Upload a multi-sheet .xlsx/.xls workbook. Each sheet name must match a Ghana region name (case-insensitive). " +
            "All sheets are processed; sheets whose headers differ from the first sheet are skipped. " +
            "Required form field: `file`, `businessNameColumn`. " +
            "Optional form fields: `customerTypeColumn` (defaults to Other if absent/unrecognised), `primaryPhoneColumn`, " +
            "`repFirstNameColumn`, `repLastNameColumn`, `repPhoneColumn`, `repRelationshipColumn`, " +
            "`tradingNameColumn`, `whatsAppColumn`, `repMiddleNameColumn`, `ghanaCardColumn`, " +
            "`districtNameColumn`, `streetAddressColumn`, `landmarkColumn`, `openingBalanceColumn`. " +
            "Rep details (first name, last name, phone) are only created when all three are present. " +
            "If a customer with the same business name already exists in that region, their record is updated instead of duplicated. " +
            "Valid CustomerType values: RetailPharmacy, WholesalePharmacy, OTCMedicineSeller, Clinic, Hospital, ChemicalShop, LicensedHealthFacility, Other.")
        .Produces<ImportCustomers.ImportResult>(200)
        .Produces<Error>(422)
        .RequireAuthorization();
    }
}
