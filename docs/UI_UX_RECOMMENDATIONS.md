# Vermaxion UI/UX Recommendations

**Review date:** 2026-08-18
**Scope:** UI review plus tracked implementation status. The prioritized P0/P1/P2 recommendations remain the acceptance source for the follow-up implementation.

## Product goal

Understand which recurring tasks are due, which character/config scope is active, and whether AutoRetainer can dispatch the next task safely.

## Reviewed surfaces

- `VERMAXION/Windows/MainWindow.cs`
- `VERMAXION/Windows/ConfigWindow.cs`
- `VERMAXION/Windows/RegistrableConfigWindow.cs`

## What is already working

- The UI models task cadence, ownership, ordering, prerequisites, and per-character/default configuration.
- A clear `CONFIGURED BUT NOT DISPATCHABLE` state already exposes registry problems.
- Setup wizards and a dedicated registrable-item editor support complex configuration.

## Implementation status

### 2026-08-20 — First implementation bundle

- Implemented global catalog-based Favorites with an `All Tasks` default tab, a flat `Favorites` tab, saved star toggles, shared row status/actions, and an explanatory empty state. Manual utilities and test controls remain in `All Tasks` only.
- Split Refill Listings pacing into the existing menu/click action delay and an independent inter-item delay used only after a withdrawal is verified. Both values default to 250 ms, are snapshotted at run start, and are clamped to 0–2000 ms.
- Added focused policy coverage for favorite resolution/toggling and pacing selection, including null, duplicate, unknown, and retired favorite IDs and proof that click pacing and unsuccessful verification do not use the inter-item delay.
- The recommendations and validation checklist below remain intentionally open for the complete follow-up UI/UX pass.

### 2026-08-20 — Complete P0/P1/P2 implementation

- Reframed the main window around engine readiness and active account/character scope, followed by written-state `Due now`, `Blocked`, `Scheduled later`, and `Complete` sections. Favorites remain flat and reuse the same transient task-row descriptors and run delegates.
- Added direct recovery navigation: registry failures open Task Order; configurable blockers open the correct Settings section for the active character; external/runtime-only blockers retain their reason without a misleading configuration action.
- Kept `Editing: Account default` or `Editing: Character`, account context, selected scope, and runtime character above the scrolling character-settings pane. Configurable rows show whether they match the account default, default rows show differing-character counts, single-character `Use default` remains immediate, and propagation/reset/delete actions confirm their named scope.
- Replaced the mixed task-order list with explicit Before AR and After AR lanes. Up/Down stays within a lane, phase changes are explicit, and every row includes cadence, ownership, and its current blocker.
- Added setup-wizard impact previews with exact changed fields, including fishing-stock rows. Applying to the account default remains isolated; applying the default to all characters is a separate confirmed action and neither action starts automation.
- Hardened the registrable-item editor with configured-list search, duplicate-safe additions, first-occurrence import normalization, parse-before-mutation validation, accepted/duplicate/unknown/invalid/added/removed preview counts, confirmed replacement, and confirmed Clear All/default-list replacement.
- Updated the affected tables and panes to use stretch/scroll layouts, wrapped explanations, stable action columns, and explicit empty/error states at the existing minimum sizes. Advanced diagnostics and test controls remain available in collapsed sections.
- Automated policy coverage now owns dashboard classification/recovery routing, lane movement/phase changes/normalization/registry completeness, wizard impact/copy boundaries, and registrable search/validation/deduplication/preview/confirmation/cancellation. Focused Debug x64 coverage passes 37/37, the complete suite passes 536/536, and the Debug x64 solution build succeeds with the established `PInvoke.User32` warning only. The manual checklist below remains David-operated and pending on fresh and mature configurations, narrow width, and common UI scales.

### Delivery mapping

- I193 and I195: global Favorites.
- I194: independent verified inter-item pacing for Refill Listings.
- I187: reviewed; no code change was required.

## Prioritized recommendations

| Priority | Recommendation | Rationale and completion signal |
| --- | --- | --- |
| P0 | Reframe the main window around Due now. | Group tasks into Due now, Blocked, Scheduled later, and Complete. Each due task should show owner, next action, and next eligible time without requiring users to decode engine internals. |
| P0 | Turn non-dispatchable status into a recovery path. | Keep the warning, but add the exact missing prerequisite and a button to open the relevant task, dependency, or ordering setting. |
| P0 | Keep configuration scope permanently visible. | Use a sticky `Editing: Account default` or `Editing: Character` banner and show which rows inherit, override, or differ before any sync action. |
| P1 | Make task ordering spatial and explain constraints. | Use drag handles or clear Up/Down affordances, mark Before AR versus After AR as lanes, and show blockers inline with the affected task. |
| P1 | Preview wizard impact before Apply. | State that a wizard edits the current account default, list the exact fields changing, and make `Apply default to all` a separate, confirmed action. |
| P1 | Simplify recurring-time language. | Show `Due`, `Completed`, or `Next: <local time>` first; move raw UTC timestamps and cadence diagnostics to details. |
| P2 | Harden the registrable-item editor. | Add search, duplicate detection, import validation with a preview, and confirmation/undo for Clear All and default-list replacement. |

## Suggested information hierarchy

1. Engine readiness
2. Due-now task queue
3. Later/completed tasks
4. Configuration scope
5. Advanced ordering and diagnostics

## Validation checklist

- A new user can identify the primary action and current blocker within five seconds.
- Every disabled control has a nearby plain-language reason and, when possible, a direct corrective action.
- Healthy, warning, error, running, and disabled states remain distinguishable without colour.
- The UI remains usable at narrow window widths and common Dalamud UI scales without clipped labels or unreachable controls.
- Destructive, global, or high-impact actions identify their scope and require confirmation or provide a safe undo.
- Empty, loading, stale-data, success, partial-success, and failure states each provide an appropriate next action.
- Settings clearly identify whether they apply globally, per account, per character, per preset, or only for the current session.
- Advanced diagnostics are still reachable but do not compete with the everyday workflow.

## Recommended implementation order

1. Implement P0 items and validate the primary workflow plus blocker recovery.
2. Implement P1 information-architecture and configuration improvements.
3. Apply P2 polish, then test at multiple UI scales with both fresh and mature configurations.
