using FluentValidation;
using MediatR;
using Microsoft.Extensions.Options;
using prohpharmacy_trekking_app.Services.DocumentExtraction;
using prohpharmacy_trekking_app.Services.ProductMatching;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Products.Matching;

internal sealed class MatchProductFilesHandler(
    IValidator<MatchProductFilesCommand> validator,
    IProductCatalogueReader catalogueReader,
    IProductNameMatcher matcher,
    MatchedProductRowFactory rowFactory,
    ProductAggregationService aggregationService,
    DocumentFileValidator fileValidator,
    IEnumerable<IProductDocumentExtractor> extractors,
    DocumentProcessingGate gate,
    IOptions<DocumentMatchingOptions> options,
    ILogger<MatchProductFilesHandler> logger)
    : IRequestHandler<MatchProductFilesCommand, Result<MatchProductFilesResponse>>
{
    public async Task<Result<MatchProductFilesResponse>> Handle(MatchProductFilesCommand request,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
            return Result.Failure<MatchProductFilesResponse>(Error.ValidationError(validation));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.ProcessingTimeoutSeconds));
        try { await gate.EnterAsync(timeout.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Result.Failure<MatchProductFilesResponse>(Error.BadRequest("Document processing is busy; retry shortly."));
        }
        try
        {
            var catalogue = await catalogueReader.ReadAsync(timeout.Token);
            var knownUnits = catalogue.SelectMany(p => new[] { p.BasicUnitName, p.PackagingUnitName })
                .Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => u!.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var response = new MatchProductFilesResponse();
            for (var index = 0; index < request.Files.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var file = request.Files[index];
                var result = new ProductMatchingFileResult
                {
                    FileIndex = index,
                    FileName = Path.GetFileName(file.FileName.Replace('\\', '/'))
                };
                response.Files.Add(result);
                try
                {
                    timeout.Token.ThrowIfCancellationRequested();
                    if (file.Length == 0 || file.Length > options.Value.MaxFileBytes)
                        throw new DocumentExtractionException("FileSize", "The file is empty or exceeds the file-size limit.");
                    var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
                    var extractor = extractors.SingleOrDefault(e => e.CanExtract(extension))
                        ?? throw new DocumentExtractionException("UnsupportedFormat",
                            "Supported formats are .xlsx, .pdf, .jpg, .jpeg and .png.");
                    using var stream = new MemoryStream();
                    await file.CopyToAsync(stream, timeout.Token);
                    var bytes = stream.ToArray();
                    fileValidator.Validate(bytes, extension);
                    var extracted = await extractor.ExtractAsync(bytes, extension,
                        request.ExcelMappings.SingleOrDefault(m => m.FileIndex == index), knownUnits, timeout.Token);
                    result.Errors.AddRange(extracted.Errors);
                    foreach (var row in extracted.Rows)
                    {
                        timeout.Token.ThrowIfCancellationRequested();
                        var match = matcher.Match(row.ExtractedName, catalogue);
                        // Photographs can include camera/desktop chrome below the
                        // table. A line with no quantity and no catalogue
                        // candidate is not actionable and is discarded; genuine
                        // unmatched product text still has candidates or a
                        // quantity and remains available for review.
                        if (match.Candidates.Count == 0 &&
                            (string.IsNullOrWhiteSpace(row.QuantityText) || row.ExtractedName.Trim().Length < 2))
                            continue;
                        result.Rows.Add(rowFactory.Create(row, match));
                    }
                    result.Status = result.Errors.Count == 0 ? "Processed" : result.Rows.Count == 0 ? "Failed" : "Partial";
                }
                catch (DocumentExtractionException ex)
                {
                    result.Errors.Add(new(ex.Code, ex.Message));
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    result.Errors.Add(new("ProcessingTimeout", "The batch processing deadline was reached."));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    logger.LogWarning("Product document file {FileIndex} failed with {ExceptionType}.", index, ex.GetType().Name);
                    result.Errors.Add(new("ExtractionFailed", "The document could not be read. Check its format, encryption and layout."));
                }
                if (result.Errors.Count > 0)
                    result.Status = result.Rows.Count == 0 ? "Failed" : "Partial";
            }
            var rows = response.Files.SelectMany(f => f.Rows).ToList();
            response.Summary = new ProductMatchingSummary
            {
                TotalFiles = response.Files.Count,
                FailedFiles = response.Files.Count(f => f.Status == "Failed"),
                PartialFiles = response.Files.Count(f => f.Status == "Partial"),
                TotalRows = rows.Count, Exact = rows.Count(r => r.MatchStatus == "Exact"),
                Suggested = rows.Count(r => r.MatchStatus == "Suggested"),
                Ambiguous = rows.Count(r => r.MatchStatus == "Ambiguous"),
                Unmatched = rows.Count(r => r.MatchStatus == "Unmatched")
            };
            response.AggregatedProducts = aggregationService.Aggregate(rows);
            return Result.Success(response);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Result.Failure<MatchProductFilesResponse>(Error.BadRequest("Document processing exceeded its time limit."));
        }
        finally { gate.Release(); }
    }
}
