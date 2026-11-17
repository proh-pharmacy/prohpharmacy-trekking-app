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
            var selected = match.Candidates.FirstOrDefault(c => c.ProductId == match.SuggestedProductId) ?? match.Candidates[0];
            basicUnit = selected.BasicUnitName;
            packagingUnit = selected.PackagingUnitName;
            if (source.GenericQuantityIsBasic && source.Quantity.HasValue)
            {
                // FORM describes the external POS sale form. It does not turn
                // this import's QTY column into a packaging quantity.
                basic ??= source.Quantity;
            }
            else
            {
                var observations = source.QuantityObservations;
                if (!string.IsNullOrWhiteSpace(source.UnitText) && observations.All(o => o.Unit is null) && source.Quantity.HasValue)
                    observations = [new DocumentExtraction.QuantityObservation(source.Quantity.Value, source.UnitText)];
                var mapped = ProductUnitMatcher.Map(observations, selected);
                basic ??= mapped.Basic; packaging ??= mapped.Packaging; warnings.AddRange(mapped.Warnings);
            }
        }
        // The shared unit mapper above handles exact and selected suggestions,
        // including aliases and numeric-only basic quantities. Do not remap
        // them using literal unit-name equality and produce contradictory warnings.
        if (source.Quantity.HasValue && source.BasicQtyText is null && source.PackagingQtyText is null &&
            match.Status != "Exact" && !canMapSelectedCandidate)
        {
            // The generic quantity remains usable even before an unmatched or
            // ambiguous row is assigned to a catalogue product in the review UI.
            basic ??= source.Quantity;
            warnings.Add("Confirm the product and map the quantity to its basic or packaging unit.");
        }
        if (match.Status == "Exact" && packaging.HasValue && match.Candidates[0].PackagingUnitId is null)
        {
            packaging = null;
            warnings.Add("The matched product has no packaging unit; resolve the packaging quantity.");
        }
        return new MatchedProductRow
        {
            Page = source.Page, Worksheet = source.Worksheet, Row = source.Row,
            PriceText = source.PriceText, BasicUnitPrice = source.BasicUnitPrice,
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
