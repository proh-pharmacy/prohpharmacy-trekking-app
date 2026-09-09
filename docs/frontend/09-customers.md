# 09 — Customers

## Endpoints

| Method | Endpoint | Purpose |
|---|---|---|
| `GET` | `api/v1/customers` | List customers (paginated, filterable) |
| `GET` | `api/v1/customers/map-pins` | All customer locations for a map (unpaginated) |
| `GET` | `api/v1/customers/{id}` | Get a single customer |
| `POST` | `api/v1/customers` | Register a new customer |
| `PATCH` | `api/v1/customers/{id}` | Update customer business details |
| `POST` | `api/v1/customers/{customerId}/locations` | Add an additional location |
| `POST` | `api/v1/customers/{customerId}/people/{personId}/portrait` | Upload representative portrait |

---

## Enum Reference

### `customerType`
`RetailPharmacy` `WholesalePharmacy` `OTCMedicineSeller` `Clinic` `Hospital` `ChemicalShop` `LicensedHealthFacility` `Other`

### `relationshipType`
`Owner` `Proprietor` `Director` `Manager` `PrimaryContact` `CreditResponsiblePerson` `Guarantor` `Other`

### `registrationStatus`
`Draft` `PendingReview` `Active` `Rejected` `Suspended` `Inactive`

### `locationType`
`BusinessPremises` `DeliveryLocation` `Residential` `Other`

> Always send enum values as strings, not integers.

---

## GET /api/v1/customers/map-pins

Returns a flat, unpaginated array of every customer that has a primary GPS location recorded. Use this to seed a customer overview map — do not use the paginated list endpoint for this.

### Query parameters

| Parameter | Type | Description |
|---|---|---|
| `branchId` | `guid` | Scope to customers belonging to a specific branch |
| `regionId` | `guid` | Scope to customers in a specific region |

### Response `200 OK`

```json
[
  {
    "customerAccountId": "...",
    "customerCode": "GAR-00001",
    "businessName": "Accra Pharmacy Ltd",
    "tradingName": "Accra Pharma",
    "customerType": "RetailPharmacy",
    "registrationStatus": "Active",
    "primaryPhoneNumber": "+233201234567",
    "latitude": 5.6032,
    "longitude": -0.1869,
    "accuracyMetres": 12.5,
    "streetAddress": "12 Liberation Road, Accra",
    "landmarkAndDirections": "Next to Accra Mall, ground floor",
    "branchId": "...",
    "branchName": "Tema Branch",
    "regionId": "...",
    "regionName": "Greater Accra Region",
    "primaryContactName": "Ama Boateng",
    "primaryContactPhone": "+233209876543"
  }
]
```

### Leaflet example

```js
const res = await fetch('/api/v1/customers/map-pins', {
    headers: { Authorization: `Bearer ${token}` }
})
const pins = await res.json()

pins.forEach(p => {
    L.marker([p.latitude, p.longitude])
        .addTo(map)
        .bindPopup(`
            <strong>${p.businessName}</strong><br>
            <small>${p.customerCode} · ${p.customerType}</small><br>
            ${p.primaryContactName ?? ''}<br>
            ${p.streetAddress ?? p.landmarkAndDirections ?? ''}
        `)
})
```

### Marker colour by customer type

```js
const typeColours = {
    RetailPharmacy:         '#00bf6f',
    WholesalePharmacy:      '#0284c7',
    OTCMedicineSeller:      '#f59e0b',
    Clinic:                 '#8b5cf6',
    Hospital:               '#dc2626',
    ChemicalShop:           '#64748b',
    LicensedHealthFacility: '#0891b2',
    Other:                  '#94a3b8'
}

function getCustomerIcon(customerType) {
    const colour = typeColours[customerType] ?? '#94a3b8'
    return L.divIcon({
        className: '',
        html: `<div style="
            width:12px; height:12px; border-radius:50%;
            background:${colour}; border:2px solid #fff;
            box-shadow:0 1px 3px rgba(0,0,0,.4)">
        </div>`,
        iconSize: [12, 12],
        iconAnchor: [6, 6]
    })
}
```

---

## GET /api/v1/customers

### Query parameters

