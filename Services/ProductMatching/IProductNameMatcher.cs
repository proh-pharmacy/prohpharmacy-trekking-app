namespace prohpharmacy_trekking_app.Services.ProductMatching;

public interface IProductNameMatcher
{
    ProductNameMatch Match(string name, IReadOnlyList<CatalogueProduct> catalogue);
}
