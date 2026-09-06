# 04 — Staff Onboarding

## Overview

Staff are the people who work at branches — drivers, managers, pharmacy reps, etc. Creating a staff member records their employment details. Optionally granting app access sends them an invitation email so they can log in.

---

## Endpoints Used

| Method | Endpoint | Purpose |
|---|---|---|
| `GET` | `api/v1/staff` | List all staff (paginated, filterable) |
| `GET` | `api/v1/staff/{id}` | Get a single staff member |
| `POST` | `api/v1/staff` | Create a new staff member |
| `PATCH` | `api/v1/staff/{id}` | Update staff details |
| `PATCH` | `api/v1/staff/{id}/status` | Change employment status |
| `POST` | `api/v1/staff/{id}/grant-access` | Send invitation email + create user account |

---

## 1. Listing Staff

```http
GET /api/v1/staff
Authorization: Bearer <token>
```

Supports pagination and filters (branch, status, search). Check Scalar for the full query parameter list.

---

## 2. Creating a Staff Member

```http
POST /api/v1/staff
Authorization: Bearer <token>
Content-Type: application/json

{
  "firstName": "Kwame",
  "lastName": "Asante",
  "email": "k.asante@prohpharmacy.com",
  "phoneNumber": "+233201234567",
  "branchId": "<branch-guid>",
  "employmentType": "FullTime",
  "jobTitle": "Driver"
}
```

- `201` — staff created, returns the staff record including auto-generated `employeeNumber`
- `422` — validation error (missing fields, duplicate email, etc.)
- `422` — branch is inactive

### What happens
- A staff record is created with status `Active`
- An `employeeNumber` is auto-generated (e.g. `EMP-2026-0012`)
- No user account or login access is created yet — that's a separate step

---

## 3. Updating a Staff Member

```http
PATCH /api/v1/staff/{id}
Authorization: Bearer <token>
Content-Type: application/json

{
  "firstName": "Kwame",
  "lastName": "Asante",
  "phoneNumber": "+233209999999",
  "jobTitle": "Senior Driver",
  "branchId": "<branch-guid>"
}
```

---

## 4. Changing Employment Status

```http
PATCH /api/v1/staff/{id}/status
Authorization: Bearer <token>
Content-Type: application/json

{
  "status": "Inactive"
}
```

Common status values: `Active`, `Inactive`, `OnLeave`, `Terminated`.

---

## 5. Granting App Access

Once a staff member is created, you can optionally give them login access to the system. This sends them an invitation email with a link to set their password.

```http
POST /api/v1/staff/{id}/grant-access
Authorization: Bearer <token>
Content-Type: application/json

{
  "email": "k.asante@prohpharmacy.com",
  "roles": ["Driver"]
}
```

- `200` — invitation sent, user account created
- `422` — already has access, or invalid role

### What happens on the backend
1. A user account is created linked to this staff member
2. A time-limited invitation token is generated
3. An invitation email is sent with a link: `{FrontendUrl}/accept-invitation?token=<token>`

---

## 6. The Invitation Acceptance Flow

When the staff member clicks the email link, the frontend must handle `/accept-invitation?token=<token>`:

```http
POST /api/v1/invitations/accept
Content-Type: application/json

{
  "token": "<token-from-url>",
  "password": "theirChosenPassword",
  "confirmPassword": "theirChosenPassword"
}
```

- `200` — account activated, they can now log in
- `422` — token expired, already used, or passwords don't match

After success, redirect to `/login` with a success toast.

---

## UI Flow

```
Staff → Staff List
  └── Create Staff (modal or page)
        ├── Personal details (name, phone, email)
        ├── Branch assignment
        └── Job title / employment type

Staff → Staff Detail Page
  ├── Edit details
  ├── Change status (Active / Inactive / On Leave / Terminated)
  └── Grant App Access button
        └── Role selection → sends invitation email
```

---

## Implementation Checklist

- [ ] Staff list page (paginated, search, filter by branch/status)
- [ ] Create staff form (branch dropdown populated from `GET /organisation/branches`)
- [ ] Staff detail / edit page
- [ ] Change employment status action
- [ ] Grant access modal (email pre-filled, role multi-select)
- [ ] `/accept-invitation` page — reads token from URL, shows set password form
- [ ] Resend invitation (see `POST api/v1/invitations/{id}/resend` in roles doc)
