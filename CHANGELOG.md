# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- `NetworkCountdown`: server-authoritative remaining-time countdown with lifecycle sync (start, pause, resume, stop, extend).
- Clients dead-reckon locally between syncs; server snapshots are smoothly corrected via `SyncedClock`.

### Fixed

- `SyncedClock` reconciliation when the clock advances with a negative delta.

## [0.2.0] - 2026-08-31

### Added

- `NetworkSync.GameTime` module: `SyncedClock`, `NetworkGameTime`, and `NetworkDeadline`.
- Play-mode inspector drawer for `NetworkDeadline`.

## [0.1.0]

### Changed

- Authority send runs on a configurable network update stage instead of the time-service tick event.
- Send and interpolation stages are inspector fields.

### Fixed

- Relative position always uses the anchor scale. Relative scale only affects the scale channel.

