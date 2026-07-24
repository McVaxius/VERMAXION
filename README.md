# VERMAXION

VERMAXION exposes its existing v1 automation status IPC plus an additive v2 DAD handoff reservation. A live DAD
operation renews a 15-second lease every five seconds. VERMAXION finishes current owned work, blocks new work,
turns AutoRetainer Multi Mode off, waits for AutoRetainer idle, releases its suppression, and publishes the local
grant event. V2 status JSON emits canonical string reservation states. A waiting, pending, or armed Before-AR gate
has not crossed VERMAXION's real-work boundary and yields to DAD, releasing only VERMAXION-owned suppression;
running engine, fishing, manual, and other active work still drains normally. If a pre-grant attempt ends, VERMAXION
restores Multi Mode only when that attempt disabled it. A successful grant transfers the boundary to DAD and is
never followed by a VERMAXION Multi Mode restore. An explicit reservation request made after the prior lease reaches
terminal `Released` starts a fresh `Pending`/`Granting` attempt, including when DAD reuses the same operation token.
Same-token renewals remain idempotent while a reservation is active, and a conflicting active token remains rejected.


---

**Help fund my AI overlords' coffee addiction so they can keep generating more plugins instead of taking over the world**

[☕ Support development on Ko-fi](https://ko-fi.com/mcvaxius)

[XA and I have created some Plugins and Guides here at -> aethertek.io](https://aethertek.io/)
### Repo URL:
```
https://aethertek.io/x.json
```

---


AutoRetainer post-process automation for weekly and daily tasks, configured per character.

## Features

- **FC Buff Refill** — Seal Sweetener II purchase/cast on every AR run
- **Retainer Equipping** — Upgrade AutoRetainer-enabled combat retainers by compatible average item level and gatherers by Perception
- **Lord of Verminion** — Queue 5 intentional fails per week
- **Mini Cactpot** — 3x daily via Saucy plugin
- **Jumbo Cactpot** — Weekly submission (Saturdays)
- **Chocobo Racing** — Configurable daily races via Chocoholic plugin
- **Ocean Fishing** — Ordered per-account character fallback, ordered ADS fishing-stock preparation, verified queue/voyage lifecycle, and optional post-voyage discard/sell cleanup

## Automation ownership and ordering

Every per-character `Enable*` feature has one explicit owner. The Task Order tab contains only the 18 engine-dispatched tasks and shows their catalog cadence/ownership metadata. Existing custom order and Before-AR/After-AR phase choices are retained during normalization.

- **Ordered engine tasks:** Run through the configured task order. Retainer Equipping runs Before AR by default and is fully registered alongside Gear Updater, Highest Combat Job, Current Job Equipment, Seasonal Gear, Minion Roulette, and the existing tasks.
- **Misc Commands hook:** Runs once at the beginning of an applicable After-AR or manual engine run, including when it is the only work. It never arms a Before-AR pass by itself.
- **Fishing coordinator:** Ocean Fishing retains its preemptive startup window and account/relog coordinator. It is intentionally not reorderable through the engine task list.
- **Manual utility:** Retainer Bell remains an explicit manual utility rather than a character enable flag.
- **Configuration-only WIP:** Adventurer Activity (Evercold) is labelled as configuration-only and is not advertised as runtime dispatch.

The main and configuration windows show exact blocked prerequisites, such as an empty Register Registrables list, rather than collapsing them into a generic no-work result. If the catalog, task order definitions, and runtime bindings ever disagree, VERMAXION rejects the run visibly and safely releases its AutoRetainer ownership instead of partially dispatching.

Equipment automation uses the game's native gearset and recommended-equipment modules. Gear Updater scans all 100 saved slots and restores the starting gearset; Highest Combat Job only considers combat jobs represented by valid saved gearsets; Current Job Equipment aborts if the active job or gearset changes; Seasonal Gear derives equipment slots from game data and restores the starting gearset on failure. After an applicable native gearset-change request, Gear Updater, Highest Combat Job, and Seasonal Gear restoration own a three-second window that clicks the first ready Yes/No prompt without inspecting its text and suppresses duplicate requests until normal activation verification resumes. Current Job Equipment does not use that prompt path. These paths use bounded polling and verified native saves without SimpleTweaks commands or blocking sleeps.

Retainer Equipping considers only retainers enabled in AutoRetainer for the current character. Combat completion and allocation use AutoRetainer-compatible average item level; gathering uses total Perception only. Its three source modes are inventory only, inventory plus Armoury Chest excluding saved-gearset items (default), and all inventory/Armoury gear. Player-equipped containers remain excluded, the non-unique filter is independent, and distinct physical items are allocated across both ring slots. AutoRetainer's collect-only state is checkpointed and restored on every exit path. Its yellow `WIP` Main Window row shows live readiness and can run only this ordered-engine task; an explicit click ignores the Retainer Equipping scheduling checkbox but does not run Misc Commands or any other configured task.

FC Buff Refill keeps a persistent Seal Sweetener II stock ledger per Free Company ID. An already-active action does not consume cached stock; a verified activation decrements it once. Unknown stock, zero stock, and failed activation are reconciled through the FC interface before purchase checks.

Fishing stock is an ordered global catalog with explicit per-account/per-character enabled and target values. Versatile Lure defaults to enabled at 22; Plump Worm, Ragworm, and Krill default to disabled at 99. Catalog default changes never silently overwrite characters: use the row sync, all-catalog sync, or `Apply Default to ALL` controls. ADS receives each exact missing quantity in catalog order. Optional bait failures are reported without forfeiting fishing; Versatile Lure permits continuation when at least one remains and blocks at zero. If no saved Fisher gearset exists, or ten equip requests do not verify Fisher, VERMAXION reuses or buys exactly one Weathered Fishing Rod and equips it without creating or changing a saved gearset.

During the first Ocean Fishing session, `/ahstart` and Fishing/Gathering acknowledgement remain suppressed until the character is currently within 0.5 yalms of the selected fixed rail. Arrival permits the first attempt immediately; facing settlement continues independently, and vnavmesh is stopped only by a valid at-destination acknowledgement or the existing fixed-rail recovery transition. After that first acknowledgement permanently locks voyage movement, later route changes and fishing interruptions retry `/ahstart` in place without another distance gate.

Four replayable setup wizards cover Default & Sync, FC Buff, Fishing, and Retainer Equipping. They stage edits and write only the current account's Default Config after explicit Apply; they never start automation or silently change existing characters.

## How It Works

1. AutoRetainer finishes retainers/subs on a character
2. AR fires post-process event → Vermaxion picks it up
3. Vermaxion runs enabled tasks while retaining and restoring any external-plugin state it owns
4. Vermaxion signals AR to continue to the next character

Each AutoRetainer/manual run records a structured plan for every catalog entry: runnable, disabled, not due, blocked, or unsupported, with a concrete reason.

## Requirements

- **AutoRetainer** (required for post-process hook)
- **Ocean Fishing:** XA Database, AutoRetainer, Lifestream, AutoHook, and vnavmesh. ADS provides ordered fishing-stock purchases and is the only supported repair provider.
- **Retainer Equipping:** AutoRetainer and a reachable retainer bell through the configured Lifestream route.
- Saucy (Mini Cactpot) and Chocoholic (Chocobo Racing) — optional per feature

Ocean Fishing does not require Questionable. It does not manage AutoHook presets, choose bait dynamically, or use local/self repair.

## Commands

| Command | Description |
|---------|-------------|
| `/vermaxion` | Open main window |
| `/vmx` | Open main window |
| `/vmx on/off` | Enable/disable for current character |
| `/vmx run` | Manual trigger |
| `/vmx cancel` | Cancel current run |
| `/vmx config` | Open config window |

## Installation

See [how-to-import-plugins.md](how-to-import-plugins.md)

## Status

2026-07-23 — Added per-FC confirmed-action stock accounting, ordered ADS fishing-stock catalog recovery, bounded Weathered Fishing Rod fallback, AutoRetainer-aware retainer equipping, and four replayable setup wizards. Deterministic verification covers 412 tests and the Debug x64 solution build; native/live-game behavior remains not executed and must not be treated as live validation.

2026-07-23 — Native gearset changes now own a ready-only three-second Yes/No window for Gear Updater, Highest Combat Job, and Seasonal Gear restoration. Ocean Fishing now gates its first start/acknowledgement on the existing 0.5-yalm rail threshold, leaves facing settlement independent, and preserves all post-start in-place retries. Deterministic coverage expands the suite to 420 tests; live acceptance remains pending.

2026-07-12 — Ocean Fishing now resolves non-positive lure targets to the default 22 and uses fresh Henchman-envelope random rail destinations on voyage entry, route changes, and failed fishability retries, with no Henchman runtime dependency. Verification: all 266 tests pass, the Debug x64 solution build succeeds with only the existing PInvoke.User32 NU1601 warning, and multi-client runtime acceptance remains pending.

2026-07-02 — Ocean Fishing reliability overhaul: each registration window now caches one ordered candidate queue (`AlwaysFish` first, then XA Database Fisher level), treats the full XADB roster as authoritative for every character including the logged-in character, and excludes unknown levels unless overridden. Missing unlock/gearset/lure failures advance immediately; ADS/travel failures retry the same character twice at 3s/10s; registration closure and post-queue failures stop. Lifecycle restoration now retains ownership until AutoHook, AutoRetainer multi-mode, and YesAlready are verified. Registration text is loaded from localized `CtsIkdEntrance_00663` rows 4/10, locked Arcanists' Guild shard 43 gets one verified attunement attempt, optional `/ays discard` and `/ays itemsell` cleanup runs before return, and return succeeds only after observed Lifestream activity or a territory change.

2026-07-02 - Completed the Henchman 2.0.6.6 `OnABoat` parity audit and replaced the first-pass fishing lifecycle with native VERMAXION ownership. Intermediate relog characters are now observed and retried instead of treated as terminal failures. The complete run owns a named YesAlready pause lease, snapshots and conditionally restores AutoRetainer multi-mode and AutoHook, validates quest 69379, uses verified Limsa/aethernet travel, selects `Register to board.` internally, separates queue registration from Commence and duty entry, requires Ocean Fishing territory/status, verifies rail fishability with facing/fallback positions, handles zone transitions and `IKDResult`, waits for return settlement, and cleans up on every terminal path. Added the adjacent `T` account-level test mode without changing scheduled attempt guards or `AlwaysFish` flags.

2026-07-02 live verification - 151 tests pass and the Debug x64 solution builds successfully (only the existing PInvoke.User32 NU1601 resolution warning remains). W: and X: both auto-reloaded the shared dev DLL. The first dual-client `T` run verified run-state capture, YesAlready/AR ownership, W target arrival/startup, and X's bounded idle retry after an unobserved relog. It also exposed a Lifestream IPC normalization defect (`/li limsa` was passed where `limsa` is required); that defect is fixed and regression-tested, and both interrupted test runs restored AutoRetainer multi-mode before the final DLL reload. The final-build Dryskthota wait and 20:00-20:15 UTC registration-through-return checkpoints remain pending a second `T` activation on both clients and the real opening.

v0.0.0.1 — Initial scaffold. Core architecture complete, game interaction stubs need in-game testing.

2026-07-02 — Recovered VERMAXION account configs after the content-ID account regression. W: restored primary account `4000174C01C65D` with 87 characters and 5 fishing-enabled characters; X: restored primary account `4000174C2E9532` with 108 characters and 0 fishing-enabled characters. Generated one-character account files were backed up and quarantined, and global `LastAccountId` now points at the restored primary accounts.

2026-07-02 — Account selection now resolves by existing character membership, preferring the largest matching account for duplicate membership and adding unknown characters to the currently selected valid account. Fishing relog now releases VERMAXION/AutoRetainer ownership, waits for idle conditions, sends `/ays relog` without `/ays reset`, retries unobserved commands, and fails explicitly on registration expiry or wrong-character arrival. Ocean Fishing now runs a full queue flow through FSH equip, repair/lure prep, Limsa travel, Dryskthota interaction, queue confirmation, departure wait, boat positioning, casting, result close, and configured return.
