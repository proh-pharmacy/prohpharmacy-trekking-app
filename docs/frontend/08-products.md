# 08 — Products

## Endpoints

| Method | Endpoint | Purpose |
|---|---|---|
| `GET` | `api/v1/products` | List all products (paginated, searchable) |
| `POST` | `api/v1/products` | Create a product |
| `PUT` | `api/v1/products/{id}` | Update a product |
| `PATCH` | `api/v1/products/{id}/status` | Toggle active / inactive |

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
| `unit` | No | e.g. `Strips`, `Bottles`, `Sachets`. Max 50 chars |
| `description` | No | Max 500 chars |

- New products default to `isActive: true`

### Response `201 Created` — `ProductResponse` (same shape as list item)

### Errors
- `422` — validation error

---

## PUT /api/v1/products/{id}

### Request body — same fields as POST, all optional rules same

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
