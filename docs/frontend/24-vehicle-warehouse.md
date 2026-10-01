# 24 — Vehicle Warehouse and Trek Stock Loads

Verified against the implementation on 2026-10-01. Vehicle stock persists between treks. A trek stock load is a separate allocation record: saving or removing it does not change warehouse quantities. Delivery recording also leaves warehouse quantities unchanged; completion performs deductions.

Add a **Stock** tab on vehicle details with current balances, bulk load/removal forms and stock history. Add a **Stock loads** tab to trek details for allocations and quantity warnings. All endpoints in this guide require a staff Bearer JWT; there are no driver-token stock-management equivalents.

## Vehicle endpoints

| Method | Route | Success |
|---|---|---|
| GET | `/api/v1/vehicles/{vehicleId}/stock` | `200`, `StockItem[]`, sorted by product name |
| POST | `/api/v1/vehicles/{vehicleId}/stock/load` | `200`, mutation response |
| POST | `/api/v1/vehicles/{vehicleId}/stock/remove` | `200`, same mutation shape |
| GET | `/api/v1/vehicles/{vehicleId}/stock/ledger` | `200`, array or pagination envelope |

GET routes return HTTP `404` for an unknown vehicle. POST handler failures return HTTP `422` (including missing vehicle, with body `code: "404"`). See [error conventions](./00-api-conventions.md).

### Current stock

```json
[
  {
    "stockId": "<stock-guid>",
    "productId": "<product-guid>",
    "productName": "Paracetamol 500mg",
    "basicQuantityOnHand": 100,
    "packagingQuantityOnHand": 2,
    "lowStockThreshold": null,
    "isLowStock": false,
    "updatedAt": null
  }
]
```

`isLowStock` is true when a threshold exists and basic stock ≤ threshold. There is no endpoint here to configure the threshold. `updatedAt` can be null for newly created records. Basic and packaging quantities are tracked separately; do not convert or merge them automatically.

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

Both mutations return this shape; `updatedStock` contains the affected products only, using the complete stock-item shape above:

```json
{
  "productsUpdated": 1,
  "updatedStock": [
    {
      "stockId": "<stock-guid>",
      "productId": "<product-guid>",
      "productName": "Paracetamol 500mg",
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

Query parameters: optional `productId`, `source`, `pageNumber`, `pageSize`. Sources: `ManualLoad`, `TrekCompletion`, `ReturnApproval` (case-insensitive filter). Sort is fixed to `recordedAt_desc`; no search, date or sort query is exposed. Send `pageNumber=1&pageSize=20` for the [pagination envelope](./00-api-conventions.md); omitting `pageSize` returns an array. There is no enforced page-size cap in the query builder.

Each row has:

```json
{
  "id": "<ledger-guid>",
  "productId": "<product-guid>",
  "productName": "Paracetamol 500mg",
  "changeType": "Reduction",
  "source": "ManualLoad",
  "basicQtyChange": 5,
  "packagingQtyChange": 0,
  "balanceAfter": 95,
  "reason": "Damaged goods",
  "authorName": "Ama Owusu",
  "recordedAt": "2026-10-01T11:00:00Z"
}
```

`changeType` is `Addition` or `Reduction`; quantity changes are nonnegative magnitudes. Manual removals also use `source: "ManualLoad"`—use `changeType` for the sign. `balanceAfter` is the basic-stock snapshot only. `authorName` is nullable. The entity's `referenceId` is not exposed in this DTO.

## Trek stock-load endpoints

| Method | Route | Success |
|---|---|---|
| GET | `/api/v1/treks/{trekId}/stock-loads` | `200`, all load rows |
| GET | `/api/v1/treks/{trekId}/stock-loads/check` | `200`, warnings; **requires JSON body** |
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
    "basicQuantityLoaded": 120,
    "packagingQuantityLoaded": 2,
    "vehicleBasicOnHand": 95,
    "exceedsVehicleStock": true,
    "loadedBy": "Ama Owusu",
    "loadedAt": "2026-10-01T11:30:00Z"
  }
]
```

Missing warehouse records produce `vehicleBasicOnHand: null` and `exceedsVehicleStock: false`. That means untracked stock, not confirmed availability.

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
      "requestedQty": 120,
      "availableQty": 95,
      "shortfall": 25
    }
  ]
}
```

No warnings yields `{ "hasWarnings": false, "warnings": [] }`. The handler compares each requested basic quantity against current vehicle basic stock. It ignores packaging quantities, products without stock records, and allocations on other treks. Warnings never block saving.

Browser Fetch cannot send a GET body. The backend needs a POST check route (not currently implemented) for direct use of this endpoint in a browser. For the current frontend, fetch vehicle stock and compare the draft basic quantities locally, display the same warning/confirmation, then POST the `items` object to `/stock-loads`. Keep the missing-stock state visible separately. Refresh after saving because stock may have changed since the check.

## Completion and return effects

Completion deducts delivered quantities for tracked products from the trek's vehicle, floors balances at zero and appends `Reduction` / `TrekCompletion` ledger rows. Both planned and unplanned products are considered, but the current code only includes stop-product rows where `basicQtyDelivered > 0`; packaging-only deliveries are skipped. Products without vehicle stock records are skipped too. Trek load amounts do not cap deductions.

Approved returns add basic and packaging quantities to an existing stock record on the **capturing trek's vehicle**, with `Addition` / `ReturnApproval` history. No record is created if the product is untracked. Pending/rejected returns have no stock effect. Leftovers persist for future treks.

After completion or approval, refresh stock, stock ledger, trek allocations and the [driver report](./25-driver-trek-report.md). Treat staff completion as a single action: the status handler currently allows repeated completion and deducts again. Reopening/cancelling does not restore vehicle stock. The driver completion endpoint rejects an already completed trek.

Sources: `Features/Fleet/GetVehicleStock.cs`, `LoadVehicleStock.cs`, `RemoveVehicleStock.cs`, `GetVehicleStockLedger.cs`; `Features/Trekking/GetTrekStockLoads.cs`, `CheckTrekStockLoads.cs`, `BulkSetTrekStockLoads.cs`, `RemoveTrekStockLoad.cs`, `ChangeTrekStatus.cs`, `CompleteTrekByDriverToken.cs`, `ApproveReturn.cs`.
