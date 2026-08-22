# VERMAXION Changelog

## 2026-08-22 - Arcanists' Guild aethernet stall recovery

### Fixed

- Ocean Fishing now waits on its own aethernet window and, if it remains visible for 10 seconds, closes it, cancels Lifestream, stops vnavmesh, and retries within the existing navigation timeout.

## 2026-08-22 - Opt-in Ocean Fishing window watcher

### Added

- Added the disabled-by-default `Actively check for Ocean Fishing windows without AR pre/post process` checkbox to the fixed top of the Main Window. It starts the existing Ocean Fishing coordinator during an open startup window without waiting for an AutoRetainer pre/post process.

### Changed

- Window-watcher starts use the existing candidate order, startup guards, relog and recovery paths, configured return destination, and voyage behavior. Manual and AutoRetainer post-process starts remain unchanged and share the same per-window deduplication.

### Verification

- Reviewed the complete source diff and exact-scope searches. Automated tests, builds, packaging, deployment, and live-client verification were not run by request.

## 2026-08-20 - Complete UI/UX review implementation

### Added

- Added a saved global `Auto width the columns` setting, enabled by default, with immediate save and guidance for automatic versus manual task-table sizing.
- Added a readiness-first automation dashboard with `Due now`, `Blocked`, `Scheduled later`, and `Complete` sections, written state labels, local next-eligible times, owner/cadence context, direct blocker recovery where configuration can help, and collapsed advanced diagnostics/test controls.
- Added persistent configuration-scope context, row-level account-default comparisons, differing-character counts, immediate single-character `Use default`, and named-scope confirmations for propagation, reset, and delete actions.
- Added explicit Before AR and After AR task-order lanes with lane-local movement, explicit phase changes, and inline cadence, ownership, and blocker details.
- Added setup-wizard field-impact previews and separate account-default versus confirmed all-character apply actions. Fishing previews include every changed stock row.
- Added personal registrable-list search and validated import previews with accepted, duplicate, unknown, invalid, added, and removed counts. Import, Clear All, and default-list replacement now mutate only after confirmation.

### Changed

- Corrected the Main Window to keep its identity, readiness, recovery, and primary actions fixed above exactly one scrolling body. The task table no longer owns a nested scrollbar or forces an empty minimum height, so short Favorites views do not reserve a blank table area.
- Replaced the multiline six-column task table with a single-line `★ | Task | When | Type | Actions` layout. Compact local timing and owner/cadence codes carry full legends in header tooltips, while each task tooltip retains its complete status, blocker, maturity, schedule, and disabled-action context without increasing row height.
- Automatic task-column sizing now fits the compact columns to their contents, assigns the remaining width to Task, and prevents divider dragging. Disabling it restores the shared, natively persisted manual layout for All Tasks and Favorites.
- Configuration, task-order, wizard, and registrable-editor layouts retain their stretch/scroll tables, wrapped explanations, stable action columns, and explicit empty/error states at their existing minimum sizes.
- Configuration recovery selects the active character, opens the correct tab and section, and scrolls it into view. Runtime-only blockers remain informational.

### Verification

- The focused `UiUxPolicyTests` class passes 18/18 tests, including fresh and legacy automatic-width defaults. The complete Debug x64 suite passes 538/538 tests.
- The Debug x64 solution build succeeds with zero errors and only the existing `PInvoke.User32` NU1601 dependency-resolution warning. Manual checklist verification remains David-operated and pending.

## 2026-08-20 - Favorites and independent Refill Listings pacing

### Added

- Added global saved Favorites for catalog automations. The main window now defaults to `All Tasks`, provides a flat `Favorites` tab using the same task status and run actions, and keeps manual utilities and test controls in `All Tasks` only.
- Added an independent Refill Listings inter-item delay, defaulting to 250 ms and clamped to 0–2000 ms. It is used only after a listing withdrawal is verified and before the next listing is selected.

### Changed

