# 19 — Live Tracking (`/portal/tracking`)

## Overview

The tracking page connects **directly** to Traccar's WebSocket API to receive real-time vehicle positions. No backend SignalR is used for this page. Device metadata (vehicle registration, staff name, branch) is fetched once from the backend on load and merged with the live position stream.

---

## Connection Details

| | Value |
|---|---|
| WebSocket URL | `wss://tracking.prohpharmacy.com/api/socket` |
| Authentication | `?token=<TRACCAR_TOKEN>` query parameter |
| REST base URL | `https://tracking.prohpharmacy.com/api` |
| Token env var | `VITE_TRACCAR_TOKEN` (or `NEXT_PUBLIC_TRACCAR_TOKEN` for Next.js) |

The token is a long-lived API key generated in Traccar → Settings → User → API Access. Store it as an environment variable — **never hardcode it in source**.

---

## WebSocket Message Format

Traccar sends a JSON object on every push. Any combination of keys may appear:

```json
{
  "positions": [
    {
      "id": 1090,
      "deviceId": 21,
      "valid": true,
      "latitude": 5.6870775,
      "longitude": -0.2794193,
      "altitude": 74.2,
      "speed": 0.0,
      "course": 0.0,
      "address": null,
      "fixTime": "2026-09-20T23:08:18.000+00:00",
      "deviceTime": "2026-09-20T23:08:18.000+00:00",
      "serverTime": "2026-09-20T23:08:18.101+00:00",
      "attributes": {
        "batteryLevel": 94.0,
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

- **On connect** — Traccar immediately pushes the current positions and status of all devices.
- **On update** — any subsequent push contains only changed entries.
- `positions` and `devices` are both optional in any given message — always check before iterating.

---

## Loading Device Metadata

Call the backend once on page load to get vehicle and staff info for each Traccar device:

```http
GET api/v1/fleet/devices/traccar
Authorization: Bearer <accessToken>
```

Response:
```json
[
  {
    "traccarDeviceId": 21,
    "name": "Ahafo - Sakoes Vehicle",
    "isLinked": true,
    "vehicleId": "...",
    "vehicleRegistration": "GR-1234-24",
    "staffName": "Kofi Mensah"
  }
]
```

Build a lookup map keyed by `traccarDeviceId` and use it to enrich every position update:

```ts
const deviceMeta = new Map(devices.map(d => [d.traccarDeviceId, d]))
```

---

## Connecting to the WebSocket

```ts
// lib/traccar-socket.ts

const TRACCAR_WS = `wss://tracking.prohpharmacy.com/api/socket?token=${import.meta.env.VITE_TRACCAR_TOKEN}`

export function createTraccarSocket(onPosition: (update: TraccarMessage) => void) {
  let ws: WebSocket
  let reconnectTimer: ReturnType<typeof setTimeout>

  function connect() {
    ws = new WebSocket(TRACCAR_WS)

    ws.onopen = () => {
      console.log('[Tracking] connected')
    }

    ws.onmessage = (event) => {
      try {
        const msg: TraccarMessage = JSON.parse(event.data)
        onPosition(msg)
      } catch {
        // ignore malformed frames
      }
    }

    ws.onerror = () => {
      // onerror always fires before onclose — let onclose handle reconnect
    }

    ws.onclose = () => {
      console.log('[Tracking] disconnected — reconnecting in 5s')
      reconnectTimer = setTimeout(connect, 5000)
    }
  }

  connect()

  return () => {
    clearTimeout(reconnectTimer)
    ws.onclose = null   // prevent reconnect on intentional close
    ws.close()
  }
}
```

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
  speed: number        // knots
  course: number       // degrees 0–360
  address: string | null
  fixTime: string
  serverTime: string
  attributes: {
    batteryLevel?: number
    motion?: boolean
    charge?: boolean
    [key: string]: unknown
  }
}

export interface TraccarDeviceStatus {
  id: number
  name: string
  status: 'online' | 'offline' | 'unknown'
  lastUpdate: string | null
}

// Enriched — merged with backend metadata
export interface VehiclePosition {
  traccarDeviceId: number
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

## React Hook

```tsx
// hooks/useTracking.ts
import { useEffect, useRef, useState } from 'react'
import { createTraccarSocket } from '@/lib/traccar-socket'
import api from '@/lib/api'

