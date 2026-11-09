# 13 — Trek Operations

> Updated for the invoice-first backend on 2026-11-08. See [Invoice First & Payments](./27-invoice-first-payments.md) for the payment and receipt contracts.


Related feature guides: [Sale Invoices](./22-sale-invoices.md), [Invoice Returns & Approval](./23-invoice-returns.md), [Vehicle Warehouse & Trek Stock Loads](./24-vehicle-warehouse.md), and [Driver Trek Report](./25-driver-trek-report.md). Completion now deducts tracked warehouse stock and notifies the creator about pending returns. Prevent repeat staff completion; it can deduct stock again.

Stop responses additionally expose `invoiceId`, `invoiceNumber`, `invoiceStatus`,
`invoiceTotal`, `invoicePaid`, and `invoiceBalance`, including before delivery.
Use these for stop-level payment summaries; product-level `amtPaid`, `balance`, and
`paymentMethod` are legacy fields. See [the stop summary contract](./27-invoice-first-payments.md#read-invoices).

## Overview

Trek operations covers the day-to-day execution of a trekking route — from planning and dispatch through to delivery recording and ledger updates. This is separate from fleet setup (guide 10) and trek configuration (guide 11).

### The full flow

```
Admin creates trek (Draft) → freely adds/changes/removes planned stops
        ↓
Trek transitions to Scheduled → invoices issued, ledger charged, driver link emailed
        ↓
Driver opens link (no login) → calls start endpoint OR makes any delivery action → trek becomes InProgress
        ↓
Driver edits goods / records deliveries → existing invoice totals update
        ↓
Driver records payment separately → receipt issued and ledger credited
        ↓
Admin/driver completes trek → stock reconciliation; existing ledger entries retained
        ↓
View/print invoices and their separate payment receipts
```

Only Draft treks can be deleted. Scheduling issues invoices and permanently closes the Draft editing boundary.

---

## Endpoints

| Method | Endpoint | Auth | Purpose |
|---|---|---|---|
| `POST` | `api/v1/treks` | Required | Create a new trek |
| `PATCH` | `api/v1/treks/{id}` | Required | Update trek details |
| `GET` | `api/v1/treks` | Required | List / search treks |
| `GET` | `api/v1/treks/{id}` | Required | Get a single trek |
| `POST` | `api/v1/treks/{id}/stops` | Required | Add a customer stop |
| `PATCH` | `api/v1/treks/{trekId}/stops/{stopId}` | Required | Update stop sequence, notes, or products |
| `DELETE` | `api/v1/treks/{trekId}/stops/{stopId}` | Required | Remove a stop |
| `PATCH` | `api/v1/treks/{id}/status` | Required | Change trek status (Scheduled auto-emails driver) |
| `DELETE` | `api/v1/treks/{id}` | Required | Delete a Draft trek |
| `POST` | `api/v1/treks/{id}/generate-link` | Required | Generate driver token URL |
| `POST` | `api/v1/treks/{id}/send-email` | Required | Resend sheet + link to staff |
| `POST` | `api/v1/treks/{id}/record` | Required | Admin records delivery results |
| `PATCH` | `api/v1/treks/{trekId}/stops/{stopId}/products/{stopProductId}/price` | Required | Override snapshotted price on a stop product |
| `POST` | `api/v1/treks/{trekId}/sync-prices` | Required | Re-sync all stop product prices from the current catalog |
| `GET` | `api/v1/treks/{trekId}/price-diff` | Required | Get price differences between trek and current catalog |
| `GET` | `api/v1/treks/{trekId}/report` | Required | Admin trek financial report (JSON) |
| `GET` | `api/v1/treks/{trekId}/report/pdf` | Required | Admin trek financial report (PDF) |
| `GET` | `api/v1/treks/{trekId}/stock-snapshot` | Required | Admin stock reconciliation snapshot (JSON, Completed treks only) |
| `GET` | `api/v1/treks/{trekId}/stock-snapshot/pdf` | Required | Admin stock reconciliation snapshot (PDF) |
| `GET` | `api/v1/treks/driver/{token}/stock-snapshot` | None | Driver stock reconciliation snapshot (JSON) |
| `GET` | `api/v1/treks/driver/{token}/stock-snapshot/pdf` | None | Driver stock reconciliation snapshot (PDF) |
| `GET` | `api/v1/treks/{id}/sheet/pdf` | Required | Download delivery sheet PDF |
| `GET` | `api/v1/treks/driver/{token}` | None | Driver views their trek |
| `POST` | `api/v1/treks/driver/{token}/start` | None | Driver starts the trek (Scheduled → InProgress) |
| `GET` | `api/v1/treks/driver/{token}/sheet/pdf` | None | Driver downloads delivery sheet PDF |
| `POST` | `api/v1/treks/driver/{token}/record` | None | Driver records deliveries |
| `POST` | `api/v1/treks/{trekId}/stops/{stopId}/returns` | Required | Admin records a product return at a stop |
| `POST` | `api/v1/treks/driver/{token}/stops/{stopId}/returns` | None | Driver records a product return |
| `DELETE` | `api/v1/treks/driver/{token}/stops/{stopId}/returns/{returnId}` | None | Driver voids a return |

---

## Enum Reference

### `status`
`Draft` `Scheduled` `InProgress` `Completed` `Cancelled`

### `paymentMethod`
`Cash` `MobileMoney` `Credit` `Cheque` `BankTransfer`

---

## Picking a vehicle for a new trek

Before creating a trek, fetch the full vehicle list to let the admin pick one. Vehicle branch assignment is optional, so do not filter by branch — show all vehicles.

```
GET /api/v1/fleet/vehicles
```

The vehicle response already includes the assigned driver via `currentStaffId` and `currentStaffName`. Only vehicles where `currentStaffId` is not `null` are eligible for trek creation — grey out or hide unassigned vehicles in the picker. The backend enforces this too and will return `422` if you try to create a trek with an unassigned vehicle.

```json
{
  "id": "...",
  "displayName": "Sprinter Van 1",
  "registrationNumber": "GR-1234-24",
  "currentStaffId": "...",
  "currentStaffName": "Kwame Asante"
}
```

---

## POST /api/v1/treks

Creates a trek in `Draft` status. The driver is **automatically inferred** from the vehicle's active staff assignment — do not send a `driverStaffId`. Returns `422` if the vehicle has no active driver assigned.

### Request body

```json
{
  "regionId": "<guid>",
  "branchId": "<guid>",
  "scheduledDate": "2026-09-10",
  "vehicleId": "<guid>",
  "salesStaffId": "<guid>",
  "notes": "Collect payment for last month's outstanding balance at Stop 3"
}
```

| Field | Required | Notes |
|---|---|---|
| `regionId` | Yes | The region this trek covers |
| `branchId` | No | Optional branch association |
| `scheduledDate` | Yes | `YYYY-MM-DD` |
| `vehicleId` | Yes | Must have an active staff assignment — use `currentStaffId != null` to filter |
| `salesStaffId` | No | Optional sales/records staff accompanying the driver |
| `notes` | No | Max 500 chars |

### Picking a vehicle

Fetch `GET /api/v1/fleet/vehicles?regionId={regionId}` after the region is selected to show vehicles from that region. The backend does not enforce that the vehicle belongs to the selected region — any vehicle with an active driver is accepted.

### Response `201 Created`

```json
{
  "id": "...",
  "trekNumber": "TRK-00001",
  "regionId": "...",
  "regionName": "Greater Accra Region",
  "branchId": null,
  "branchName": null,
  "driverStaffId": "...",
  "driverName": "Kwame Asante",
  "salesStaffId": null,
  "salesStaffName": null,
  "vehicleId": "...",
  "vehicleDisplayName": "Sprinter Van 1",
  "scheduledDate": "2026-09-10",
  "status": "Draft",
  "notes": null,
  "createdAt": "2026-09-10T07:00:00Z",
  "updatedAt": null,
  "stops": []
}
```

### Errors
- `404` — region, branch (if provided), or vehicle not found
- `422` — validation error, or vehicle has no active driver assigned

---

## PATCH /api/v1/treks/{id}

Updates the region, branch, scheduled date, vehicle, and notes. The driver is re-inferred from the new vehicle's active staff assignment — same rule as create. Blocked if the trek is `Completed` or `Cancelled`.

### Request body

```json
{
  "regionId": "<guid>",
  "branchId": "<guid>",
  "scheduledDate": "2026-09-11",
  "vehicleId": "<guid>",
  "salesStaffId": "<guid>",
  "notes": "Updated route — skip Stop 2 if closed"
}
```

| Field | Required | Notes |
|---|---|---|
| `regionId` | Yes | |
| `branchId` | No | Send `null` to clear |
| `scheduledDate` | Yes | `YYYY-MM-DD` |
| `vehicleId` | Yes | Must have an active staff assignment |
| `salesStaffId` | No | Send `null` to remove the sales staff from the trek |
| `notes` | No | Max 500 chars. Send `null` to clear |

### Response `200 OK` — full `TrekResponse` with updated `regionName` and `driverName` reflected

### Errors
- `404` — trek, region, branch (if provided), or vehicle not found
- `422` — vehicle has no active driver, or trek is `Completed` / `Cancelled`

---

## POST /api/v1/treks/{trekId}/stops

Adds a customer stop to the trek with the products to be delivered.

### Request body

```json
{
  "customerAccountId": "<guid>",
  "sequence": 1,
  "notes": "Call ahead before arriving",
  "products": [
    { "productId": "<guid>", "plannedBasicQuantity": 10, "plannedPackagingQuantity": 2 },
    { "productId": "<guid>", "plannedBasicQuantity": 5 }
  ]
}
```

| Field | Required | Notes |
|---|---|---|
| `customerAccountId` | Yes | |
| `sequence` | Yes | Order of the stop on the route (must be > 0) |
| `products` | Yes | At least one product |
| `products[].productId` | Yes | |
| `products[].plannedBasicQuantity` | No | >= 0 when provided |
| `products[].plannedPackagingQuantity` | No | >= 0; ignored if the product has no packaging unit |
| `notes` | No | Max 500 chars |

At least one of `plannedBasicQuantity` or `plannedPackagingQuantity` must be > 0 per product.

### Response `201 Created`

```json
{
  "stopId": "...",
  "sequence": 1,
  "customerAccountId": "...",
  "customerClientGeneratedId": null,
  "customerName": "Tema Central Pharmacy",
  "customerCode": "GAR-00001",
  "customerPhone": "+233244123456",
  "customerType": "RetailPharmacy",
  "regionName": "Greater Accra Region",
  "districtName": "Tema",
  "primaryLocationLandmark": "Opposite the blue mosque",
  "primaryLocationStreet": "Community 5, Tema",
  "primaryContactName": "Ama Boateng",
  "primaryContactPhone": "+233209876543",
  "notes": null,
  "products": [
    {
      "stopProductId": "...",
      "clientGeneratedId": null,
      "productId": "...",
      "productName": "Paracetamol 500mg",
      "basicUnitName": "Tab",
      "packagingUnitName": "Box",
      "basicUnitPrice": 2.50,
      "packagingUnitPrice": 60.00,
      "plannedBasicQuantity": 10,
      "plannedPackagingQuantity": 2,
      "basicQtyDelivered": null,
      "packagingQtyDelivered": null,
      "amountDue": 145.00,
      "paymentMethod": null,
      "amtPaid": null,
      "balance": null,
      "notes": null,
      "deliveredAt": null
    }
  ]
}
```

> `stopProductId` is the ID to pass when recording delivery results — use it directly in the `POST /record` body. `clientGeneratedId` is the device-generated UUID originally submitted with an offline `RecordUnplannedSale`; it is `null` for products created from the admin panel and non-null for sales reconciled from an offline queue. Use it to match local records whose `serverId` was lost after acknowledgement.

### Errors
- `404` — trek, customer, or product not found
- `422` — validation error

---

## PATCH /api/v1/treks/{trekId}/stops/{stopId}

Updates a stop's customer, sequence, notes, or product list. Customer replacement is
allowed only while the trek is Draft; scheduled and later stops already belong to
their issued-invoice customer. All fields are optional — send only what you want to change.

### Request body

```json
{
  "customerAccountId": "<customer-guid>",
  "sequence": 2,
  "notes": "Updated delivery notes",
  "products": [
    {
      "productId": "<product-guid>",
      "plannedBasicQuantity": 10,
      "plannedPackagingQuantity": 2
    }
  ]
}
```

| Field | Required | Constraints |
|---|---|---|
| `customerAccountId` | No | Changing the customer of an invoiced stop is rejected |
| `sequence` | No | > 0 |
| `notes` | No | Max 500 chars |
| `products` | No | If provided, must be non-empty; at least one qty > 0 per product; replaces the entire product list |
| `products[].productId` | Yes (if products provided) | Must exist |
| `products[].plannedBasicQuantity` | No | >= 0 when provided |
| `products[].plannedPackagingQuantity` | No | >= 0 when provided |

**Invoice constraints:** replacing the product list cannot delete recorded delivery lines or lower the invoice total below payments already received. Customer reassignment is rejected once an invoice exists.

### Response `200 OK` — same `TrekStopResponse` shape as `POST /api/v1/treks/{trekId}/stops`

### Errors
- `404` — trek, stop, or customer not found
- `422` — validation error or product not found

---

## DELETE /api/v1/treks/{trekId}/stops/{stopId}

Stops can be permanently deleted only while the trek is Draft. Scheduled and later
stops have issued invoice history and deletion returns `422`.

Removes a stop from the trek.

### Response `204 No Content`

### Errors
- `404` — stop not found on this trek

---

## PATCH /api/v1/treks/{id}/status

Changes the trek status. Invoice rules prevent reopening a voided invoice and cancelling a trek with payments or recorded deliveries. Show validation messages and refresh the trek after changes.

### Request body

```json
{ "status": "InProgress" }
```

**When transitioning to `Scheduled`:** the backend requires at least one customer stop, issues every stop invoice and ledger charge transactionally, generates the driver token (if not already set), and emails the trek sheet PDF + driver link to the assigned Driver and SalesStaff (if present). Refetch the trek after success to obtain invoice IDs and totals.

### Response `200 OK` — full `TrekResponse`

### Errors
- `404` — trek not found
- `422` — invalid status value

---

## DELETE /api/v1/treks/{id}

Permanently deletes a trek. Only allowed while it is `Draft`. Once scheduled, its issued invoices prevent deletion.

Cascades to all child records — stops, stop products, and returns are all removed.

### Response `204 No Content`

### Errors
- `404` — trek not found
- `422` — trek is not `Draft`

> **Frontend guidance:** show trek and stop deletion only in `Draft`. Draft customer replacement is allowed; after scheduling it is rejected because the invoice belongs to the original customer.

---

## POST /api/v1/treks/{id}/generate-link

Generates a permanent token-based URL for the driver. The token is stable — calling this multiple times returns the same token.

### Response `200 OK`

```json
{
  "token": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "url": "https://trekking.prohpharmacy.com/treks/driver?token=3fa85f64-..."
}
```

> The `url` is the full frontend URL the driver opens on their phone. Share it via WhatsApp, SMS, or email.

### Errors
- `404` — trek not found

---

## POST /api/v1/treks/{id}/send-email

Resends the trekking sheet PDF + driver link to one or more staff members by email. The email is sent automatically to the Driver and SalesStaff when the trek transitions to `Scheduled` — use this endpoint only for manual resends (e.g., the driver lost their link). Generates the driver token automatically if not yet set.

### Request body

```json
{
  "staffIds": ["<guid>", "<guid>"]
}
```

### Response `200 OK`

```json
{
  "trekNumber": "TRK-00001",
  "sent": 2,
  "recipients": [
    "Kwame Asante <kwame@prohpharmacy.com>",
    "Ama Boateng <ama@prohpharmacy.com>"
  ]
}
```

### Errors
- `404` — trek not found
- `422` — no staff IDs provided

---

## GET /api/v1/treks/{id}/sheet/pdf

Downloads the delivery sheet as a PDF — the pre-route manifest the driver carries. Trigger this as a file download or open in a new tab.

```js
window.open(`/api/v1/treks/${trekId}/sheet/pdf`, '_blank')
```

### Response
`application/pdf` file download named `TrekkingSheet-TRK-00001-2026-09-10.pdf`

### What the PDF contains per stop

Each stop card on the sheet includes:

| Section | Fields printed |
|---|---|
| Header | Stop sequence, customer name, customer code |
| Left column | Scheduled date, phone number, street address, landmark & directions, district / region |
| Right column | Region, driver, vehicle, sales staff (if assigned), primary contact name and phone |
| Products table | Product name, unit, planned quantity (with a blank "Delivered" column for the driver to fill in by hand) |

### Errors
- `404` — trek not found

---

## Driver Portal (no authentication)

The driver portal is a no-auth frontend page at `/treks/driver?token={token}`. The token from `generate-link` acts as the access key.

### GET /api/v1/treks/driver/{token}

Fetches the full trek for the driver view.

### Response `200 OK`

```json
{
  "trekId": "...",
  "trekNumber": "TRK-00001",
  "scheduledDate": "2026-09-10",
  "driverName": "Kwame Asante",
  "vehicleDisplayName": "Sprinter Van 1",
  "regionName": "Greater Accra Region",
  "salesStaffId": null,
  "salesStaffName": null,
  "status": "InProgress",
  "isLocked": false,
  "stops": [
    {
      "stopId": "...",
      "sequence": 1,
      "customerAccountId": "...",
      "customerClientGeneratedId": null,
      "customerName": "Tema Central Pharmacy",
      "customerCode": "GAR-00001",
      "primaryPhoneNumber": "+233244123456",
      "customerType": "Retail",
      "regionName": "Greater Accra",
      "districtName": "Tema Municipal",
      "primaryLocationLandmark": "Opposite the blue mosque",
      "primaryLocationStreet": "Community 5, Tema",
      "primaryContactName": "Ama Owusu",
      "primaryContactPhone": "+233244987654",
      "notes": null,
      "products": [
        {
          "stopProductId": "...",
          "clientGeneratedId": null,
          "productName": "Paracetamol 500mg",
          "basicUnitName": "Tab",
          "packagingUnitName": "Box",
          "basicUnitPrice": 2.50,
          "packagingUnitPrice": 60.00,
          "plannedBasicQuantity": 10,
          "plannedPackagingQuantity": 2,
          "basicQtyDelivered": null,
          "packagingQtyDelivered": null,
          "paymentMethod": null,
          "amtPaid": null,
          "balance": null,
          "notes": null,
          "deliveredAt": null
        }
      ]
    }
  ]
}
```

> `isLocked: true` when the trek is `Completed` — show a read-only view, hide the submit button.

### Errors
- `404` — invalid token

---

### POST /api/v1/treks/driver/{token}/start

Starts the trek — transitions status from `Scheduled` to `InProgress`. Call this when the driver taps "Start Trek" on their portal.

- **Idempotent:** returns `204 No Content` if the trek is already `InProgress` — safe to call on app launch.
- Returns `422` if the trek is `Completed` or `Cancelled`.

**Auto-start:** delivery recording, adding a walk-in stop, adding an unplanned sale and offline sync start a scheduled trek as a side effect. The new invoice-return POST does not. Call this endpoint for an explicit "start" button before deliveries or returns are recorded.

### Response `204 No Content`

### Errors
- `404` — invalid token
- `422` — trek is `Completed` or `Cancelled`

---

### GET /api/v1/treks/driver/{token}/sheet/pdf

Downloads the delivery sheet PDF using the driver token. No authentication required.

Returns a `application/pdf` file attachment named `TrekkingSheet-{trekNumber}-{scheduledDate}.pdf`. The PDF includes all stops with any delivery data already recorded (quantities, payment, balance).

### Errors
- `404` — invalid token

---

### POST /api/v1/treks/driver/{token}/record

Driver submits delivery results for one or more products. Can be called multiple times — each call updates the specified products and their existing invoices and ledger charges.

### Request body

```json
{
  "products": [
    {
      "stopProductId": "...",
      "basicQtyDelivered": 8,
      "packagingQtyDelivered": 1,
      "notes": "Short delivery — 2 boxes damaged"
    }
  ]
}
```

| Field | Notes |
|---|---|
| `stopProductId` | From `products[].stopProductId` in the driver view |
| `basicQtyDelivered` | Actual basic units delivered (e.g. tablets) |
| `packagingQtyDelivered` | Actual packages delivered (e.g. boxes) — only meaningful when the product has a packaging unit |
| `notes` | Optional per-product note |

### Delivery and payment are separate

Save quantities and notes with `/record`, then refresh the invoice. At least one
quantity is required per product; quantities must be non-negative. Supply explicit
zeros for both quantities when cancelling supply of a planned line. Omitted lines
remain on the invoice. Delivery never implies payment.

Use the invoice payment endpoint for money collected and display the returned
receipt. Do not send `amtPaid`, `paymentMethod` or `balance` with delivery or
unplanned-product requests; non-null values, including zero, are rejected.
See [the payment request and receipt contract](./27-invoice-first-payments.md#record-a-payment).

`amountDue` is a calculated billable line total. Current paid/outstanding totals
come from the invoice, not the legacy line payment fields.

### Response `200 OK`

```json
{
  "trekId": "...",
  "trekNumber": "TRK-00001",
  "status": "InProgress",
  "recorded": 1,
  "invoices": [
    { "stopId": "<stop-guid>", "invoiceId": "<invoice-guid>", "invoiceNumber": "INV-GAR00001" }
  ]
}
```

> Persist the returned `invoices` mapping, then re-fetch the trek (`GET /api/v1/treks/driver/{token}` or `GET /api/v1/treks/{id}`) to refresh product values. Omit deprecated `stopInvoices`; planned invoice identity is established during scheduling and active walk-ins receive it during creation. See [sale invoices](./22-sale-invoices.md).

### Errors
- `404` — invalid token
- `422` — trek is already `Completed`

---

## POST /api/v1/treks/{id}/record (admin)

Same shape and behaviour as the driver endpoint but requires authentication. Use this when an admin needs to enter or correct delivery data from the office.

### Request body

```json
{
  "products": [
    {
      "stopProductId": "...",
      "basicQtyDelivered": 8,
      "packagingQtyDelivered": 1,
      "notes": "Short delivery — 2 boxes damaged"
    }
  ]
}
```

> After submitting, re-fetch `GET /api/v1/treks/{id}` to get the updated product values back.

---

## How the Ledger Auto-Updates

Scheduling creates each planned invoice and customer ledger debit. Active walk-in
invoices create theirs immediately; later invoice changes update the debit. Each
payment creates a separate credit. Completion does not rebuild ledger
entries. Return approval creates its own credit without changing payment receipts.

Cancelling an unpaid, undelivered trek voids its invoices and removes their invoice
charges. A paid or delivered trek cannot be cancelled. Manual adjustments and
return credits are preserved. See [ledger](./14-ledger.md).

---

## GET /api/v1/treks

### Query parameters

| Parameter | Type | Description |
|---|---|---|
| `search` | `string` | Trek number or driver name |
| `sort` | `string` | e.g. `scheduledDate_desc` |
| `pageNumber` | `int` | Default: 1 |
| `pageSize` | `int` | Default: 20 |
| `regionId` | `guid` | Filter by region |
| `branchId` | `guid` | Filter by branch |
| `status` | `string` | e.g. `InProgress`, `Completed` |
| `scheduledDate` | `DateOnly` | e.g. `2026-09-10` |

### Response `200 OK` — `PaginatedData<TrekResponse>`

---

## PATCH /api/v1/treks/{trekId}/stops/{stopId}/products/{stopProductId}/price

Corrects the snapshotted `basicUnitPrice` and/or `packagingUnitPrice` on a specific stop product — use this when a price was wrong at the time the stop was added. `amountDue` is recalculated: delivered quantities × new prices when delivery has been recorded, otherwise planned quantities × new prices. Not allowed once the trek is `Completed`.

### Request body

```json
{
  "basicUnitPrice": 3.00,
  "packagingUnitPrice": 72.00
}
```

| Field | Required | Notes |
|---|---|---|
| `basicUnitPrice` | Yes | Corrected price for the basic unit |
| `packagingUnitPrice` | No | Corrected price for the packaging unit. Send `null` to clear |

### Response `200 OK` — full stop product object with updated prices and recalculated `amountDue`

### Errors
- `404` — trek, stop, or stop product not found
- `422` — trek is already `Completed`

---

## POST /api/v1/treks/{trekId}/sync-prices

Re-snapshots all stop product prices from the current product catalog in one call. Use this when product prices have changed since the trek was created and you want the trek to reflect the updated pricing.

Allowed on `Draft`, `Scheduled`, and `InProgress` treks. Blocked on `Completed` and `Cancelled`.

### What it does per stop product

| Scenario | Action |
|---|---|
| Basic price changed | Updates `basicUnitPrice` |
| Packaging price changed | Updates `packagingUnitPrice` |
| Packaging unit removed from catalog | Clears `packagingUnitPrice`, `plannedPackagingQuantity`, and `packagingQtyDelivered` |
| Packaging unit added to catalog | Sets `packagingUnitPrice` from catalog (quantities remain `null`) |
| Nothing changed | No update |

`amountDue` is recalculated for every stop product that was changed.

### Response `200 OK`

```json
{
  "trekId": "...",
  "trekNumber": "TRK-00001",
  "productsUpdated": 4,
  "packagingAdded": 1,
  "packagingRemoved": 1,
  "changes": [
    "Paracetamol 500mg: basic price 2.50 → 3.00",
    "Amoxicillin 250mg: packaging price 60.00 → 72.00",
    "ORS Sachets: packaging unit removed — clearing packaging price and quantities",
    "Vitamin C 1000mg: packaging unit added at 36.00"
  ]
}
```

| Field | Description |
|---|---|
| `productsUpdated` | Number of stop products where at least one value changed |
| `packagingAdded` | Number of stop products that gained a packaging unit |
| `packagingRemoved` | Number of stop products that lost a packaging unit |
| `changes` | Human-readable description of every change made |

If nothing has changed since the last sync, `productsUpdated` will be `0` and `changes` will be empty — the response is still `200 OK`.

### Errors
- `404` — trek not found
- `422` — trek is `Completed` or `Cancelled`

---

## GET /api/v1/treks/{trekId}/price-diff

Returns a breakdown of every stop product whose snapshotted price or packaging configuration is out of date with the current catalog. Use this to show the user exactly what will change before they confirm a sync.

Only products with at least one difference are included — products that are up to date are omitted.

### Response `200 OK`

```json
{
  "trekId": "...",
  "trekNumber": "TRK-00001",
  "syncRequired": true,
  "differences": [
    {
      "stopId": "...",
      "stopSequence": 1,
      "customerName": "Accra Pharmacy Ltd",
      "stopProductId": "...",
      "productName": "Paracetamol 500mg",
      "snapshotBasicUnitPrice": 2.50,
      "catalogBasicUnitPrice": 3.00,
      "basicPriceChanged": true,
      "snapshotPackagingUnitPrice": 60.00,
      "catalogPackagingUnitPrice": 72.00,
      "packagingPriceChanged": true,
      "packagingAdded": false,
      "packagingRemoved": false
    },
    {
      "stopId": "...",
      "stopSequence": 2,
      "customerName": "Tema Central Pharmacy",
      "stopProductId": "...",
      "productName": "ORS Sachets",
      "snapshotBasicUnitPrice": 5.00,
      "catalogBasicUnitPrice": 5.00,
      "basicPriceChanged": false,
      "snapshotPackagingUnitPrice": 95.00,
      "catalogPackagingUnitPrice": null,
      "packagingPriceChanged": false,
      "packagingAdded": false,
      "packagingRemoved": true
    }
  ]
}
```

| Field | Description |
|---|---|
| `syncRequired` | `true` when at least one difference exists |
| `differences` | Only products with a difference — empty array means everything is up to date |
| `basicPriceChanged` | Snapshotted basic price differs from catalog |
| `packagingPriceChanged` | Both snapshot and catalog have packaging but prices differ |
| `packagingAdded` | Catalog now has a packaging unit the stop product didn't have |
| `packagingRemoved` | Catalog no longer has a packaging unit the stop product had |

> **Frontend guidance:** call this endpoint when `syncRequired: true` is returned from `GET /api/v1/treks/{id}`. Show the diff to the user in a confirmation dialog before calling `POST /sync-prices`.

### `syncRequired` on the trek response

`GET /api/v1/treks/{id}` now includes a `syncRequired` field. Use it to dynamically show or hide the sync button on the trek detail page without needing to call the diff endpoint upfront.

```json
{
  "id": "...",
  "trekNumber": "TRK-00001",
  "syncRequired": true
}
```

### Errors
- `404` — trek not found

---

## Recording Product Returns

Returns now require an invoice and are created as `Pending`. Use the [Invoice Returns & Approval guide](./23-invoice-returns.md) for the full request/response contract, list filters and review actions.

### POST /api/v1/treks/{trekId}/stops/{stopId}/returns (staff JWT)

### POST /api/v1/treks/driver/{token}/stops/{stopId}/returns (driver token)

Request (the optional `gps` object is driver-only):

```json
{
  "saleInvoiceId": "<invoice-guid>",
  "productId": "<product-guid>",
  "basicQtyReturned": 4,
  "packagingQtyReturned": 1,
  "refundMethod": "Cash",
  "reason": "Expired stock",
  "gps": { "latitude": 5.6037, "longitude": -0.187, "accuracyMetres": 12.5 }
}
```

The server calculates the refund from the invoice line's basic and packaging prices. Do not send an editable refund amount, prices or `clientGeneratedId`. Basic quantity must be > 0; packaging quantity must be > 0 if supplied; reason is optional, maximum 500 characters. This flow is online-only and does not support idempotent retries.

Response `201 Created`:

```json
{
  "returnId": "<return-guid>",
  "invoiceNumber": "INV-GAR00001",
  "productId": "<product-guid>",
  "productName": "Paracetamol 500mg",
  "basicQtyReturned": 4,
  "packagingQtyReturned": 1,
  "basicUnitPrice": 2.5,
  "packagingUnitPrice": 60,
  "refundAmount": 70,
  "refundMethod": "Cash",
  "reason": "Expired stock",
  "approvalStatus": "Pending",
  "recordedAt": "2026-10-01T10:30:00Z"
}
```

Handler failures use HTTP `422`, including missing invoices/stops and closed treks. Driver recording additionally checks the invoice customer belongs to the trek region. The token-only portal can read invoice details at `GET /api/v1/treks/driver/{token}/invoices/{invoiceId}` within its region. Recording a return does not auto-start the trek.

Completion notifies the trek creator of pending returns. Only approval creates a customer ledger credit and restores tracked vehicle stock; rejection has no financial or stock effect.

---

### DELETE /api/v1/treks/driver/{token}/stops/{stopId}/returns/{returnId} (driver)

Voids (permanently deletes) a return record from the driver portal. Blocked on `Completed` and `Cancelled` treks. There is no admin equivalent: admin review happens after the recording trek is `Completed`, and the review workflow uses `/approve` or `/reject` — not deletion. The handler does not check approval status or reverse ledger/stock effects, so drivers should only use this on their own pending captures before the trek is closed.

### Response `204 No Content`

### Errors
- `404` — return, stop, or trek not found
- `422` — trek is `Completed` or `Cancelled`

---

### Returns on the trek response

Returns appear inside each stop under the `returns` array when fetching `GET /api/v1/treks/{id}` or `GET /api/v1/treks/driver/{token}`. Each return now carries its **own** `saleInvoiceId` + `invoiceNumber` — this is the historical invoice the return was recorded against, not the stop's current-trek invoice. Approval fields are not embedded; use the dedicated staff return lists in [guide 23](./23-invoice-returns.md) for review state:

```json
{
  "stopId": "...",
  "customerName": "Tema Central Pharmacy",
  "invoiceId": "<current-trek-invoice-guid>",
  "invoiceNumber": "INV-GAR00099",
  "products": [],
  "returns": [
    {
      "returnId": "...",
      "saleInvoiceId": "<historical-invoice-guid>",
      "invoiceNumber": "INV-GAR00042",
      "productId": "...",
      "productName": "Paracetamol 500mg",
      "basicUnitName": "Tab",
      "packagingUnitName": "Box",
      "basicQtyReturned": 4,
      "packagingQtyReturned": 1,
      "basicUnitPrice": 2.50,
      "packagingUnitPrice": 60.00,
      "refundAmount": 70.00,
      "refundMethod": "Cash",
      "reason": "Expired stock",
      "recordedAt": "2026-09-19T10:30:00Z"
    }
  ]
}
```

> **Important:** when a driver records a return against a historical invoice (an invoice from a previous trek), the return's `invoiceNumber` is that historical one — **not** the stop-level `invoiceNumber` which refers to the current trek's invoice. Always render `returns[i].invoiceNumber`, never fall back to `stop.invoiceNumber` for return rows.

Returns also appear on the PDF delivery sheet with product, quantity returned, refund amount, refund method, reason and approval status. Rejected rows are muted. The separate [driver financial report](./25-driver-trek-report.md) includes approved refunds in its totals.

---

## GET /api/v1/treks/{trekId}/stock-snapshot

Returns the **vehicle stock reconciliation snapshot** captured at the moment the trek was completed. The snapshot is written once by the server inside the `complete trek` transaction, is **never updated**, and persists even after subsequent treks mutate the vehicle's live warehouse stock. Use this endpoint to show "what was in the van for this trek, what was sold, what should still be on the van after completion" on the trek details page and in audit/reconciliation views.

Rows are included for every product that was **on the vehicle at trek start** (any `VehicleProductStock` row with a non-zero quantity), **loaded for the trek** (`TrekStockLoad`), or **sold during the trek** (any stop product with `basicQtyDelivered` or `packagingQtyDelivered`). An untouched product that was already on the vehicle shows `basicQtySold = 0` and the full starting qty as `basicQtyRemaining` — useful for proving "this stock was on the van and came back intact". A product that was sold without having been on the van shows a negative remaining — surfacing a reconciliation discrepancy rather than hiding it.

The snapshot is **only available for `Completed` treks**. Calling this endpoint on a `Draft`, `Scheduled`, `InProgress`, or `Cancelled` trek returns `400 Bad Request` with `"Stock snapshot is only available for completed treks."`. Hide the download/open controls on the trek details page until `status === 'Completed'`.

### Response `200 OK`

```json
{
  "trekId": "<trek-guid>",
  "trekNumber": "T-2026-00041",
  "trekDate": "2026-10-03",
  "trekStatus": "Completed",
  "capturedAt": "2026-10-03T15:42:11Z",
  "items": [
    {
      "productId": "<product-guid>",
      "productName": "Paracetamol 500mg",
      "basicUnitName": "Tab",
      "packagingUnitName": "Box",
      "basicUnitPrice": 2.50,
      "packagingUnitPrice": 60.00,
      "basicQtyAtStart": 100,
      "packagingQtyAtStart": 4,
      "basicQtySold": 35,
      "packagingQtySold": 1,
      "basicQtyRemaining": 65,
      "packagingQtyRemaining": 3,
      "revenueAmount": 147.50,
      "hasDiscrepancy": false
    }
  ],
  "totals": {
    "productCount": 1,
    "totalRevenue": 147.50,
    "discrepancyCount": 0
  }
}
```

**Fields**

- `capturedAt` — UTC timestamp of the snapshot. Always non-null since this endpoint only returns `200` for completed treks.
- `basicQtyAtStart` / `packagingQtyAtStart` — the vehicle's warehouse quantity for this product **immediately before** the completion deduction ran. This is the trek-start baseline.
- `basicQtySold` / `packagingQtySold` — total delivered quantities across all stops on this trek.
- `basicQtyRemaining` / `packagingQtyRemaining` — `start − sold`. **Not clamped** — a negative value means more was sold than was on the vehicle (a counting error or a mid-trek load that didn't go through the system).
- `revenueAmount` — money attributable to this product on this trek (`AmountDue`, or `AmtPaid + Balance` if `AmountDue` is null).
- `hasDiscrepancy` — `true` if either remaining value is negative. Flag the row in the UI.
- Unit names and prices are **snapshot values** captured at completion — later product renames or price changes do not alter history.
- `totals.discrepancyCount` — number of rows with `hasDiscrepancy = true`; useful for a header badge.

Items are ordered alphabetically by product name.

The snapshot is **independent** of the live `/api/v1/vehicles/{vehicleId}/stock` state. If the van's catalogue is reset after this trek, the snapshot is still intact and still reflects what the van held during this trek.

### Errors
- `400` — trek is not `Completed`
- `404` — trek not found

---

## GET /api/v1/treks/{trekId}/stock-snapshot/pdf (admin)

Downloads the stock reconciliation snapshot as a landscape A4 PDF. Same payload as the JSON endpoint, same `Completed`-only guard. Open in a new tab or trigger a download.

```js
window.open(`/api/v1/treks/${trekId}/stock-snapshot/pdf`, '_blank')
```

Filename: `StockSnapshot-{trekNumber}-{trekDate}.pdf`.

### Errors
- `400` — trek is not `Completed`
- `404` — trek not found

---

## GET /api/v1/treks/driver/{token}/stock-snapshot (driver portal)

Driver-token twin of the admin stock snapshot endpoint. Same response shape, same `Completed`-only guard. No authentication required — the token is the access key.

Use this to let drivers download/view the reconciliation record for a completed trek from the no-auth portal.

### Errors
- `400` — trek is not `Completed`
- `404` — invalid token

---

## GET /api/v1/treks/driver/{token}/stock-snapshot/pdf (driver portal)

PDF variant of the driver snapshot endpoint. Returns the same landscape A4 PDF as the admin endpoint. Filename: `StockSnapshot-{trekNumber}-{trekDate}.pdf`.

### Errors
- `400` — trek is not `Completed`
- `404` — invalid token

---

## GET /api/v1/treks/{trekId}/report (admin)

Staff-authenticated twin of the [driver financial report](./25-driver-trek-report.md). Returns the exact same `TrekReportData` shape — sales value, collections, outstanding balances, approved refunds, net cash on hand, per-stop breakdowns and stock reconciliation. Available at any trek status.

Use this on the admin trek details page so staff can view the same financial roll-up the driver sees in their portal.

See [guide 25 — Driver Trek Report](./25-driver-trek-report.md) for the full response schema.

### Errors
- `404` — trek not found

---

## GET /api/v1/treks/{trekId}/report/pdf (admin)

PDF variant of the admin financial report — same layout as the driver portal PDF. Filename: `TrekReport-{trekNumber}-{scheduledDate}.pdf`.

```js
window.open(`/api/v1/treks/${trekId}/report/pdf`, '_blank')
```

### Errors
- `404` — trek not found

---

## Three downloadable reports per trek

Each completed trek has three independent reports available from both the admin trek details page and the driver portal. Design the trek details screen with a single "Downloads" section that lists them:

| Report | Admin endpoint | Driver endpoint | Availability |
|---|---|---|---|
| Delivery sheet | `GET /api/v1/treks/{trekId}/sheet/pdf` | `GET /api/v1/treks/driver/{token}/sheet/pdf` | Any status |
| Financial report | `GET /api/v1/treks/{trekId}/report` + `/pdf` | `GET /api/v1/treks/driver/{token}/report` + `/pdf` (see [guide 25](./25-driver-trek-report.md)) | Any status |
| Stock reconciliation snapshot | `GET /api/v1/treks/{trekId}/stock-snapshot` + `/pdf` | `GET /api/v1/treks/driver/{token}/stock-snapshot` + `/pdf` | `Completed` only (400 otherwise) |

Hide or disable the snapshot entries until the trek is `Completed`. The delivery sheet and financial report are safe to expose at any status.
