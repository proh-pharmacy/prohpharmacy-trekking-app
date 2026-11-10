using Microsoft.Extensions.Options;
using prohpharmacy_trekking_app.Services.DocumentExtraction;
using prohpharmacy_trekking_app.Services.ProductMatching;

namespace prohpharmacy_trekking_app.Extensions;

public static class DocumentMatchingServiceExtensions
{
    public static IServiceCollection AddDocumentMatching(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DocumentMatchingOptions>().Bind(configuration.GetSection("DocumentMatching"))
            .Validate(o => o.MaxFiles is > 0 and <= 20 && o.MaxFileBytes > 0 &&
                o.MaxTotalBytes >= o.MaxFileBytes && o.MaxTotalBytes < DocumentMatchingOptions.RequestBodyLimit &&
                o.MaxPages is > 0 and <= 100 && o.MaxRowsPerFile is > 0 and <= 10000 &&
                o.MaxTextLength is > 0 and <= 10000 && o.MaxConcurrentRequests is > 0 and <= 16 &&
                o.ProcessingTimeoutSeconds > 0 && o.OcrTimeoutSeconds > 0 &&
                o.MinimumSimilarity is >= 0 and <= 100 && o.AmbiguityMargin is >= 0 and <= 100 &&
                o.MaxCandidates is > 0 and <= 20, "Invalid document matching limits.")
            .Validate(o => string.IsNullOrWhiteSpace(o.OcrBaseUrl) ||
                (Uri.TryCreate(o.OcrBaseUrl, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"),
                "OCR URL must be an absolute HTTP(S) URL.")
            .ValidateOnStart();
        services.AddSingleton<DocumentProcessingGate>();
        services.AddSingleton<DocumentFileValidator>();
        services.AddSingleton<DocumentTableRowParser>();
        services.AddSingleton<ProductIdentityGuard>();
        services.AddSingleton<IProductNameMatcher, ProductNameMatcher>();
        services.AddSingleton<MatchedProductRowFactory>();
        services.AddSingleton<ProductAggregationService>();
        services.AddScoped<IProductCatalogueReader, ProductCatalogueReader>();
        services.AddTransient<IProductDocumentExtractor, ExcelProductExtractor>();
        services.AddTransient<IProductDocumentExtractor, PdfProductExtractor>();
        services.AddTransient<IProductDocumentExtractor, ImageProductExtractor>();
        services.AddHttpClient<IOcrClient, PaddleOcrClient>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<DocumentMatchingOptions>>().Value;
            if (!string.IsNullOrWhiteSpace(options.OcrBaseUrl))
                client.BaseAddress = new Uri(options.OcrBaseUrl.TrimEnd('/') + "/");
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.MaxResponseContentBufferSize = 4 * 1024 * 1024;
        });
        return services;
    }
}
