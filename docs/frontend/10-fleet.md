# 10 — Fleet

## Endpoints

| Method | Endpoint | Purpose |
|---|---|---|
| `POST` | `api/v1/fleet/vehicles` | Register a vehicle |
| `GET` | `api/v1/fleet/vehicles` | List vehicles (paginated, filterable) |
| `GET` | `api/v1/fleet/vehicles/{id}` | Get a single vehicle |
| `PUT` | `api/v1/fleet/vehicles/{id}` | Update vehicle details |
| `PATCH` | `api/v1/fleet/vehicles/{id}/status` | Change operational status |
| `POST` | `api/v1/fleet/vehicles/{vehicleId}/assign-staff` | Assign vehicle to a staff member |
| `POST` | `api/v1/fleet/vehicles/{vehicleId}/unassign-staff` | Unassign vehicle from current staff |
| `POST` | `api/v1/fleet/devices` | Register a tracking device for a vehicle |
| `GET` | `api/v1/fleet/devices` | List tracking devices (paginated) |
| `GET` | `api/v1/fleet/devices/{id}` | Get a single tracking device |
| `PUT` | `api/v1/fleet/devices/{id}` | Update tracking device details |
| `DELETE` | `api/v1/fleet/devices/{id}` | Delete a tracking device |
| `POST` | `api/v1/fleet/devices/sync` | Sync devices to Traccar |
| `GET` | `api/v1/fleet/drivers` | List registered fleet drivers |
| `POST` | `api/v1/fleet/drivers` | Register a staff member as a fleet driver |
| `DELETE` | `api/v1/fleet/drivers/{staffMemberId}` | Remove a fleet driver |
| `POST` | `api/v1/fleet/drivers/sync` | Sync fleet drivers to Traccar |
| `GET` | `api/v1/fleet/traccar-users` | List all Traccar users |
| `POST` | `api/v1/fleet/traccar-users` | Create a Traccar user |
| `PUT` | `api/v1/fleet/traccar-users/{traccarUserId}` | Update a Traccar user |
| `DELETE` | `api/v1/fleet/traccar-users/{traccarUserId}` | Delete a Traccar user |

---

## Enum Reference

### `operationalStatus`
`Active` `UnderMaintenance` `Decommissioned`

### `deviceStatus`
`Active` `Inactive`

> Always send enum values as strings, not integers.

---

## Shared Response Shape — `VehicleResponse`

```json
{
  "id": "...",
  "registrationNumber": "GR-1234-24",
  "displayName": "Sprinter Van 1",
  "make": "Mercedes-Benz",
  "model": "Sprinter",
  "year": 2022,
  "colour": "White",
  "branchId": "...",
  "branchName": "Tema Branch",
  "operationalStatus": "Active",
  "currentStaffId": "...",
  "currentStaffName": "Kwame Asante",
  "createdAt": "2026-09-09T10:00:00Z",
  "updatedAt": null
}
```

---

## POST /api/v1/fleet/vehicles

### Request body

```json
{
  "registrationNumber": "GR-1234-24",
  "displayName": "Sprinter Van 1",
  "make": "Mercedes-Benz",
  "model": "Sprinter",
  "year": 2022,
  "colour": "White",
  "branchId": "<branch-guid>"
}
```

| Field | Required | Constraints |
|---|---|---|
| `registrationNumber` | Yes | Max 30 chars — stored uppercase, cannot be changed after creation |
| `displayName` | Yes | Max 80 chars |
| `make` | Yes | Max 80 chars |
| `model` | Yes | Max 80 chars |
| `year` | Yes | 1990 to current year + 1 |
| `colour` | Yes | Max 50 chars |
| `branchId` | No | Must be an active branch if provided |

### Response `201 Created` — `VehicleResponse`

### Errors
- `422` — validation error or branch not found / inactive

---

## GET /api/v1/fleet/vehicles

### Query parameters

| Parameter | Type | Description |
|---|---|---|
| `search` | `string` | Search in registration number, display name, make, model |
| `sort` | `string` | e.g. `createdAt_desc`, `displayName_asc` |
| `pageNumber` | `int` | Default: 1 |
| `pageSize` | `int` | Default: 20 |
| `branchId` | `guid` | Filter by branch |
| `status` | `string` | e.g. `Active`, `UnderMaintenance` |

### Response `200 OK` — `PaginatedData<VehicleResponse>`

---

## GET /api/v1/fleet/vehicles/{id}

### Response `200 OK` — `VehicleResponse`

### Errors
- `404` — vehicle not found

---

## PUT /api/v1/fleet/vehicles/{id}

Registration number cannot be changed after creation.

### Request body

```json
{
  "displayName": "Sprinter Van 1",
  "make": "Mercedes-Benz",
  "model": "Sprinter",
  "year": 2022,
  "colour": "Silver",
  "branchId": "<branch-guid>"
}
```

| Field | Required | Constraints |
|---|---|---|
| `displayName` | Yes | Max 80 chars |
| `make` | Yes | Max 80 chars |
| `model` | Yes | Max 80 chars |
| `year` | Yes | 1990 to current year + 1 |
| `colour` | Yes | Max 50 chars |
| `branchId` | No | Must be an active branch if provided |

