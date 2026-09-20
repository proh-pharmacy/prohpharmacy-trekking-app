# 19 — Live Tracking (`/portal/tracking`)

## Overview

The tracking page connects **directly** to Traccar's WebSocket API to receive real-time vehicle positions. No backend SignalR is used for this page. Device metadata (vehicle registration, staff name, branch) is fetched once from the backend on load and merged with the live position stream.

All endpoints below were tested live against `https://tracking.prohpharmacy.com` — the shapes and field names are confirmed real.

---

## Connection Details

| | Value |
|---|---|
| WebSocket URL | `wss://tracking.prohpharmacy.com/api/socket` |
| REST base URL | `https://tracking.prohpharmacy.com/api` |
| Authentication | `?token=<TRACCAR_TOKEN>` as a query parameter on every request |
| Token env var | `VITE_TRACCAR_TOKEN` (or `NEXT_PUBLIC_TRACCAR_TOKEN` for Next.js) |

The token is generated in Traccar → Settings → User → API Access. Store it as an environment variable — **never hardcode it in source**.

---

## REST Endpoints

> **Do not call Traccar directly from the frontend** — it will be blocked by CORS. All REST calls go through your backend, which proxies to Traccar server-side.

### 1. Current position of a device

```http
GET api/v1/fleet/devices/{backendDeviceId}/position
Authorization: Bearer <accessToken>
```

`backendDeviceId` is the backend UUID from `GET api/v1/fleet/devices/traccar` (`backendDeviceId` field).

**Response:**
```json
{
  "deviceId": "...",
  "deviceName": "Ahafo - Sakoes Vehicle",
  "staffMemberId": "...",
  "staffName": "Kofi Mensah",
  "vehicleId": "...",
  "vehicleRegistration": "GR-1234-24",
  "latitude": 5.728970162899248,
  "longitude": -0.04652289249332973,
  "speed": 0.0,
  "course": 0.0,
  "address": null,
  "ignition": null,
  "motion": false,
  "batteryLevel": 50.0,
  "fixTime": "2026-09-20T23:08:01.000+00:00",
  "valid": true
}
```

---

### 2. Route history (for map replay / polyline)

```http
GET api/v1/fleet/devices/{backendDeviceId}/position/history?from=2026-09-20T00:00:00Z&to=2026-09-20T23:59:59Z
Authorization: Bearer <accessToken>
```

- Maximum range: **31 days**
- `from` and `to` are ISO 8601 UTC

**Response** (array of all GPS pings in the time range):
```json
[
  {
    "latitude": 5.726709188459911,
    "longitude": -0.04784365750551864,
    "speed": 0.0,
    "course": 0.0,
    "address": null,
    "motion": false,
    "batteryLevel": 50.0,
    "fixTime": "2026-09-20T10:55:58.000+00:00",
    "valid": true
  },
  {
    "latitude": 5.72645857424132,
    "longitude": -0.04803760617107798,
    "speed": 2.47,
    "course": 234.55,
    "motion": true,
    "batteryLevel": 50.0,
    "fixTime": "2026-09-20T10:56:25.000+00:00",
    "valid": true
  }
]
```

Feed `[latitude, longitude]` pairs as a polyline to draw the route on the map.

---

### 3. All devices (with backend link status)

This comes from **your backend**, not Traccar directly:

```http
GET api/v1/fleet/devices/traccar
Authorization: Bearer <accessToken>
```

**Response:**
```json
[
  {
    "traccarDeviceId": 21,
    "name": "Ahafo - Sakoes Vehicle",
    "uniqueId": "2531c74e72f641a7ab890ace4f335fb4",
    "status": "unknown",
    "lastUpdate": "2026-09-20T23:08:18.101+00:00",
    "disabled": false,
    "isLinked": true,
    "backendDeviceId": "...",
    "vehicleId": "...",
    "vehicleRegistration": "GR-1234-24",
    "staffName": "Kofi Mensah"
  }
]
```

Use this to build the lookup map that enriches raw Traccar positions with vehicle/staff labels.

---

## WebSocket — Real-time Position Stream

### Connecting

```
wss://tracking.prohpharmacy.com/api/socket?token=<TRACCAR_TOKEN>
```

- **On connect** — Traccar immediately pushes the current state of all devices and their latest positions
- **On every GPS ping** — Traccar pushes a message containing only the updated entries
- **Confirmed working** — tested live, connection established, positions received instantly

### Message format

