# Phase 4 — Vehicle Warehouse / Stock Catalogue

## Goal
Each vehicle is treated as a warehouse. Products are loaded onto the vehicle and tracked across treks. Stock is continuous — leftovers from one trek carry forward automatically. Gives real-time insight into what each vehicle is holding at any point in time.

---

## Resolved design decisions

| Question | Decision |
|---|---|
| Leftovers between treks | Carry forward — vehicle stock is always the physical reality |
| Stock loading vs trek creation | Separate, independently managed features |
| Trek catalogue flexibility | Admin can add/remove products from a trek load at will |
| Over-stock warning | Soft warning only — never a hard block. Separate check endpoint for the frontend to call before the user confirms; then hit the actual load endpoint |
| Minimum stock | Floor at 0 — vehicle warehouse can never go negative |
| Bulk loading | Single endpoint accepts array of `{ productId, basicQty, packagingQty }` |
| Approved returns | Re-add returned quantities to vehicle warehouse |
| Which products to deduct at trek completion | All products that had deliveries (planned or unplanned). However if a product has no `VehicleProductStock` record for that vehicle, skip it — do not create a negative entry. Future: driver catalogue will be seeded from vehicle stock so untracked deliveries won't happen |
| Return approval — vehicle FK | Load `TrekkingTrip.VehicleId` via the existing `TrekkingTripStop → TrekkingTrip` include |

---

## Entities

### `VehicleProductStock` ✅ (confirmed)
Master record of what each vehicle holds right now.

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | |
| `VehicleId` | `Guid` | FK to `Vehicle` |
| `ProductId` | `Guid` | FK to `Product` |
| `BasicQuantityOnHand` | `decimal` | Always >= 0 |
| `PackagingQuantityOnHand` | `decimal` | Always >= 0 |
| `LowStockThreshold` | `decimal?` | Optional alert threshold |
| `CreatedAt` | `DateTime` | |
| `UpdatedAt` | `DateTime?` | |

Unique constraint: `(VehicleId, ProductId)`

Location: `Features/Fleet/Entities/VehicleProductStock.cs`

---

### `TrekStockLoad`
Products earmarked for a specific trek. Flexible — admin can add or remove items at any point before the trek completes. Does **not** deduct from the vehicle warehouse on creation; warehouse is only decremented at trek completion when deliveries are confirmed.

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | |
| `TrekkingTripId` | `Guid` | FK to `TrekkingTrip` |
| `VehicleId` | `Guid` | Denormalised for quick queries |
| `ProductId` | `Guid` | FK to `Product` |
| `BasicQuantityLoaded` | `decimal` | |
| `PackagingQuantityLoaded` | `decimal` | |
| `LoadedByStaffId` | `Guid` | Who added this line |
| `LoadedAt` | `DateTime` | |

Unique constraint: `(TrekkingTripId, ProductId)` — one line per product per trek.

Location: `Features/Trekking/Entities/TrekStockLoad.cs`

---

### `VehicleStockLedger`
Full audit trail (stock cycle) of every change to a vehicle's warehouse — additions, trek-completion deductions, and return-approval credits.

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | |
| `VehicleId` | `Guid` | FK to `Vehicle` |
| `ProductId` | `Guid` | FK to `Product` |
| `ChangeType` | `StockChangeType` (enum) | `Addition` / `Reduction` |
| `Source` | `StockChangeSource` (enum) | `ManualLoad`, `TrekCompletion`, `ReturnApproval` |
| `BasicQtyChange` | `decimal` | Always positive; sign comes from `ChangeType` |
| `PackagingQtyChange` | `decimal` | |
| `BalanceAfter` | `decimal` | Snapshot of `BasicQuantityOnHand` after this change |
| `Reason` | `string` | Auto-generated (see below) |
| `ReferenceId` | `Guid?` | Trek ID or Return ID depending on source |
| `AuthorStaffId` | `Guid?` | Staff who triggered the change (null for system) |
| `RecordedAt` | `DateTime` | |

Location: `Features/Fleet/Entities/VehicleStockLedger.cs`

#### Auto-generated reasons

| Source | Reason format |
|---|---|
| `ManualLoad` | `"Batch stock addition"` |
| `TrekCompletion` | `"Trek {TrekNumber} completed — {qty} units delivered"` |
| `ReturnApproval` | `"Return approved — Invoice {InvoiceNumber}, {ProductName}"` |

---

## Stock flow

```
Manual depot restock
    → VehicleProductStock += added qty
    → VehicleStockLedger (Addition, ManualLoad)

Trek assigned / in progress
    → TrekStockLoad records created (no warehouse change yet)
    → Warning if sum of TrekStockLoad > VehicleProductStock for vehicle

Trek completed
    → VehicleProductStock -= qty delivered per product (floor at 0)
    → VehicleStockLedger (Reduction, TrekCompletion) per product

Return approved
    → VehicleProductStock += qty returned per product
    → VehicleStockLedger (Addition, ReturnApproval)
```

---

## New endpoints

### Vehicle warehouse

| Method | Route | Description |
|---|---|---|
| `GET` | `/api/v1/vehicles/{vehicleId}/stock` | Current stock for a vehicle (list of products + qty) |
| `POST` | `/api/v1/vehicles/{vehicleId}/stock/load` | Bulk add products — accepts `[{ productId, basicQty, packagingQty }]` |
| `GET` | `/api/v1/vehicles/{vehicleId}/stock/ledger` | Paginated stock cycle (all additions + reductions) |

### Trek stock load (trek catalogue)

| Method | Route | Description |
|---|---|---|
| `GET` | `/api/v1/treks/{trekId}/stock-loads` | View all products loaded for a trek |
| `GET` | `/api/v1/treks/{trekId}/stock-loads/check` | Dry-run check — returns list of products where load qty exceeds vehicle stock (for frontend warning modal) |
| `POST` | `/api/v1/treks/{trekId}/stock-loads` | Bulk add/update products for a trek |
| `DELETE` | `/api/v1/treks/{trekId}/stock-loads/{productId}` | Remove a product from the trek load |

---

## Trek completion change (update `ChangeTrekStatus` + `CompleteTrekByDriverToken`)

After the status is set to `Completed` and the transaction commits, for each product across all stops:
1. Sum `BasicQtyDelivered` + `PackagingQtyDelivered` across all stops for that product on this trek
2. Fetch `VehicleProductStock` for `(trip.VehicleId, productId)`
3. Deduct delivered qty — floor at 0
4. Write a `VehicleStockLedger` entry (`Reduction`, `TrekCompletion`)

---

## Return approval change (update `ApproveReturn`)

After setting `ApprovalStatus = Approved`:
1. Fetch `VehicleProductStock` for `(trip.VehicleId, ret.ProductId)`
2. Add `BasicQtyReturned` / `PackagingQtyReturned` back
3. Write a `VehicleStockLedger` entry (`Addition`, `ReturnApproval`)

---

## New enums

```
StockChangeType  — Addition, Reduction
StockChangeSource — ManualLoad, TrekCompletion, ReturnApproval
```

Location: `Features/Fleet/Enums/`

---

## Migration

`AddVehicleWarehouseAndStockLedger` — adds `VehicleProductStocks`, `TrekStockLoads`, `VehicleStockLedger` tables.
