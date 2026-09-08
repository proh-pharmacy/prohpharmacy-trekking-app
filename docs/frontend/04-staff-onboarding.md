# 04 — Staff Onboarding

## Overview

Staff are the people who work at branches — drivers, field staff, managers, etc. Creating a staff member records their employment details. App access (login account) can be granted at creation time or later as a separate step.

---

## Endpoints Used

| Method | Endpoint | Purpose |
|---|---|---|
| `GET` | `api/v1/staff` | List all staff (paginated, filterable) |
| `GET` | `api/v1/staff/{id}` | Get a single staff member |
| `POST` | `api/v1/staff` | Create a new staff member |
| `PATCH` | `api/v1/staff/{id}` | Update staff details |
| `PATCH` | `api/v1/staff/{id}/status` | Change employment status |
| `POST` | `api/v1/staff/{id}/grant-access` | Grant login access to an existing staff member |
| `POST` | `api/v1/invitations` | Send an email invitation to an existing staff member |
| `POST` | `api/v1/staff/{id}/photo` | Upload staff passport photo |

---

## 1. List Staff

```http
GET /api/v1/staff
Authorization: Bearer <token>
```

Supports pagination and filters. Returns `PaginatedData<StaffResponse>`.

---

## 2. Get a Staff Member

```http
GET /api/v1/staff/{id}
Authorization: Bearer <token>
```

Response:
```json
{
  "id": "...",
  "employeeNumber": "EMP-2026-001",
  "firstName": "Kwame",
  "lastName": "Asante",
  "fullName": "Kwame Asante",
  "phoneNumber": "+233201234567",
  "emailAddress": "k.asante@prohpharmacy.com",
  "role": "Driver",
  "branchId": "...",
  "branchName": "Tema Branch",
  "employmentStatus": "Active",
  "joinedOn": "2026-09-08",
  "hasAppAccess": true,
  "systemRoles": ["Driver"],
  "currentDeviceId": "...",
  "currentDeviceName": "Device 001",
  "createdAt": "2026-09-08T10:00:00Z",
  "updatedAt": null
}
```

---

## 3. Create a Staff Member

```http
POST /api/v1/staff
Authorization: Bearer <token>
Content-Type: application/json
```

### Required fields

```json
{
  "firstName": "Kwame",
  "lastName": "Asante",
  "phoneNumber": "+233201234567",
  "emailAddress": "k.asante@prohpharmacy.com",
  "branchId": "<branch-guid>",
  "joinedOn": "2026-09-08",
  "role": "Driver"
}
```

### Optional fields

| Field | Type | Notes |
|---|---|---|
| `employeeNumber` | `string` | Custom employee number. Auto-generated if omitted |
| `grantAppAccess` | `bool` | `true` to create a login account in the same request |
| `initialPassword` | `string` | Min 8 chars. If omitted, defaults to `firstnamelastname` (e.g. `kwameasante`) |

### Full example with app access

```json
{
  "firstName": "Kwame",
  "lastName": "Asante",
  "phoneNumber": "+233201234567",
  "emailAddress": "k.asante@prohpharmacy.com",
  "branchId": "<branch-guid>",
  "joinedOn": "2026-09-08",
  "role": "Driver",
  "grantAppAccess": true,
  "initialPassword": "mypassword123"
}
```

### Notes
- `role` is **optional** when `grantAppAccess: false` — you can set it later
- `role` is **required** when `grantAppAccess: true` — the account must be assigned to a role at creation
- `role` must match an existing role name: seeded roles are `SuperAdmin`, `OperationsManager`, `BranchManager`, `FieldStaff`, `Driver`, `CreditOfficer`, `Auditor` — or any custom role created via `POST /api/v1/roles`
- `branchId` must be an active branch — inactive branch returns `400`
- `emailAddress` must be unique — duplicate returns `422`
- If `grantAppAccess: true`, the plain-text `initialPassword` is returned **once** in the response — store or display it to the admin immediately
- If `grantAppAccess: false`, employment status is set to `Pending` until access is granted later

### Response `201 Created`
```json
{
  "id": "...",
  "employeeNumber": "EMP-2026-012",
  "fullName": "Kwame Asante",
  "role": "Driver",
  "branchName": "Tema Branch",
  "employmentStatus": "Active",
  "hasAppAccess": true,
  "systemRoles": ["Driver"],
  "initialPassword": "kwameasante",
  "..."
}
```

---

## 4. Update a Staff Member

Cannot update `emailAddress` or `joinedOn` after creation.

```http
PATCH /api/v1/staff/{id}
Authorization: Bearer <token>
Content-Type: application/json

{
  "firstName": "Kwame",
  "lastName": "Asante",
  "phoneNumber": "+233209999999",
  "branchId": "<branch-guid>",
  "role": "FieldStaff"
}
```

- All fields are **required** in the update payload
- `role` is optional — omit to keep the current role
- Returns `422` if trying to update an `Offboarded` staff member

---

## 5. Change Employment Status