- Refill Listings snapshots both pacing settings when a run starts. The existing action delay remains exclusive to ordinary menu and click pacing; navigation, verification polling, retries, timeouts, settlement, and closing are unchanged.
- Expanded the UI/UX guide with implementation status while retaining every P0/P1/P2 recommendation for the complete follow-up pass.

### Verification

- Focused favorites and Refill Listings pacing policy coverage passes 8/8 tests, including null, duplicate, unknown, and retired favorite IDs, independent clamping, ordinary click pacing, successful verification, and failed-verification polling.
- Focused catalog, equipment-timing, favorites, and Refill Listings pacing coverage passes 36/36 tests. The complete Debug x64 suite passes 520/520 tests.
- The Debug x64 solution build succeeds with zero errors and only the existing `PInvoke.User32` NU1601 dependency-resolution warning. Live-client verification was not performed.

## 2026-08-18 - Gearset bootstrap and narrow post-process utilities

### Added

- Added a bounded native bootstrap for missing unlocked class/job gearsets, including exact current-job anchoring, owned-mainhand selection, recommended-equipment timing, exact save verification, and manual controls. Gear Updater also uses optional Stylist IPC with its existing native path as the fallback.
- Added disabled-by-default current-character Allied Society automation through Questionable Companion and one-shot After-AR parking with Home, Limsa, Free Company, Inn, Workshop, and validated custom `/li ...` destinations.

### Changed

- Added one absolute Refill Listings action delay, defaulting to 250 ms and clamped to 0–2000 ms, for ordinary listing action pacing.
- Existing Current Job Equipment, Seasonal Gear, Ocean Fishing, and W40 behavior remain on their prior paths.

### Verification

- Static acceptance review covered default compatibility, native timing, job/content and save drift, unsafe/full/missing-equipment failures, Stylist fallback, and invalid parking/Allied selections.
- The single permitted Debug x64 plugin build reached compilation and reported one `uint`-to-`int` argument error in unlocked-job discovery. The direct cast was applied afterward, but the final source was not rebuilt under the one-build limit. Automated tests and live-client actions were not run; runtime behavior remains untested.

## 2026-08-08 - Character-select stall recovery

### Added

- Added the enabled-by-default global `EnableCharacterSelectStallRecovery` setting and its configuration control.
- Whenever `CharaSelect` remains visible, a five-minute timer now makes one automatic attempt to load character-list entry 0. The Main Window also exposes a `Load first character now` control that queues the same framework-thread attempt.
- Both triggers require only a visible `CharaSelect` addon, then invoke `_CharaSelectListMenu` callbacks `29, 0` and `21, 0` before accepting the resulting OK confirmation. They never open, navigate, or back out of character select; a fired callback accepts one new login confirmation only.
- The Main Window reports the setting, timer/stall state, and precise blocked reason.

### Changed

- The automatic timer now uses `CharaSelect` visibility as its sole gate, resets when that addon is hidden, and displays `Automatic recovery in m:ss` while armed or `Automatic attempt used` after its one automatic attempt.

### Verification

- Focused Debug x64 character-select recovery coverage passes 5/5, including visible-`CharaSelect` timer arm, `5:00` and `4:32` countdown formatting, one-shot expiry, hidden-addon reset, and re-arm behavior.
- The complete Debug x64 test project passes 502/502. The Debug x64 plugin build succeeds with 0 errors and the existing `PInvoke.User32` NU1601 dependency-resolution warning (reported during restore and build).
- Live-client verification was not performed.
- The focused legacy-null account-persistence regression passes 1/1. The fresh Debug x64 plugin build succeeds with 0 errors and the existing `PInvoke.User32` NU1601 dependency-resolution warning. Live-client verification remains pending.

### Fixed

- Treat a visible `CharaSelect` addon as the complete character-select gate. The generic ECommons addon-ready predicate and `AgentLobby`/entry checks do not apply to this recovery path, which invokes the callback through the visible-addon path.
- Legacy account JSON with null or missing `CharacterCreatedAtUtc` metadata now loads for the existing timestamp backfill without dropping character configurations.
- Every confirmed login now independently registers the ready `Name@World` character against its selected account, including plugin reload while already logged in. An unknown character remains with its selected readable account even when another account file is unreadable; unreadable-only account sets still fail closed. Before-AR automation waits for that registration but keeps its existing task, AutoRetainer, and DAD gates unchanged.

