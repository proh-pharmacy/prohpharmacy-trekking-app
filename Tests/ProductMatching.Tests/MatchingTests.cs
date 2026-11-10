using Microsoft.Extensions.Options;
using prohpharmacy_trekking_app.Services.DocumentExtraction;
using prohpharmacy_trekking_app.Services.ProductMatching;
using Xunit;

namespace ProductMatching.Tests;

public sealed class MatchingTests
{
    private readonly ProductNameMatcher matcher = new(Options.Create(new DocumentMatchingOptions()), new ProductIdentityGuard());

    internal static CatalogueProduct Product(string name) =>
        new(Guid.NewGuid(), name, Guid.NewGuid(), "Tablet", Guid.NewGuid(), "Box");

    [Fact]
    public void Exact_match_normalizes_spacing_without_losing_strength()
    {
        var p = Product("Paracetamol 500mg");
        var result = matcher.Match("  PARACETAMOL   500 mg ", [p]);
        Assert.Equal("Exact", result.Status);
        Assert.Equal(p.ProductId, result.SuggestedProductId);
    }

    [Fact]
    public void Duplicate_catalogue_names_are_ambiguous()
    {
        var result = matcher.Match("Paracetamol 500mg", [Product("Paracetamol 500mg"), Product("PARACETAMOL 500mg")]);
        Assert.Equal("Ambiguous", result.Status);
        Assert.Null(result.SuggestedProductId);
        Assert.Equal(2, result.Candidates.Count);
    }

    [Theory]
    [InlineData("Paracetmol 500mg", "Paracetamol 250mg")]
    [InlineData("Paracetmol 500mg tablet", "Paracetamol 500mg syrup")]
    [InlineData("Paracetmol 500mg 20", "Paracetamol 500mg 100")]
    [InlineData("Paracetmol 5mg", "Paracetamol 5ml")]
    public void Conflicting_identity_details_are_not_suggested(string source, string target)
    {
        Assert.Equal("Unmatched", matcher.Match(source, [Product(target)]).Status);
    }

    [Fact]
    public void Typo_with_compatible_strength_is_suggested()
    {
        Assert.Equal("Suggested", matcher.Match("Paracetmol 500mg", [Product("Paracetamol 500mg")]).Status);
    }

    [Fact]
    public void Missing_strength_requires_disambiguation()
    {
        Assert.Equal("Ambiguous", matcher.Match("Paracetamol", [Product("Paracetamol 500mg")]).Status);
    }

    [Fact]
    public void Unrelated_names_are_unmatched()
    {
        Assert.Equal("Unmatched", matcher.Match("Banana", [Product("Paracetamol 500mg")]).Status);
    }

    [Theory]
    [InlineData("1,000")]
    [InlineData("1,5")]
    [InlineData("-5")]
    [InlineData("ten")]
    [InlineData("5O")]
    public void Uncertain_quantities_are_not_guessed(string value)
    {
        var warnings = new List<string>();
        Assert.Null(QuantityParser.Parse(value, "Quantity", warnings));
        Assert.NotEmpty(warnings);
    }

    [Fact]
    public void Generic_quantity_maps_only_to_unique_exact_unit()
    {
        var product = Product("Paracetamol 500mg");
        var row = new ExtractedProductRow { ExtractedName = product.Name, Quantity = 3, QuantityText = "3", UnitText = "Box" };
        var result = new MatchedProductRowFactory().Create(row, matcher.Match(row.ExtractedName, [product]));
        Assert.Equal(3m, result.PackagingQty);
        Assert.Null(result.BasicQty);
        Assert.True(result.RequiresReview);
        row.UnitText = "cartons";
        Assert.Null(new MatchedProductRowFactory().Create(row, matcher.Match(row.ExtractedName, [product])).PackagingQty);
    }
}
