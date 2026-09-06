# Traccar Integration

## Overview

Traccar is an open-source GPS tracking platform deployed at `https://tracking.prohpharmacy.com`. It runs inside a Docker container on the VPS (`cloud.prohpharmacy.com`) and handles raw GPS data from devices. Our API integrates with Traccar to register devices and drivers, receive position updates, and broadcast them in real time.

---

## How It Works — Full Flow

```
Phone (Traccar Client app)
        │  sends GPS over TCP port 5055
        ▼
Traccar Server (tracking.prohpharmacy.com)
        │  forwards every position as JSON via HTTP
        ▼
Our API — POST /api/traccar/webhook
        │  updates LastLatitude/LastLongitude in DB
        │  broadcasts via SignalR to connected frontends
        ▼
Frontend (SignalR "PositionUpdated" event)
```

---

## Traccar Concepts vs Our Concepts

| Traccar | Our App | Notes |
|---|---|---|
| **Device** | `TrackingDevice` | The GPS hardware / phone running Traccar Client |
| **Driver** | `StaffMember` | The person; has attributes: phone, branch, role |
| **UniqueId** | `TrackingDevice.TraccarUniqueId` | UUID we generate and store; configured in Traccar Client app on the phone |
| **Device ID** (numeric) | `TrackingDevice.TraccarDeviceId` | Assigned by Traccar after device registration |
| **Driver ID** (numeric) | `StaffMember.TraccarDriverId` | Assigned by Traccar after driver registration |

---

## Device Setup Flow

1. Call `POST /api/v1/fleet/devices` with a `staffMemberId`
2. API auto-generates a `TraccarUniqueId` (UUID) and registers the device in Traccar
3. Traccar assigns a numeric `TraccarDeviceId` — stored on the `TrackingDevice` record
4. The staff member opens **Traccar Client** on their phone and pastes the `traccarUniqueId` into the **Device Identifier** field
5. Traccar Client begins sending GPS positions to `tracking.prohpharmacy.com:5055`

---

## Driver Setup Flow

1. Call `POST /api/v1/fleet/drivers` with a `staffMemberId`
2. API creates a Traccar driver with attributes: `phone`, `branch`, `role`, `employeeNumber`
3. Traccar assigns a numeric `TraccarDriverId` — stored on the `StaffMember` record
4. When the staff member is assigned a device (`POST /api/v1/fleet/devices/{id}/assign`), the driver is linked to the device in Traccar via `POST /api/permissions` so Traccar's UI shows who is driving
5. On unassign, the link is removed via `DELETE /api/permissions`

---

## Sync Endpoints

Used when the Traccar VPS is wiped and rebuilt (new instance, no data).

| Endpoint | Description |
|---|---|
| `POST /api/v1/fleet/devices/sync` | Light sync — creates Traccar entries for devices missing one |
| `POST /api/v1/fleet/devices/sync?force=true` | Full reconciliation — re-links by UniqueId, creates missing, deletes orphans |
| `POST /api/v1/fleet/drivers/sync` | Light sync — creates Traccar drivers for staff missing one |
| `POST /api/v1/fleet/drivers/sync?force=true` | Full reconciliation — verifies stored IDs still exist, re-creates missing, deletes orphans |

**Force sync is safe** — devices are matched by `TraccarUniqueId` so existing Traccar entries are re-linked, not deleted and re-created.

---

## Position Webhook

### Configuration

Set in `/opt/traccar/conf/traccar.xml` inside the Docker container:

```xml
<entry key='forward.url'>https://YOUR_API_URL/api/traccar/webhook?secret=YOUR_SECRET</entry>
<entry key='forward.type'>json</entry>
<entry key='forward.retry.enable'>true</entry>
```

After editing, restart the container:
```bash
sudo docker restart traccar
```

### Security

The webhook URL includes a `?secret=` query parameter. The API checks this against `TraccarSettings:WebhookSecret` in `appsettings.json`. Requests with a wrong or missing secret receive `401 Unauthorized`.

### Payload Format

Traccar sends this JSON body on every position update:

```json
{
  "event": { "id": 1, "type": "deviceMoving", "deviceId": 8, "eventTime": "..." },
  "position": {
    "deviceId": 8,
    "valid": true,
    "latitude": 5.6037,
    "longitude": -0.1870,
    "speed": 12.5,
    "course": 180,
    "fixTime": "...",
    "attributes": { "ignition": true, "motion": true, "batteryLevel": 87.0 }
  },
  "device": { "id": 8, "name": "Admin Admin", "uniqueId": "abc123..." }
}
```

### What the Webhook Does

1. Validates the `?secret=` param
2. Looks up the `TrackingDevice` by `TraccarDeviceId`
3. Updates `LastLatitude`, `LastLongitude`, `LastReportedAt`
4. Resolves the assigned staff member and their assigned vehicle
5. Broadcasts a `PositionUpdated` event via SignalR to:
   - `branch-{branchId}` group (branch-filtered clients)
   - All connected clients

---

## Position Endpoints

| Endpoint | Description |
|---|---|
| `GET /api/v1/fleet/devices/{id}/position` | Live position from Traccar for a specific device |
| `GET /api/v1/fleet/positions` | All devices with a known position (from DB cache). Optional `?branchId=` filter |
| `GET /api/v1/fleet/devices/{id}/position/history?from=&to=` | Position history from Traccar. Max 31-day range |

`GET /api/v1/fleet/positions` uses cached `LastLatitude/LastLongitude` from the DB (no Traccar call) — use this for map overviews. `GET .../position` fetches live from Traccar — use this for a single device detail view.

---

## SignalR

Hub URL: `/hubs/tracking`

Clients join a branch group to receive only their branch's positions:
```js
connection.invoke("JoinBranch", branchId);
```

Event name: `PositionUpdated`

Payload:
```json
{
  "deviceId": "uuid",
  "traccarDeviceId": 8,
  "staffMemberId": "uuid",
  "staffName": "John Doe",
  "vehicleId": "uuid",
  "vehicleRegistration": "GR-1234-24",
  "branchId": "uuid",
  "branchName": "Accra Branch",
  "latitude": 5.6037,
  "longitude": -0.1870,
  "speed": 12.5,
  "course": 180,
  "fixTime": "2026-09-06T00:00:00Z",
  "valid": true,
  "ignition": true,
  "motion": true,
  "batteryLevel": 87.0,
  "address": "Ring Road, Accra"
}
```

---

## VPS Details

| Item | Value |
|---|---|
| SSH host | `cloud.prohpharmacy.com` |
| SSH user | `sakoe` |
| Traccar URL | `https://tracking.prohpharmacy.com` |
| Container name | `traccar` |
| Config file | `/opt/traccar/conf/traccar.xml` (inside container) |
| Deployed via | Dokploy (`traccar_compose`) |

### Useful Commands

```bash
# View Traccar logs
sudo docker logs traccar --tail 100 -f

# Edit config
sudo docker exec -it traccar nano /opt/traccar/conf/traccar.xml

# Restart after config change
sudo docker restart traccar

# Check container status
sudo docker ps | grep traccar
```
