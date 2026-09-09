# 12 — Real-Time Fleet Tracking

## Overview

Live vehicle tracking is powered by two layers:

1. **Traccar → backend (webhook)** — Traccar POSTs a position update to your backend every time a device reports. The backend saves the latest position to the database and immediately broadcasts it over SignalR.
2. **Backend → frontend (SignalR WebSocket)** — The frontend holds a persistent WebSocket connection to `/hubs/tracking` and receives `PositionUpdated` events in real time.

For the initial map load, use the REST endpoint to fetch the last known position of all devices, then let SignalR keep them updated from that point on.

---

## Endpoints

| Method | Endpoint | Purpose |
|---|---|---|
| `GET` | `api/v1/fleet/positions` | Last known position for all devices |
| `GET` | `api/v1/fleet/devices/{id}/position` | Live position for a single device (direct Traccar call) |
| `GET` | `api/v1/fleet/devices/{id}/position/history` | Position history for a device over a time range |
| `WebSocket` | `/hubs/tracking` | SignalR hub — real-time position broadcasts |

---

## GET /api/v1/fleet/positions

Returns the last known position for every device that has reported in at least once. Data comes from the local database — no Traccar call is made. Use this to seed the map on load.

### Query parameters

| Parameter | Type | Description |
|---|---|---|
| `branchId` | `guid` | Filter to devices belonging to a specific branch |

### Response `200 OK`

```json
[
  {
    "deviceId": "...",
    "deviceName": "Kwame Asante - GR-1234-24",
    "staffMemberId": "...",
    "staffName": "Kwame Asante",
    "vehicleId": "...",
    "vehicleRegistration": "GR-1234-24",
    "vehicleDisplayName": "Sprinter Van 1",
    "branchId": "...",
    "branchName": "Tema Branch",
    "latitude": 5.6032,
    "longitude": -0.1869,
    "lastAddress": "Liberation Road, Accra",
    "lastReportedAt": "2026-09-09T08:30:00Z"
  }
]
```

---

## GET /api/v1/fleet/devices/{id}/position

Fetches the live current position directly from Traccar for a single device. Use this when you need the freshest position for one specific vehicle (e.g. a detail drawer/panel).

### Response `200 OK`

```json
{
  "deviceId": "...",
  "deviceName": "Kwame Asante - GR-1234-24",
  "staffMemberId": "...",
  "staffName": "Kwame Asante",
  "vehicleId": "...",
  "vehicleRegistration": "GR-1234-24",
  "latitude": 5.6032,
  "longitude": -0.1869,
  "speed": 42.5,
  "course": 270.0,
  "address": "Liberation Road, Accra",
  "ignition": true,
  "motion": true,
  "batteryLevel": 87.0,
  "fixTime": "2026-09-09T08:30:00Z",
  "valid": true
}
```

### Errors
- `404` — device not found or no position data available yet
- `422` — device is not registered in Traccar

---

## GET /api/v1/fleet/devices/{id}/position/history

Returns the full GPS trail for a device over a given time range. Use this to draw a route polyline on the map. Data comes directly from Traccar — maximum range is 31 days.

### Query parameters

| Parameter | Type | Required | Description |
|---|---|---|---|
| `from` | `DateTime` | Yes | Start of range (ISO 8601, UTC) |
| `to` | `DateTime` | Yes | End of range (ISO 8601, UTC) |

### Response `200 OK`

```json
[
  {
    "latitude": 5.6032,
    "longitude": -0.1869,
    "speed": 42.5,
    "course": 270.0,
    "address": "Liberation Road, Accra",
    "ignition": true,
    "motion": true,
    "batteryLevel": 87.0,
    "fixTime": "2026-09-09T08:30:00Z",
    "valid": true
  }
]
```

### Errors
- `404` — device not found
- `422` — `to` is before `from`, or range exceeds 31 days

---

## SignalR Hub — /hubs/tracking

### Connecting

Install the SignalR JS client:

```bash
npm install @microsoft/signalr
```

Connect with the JWT bearer token:

```js
import * as signalR from '@microsoft/signalr'

const connection = new signalR.HubConnectionBuilder()
    .withUrl('/hubs/tracking', {
        accessTokenFactory: () => localStorage.getItem('token')
    })
    .withAutomaticReconnect()
    .build()

await connection.start()
```

> **On connect**, the hub immediately pushes the last known position for every device that has reported in at least once — same `PositionUpdated` event as live updates. The frontend only needs one pipeline: connect, listen, done. No separate REST call needed to seed the map.

### Branch filtering (optional)

To receive updates only for devices belonging to a specific branch, join the branch group after connecting. You can join multiple branches.

```js
// Subscribe to a branch
await connection.invoke('JoinBranch', branchId)

// Unsubscribe from a branch
await connection.invoke('LeaveBranch', branchId)
```

If you do not join any branch, you receive updates for **all** devices.

### Listening for position updates

```js
connection.on('PositionUpdated', (position) => {
    console.log(position)
})
```

### `PositionUpdated` event shape

