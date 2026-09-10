# 17 — Global Search

## Overview

One endpoint searches customers, products, staff, and treks simultaneously. All four result categories are always present in the response — populated if there's a match, empty `[]` if not. If the query matches items in more than one category, all matching categories are populated.

---

## GET /api/v1/search

### Query parameters

| Parameter | Type | Required | Description |
|---|---|---|---|
| `q` | `string` | Yes | Search term — minimum 2 characters |

### Response `200 OK`

```json
{
  "query": "kwame",
  "customers": [],
  "products": [],
  "staff": [
    {
      "id": "...",
      "fullName": "Kwame Asante",
      "role": "Driver",
      "branchName": "Tema Branch"
    }
  ],
  "treks": []
}
```

```json
{
  "query": "parac",
  "customers": [
    {
      "id": "...",
      "businessName": "Paracetamol Pharmacy Ltd",
      "customerCode": "GAR-00041",
      "primaryPhoneNumber": "+233244123456"
    }
  ],
  "products": [
    {
      "id": "...",
      "name": "Paracetamol 500mg",
      "unit": "Strips"
    },
    {
      "id": "...",
      "name": "Paracetamol 250mg",
      "unit": "Strips"
    }
  ],
  "staff": [],
  "treks": []
}
```

### Result limits

Up to **5 results per category**. This is a typeahead/quick-nav search, not a full results page — if the user wants more, they should navigate to the relevant list page with the search pre-filled.

### What is searched

| Category | Fields matched |
|---|---|
| `customers` | Business name, customer code, phone number |
| `products` | Product name (active products only) |
| `staff` | First name, last name, full name, employee number, email address |
| `treks` | Trek number (e.g. `TRK-00042`) |

### Errors

- `422` — `q` is missing or fewer than 2 characters

---

## Frontend integration

### Debounce

Do not fire a request on every keystroke. Debounce the input by 300ms:

```js
let timer;
input.addEventListener('input', () => {
  clearTimeout(timer);
  timer = setTimeout(() => {
    if (input.value.trim().length >= 2) fetchSearch(input.value.trim());
    else clearResults();
  }, 300);
});
```

### Fetch

```js
async function fetchSearch(q) {
  const response = await fetch(
    `/api/v1/search?q=${encodeURIComponent(q)}`,
    { headers: { Authorization: `Bearer ${token}` } }
  );
  const data = await response.json();
  renderResults(data);
}
```

### Rendering

Group results by category with a heading per section. Only render sections that have at least one result. Show "No results" only if all four arrays are empty.

| Category | Navigate to |
|---|---|
| Customer result | Customer detail page (`/customers/{id}`) |
| Product result | Product detail/edit page (`/products/{id}`) |
| Staff result | Staff detail page (`/staff/{id}`) |
| Trek result | Trek detail page (`/treks/{id}`) |
