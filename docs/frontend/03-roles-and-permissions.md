# 03 — Roles & Permissions

## Overview

The system uses permission-based access control. Roles are seeded by the backend — each role has a set of permissions. A user can have **multiple roles** and their effective permissions are the **union of all assigned roles' permissions**, deduplicated. Permissions come back directly in the login response so no extra call is needed.

---

## Endpoints Used

| Method | Endpoint | Purpose |
|---|---|---|
| `GET` | `api/v1/roles` | List all roles with their current permissions |
| `POST` | `api/v1/roles` | Create a new custom role |
| `GET` | `api/v1/roles/{id}/permissions` | All permissions grouped by module with enabled true/false |
| `PUT` | `api/v1/roles/{id}/permissions` | Sync a role's permissions (send enabled list, backend diffs) |
| `GET` | `api/v1/users` | List all users with their assigned roles |
| `GET` | `api/v1/users/{id}` | Get a single user |
| `POST` | `api/v1/users/{id}/roles` | Assign a role to a user |
| `DELETE` | `api/v1/users/{id}/roles/{roleName}` | Remove a role from a user |
| `POST` | `api/v1/users/{id}/activate` | Activate a suspended user |
| `POST` | `api/v1/users/{id}/suspend` | Suspend a user |
| `POST` | `api/v1/users/{id}/revoke-sessions` | Force logout all sessions |
| `POST` | `api/v1/users/{id}/reset-password` | Admin resets a user's password |

---

## Seeded Roles

| Role | Access |
|---|---|
| `SuperAdmin` | Everything |
| `OperationsManager` | All treks, fleet, customers, reports |
| `BranchManager` | Staff, vehicles, treks, customers for their branch |
| `FieldStaff` | Assigned treks, customer registration, visit capture |
| `Driver` | Assigned treks only |
| `CreditOfficer` | Customer KYC, credit assessment, ledger |
| `Auditor` | Read-only reports and audit events |

---

## Available Permissions

```
Staff.View              Staff.Manage
Roles.Manage            Branches.Manage
Vehicles.Manage         TrackingDevices.Manage
Treks.ViewAll           Treks.Create
Treks.Assign            Treks.Start            Treks.Complete
Customers.Register      Customers.Edit         Customers.Approve
CustomerKyc.View        CustomerKyc.Manage
CustomerCredit.View     CustomerCredit.Manage
Visits.Record           Visits.Verify
Tracking.ViewAll        Reports.Export         Audit.View
```

---

## 1. Create a Role

```http
POST /api/v1/roles
Authorization: Bearer <token>
Content-Type: application/json

{
  "name": "Pharmacist",
  "description": "Optional description",
  "permissions": ["Customers.Register", "Visits.Record"]
}
```

- `name` is required and must be unique
- `permissions` is optional — omit or pass `[]` and sync later via `PUT /api/v1/roles/{id}/permissions`
- New roles are marked `isSystem: false`

### Response `201 Created`
```json
{
  "id": "...",
  "name": "Pharmacist",
  "description": "Optional description",
  "permissions": ["Customers.Register", "Visits.Record"],
  "isSystem": false
}
```

- `422` — name already exists or an invalid permission key was supplied

---

## 2. List Roles

```http
GET /api/v1/roles
Authorization: Bearer <token>
```

Response:
```json
[
  {
    "id": "...",
    "name": "BranchManager",
    "description": "Staff, vehicles, treks and customers for assigned branches.",
    "permissions": ["Staff.View", "Staff.Manage", "Treks.Create", "..."],
    "isSystem": true
  }
]
```

---

## 3. Get Role Permissions (for toggle UI)

Returns every available permission grouped by module with `enabled: true/false` for the current role.

```http
GET /api/v1/roles/{id}/permissions
Authorization: Bearer <token>
```

Response:
```json
{
  "roleId": "...",
  "roleName": "BranchManager",
  "description": "Staff, vehicles, treks and customers for assigned branches.",
  "groups": [
    {
      "module": "Customers",
      "permissions": [
        { "key": "Customers.Register", "enabled": true },
        { "key": "Customers.Edit", "enabled": true },
        { "key": "Customers.Approve", "enabled": false }
      ]
    },
    {
      "module": "Staff",
      "permissions": [
        { "key": "Staff.View", "enabled": true },
        { "key": "Staff.Manage", "enabled": true }
      ]
    }
  ]
}
```

Use this to render a toggle UI — one switch per permission, pre-set to `enabled`.

---

## 4. Sync Role Permissions

Send the full list of **enabled** permission keys. The backend diffs — adds new ones, removes unchecked ones.

```http
PUT /api/v1/roles/{id}/permissions
Authorization: Bearer <token>
Content-Type: application/json

{
  "permissions": [
    "Customers.Register",
    "Customers.Edit",
    "Staff.View",
    "Treks.Create"
  ]
}
```

- `200` — returns updated role with final permissions list
- `422` — one or more permission keys are invalid
- `404` — role not found

### Frontend pattern