```json
{
  "deviceId": "...",
  "traccarDeviceId": 42,
  "staffMemberId": "...",
  "staffName": "Kwame Asante",
  "vehicleId": "...",
  "vehicleRegistration": "GR-1234-24",
  "branchId": "...",
  "branchName": "Tema Branch",
  "latitude": 5.6032,
  "longitude": -0.1869,
  "speed": 42.5,
  "course": 270.0,
  "fixTime": "2026-09-09T08:30:00Z",
  "valid": true,
  "ignition": true,
  "motion": true,
  "batteryLevel": 87.0,
  "address": "Liberation Road, Accra"
}
```

| Field | Description |
|---|---|
| `deviceId` | Your local device GUID — use this as the map marker key |
| `latitude` / `longitude` | Move the marker to these coordinates |
| `speed` | Speed in km/h |
| `course` | Bearing in degrees (0–360) — use to rotate a direction arrow icon |
| `ignition` | `true` = engine on, `false` = engine off, `null` = not reported |
| `motion` | `true` = moving, `false` = stationary, `null` = not reported |
| `batteryLevel` | Battery % (0–100), `null` if not reported |
| `address` | Human-readable address from Traccar's reverse geocoding — may be `null` |
| `valid` | `false` means the GPS fix was invalid — do not move the marker |

---

## Leaflet Integration

### Installation

```bash
npm install leaflet
npm install --save-dev @types/leaflet   # if using TypeScript
```

### Full example

```js
import L from 'leaflet'
import 'leaflet/dist/leaflet.css'
import * as signalR from '@microsoft/signalr'

// 1. Initialise map centred on Ghana
const map = L.map('map').setView([7.9465, -1.0232], 7)

L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
    attribution: '© OpenStreetMap contributors'
}).addTo(map)

const markers = {}  // keyed by deviceId

// 2. Connect SignalR — hub pushes last known positions immediately on connect,
//    then continues streaming live updates. No separate REST call needed.
const token = localStorage.getItem('token')

const connection = new signalR.HubConnectionBuilder()
    .withUrl('/hubs/tracking', { accessTokenFactory: () => token })
    .withAutomaticReconnect()
    .build()

connection.on('PositionUpdated', (p) => {
    if (!p.valid) return  // skip bad GPS fixes

    if (markers[p.deviceId]) {
        // Update existing marker
        markers[p.deviceId]
            .setLatLng([p.latitude, p.longitude])
            .setPopupContent(`
                <strong>${p.staffName ?? 'Unassigned'}</strong><br>
                ${p.vehicleRegistration ?? ''}<br>
                ${p.speed ? `${p.speed.toFixed(1)} km/h · ` : ''}
                ${p.ignition ? 'Engine on' : 'Engine off'}<br>
                <small>${p.address ?? ''}</small>
            `)
    } else {
        // New device appearing for the first time
        markers[p.deviceId] = L.marker([p.latitude, p.longitude])
            .addTo(map)
            .bindPopup(`<strong>${p.staffName ?? 'Unassigned'}</strong>`)
    }
})

await connection.start()  // triggers immediate push of all stored positions
```

### Marker colour by status

Use `L.divIcon` to render coloured markers based on ignition/motion state:

```js
function getMarkerIcon(ignition, motion) {
    const colour = ignition && motion ? '#00bf6f'   // moving — green
                 : ignition          ? '#f59e0b'   // idling — amber
                 :                    '#94a3b8'   // off — grey

    return L.divIcon({
        className: '',
        html: `<div style="
            width:14px; height:14px; border-radius:50%;
            background:${colour}; border:2px solid #fff;
            box-shadow:0 1px 3px rgba(0,0,0,.4)">
        </div>`,
        iconSize: [14, 14],
        iconAnchor: [7, 7]
    })
}

// In the PositionUpdated handler:
markers[p.deviceId].setIcon(getMarkerIcon(p.ignition, p.motion))
```

### Drawing a route trail (position history)

```js
async function showTrail(deviceId, from, to) {
    const res = await fetch(
        `/api/v1/fleet/devices/${deviceId}/position/history?from=${from.toISOString()}&to=${to.toISOString()}`,
        { headers: { Authorization: `Bearer ${token}` } }
    )
    const points = await res.json()

    const latlngs = points.filter(p => p.valid).map(p => [p.latitude, p.longitude])
    L.polyline(latlngs, { color: '#00bf6f', weight: 3 }).addTo(map)
}
```

---

## Update Frequency

Position updates arrive as frequently as the GPS device reports to Traccar:

| Device type | Typical interval |
|---|---|
| Hardware GPS tracker | Every 30 s – 2 min (configurable in Traccar) |
| Smartphone app | Depends on the Traccar Client app settings |

The webhook fires immediately when Traccar receives each report — there is no additional delay on the backend side.

---

## Traccar Webhook Configuration

In your Traccar admin panel, add a webhook pointing to:

```
https://yourdomain.com/api/traccar/webhook?secret=YOUR_WEBHOOK_SECRET
```

The secret must match `TraccarSettings:WebhookSecret` in your `appsettings.json`. The endpoint will return `401` and ignore the payload if the secret is missing or wrong.
