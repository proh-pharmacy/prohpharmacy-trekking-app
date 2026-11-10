using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using prohpharmacy_trekking_app.Services.DocumentExtraction;
using Xunit;

namespace ProductMatching.Tests;

public sealed class OcrClientTests
{
    [Fact]
    public async Task Client_posts_page_and_returns_coordinates()
    {
        var handler = new StubHandler(async request =>
        {
            Assert.Equal("http://ocr/extract", request.RequestUri!.AbsoluteUri);
            var parts = Assert.IsType<MultipartFormDataContent>(request.Content).ToList();
            Assert.Equal("2", await parts[1].ReadAsStringAsync());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"tokens":[{"text":"Paracetamol","left":1,"top":2,"right":30,"bottom":12,"confidence":0.97}]}""",
                    Encoding.UTF8, "application/json")
            };
        });
        var client = new PaddleOcrClient(new HttpClient(handler) { BaseAddress = new Uri("http://ocr/") },
            Options.Create(new DocumentMatchingOptions()));
        var tokens = await client.ReadAsync("%PDF-"u8.ToArray(), ".pdf", 2, default);
        Assert.Equal(0.97, Assert.Single(tokens).Confidence);
    }

    [Fact]
    public async Task Unavailable_service_has_a_stable_error()
    {
        var client = new PaddleOcrClient(new HttpClient(new StubHandler(_ => throw new HttpRequestException()))
            { BaseAddress = new Uri("http://ocr/") }, Options.Create(new DocumentMatchingOptions()));
        var error = await Assert.ThrowsAsync<DocumentExtractionException>(() =>
            client.ReadAsync([], ".png", 1, default));
        Assert.Equal("OcrUnavailable", error.Code);
    }

    [Fact]
    public async Task Request_cancellation_is_not_converted_to_file_failure()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var client = new PaddleOcrClient(new HttpClient(new StubHandler(_ => throw new OperationCanceledException(cancellation.Token)))
            { BaseAddress = new Uri("http://ocr/") }, Options.Create(new DocumentMatchingOptions()));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.ReadAsync([], ".png", 1, cancellation.Token));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request);
    }
}
