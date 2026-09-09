# 07 — Organisation (Regions, Districts, Branches)

## Overview

The organisation hierarchy is: **Region → District → Branch**. Branches must belong to a district, and the district must belong to the specified region. Regions and districts are seeded for Ghana — you typically only need to create branches.

---

## Endpoints Used

| Method | Endpoint | Purpose |
|---|---|---|
| `GET` | `api/v1/organisation/regions` | List all regions |
| `POST` | `api/v1/organisation/regions` | Create a region |
| `GET` | `api/v1/organisation/districts` | List all districts (filter by region) |
| `POST` | `api/v1/organisation/districts` | Create a district |
| `GET` | `api/v1/organisation/branches` | List branches (paginated, filterable) |
| `GET` | `api/v1/organisation/branches/{id}` | Get a single branch |
| `POST` | `api/v1/organisation/branches` | Create a branch |
| `PUT` | `api/v1/organisation/branches/{id}` | Update a branch |
| `PATCH` | `api/v1/organisation/branches/{id}/toggle-status` | Activate / deactivate a branch |

---

## 1. Regions

Regions are seeded (all Ghana regions). You rarely need to create one manually.

### List regions

```http
GET /api/v1/organisation/regions
Authorization: Bearer <token>
```

Query params:

| Parameter | Type | Description |
|---|---|---|
| `search` | `string` | Search by name or code |
| `sort` | `string` | e.g. `name_asc`, `createdAt_desc` |
| `pageNumber` | `int` | Default: unpaginated if omitted |
| `pageSize` | `int` | Default: unpaginated if omitted |
| `includeInactive` | `bool` | `true` to include inactive regions. Default: active only |

Response item:
```json
{
  "id": "...",
  "code": "GAR",
  "name": "Greater Accra Region",
  "isActive": true,
  "createdAt": "2026-09-09T00:00:00Z"
}
```

### Create a region

```http
POST /api/v1/organisation/regions
Authorization: Bearer <token>
Content-Type: application/json

{ "name": "Greater Accra Region" }
```

- `name` is required and must be unique
- `code` is auto-generated from the name
- `422` — name already exists

---

## 2. Districts

Districts belong to a region. Seeded for Ghana alongside regions.

### List districts

```http
GET /api/v1/organisation/districts
Authorization: Bearer <token>
```

Query params:

| Parameter | Type | Description |
|---|---|---|
| `regionId` | `guid` | Filter by region |
| `search` | `string` | Search by name or code |
| `sort` | `string` | e.g. `name_asc`, `createdAt_desc` |
| `pageNumber` | `int` | Omit for all results |
| `pageSize` | `int` | Omit for all results |
| `includeInactive` | `bool` | Default: active only |

Response item:
```json
{
  "id": "...",
  "regionId": "...",
  "regionName": "Greater Accra Region",
  "code": "ACD",
  "name": "Accra",
  "isActive": true,
  "createdAt": "2026-09-09T00:00:00Z"
}
```

### Create a district

```http
POST /api/v1/organisation/districts
Authorization: Bearer <token>
Content-Type: application/json

{
  "regionId": "<region-guid>",
  "name": "Accra"
}
```

- `regionId` and `name` are both required
- Name must be unique within the region
- `code` is auto-generated
- `404` — region not found
- `422` — name already exists in this region

---

## 3. Branches

### List branches

```http
GET /api/v1/organisation/branches
Authorization: Bearer <token>
```

Query params:

| Parameter | Type | Description |
|---|---|---|
| `search` | `string` | Search by name or code |
| `sort` | `string` | e.g. `name_asc`, `createdAt_desc` |
| `pageNumber` | `int` | Omit for all results |
| `pageSize` | `int` | Omit for all results |
| `regionId` | `guid` | Filter by region |
| `districtId` | `guid` | Filter by district |
| `branchType` | `string` | `Retail` / `Wholesale` / `Laboratory` |
| `includeInactive` | `bool` | Default: active branches only |

### Get a branch

```http
GET /api/v1/organisation/branches/{id}
Authorization: Bearer <token>
```

