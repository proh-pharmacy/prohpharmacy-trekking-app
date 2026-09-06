# 03 — Roles & Permissions

## Overview

The system uses role-based access control (RBAC). Roles are seeded by the backend. Admins assign roles to users, and the frontend uses the authenticated user's roles (from `GET /auth/me`) to show or hide UI elements and guard routes.

---

## Endpoints Used

| Method | Endpoint | Purpose |
|---|---|---|
| `GET` | `api/v1/roles` | List all available roles |
| `GET` | `api/v1/users` | List all users with their roles |
| `GET` | `api/v1/users/{id}` | Get a single user |
| `POST` | `api/v1/users/{id}/roles` | Assign a role to a user |
| `DELETE` | `api/v1/users/{id}/roles/{roleName}` | Remove a role from a user |
| `POST` | `api/v1/users/{id}/activate` | Activate a suspended user |
| `POST` | `api/v1/users/{id}/suspend` | Suspend a user |
| `POST` | `api/v1/users/{id}/revoke-sessions` | Force logout all sessions |
| `POST` | `api/v1/users/{id}/reset-password` | Admin resets a user's password |

---

## 1. Listing Available Roles

Roles are seeded by the backend and don't change often. Fetch once and cache.

```http
GET /api/v1/roles
Authorization: Bearer <token>
```

Response:
```json
[
  { "name": "Admin" },
  { "name": "Manager" },
  { "name": "Staff" },
  { "name": "Driver" }
]
```

Use this to populate the role assignment dropdown.

---

## 2. Listing Users

```http
GET /api/v1/users
Authorization: Bearer <token>
```

Supports pagination and search. Each user object includes their current roles.

---

## 3. Assigning a Role

```http
POST /api/v1/users/{id}/roles
Authorization: Bearer <token>
Content-Type: application/json

{
  "roleName": "Manager"
}
```

- `200` — role assigned
- `422` — role already assigned or invalid role name

---

## 4. Removing a Role

```http
DELETE /api/v1/users/{id}/roles/{roleName}
Authorization: Bearer <token>
```

- `204` — role removed
- `404` — user or role not found

---

## 5. User Status Management

### Suspend a user
```http
POST /api/v1/users/{id}/suspend
Authorization: Bearer <token>
```

### Activate a user
```http
POST /api/v1/users/{id}/activate
Authorization: Bearer <token>
```

### Force logout (revoke all sessions)
```http
POST /api/v1/users/{id}/revoke-sessions
Authorization: Bearer <token>
```

### Reset password (admin)
```http
POST /api/v1/users/{id}/reset-password
Authorization: Bearer <token>
```

---

## 6. Frontend Permission Checks

The authenticated user's roles come from `GET /auth/me`. Store them in your global auth store and use them to conditionally render UI.

```ts
// hooks/usePermissions.ts
export function usePermissions() {
  const { user } = useAuth()

  const hasRole = (role: string) =>
    user?.roles?.includes(role) ?? false

  const isAdmin = hasRole('Admin')
  const isManager = hasRole('Manager') || isAdmin
  const isDriver = hasRole('Driver')

  return { hasRole, isAdmin, isManager, isDriver }
}
```

### Guarding routes

```tsx
// Only admins can access /settings
<Route
  path="/settings"
  element={
    <RoleGuard roles={['Admin']}>
      <SettingsPage />
    </RoleGuard>
  }
/>
```

```tsx
// components/RoleGuard.tsx
export function RoleGuard({ roles, children }: { roles: string[], children: React.ReactNode }) {
  const { hasRole } = usePermissions()
  const allowed = roles.some(hasRole)
  return allowed ? <>{children}</> : <Navigate to="/unauthorized" />
}
```

### Hiding UI elements

```tsx
const { isAdmin } = usePermissions()

{isAdmin && (
  <Button onClick={suspendUser}>Suspend User</Button>
)}
```

---

## UI Flow

```
Settings → Users & Roles
  ├── Users list (name, email, roles, status)
  │     ├── Assign / remove roles (multi-select dropdown)
  │     ├── Activate / Suspend toggle
  │     ├── Revoke sessions button
  │     └── Reset password button
  └── Roles list (read-only — seeded by backend)
```

---

## Implementation Checklist

- [ ] Fetch and cache roles list
- [ ] Users list page (paginated, searchable)
- [ ] Role assignment UI (add / remove roles per user)
- [ ] Activate / Suspend user action with confirmation
- [ ] Revoke sessions action with confirmation
- [ ] Reset password action
- [ ] `usePermissions` hook
- [ ] `RoleGuard` component for route-level protection
- [ ] Conditional UI rendering based on roles