```json
{
  "positions": [
    {
      "id": 1090,
      "deviceId": 21,
      "valid": true,
      "latitude": 5.728970162899248,
      "longitude": -0.04652289249332973,
      "speed": 0.0,
      "course": 0.0,
      "fixTime": "2026-09-20T23:08:01.000+00:00",
      "attributes": {
        "batteryLevel": 50.0,
        "motion": false,
        "charge": false
      }
    }
  ],
  "devices": [
    {
      "id": 21,
      "name": "Ahafo - Sakoes Vehicle",
      "status": "online",
      "lastUpdate": "2026-09-20T23:08:18.101+00:00"
    }
  ]
}
```

- Both `positions` and `devices` keys are optional in any given message — always check before iterating
- A message may contain only `positions`, only `devices`, or both

---

## Types

```ts
export interface TraccarMessage {
  positions?: TraccarPosition[]
  devices?: TraccarDeviceStatus[]
}

export interface TraccarPosition {
  id: number
  deviceId: number
  valid: boolean
  latitude: number
  longitude: number
  altitude: number
  speed: number        // knots — multiply by 1.852 for km/h
  course: number       // degrees 0–360
  address: string | null
  accuracy: number
  fixTime: string      // UTC ISO 8601
  deviceTime: string
  serverTime: string
  geofenceIds: number[] | null
  attributes: {
    batteryLevel?: number   // 0–100
    motion?: boolean
    charge?: boolean
    distance?: number       // metres since last fix
    totalDistance?: number  // cumulative metres
    [key: string]: unknown
  }
}

export interface TraccarDeviceStatus {
  id: number
  name: string
  status: 'online' | 'offline' | 'unknown'
  lastUpdate: string | null
}

export interface VehiclePosition {
  traccarDeviceId: number
  backendDeviceId: string | null   // UUID — use this for REST proxy calls
  vehicleRegistration: string | null
  staffName: string | null
  latitude: number
  longitude: number
  speed: number
  course: number
  motion: boolean
  batteryLevel: number | null
  fixTime: string
  status: 'online' | 'offline' | 'unknown'
}
```

---

## WebSocket Hook

```ts
// lib/traccar-socket.ts

const TRACCAR_WS = `wss://tracking.prohpharmacy.com/api/socket?token=${import.meta.env.VITE_TRACCAR_TOKEN}`

export function createTraccarSocket(onMessage: (msg: TraccarMessage) => void) {
  let ws: WebSocket
  let reconnectTimer: ReturnType<typeof setTimeout>

  function connect() {
    ws = new WebSocket(TRACCAR_WS)

    ws.onopen = () => console.log('[Tracking] connected')

    ws.onmessage = (event) => {
      try {
        onMessage(JSON.parse(event.data))
      } catch {
        // ignore malformed frames
      }
    }

    ws.onerror = () => {
      // always fires before onclose — let onclose handle reconnect
    }

    ws.onclose = () => {
      console.log('[Tracking] disconnected — reconnecting in 5s')
      reconnectTimer = setTimeout(connect, 5000)
    }
  }

  connect()

  return () => {
    clearTimeout(reconnectTimer)
    ws.onclose = null  // prevent reconnect on intentional close
    ws.close()
  }
}
```

---

## useTracking Hook

```tsx
// hooks/useTracking.ts
import { useEffect, useRef, useState } from 'react'
import { createTraccarSocket } from '@/lib/traccar-socket'
import api from '@/lib/api'

export function useTracking() {
  const [positions, setPositions] = useState<Map<number, VehiclePosition>>(new Map())
  const [connected, setConnected] = useState(false)
  const metaRef = useRef<Map<number, any>>(new Map())

  useEffect(() => {
    // Load device metadata once from backend
    api.get('/api/v1/fleet/devices/traccar').then(({ data }) => {
      metaRef.current = new Map(data.map((d: any) => [d.traccarDeviceId, d]))
    })

    const disconnect = createTraccarSocket((msg) => {
      setConnected(true)

      if (msg.positions?.length) {
        setPositions(prev => {
          const next = new Map(prev)
          for (const p of msg.positions!) {
            const meta = metaRef.current.get(p.deviceId)
            next.set(p.deviceId, {
              traccarDeviceId: p.deviceId,
              backendDeviceId: meta?.backendDeviceId ?? null,
              vehicleRegistration: meta?.vehicleRegistration ?? null,
              staffName: meta?.staffName ?? null,
              latitude: p.latitude,
              longitude: p.longitude,
              speed: p.speed,
              course: p.course,
              motion: p.attributes.motion ?? false,
              batteryLevel: p.attributes.batteryLevel ?? null,
              fixTime: p.fixTime,
              status: 'online'
            })
          }
          return next
        })
      }

      if (msg.devices?.length) {
        setPositions(prev => {
          const next = new Map(prev)
          for (const d of msg.devices!) {
            const existing = next.get(d.id)
            if (existing) next.set(d.id, { ...existing, status: d.status })
          }
          return next
        })
      }
    })

    return disconnect
  }, [])

  return { positions: Array.from(positions.values()), connected }
}
```

---

## Route History Hook

```tsx
// hooks/useRouteHistory.ts
import { useState } from 'react'
import api from '@/lib/api'

