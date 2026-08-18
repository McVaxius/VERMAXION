# Vermaxion UI/UX Recommendations

**Review date:** 2026-08-18  
**Scope:** UI code review only; no runtime behaviour or implementation changes are included in this document.

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
