namespace prohpharmacy_trekking_app.Services.ProductMatching;

public interface IProductCatalogueReader
{
    Task<IReadOnlyList<CatalogueProduct>> ReadAsync(CancellationToken cancellationToken);
}