## 2026-08-01 - Fishing handoff waits for Lifestream

### Fixed
- AutoRetainer post-process fishing startup now waits for Lifestream to become idle, retaining the existing post-process hold and before-AR gate until the existing fishing handoff runs.

## 2026-08-01 - Ocean Fishing 1.5-yalm clearance

### Changed
- Ocean Fishing now uses one shared 1.5-yalm clearance policy for continuous rail candidates, the initial start gate, and recovery-point separation from the prior destination.
- The 32-sample cap, stopped-path and facing gates, paired `/ahstart` then `/ac cast` cadence, bounded recovery, and permanent post-acknowledgement movement lock are unchanged. W40 remains a separate active workflow.

### Verification
- Focused Debug x64 fishing policy tests pass 143/143, including the 1.499-yalm rejection and exact 1.5-yalm acceptance boundary. The full Debug x64 suite passes 497/497 tests.
- The Debug x64 plugin build succeeds with zero errors and the existing `PInvoke.User32` NU1601 dependency-resolution warning. No package, deployment, version bump, commit, push, remote-client access, or live-game verification was performed.

## 2026-07-30 - Automatic Register Registrables inventory mode

### Added
- Added the opt-in per-character `RegisterUnregisteredItemsFromInventory` setting, defaulting to `false` for new and existing JSON. Clone and default-to-character copies preserve the value.
- Automatic mode takes one ordered snapshot of readable, loaded `Inventory1` through `Inventory4`, deduplicates item IDs by first bag/slot occurrence, and ignores the personal list for that run. It selects only still-locked direct mounts, minions, fashion accessories, facewear, orchestrion rolls, emotes/hairstyles, bardings, and Triple Triad cards using the ADS action-ID classification. Faded orchestrion materials and unrelated/indirect items are excluded.

### Changed
- Manual mode continues to use only the configured personal list and retains the empty-list blocker. Automatic mode is eligible with an empty personal list.
- The fixed queue skips already registered items, rechecks native registration immediately before use, then waits the existing seven seconds after every use request. A verified unlock advances even if duplicate copies remain; only a still-present locked item retries, with the existing three-attempt limit and warning. Unreadable bag, slot, item-registration, or verification state fails closed, and exhausted items do not trigger a rescan.

### Verification
- Focused Register Registrables, recovery, and automation-catalog tests pass 64/64, including all eight ADS action IDs, faded/unrelated rejection, inventory ordering/deduplication, locked filtering, unreadable-state failure, automatic/manual source selection, empty-list eligibility, exact seven-second verification, duplicate-copy advancement, and three-attempt exhaustion.
- The complete Debug x64 solution suite passes 497/497 tests. The isolated Debug x64 plugin build succeeds with zero errors and only the existing `PInvoke.User32` NU1601 resolution warning.
- No ADS edits, version/manifest/dependency changes, packaging, release, commit, push, client control, or live-game testing was performed. Live acceptance was out of scope and no deferred work was created.

## 2026-07-29 - Jumbo follow-up and shared configuration safety

### Fixed
- Jumbo Cactpot now restores the state-owned Yes click only for the second/third-ticket follow-up prompt. The guarded first-ticket confirmation, purchase system-message verification, payout recovery, ticket cadence, Mini Cactpot, and Saucy behavior are unchanged.
- Per-account saves no longer write a process's entire stale in-memory snapshot over a shared configuration file. Each local process tracks its loaded baseline, locks the account across processes, reads the newest valid disk copy, and merges only locally changed account, default, and character records. Remote character additions/deletions are retained, deliberate local additions/deletions propagate, and a same-character conflict uses the current saver's value.
- Account JSON remains schema-compatible. Saves validate a same-directory temporary file before atomic replacement and retain one last-known-good `.bak`; a malformed primary loads from that backup. If neither copy is valid, loading and saving fail closed without overwriting either file or treating the unreadable account as absent.

