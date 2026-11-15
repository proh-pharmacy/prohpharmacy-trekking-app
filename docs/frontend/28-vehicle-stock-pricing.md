# Optional vehicle-stock selling price

The external stock document can provide an optional **basic-unit selling price**. `Rate` means a monetary price, not a percentage. This feature uses the existing match-files and vehicle stock endpoints.

## Upload and review

`POST /api/v1/products/match-files` now adds these fields to each matched row:

```json
{
  "extractedName": "ABONIKI BALM",
  "suggestedProductId": "<matched product ID>",
  "basicQty": 192,
  "priceText": "10.34",
  "basicUnitPrice": 10.34
}
```

All existing response fields remain. The example above shows only the relevant fields.

Images, scanned PDFs, text PDFs and Excel share header recognition. Supported headings include `Rate`, `Price`, `Unit Price`, `Basic Price`, `Basic Unit Price`, `Selling Price`, `Sale Price`, `Sales Price`, `Unit Rate`, `Unit Selling Price`, `Selling Unit Price`, and `Price Per Unit`. Casing, spaces and punctuation are ignored; a trailing `GHS` is supported. Cost, line total, amount and percentage columns are not automatically treated as selling prices. Rates are never inferred from quantities or packaging units.

Excel mappings optionally accept `priceColumn`, for example `"priceColumn": "Agreed Price"`, alongside the existing mappings. Use it for an unfamiliar heading or to disambiguate multiple price columns.

A missing or zero price is `null`. Unreadable, negative, percentage or ambiguous price text stays available in `priceText`, while `basicUnitPrice` is `null` with a warning. Review before submission. Supported amounts use a decimal point, optional thousands separators and optional GHS currency prefix, with at most two decimal places. Zero means that no vehicle-specific price was supplied.

`aggregatedProducts[]` also includes `basicUnitPrice` and `warnings`. Identical prices may accompany summed quantities. Conflicting, partly missing or unreadable prices produce a null aggregated price and a warning; choose a price explicitly. Never average or sum unit prices.

The matcher only extracts and matches. It does not save prices or modify stock.

## Save to the vehicle

After review, submit the optional price through the existing endpoint:

```http
POST /api/v1/vehicles/{vehicleId}/stock/load
```

```json
{
  "items": [
    {
      "productId": "<matched product ID>",
      "basicQty": 192,
      "packagingQty": 0,
      "basicUnitPrice": 10.34
    }
  ]
}
```

`basicUnitPrice` is optional and must be positive when used, with at most two decimal places. The backend normalizes zero to `null`. It updates only the vehicle/product stock record. Catalogue prices and markup rules are not changed. Quantity behavior remains additive. Submit each product once; resolve duplicate prices before loading.

For each submitted product, omitted, null or zero `basicUnitPrice` clears any previous vehicle rate and restores normal pricing for future items. Products absent from the request are unchanged. This prevents an unpriced new upload from silently inheriting an older rate. Existing invoice items keep their saved price.

The per-trek `/stock-loads` endpoint remains an allocation endpoint. Save the rate through the vehicle `/stock/load` endpoint; do not expect submitting a rate only to trek allocation to persist it.

## Price precedence

For a new trek sale, the basic-unit price resolves in this order:

1. Current stock price for that trek's assigned vehicle and product.
2. Customer/product markup applied to the catalogue basic price.
3. Customer-wide markup applied to the catalogue basic price.
4. Trek-region/product markup applied to the catalogue basic price.
5. Trek-region-wide markup applied to the catalogue basic price.
6. Catalogue basic price.

A vehicle price is already the final selling price. Do not add any markup. The same rule applies to normal stop products, replacement product lists, admin/driver unplanned items and offline synchronization. Offline actions targeting another trek use the **target trek's vehicle**.

Packaging uses the existing customer → regional → catalogue rules. Do not infer or convert packaging prices from the uploaded basic rate.

## Read responses and existing invoices

Vehicle stock reads and vehicle-filtered product lists expose `vehicleBasicUnitPrice` (nullable) and return the vehicle price in `basicUnitPrice` when present. Catalogue-only product queries still return catalogue prices. Driver vehicle-stock and offline catalogue responses use the trek's regional fallback where no vehicle rate exists; per-customer pricing comes from stop overrides or saved invoice lines.

Stop/invoice product responses expose `usesVehicleStockPrice`. Their `basicUnitPrice` is the **stored invoice-item price**. Display it directly rather than recalculating it from a current product catalogue query.

When issuing an invoice on scheduling, the backend captures the vehicle price even if the draft stop was created before the stock upload. Later stock uploads do not silently rewrite an issued invoice. Upload the stock document before scheduling as the normal workflow.

Global `price-diff`, `syncRequired` and `sync-prices` exclude a basic price when the vehicle currently has a price for that product or the invoice line was saved from vehicle pricing. Packaging can still produce a price difference. The manual stop-product price correction endpoint rejects changes to such a protected basic price; it may still update packaging with the existing basic price supplied.

## Offline cache

`GET /api/v1/treks/driver/{token}/offline/products` includes `vehicleBasicUnitPrice` on products and applicable stop overrides. The basic price is already resolved; do not apply customer markup again.

The endpoint accepts `since` for compatibility but now returns the complete current pricing bundle. Product timestamps alone cannot detect a stock-price upload or deleted markup rule. Replace the cached `stopPriceOverrides` dictionary on refresh, including clearing removed entries. Replace the catalogue for the requested `all` scope so removed vehicle-stock products do not remain in the vehicle-only cache.

## Deployment

Apply migration `20261113085519_AddVehicleStockBasicUnitPrice` before using the updated API. It adds a nullable vehicle-stock basic price and an invoice-line source flag. Existing stock prices default to null, retaining fallback pricing. Existing invoice values are preserved. Price-column interpretation remains in .NET; the Python OCR service returns text and coordinates.
