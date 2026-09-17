# Trek Driver Control Panel — Feature Plan

> Status: **Planning — not yet implemented**
> Refine this document until complete, then begin coding.

---

## Overview

The driver token (currently used only to view a single trek sheet) is elevated into a full **field control panel**. A driver or sales rep in the field opens their unique link — no login required — and gets a complete operational interface covering their assigned trek and all other active treks in their region.

Because routes often pass through areas with no connectivity, the control panel must work **fully offline** with a sync mechanism to push queued actions when connectivity returns.

---

## The Token

The driver token (`DriverToken` on `TrekkingTrip`) is a UUID generated when the admin sends the trek assignment email. It remains the sole credential for all driver portal operations — no JWT, no login.

**From the token the backend can always derive:**

| Value | Source |
|---|---|
| Trek | `TrekkingTrips WHERE DriverToken = token` |
| Region | `trek.RegionId` |
| Attribution | `trek.SalesStaffId ?? trek.DriverStaffId` (sales rep first, driver second, both nullable) |

All actions recorded through the token are attributed using `salesStaffId ?? driverStaffId`. If both are null the trek ID and region still provide full context — the action is never unattributed.

---

## Capabilities

### 1. Control Panel View
Driver sees:
- Their assigned trek (stops, products, delivery status)
- All other active treks in the same region

### 2. New Customer Registration
Register a brand-new customer from the field. Full registration — business name, phone, location, contact person. Uses the existing `ClientGeneratedId` pattern for offline idempotency.

### 3. Walk-in Stops (Impromptu Stops)
Add an unplanned stop to any active trek in the region. The customer can be:
- Newly registered in the same session
- An existing customer already in the system

Stop is automatically flagged `IsWalkIn = true` when added to an `InProgress` trek.

### 4. Unplanned Product Sales
Sell products at a stop that were not in the original plan. Product and delivery are recorded in one action (the physical transaction is happening in real time). Flagged `IsUnplanned = true` on the stop product.

### 5. Product Returns
Record a product return from a customer at any stop — planned or walk-in. The returned product does not have to be from the current trek; a customer may be returning something from a previous delivery.

Return generates a **ledger debit entry** for the customer when the trek is marked `Completed` (mirrors how deliveries generate credit entries).

---

## Data Model Changes

### Modified: `TrekkingTripStop`

| Field | Type | Notes |
|---|---|---|
| `IsWalkIn` | `bool` | Default `false`. Auto-set `true` when stop added to an `InProgress` trek |

### Modified: `TrekkingTripStopProduct`

| Field | Type | Notes |
|---|---|---|
| `IsUnplanned` | `bool` | Default `false`. `true` for mid-trek additions outside the original plan |

### New Entity: `TrekkingTripStopReturn`

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `TrekkingTripStopId` | `Guid` | FK → `TrekkingTripStop` (cascade delete) |
| `ProductId` | `Guid` | FK → `Product` (restrict) |
| `BasicQtyReturned` | `decimal(10,3)` | Required |
| `PackagingQtyReturned` | `decimal(10,3)?` | Null if product has no packaging unit |
| `BasicUnitPrice` | `decimal(14,2)` | Snapshotted at return time |
| `PackagingUnitPrice` | `decimal(14,2)?` | Snapshotted at return time |
| `RefundAmount` | `decimal(14,2)?` | Actual amount refunded |
| `RefundMethod` | `PaymentMethod?` | Same enum as deliveries |
| `Reason` | `string?` | Max 500 chars |
| `RecordedByStaffId` | `Guid?` | `salesStaffId ?? driverStaffId` at time of recording |
| `ClientGeneratedId` | `Guid?` | Offline idempotency key — unique, nullable |
| `Latitude` | `decimal(9,6)?` | GPS latitude at time of return — optional |
| `Longitude` | `decimal(9,6)?` | GPS longitude at time of return — optional |
| `GpsAccuracyMetres` | `decimal(8,2)?` | Device-reported accuracy — optional |
| `RecordedAt` | `DateTime` | UTC |

### GPS Notes

GPS is captured via the device's hardware chip — works with **no mobile data, no WiFi, no SIM**. The frontend calls `navigator.geolocation.getCurrentPosition()` at the time of each action.

All three GPS fields (`latitude`, `longitude`, `gpsAccuracyMetres`) are **optional everywhere** — if the device fails to get a fix, or the user denies location permission, the action still goes through without coordinates. Never block an action on GPS failure.

