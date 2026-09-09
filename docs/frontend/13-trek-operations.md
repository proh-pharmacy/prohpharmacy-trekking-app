# 13 — Trek Operations

## Overview

Trek operations covers the day-to-day execution of a trekking route — from planning and dispatch through to delivery recording and ledger updates. This is separate from fleet setup (guide 10) and trek configuration (guide 11).

### The full flow

```
Admin creates trek → adds stops → sends email / shares driver link
        ↓
Driver opens link (no login) → sees route → records deliveries stop by stop
        ↓
Admin marks trek Completed → ledger auto-updated per customer
        ↓
Admin downloads delivery sheet PDF (pre-route) or prints per-customer receipt
```

---

## Endpoints

| Method | Endpoint | Auth | Purpose |
|---|---|---|---|
| `POST` | `api/v1/treks` | Required | Create a new trek |
| `PATCH` | `api/v1/treks/{id}` | Required | Update trek details |
| `GET` | `api/v1/treks` | Required | List / search treks |
| `GET` | `api/v1/treks/{id}` | Required | Get a single trek |
| `POST` | `api/v1/treks/{id}/stops` | Required | Add a customer stop |
| `DELETE` | `api/v1/treks/{trekId}/stops/{stopId}` | Required | Remove a stop |
| `PATCH` | `api/v1/treks/{id}/status` | Required | Change trek status |
| `POST` | `api/v1/treks/{id}/generate-link` | Required | Generate driver token URL |
| `POST` | `api/v1/treks/{id}/send-email` | Required | Email sheet + link to staff |
| `POST` | `api/v1/treks/{id}/record` | Required | Admin records delivery results |
| `GET` | `api/v1/treks/{id}/sheet/pdf` | Required | Download delivery sheet PDF |
| `GET` | `api/v1/treks/driver/{token}` | None | Driver views their trek |
| `POST` | `api/v1/treks/driver/{token}/record` | None | Driver records deliveries |

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
  "currentStaffName": "Kwame Asante",
  ...
}
```

---

## POST /api/v1/treks

Creates a trek in `Draft` status. The driver is **automatically inferred** from the vehicle's active staff assignment — do not send a `driverStaffId`. Returns `422` if the vehicle has no active driver assigned.

### Request body

```json
{
  "branchId": "<guid>",
  "scheduledDate": "2026-09-10",
  "vehicleId": "<guid>",
  "notes": "Collect payment for last month's outstanding balance at Stop 3"
}
```

| Field | Required | Notes |
|---|---|---|
| `branchId` | Yes | |
| `scheduledDate` | Yes | `YYYY-MM-DD` |
| `vehicleId` | Yes | Must have an active staff assignment — use `currentStaffId != null` to filter |
| `notes` | No | Max 500 chars |

### Response `201 Created`

```json
{
  "id": "...",
  "trekNumber": "TRK-00001",
  "branchId": "...",
  "branchName": "Tema Branch",
  "driverStaffId": "...",
  "driverName": "Kwame Asante",
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
- `404` — branch or vehicle not found
- `422` — validation error, or vehicle has no active driver assigned

---

## PATCH /api/v1/treks/{id}

Updates the branch, scheduled date, vehicle, and notes. The driver is re-inferred from the new vehicle's active staff assignment — same rule as create. Blocked if the trek is `Completed` or `Cancelled`.

### Request body

```json
{
  "branchId": "<guid>",
  "scheduledDate": "2026-09-11",
  "vehicleId": "<guid>",
  "notes": "Updated route — skip Stop 2 if closed"
}
```

| Field | Required | Notes |
|---|---|---|
| `branchId` | Yes | |
| `scheduledDate` | Yes | `YYYY-MM-DD` |
| `vehicleId` | Yes | Must have an active staff assignment |
| `notes` | No | Max 500 chars. Send `null` to clear |

### Response `200 OK` — full `TrekResponse` with updated `driverName` reflected

### Errors
- `404` — trek, branch, or vehicle not found
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
    { "productId": "<guid>", "plannedQuantity": 10 },
    { "productId": "<guid>", "plannedQuantity": 5 }
  ]
}
```

| Field | Required | Notes |
|---|---|---|
| `customerAccountId` | Yes | |
| `sequence` | Yes | Order of the stop on the route (must be > 0) |
| `products` | Yes | At least one product |
| `products[].productId` | Yes | |
| `products[].plannedQuantity` | Yes | Must be > 0 |
| `notes` | No | Max 500 chars |

### Response `201 Created`

```json
{
  "stopId": "...",
  "sequence": 1,
  "customerAccountId": "...",
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
      "productId": "...",
      "productName": "Paracetamol 500mg",
      "unit": "Box",
      "plannedQuantity": 10,
      "qtyDelivered": null,
      "paymentMethod": null,
      "amtPaid": null,
      "balance": null,
      "notes": null,
      "deliveredAt": null
    }
  ]
}
```

> `stopProductId` is the ID to pass when recording delivery results — use it directly in the `POST /record` body.

### Errors
- `404` — trek, customer, or product not found
- `422` — validation error

---

## DELETE /api/v1/treks/{trekId}/stops/{stopId}

Removes a stop from the trek.

### Response `204 No Content`

### Errors
- `404` — stop not found on this trek

---

## PATCH /api/v1/treks/{id}/status

Changes the trek status. No restrictions on transitions — the frontend controls the allowed flow.

### Request body

```json
{ "status": "InProgress" }
```

### Response `200 OK` — full `TrekResponse`

### Errors
- `404` — trek not found
- `422` — invalid status value

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

Sends the trekking sheet PDF + driver link to one or more staff members by email. Generates the driver token automatically if not yet created.

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
| Right column | Branch, driver, vehicle, primary contact name and phone |
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
  "branchName": "Tema Branch",
  "status": "InProgress",
  "isLocked": false,
  "stops": [
    {
      "stopId": "...",
      "sequence": 1,
      "customerName": "Tema Central Pharmacy",
      "customerCode": "GAR-00001",
      "primaryPhoneNumber": "+233244123456",
      "location": "Opposite the blue mosque, Community 5, Tema",
      "products": [
        {
          "stopProductId": "...",
          "productName": "Paracetamol 500mg",
          "unit": "Box",
          "plannedQuantity": 10,
          "qtyDelivered": null,
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

### POST /api/v1/treks/driver/{token}/record

Driver submits delivery results for one or more products. Can be called multiple times — each call updates the specified products and re-syncs the customer ledger entries for affected stops.

### Request body

```json
{
  "products": [
    {
      "stopProductId": "...",
      "qtyDelivered": 8,
      "paymentMethod": "Cash",
      "amtPaid": 240.00,
      "balance": 60.00,
      "notes": "Short delivery — 2 boxes damaged"
    }
  ]
}
```

| Field | Notes |
|---|---|
| `stopProductId` | From `products[].stopProductId` in the driver view |
| `qtyDelivered` | Actual quantity delivered (can differ from planned) |
| `paymentMethod` | `Cash` `MobileMoney` `Credit` `Cheque` `BankTransfer` |
| `amtPaid` | Amount collected at the door |
| `balance` | Remaining amount owed — auto-creates a Debit ledger entry |
| `notes` | Optional per-product note |

### Response `200 OK`

```json
{
  "trekId": "...",
  "trekNumber": "TRK-00001",
  "status": "InProgress",
  "recorded": 1
}
```

> The record response is intentionally slim. After a successful submission, re-fetch the trek (`GET /api/v1/treks/driver/{token}` or `GET /api/v1/treks/{id}`) to get the updated product values and repopulate your form.

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
      "qtyDelivered": 8,
      "paymentMethod": "Cash",
      "amtPaid": 240.00,
      "balance": 60.00,
      "notes": "Short delivery — 2 boxes damaged"
    }
  ]
}
```

> After submitting, re-fetch `GET /api/v1/treks/{id}` to get the updated product values back.

---

## How the Ledger Auto-Updates

Every time delivery results are recorded (by driver or admin), the backend automatically:

1. **Deletes** any existing auto-generated ledger entries for the affected stops
2. **Re-creates** them from the current totals:
   - `amtPaid > 0` → **Credit** entry: `"Payment received — Trek TRK-00001"`
   - `balance > 0` → **Debit** entry: `"Outstanding balance — Trek TRK-00001"`

This means recording is idempotent — re-submitting corrected figures replaces the old ledger entries cleanly.

---

## GET /api/v1/treks

### Query parameters

| Parameter | Type | Description |
|---|---|---|
| `search` | `string` | Trek number or driver name |
| `sort` | `string` | e.g. `scheduledDate_desc` |
| `pageNumber` | `int` | Default: 1 |
| `pageSize` | `int` | Default: 20 |
| `branchId` | `guid` | Filter by branch |
| `status` | `string` | e.g. `InProgress`, `Completed` |
| `scheduledDate` | `DateOnly` | e.g. `2026-09-10` |

### Response `200 OK` — `PaginatedData<TrekResponse>`
