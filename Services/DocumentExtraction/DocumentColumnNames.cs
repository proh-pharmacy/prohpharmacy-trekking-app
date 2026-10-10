namespace prohpharmacy_trekking_app.Services.DocumentExtraction;

public static class DocumentColumnNames
{
    public static string? Identify(string text) =>
        string.Concat(text.ToLowerInvariant().Where(char.IsLetterOrDigit)) switch
        {
            "product" or "products" or "productname" or "productnames" or
            "productdescription" or "productitem" or "item" or "items" or
            "itemname" or "description" or "itemdescription" => "name",
            "qty" or "qtys" or "quantity" or "quantities" or "qtyloaded" or
            "qtyunits" or "quantityunits" or "onhand" or "stock" or
            "stockonhand" or "quantityonhand" or "currentstock" or "quantityunit" => "quantity",
            "unit" or "units" or "uom" or "unitofmeasure" or "measure" => "unit",
            "basicqty" or "basicquantity" or "basicunits" => "basic",
            "packagingqty" or "packagingquantity" or "packqty" or "packagingunits" => "packaging",
            _ => null
        };
}
