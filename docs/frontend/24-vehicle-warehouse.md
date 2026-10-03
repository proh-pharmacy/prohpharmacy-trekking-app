# 24 — Vehicle Warehouse and Trek Stock Loads

Verified against the implementation on 2026-10-01. Vehicle stock persists between treks. A trek stock load is a separate allocation record: saving or removing it does not change warehouse quantities. Delivery recording also leaves warehouse quantities unchanged; completion performs deductions.

Add a **Stock** tab on vehicle details with current balances, bulk load/removal forms and stock history. Add a **Stock loads** tab to trek details for allocations and quantity warnings. All endpoints in this guide require a staff Bearer JWT; there are no driver-token stock-management equivalents.

For a searchable, paginated product picker limited to a vehicle, use `GET /api/v1/products?vehicleId={vehicleId}&inStockOnly=true`. It returns full product catalogue fields plus `vehicleStock`; see [Products](./08-products.md#get-apiv1products). Omitting `inStockOnly` includes tracked zero-balance products. For the add-product picker, use `excludeVehicleStock=true` instead so already-catalogued products never occupy a page.

## Vehicle endpoints

| Method | Route | Success |
|---|---|---|
| GET | `/api/v1/fleet/vehicles/stock-overview` | `200`, paginated vehicle list with per-vehicle stock summary |
| GET | `/api/v1/vehicles/{vehicleId}/stock` | `200`, `StockItem[]`, newest stock records first |
| GET | `/api/v1/vehicles/{vehicleId}/stock/summary` | `200`, tracked/in-stock/out-of-stock product counts |
| GET | `/api/v1/vehicles/{vehicleId}/stock/{productId}` | `200`, one current `StockItem` |
| POST | `/api/v1/vehicles/{vehicleId}/stock/load` | `200`, mutation response |
| POST | `/api/v1/vehicles/{vehicleId}/stock/remove` | `200`, same mutation shape |
| POST | `/api/v1/vehicles/{vehicleId}/stock/reset` | `200`, reset summary; refuses while active treks exist |
| GET | `/api/v1/vehicles/{vehicleId}/stock/export` | `200`, current-stock `.xlsx` download |
| GET | `/api/v1/vehicles/{vehicleId}/stock/ledger` | `200`, array or pagination envelope |
| GET | `/api/v1/vehicles/{vehicleId}/stock/ledger/export` | `200`, filtered `.xlsx` download |

GET routes return HTTP `404` for an unknown vehicle. POST handler failures return HTTP `422` (including missing vehicle, with body `code: "404"`). See [error conventions](./00-api-conventions.md).

### Fleet stock overview (vehicle stock management page)

```
GET /api/v1/fleet/vehicles/stock-overview
    ?regionId=<guid>
    &branchId=<guid>
    &status=Active
    &stockState=loaded
    &search=GR-
    &sort=createdAt_desc
    &pageNumber=1
    &pageSize=20
```

Paginated vehicle list enriched with each vehicle's stock summary. Intended for a dedicated **vehicle stock management** page: one row per vehicle with its loaded-product counts, no extra per-vehicle calls needed.

Query parameters:

- `regionId`, `branchId` — vehicle organisation filters.
- `status` — vehicle operational status: `Active`, `UnderMaintenance`, `Decommissioned` (case-insensitive). Invalid values return `400`.
- `stockState` — `loaded` to show only vehicles that have at least one product with a non-zero quantity; `empty` to show vehicles with no stock rows or all rows at zero. Omit to show all. Invalid values return `400`.
- `search` — matches `registrationNumber`, `displayName`, `make`, `model` (case-insensitive).
- `sort` — any vehicle field with `_asc`/`_desc`. Default `createdAt_desc`. Stock summary fields are not sortable in this version.
- `pageNumber`, `pageSize` — standard pagination.

Response `200 OK` — standard `PaginatedData<T>` envelope:

```json
{
  "data": [
    {
      "id": "<vehicle-guid>",
      "registrationNumber": "GR-1234-24",
      "displayName": "Van 7",
      "make": "Toyota",
      "model": "HiAce",
      "year": 2022,
      "colour": "White",
      "regionId": "<region-guid>",
      "regionName": "Greater Accra",
      "branchId": "<branch-guid>",
      "branchName": "Accra Central",
      "operationalStatus": "Active",
      "currentStaffId": "<staff-guid>",
      "currentStaffName": "Kwame Asante",
      "createdAt": "2026-01-10T09:00:00Z",
      "updatedAt": null,
      "stock": {
        "trackedProductCount": 24,
        "inStockProductCount": 18,
        "outOfStockProductCount": 6,
        "lowStockProductCount": 3,
        "hasStockLoaded": true,
        "lastUpdatedAt": "2026-10-02T14:12:03Z"
      }
    }
  ],
  "pageNumber": 1,
  "pageSize": 20,
  "totalItems": 42,
  "totalPages": 3
}
```

Notes:

- A product counts as **out of stock** only when **both** `basicQuantityOnHand == 0` and `packagingQuantityOnHand == 0` — same rule as `/stock/summary`.
- `lowStockProductCount` counts tracked products whose `lowStockThreshold` is set and whose `basicQuantityOnHand <= lowStockThreshold`.
- `hasStockLoaded` is `true` whenever `inStockProductCount > 0`. Use this for a simple "Loaded / Empty" badge on each row.
- `lastUpdatedAt` is the most recent `updatedAt` (falling back to `createdAt`) across the vehicle's stock rows, or `null` if the vehicle has no tracked products.
- `currentStaffId` / `currentStaffName` reflect the currently active staff assignment (where `unassignedAt` is null); `null` when unassigned.
- A vehicle with no tracked products at all returns a zeroed `stock` object with `lastUpdatedAt: null`.

Drill-down for a row stays on the existing per-vehicle endpoints (`/stock`, `/stock/summary`, `/stock/{productId}`, `/stock/load`, `/stock/remove`, `/stock/reset`, `/stock/export`, `/stock/ledger`).

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
| GET | `/api/v1/treks/driver/{token}/vehicle-stock` | `200`, `StockItem[]` — the trek vehicle's current warehouse stock; no staff JWT |
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
      "packagingShortfall": 2,
      "notInVehicleCatalogue": false
    }
  ]
}
```

No warnings yields `{ "hasWarnings": false, "warnings": [] }`. The handler compares basic and packaging quantities independently against the matching vehicle balances and returns their unit IDs/names. A warning is returned when either unit is short; a unit with sufficient stock has a shortfall of zero. Warnings never block saving.

#### Products outside the vehicle catalogue

If a submitted product does **not** have a `VehicleProductStock` row for the trek's vehicle, it is still returned as a warning — with `notInVehicleCatalogue: true`, `availableBasicQty: 0`, `availablePackagingQty: 0`, and shortfalls equal to the full requested quantities. Product name and unit info come from the global products catalogue. If the product ID doesn't exist in the global catalogue at all, it is silently skipped.

Render these warnings with a distinct label (e.g. "Not in vehicle catalogue") and require the user to add them to the vehicle before confirming the load. Warnings with `notInVehicleCatalogue: false` are regular shortfall warnings against a product the vehicle already stocks.

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

### Driver vehicle-stock list (offline-cached read)

```http
GET /api/v1/treks/driver/{token}/vehicle-stock
```

Anonymous driver-token GET. Resolves the token → trek → `VehicleId`, then returns the trek vehicle's `VehicleProductStocks` using the **same `StockItem` shape as the staff route `GET /api/v1/vehicles/{vehicleId}/stock`** (product metadata, basic/packaging units and prices, `basicQuantityOnHand`, `packagingQuantityOnHand`, `isLowStock`, `updatedAt`, etc.). Rows are ordered by stock-record `createdAt` descending. Invalid token returns `404`.

This endpoint does **not** read the per-trek `TrekStockLoads` allocation table. The driver sees exactly what's physically tracked on their assigned van — the same data the admin maintains via `POST /vehicles/{id}/stock/load`, `POST /vehicles/{id}/stock/remove` and `POST /vehicles/{id}/stock/reset`.

The driver portal is expected to cache the response in IndexedDB keyed by the trek/token at pickup, then read from the cache while offline so the driver can browse what's in the van without a network call. The `basicQuantityOnHand` / `packagingQuantityOnHand` values are the **server-side balance at cache time** — treat as stale during offline operation. Authoritative decrements happen on trek completion server-side; refresh the cache once the device is back online. The driver side pushes no stock mutations against this endpoint — the trek-level offline queue (`SyncOfflineActionsByDriverToken`) still handles deliveries, unplanned sales and returns.

## Stock snapshot export

```http
GET /api/v1/vehicles/{vehicleId}/stock/export?productId={productId}&includeOutOfStock=true
```

Both query parameters are optional. `productId` restricts the sheet to a single tracked product. `includeOutOfStock` defaults to `true` — set `false` to hide rows whose basic and packaging balances are both zero. The downloaded `.xlsx` includes the vehicle, generation timestamp (GMT), product name, combined readable on-hand (e.g. `2 Boxes, 15 Tablets`), basic and packaging unit prices, low-stock threshold, last-updated timestamp and a derived status column (`In stock`, `Low stock`, `Out of stock`). Low-stock rows render in red; out-of-stock rows render muted. Unknown vehicle returns `404`.

## Full stock reset

```http
POST /api/v1/vehicles/{vehicleId}/stock/reset
```

```json
{ "reason": "End-of-month van reconciliation" }
```

Removes every `VehicleProductStock` row for the vehicle after writing one `Reduction` / `StockReset` ledger entry per product capturing the pre-reset `basicQuantityOnHand` and `packagingQuantityOnHand` (balances after = 0). Historical ledger rows are untouched — the ledger has no foreign key to the stock table, so audit history survives the delete and still reports via `/stock/ledger` and `/stock/ledger/export`. After reset the vehicle shows no tracked products until the admin re-loads via `POST /stock/load`.

**Preconditions**
- `reason` is required and max 300 characters.
- The vehicle must have at least one stock record (otherwise `422` with `"This vehicle has no stock records to reset."`).
- The vehicle **must not** have any `Scheduled` or `InProgress` trek. Attempting the reset while active treks exist returns `422` with a message naming the blocking trek numbers and statuses — e.g. `"Cannot reset stock — vehicle has active trek(s): TRK-00009 (InProgress), TRK-00011 (Scheduled). Complete or cancel them first."` Call `/api/v1/treks/{id}/change-status` to complete or cancel them first.

**Side-effect to be aware of.** A `Pending` return captured before the reset and approved afterwards credits returned quantities onto the vehicle's existing stock record. After a reset there is no stock record, so the stock credit is **silently skipped** (customer ledger credit still happens). Only reset when all pending returns on the vehicle's recently completed treks have been reviewed.

Response `200`:

```json
{
  "productsRemoved": 7,
  "ledgerEntriesCreated": 7,
  "resetAt": "2026-10-02T15:30:00Z"
}
```

The response does not list product IDs — pull the historical breakdown from `/stock/ledger?source=StockReset` or the stock-ledger export.

## Completion and return effects

Completion deducts delivered quantities for tracked products from the trek's vehicle, floors balances at zero and appends `Reduction` / `TrekCompletion` ledger rows. Both planned and unplanned products are considered, but the current code only includes stop-product rows where `basicQtyDelivered > 0`; packaging-only deliveries are skipped. Products without vehicle stock records are skipped too. Trek load amounts do not cap deductions.

Approved returns add basic and packaging quantities to an existing stock record on the **capturing trek's vehicle**, with `Addition` / `ReturnApproval` history. No record is created if the product is untracked. Pending/rejected returns have no stock effect. Leftovers persist for future treks.

After completion or approval, refresh stock, stock ledger, trek allocations and the [driver report](./25-driver-trek-report.md). Treat staff completion as a single action: the status handler currently allows repeated completion and deducts again. Reopening/cancelling does not restore vehicle stock. The driver completion endpoint rejects an already completed trek.

Sources: `Features/Fleet/GetVehicleStock.cs`, `LoadVehicleStock.cs`, `RemoveVehicleStock.cs`, `ResetVehicleStock.cs`, `ExportVehicleStock.cs`, `GetVehicleStockLedger.cs`, `ExportVehicleStockLedger.cs`; `Features/Trekking/GetTrekStockLoads.cs`, `GetVehicleStockByDriverToken.cs`, `CheckTrekStockLoads.cs`, `BulkSetTrekStockLoads.cs`, `RemoveTrekStockLoad.cs`, `ChangeTrekStatus.cs`, `CompleteTrekByDriverToken.cs`, `ApproveReturn.cs`.