| Parameter | Type | Description |
|---|---|---|
| `search` | `string` | Search by business name, customer code, or phone number |
| `sort` | `string` | e.g. `createdAt_desc`, `businessName_asc` |
| `pageNumber` | `int` | Default: 1 |
| `pageSize` | `int` | Default: 20 |
| `regionId` | `guid` | Filter by region |
| `branchId` | `guid` | Filter by owning branch |
| `customerType` | `string` | e.g. `RetailPharmacy` |
| `status` | `string` | e.g. `Active`, `PendingReview` |

### Response `200 OK` — `PaginatedData<CustomerResponse>`

---

## GET /api/v1/customers/{id}

### Response `200 OK`

```json
{
  "id": "...",
  "customerCode": "GAR-00001",
  "businessName": "Accra Pharmacy Ltd",
  "tradingName": "Accra Pharma",
  "customerType": "RetailPharmacy",
  "registrationStatus": "Active",
  "primaryPhoneNumber": "+233201234567",
  "whatsAppNumber": "+233201234567",
  "regionId": "...",
  "regionName": "Greater Accra Region",
  "owningBranchId": "...",
  "owningBranchName": "Tema Branch",
  "registeredByStaffId": "...",
  "registeredByName": "Kwame Asante",
  "registeredDuringTrekId": null,
  "createdOffline": false,
  "recordedAt": "2026-09-09T10:00:00Z",
  "createdAt": "2026-09-09T10:00:00Z",
  "updatedAt": null,
  "primaryPerson": {
    "id": "...",
    "fullName": "Ama Boateng",
    "relationshipType": "Owner",
    "primaryPhoneNumber": "+233209876543",
    "isPrimaryContact": true,
    "isCreditResponsiblePerson": true
  },
  "primaryLocation": {
    "id": "...",
    "latitude": 5.6032,
    "longitude": -0.1869,
    "accuracyMetres": 12.5,
    "landmarkAndDirections": "Next to Accra Mall, ground floor",
    "streetAddress": "12 Liberation Road, Accra",
    "regionName": "Greater Accra Region",
    "districtName": "Accra",
    "captureMethod": "PwaGps",
    "verificationStatus": "GpsCaptured",
    "isPrimary": true
  }
}
```

### Errors
- `404` — customer not found

---

## POST /api/v1/customers

Customer, representative, and primary location are created in a single request.

### Request body

```json
{
  "businessName": "Accra Pharmacy Ltd",
  "tradingName": "Accra Pharma",
  "customerType": "RetailPharmacy",
  "regionId": "<region-guid>",
  "primaryPhoneNumber": "+233201234567",
  "whatsAppNumber": "+233201234567",
  "registeredDuringTrekId": null,
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
```

### Field rules

**Business fields:**

| Field | Required | Constraints |
|---|---|---|
| `businessName` | Yes | Max 200 chars |
| `customerType` | Yes | String — see enum table |
| `regionId` | Yes | Must exist |
| `primaryPhoneNumber` | Yes | Max 30 chars |
| `tradingName` | No | Max 200 chars |
| `whatsAppNumber` | No | Max 30 chars |
| `registeredDuringTrekId` | No | Trek ID if registered during a visit |

**Representative fields:**

| Field | Required | Constraints |
|---|---|---|
| `firstName` | Yes | Max 80 chars |
| `lastName` | Yes | Max 80 chars |
| `relationshipType` | Yes | String — see enum table |
| `primaryPhoneNumber` | Yes | Max 30 chars |
| `middleName` | No | Max 80 chars |
| `ghanaCardNumber` | No | Max 30 chars |

**Location fields:**

| Field | Required | Constraints |
|---|---|---|
| `districtId` | Yes | Must exist |
| `landmarkAndDirections` | No | Max 500 chars |
| `streetAddress` | No | Max 300 chars |
| `latitude` | Yes | -90 to 90 |
| `longitude` | Yes | -180 to 180 |
| `accuracyMetres` | Yes | Must be > 0 |

### Notes
- `owningBranch` is **auto-resolved** from the authenticated staff member's branch — do not send it
- `customerCode` is **auto-generated** in format `{REGION_CODE}-{SEQUENCE:D5}` e.g. `GAR-00001`
- `captureMethod` is set to `PwaGps` when `accuracyMetres > 0`, otherwise `ManualLocationSelection`
- `verificationStatus` is set to `GpsCaptured` for GPS, `Unverified` for manual
- `registrationStatus` defaults to `Active`

