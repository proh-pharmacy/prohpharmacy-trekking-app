# 18 — Offline Customer Sync

## Overview

The app supports registering new customers while the device has no internet connection. The frontend stores the record locally, then pushes it to the server when connectivity is restored. The backend deduplicates using a client-generated ID so double-syncing is safe.

---

## How it works

### Going offline

Before the user goes into the field, the app must cache the following reference data locally (fetch once on login or app load):

| Data | Endpoint | Why it's needed offline |
|---|---|---|
| Regions | `GET /api/v1/organisation/regions` | `regionId` is required on every customer |
| Districts | `GET /api/v1/organisation/districts` | `districtId` is required on every location |
| Trek assignment | `GET /api/v1/treks/{id}` | `registeredDuringTrekId` if registering mid-trek |

### Creating a customer offline

When the user submits the registration form with no internet:

1. **Generate a UUID client-side** — this becomes `clientGeneratedId`. Use `crypto.randomUUID()`.
2. **Capture the device timestamp** — store `recordedAt: new Date().toISOString()`.
3. **Save the full record to local storage** (IndexedDB recommended) with status `pending`.
4. Show the customer in the UI immediately — treat it as created locally.

### Syncing when back online

When connectivity is detected, collect all `pending` records and send them in one batch. Mark each as `syncing`, then on success mark as `synced` and store the server-assigned `customerId` and `customerCode`.

---

## POST /api/v1/customers/sync

Accepts a batch of offline-created customers. Each item is processed independently — one failure does not block the rest.

### Request body

```json
{
  "items": [
    {
      "clientGeneratedId": "a1b2c3d4-...",
      "recordedAt": "2026-09-10T08:32:00Z",
      "businessName": "Accra Pharmacy Ltd",
      "tradingName": "Accra Pharma",
      "customerType": "RetailPharmacy",
      "regionId": "<region-guid>",
      "primaryPhoneNumber": "+233201234567",
      "whatsAppNumber": "+233201234567",
      "registeredDuringTrekId": "<trek-guid-or-null>",
      "representative": {
        "firstName": "Ama",
        "middleName": null,
        "lastName": "Boateng",
        "relationshipType": "Owner",
        "primaryPhoneNumber": "+233209876543",
        "ghanaCardNumber": "GHA-123456789-0"
      },
      "location": {
        "districtId": "<district-guid>",
        "streetAddress": "12 Liberation Road, Accra",
        "landmarkAndDirections": "Next to Accra Mall, ground floor",
        "latitude": 5.6032,
        "longitude": -0.1869,
        "accuracyMetres": 12.5
      }
    }
  ]
}
```

### Field rules

**Top-level fields:**

| Field | Required | Notes |
|---|---|---|
| `clientGeneratedId` | Yes | UUID generated on the device — used for deduplication |
| `recordedAt` | Yes | Device timestamp at time of capture (ISO 8601 UTC) |
| `businessName` | Yes | Max 200 chars |
| `customerType` | Yes | See enum reference in doc 09 |
| `regionId` | Yes | Must match a cached region |
| `primaryPhoneNumber` | Yes | Max 30 chars |
| `tradingName` | No | Max 200 chars |
| `whatsAppNumber` | No | Max 30 chars |
| `registeredDuringTrekId` | No | Trek GUID if customer was registered mid-trek |

**`representative`:**

| Field | Required | Notes |
|---|---|---|
| `firstName` | Yes | Max 80 chars |
| `lastName` | Yes | Max 80 chars |
| `relationshipType` | Yes | See enum reference in doc 09 |
| `primaryPhoneNumber` | Yes | Max 30 chars |
| `middleName` | No | Max 80 chars |
| `ghanaCardNumber` | No | Max 30 chars |

**`location`:**

| Field | Required | Notes |
|---|---|---|
| `districtId` | Yes | Must match a cached district |
| `latitude` | Yes | -90 to 90 |
| `longitude` | Yes | -180 to 180 |
| `accuracyMetres` | Yes | `> 0` = GPS captured, `0` = manual map selection |
| `streetAddress` | No | Max 300 chars |
| `landmarkAndDirections` | No | Max 500 chars |

### Response `200 OK`

```json
{
  "synced": 2,
  "skipped": 1,
  "failed": 0,
  "results": [
    {
      "clientGeneratedId": "a1b2c3d4-...",
      "status": "Created",
      "customerId": "...",
      "customerCode": "GAR-00042",
      "error": null
    },
    {
      "clientGeneratedId": "b2c3d4e5-...",
      "status": "AlreadySynced",
      "customerId": "...",
      "customerCode": "GAR-00031",
      "error": null
    },
    {
      "clientGeneratedId": "c3d4e5f6-...",
      "status": "Created",
      "customerId": "...",
      "customerCode": "GAR-00043",
      "error": null
    }
  ]
}
```

### Per-item statuses

| Status | Meaning |
|---|---|
| `Created` | Customer was successfully created. Use `customerId` and `customerCode` to update your local record. |
| `AlreadySynced` | A customer with this `clientGeneratedId` already exists. The existing `customerId` and `customerCode` are returned — update your local record with these. |
| `Failed` | Validation or lookup error. Check `error` for the reason. The item was not saved. |

### Errors
- `422` — batch is empty

---

## Key differences from online registration

| | `POST /api/v1/customers` | `POST /api/v1/customers/sync` |
|---|---|---|
| `clientGeneratedId` | Not sent | Required — dedup key |
| `recordedAt` | Set by server (now) | Set by device (time of capture) |
| `createdOffline` | Always `false` | Always `true` |
| Response | Full `CustomerResponse` | Per-item `SyncItemResult` |
| Batch | Single record | Up to many records at once |

---

## Suggested frontend flow

```js
// 1. On form submit offline — save locally
const pendingCustomer = {
  clientGeneratedId: crypto.randomUUID(),
  recordedAt: new Date().toISOString(),
  status: 'pending',
  // ... all form fields
};
await localDb.customers.add(pendingCustomer);

// 2. On reconnect — collect and push
async function syncPending() {
  const pending = await localDb.customers.where({ status: 'pending' }).toArray();
  if (!pending.length) return;

  const response = await fetch('/api/v1/customers/sync', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${token}`
    },
    body: JSON.stringify({ items: pending })
  });

  const { results } = await response.json();

  for (const result of results) {
    if (result.status === 'Created' || result.status === 'AlreadySynced') {
      await localDb.customers.update(result.clientGeneratedId, {
        status: 'synced',
        customerId: result.customerId,
        customerCode: result.customerCode
      });
    } else {
      await localDb.customers.update(result.clientGeneratedId, {
        status: 'failed',
        syncError: result.error
      });
    }
  }
}

// 3. Listen for reconnection
window.addEventListener('online', syncPending);
```

## Notes

- **Token expiry** — if the device is offline for a long time, the JWT will expire. Detect a `401` response on sync and redirect to login before retrying.
- **Portrait upload** — `POST /api/v1/customers/{customerId}/people/{personId}/portrait` requires the server-assigned `customerId` and `personId`. Queue portrait uploads separately and run them after the sync batch completes.
- **Batch size** — there is no enforced limit, but keep batches under 100 items per request for predictable response times.