### Verification
- Focused Jumbo, persistence, and account-selection tests pass 65/65, including two stale clients editing different characters, remote additions/deletions, intentional bulk changes, same-character conflict precedence, backup recovery, malformed-file refusal, and valid atomic replacement.
- The full Debug suite passes 470/470 tests. The Debug x64 plugin build succeeds with zero errors and only the existing `PInvoke.User32` NU1601 resolution warning.
- No version, manifest, dependency, package, commit, push, deployment, client configuration, or live-game action was performed. Live Jumbo and multi-client verification were not run.

## 2026-07-26 - Retainer Equipping live defect repair

### Fixed
- Saved gearsets are now protected by counted exact fingerprints containing the encoded HQ item ID, glamour, both stains, and all five materia IDs and grades. `Ignore Gearset` preserves the exact saved copies while allowing physical surplus duplicates; `Ignore Armory`, `All Gear`, player-equipped exclusion, compatibility, and allocation behavior are unchanged.
- Retainer equipment-window ownership is now bound to the selected retainer. Multiple upgrades remain in one retainer's window, cross-retainer transitions return to the list exactly once, and the final equipment window closes before completion.
- Native equipment moves now wait 500 ms after opening the window and between items or attempts, dispatch one request at a time, and poll the exact destination for up to two seconds. Nonzero native returns can still settle successfully; unresolved moves retry only while the exact source remains, stop after three total attempts, emit one terminal warning, and continue without discarding earlier upgrades.
- Completion, failure, cancellation, and Full Stop clear all pending move state while retaining the existing bell ownership and collect-only restoration paths.

### Verification
- Focused Retainer Equipment tests pass 29/29, including HQ/customization fingerprints, counted saved copies and surplus duplicates, same-class combat retainers, gatherers, same-retainer batching, final-window closure, asynchronous nonzero-return success, exact verification, bounded retry, source loss, and single terminal-signal behavior.
- The full Debug suite passes 460/460 tests. The Debug x64 plugin build succeeds with zero errors and only the existing `PInvoke.User32` NU1601 resolution warning.
- Packaging, publication, version changes, plugin commits/pushes, DLL copying, client mutation, remote testing, and live-game actions were not performed. Live acceptance remains operator-run.

## 2026-07-25 - Ocean Fishing AutoHook start with direct-cast fallback

### Fixed
- Every eligible Ocean Fishing start now sends exact `/ahstart` first and exact `/ac cast` second in the same tick. AutoHook remains the primary start path, while the direct cast provides a fallback when preset-driven startup does not cast.
- The paired dispatch remains one attempt: one three-second retry cadence, one attempt-counter increment, and one slot toward the existing five-attempt placement-recovery threshold.
- Both commands remain behind the existing continuous-rail placement and Ocean Fishing duty gates. Fishing/Gathering acknowledgement, recovery, voyage-long movement lock, AutoHook preset ownership, and AutoHook lifecycle cleanup are unchanged.

### Verification
- Focused fishing policy tests pass 143/143, including exact command strings, paired-attempt cadence, placement gates, recovery, movement locking, and outside-duty suppression.
- The full Debug suite passes 452/452 tests. The Debug x64 plugin build succeeds with zero errors and only the existing `PInvoke.User32` NU1601 resolution warning.
- Packaging, publication, client configuration, and live-game actions were not performed. Multi-client live acceptance remains pending separately authorized observation.

## 2026-07-25 - Ocean Fishing live Fisher-cap revalidation

### Fixed
- Ocean Fishing now re-reads the current character's Fisher level from native `PlayerState` immediately before acquiring a new run or starting `FishingService`. An unavailable live level fails closed and retries without switching job, traveling, queueing, or casting.
- A non-override candidate whose live Fisher level is at or above the configured cap is rejected even when the cached XADB roster reports a lower level. Current-character rejection stops before run ownership; post-relog rejection releases the owned lifecycle and advances to the next cached candidate.
- The explicit `AlwaysFish` selection now carries override provenance through the startup coordinator, preserving its deliberate cap bypass while normal candidates remain protected.
- Added current-character, unavailable-native-state, post-relog mismatch, next-candidate recovery, and explicit-override regression coverage. W40 voyage positioning and casting behavior is unchanged.

