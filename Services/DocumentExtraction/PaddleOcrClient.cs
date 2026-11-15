using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace prohpharmacy_trekking_app.Services.DocumentExtraction;

public sealed class PaddleOcrClient(HttpClient http, IOptions<DocumentMatchingOptions> options,
    ILogger<PaddleOcrClient> logger) : IOcrClient
{
    private sealed record OcrResponse(List<DocumentToken> Tokens);
    private sealed record OcrDocumentResponse(List<OcrPagePayload> Pages);
    private sealed record OcrPagePayload(int Page, List<DocumentToken> Tokens, string? Error);

    public async Task<IReadOnlyList<DocumentToken>> ReadAsync(byte[] bytes, string extension, int page,
        CancellationToken cancellationToken)
    {
        if (http.BaseAddress is null)
            throw new DocumentExtractionException("OcrUnavailable", "The document scanner is not available right now.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.OcrTimeoutSeconds));
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(extension == ".pdf" ? "application/pdf" :
            extension == ".png" ? "image/png" : "image/jpeg");
        content.Add(file, "file", "document" + extension);
        content.Add(new StringContent(page.ToString(System.Globalization.CultureInfo.InvariantCulture)), "page");
        try
        {
            using var response = await http.PostAsync("extract", content, timeout.Token);
            await EnsureSuccessAsync(response, timeout.Token);
            if (response.Content.Headers.ContentLength > 4 * 1024 * 1024)
                throw new DocumentExtractionException("OcrResponseLimit", "OCR returned too much text.");
            var payload = await response.Content.ReadFromJsonAsync<OcrResponse>(cancellationToken: timeout.Token);
            if (payload?.Tokens is null || !TokensAreValid(payload.Tokens))
                throw new DocumentExtractionException("InvalidOcrResponse", "OCR returned an invalid response.");
            return payload.Tokens;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DocumentExtractionException("OcrTimeout", "The document scanner took too long to respond. Please try again.");
        }
        catch (HttpRequestException)
        {
            throw new DocumentExtractionException("OcrUnavailable", "The document scanner is temporarily unavailable. Please try again later.");
        }
        catch (JsonException)
        {
            throw new DocumentExtractionException("InvalidOcrResponse", "OCR returned an invalid response.");
        }
    }

    public async Task<IReadOnlyList<OcrPageResult>> ReadPagesAsync(byte[] bytes, IReadOnlyList<int> pages,
        CancellationToken cancellationToken)
    {
        if (http.BaseAddress is null)
            throw new DocumentExtractionException("OcrUnavailable", "The document scanner is not available right now.");
        if (pages.Count == 0) return [];
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var timeoutSeconds = Math.Min(options.Value.QueuedProcessingTimeoutSeconds,
            (options.Value.OcrTimeoutSeconds * pages.Count) + 30);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(file, "file", "document.pdf");
        content.Add(new StringContent(JsonSerializer.Serialize(pages)), "pages");
        try
        {
            using var response = await http.PostAsync("extract-document", content, timeout.Token);
            await EnsureSuccessAsync(response, timeout.Token);
            if (response.Content.Headers.ContentLength > 16 * 1024 * 1024)
                throw new DocumentExtractionException("OcrResponseLimit", "OCR returned too much text.");
            var payload = await response.Content.ReadFromJsonAsync<OcrDocumentResponse>(
                cancellationToken: timeout.Token);
            if (payload?.Pages is null || payload.Pages.Count != pages.Count ||
                payload.Pages.Select(p => p.Page).Distinct().Count() != pages.Count ||
                payload.Pages.Any(p => !pages.Contains(p.Page) || p.Tokens is null || !TokensAreValid(p.Tokens)))
                throw new DocumentExtractionException("InvalidOcrResponse", "OCR returned an invalid response.");
            return payload.Pages.Select(p => new OcrPageResult(p.Page, p.Tokens, p.Error)).ToList();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DocumentExtractionException("OcrTimeout", "The document scanner took too long to respond. Please try again.");
        }
        catch (HttpRequestException)
        {
            throw new DocumentExtractionException("OcrUnavailable", "The document scanner is temporarily unavailable. Please try again later.");
        }
        catch (JsonException)
        {
            throw new DocumentExtractionException("InvalidOcrResponse", "OCR returned an invalid response.");
        }
    }

    private bool TokensAreValid(IReadOnlyCollection<DocumentToken> tokens) =>
        tokens.Count <= options.Value.MaxRowsPerFile * 30 &&
        tokens.All(t => !string.IsNullOrWhiteSpace(t.Text) && t.Text.Length <= options.Value.MaxTextLength &&
            double.IsFinite(t.Left) && double.IsFinite(t.Top) &&
            double.IsFinite(t.Right) && double.IsFinite(t.Bottom) &&
            t.Right >= t.Left && t.Bottom >= t.Top &&
            (!t.Confidence.HasValue || double.IsFinite(t.Confidence.Value) &&
                t.Confidence.Value >= 0 && t.Confidence.Value <= 1));

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var detail = await ReadServiceDetailAsync(response, cancellationToken);
        var code = response.StatusCode switch
        {
            System.Net.HttpStatusCode.ServiceUnavailable => "OcrServiceUnavailable",
            System.Net.HttpStatusCode.GatewayTimeout => "OcrServiceTimeout",
            System.Net.HttpStatusCode.RequestTimeout => "OcrServiceTimeout",
            System.Net.HttpStatusCode.BadRequest => "OcrServiceRejected",
            System.Net.HttpStatusCode.UnprocessableEntity => "OcrServiceRejected",
            _ => "OcrServiceError"
        };
        logger.LogWarning("OCR service returned HTTP {StatusCode} with detail: {Detail}",
            (int)response.StatusCode, detail);
        var message = code switch
        {
            "OcrServiceUnavailable" => "The document scanner is temporarily unavailable. Please try again later.",
            "OcrServiceTimeout" => "The document scanner took too long to process this file. Try a clearer or smaller image.",
            "OcrServiceRejected" => "The document could not be read. Try a clearer image.",
            _ => "The document scanner could not process this file. Please try again later."
        };
        throw new DocumentExtractionException(code, message);
    }

    private static async Task<string> ReadServiceDetailAsync(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (body.Length > 4096)
                return "The OCR service returned an oversized error response.";
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.TryGetProperty("detail", out var detail) &&
                detail.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(detail.GetString()))
                return detail.GetString()!.Trim();
        }
        catch (JsonException)
        {
        }
        catch (HttpRequestException)
        {
        }
        return "The OCR service did not provide an error detail.";
    }
}
