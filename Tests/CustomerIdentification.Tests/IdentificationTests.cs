using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Features.Customers;
using prohpharmacy_trekking_app.Features.Customers.Entities;
using prohpharmacy_trekking_app.Features.Customers.Enums;
using prohpharmacy_trekking_app.Features.Fleet;
using prohpharmacy_trekking_app.Features.Fleet.Entities;
using prohpharmacy_trekking_app.Features.Organisation.Entities;
using prohpharmacy_trekking_app.Features.Products;
using prohpharmacy_trekking_app.Features.Products.Entities;
using prohpharmacy_trekking_app.Features.Staff.Entities;
using prohpharmacy_trekking_app.Features.Trekking;
using prohpharmacy_trekking_app.Features.Trekking.Entities;
using prohpharmacy_trekking_app.Features.Trekking.Enums;
using prohpharmacy_trekking_app.Features.Units.Entities;
using prohpharmacy_trekking_app.Providers;
using prohpharmacy_trekking_app.Services.ImageKit;
using Xunit;

public class IdentificationTests
{
    [Theory]
    [InlineData("{}", true)]
    [InlineData("{\"idDocumentType\":\"GhanaCard\",\"idDocumentNumber\":\" GHA-123 \"}", true)]
    [InlineData("{\"idDocumentType\":\"ghanacard\",\"idDocumentNumber\":\"GHA-123\"}", true)]
    [InlineData("{\"idDocumentType\":\"Unknown\",\"idDocumentNumber\":\"GHA-123\"}", false)]
    [InlineData("{\"idDocumentType\":\"99\",\"idDocumentNumber\":\"GHA-123\"}", false)]
    [InlineData("{\"idDocumentType\":0,\"idDocumentNumber\":\"GHA-123\"}", false)]
    [InlineData("{\"idDocumentType\":\"GhanaCard\"}", false)]
    [InlineData("{\"idDocumentType\":null,\"idDocumentNumber\":null}", false)]
    [InlineData("{\"idDocumentType\":\"GhanaCard\",\"idDocumentNumber\":\"  \"}", false)]
    public void Metadata_requires_a_valid_pair_and_normalizes_numbers(string json, bool valid)
    {
        var error = CustomerIdDocumentInput.Read(JsonSerializer.Deserialize<JsonElement>(json), out var supplied, out var type, out var number);
        Assert.Equal(valid, error is null);
        if (valid && supplied)
        {
            Assert.Equal(CustomerIdDocumentType.GhanaCard, type);
            Assert.Equal("GHA-123", number);
        }
        Assert.NotNull(CustomerIdDocumentInput.Validate(CustomerIdDocumentType.GhanaCard, new string('x', 101)));
    }

