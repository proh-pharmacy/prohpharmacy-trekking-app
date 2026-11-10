using prohpharmacy_trekking_app.Features.Products.Matching;
using prohpharmacy_trekking_app.Services.DocumentExtraction;

namespace prohpharmacy_trekking_app.Services.ProductMatching;

public sealed class MatchedProductRowFactory
{
    public MatchedProductRow Create(ExtractedProductRow source, ProductNameMatch match)
    {
        var warnings = source.Warnings.Concat(match.Warnings).ToList();
        var basic = source.BasicQty;
        var packaging = source.PackagingQty;
        string? basicUnit = null, packagingUnit = null;
        var canMapSelectedCandidate = match.SuggestedProductId.HasValue && match.Candidates.Count > 0;
        if ((match.Status == "Exact" || canMapSelectedCandidate) && source.QuantityObservations.Count > 0)
        {
            var observations = source.QuantityObservations;
            if (!string.IsNullOrWhiteSpace(source.UnitText) && observations.All(o => o.Unit is null) && source.Quantity.HasValue)
                observations = [new DocumentExtraction.QuantityObservation(source.Quantity.Value, source.UnitText)];
            var selected = match.Candidates.FirstOrDefault(c => c.ProductId == match.SuggestedProductId) ?? match.Candidates[0];
            var mapped = ProductUnitMatcher.Map(observations, selected);
            basic ??= mapped.Basic; packaging ??= mapped.Packaging; warnings.AddRange(mapped.Warnings);
            basicUnit = selected.BasicUnitName;
            packagingUnit = selected.PackagingUnitName;
        }
        if (source.Quantity.HasValue && source.BasicQtyText is null && source.PackagingQtyText is null)
        {
            if (match.Status == "Exact" && !string.IsNullOrWhiteSpace(source.UnitText))
            {
                var product = match.Candidates.Single();
                var unit = ProductNameNormalizer.Normalize(source.UnitText);
                var isBasic = unit == ProductNameNormalizer.Normalize(product.BasicUnitName);
                var isPackaging = product.PackagingUnitName is not null &&
                    unit == ProductNameNormalizer.Normalize(product.PackagingUnitName);
                if (isBasic != isPackaging)
                {
                    if (isBasic) basic = source.Quantity;
                    else packaging = source.Quantity;
                }
                else warnings.Add("The quantity unit does not map uniquely to the product's units.");
            }
            else warnings.Add("Confirm the product and map the quantity to its basic or packaging unit.");
        }
        if (match.Status == "Exact" && packaging.HasValue && match.Candidates[0].PackagingUnitId is null)
        {
            packaging = null;
            warnings.Add("The matched product has no packaging unit; resolve the packaging quantity.");
        }
        return new MatchedProductRow
        {
            Page = source.Page, Worksheet = source.Worksheet, Row = source.Row,
            ExtractedName = source.ExtractedName, QuantityText = source.QuantityText,
            BasicQtyText = source.BasicQtyText, PackagingQtyText = source.PackagingQtyText,
            UnitText = source.UnitText, Quantity = source.Quantity, BasicQty = basic, PackagingQty = packaging,
            QuantityObservations = source.QuantityObservations, BasicUnitText = basicUnit, PackagingUnitText = packagingUnit,
            OcrConfidence = source.OcrConfidence, MatchStatus = match.Status,
            SuggestedProductId = match.SuggestedProductId, Candidates = match.Candidates,
            RequiresReview = true, Warnings = warnings.Distinct().ToList()
        };
    }
}