```ts
// 1. Load the role's permissions on page open
const { data } = await api.get(`/api/v1/roles/${roleId}/permissions`)

// 2. Track enabled permissions in local state
const [enabled, setEnabled] = useState<Set<string>>(
  new Set(
    data.groups.flatMap(g => g.permissions.filter(p => p.enabled).map(p => p.key))
  )
)

// 3. Toggle a permission
const toggle = (key: string) => {
  setEnabled(prev => {
    const next = new Set(prev)
    next.has(key) ? next.delete(key) : next.add(key)
    return next
  })
}

// 4. Save — send the full enabled set
const save = async () => {
  await api.put(`/api/v1/roles/${roleId}/permissions`, {
    permissions: [...enabled]
  })
}
```

---

## 5. Assigning Roles to a User

A user can hold multiple roles. Their effective permissions are the union of all assigned roles. Send multiple roles in a single request.

```http
POST /api/v1/users/{id}/roles
Authorization: Bearer <token>
Content-Type: application/json

{
  "roleNames": ["BranchManager", "CreditOfficer"]
}
```

Response — returns the user's full roles list after assignment:
```json
{
  "userId": "...",
  "roles": ["BranchManager", "CreditOfficer"]
}
```

- `200` — roles assigned (roles already assigned are silently skipped)
- `404` — user or one of the role names not found
- `422` — validation error (empty list)

---

## 6. Removing a Role from a User

```http
DELETE /api/v1/users/{id}/roles/{roleName}
Authorization: Bearer <token>
```

- `204` — role removed
- `404` — user or role not found

---

## 7. User Status Management

### Suspend
```http
POST /api/v1/users/{id}/suspend
Authorization: Bearer <token>
```

### Activate
```http
POST /api/v1/users/{id}/activate
Authorization: Bearer <token>
```

### Revoke all sessions (force logout)
```http
POST /api/v1/users/{id}/revoke-sessions
Authorization: Bearer <token>
```

### Reset password (admin-initiated)
```http
POST /api/v1/users/{id}/reset-password
Authorization: Bearer <token>
```

---

## 8. Get a User by ID

Returns the full combined profile — all staff details plus identity fields.

```http
GET /api/v1/users/{id}
Authorization: Bearer <token>
```

Response:
```json
{
  "userId": "...",
  "staffMemberId": "...",
  "employeeNumber": "EMP-2026-001",
  "firstName": "Kwame",
  "lastName": "Asante",
  "fullName": "Kwame Asante",
  "emailAddress": "k.asante@prohpharmacy.com",
  "phoneNumber": "+233201234567",
  "role": "Driver",
  "branchId": "...",
  "branchName": "Tema Branch",
  "employmentStatus": "Active",
  "joinedOn": "2026-09-08",
  "hasAppAccess": true,
  "isActive": true,
  "systemRoles": ["Driver"],
  "permissions": ["Treks.ViewAll", "Treks.Start", "Treks.Complete"],
  "profilePhotoUrl": "https://ik.imagekit.io/...",
  "currentDeviceId": "...",
  "currentDeviceName": "Device 001",
  "lastLoginAt": "2026-09-08T10:00:00Z",
  "createdAt": "2026-09-08T10:00:00Z",
  "updatedAt": null
}
```

> Note: `id` here is the **userId** (ApplicationUser ID), not the staffMemberId.

---

## 9. Frontend Permission Checks



Permissions come back in the **login response** — store them in your global auth store. Guard by permission, not role, for finer control.

```ts
// hooks/usePermissions.ts
export function usePermissions() {
  const { user } = useAuth() // user.permissions: string[]

  const can = (permission: string) =>
    user?.permissions?.includes(permission) ?? false

  return { can }
}
```

### Guarding routes by permission

```tsx
<Route
  path="/treks/create"
  element={
    <PermissionGuard permission="Treks.Create">
      <CreateTrekPage />
    </PermissionGuard>
  }
/>
```

```tsx
// components/PermissionGuard.tsx
export function PermissionGuard({ permission, children }: { permission: string, children: React.ReactNode }) {
  const { can } = usePermissions()
  return can(permission) ? <>{children}</> : <Navigate to="/unauthorized" />
}
```

### Hiding UI elements

```tsx
const { can } = usePermissions()

{can('Customers.Approve') && (
  <Button onClick={approveCustomer}>Approve</Button>
)}
```

---

## 10. UI Flow

```
Settings → Roles
  ├── Roles list (name, description, permission count)
  └── Role detail → permission toggle page
        ├── Grouped by module (Customers, Staff, Treks, ...)
        ├── Toggle switch per permission (pre-loaded from GET)
        └── Save button → PUT /roles/{id}/permissions

Settings → Users
  ├── Users list (name, email, roles badges, status)
  │     ├── Assign role (select from roles list → POST)
  │     ├── Remove role (× badge → DELETE)
  │     ├── Activate / Suspend toggle
  │     ├── Revoke sessions
  │     └── Reset password
```

---

## Implementation Checklist

- [ ] Roles list page
- [ ] Create role form (name, description, optional initial permissions) → `POST /api/v1/roles`
- [ ] Role permission toggle page (GET to load, local state for toggles, PUT to save)
- [ ] Users list page (paginated, searchable, shows role badges)
- [ ] Assign role to user (select dropdown → POST)
- [ ] Remove role from user (× on badge → DELETE)
- [ ] Activate / Suspend with confirmation
- [ ] Revoke sessions with confirmation
- [ ] Reset password action
- [ ] `usePermissions` hook with `can(permission)` helper
- [ ] `PermissionGuard` component for route-level protection
- [ ] Conditional UI rendering using `can('Permission.Key')`