## 2026-07-24 - Ocean Fishing continuous rail placement and settled cast gate

### Fixed
- Replaced the six static boat destinations with continuous Henchman-proven rail sampling. Starboard preserves the middle obstruction gap, port uses its full proven span, and both retain their outward character rotations.
- Each sampling pass rejects candidates within three yalms of another player and rejects recovery points within three yalms of the previous destination. An exhausted 32-candidate pass stops navigation, blocks `/ahstart`, and retries after one second.
- Reaching a destination within 0.5 yalms now stops vnavmesh and applies character rotation. The camera is not rotated. The first `/ahstart` and Fishing/Gathering acknowledgement remain locked until live player clearance is at least three yalms, `vnavmesh.Path.IsRunning` has remained false for one continuous second, and facing readback is within 0.05 radians.
- Clearance loss before acknowledgement resamples as soon as movement is safe; facing verification resamples after ten active seconds, and unavailable path-status IPC fails closed after ten active seconds. Existing navigation stall, timeout, false-`CanFish`, and five-attempt recovery now resample instead of cycling fixed indices.
- The first valid acknowledgement still permanently locks voyage movement. Later route changes and fishing interruptions retry in place without respreading. The existing Ocean Fishing context gate still rejects stale Fishing/Gathering state and `/ahstart` outside the duty.

### Verification
- Focused fishing policy tests pass 143/143, including rail ranges, obstruction-gap preservation, exact three-yalm boundary, bounded sampling, stopped-path resets, facing/path-status timeouts, pre-readiness recovery pauses, movement locking, and outside-duty stale-condition coverage.
- The full Debug suite passes 448/448 tests. The Debug x64 plugin build succeeds with zero errors and only the existing `PInvoke.User32` NU1601 resolution warning.
- Packaging, publication, client configuration, and live-game actions were not performed. Multi-client live acceptance remains pending separately authorized observation.

## 2026-07-24 - Retainer Equipping main-window ordered run

### Added
- Added Retainer Equipping immediately after Refill Listings in the Main Window. The row shows the current character's scheduling checkbox state, cached AutoRetainer readiness or live execution status, a functional Run button, and yellow `WIP` maturity.
- Added exact manual readiness for login, DAD ownership, another engine run, an existing retainer bell session, positive targets, readable/idle AutoRetainer state, selected retainers, target completion, unknown stats, and targeted active ventures. UI probes are cached for five seconds, while a click always forces a fresh AutoRetainer read.
- Added a scoped engine manual-run path that queues only Retainer Equipping, bypasses only its own scheduling checkbox, and suppresses Misc Commands and every unrelated configured task.

### Changed
- Retainer Equipping is now catalogued as `Wip` without changing its `EngineTask` ownership, Before-AR default placement, or normal automatic dispatch.
- Manual execution uses the existing engine state, bell ownership, watchdog, handoff settling, cancellation, cleanup, and collect-only restoration paths. Targeted retainers with active ventures wait for AutoRetainer collection, while already-complete retainers do not block.

### Verification
- Added deterministic WIP-dispatch, isolated-run-scope, scheduling-bypass, hook suppression, readiness matrix, forced-cache-refresh, blocker-reason, and collect-only restoration regressions.
- The complete Debug suite passes 433 tests, and the Debug x64 solution build succeeds with zero errors and only the existing `PInvoke.User32` NU1601 resolution warning.
- Packaging, release, X: copy, and live-game validation were not performed.

## 2026-07-23 - Native gearset confirmation and Ocean Fishing distance gate