Response:
```json
{
  "id": "...",
  "code": "TEM-BR",
  "name": "Tema Branch",
  "branchType": "Retail",
  "regionId": "...",
  "regionName": "Greater Accra Region",
  "districtId": "...",
  "districtName": "Tema",
  "address": "Plot 5, Harbour Road, Tema",
  "latitude": 5.6698,
  "longitude": -0.0166,
  "contactNumber": "+233201234567",
  "isActive": true,
  "createdAt": "2026-09-09T10:00:00Z",
  "updatedAt": null
}
```

### Create a branch

```http
POST /api/v1/organisation/branches
Authorization: Bearer <token>
Content-Type: application/json
```

**Required fields:**
```json
{
  "name": "Tema Branch",
  "branchType": "Retail",
  "regionId": "<region-guid>",
  "districtId": "<district-guid>",
  "address": "Plot 5, Harbour Road, Tema",
  "contactNumber": "+233201234567"
}
```

**Optional fields:**

| Field | Type | Notes |
|---|---|---|
| `latitude` | `decimal` | GPS coordinate |
| `longitude` | `decimal` | GPS coordinate |

**`branchType` values — send the string name, not a number:**

| Value | Meaning |
|---|---|
| `"Retail"` | Retail pharmacy branch |
| `"Wholesale"` | Wholesale distribution branch |
| `"Laboratory"` | Laboratory branch |

> **Important:** always send `branchType` as a string (`"Retail"`, not `1`). Sending `0` or omitting the field causes a `422` validation error.

**Validation rules:**
- `districtId` must belong to the specified `regionId` — mismatch returns `400`
- Branch name must be unique within the district
- `code` is auto-generated from the name
- `422` — validation errors or name conflict
- `404` — region or district not found

**Response `201 Created`** — same shape as GET single branch above.

---

### Update a branch

All fields are required.

```http
PUT /api/v1/organisation/branches/{id}
Authorization: Bearer <token>
Content-Type: application/json

{
  "name": "Tema Branch",
  "branchType": "Retail",
  "regionId": "<region-guid>",
  "districtId": "<district-guid>",
  "address": "New Address, Tema",
  "contactNumber": "+233209999999",
  "latitude": 5.6698,
  "longitude": -0.0166
}
```

- `200` — returns updated branch
- `404` — branch not found
- `422` — validation errors

---

### Toggle branch status

Toggles between active and inactive. No body required.

```http
PATCH /api/v1/organisation/branches/{id}/toggle-status
Authorization: Bearer <token>
```

Response:
```json
{
  "id": "...",
  "code": "TEM-BR",
  "name": "Tema Branch",
  "isActive": false,
  "message": "Branch deactivated."
}
```

**Important:** deactivating a branch prevents new staff from being assigned to it. Existing staff are unaffected.

---

## UI Flow

```
Organisation → Regions list
  └── (rarely needed) Create region

Organisation → Districts list (filter by region)
  └── (rarely needed) Create district

Organisation → Branches list (paginated, filter by region/district/type)
  ├── Create branch
  │     ├── Region dropdown → GET /organisation/regions
  │     ├── District dropdown → GET /organisation/districts?regionId=<id>
  │     │     (re-fetch when region changes)
  │     ├── Branch type dropdown: Retail | Wholesale | Laboratory
  │     └── Address, contact number, optional GPS
  ├── Branch detail page
  │     ├── Edit → PUT /organisation/branches/{id}
  │     └── Toggle status button (Active → Deactivate / Inactive → Activate)
```

---

## Implementation Checklist

- [ ] Regions list (used as dropdown source)
- [ ] Districts list filtered by region (used as dropdown source)
- [ ] Branches list page (paginated, filter by region / district / type / status)
- [ ] Create branch form
  - [ ] Region dropdown from `GET /organisation/regions`
  - [ ] District dropdown from `GET /organisation/districts?regionId=<id>` (re-fetch on region change)
  - [ ] Branch type dropdown: `Retail` | `Wholesale` | `Laboratory` (send string value)
- [ ] Branch detail page (view all fields)
- [ ] Edit branch form (PUT — all fields required)
- [ ] Toggle active/inactive with confirmation