### Response `200 OK` — `VehicleResponse`

### Errors
- `404` — vehicle not found
- `422` — validation error

---

## PATCH /api/v1/fleet/vehicles/{id}/status

### Request body

```json
{
  "status": "UnderMaintenance"
}
```

### Response `200 OK`

```json
{
  "vehicleId": "...",
  "operationalStatus": "UnderMaintenance"
}
```

### Errors
- `404` — vehicle not found
- `422` — validation error

---

## POST /api/v1/fleet/vehicles/{vehicleId}/assign-staff

The vehicle must be `Active`. A vehicle can only have one active assignment at a time. If the vehicle has a registered tracking device, its name is updated to `"{StaffName} - {RegistrationNumber}"` and the Traccar driver link is set automatically.

### Request body

```json
{
  "staffMemberId": "<staff-guid>",
  "notes": "Assigned for Q3 routes"
}
```

| Field | Required | Constraints |
|---|---|---|
| `staffMemberId` | Yes | The `id` from the staff record (not the user/account ID) |
| `notes` | No | Max 500 chars |

### Response `200 OK`

```json
{
  "assignmentId": "...",
  "vehicleId": "...",
  "vehicleRegistration": "GR-1234-24",
  "staffMemberId": "...",
  "staffName": "Kwame Asante",
  "assignedAt": "2026-09-09T10:00:00Z",
  "notes": "Assigned for Q3 routes"
}
```

### Errors
- `404` — vehicle or staff not found
- `422` — vehicle already assigned or vehicle not active

---

## POST /api/v1/fleet/vehicles/{vehicleId}/unassign-staff

Ends the active vehicle assignment. If the vehicle has a registered tracking device, its name reverts to the registration number and the Traccar driver link is removed automatically.

### Response `204 No Content`

### Errors
- `404` — vehicle not found or no active assignment
- `422` — validation error

---

## Shared Response Shape — `DeviceResponse`

```json
{
  "id": "...",
  "traccarDeviceId": 42,
  "traccarUniqueId": "860123456789012",
  "name": "Kwame Asante - GR-1234-24",
  "phoneNumber": "+233201234567",
  "status": "Active",
  "vehicleId": "...",
  "vehicleRegistration": "GR-1234-24",
  "staffMemberId": "...",
  "staffName": "Kwame Asante",
  "lastReportedAt": "2026-09-09T08:30:00Z",
  "lastLatitude": 5.6032,
  "lastLongitude": -0.1869,
  "lastAddress": "Liberation Road, Accra",
  "createdAt": "2026-09-09T10:00:00Z",
  "updatedAt": null
}
```

`staffMemberId` and `staffName` reflect the vehicle's currently assigned staff member. They are `null` if the vehicle has no active assignment.

---

## POST /api/v1/fleet/devices

Registers a GPS tracking device for a vehicle and immediately syncs to Traccar.

- For **smartphones**: omit `uniqueId` — a UUID is auto-generated.
- For **hardware GPS trackers**: provide the IMEI as `uniqueId`.

### Request body

```json
{
  "vehicleId": "<vehicle-guid>",
  "uniqueId": "860123456789012",
  "phoneNumber": "+233201234567"
}
```

| Field | Required | Constraints |
|---|---|---|
| `vehicleId` | Yes | The vehicle to attach this device to — one device per vehicle |
| `uniqueId` | No | Max 50 chars — auto-generated UUID if omitted |
| `phoneNumber` | No | Max 30 chars |

### Notes
- `name` is auto-set to `"{StaffName} - {RegistrationNumber}"` if the vehicle has an assigned driver, otherwise just `"{RegistrationNumber}"`
- `staffMemberId` is auto-populated from the vehicle's current active staff assignment
- `traccarDeviceId` is populated after Traccar registration (may be `null` if Traccar is unreachable at creation time)

### Response `201 Created` — `DeviceResponse`

### Errors
- `422` — validation error, vehicle not found, or vehicle already has a device

---

## GET /api/v1/fleet/devices

### Query parameters

| Parameter | Type | Description |
|---|---|---|
| `search` | `string` | Search in name, unique ID, phone number |
| `sort` | `string` | e.g. `createdAt_desc` |
| `pageNumber` | `int` | Default: 1 |
| `pageSize` | `int` | Default: 20 |
| `status` | `string` | `Active` or `Inactive` |

### Response `200 OK` — `PaginatedData<DeviceResponse>`

---

## GET /api/v1/fleet/devices/{id}

### Response `200 OK` — `DeviceResponse`

### Errors
- `404` — device not found

---

## PUT /api/v1/fleet/devices/{id}

Unique ID (IMEI) cannot be changed after creation.

### Request body

```json
{
  "name": "Kwame Asante - Van 1",
  "phoneNumber": "+233201234567",
  "traccarDeviceId": 42
}
```

| Field | Required | Constraints |
|---|---|---|
| `name` | Yes | Max 100 chars |
| `phoneNumber` | No | Max 30 chars |
| `traccarDeviceId` | No | Traccar numeric device ID |

