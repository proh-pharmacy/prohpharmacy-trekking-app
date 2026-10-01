using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Features.Customers;
using prohpharmacy_trekking_app.Features.Customers.Entities;
using prohpharmacy_trekking_app.Features.Customers.Enums;
using prohpharmacy_trekking_app.Features.Fleet.Entities;
using prohpharmacy_trekking_app.Features.Organisation.Entities;
using prohpharmacy_trekking_app.Features.Staff.Entities;
using prohpharmacy_trekking_app.Features.Trekking;
using prohpharmacy_trekking_app.Features.Trekking.Entities;
using prohpharmacy_trekking_app.Features.Trekking.Enums;
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
            var vehicle = new Vehicle { RegistrationNumber = "TEST-1", Region = region };
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
            var seed = await new GetOfflineCustomersByDriverToken.Handler(db).Handle(new() { Token = token, Since = since }, default);
            var row = Assert.Single(seed.Value);
            Assert.Equal(customerId, row.Id);
            Assert.Equal(detail.Value.IdCardFrontUrl, row.IdCardFrontUrl);
            Assert.Equal(detail.Value.IdCardBackUrl, row.IdCardBackUrl);
            Assert.Equal("P-1", row.IdDocumentNumber);
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
