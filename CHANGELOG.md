# VERMAXION Changelog

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
