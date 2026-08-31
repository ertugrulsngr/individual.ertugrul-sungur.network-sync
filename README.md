<div align="center">

# Network Sync

[![Network Sync demo](https://img.youtube.com/vi/VCNgMjD5I8Y/maxresdefault.jpg)](https://youtu.be/VCNgMjD5I8Y)

</div>

---

**Network Sync** is a Unity package built on top of [Netcode for GameObjects](https://docs.unity3d.com/Packages/com.unity.netcode.gameobjects@2.13/manual/index.html). It provides synchronization components and shared network services for networked gameplay.

The code is written with customization in mind, so behavior can be changed without rewriting the pipeline. Use the ready-made NetworkTransformSync to keep objects aligned across peers, or build on the base classes to synchronize any kind of state through the same tick-stamped, interpolated pipeline.

It also includes a shared timing and latency system based on the RFC 6298 round-trip estimation model, which keeps remote motion stable as network conditions change.

---

## Features

- **State sync foundation.** A send-and-receive pipeline over the network, with authority-based sending and automatic late-join catch-up to the latest state.
- **Interpolated layer.** Buffers what remotes receive and interpolates it, so motion stays smooth under latency.
- **Transform sync.** Synchronizes position, rotation, and scale with smoothing and teleport support. It can sync in world space or relative to an anchor — a moving platform, vehicle, or elevator — so relative motion works with or without Unity parenting and stays correct while the anchor itself moves.
- **Timing and latency services.** A shared clock and round-trip estimation used by every sync behaviour.
- **Game time module (`NetworkSync.GameTime`).** `SyncedClock`, networked scaled `NetworkGameTime`, and `NetworkDeadline` for absolute deadlines on the game clock axis.

> **Note:** `NetworkSync.Core.Timing` (NGO tick/interpolation time) and `NetworkSync.GameTime` (scaled gameplay seconds) are different clocks.

---

## Quick start

1. Add a **NetworkManager** and a **NetworkSyncManager** to your scene.
2. Add **NetworkTransformSync** to any networked GameObject.
3. Start a network session.

The transform is then synchronized to remote peers with smooth interpolation.

To make an object move relative to another networked object, assign it an anchor — or let it follow its network parent automatically.

---

## Customization

There are two ways to customize, depending on how far you need to go.

**In the inspector.** NetworkTransformSync exposes per-axis toggles, change thresholds, rotation compression, send rate, buffer size, anchoring, and smoothing.

**In code.** NetworkTransformSync is a thin layer over a generic pipeline. To synchronize your own state, build on one of the base classes and change only the parts you need: who has authority, how state is written to and read from the network, how received state is applied, and how it is interpolated.

---

## Architecture

```text
NetworkSyncManager        scene entry point
├── Latency service       round-trip estimation
└── Time service          shared server / send / interpolation clocks
        │
State sync                send and receive + late-join
    └── Interpolated      buffering + remote interpolation
            └── Transform  position / rotation / scale · anchors · smoothing
```

- **Latency service:** tracks round-trip time and keeps a smoothed estimate as conditions change.
- **Time service:** provides a shared clock, a send timeline for stamping outgoing state, and an interpolation timeline that samples slightly in the past for smoother remote motion.
- **Sync stack:** authority sends at a tick interval; remotes buffer what they receive and interpolate, then optionally smooth toward the target.
