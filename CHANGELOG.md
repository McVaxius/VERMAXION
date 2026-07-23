# VERMAXION Changelog

## 2026-07-22 - Ocean Fishing fixed-rail recovery

### Fixed
- Replaced the live-unverified dynamic vnavmesh edge scan and player/entry-position fallbacks with six proven Henchman/FUTA rail coordinates. Destinations keep canonical starboard/port rotations and are ranked by two-yalm player clearance, greatest clearance, and stable canonical order; player positions are never used as navigation targets.
- Tightened rail arrival to 0.5 yalms. Navigation now stops and applies the canonical facing at arrival, waits 500 ms before a bounded facing reapply, and retries facing at most once per second until Fishing/Gathering acknowledges startup.
- Before the first acknowledgement, Ocean Fishing now advances through and wraps the fixed list on ten seconds without 0.25 yalms of progress, 30 active navigation seconds, ten available/non-busy seconds with `CanFish` false after arrival, or five unacknowledged post-arrival `/ahstart` attempts. Recovery clocks pause during route transitions, unavailable-player states, combat, casting, and occupied states.
- `/ahstart` remains immediately eligible and retries every three seconds while moving or settled. Versatile Lure remains once per seven-minute session, advances preserve session state, and the first Fishing/Gathering acknowledgement still stops navigation immediately and permanently locks movement for the voyage.

### Verification
- Added deterministic fixed-coordinate, canonical-rotation, crowd-ranking, wraparound, stall, timeout, false-`CanFish`, unacknowledged-start, paused-timer, facing-settlement, and permanent-lock regressions while retaining startup, route, lifecycle, result, cleanup, and lure coverage.
- The full Debug suite passes 389/389 tests, and the Debug x64 solution build succeeds with zero errors and only the existing `PInvoke.User32` NU1601 resolution warning.

## 2026-07-22 - P27 holistic automation dispatch and native equipment hardening

### Added
- Added one internal automation catalog that assigns each of the 23 per-character `Enable*` feature flags exactly one stable ID, cadence, maturity, default phase, and runtime owner. The 17 ordered engine tasks now have matching catalog, order, and runtime registrations that fail closed with a visible diagnostic if they diverge.
- Added ordered dispatch for Gear Updater, Highest Combat Job, Current Job Equipment, Seasonal Gear, and Minion Roulette. Existing custom order and phase choices are preserved while the five IDs are inserted deterministically after Register Registrables.
- Added structured runnable, disabled, not-due, blocked, and unsupported planning results. Every AutoRetainer/manual run logs the complete plan, and the configuration/main windows expose registry failures and concrete prerequisite blockers.

### Changed
- Misc Commands is a run-start hook and can now be the only After-AR/manual work. It still does not arm or run a Before-AR pass by itself. Ocean Fishing remains owned by its preemptive startup coordinator and is no longer presented as reorderable.
- Gear Updater, Highest Combat Job, Current Job Equipment, and Seasonal Gear now use bounded native gearset/recommended-equipment/inventory adapters. SimpleTweaks commands, hardcoded job targets, blocking delays, delayed continuations, and temporary framework subscriptions were removed.
- Gear Updater enumerates all 100 saved-gearset slots, chooses one stable gearset per unlocked class/job, verifies native updates, and restores the starting gearset. Highest Combat Job uses saved combat gearsets plus Lumina metadata and actual levels. Current Job Equipment saves only the captured active gearset. Seasonal Gear derives slots from Lumina data, verifies moves and saves, and restores the starting gearset on failure without applying recommended gear.
- Minion Roulette sends exactly one command per run and updates its informational attempt counter without using it as an eligibility gate.

### Fixed
- Empty Register Registrables lists and Rival Wings completion/disable recommendations no longer change character enablement. They now produce visible blocked/skip reasons while preserving their checkboxes.
- Cancellation, character changes, watchdog failures, and Full Stop now clean up the new equipment state machines and recommended-equipment operation state.

