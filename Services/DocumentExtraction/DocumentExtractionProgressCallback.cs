namespace prohpharmacy_trekking_app.Services.DocumentExtraction;

public delegate Task DocumentExtractionProgressCallback(
    int completedPages,
    int totalPages,
    CancellationToken cancellationToken);