### Response `200 OK` — `DeviceResponse`

### Errors
- `404` — device not found
- `422` — validation error

---

## DELETE /api/v1/fleet/devices/{id}

Removes the device from Traccar and deletes it from the local database.

### Response `204 No Content`

### Errors
- `404` — device not found
- `422` — Traccar delete failed (device is still removed locally)

---

## POST /api/v1/fleet/devices/sync

Syncs local devices to Traccar. With `force=false` (default), only devices missing a Traccar ID are created. With `force=true`, performs a full reconciliation including deletions.

### Query parameters

| Parameter | Type | Description |
|---|---|---|
| `force` | `bool` | Default: `false` |

### Response `200 OK`

```json
{
  "synced": 3,
  "alreadySynced": 12,
  "failed": 0,
  "deleted": 1,
  "errors": []
}
```

---

## Fleet Drivers

Fleet drivers are staff members explicitly registered as drivers. They are stored locally first and synced to Traccar separately. `staffMemberId` is the `id` from the staff record (not the user/account ID).

## Shared Response Shape — `FleetDriverResponse`

```json
{
  "id": "...",
  "staffMemberId": "...",
  "staffName": "Kwame Asante",
  "phoneNumber": "+233201234567",
  "branchName": "Tema Branch",
  "traccarDriverId": 7,
  "traccarUniqueId": "abc123def456",
  "isSynced": true,
  "createdAt": "2026-09-09T10:00:00Z",
  "updatedAt": null
}
```

`traccarDriverId` and `traccarUniqueId` are `null` until the driver has been synced with Traccar. `isSynced` is `true` when `traccarDriverId` is set.

---

## GET /api/v1/fleet/drivers

### Query parameters

| Parameter | Type | Description |
|---|---|---|
| `synced` | `bool` | `true` — only synced drivers. `false` — only unsynced. Omit for all. |

### Response `200 OK` — `List<FleetDriverResponse>`

---

## POST /api/v1/fleet/drivers

Adds a staff member to the local fleet drivers list and immediately syncs them to Traccar. `traccarDriverId` will be populated in the response if sync succeeded.

### Request body

```json
{
  "staffMemberId": "<staff-guid>"
}
```

| Field | Required | Constraints |
|---|---|---|
| `staffMemberId` | Yes | The `id` from the staff record (not the user/account ID) |

### Response `201 Created` — `FleetDriverResponse`

### Errors
- `404` — staff member not found
- `422` — staff member is already a registered fleet driver

---

## DELETE /api/v1/fleet/drivers/{staffMemberId}

Removes the driver from the local list and deletes them from Traccar automatically if they were synced.

### Response `204 No Content`

### Errors
- `404` — fleet driver not found

---

## POST /api/v1/fleet/drivers/sync

Syncs all registered fleet drivers to Traccar. Re-creates any driver whose Traccar entry is missing. With `force=true`, also deletes Traccar driver entries that have no matching local fleet driver.

### Query parameters

| Parameter | Type | Description |
|---|---|---|
| `force` | `bool` | Default: `false` |

### Response `200 OK`

```json
{
  "synced": 5,
  "alreadySynced": 8,
  "failed": 0,
  "deleted": 2,
  "errors": []
}
```

---

## Traccar Users

Traccar users can log directly into the Traccar web interface. All data is stored in Traccar only — there is no local copy. IDs are Traccar's numeric IDs.

## Shared Response Shape — `TraccarUserResponse`

```json
{
  "id": 7,
  "name": "Kwame Asante",
  "email": "kwame@example.com",
  "administrator": false,
  "disabled": false,
  "deviceLimit": 0,
  "expirationTime": null
}
```

---

## GET /api/v1/fleet/traccar-users

### Response `200 OK` — `List<TraccarUserResponse>`

---

## POST /api/v1/fleet/traccar-users

### Request body

```json
{
  "name": "Kwame Asante",
  "email": "kwame@example.com",
  "password": "securepass",
  "administrator": false
}
```

| Field | Required | Constraints |
|---|---|---|
| `name` | Yes | Max 100 chars |
| `email` | Yes | Valid email, max 200 chars |
| `password` | Yes | Min 6 chars |
| `administrator` | No | Default: `false` |

### Response `201 Created` — `TraccarUserResponse`

### Errors
- `422` — validation error or Traccar rejected the request

---

## PUT /api/v1/fleet/traccar-users/{traccarUserId}

### Request body

```json
{
  "name": "Kwame Asante",
  "email": "kwame@example.com",
  "password": null,
  "administrator": false,
  "disabled": false
}
```

| Field | Required | Constraints |
|---|---|---|
| `name` | Yes | Max 100 chars |
| `email` | Yes | Valid email, max 200 chars |
| `administrator` | Yes | |
| `disabled` | Yes | |
| `password` | No | Min 6 chars — omit to leave password unchanged |

### Response `200 OK` — `TraccarUserResponse`

### Errors
- `404` — Traccar user not found
- `422` — validation error

---

## DELETE /api/v1/fleet/traccar-users/{traccarUserId}

### Response `204 No Content`

### Errors
- `404` — Traccar user not found