### Verification
- Expanded the baseline from 366 to 388 passing tests with catalog/reflection, registry contract, migration, dispatch, misc-hook, native equipment policy, timeout, partial-failure, restoration, and cleanup coverage. The isolated Debug x64 plugin build succeeds with only the existing `PInvoke.User32` NU1601 resolution warning.

## 2026-07-21 - I61/I57 narrow Ocean Fishing positioning and startup fix

### Fixed
- Ocean Fishing now derives its boat position from one read-only voyage-entry vnavmesh scan across 32 directions at 0.5-yalm intervals up to 20 yalms. It chooses the nearest edge at least 2 yalms from other players, otherwise the greatest-clearance edge, faces outward, and uses the specified player/entry fallback when no mesh edge is available.
- Each seven-minute session sets Versatile Lure once and retries `/ahstart` every three seconds, including during initial movement and after fishing is interrupted. Fishing/Gathering acknowledgement stops navigation immediately and permanently locks voyage movement.
- Before fishing has ever started, the first destination may use one scanned alternative only after remaining unfishable for 10 seconds. Route changes, crowd changes, and later failures never trigger repositioning. AutoHook preset ownership and unrelated fishing lifecycle behavior are unchanged.

## 2026-07-20 - I62 recommended-equipment fix

### Fixed
- Equipment updaters now use the native recommended-equipment module instead of `/equiprecommended`, while retaining their existing `/updategearset` save flow and timing.

## 2026-07-17 - P1195 DAD terminal-reservation reacquisition

### Fixed
- A valid DAD v2 `Reserve` received after the retained reservation reached terminal `Released` now initializes a
  fresh `Pending`/`Granting` attempt with new timestamps and a new 15-second lease, including for the same operation
  token. The new attempt can grant normally after VERMAXION and AutoRetainer reach the existing safe boundary.
- Active same-token renewals remain idempotent and extend only the current lease. Conflicting active tokens remain
  rejected without replacing the owner.
- Added regressions for explicit release and lease-expiry reacquisition while retaining renewal and conflict coverage.
  No IPC channel, JSON shape, DTO, enum, configuration, manifest, or plugin version changed.

## v0.0.0.1 - Initial Scaffold

### Added
- Full plugin scaffold with account-based per-character configuration (FrenRider pattern)
- ConfigManager with account/character system, JSON persistence, KrangleService
- ARPostProcessService: Two-phase IPC integration with AutoRetainer
  - Subscribe to OnCharacterAdditionalTask → RequestCharacterPostprocess
  - Subscribe to OnCharacterReadyForPostprocess → run tasks → FinishCharacterPostprocessRequest
- VermaxionEngine: State machine orchestrator that sequences all tasks
- ResetDetectionService: Weekly (Tue 8:00 UTC), daily (15:00 UTC), Saturday detection
- HenchmanService: Stop/start via /henchman off and /henchman on slash commands
- FCBuffService: Seal Sweetener check and purchase flow (stub - needs addon research)
- VerminionService: Lord of Verminion 5x queue (stub - needs ContentsFinder research)
- CactpotService: Mini Cactpot via Saucy, Jumbo Cactpot (stub - needs addon research)
- ChocoboRaceService: Chocobo racing via Chocoholic commands (stub)
- MainWindow: Status overview with task table, reset timers, manual run button
- ConfigWindow: Left panel character list, right panel settings (FrenRider-style layout)
- DTR bar entry with status display
- Commands: /vermaxion (main UI), /vmx [on|off|run|cancel|config]

### Known Stubs (Need In-Game Research)
- VerminionService: Duty queue interaction not implemented
- FCBuffService: FreeCompanyAction addon interaction not implemented
- CactpotService: Saucy command syntax needs verification
- CactpotService: Jumbo Cactpot addon interaction not implemented
- ChocoboRaceService: Chocoholic command syntax needs verification
