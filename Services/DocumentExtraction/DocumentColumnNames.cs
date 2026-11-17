namespace prohpharmacy_trekking_app.Services.DocumentExtraction;

public static class DocumentColumnNames
{
    public static string? Identify(string text)
    {
        // A percentage column must never be interpreted as an absolute price.
        if (text.Contains('%')) return null;
        var key = string.Concat(text.ToLowerInvariant().Where(char.IsLetterOrDigit));
        if (key.EndsWith("ghs", StringComparison.Ordinal)) key = key[..^3];
        return key switch
        {
            "product" or "products" or "productname" or "productnames" or
            "productdescription" or "productitem" or "item" or "items" or
            "itemname" or "description" or "itemdescription" => "name",
            "qty" or "qtys" or "quantity" or "quantities" or "qtyloaded" or
            "qtyunits" or "quantityunits" or "onhand" or "stock" or
            "stockonhand" or "quantityonhand" or "currentstock" or "quantityunit" or
            "ordered" or "qtyordered" or "quantityordered" or "orderedqty" or
            "orderedquantity" or "orderqty" or "orderquantity" => "quantity",
            "unit" or "units" or "uom" or "unitofmeasure" or "measure" or "form" => "unit",
            "basicqty" or "basicquantity" or "basicunits" => "basic",
            "packagingqty" or "packagingquantity" or "packqty" or "packagingunits" => "packaging",
            "rate" or "price" or "unitprice" or "basicunitprice" or "basicprice" or
            "sellingprice" or "saleprice" or "salesprice" or "unitrate" or
            "unitsellingprice" or "sellingunitprice" or "priceperunit" or
            "priceunit" or "basicunitrate" => "price",
            "cost" or "unitcost" => "cost",
            "amount" or "total" or "linetotal" or "lineamount" or "totalamount" or
            "netamount" or "grossamount" or "extendedprice" or "totalprice" => "amount",
            _ => null
        };
    }
}
