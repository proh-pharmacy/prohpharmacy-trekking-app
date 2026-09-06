# 01 — Login Implementation

## Overview

Covers authentication flow end-to-end: login, token storage, silent refresh, protected routes, current user loading, logout, and password change.

---

## Endpoints Used

| Method | Endpoint | Auth | Purpose |
|---|---|---|---|
| `POST` | `api/v1/auth/login` | Anonymous | Exchange credentials for tokens |
| `POST` | `api/v1/auth/refresh` | Anonymous | Get a new access token using refresh token |
| `GET` | `api/v1/auth/me` | Required | Load the authenticated user's profile |
| `POST` | `api/v1/auth/logout` | Required | Invalidate the current session |
| `POST` | `api/v1/auth/change-password` | Required | Change authenticated user's password |

---

## 1. Login

### Request
```http
POST /api/v1/auth/login
Content-Type: application/json

{
  "email": "admin@prohpharmacy.com",
  "password": "yourpassword"
}
```

### Response
```json
{
  "accessToken": "eyJ...",
  "refreshToken": "eyJ...",
  "expiresIn": 3200
}
```

### What to do on success
1. Store `accessToken` in memory (a module-level variable or Zustand/Redux store — **not** localStorage).
2. Store `refreshToken` in an `httpOnly` cookie **or** localStorage (less secure but simpler). Choose based on your security requirements.
3. Call `GET /api/v1/auth/me` to load the user profile and store it globally.
4. Redirect to the dashboard.

### What to do on failure
- `401` / `422` — show "Invalid email or password."
- Do not reveal which field was wrong.

---

## 2. Token Storage Strategy

| Storage | Access Token | Refresh Token |
|---|---|---|
| Recommended | In-memory (JS variable / store) | `httpOnly` cookie via server proxy |
| Simpler (less secure) | `localStorage` | `localStorage` |

> If you use localStorage, both tokens are accessible to JavaScript and vulnerable to XSS. Use in-memory for the access token at minimum.

---

## 3. Axios / Fetch Setup

Set up a central API client that automatically attaches the access token to every request.

```ts
// lib/api.ts
import axios from 'axios'

const api = axios.create({
  baseURL: process.env.NEXT_PUBLIC_API_URL,
})

// Attach access token to every request
api.interceptors.request.use((config) => {
  const token = getAccessToken() // from your store
  if (token) config.headers.Authorization = `Bearer ${token}`
  return config
})

export default api
```

---

## 4. Silent Token Refresh

The access token expires in `expiresIn` seconds (currently `3200` — ~53 min). Set up a response interceptor to automatically refresh it when a `401` is returned.

```ts
// lib/api.ts (continued)

let isRefreshing = false
let failedQueue: any[] = []

const processQueue = (error: any, token: string | null) => {
  failedQueue.forEach((p) => (error ? p.reject(error) : p.resolve(token)))
  failedQueue = []
}

api.interceptors.response.use(
  (res) => res,
  async (error) => {
    const original = error.config

    if (error.response?.status === 401 && !original._retry) {
      if (isRefreshing) {
        return new Promise((resolve, reject) => {
          failedQueue.push({
            resolve: (token: string) => {
              original.headers.Authorization = `Bearer ${token}`
              resolve(api(original))
            },
            reject,
          })
        })
      }

      original._retry = true
      isRefreshing = true

      try {
        const refreshToken = getRefreshToken() // from storage
        const { data } = await axios.post(`${process.env.NEXT_PUBLIC_API_URL}/api/v1/auth/refresh`, {
          refreshToken,
        })
        setAccessToken(data.accessToken)   // update in-memory store
        setRefreshToken(data.refreshToken) // update storage
        original.headers.Authorization = `Bearer ${data.accessToken}`
        processQueue(null, data.accessToken)
        return api(original)
      } catch (err) {
        processQueue(err, null)
        clearAuth()     // wipe tokens
        redirectToLogin()
        return Promise.reject(err)
      } finally {
        isRefreshing = false
      }
    }

    return Promise.reject(error)
  }
)
```

### Refresh endpoint
```http
POST /api/v1/auth/refresh
Content-Type: application/json

{
  "refreshToken": "eyJ..."
}
```

Response is the same shape as login — new `accessToken` and `refreshToken`.

---

## 5. Loading the Current User

After login (or on app boot if a refresh token exists), load the authenticated user:

```http
GET /api/v1/auth/me
Authorization: Bearer <accessToken>
```

Store the response globally (Zustand, Redux, Context). This gives you the user's roles, branch, and staff details needed for permission checks throughout the app.

---

## 6. Protected Routes

Wrap all authenticated pages in a guard that:
1. Checks if an access token exists in memory.
2. If not, attempts a silent refresh using the stored refresh token.
3. If refresh fails, redirects to `/login`.

```tsx
// components/AuthGuard.tsx
export function AuthGuard({ children }: { children: React.ReactNode }) {
  const { user, isLoading } = useAuth()

  if (isLoading) return <FullPageSpinner />
  if (!user) return <Navigate to="/login" />

  return <>{children}</>
}
```

---

## 7. Logout

```http
POST /api/v1/auth/logout
Authorization: Bearer <accessToken>
```

On success (or even on failure):
1. Clear the access token from memory.
2. Clear the refresh token from storage.
3. Clear the user from the global store.
4. Redirect to `/login`.

---

## 8. Change Password

Available from the user's profile / settings page.

```http
POST /api/v1/auth/change-password
Authorization: Bearer <accessToken>
Content-Type: application/json

{
  "currentPassword": "oldpass",
  "newPassword": "newpass"
}
```

- `422` — validation error (e.g. current password wrong, new password too weak)
- `200` — success, optionally force re-login

---

## Implementation Checklist

- [ ] Login form (email + password, loading state, error message)
- [ ] Token storage (access token in memory, refresh token in storage)
- [ ] Axios/fetch client with `Authorization` header interceptor
- [ ] Silent refresh interceptor with queuing
- [ ] `GET /auth/me` call on boot + after login
- [ ] Global auth store (user, tokens, isLoading)
- [ ] Protected route wrapper
- [ ] Logout action (clear store + redirect)
- [ ] Change password form