```http
PATCH /api/v1/staff/{id}/status
Authorization: Bearer <token>
Content-Type: application/json

{
  "status": "Suspended"
}
```

Valid statuses: `Pending` `Active` `Suspended` `Offboarded`

Response:
```json
{
  "staffMemberId": "...",
  "employmentStatus": "Suspended",
  "appAccessRevoked": true
}
```

**Important behaviour:**
- Setting `Suspended` or `Offboarded` immediately **revokes app access** and invalidates all refresh tokens — the staff member is logged out everywhere
- Setting `Active` re-enables app access if the staff member has an account
- `appAccessRevoked: true` in the response means you should inform the admin that the login was also disabled

---

## 6. Grant App Access (for existing staff)

Use this when a staff member was created without `grantAppAccess: true` and needs login access later.

```http
POST /api/v1/staff/{id}/grant-access
Authorization: Bearer <token>
Content-Type: application/json

{
  "role": "Driver",
  "initialPassword": "mypassword123"
}
```

- `role` is optional if the staff member already has a role assigned — provide it to override
- `initialPassword` is optional — defaults to `firstnamelastname` if omitted
- Returns `422` if staff member already has access
- Returns `400` if staff member is `Offboarded`
- Sends a welcome email to the staff member
- Returns the full staff response including `initialPassword` once

---

## 7. Send an Invitation

An alternative to `grant-access` — sends an email to the staff member with a link for them to set their own password.

```http
POST /api/v1/invitations
Authorization: Bearer <token>
Content-Type: application/json

{
  "staffMemberId": "<guid>",
  "roleNames": ["Driver", "FieldStaff"]
}
```

- `roleNames` is **required** — at least one role must be specified
- Staff member must already exist and **not** have app access yet
- Cannot invite an `Offboarded` staff member
- If a valid pending invitation already exists, a fresh email is sent, the expiry is extended, and the roles are updated — no duplicate invitation is created
- Link expires in **48 hours**
- The specified roles are assigned to the user account when they accept the invitation

### Response `200 OK`
```json
{
  "invitationId": "...",
  "staffMemberId": "...",
  "staffFullName": "Kwame Asante",
  "staffEmail": "k.asante@prohpharmacy.com",
  "roles": ["Driver", "FieldStaff"],
  "expiresAt": "2026-09-10T10:00:00Z",
  "message": "Invitation email sent to k.asante@prohpharmacy.com."
}
```

**grant-access vs invitation:**
| | `grant-access` | `invitation` |
|---|---|---|
| Password set by | Admin | Staff member |
| Immediate access | Yes | No — after they accept |
| Returns password | Yes (once) | No |

---

## 8. Upload Passport Photo

```http
POST /api/v1/staff/{id}/photo
Authorization: Bearer <token>
Content-Type: multipart/form-data

file: <image file>
```

- Accepts **JPEG, PNG, or WebP** only
- Maximum size: **5 MB**
- Photo is stored on ImageKit and the URL is saved against the staff record
- Returns `422` for unsupported file type or oversized file

### Response `200 OK`
```json
{
  "staffMemberId": "...",
  "profilePhotoUrl": "https://ik.imagekit.io/..."
}
```

---

## UI Flow

```
Staff → Staff List (paginated, search, filter by branch/status)
  └── Create Staff (modal or page)
        ├── First name, last name, phone, email
        ├── Branch dropdown (active branches only)
        ├── Role dropdown (seeded roles)
        ├── Joined on (date picker)
        ├── Employee number (optional)
        ├── Grant app access toggle
        │     └── (if on) Initial password field (optional — show auto-default hint)
        └── Submit → show initialPassword in a one-time reveal dialog

Staff → Staff Detail Page
  ├── View all fields
  ├── Edit button → PATCH /staff/{id}
  ├── Status badge + Change status dropdown (Active/Suspended/Offboarded)
  │     └── Warn: "This will revoke app access and log them out"
  └── Grant Access button (shown only if hasAppAccess: false)
        └── Role (pre-filled if set) + optional password → POST /staff/{id}/grant-access
              └── Show initialPassword in a one-time reveal dialog
```

---

## Implementation Checklist

- [ ] Staff list page (paginated, search, filter by branch and status)
- [ ] Staff detail page (all fields, edit button, status badge)
- [ ] Create staff form
  - [ ] Branch dropdown from `GET /organisation/branches`
  - [ ] Role dropdown from `GET /roles`
  - [ ] Grant access toggle + password field
  - [ ] One-time initial password reveal on success
- [ ] Edit staff form (PATCH)
- [ ] Change status with confirmation warning when suspending/offboarding
- [ ] Grant access modal (for existing staff without access)
- [ ] One-time initial password reveal after granting access
- [ ] Send invitation (`POST /api/v1/invitations`) — alternative to grant-access, staff sets their own password
- [ ] Upload passport photo (`POST /api/v1/staff/{id}/photo`) — file input accepting JPEG/PNG/WebP ≤ 5 MB
