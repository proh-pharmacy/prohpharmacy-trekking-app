# 06 — Forgot Password / Password Reset

## Overview

Self-service password reset for users who cannot log in. The user submits their email, receives a reset link, clicks it, and sets a new password.

> **Backend note:** These two endpoints do not exist yet and need to be built before this flow can be implemented.
> - `POST api/v1/auth/forgot-password`
> - `POST api/v1/auth/reset-password`

---

## Endpoints Used

| Method | Endpoint | Auth | Purpose |
|---|---|---|---|
| `POST` | `api/v1/auth/forgot-password` | Anonymous | Request a password reset email |
| `POST` | `api/v1/auth/reset-password` | Anonymous | Submit new password using the reset token |

---

## Flow

```
/forgot-password page
  └── User enters email → POST /auth/forgot-password
        └── Backend sends reset email with link
              └── User clicks link → /reset-password?token=<token>
                    └── User sets new password → POST /auth/reset-password
                          └── Redirect to /login
```

---

## 1. Request a Reset Email

```http
POST /api/v1/auth/forgot-password
Content-Type: application/json

{
  "email": "k.asante@prohpharmacy.com"
}
```

- `200` — always return success (do not reveal whether the email exists)
- `422` — invalid email format

### Frontend behaviour
- Show a generic success message regardless of outcome: **"If that email is registered, a reset link has been sent."**
- Disable the submit button after the first attempt to prevent spamming.

---

## 2. Reset the Password

When the user clicks the link in their email, the frontend reads `?token=` from the URL and presents a set-new-password form.

```http
POST /api/v1/auth/reset-password
Content-Type: application/json

{
  "token": "<token-from-url>",
  "newPassword": "theirNewPassword",
  "confirmPassword": "theirNewPassword"
}
```

- `200` — password updated, redirect to `/login`
- `422` — token expired, already used, or passwords don't match

### Token expiry
Tokens should expire after **1 hour**. If the token is invalid or expired, show: **"This link has expired or already been used. Request a new one."** with a link back to `/forgot-password`.

---

## UI Flow

```
/login page
  └── "Forgot password?" link → /forgot-password

/forgot-password
  └── Email input + Submit button
        └── On submit → POST /auth/forgot-password
              └── Show: "If that email is registered, a reset link has been sent."

/reset-password?token=<token>
  └── New password + Confirm password fields
        └── On submit → POST /auth/reset-password
              ├── On success → redirect to /login with toast "Password updated — please log in."
              └── On failure (expired/invalid) → show error + link back to /forgot-password
```

---

## Implementation Checklist

- [ ] *(Backend)* `POST /api/v1/auth/forgot-password` endpoint
- [ ] *(Backend)* `POST /api/v1/auth/reset-password` endpoint
- [ ] *(Backend)* Reset email template
- [ ] "Forgot password?" link on the login page
- [ ] `/forgot-password` page — email form, generic success message
- [ ] `/reset-password` page — reads `?token` from URL, new password form
- [ ] Expired/invalid token error state with link back to `/forgot-password`
- [ ] Redirect to `/login` with success toast on completion
