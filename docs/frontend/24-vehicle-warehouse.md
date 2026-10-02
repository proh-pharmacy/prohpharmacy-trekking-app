# 24 — Vehicle Warehouse and Trek Stock Loads

Verified against the implementation on 2026-10-01. Vehicle stock persists between treks. A trek stock load is a separate allocation record: saving or removing it does not change warehouse quantities. Delivery recording also leaves warehouse quantities unchanged; completion performs deductions.

Add a **Stock** tab on vehicle details with current balances, bulk load/removal forms and stock history. Add a **Stock loads** tab to trek details for allocations and quantity warnings. All endpoints in this guide require a staff Bearer JWT; there are no driver-token stock-management equivalents.

For a searchable, paginated product picker limited to a vehicle, use `GET /api/v1/products?vehicleId={vehicleId}&inStockOnly=true`. It returns full product catalogue fields plus `vehicleStock`; see [Products](./08-products.md#get-apiv1products). Omitting `inStockOnly` includes tracked zero-balance products. For the add-product picker, use `excludeVehicleStock=true` instead so already-catalogued products never occupy a page.

## Vehicle endpoints

| Method | Route | Success |
|---|---|---|
| GET | `/api/v1/vehicles/{vehicleId}/stock` | `200`, `StockItem[]`, newest stock records first |
| GET | `/api/v1/vehicles/{vehicleId}/stock/summary` | `200`, tracked/in-stock/out-of-stock product counts |
| GET | `/api/v1/vehicles/{vehicleId}/stock/{productId}` | `200`, one current `StockItem` |
| POST | `/api/v1/vehicles/{vehicleId}/stock/load` | `200`, mutation response |
| POST | `/api/v1/vehicles/{vehicleId}/stock/remove` | `200`, same mutation shape |
| GET | `/api/v1/vehicles/{vehicleId}/stock/ledger` | `200`, array or pagination envelope |
| GET | `/api/v1/vehicles/{vehicleId}/stock/ledger/export` | `200`, filtered `.xlsx` download |

GET routes return HTTP `404` for an unknown vehicle. POST handler failures return HTTP `422` (including missing vehicle, with body `code: "404"`). See [error conventions](./00-api-conventions.md).

### Current stock

```json
[
  {
    "stockId": "<stock-guid>",
    "productId": "<product-guid>",
    "productName": "Paracetamol 500mg",
    "description": "Pain relief tablets",
    "basicUnitId": "<unit-guid>",
    "basicUnitName": "Tablet",
    "basicUnitPrice": 2.5,
    "packagingUnitId": "<unit-guid>",
    "packagingUnitName": "Box",
    "packagingUnitPrice": 60,
    "isActive": true,
    "basicQuantityOnHand": 100,
    "packagingQuantityOnHand": 2,
    "lowStockThreshold": null,
    "isLowStock": false,
    "updatedAt": null
  }
]
```

Results are ordered by the vehicle stock record's `createdAt` descending, so the most recently added vehicle products appear first. Every stock item includes the product description, active status, and individual basic/packaging unit IDs, names, and prices. Packaging fields are `null` when the product has no packaging unit. `isLowStock` is true when a threshold exists and basic stock ≤ threshold. There is no endpoint here to configure the threshold. `updatedAt` can be null for newly created records. Basic and packaging quantities are tracked separately; do not convert or merge them automatically.

### Stock summary counts

```http
GET /api/v1/vehicles/{vehicleId}/stock/summary
```

Response `200 OK`:

```json
{
  "vehicleId": "<vehicle-guid>",
  "vehicleInfo": "Greater Accra - Delivery Van 1",
  "trackedProductCount": 25,
  "inStockProductCount": 18,
  "outOfStockProductCount": 7
}
```

- `vehicleInfo` is the vehicle label formatted as `Region Name - Vehicle Display Name`.
- `trackedProductCount` counts every product with a vehicle stock record, regardless of quantity or product status.
- `inStockProductCount` counts tracked products where either the basic or packaging quantity is greater than zero.
- `outOfStockProductCount` counts tracked products where both quantities are zero.

The counts describe products, not summed units. This matters because products can use different basic and packaging units. A vehicle with no tracked products returns `200` with all three counts set to zero. An unknown vehicle returns `404`.

The summary deliberately does not return a threshold-based count. No vehicle thresholds are currently configured, and zero quantity is represented explicitly as `outOfStockProductCount`. A future sales-velocity recommendation should be introduced separately rather than changing these definitions.

### One product's current quantity

```http
GET /api/v1/vehicles/{vehicleId}/stock/{productId}
```

Response `200 OK`:

```json
{
  "stockId": "<stock-guid>",
  "productId": "<product-guid>",
  "productName": "Paracetamol 500mg",
  "description": "Pain relief tablets",
  "basicUnitId": "<unit-guid>",
  "basicUnitName": "Tablet",
  "basicUnitPrice": 2.5,
  "packagingUnitId": "<unit-guid>",
  "packagingUnitName": "Box",
  "packagingUnitPrice": 60,
  "isActive": true,
  "basicQuantityOnHand": 100,
  "packagingQuantityOnHand": 2,
  "lowStockThreshold": 20,
  "isLowStock": false,
  "updatedAt": "2026-10-01T12:00:00Z"
}
```

Use this endpoint when refreshing a single row or checking the latest quantity before an adjustment. It returns the same complete `StockItem` shape as the vehicle stock list and load/remove mutation responses. It returns `404` when the vehicle does not exist or when the product has no stock record for that vehicle. A tracked product with zero quantities returns `200` with both quantities set to zero; do not interpret zero as an untracked product.

For an add-product picker, use:

```http
GET /api/v1/products?vehicleId={vehicleId}&excludeVehicleStock=true&isActive=true&pageNumber=1&pageSize=20
```

This excludes every product already in the vehicle catalogue, including tracked products whose current quantities are zero. See [Products](./08-products.md#get-apiv1products) for response and filter rules.

Recommended add-product flow:

1. Open the picker with the exclusion query above and keep `search`, `pageNumber`, and `pageSize` on the server.
2. Submit the selected product through `POST /api/v1/vehicles/{vehicleId}/stock/load` with its initial quantities.
3. On success, refresh the exclusion query and the vehicle stock list. The newly tracked product moves out of the picker and into current stock.
4. To refresh only that new stock row, call `GET /api/v1/vehicles/{vehicleId}/stock/{productId}`.

Do not combine `excludeVehicleStock=true` with `inStockOnly=true`; the API returns `400`. Both filters require `vehicleId`. An unknown vehicle returns `404`.

### Load and remove

Loading adds quantities to the existing balance or creates the product stock record. Send an object containing `items`, **not a bare array**, despite the endpoint's Swagger description.

```json
{
  "items": [
    { "productId": "<product-guid>", "basicQty": 100, "packagingQty": 2 }
  ]
}
```

Removal uses the same items plus a required reason:

```json
{
  "reason": "Damaged goods",
  "items": [
    { "productId": "<product-guid>", "basicQty": 5, "packagingQty": 0 }
  ]
}
```

For both, `items` must be nonempty, `productId` nonempty, `basicQty` > 0 and `packagingQty` ≥ 0. Packaging-only adjustments are not supported by these validators. Select real catalogue products and deduplicate product IDs before sending: duplicate new products are not merged by the handler. No low-stock threshold or author field is accepted.

Removal requires an existing stock record for every requested product. It clamps each quantity to zero rather than rejecting an over-removal. Ledger changes record the quantity actually deducted. It does not delete the stock row.

Both mutations derive the ledger author from the authenticated token's staff identity; the frontend does not send an author ID. They return this shape, where `updatedStock` contains the affected products only using the complete stock-item shape above:

```json
{
  "productsUpdated": 1,
  "updatedStock": [
    {
      "stockId": "<stock-guid>",
      "productId": "<product-guid>",
      "productName": "Paracetamol 500mg",
      "description": "Pain relief tablets",
      "basicUnitId": "<unit-guid>",
      "basicUnitName": "Tablet",
      "basicUnitPrice": 2.5,
      "packagingUnitId": "<unit-guid>",
      "packagingUnitName": "Box",
      "packagingUnitPrice": 60,
      "isActive": true,
      "basicQuantityOnHand": 95,
      "packagingQuantityOnHand": 2,
      "lowStockThreshold": null,
      "isLowStock": false,
      "updatedAt": "2026-10-01T11:00:00Z"
    }
  ]
}
```

These adjustments are additive/subtractive and have no idempotency key. Disable duplicate submission; after an uncertain network result, fetch stock/history before retrying. Refresh stock and ledger after success.

### Stock ledger

Query parameters: optional `productId`, `source`, `from`, `to`, `pageNumber`, `pageSize`. Sources: `ManualLoad`, `TrekCompletion`, `ReturnApproval` (case-insensitive filter). `from` and `to` are inclusive bounds on `recordedAt` and accept any ISO-8601 timestamp (e.g. `2026-01-01T00:00:00Z`). Sort is fixed to `recordedAt_desc`; no search or sort query is exposed. Send `pageNumber=1&pageSize=20` for the [pagination envelope](./00-api-conventions.md); omitting `pageSize` returns an array. There is no enforced page-size cap in the query builder.

Each row has:

```json
{
  "id": "<ledger-guid>",
  "productId": "<product-guid>",
  "productName": "Paracetamol 500mg",
  "basicUnitName": "Tablet",
  "packagingUnitName": "Box of 100",
  "changeType": "Reduction",
  "source": "ManualLoad",
  "basicQtyChange": 5,
  "packagingQtyChange": 0,
  "basicBalanceAfter": 95,
  "packagingBalanceAfter": 10,
  "reason": "Damaged goods",
  "authorName": "Ama Owusu",
  "recordedAt": "2026-10-01T11:00:00Z"
}
```

`changeType` is `Addition` or `Reduction`; quantity changes are nonnegative magnitudes. Manual removals also use `source: "ManualLoad"`—use `changeType` for the sign. `basicBalanceAfter` and `packagingBalanceAfter` are post-change snapshots of each unit tracked separately — pair them with `basicUnitName` and `packagingUnitName` when rendering (e.g. `95 Tablet`, `10 Box of 100`). `packagingUnitName` is nullable (product has no packaging unit). `authorName` is nullable. The entity's `referenceId` is not exposed in this DTO.

### Export stock ledger to Excel

```http
GET /api/v1/vehicles/{vehicleId}/stock/ledger/export?productId={productId}&from=2026-09-01&to=2026-09-30&exportStyle=worksheet
```

`productId` is optional. When it is omitted, the export includes all products matching the other filters; it is not necessary to request each product separately. All filters are optional:

| Parameter | Values | Behaviour |
|---|---|---|
| `productId` | Product GUID | Export only that product; omit for all products |
| `source` | `ManualLoad`, `TrekCompletion`, `ReturnApproval` | Export only that movement source |
| `from` | `YYYY-MM-DD` | Include entries from the start of this UTC date |
| `to` | `YYYY-MM-DD` | Include entries through the end of this UTC date |
| `exportStyle` | `worksheet` or `workbook` | Defaults to `worksheet` |

`worksheet` creates one worksheet named **Stock Ledger** containing all matching products. `workbook` creates one worksheet per matching product and uses the product name as the worksheet name. Unsafe Excel characters are replaced, names are truncated to 31 characters, and duplicate product names receive a numeric suffix.

Examples:

```http
# All products and all dates in one worksheet
GET /api/v1/vehicles/{vehicleId}/stock/ledger/export?exportStyle=worksheet

# All products within an inclusive date range, separated by product
GET /api/v1/vehicles/{vehicleId}/stock/ledger/export?from=2026-09-01&to=2026-09-30&exportStyle=workbook

# One product within an inclusive date range
GET /api/v1/vehicles/{vehicleId}/stock/ledger/export?productId={productId}&from=2026-09-01&to=2026-09-30&exportStyle=worksheet
```

The downloaded workbook includes the vehicle, applied period, human-readable product name, stock cycle/source, quantity, balance, reason, author and timestamp. **Recorded At (GMT)** is a sortable Excel date displayed in a human-readable form such as `02 Oct 2026, 02:35 PM`, matching Ghana time. Basic and packaging quantities are combined into readable values: for example, `1 Box, 3 Tablets`; when only one unit has a value it renders as `3 Tablets`. The **Stock Cycle**, **Quantity**, and **Balance** cells use green foreground text for additions and red foreground text for reductions; their backgrounds are unchanged. Separate unit columns and the internal product GUID are not included. An empty result still downloads a valid workbook with headers. Invalid styles, sources or reversed date ranges return `400`; an unknown vehicle returns `404`.

## Trek stock-load endpoints

| Method | Route | Success |
|---|---|---|
| GET | `/api/v1/treks/{trekId}/stock-loads` | `200`, all load rows |
| GET | `/api/v1/treks/{trekId}/stock-loads/check` | `200`, warnings; **requires JSON body** |
| POST | `/api/v1/treks/{trekId}/stock-loads/check` | `200`, browser-compatible staff warning check |
| POST | `/api/v1/treks/driver/{token}/stock-loads/check` | `200`, driver-token warnings; no staff JWT |
| POST | `/api/v1/treks/{trekId}/stock-loads` | `200`, affected load rows |
| DELETE | `/api/v1/treks/{trekId}/stock-loads/{productId}` | `204`, empty body |

GET failures use HTTP `404`. POST and DELETE handler failures use HTTP `422`. Mutations reject `Completed` and `Cancelled` treks. Delete uses the product ID, not the load row ID, and fails if the load is absent.

### Save allocations

```json
{
  "items": [
    { "productId": "<product-guid>", "basicQty": 120, "packagingQty": 2 }
  ]
}
```

Both quantities must be ≥ 0; zero allocations are allowed. Items must be nonempty with nonempty product IDs. Use one row per product. Saving **replaces** quantities for supplied products and adds new products; it leaves omitted products unchanged. Delete explicitly to remove a line. POST returns only affected rows; GET returns all rows ordered by product name:

```json
[
  {
    "id": "<load-guid>",
    "productId": "<product-guid>",
    "productName": "Paracetamol 500mg",
    "basicUnitId": "<unit-guid>",
    "basicUnitName": "Tablet",
    "packagingUnitId": "<unit-guid>",
    "packagingUnitName": "Box",
    "basicQuantityLoaded": 120,
    "packagingQuantityLoaded": 2,
    "vehicleBasicOnHand": 95,
    "vehiclePackagingOnHand": 4,
    "exceedsVehicleStock": true,
    "loadedBy": "Ama Owusu",
    "loadedAt": "2026-10-01T11:30:00Z"
  }
]
```

Missing warehouse records produce `vehicleBasicOnHand: null`, `vehiclePackagingOnHand: null` and `exceedsVehicleStock: false`. That means untracked stock, not confirmed availability. `exceedsVehicleStock` is true when either the basic or packaging load exceeds its matching vehicle balance. Packaging unit fields are `null` when the product has no packaging unit.

### Warning check and browser limitation

The implemented GET `/stock-loads/check` binds a **bare JSON array body**:

```json
[
  { "productId": "<product-guid>", "basicQty": 120, "packagingQty": 2 }
]
```

Response:

```json
{
  "hasWarnings": true,
  "warnings": [
    {
      "productId": "<product-guid>",
      "productName": "Paracetamol 500mg",
      "basicUnitId": "<unit-guid>",
      "basicUnitName": "Tablet",
      "packagingUnitId": "<unit-guid>",
      "packagingUnitName": "Box",
      "requestedBasicQty": 120,
      "availableBasicQty": 95,
      "basicShortfall": 25,
      "requestedPackagingQty": 6,
      "availablePackagingQty": 4,
      "packagingShortfall": 2
    }
  ]
}
```

No warnings yields `{ "hasWarnings": false, "warnings": [] }`. The handler compares basic and packaging quantities independently against the matching vehicle balances and returns their unit IDs/names. A warning is returned when either unit is short; a unit with sufficient stock has a shortfall of zero. Warnings never block saving.

#### Important: products outside the vehicle catalogue

The current check only evaluates products that already have a `VehicleProductStock` record for the trek's vehicle. If a submitted product is not tracked by that vehicle, the handler silently skips it and does **not** add a warning. Consequently, `hasWarnings: false` means only that no tracked product exceeded its balance; it does not prove that every submitted product belongs to the vehicle catalogue.

Until the backend returns an explicit `NotTracked` warning, the frontend must compare every submitted product ID with the selected vehicle's catalogue. Use `GET /api/v1/products?vehicleId={vehicleId}` or the trek stock-load response's nullable `vehicleBasicOnHand` / `vehiclePackagingOnHand` values. Present missing products separately as **Not in vehicle catalogue** and require the user to add them to the vehicle before confirming the trek load.

The intended future warning should distinguish the cases with a `warningType` such as `NotTracked` or `InsufficientStock`; this is not part of the current response contract.

Browser Fetch cannot send a GET body. Staff frontends should use the authenticated POST route at the same path:

```http
POST /api/v1/treks/{trekId}/stock-loads/check
```

```json
{
  "items": [
    { "productId": "<product-guid>", "basicQty": 120, "packagingQty": 2 }
  ]
}
```

The legacy GET route remains available for non-browser clients. The driver portal should use the anonymous driver-token route instead:

```http
POST /api/v1/treks/driver/{token}/stock-loads/check
```

```json
{
  "items": [
    { "productId": "<product-guid>", "basicQty": 120, "packagingQty": 2 }
  ]
}
```

This route requires no `Authorization` header. The driver token is the credential and resolves the trek and its vehicle; an invalid token returns `404`. It is a read-only dry run and returns the same warning response documented above. Keep the token private and do not make the staff `trekId` route anonymous.

## Completion and return effects

Completion deducts delivered quantities for tracked products from the trek's vehicle, floors balances at zero and appends `Reduction` / `TrekCompletion` ledger rows. Both planned and unplanned products are considered, but the current code only includes stop-product rows where `basicQtyDelivered > 0`; packaging-only deliveries are skipped. Products without vehicle stock records are skipped too. Trek load amounts do not cap deductions.

Approved returns add basic and packaging quantities to an existing stock record on the **capturing trek's vehicle**, with `Addition` / `ReturnApproval` history. No record is created if the product is untracked. Pending/rejected returns have no stock effect. Leftovers persist for future treks.

After completion or approval, refresh stock, stock ledger, trek allocations and the [driver report](./25-driver-trek-report.md). Treat staff completion as a single action: the status handler currently allows repeated completion and deducts again. Reopening/cancelling does not restore vehicle stock. The driver completion endpoint rejects an already completed trek.

Sources: `Features/Fleet/GetVehicleStock.cs`, `LoadVehicleStock.cs`, `RemoveVehicleStock.cs`, `GetVehicleStockLedger.cs`; `Features/Trekking/GetTrekStockLoads.cs`, `CheckTrekStockLoads.cs`, `BulkSetTrekStockLoads.cs`, `RemoveTrekStockLoad.cs`, `ChangeTrekStatus.cs`, `CompleteTrekByDriverToken.cs`, `ApproveReturn.cs`.
