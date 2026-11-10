using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Products.Matching;
using prohpharmacy_trekking_app.Services.ProductMatching;
using Xunit;

namespace ProductMatching.Tests;

public sealed class EndpointTests
{
    [Fact]
    public async Task Upload_is_authenticated_and_partial_failures_preserve_matches()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddDocumentMatching(builder.Configuration);
        builder.Services.AddMediatR(c => c.RegisterServicesFromAssembly(typeof(MatchProductFilesCommand).Assembly));
        builder.Services.AddValidatorsFromAssembly(typeof(MatchProductFilesCommand).Assembly);
        var catalogue = new FakeCatalogue();
        builder.Services.AddSingleton<IProductCatalogueReader>(catalogue);
        builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, TestAuth>("test", _ => { });
        builder.Services.AddAuthorization();
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        new MatchProductFilesEndpoint().AddRoutes(app);
        await app.StartAsync();
        using var client = app.GetTestClient();
        using (var unauthenticated = await client.PostAsync("/api/v1/products/match-files", new MultipartFormDataContent()))
            Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("test");
        using var upload = new MultipartFormDataContent();
        upload.Add(new ByteArrayContent(ExtractionTests.Workbook()), "files", "same.xlsx");
        upload.Add(new ByteArrayContent("corrupt"u8.ToArray()), "files", "same.xlsx");
        using var response = await client.PostAsync("/api/v1/products/match-files", upload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(1, body.GetProperty("summary").GetProperty("exact").GetInt32());
        Assert.Equal(1, body.GetProperty("summary").GetProperty("failedFiles").GetInt32());
        var files = body.GetProperty("files");
        Assert.Equal(0, files[0].GetProperty("fileIndex").GetInt32());
        Assert.Equal(1, files[1].GetProperty("fileIndex").GetInt32());
        Assert.Equal(10, files[0].GetProperty("rows")[0].GetProperty("packagingQty").GetDecimal());
        Assert.Equal(1, catalogue.Calls);

        using var invalid = new MultipartFormDataContent();
        invalid.Add(new ByteArrayContent(ExtractionTests.Workbook()), "files", "stock.xlsx");
        invalid.Add(new StringContent("[null]"), "excelMappings");
        using var rejected = await client.PostAsync("/api/v1/products/match-files", invalid);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
        Assert.Equal(1, catalogue.Calls);

        using var tooMany = new MultipartFormDataContent();
        for (var i = 0; i < 6; i++)
            tooMany.Add(new ByteArrayContent("test"u8.ToArray()), "files", $"{i}.png");
        using var limited = await client.PostAsync("/api/v1/products/match-files", tooMany);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, limited.StatusCode);
        Assert.Equal(1, catalogue.Calls);
    }

    private sealed class FakeCatalogue : IProductCatalogueReader
    {
        public int Calls { get; private set; }
        public Task<IReadOnlyList<CatalogueProduct>> ReadAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<CatalogueProduct>>([MatchingTests.Product("Paracetamol 500mg")]);
        }
    }

    private sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
            Task.FromResult(Request.Headers.ContainsKey("Authorization")
                ? AuthenticateResult.Success(new AuthenticationTicket(
                    new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "test")], "test")), "test"))
                : AuthenticateResult.NoResult());
    }
}