GPS is relevant on three action types:
- **`RegisterCustomer`** → stored as the customer's primary location (same `Latitude`/`Longitude`/`AccuracyMetres` fields already on `CustomerLocation`)
- **`AddWalkInStop`** → stored on the stop for reference (where in the field the stop was created)
- **`RecordReturn`** → stored on the return record

Not captured on `RecordUnplannedSale` — the stop already has a location.

---

## Endpoints

All driver portal endpoints are **unauthenticated** — protected only by the token UUID.

### Control Panel

| Method | Endpoint | Purpose |
|---|---|---|
| `GET` | `api/v1/treks/driver/{token}` | Driver's assigned trek (existing — may expand response) |
| `GET` | `api/v1/treks/driver/{token}/region/treks` | All active treks in the driver's region |

### Offline Seed & Delta Sync (Download)

| Method | Endpoint | Purpose |
|---|---|---|
| `GET` | `api/v1/treks/driver/{token}/offline/products` | Full product catalogue. Supports `?since=` for delta |
| `GET` | `api/v1/treks/driver/{token}/offline/customers` | All customers in the region. Supports `?since=` for delta |
| `GET` | `api/v1/treks/driver/{token}/offline/trek` | Full trek + stops + products + returns. Supports `?since=` |

### Customer Registration

| Method | Endpoint | Purpose |
|---|---|---|
| `POST` | `api/v1/treks/driver/{token}/customers` | Register a new customer from the field |

### Walk-in Stops

| Method | Endpoint | Purpose |
|---|---|---|
| `POST` | `api/v1/treks/driver/{token}/treks/{trekId}/stops` | Add a walk-in stop to any active trek in the region |

### Unplanned Product Sales

| Method | Endpoint | Purpose |
|---|---|---|
| `POST` | `api/v1/treks/driver/{token}/stops/{stopId}/products/unplanned` | Add + record an unplanned sale at a stop |

### Product Returns

| Method | Endpoint | Purpose |
|---|---|---|
| `POST` | `api/v1/treks/driver/{token}/stops/{stopId}/returns` | Record a product return at a stop |
| `DELETE` | `api/v1/treks/driver/{token}/stops/{stopId}/returns/{returnId}` | Void a return |

### Offline Batch Sync (Push)

| Method | Endpoint | Purpose |
|---|---|---|
| `POST` | `api/v1/treks/driver/{token}/sync` | Push all queued offline actions in one batch |

---

### Admin-side mirrors (all require `RequireAuthorization()`)

| Method | Endpoint | Purpose |
|---|---|---|
| `POST` | `api/v1/treks/{trekId}/stops/{stopId}/products/unplanned` | Admin adds unplanned product to a stop |
| `POST` | `api/v1/treks/{trekId}/stops/{stopId}/returns` | Admin records a return |
| `DELETE` | `api/v1/treks/{trekId}/stops/{stopId}/returns/{returnId}` | Admin voids a return |

---

## Offline Sync — Batch Push

**`POST /api/v1/treks/driver/{token}/sync`**

Driver submits all queued actions in the order they occurred on the device.

### Request

```json
{
  "actions": [
    {
      "type": "RegisterCustomer",
      "clientId": "<device-generated-uuid>",
      "occurredAt": "2026-09-17T10:32:00Z",
      "payload": {
        "businessName": "Koforidua Pharmacy",
        "primaryPhoneNumber": "0244123456",
        "customerType": "RetailPharmacy",
        "regionId": "...",
        "gps": {
          "latitude": 6.0835,
          "longitude": -0.2170,
          "accuracyMetres": 12.5
        }
      }
    },
    {
      "type": "AddWalkInStop",
      "clientId": "<device-generated-uuid>",
      "occurredAt": "2026-09-17T10:35:00Z",
      "payload": {
        "trekId": "...",
        "customerClientId": "<clientId-of-offline-customer>",
        "sequence": 5,
        "notes": "Met on the main road",
        "gps": {
          "latitude": 6.0835,
          "longitude": -0.2170,
          "accuracyMetres": 12.5
        }
      }
    },
    {
      "type": "RecordUnplannedSale",
      "clientId": "<device-generated-uuid>",
      "occurredAt": "2026-09-17T10:40:00Z",
      "payload": {
        "stopClientId": "<clientId-of-offline-stop>",
        "productId": "...",
        "basicQtyDelivered": 10,
        "paymentMethod": "Cash",
        "amtPaid": 250.00,
        "balance": 0.00
      }
    },
    {
      "type": "RecordReturn",
      "clientId": "<device-generated-uuid>",
      "occurredAt": "2026-09-17T10:45:00Z",
      "payload": {
        "stopId": "<existing-server-stop-id>",
        "productId": "...",
        "basicQtyReturned": 2,
        "refundAmount": 50.00,
        "refundMethod": "Cash",
        "reason": "Damaged packaging",
        "gps": {
          "latitude": 6.0835,
          "longitude": -0.2170,
          "accuracyMetres": 18.0
        }
      }
    }
  ]
}
```