### Fixed
- Gear Updater target changes and restoration, Highest Combat Job, and Seasonal Gear restoration now own a three-second confirmation window after every applicable native `EquipGearset` request. Each window polls every framework tick and accepts the first ready `SelectYesno` without reading prompt text or metadata; duplicate gearset requests are suppressed while the window is open, including when the native call returns an error.
- Closing or expiring the gearset confirmation window restarts normal active-gearset verification without consuming another attempt. Current Job Equipment remains unchanged and does not use the confirmation path.
- Ocean Fishing now keeps the initial `/ahstart` and Fishing/Gathering acknowledgement gated until the character is currently within the existing 0.5-yalm fixed-rail threshold. Premature conditions do not increment attempts, stop navigation, mark fishing started, or lock movement.
- Reaching the fixed rail permits the first `/ahstart` immediately without waiting for the 500 ms facing-settlement timer. Arrival no longer stops vnavmesh; the first valid at-destination Fishing/Gathering acknowledgement owns the stop and permanent movement lock.
- Existing fixed-rail stall/timeout cycling, false-`CanFish` fallback, five post-arrival attempts, six coordinates, route behavior, and in-place post-start retries are unchanged.

### Verification
- Added prompt-ready/not-ready, three-second boundary, native-error, duplicate-suppression, final-attempt, and post-window activation-verification regressions across native equipment paths.
- Added pre-arrival cast/acknowledgement suppression, counter/lock preservation, immediate-at-threshold start, acknowledgement ownership, stall recovery, and post-start in-place retry regressions. The full Debug suite contains 420 tests; native/live-game acceptance remains pending.

## 2026-07-23 - P27 six-item recovery and retainer equipping

### Added
- Added a persistent Seal Sweetener II ledger keyed by Free Company ID. Already-active actions leave stock unchanged, confirmed activations decrement exactly once, and successful zero reads remain distinguishable from unreadable FC UI state.
- Added a global ordered fishing-stock catalog and per-character enabled/target settings. Defaults are Versatile Lure at enabled/22 and Plump Worm, Ragworm, and Krill at disabled/99. Catalog removal purges all stored values; later default propagation is explicit.
- Added typed ADS shop purchase start/status/cancel operations. Ocean Fishing requests exact missing quantities in catalog order, verifies final inventory, reports optional partial failures, and blocks only when no Versatile Lure remains.
- Added bounded Fisher fallback after a missing saved gearset or ten unverified equip requests. It reuses an inventory/Armoury Weathered Fishing Rod or asks ADS for exactly one, then uses a verified native inventory move without saving a gearset.
- Added Retainer Equipping for AutoRetainer-enabled retainers. Combat uses AutoRetainer-compatible weighted average item level; gathering uses Perception only. Allocation respects job, level, physical-item uniqueness, both ring slots, source mode, saved-gearset membership, and the independent non-unique filter.
- Added replayable Default & Sync, FC Buff, Fishing, and Retainer Equipping wizards. Apply changes only the current account Default Config and never launches automation.

### Changed
- Retainer source selection now has inventory-only, inventory/Armoury excluding saved gearsets (default), and unrestricted inventory/Armoury modes. Player-equipped containers remain excluded.
- AutoRetainer collect-only state is checkpointed while returned ventures are collected, survives character rotation, and is restored to the original value on success, failure, cancellation, logout, disposal, and recovery.
- The automation catalog now owns 24 per-character enable flags and 18 ordered engine tasks. Retainer Equipping defaults to Before AR. Existing Gysahl Greens and Grade 8 Dark Matter vendor-stock paths are unchanged.
- I68 is resolved as the duplicate umbrella for the I75 FC-action recovery and I72 Fisher fallback work; no additional runtime feature was introduced.

### Verification
- Added deterministic migration, catalog add/remove/sync, FC reconciliation/persistence, ADS partial-outcome, Fisher boundary, source-mode, Perception-only, weighted item-level, strongest-allocation, ring-uniqueness, non-unique, target-zero, retry-signature, and collect-only restoration coverage.
- The full Debug suite passes 412 tests, and the fresh Debug x64 solution build succeeds with zero errors and only the existing `PInvoke.User32` NU1601 resolution warning.
- Native/live-game behavior was not executed and remains pending separately authorized validation.

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