export function useTracking() {
  const [positions, setPositions] = useState<Map<number, VehiclePosition>>(new Map())
  const [connected, setConnected] = useState(false)
  const metaRef = useRef<Map<number, DeviceMeta>>(new Map())

  useEffect(() => {
    // Load device metadata once
    api.get('/api/v1/fleet/devices/traccar').then(({ data }) => {
      metaRef.current = new Map(data.map((d: any) => [d.traccarDeviceId, d]))
    })

    // Open WebSocket
    const disconnect = createTraccarSocket((msg) => {
      setConnected(true)

      if (msg.positions?.length) {
        setPositions(prev => {
          const next = new Map(prev)
          for (const p of msg.positions!) {
            const meta = metaRef.current.get(p.deviceId)
            next.set(p.deviceId, {
              traccarDeviceId: p.deviceId,
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

## Map Component

Use **Leaflet** (via `react-leaflet`) or any map library. Each `VehiclePosition` entry is one marker.

```tsx
// pages/portal/tracking.tsx
import { useTracking } from '@/hooks/useTracking'

export default function TrackingPage() {
  const { positions, connected } = useTracking()

  return (
    <div className="relative h-screen">
      <StatusBadge connected={connected} />
      <MapContainer center={[7.9465, -1.0232]} zoom={7} className="h-full">
        <TileLayer url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png" />
        {positions.map(v => (
          <Marker
            key={v.traccarDeviceId}
            position={[v.latitude, v.longitude]}
            icon={vehicleIcon(v.motion, v.status)}
          >
            <Popup>
              <p className="font-semibold">{v.vehicleRegistration ?? `Device ${v.traccarDeviceId}`}</p>
              <p>{v.staffName ?? 'Unassigned'}</p>
              <p>{(v.speed * 1.852).toFixed(1)} km/h</p>
              <p className="text-xs text-gray-400">{new Date(v.fixTime).toLocaleTimeString()}</p>
            </Popup>
          </Marker>
        ))}
      </MapContainer>
    </div>
  )
}
```

> Speed from Traccar is in **knots** — multiply by `1.852` to display km/h.

---

## Permissions

The tracking page requires `Tracking.ViewAll` (for OperationsManager, Auditor) or `Tracking.ViewBranch` (for BranchManager — only sees their branch's vehicles). Gate the route in the auth guard:

```ts
requiredPermissions: ['Tracking.ViewAll', 'Tracking.ViewBranch']  // any one of these
```

---

## Environment Variables

```env
# .env.local
VITE_TRACCAR_TOKEN=<token from Traccar Settings → User → API Access>
```

Add `TRACCAR_TOKEN` to your hosting environment (Vercel / Netlify / Dokploy) as a non-secret build variable — it's read at build time by Vite and embedded in the bundle, so treat it like a read-only API key.

---

## Implementation Checklist

- [ ] `VITE_TRACCAR_TOKEN` set in local and production env
- [ ] `createTraccarSocket` with auto-reconnect on close
- [ ] Load device metadata from `GET api/v1/fleet/devices/traccar` on mount
- [ ] Merge positions with device metadata into `VehiclePosition`
- [ ] Map with one marker per device — updates in place (don't re-render whole list)
- [ ] Marker icon reflects motion state (moving vs parked) and online/offline status
- [ ] Speed displayed in km/h (convert from knots)
- [ ] Connection status indicator ("Live" / "Reconnecting...")
- [ ] Cleanup — close socket on component unmount
