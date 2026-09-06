# 02 — Initial Setup (Regions, Districts, Branches)

## Overview

Before the system can be used, an admin must configure the organisational structure: Regions → Districts → Branches. Ghana's regions are pre-seeded by the backend on startup. Districts and branches are created manually through the UI.

---

## Endpoints Used

| Method | Endpoint | Purpose |
|---|---|---|
| `GET` | `api/v1/organisation/regions` | List all regions |
| `POST` | `api/v1/organisation/regions` | Create a custom region |
| `GET` | `api/v1/organisation/districts` | List all districts (filterable) |
| `POST` | `api/v1/organisation/districts` | Create a district under a region |
| `GET` | `api/v1/organisation/branches` | List all branches |
| `GET` | `api/v1/organisation/branches/{id}` | Get a single branch |
| `POST` | `api/v1/organisation/branches` | Create a branch |
| `PUT` | `api/v1/organisation/branches/{id}` | Update a branch |
| `PATCH` | `api/v1/organisation/branches/{id}/toggle-status` | Activate / deactivate a branch |

All endpoints require authentication.

---

## Data Hierarchy

```
Region  (e.g. Greater Accra)
  └── District  (e.g. Accra Metropolitan)
        └── Branch  (e.g. Tema Branch)
```

---

## 1. Regions

Ghana's 16 regions are **pre-seeded** by the backend — no need to create them. The `GET` endpoint is used to populate dropdowns when creating districts.

### List regions
```http
GET /api/v1/organisation/regions
Authorization: Bearer <token>
```

Response:
```json
[
  { "id": "...", "name": "Greater Accra" },
  { "id": "...", "name": "Ashanti" },
  ...
]
```

Use this list to populate the **Region** dropdown when creating a district.

---

## 2. Districts

### List districts
```http
GET /api/v1/organisation/districts
Authorization: Bearer <token>
```

Supports query filters (check Scalar for full list — typically `?regionId=<guid>`).

### Create a district
```http
POST /api/v1/organisation/districts
Authorization: Bearer <token>
Content-Type: application/json

{
  "name": "Accra Metropolitan",
  "regionId": "<region-guid>"
}
```

- `201` — district created
- `422` — validation error or duplicate

---

## 3. Branches

Branches are the physical office/depot locations. Each branch belongs to a district.

### List branches
```http
GET /api/v1/organisation/branches
Authorization: Bearer <token>
```

### Create a branch
```http
POST /api/v1/organisation/branches
Authorization: Bearer <token>
Content-Type: application/json

{
  "name": "Tema Branch",
  "districtId": "<district-guid>",
  "address": "123 Harbour Road, Tema",
  "phoneNumber": "+233201234567"
}
```

### Update a branch
```http
PUT /api/v1/organisation/branches/{id}
Authorization: Bearer <token>
Content-Type: application/json

{
  "name": "Tema Main Branch",
  "address": "Updated address",
  "phoneNumber": "+233201234567"
}
```

### Toggle branch status (activate / deactivate)
```http
PATCH /api/v1/organisation/branches/{id}/toggle-status
Authorization: Bearer <token>
```

---

## UI Flow

```
Settings → Organisation
  ├── Regions       (read-only list — pre-seeded)
  ├── Districts     (CRUD — create under a region)
  └── Branches      (CRUD — create under a district, toggle status)
```

### Suggested page structure

```
/settings/organisation/regions       → list only
/settings/organisation/districts     → list + create modal
/settings/organisation/branches      → list + create/edit modal + status toggle
```

---

## Implementation Checklist

- [ ] Fetch and cache regions list (used in dropdowns — rarely changes)
- [ ] Districts list page with region filter
- [ ] Create district modal (region dropdown → name field)
- [ ] Branches list page (show status badge — Active / Inactive)
- [ ] Create branch modal (district dropdown → name, address, phone)
- [ ] Edit branch modal
- [ ] Toggle branch status with confirmation
