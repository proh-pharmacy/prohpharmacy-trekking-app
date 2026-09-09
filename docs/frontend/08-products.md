# 08 — Products & Units

## Overview

Units are managed separately as a lookup list. When creating or updating a product, fetch the units list and let the user pick from it — the selected unit name is stored as a string on the product.

---

## Units Endpoints

| Method | Endpoint | Purpose |
|---|---|---|
| `GET` | `api/v1/units` | List all units |
| `POST` | `api/v1/units` | Create a unit |
| `PUT` | `api/v1/units/{id}` | Rename a unit |
| `PATCH` | `api/v1/units/{id}/status` | Toggle active / inactive |

## Products Endpoints

| Method | Endpoint | Purpose |
|---|---|---|
| `GET` | `api/v1/products` | List all products (paginated, searchable) |
| `POST` | `api/v1/products` | Create a product |
| `PUT` | `api/v1/products/{id}` | Update a product |
| `PATCH` | `api/v1/products/{id}/status` | Toggle active / inactive |

---

## GET /api/v1/units

### Query parameters

| Parameter | Type | Description |
|---|---|---|
| `search` | `string` | Search by name |
| `sort` | `string` | e.g. `name_asc`, `createdAt_desc` |
| `pageNumber` | `int` | Omit for all results |
| `pageSize` | `int` | Omit for all results |
| `isActive` | `bool` | `true` = active only, `false` = inactive only |

### Response `200 OK` — `PaginatedData<UnitResponse>`

```json
{
  "id": "...",
  "name": "Strips",
  "isActive": true,
  "createdAt": "2026-09-09T10:00:00Z",
  "updatedAt": null
}
```

---

## POST /api/v1/units

### Request body

```json
{ "name": "Strips" }
```

| Field | Required | Constraints |
|---|---|---|
| `name` | Yes | Max 80 chars, must be unique |

### Response `201 Created` — `UnitResponse`

### Errors
- `422` — name already exists or validation error

---

## PUT /api/v1/units/{id}

### Request body

```json
{ "name": "Cartons" }
```

### Response `200 OK` — `UnitResponse`

### Errors
- `404` — unit not found
- `422` — name already taken or validation error

---

## PATCH /api/v1/units/{id}/status

No request body. Toggles `isActive` between `true` and `false`.

### Response `200 OK` — `UnitResponse` with updated `isActive`

### Errors
- `404` — unit not found

---

## GET /api/v1/products

### Query parameters

| Parameter | Type | Description |
|---|---|---|
| `search` | `string` | Search by product name |
| `sort` | `string` | e.g. `name_asc`, `createdAt_desc` |
| `pageNumber` | `int` | Omit for all results |
| `pageSize` | `int` | Omit for all results |
| `isActive` | `bool` | `true` = active only, `false` = inactive only |

### Response `200 OK` — `PaginatedData<ProductResponse>`

```json
{
  "id": "...",
  "name": "Paracetamol 500mg",
  "unit": "Strips",
  "description": "Pain relief tablets",
  "isActive": true,
  "createdAt": "2026-09-09T10:00:00Z",
  "updatedAt": null
}
```

---

## POST /api/v1/products

Fetch `GET /api/v1/units` first and use the names as a dropdown. Send the selected unit name as the `unit` field.

### Request body

```json
{
  "name": "Paracetamol 500mg",
  "unit": "Strips",
  "description": "Pain relief tablets"
}
```

| Field | Required | Constraints |
|---|---|---|
| `name` | Yes | Max 200 chars |
| `unit` | No | Max 80 chars — pick from `GET /api/v1/units` |
| `description` | No | Max 500 chars |

- New products default to `isActive: true`

### Response `201 Created` — `ProductResponse`

### Errors
- `422` — validation error

---

## PUT /api/v1/products/{id}

Same fields and rules as POST.

### Response `200 OK` — `ProductResponse`

### Errors
- `404` — product not found
- `422` — validation error

---

## PATCH /api/v1/products/{id}/status

No request body. Toggles `isActive` between `true` and `false`.

### Response `200 OK` — `ProductResponse` with updated `isActive`

### Errors
- `404` — product not found