### Response `201 Created` — full `CustomerResponse` (same as GET single)

### Errors
- `422` — validation error
- `404` — region or district not found

---

## PATCH /api/v1/customers/{id}

Updates business details, primary representative, and primary location in a single request. Omit `representative` or `location` to leave them unchanged.

### Request body

```json
{
  "businessName": "Accra Pharmacy Ltd",
  "tradingName": "Accra Pharma",
  "customerType": "RetailPharmacy",
  "regionId": "<region-guid>",
  "primaryPhoneNumber": "+233201234567",
  "whatsAppNumber": "+233209999999",
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
```

**Business fields:**

| Field | Required | Constraints |
|---|---|---|
| `businessName` | Yes | Max 200 chars |
| `regionId` | Yes | If changed, `customerCode` is regenerated for the new region |
| `primaryPhoneNumber` | Yes | Max 30 chars |
| `customerType` | Yes | String — see enum table |
| `tradingName` | No | Max 200 chars |
| `whatsAppNumber` | No | Max 30 chars |

**`representative` — omit to leave unchanged:**

| Field | Required | Constraints |
|---|---|---|
| `firstName` | Yes | Max 80 chars |
| `lastName` | Yes | Max 80 chars |
| `relationshipType` | Yes | String — see enum table |
| `primaryPhoneNumber` | Yes | Max 30 chars |
| `middleName` | No | Max 80 chars |
| `ghanaCardNumber` | No | Max 30 chars |

**`location` — omit to leave unchanged:**

| Field | Required | Constraints |
|---|---|---|
| `districtId` | Yes | Must exist |
| `streetAddress` | No | Max 300 chars |
| `landmarkAndDirections` | No | Max 500 chars |
| `latitude` | No | -90 to 90 |
| `longitude` | No | -180 to 180 |
| `accuracyMetres` | No | Must be > 0 if provided |

### Response `200 OK` — full `CustomerResponse`

### Errors
- `404` — customer, region, or district not found
- `422` — validation error

---

## POST /api/v1/customers/{customerId}/locations

### Request body

```json
{
  "locationType": "DeliveryLocation",
  "regionId": "<region-guid>",
  "districtId": "<district-guid>",
  "landmarkAndDirections": "Near Makola Market, ask for Ama",
  "streetAddress": "Makola Street, Accra",
  "latitude": 5.5483,
  "longitude": -0.2074,
  "accuracyMetres": 8.0,
  "isPrimary": false
}
```

| Field | Required | Constraints |
|---|---|---|
| `regionId` | Yes | |
| `districtId` | Yes | |
| `landmarkAndDirections` | No | Max 500 chars |
| `locationType` | No | String. Defaults to `BusinessPremises` |
| `streetAddress` | No | Max 300 chars |
| `latitude` | No | -90 to 90 |
| `longitude` | No | -180 to 180 |
| `accuracyMetres` | No | Must be > 0 if provided |
| `isPrimary` | No | Set `true` to make this the new primary location |

### Response `201 Created`

```json
{
  "id": "...",
  "customerAccountId": "...",
  "locationType": "DeliveryLocation",
  "regionId": "...",
  "regionName": "Greater Accra Region",
  "districtId": "...",
  "districtName": "Accra",
  "landmarkAndDirections": "Near Makola Market, ask for Ama",
  "streetAddress": "Makola Street, Accra",
  "latitude": 5.5483,
  "longitude": -0.2074,
  "accuracyMetres": 8.0,
  "captureMethod": "PwaGps",
  "verificationStatus": "GpsCaptured",
  "isPrimary": false,
  "createdAt": "2026-09-09T10:00:00Z"
}
```

### Errors
- `404` — customer or district not found
- `422` — validation error

---

## POST /api/v1/customers/{customerId}/people/{personId}/portrait

`multipart/form-data` upload. `personId` comes from `primaryPerson.id` in the customer response.

| Constraint | Value |
|---|---|
| Accepted types | JPEG, PNG, WebP |
| Max size | 5 MB |
| Field name | `file` |

### Response `200 OK`

```json
{
  "personId": "...",
  "portraitUrl": "https://ik.imagekit.io/..."
}
```

### Errors
- `404` — person not found on this customer
- `422` — unsupported file type or file exceeds 5 MB