    [Fact]
    public async Task Driver_documents_round_trip_through_sync_upload_replacement_and_delta_seed()
    {
        var connection = Environment.GetEnvironmentVariable("CUSTOMER_ID_TEST_POSTGRES")
            ?? throw new InvalidOperationException("Set CUSTOMER_ID_TEST_POSTGRES to an isolated PostgreSQL instance with database creation permission.");
        // Never migrate or clear an application's database. This test owns a new database.
        var builder = new NpgsqlConnectionStringBuilder(connection) { Database = "trek_id_sync_" + Guid.NewGuid().ToString("N") };
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(builder.ConnectionString).Options);
        try
        {
            await db.Database.EnsureCreatedAsync();
            var region = new Region { Name = "Test region", Code = "TST" };
            var otherRegion = new Region { Name = "Other region", Code = "OTH" };
            var district = new District { Name = "Test district", Region = region };
            var branch = new Branch { Name = "Test branch", Code = "BR", Region = region, District = district };
            var staff = new StaffMember { FirstName = "Test", LastName = "Driver", Branch = branch, EmailAddress = "driver@example.test" };
            var vehicle = new Vehicle { RegistrationNumber = "TEST-1", DisplayName = "Test van", Region = region };
            var trip = new TrekkingTrip { TrekNumber = "TRK-TEST", Region = region, Driver = staff, Vehicle = vehicle, Status = TrekStatus.InProgress, DriverToken = Guid.NewGuid() };
            db.AddRange(trip, otherRegion);
            await db.SaveChangesAsync();
            var token = trip.DriverToken!.Value;
            var clientId = Guid.NewGuid();
            var sync = new SyncOfflineActionsByDriverToken.Handler(db, NullLogger<SyncOfflineActionsByDriverToken.Handler>.Instance);
            var register = Action("RegisterCustomer", new { businessName = "Test pharmacy", primaryPhoneNumber = "0200000001", idDocumentType = "GhanaCard", idDocumentNumber = " GHA-123 " }, clientId);
            var update = Action("UpdateCustomer", new { customerClientId = clientId, idDocumentType = "PharmacyLicence", idDocumentNumber = " LIC-123 " });
            var duplicate = Action("RegisterCustomer", new { businessName = "Duplicate document", primaryPhoneNumber = "0200000002", idDocumentType = "PharmacyLicence", idDocumentNumber = "LIC-123" });
            var result = await sync.Handle(new() { Token = token, Actions = [register, update, duplicate] }, default);
            Assert.True(result.IsSuccess);
            Assert.Equal(new[] { "Created", "Created", "Conflict" }, result.Value.Results.Select(r => r.Status));
            var customerId = result.Value.Results[0].ServerId!.Value;
            var customer = await db.CustomerAccounts.SingleAsync(c => c.Id == customerId);
            Assert.Equal("LIC-123", customer.IdDocumentNumber);
            Assert.Equal(CustomerIdDocumentType.PharmacyLicence, customer.IdDocumentType);

            // Retry does not reset metadata to the original registration payload.
            var retry = await sync.Handle(new() { Token = token, Actions = [register] }, default);
            Assert.Equal("AlreadySynced", retry.Value.Results[0].Status);
            Assert.Equal("LIC-123", customer.IdDocumentNumber);
            db.ChangeTracker.Clear();
            // Resolve customerClientId from a previous batch and preserve omitted document fields.
            var edit = await sync.Handle(new() { Token = token, Actions = [Action("UpdateCustomer", new { customerClientId = clientId, tradingName = "Updated pharmacy" })] }, default);
            Assert.Equal("Created", edit.Value.Results[0].Status);
            customer = await db.CustomerAccounts.SingleAsync(c => c.Id == customerId);
            Assert.Equal("LIC-123", customer.IdDocumentNumber);
            var invalid = await sync.Handle(new() { Token = token, Actions = [Action("UpdateCustomer", new { customerId, businessName = "Should not change", idDocumentType = "Bad", idDocumentNumber = "BAD" })] }, default);
            Assert.Equal("Conflict", invalid.Value.Results[0].Status);
            Assert.Equal("Test pharmacy", customer.BusinessName);

            var outsider = new CustomerAccount { CustomerCode = "OTH-1", BusinessName = "Outside", PrimaryPhoneNumber = "0300000001", RegionId = otherRegion.Id, OwningBranchId = branch.Id, RegisteredByStaffId = staff.Id, IdDocumentType = CustomerIdDocumentType.GhanaCard, IdDocumentNumber = "TAKEN" };
            db.Add(outsider);
            await db.SaveChangesAsync();
            var conflict = await sync.Handle(new() { Token = token, Actions = [Action("UpdateCustomer", new { customerId, idDocumentType = "GhanaCard", idDocumentNumber = "TAKEN" })] }, default);
            Assert.Equal("Conflict", conflict.Value.Results[0].Status);
            var crossRegion = await sync.Handle(new() { Token = token, Actions = [Action("UpdateCustomer", new { customerId = outsider.Id, idDocumentType = "GhanaCard", idDocumentNumber = "NEW" })] }, default);
            Assert.Equal("Conflict", crossRegion.Value.Results[0].Status);

            var metadata = new SetCustomerIdDocumentByDriverToken.Handler(db);
            Assert.True((await metadata.Handle(new() { Token = Guid.NewGuid(), CustomerId = customerId, IdDocumentType = CustomerIdDocumentType.Passport, IdDocumentNumber = "P-1" }, default)).IsFailure);
            Assert.True((await metadata.Handle(new() { Token = token, CustomerId = outsider.Id, IdDocumentType = CustomerIdDocumentType.Passport, IdDocumentNumber = "P-1" }, default)).IsFailure);
            Assert.True((await metadata.Handle(new() { Token = token, CustomerId = customerId, IdDocumentType = CustomerIdDocumentType.Passport, IdDocumentNumber = " P-1 " }, default)).IsSuccess);

            var transport = new ImageTransport();
            var imageKit = new ImageKitService(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ImageKitSettings:PrivateKey"] = "test-key" }).Build(), transport);
            var upload = new UploadCustomerIdCardByDriverToken.Handler(db, imageKit);
            async Task<bool> Upload(Guid uploadToken, Guid id, bool front, IFormFile? file) =>
                (await upload.Handle(new() { Token = uploadToken, CustomerId = id, IsFront = front, File = file }, default)).IsSuccess;
            Assert.False(await Upload(Guid.NewGuid(), customerId, true, Photo()));
            Assert.False(await Upload(token, outsider.Id, true, Photo()));
            Assert.False(await Upload(token, customerId, true, null));
            Assert.False(await Upload(token, customerId, true, Photo(0)));
            Assert.False(await Upload(token, customerId, true, Photo(1, "text/plain")));
            Assert.False(await Upload(token, customerId, true, Photo(5 * 1024 * 1024 + 1)));
            Assert.Equal(0, transport.Calls);
            var since = DateTime.UtcNow;
            Assert.True(await Upload(token, customerId, true, Photo()));
            Assert.True(await Upload(token, customerId, false, Photo()));
            Assert.True(await Upload(token, customerId, true, Photo()));
            Assert.Equal(3, transport.Calls);
            db.ChangeTracker.Clear();
            var detail = await new GetCustomer.Handler(db).Handle(new() { Id = customerId }, default);
            Assert.Equal("Passport", detail.Value.IdDocumentType);
            Assert.Equal("P-1", detail.Value.IdDocumentNumber);
            Assert.Equal("https://images.example.test/3.jpg", detail.Value.IdCardFrontUrl);
            Assert.Equal("https://images.example.test/2.jpg", detail.Value.IdCardBackUrl);
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Scheme = "https";
            httpContext.Request.Host = new HostString("api.example.test");
            httpContext.Request.Path = "/api/v1/customers";
            httpContext.Request.QueryString = new QueryString("?pageNumber=1&pageSize=20");
            prohpharmacy_trekking_app.Utilities.Paginator.SetHttpContextAccessor(
                new HttpContextAccessor { HttpContext = httpContext });
            var list = await new GetCustomerList.Handler(db).Handle(new()
            {
                PageNumber = 1,
                PageSize = 20,
                Search = "Test pharmacy"
            }, default);
            var page = Assert.IsType<prohpharmacy_trekking_app.Utilities.Paginator.PaginatedData<object>>(list.Value);
            var listCustomer = Assert.IsType<prohpharmacy_trekking_app.Features.Customers.CreateCustomer.CustomerResponse>(Assert.Single(page.Data));
            Assert.Equal("Passport", listCustomer.IdDocumentType);
            Assert.Equal("P-1", listCustomer.IdDocumentNumber);
            Assert.Equal("https://images.example.test/3.jpg", listCustomer.IdCardFrontUrl);
            Assert.Equal("https://images.example.test/2.jpg", listCustomer.IdCardBackUrl);
            var seed = await new GetOfflineCustomersByDriverToken.Handler(db).Handle(new() { Token = token, Since = since }, default);
            var row = Assert.Single(seed.Value);
            Assert.Equal(customerId, row.Id);
            Assert.Equal(detail.Value.IdCardFrontUrl, row.IdCardFrontUrl);
            Assert.Equal(detail.Value.IdCardBackUrl, row.IdCardBackUrl);
            Assert.Equal("P-1", row.IdDocumentNumber);

            var unit = new Unit { Name = "Tablet" };
            var packagingUnit = new Unit { Name = "Box" };
            var available = new Product { Name = "Available product", Description = "Available description", BasicUnit = unit, BasicUnitPrice = 2, PackagingUnit = packagingUnit, PackagingUnitPrice = 20 };
            var packagingOnly = new Product { Name = "Packaging-only product", BasicUnit = unit, BasicUnitPrice = 3, PackagingUnit = packagingUnit, PackagingUnitPrice = 30 };
            var empty = new Product { Name = "Empty product", BasicUnit = unit, BasicUnitPrice = 4 };
            var untracked = new Product { Name = "Untracked product", BasicUnit = unit, BasicUnitPrice = 5 };
            db.AddRange(available, packagingOnly, empty, untracked);
            var stockCreatedAt = DateTime.UtcNow;
            db.VehicleProductStocks.AddRange(
                new VehicleProductStock { VehicleId = vehicle.Id, Product = available, BasicQuantityOnHand = 12, PackagingQuantityOnHand = 0, LowStockThreshold = 5, CreatedAt = stockCreatedAt.AddMinutes(-2) },
                new VehicleProductStock { VehicleId = vehicle.Id, Product = packagingOnly, BasicQuantityOnHand = 0, PackagingQuantityOnHand = 2, CreatedAt = stockCreatedAt.AddMinutes(-1) },
                new VehicleProductStock { VehicleId = vehicle.Id, Product = empty, BasicQuantityOnHand = 0, PackagingQuantityOnHand = 0, CreatedAt = stockCreatedAt });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var products = new GetProductList.Handler(db);
            var trackedResult = await products.Handle(new() { VehicleId = vehicle.Id }, default);
            var tracked = Assert.IsType<List<object>>(trackedResult.Value)
                .Cast<GetProductList.ProductListResponse>().ToList();
            Assert.Equal(3, tracked.Count);
            Assert.All(tracked, product => Assert.NotNull(product.VehicleStock));
            Assert.DoesNotContain(tracked, product => product.Id == untracked.Id);

            var availableResult = await products.Handle(new() { VehicleId = vehicle.Id, InStockOnly = true }, default);
            var inStock = Assert.IsType<List<object>>(availableResult.Value)
                .Cast<GetProductList.ProductListResponse>().ToList();
            Assert.Equal(2, inStock.Count);
            Assert.Contains(inStock, product => product.Id == available.Id && product.VehicleStock!.BasicQuantityOnHand == 12);
            Assert.Contains(inStock, product => product.Id == packagingOnly.Id && product.VehicleStock!.PackagingQuantityOnHand == 2);
            Assert.DoesNotContain(inStock, product => product.Id == empty.Id);
            Assert.True((await products.Handle(new() { InStockOnly = true }, default)).IsFailure);
            Assert.True((await products.Handle(new() { VehicleId = Guid.NewGuid() }, default)).IsFailure);

            var excludedResult = await products.Handle(new()
            {
                VehicleId = vehicle.Id,
                ExcludeVehicleStock = true
            }, default);
            var excluded = Assert.IsType<List<object>>(excludedResult.Value)
                .Cast<GetProductList.ProductListResponse>().ToList();
            var excludedProduct = Assert.Single(excluded);
            Assert.Equal(untracked.Id, excludedProduct.Id);
            Assert.Null(excludedProduct.VehicleStock);
            Assert.True((await products.Handle(new() { ExcludeVehicleStock = true }, default)).IsFailure);
            Assert.True((await products.Handle(new()
            {
                VehicleId = vehicle.Id,
                ExcludeVehicleStock = true,
                InStockOnly = true
            }, default)).IsFailure);

            var stockCheck = await new CheckTrekStockLoads.Handler(db).Handle(new()
            {
                TrekId = trip.Id,
                Items =
                [
                    new CheckTrekStockLoads.StockLoadLineItem
                    {
                        ProductId = available.Id,
                        BasicQty = 13,
                        PackagingQty = 1
                    },
                    new CheckTrekStockLoads.StockLoadLineItem
                    {
                        ProductId = packagingOnly.Id,
                        BasicQty = 0,
                        PackagingQty = 3
                    }
                ]
            }, default);
            Assert.True(stockCheck.Value.HasWarnings);
            var availableWarning = Assert.Single(stockCheck.Value.Warnings, warning => warning.ProductId == available.Id);
            Assert.Equal("Tablet", availableWarning.BasicUnitName);
            Assert.Equal("Box", availableWarning.PackagingUnitName);
            Assert.Equal(1, availableWarning.BasicShortfall);
            Assert.Equal(1, availableWarning.PackagingShortfall);
            var packagingWarning = Assert.Single(stockCheck.Value.Warnings, warning => warning.ProductId == packagingOnly.Id);
            Assert.Equal(0, packagingWarning.BasicShortfall);
            Assert.Equal(1, packagingWarning.PackagingShortfall);

            var driverStockCheck = new CheckTrekStockLoadsByDriverToken.Handler(db);
            var driverCheck = await driverStockCheck.Handle(new()
            {
                Token = token,
                Items =
                [
                    new CheckTrekStockLoads.StockLoadLineItem
                    {
                        ProductId = packagingOnly.Id,
                        BasicQty = 0,
                        PackagingQty = 3
                    }
                ]
            }, default);
            Assert.True(driverCheck.IsSuccess);
            Assert.Equal(1, Assert.Single(driverCheck.Value.Warnings).PackagingShortfall);
            Assert.True((await driverStockCheck.Handle(new()
            {
                Token = Guid.NewGuid(),
                Items = []
            }, default)).IsFailure);

            var vehicleStockList = await new GetVehicleStock.Handler(db)
                .Handle(new() { VehicleId = vehicle.Id }, default);
            Assert.Equal(
                new[] { empty.Id, packagingOnly.Id, available.Id },
                vehicleStockList.Value.Select(item => item.ProductId));

            var singleStock = new GetVehicleProductStock.Handler(db);
            var availableStock = await singleStock.Handle(new()
            {
                VehicleId = vehicle.Id,
                ProductId = available.Id
            }, default);
            Assert.True(availableStock.IsSuccess);
            Assert.Equal(12, availableStock.Value.BasicQuantityOnHand);
            Assert.Equal(0, availableStock.Value.PackagingQuantityOnHand);
            Assert.Equal("Available description", availableStock.Value.Description);
            Assert.Equal(unit.Id, availableStock.Value.BasicUnitId);
            Assert.Equal("Tablet", availableStock.Value.BasicUnitName);
            Assert.Equal(2, availableStock.Value.BasicUnitPrice);
            Assert.Equal(packagingUnit.Id, availableStock.Value.PackagingUnitId);
            Assert.Equal("Box", availableStock.Value.PackagingUnitName);
            Assert.Equal(20, availableStock.Value.PackagingUnitPrice);
            Assert.True(availableStock.Value.IsActive);
            var emptyStock = await singleStock.Handle(new()
            {
                VehicleId = vehicle.Id,
                ProductId = empty.Id
            }, default);
            Assert.True(emptyStock.IsSuccess);
            Assert.Equal(0, emptyStock.Value.BasicQuantityOnHand);
            Assert.True((await singleStock.Handle(new()
            {
                VehicleId = vehicle.Id,
                ProductId = untracked.Id
            }, default)).IsFailure);
            Assert.True((await singleStock.Handle(new()
            {
                VehicleId = Guid.NewGuid(),
                ProductId = available.Id
            }, default)).IsFailure);

            var stockSummary = new GetVehicleStockSummary.Handler(db);
            var summary = await stockSummary.Handle(new() { VehicleId = vehicle.Id }, default);
            Assert.True(summary.IsSuccess);
            Assert.Equal(vehicle.Id, summary.Value.VehicleId);
            Assert.Equal("Test region - Test van", summary.Value.VehicleInfo);
            Assert.Equal(3, summary.Value.TrackedProductCount);
            Assert.Equal(2, summary.Value.InStockProductCount);
            Assert.Equal(1, summary.Value.OutOfStockProductCount);
            Assert.True((await stockSummary.Handle(new() { VehicleId = Guid.NewGuid() }, default)).IsFailure);

            var applicationUserId = Guid.NewGuid();
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, applicationUserId.ToString()),
                new Claim("staff_id", staff.Id.ToString())
            ], "IntegrationTest"));
            var auth = new AuthProvider(new HttpContextAccessor { HttpContext = httpContext });
            var stockLoad = new LoadVehicleStock.Handler(db, new LoadVehicleStock.Validator(), auth);
            var loadResult = await stockLoad.Handle(new()
            {
                VehicleId = vehicle.Id,
                Items =
                [
                    new LoadVehicleStock.StockLoadItem
                    {
                        ProductId = untracked.Id,
                        BasicQty = 5,
                        PackagingQty = 0
                    }
                ]
            }, default);
            Assert.True(loadResult.IsSuccess);
            var loadedItem = Assert.Single(loadResult.Value.UpdatedStock);
            Assert.Equal("Tablet", loadedItem.BasicUnitName);
            Assert.Null(loadedItem.PackagingUnitName);
            db.ChangeTracker.Clear();
            var stockLedger = await db.VehicleStockLedger.SingleAsync(l => l.ProductId == untracked.Id);
            Assert.Equal(staff.Id, stockLedger.AuthorStaffId);
            Assert.NotEqual(applicationUserId, stockLedger.AuthorStaffId);

            db.VehicleStockLedger.Add(new VehicleStockLedger
            {
                VehicleId = vehicle.Id,
                ProductId = available.Id,
                ChangeType = prohpharmacy_trekking_app.Features.Fleet.Enums.StockChangeType.Addition,
                Source = prohpharmacy_trekking_app.Features.Fleet.Enums.StockChangeSource.ManualLoad,
                BasicQtyChange = 1,
                PackagingQtyChange = 1,
                BasicBalanceAfter = 13,
                PackagingBalanceAfter = 1,
                Reason = "Export test",
                AuthorStaffId = staff.Id,
                RecordedAt = DateTime.UtcNow.AddMinutes(-1)
            });
            db.VehicleStockLedger.Add(new VehicleStockLedger
            {
                VehicleId = vehicle.Id,
                ProductId = available.Id,
                ChangeType = prohpharmacy_trekking_app.Features.Fleet.Enums.StockChangeType.Reduction,
                Source = prohpharmacy_trekking_app.Features.Fleet.Enums.StockChangeSource.ManualLoad,
                BasicQtyChange = 3,
                PackagingQtyChange = 0,
                BasicBalanceAfter = 10,
                PackagingBalanceAfter = 1,
                Reason = "Export reduction test",
                AuthorStaffId = staff.Id,
                RecordedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            var stockExport = new ExportVehicleStockLedger.Handler(db);
            var worksheetExport = await stockExport.Handle(new()
            {
                VehicleId = vehicle.Id,
                ProductId = available.Id,
                From = DateOnly.FromDateTime(DateTime.UtcNow),
                To = DateOnly.FromDateTime(DateTime.UtcNow),
                ExportStyle = "worksheet"
            }, default);
            Assert.True(worksheetExport.IsSuccess);
            using (var package = new ExcelPackage(new MemoryStream(worksheetExport.Value.FileBytes)))
            {
                Assert.Equal(1, package.Workbook.Worksheets.Count);
                var sheet = package.Workbook.Worksheets[0];
                Assert.Equal("Stock Ledger", sheet.Name);
                Assert.Equal("RECORDED AT (GMT)", sheet.Cells[5, 1].Text);
                Assert.Matches(@"^\d{2} [A-Z][a-z]{2} \d{4}, \d{2}:\d{2} (AM|PM)$", sheet.Cells[6, 1].Text);
                Assert.Equal("Available product", sheet.Cells[6, 2].Text);
                Assert.Equal("STOCK CYCLE", sheet.Cells[5, 3].Text);
                Assert.Equal("Reduction", sheet.Cells[6, 3].Text);
                Assert.Equal("3 Tablets", sheet.Cells[6, 5].Text);
                Assert.Equal("1 Box, 10 Tablets", sheet.Cells[6, 6].Text);
                Assert.Equal("FFB91C1C", sheet.Cells[6, 3].Style.Font.Color.Rgb);
                Assert.Equal("FFB91C1C", sheet.Cells[6, 5].Style.Font.Color.Rgb);
                Assert.Equal("FFB91C1C", sheet.Cells[6, 6].Style.Font.Color.Rgb);
                Assert.Equal(ExcelFillStyle.None, sheet.Cells[6, 3].Style.Fill.PatternType);
                Assert.Equal(ExcelFillStyle.None, sheet.Cells[6, 5].Style.Fill.PatternType);
                Assert.Equal(ExcelFillStyle.None, sheet.Cells[6, 6].Style.Fill.PatternType);
                Assert.Equal("Addition", sheet.Cells[7, 3].Text);
                Assert.Equal("1 Box, 1 Tablet", sheet.Cells[7, 5].Text);
                Assert.Equal("1 Box, 13 Tablets", sheet.Cells[7, 6].Text);
                Assert.Equal("FF15803D", sheet.Cells[7, 3].Style.Font.Color.Rgb);
                Assert.Equal("FF15803D", sheet.Cells[7, 5].Style.Font.Color.Rgb);
                Assert.Equal("FF15803D", sheet.Cells[7, 6].Style.Font.Color.Rgb);
                Assert.Equal(ExcelFillStyle.None, sheet.Cells[7, 3].Style.Fill.PatternType);
                Assert.Equal(ExcelFillStyle.None, sheet.Cells[7, 5].Style.Fill.PatternType);
                Assert.Equal(ExcelFillStyle.None, sheet.Cells[7, 6].Style.Fill.PatternType);
                Assert.Equal(8, sheet.Dimension.End.Column);
                Assert.DoesNotContain(
                    sheet.Cells[5, 1, 5, sheet.Dimension.End.Column]
                        .Select(cell => cell.Text),
                    header => header.Equals("PRODUCT ID", StringComparison.OrdinalIgnoreCase));
            }

            var workbookExport = await stockExport.Handle(new()
            {
                VehicleId = vehicle.Id,
                ExportStyle = "workbook"
            }, default);
            Assert.True(workbookExport.IsSuccess);
            using (var package = new ExcelPackage(new MemoryStream(workbookExport.Value.FileBytes)))
            {
                Assert.Equal(2, package.Workbook.Worksheets.Count);
                Assert.Contains(package.Workbook.Worksheets, sheet => sheet.Name == "Available product");
                Assert.Contains(package.Workbook.Worksheets, sheet => sheet.Name == "Untracked product");
            }

            Assert.True((await stockExport.Handle(new()
            {
                VehicleId = vehicle.Id,
                ExportStyle = "invalid"
            }, default)).IsFailure);
            Assert.True((await stockExport.Handle(new()
            {
                VehicleId = vehicle.Id,
                From = new DateOnly(2026, 10, 2),
                To = new DateOnly(2026, 10, 1)
            }, default)).IsFailure);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    private static SyncOfflineActionsByDriverToken.OfflineAction Action(string type, object payload, Guid? id = null) => new()
    {
        Type = type, ClientId = id ?? Guid.NewGuid(), OccurredAt = DateTime.UtcNow, Payload = JsonSerializer.SerializeToElement(payload)
    };

    private static FormFile Photo(int length = 3, string contentType = "image/jpeg") =>
        new(new MemoryStream(new byte[length]), 0, length, "file", "card.jpg") { Headers = new HeaderDictionary(), ContentType = contentType };

    private sealed class ImageTransport : HttpMessageHandler, IHttpClientFactory
    {
        public int Calls { get; private set; }
        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"{{\"url\":\"https://images.example.test/{Calls}.jpg\"}}") });
        }
    }
}
