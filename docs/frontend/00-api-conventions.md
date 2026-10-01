# 00 — API Conventions & Error Handling

## Overview

Reference for error response shapes, status codes, and how to handle them consistently across the app. Read this before implementing any feature.

---

## Error Response Structure

Feature-handler errors generally return this two-field shape (framework binding/authentication failures may use a different or empty body):

```json
{
  "code": "422",
  "message": "Amount must be greater than 0."
}
```

| Field | Type | Description |
|---|---|---|
| `code` | `string` | Application error code; often a status-code string, but not necessarily the actual HTTP status |
| `message` | `string` | Human-readable description of what went wrong |

---

## Error Types & Status Codes

Check each endpoint's implementation guide for the actual HTTP status. Many mutations return `Results.UnprocessableEntity(result.Error)` for every handler failure: an HTTP `422` response can contain body `code: "404"`, `"409"`, or `"400"`. This applies to customer document mutations, warehouse mutations, trek load mutations and return mutations. Swagger `.Produces` declarations do not always match the returned result. Use HTTP status for transport/auth handling and body `message` / `code` for business feedback.

| HTTP Status | `code` value | When it happens |
|---|---|---|
| `400` | `"400"` | Bad request — malformed input or invalid state |
| `404` | `"404"` | Resource not found |
| `409` | `"409"` | Conflict — e.g. duplicate email, already assigned |
| `422` | `"422"` | Validation error or business rule violation |
| `403` | `"403"` | Forbidden — authenticated but not permitted |
| `500` | `"500"` | Unexpected server error |

### Examples

**Not found (404)**
```json
{
  "code": "404",
  "message": "Customer not found."
}
```

**Validation / business rule (422)**
```json
{
  "code": "422",
  "message": "Amount must be greater than 0."
}
```

**Conflict (422)**
```json
{
  "code": "422",
  "message": "Employee number already exists."
}
```

**Bad request (400)**
```json
{
  "code": "400",
  "message": "Cannot assign staff to an inactive branch."
}
```

**Forbidden (403)**
```json
{
  "code": "403",
  "message": "You do not have permission to perform this action."
}
```

---

## Handling Errors in the Frontend

### Typed error helper

```ts
// lib/api-error.ts
export interface ApiError {
  code: string
  message: string
}

export function getApiError(error: unknown): ApiError | null {
  if (axios.isAxiosError(error) && error.response?.data) {
    const data = error.response.data
    if (data.code && data.message) return data as ApiError
  }
  return null
}
```

### Usage in a mutation

```ts
try {
  await api.post('/api/v1/customers', payload)
  toast.success('Customer created.')
} catch (error) {
  const err = getApiError(error)
  if (err) {
    toast.error(err.message)
  } else {
    toast.error('Something went wrong. Please try again.')
  }
}
```

### Handling specific codes

```ts
const err = getApiError(error)

if (err?.code === '404') {
  // show not found state
} else if (err?.code === '422') {
  // show inline validation message
  setFieldError(err.message)
} else if (err?.code === '403') {
  // redirect to /unauthorized
} else {
  // generic fallback
  toast.error('Something went wrong.')
}
```

---

## Success Response Shapes

| Operation | HTTP Status | Body |
|---|---|---|
| Create | `201 Created` | The created resource object |
| Get single | `200 OK` | The resource object |
| Get list (paginated) | `200 OK` | `PaginatedData<T>` (see below) |
| Update / action | `200 OK` | The updated resource object |
| Delete / unassign | `204 No Content` | *(empty)* |

### Paginated list shape

```json
{
  "totalCount": 48,
  "totalPages": 5,
  "currentPage": 1,
  "pageSize": 10,
  "nextPageUrl": "https://api.prohpharmacy.com/api/v1/staff?pageNumber=2",
  "previousPageUrl": null,
  "path": "https://api.prohpharmacy.com/api/v1/staff?pageNumber=1",
  "links": ["...url per page"],
  "data": [ ...items ]
}
```

---

## Pagination Query Params

Query parameters vary by endpoint; the following names are common, not universal:

| Param | Type | Default | Description |
|---|---|---|---|
| `pageNumber` | `int` | `1` | Page to fetch |
| `pageSize` | `int` | Endpoint-specific | Items per page; do not assume a global default or maximum |
| `search` | `string` | — | Full-text search across key fields |
| `sort` | `string` | — | Format: `fieldName_asc` or `fieldName_desc` |

For the invoice list and vehicle stock ledger, `QueryBuilder.Paginate` only enables pagination when `pageSize` is supplied; otherwise the response is a plain array. Send explicit positive `pageNumber` and `pageSize` to obtain the envelope above. These queries do not impose a default of 20 or a maximum of 100. Vehicle stock, trek stock loads and return lists are unpaginated arrays and do not accept the generic list controls.

---

## Authentication Errors

| Scenario | Status | What to do |
|---|---|---|
| No token / expired token | `401` | Attempt silent refresh → if fails, redirect to `/login` |
| Valid token, insufficient role | `403` | Redirect to `/unauthorized` page |