### Response

```json
{
  "results": [
    { "clientId": "...", "type": "RegisterCustomer", "status": "Created", "serverId": "..." },
    { "clientId": "...", "type": "AddWalkInStop",    "status": "Created", "serverId": "..." },
    { "clientId": "...", "type": "RecordUnplannedSale", "status": "Created", "serverId": "..." },
    { "clientId": "...", "type": "RecordReturn",     "status": "Created", "serverId": "..." }
  ]
}
```

### Action statuses

| Status | Meaning |
|---|---|
| `Created` | Action processed and saved |
| `AlreadySynced` | `clientId` already exists — returned existing `serverId`, no duplicate created |
| `Conflict` | Could not apply — e.g. trek already `Completed`, phone already registered |

### Processing rules

1. Actions processed in `occurredAt` order
2. **Dependency resolution** — if an `AddWalkInStop` references a `customerClientId` from an earlier `RegisterCustomer` in the same batch, the server resolves the client ID to the real server ID before creating the stop
3. **Idempotency** — every `clientId` is stored; re-submitting the same batch is safe
4. **Conflict strategy:**
   - Customer phone already exists → return existing customer as `AlreadySynced`
   - Trek already `Completed` → return `Conflict` with reason
   - Stop `clientId` already exists → return existing stop as `AlreadySynced`

---

## Attribution Rule

On every action processed through the driver token:

```
recordedByStaffId = trek.SalesStaffId ?? trek.DriverStaffId
```

Sales rep takes precedence. If both are null the action is still valid — trek ID and region provide full context.

---

## Impact on Existing Responses

| Area | Change |
|---|---|
| `TrekStopResponse` | Add `isWalkIn` bool, add `returns[]` array |
| `TrekStopProductResponse` | Add `isUnplanned` bool |
| `GetTrek` / `GetTrekByDriverToken` | Load and include returns per stop |
| Trek completion handler | Returns → ledger debit entries per customer (`salesStaffId ?? driverStaffId`) |
| PDF generator | Returns section per stop (product, qty, refund amount) |
| `AddTrekStop` handler | Remove `Draft`-only guard; auto-set `IsWalkIn = true` when trek is `InProgress` |

---

## New Files (when ready to code)

```
Features/Trekking/
  Entities/TrekkingTripStopReturn.cs

  GetDriverControlPanel.cs                  — GET driver/{token}  (replaces / extends existing)
  GetRegionTreksByDriverToken.cs            — GET driver/{token}/region/treks
  GetOfflineProductsByDriverToken.cs        — GET driver/{token}/offline/products
  GetOfflineCustomersByDriverToken.cs       — GET driver/{token}/offline/customers
  GetOfflineTrekByDriverToken.cs            — GET driver/{token}/offline/trek
  CreateCustomerByDriverToken.cs            — POST driver/{token}/customers
  AddWalkInStopByDriverToken.cs             — POST driver/{token}/treks/{trekId}/stops
  AddUnplannedStopProduct.cs                — POST treks/{trekId}/stops/{stopId}/products/unplanned
  AddUnplannedStopProductByDriverToken.cs   — POST driver/{token}/stops/{stopId}/products/unplanned
  RecordStopReturn.cs                       — POST treks/{trekId}/stops/{stopId}/returns
  RecordStopReturnByDriverToken.cs          — POST driver/{token}/stops/{stopId}/returns
  DeleteStopReturn.cs                       — DELETE treks/{trekId}/stops/{stopId}/returns/{id}
  DeleteStopReturnByDriverToken.cs          — DELETE driver/{token}/stops/{stopId}/returns/{id}
  SyncOfflineActionsByDriverToken.cs        — POST driver/{token}/sync
```

---

## Open Questions

- [ ] Should `AddWalkInStop` via driver token be restricted to the driver's own assigned trek, or any active trek in the region?
- [ ] Should the `RecordUnplannedSale` offline action also support `packagingQtyDelivered`?
- [ ] Should product returns from a previous trek reference the original trek ID for reporting purposes?
- [ ] When a conflict occurs during sync (e.g. trek already Completed), should the entire batch fail or just that action?
- [ ] Does the PDF generator need a separate "field report" layout for walk-in stops vs planned stops?

---

*Last updated: 2026-09-17*