export function useRouteHistory() {
  const [route, setRoute] = useState<[number, number][]>([])
  const [loading, setLoading] = useState(false)

  async function loadRoute(backendDeviceId: string, date: string) {
    // backendDeviceId — the UUID from GET api/v1/fleet/devices/traccar (backendDeviceId field)
    // date format: 'YYYY-MM-DD'
    setLoading(true)
    try {
      const from = `${date}T00:00:00Z`
      const to = `${date}T23:59:59Z`
      const { data } = await api.get(
        `/api/v1/fleet/devices/${backendDeviceId}/position/history`,
        { params: { from, to } }
      )
      setRoute(data.filter((p: any) => p.valid).map((p: any) => [p.latitude, p.longitude]))
    } finally {
      setLoading(false)
    }
  }

  return { route, loading, loadRoute }
}
```

Pass `route` as a `positions` array to a Leaflet `<Polyline>` or Google Maps `Polyline`.

---

## Map Component

```tsx
// pages/portal/tracking.tsx
import { MapContainer, TileLayer, Marker, Popup, Polyline } from 'react-leaflet'
import { useTracking } from '@/hooks/useTracking'
import { useRouteHistory } from '@/hooks/useRouteHistory'

export default function TrackingPage() {
  const { positions, connected } = useTracking()
  const { route, loading, loadRoute } = useRouteHistory()

  return (
    <div className="relative h-screen">

      {/* Connection status */}
      <div className={`absolute top-4 right-4 z-10 px-3 py-1 rounded-full text-sm font-medium ${
        connected ? 'bg-green-100 text-green-700' : 'bg-yellow-100 text-yellow-700'
      }`}>
        {connected ? 'Live' : 'Reconnecting...'}
      </div>

      <MapContainer center={[7.9465, -1.0232]} zoom={7} className="h-full">
        <TileLayer url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png" />

        {/* Live vehicle markers */}
        {positions.map(v => (
          <Marker key={v.traccarDeviceId} position={[v.latitude, v.longitude]}>
            <Popup>
              <p className="font-semibold">{v.vehicleRegistration ?? `Device ${v.traccarDeviceId}`}</p>
              <p>{v.staffName ?? 'Unassigned'}</p>
              <p>{(v.speed * 1.852).toFixed(1)} km/h</p>
              {v.batteryLevel != null && <p>Battery: {v.batteryLevel}%</p>}
              <p className="text-xs text-gray-400">{new Date(v.fixTime).toLocaleTimeString()}</p>
              {v.backendDeviceId && (
                <button onClick={() => loadRoute(v.backendDeviceId!, new Date().toISOString().slice(0, 10))}>
                  Show today's route
                </button>
              )}
            </Popup>
          </Marker>
        ))}

        {/* Route replay polyline */}
        {route.length > 0 && (
          <Polyline positions={route} color="#00bf6f" weight={3} />
        )}
      </MapContainer>
    </div>
  )
}
```

> **Speed is in knots** — always multiply by `1.852` before displaying km/h.

---

## Permissions

Gate the route with any one of:

```ts
requiredPermissions: ['Tracking.ViewAll', 'Tracking.ViewBranch']
```

| Role | Permission | Sees |
|---|---|---|
| SuperAdmin, OperationsManager, Auditor | `Tracking.ViewAll` | All vehicles |
| BranchManager | `Tracking.ViewBranch` | Branch vehicles only |

---

## Environment Variables

```env
# .env.local
VITE_TRACCAR_TOKEN=<token from Traccar → Settings → User → API Access>
```

Add `VITE_TRACCAR_TOKEN` to Vercel / Netlify / Dokploy as a build-time variable. It is embedded in the JS bundle at build time — treat it as a read-only reporting key, not an admin secret.

---

## Implementation Checklist

- [ ] `VITE_TRACCAR_TOKEN` set in local and production env
- [ ] `createTraccarSocket` with 5s auto-reconnect
- [ ] Load device metadata from `GET api/v1/fleet/devices/traccar` on mount
- [ ] Merge positions with device metadata into `VehiclePosition`
- [ ] Live map — one marker per device, updates in place
- [ ] Marker reflects motion state (moving vs parked) and online/offline status
- [ ] Speed displayed in km/h (knots × 1.852)
- [ ] Connection status badge ("Live" / "Reconnecting...")
- [ ] Route history — fetch on demand, draw as polyline, `Accept: application/json` header required
- [ ] Socket closed on component unmount
