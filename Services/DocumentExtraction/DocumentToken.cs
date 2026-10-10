namespace prohpharmacy_trekking_app.Services.DocumentExtraction;

public sealed record DocumentToken(string Text, double Left, double Top, double Right, double Bottom,
    double? Confidence = null);
