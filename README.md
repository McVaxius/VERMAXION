# VERMAXION

The main window uses the approved regular and compact presentation, with a `C` compact switch, shared relative colour themes and a fifteen-language selector. `UiLanguage` and `UiAccentRgb` are saved through the existing configuration; the existing `CompactUi` preference is reused. The other five windows keep their established layouts and share the selected appearance.

**Transparency** applies to the complete plugin window, including its titlebar and popups. Settings provides normal opacity, automatic focus fade, faded opacity and delay; defaults are 100%, fading to 50% after 10 seconds without focus and restoring on focus. Compact and language controls can be hidden independently on Main while remaining available in Settings.

Main's titlebar opens Settings, toggles Enabled, runs all eligible tasks, cancels the current run or invokes FULL STOP through the existing actions and readiness checks. The packaged plugin icon appears in Main branding and its titlebar, including when collapsed.

Managed Segoe UI fonts borrow Windows' text and symbol faces and Dalamud's selected-locale Noto CJK coverage. No Windows font files are distributed. Readiness and required-glyph checks remain explicit, including the DTR tooltip's black-circle symbol.

Settings → Characters shows accounts and saved characters in a dedicated left pane. Select `(Default Config)` or a character there, then choose the task in the separate Settings picker above its editor. Narrow windows use a labeled character picker. The editing account stays separate from the active automation account, so browsing profiles keeps runtime work and runtime saves on the logged-in scope.

Fifteen embedded translation catalogs cover authored window labels, tooltips, Settings explanations and displayed status messages, including collection inspection, supplies, readiness and cleanup. English, German, French, Spanish, Italian, Russian, Japanese, Korean, Simplified Chinese, Vietnamese, Brazilian Portuguese, Indonesian, Polish, Turkish and Hindi are available. Hindi uses the local text renderer for Devanagari shaping; game-owned DTR text remains English for that selection. Names, external data, command tokens and logs retain their original text; numbers and dates use the selected UI culture. Native UI IDs remain stable. Build and resource validation do not establish host font readiness or game visual acceptance.

Hindi is enabled only when the local font check passes. Otherwise the selector shows disabled **Hindi (unavailable)** while other languages remain usable. A saved Hindi choice that fails its required-font check shows an English status and **Use English**; that button explicitly saves English. Font failures never change the saved language automatically.

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

The persisted, enabled-by-default global `Enabled` master switch is controlled by the Main Window checkbox or `/vmx on|off`. Turning it off blocks new automatic pre/post-AR, timer, Ocean Fishing, idle inn-parking, and character-select recovery work without force-stopping active work or disabling manual Run/Test controls. Each character's clearly labelled `Character automation enabled` setting remains a secondary gate.

Main's **Run due tasks on a timer** is saved for the current runtime account and defaults to off, with **Timer interval (minutes)** initially set to 30. It considers enabled, due ordered engine tasks from both Before-AR and After-AR phases for the logged-in, registered character, retaining the master/character gates and each task's normal schedule. Enable, login, reload and interval changes start a fresh interval; the next interval after an owned run starts from its completion. A blocked expiry waits for a safe idle opportunity and keeps one pending run. **FULL STOP** disables the saved timer. AutoRetainer may be absent; when installed, busy, unreadable or externally suppressed state blocks startup, and the existing owned suppression lease is conditionally released on completion or cancellation. Ocean Fishing retains its separate coordinator and window-watch setting.

## Features

- **FC Buff Refill** — Seal Sweetener II stock reconciliation and purchasing on an every-AR, daily, weekly, or monthly schedule, with per-character VERMAXION activation disabled by default
- **Retainer Equipping** — Upgrade AutoRetainer-enabled combat retainers by compatible average item level and gatherers by Perception
- **Lord of Verminion** — Queue 5 intentional fails per week
- **Mini Cactpot** — 3x daily via Saucy plugin
- **Jumbo Cactpot** — Weekly submission (Saturdays)
- **Chocobo Racing** — Native observable daily race loop with Always Race and optional Choke-abo Target Pedigree modes
- **Ocean Fishing** — Ordered per-account character fallback, ordered ADS fishing-stock preparation, verified queue/voyage lifecycle, and optional post-voyage discard/sell cleanup

- **Character-select stall recovery** - An enabled-by-default recovery that arms whenever `CharaSelect` remains visible and the global master is on. After five minutes, it makes one guarded attempt to load entry 0. The dashboard's Advanced diagnostics shows a live `m:ss` countdown and keeps the same guarded `Load first character now` test control available manually.
- **Register Registrables** - Personal-list registration or opt-in automatic discovery of locked direct registrables in the four main inventory bags

## Automation ownership and ordering

Every per-character `Enable*` feature has one explicit owner. The Task Order tab contains only the 18 engine-dispatched tasks and shows their catalog cadence/ownership metadata. Existing custom order and Before-AR/After-AR phase choices are retained during normalization.

Chocobo Racing defaults to **Always Race**, which preserves the existing fail-open V1 Choke-abo guard and makes no V2 calls. **Target Pedigree** adds per-character target pedigree G2-G9, retirement rank 40-50, and preferred feed grade 1-3. It validates strict Content-ID-bound V2 status, invokes Ensure only at the real race-start boundary and after each completed race, waits while Choke-abo owns an immediate game action, and yields other VERMAXION tasks with a distinct deferred result at stable breeding waits or blocks. Deferred work records neither completion nor failure; only a completed configured daily race batch writes the existing timestamps. Disabling target racing, changing to Always Race, global stop, and plugin disposal request a best-effort safe-boundary pause.

Stage 1 intentionally leaves Choke-abo's retirement, covering, fledgling-selector, and adoption actions capture-blocked. Use Choke-abo's `/chokeabo dpopup` recorder and supply its four generated capture files before treating the target cycle as complete or live-accepted.

- **Ordered engine tasks:** Run through the configured task order. Retainer Equipping runs Before AR by default and is fully registered alongside Gear Updater, Highest Combat Job, Current Job Equipment, Seasonal Gear, Minion Roulette, and the existing tasks.
- **Misc Commands hook:** Runs once at the beginning of an applicable After-AR or manual engine run, including when it is the only work. It never arms a Before-AR pass by itself.
- **Fishing coordinator:** Ocean Fishing retains its preemptive startup window and account/relog coordinator. Configuration → Global → Fishing includes the disabled-by-default `Watch Ocean Fishing windows` checkbox that can invoke that same coordinator without AR pre/post processing. It is intentionally not reorderable through the engine task list.
- **Manual utility:** Retainer Bell remains an explicit manual utility rather than a character enable flag.
- **Configuration-only WIP:** Adventurer Activity (Evercold) is labelled as configuration-only and is not advertised as runtime dispatch.

The main and configuration windows show exact blocked prerequisites, such as an empty Register Registrables list when automatic inventory discovery is disabled, rather than collapsing them into a generic no-work result. If the catalog, task order definitions, and runtime bindings ever disagree, VERMAXION rejects the run visibly and safely releases its AutoRetainer ownership instead of partially dispatching.

Register Registrables defaults to `All unregistered registrables discovered in inventory` for new configurations and missing saved source fields. Starting with v0.4.0.11, a one-time upgrade also selects this source for every saved account's default profile regardless of enablement, and for character profiles with Register Registrables disabled. Enabled character profiles retain their source choices; all enablement checkboxes, personal lists, and unrelated settings are preserved. After the upgrade is saved successfully, subsequent source changes are respected, including on disabled profiles. This also applies when upgrading directly to a later version. Inventory discovery ignores the personal list for that run, so it can run when that list is empty; selecting the personal-list source uses only that configured list. Automatic discovery takes one ordered snapshot of loaded `Inventory1` through `Inventory4`, deduplicates item IDs by first bag/slot occurrence, and queues only still-locked direct mounts, minions, fashion accessories, facewear, orchestrion rolls, emotes/hairstyles, bardings, and Triple Triad cards. Faded orchestrion materials and other indirect items are excluded. Registration is checked while the fixed queue is built, immediately before each use, and after the existing seven-second wait. A verified unlock advances even when duplicate copies remain; a still-present locked item retries up to three total attempts, and unreadable inventory or registration state fails the run closed without rescanning.

Equipment automation uses the game's native gearset and recommended-equipment modules. Gear Updater scans all 100 saved slots and restores the starting gearset; Highest Combat Job only considers combat jobs represented by valid saved gearsets; Current Job Equipment aborts if the active job or gearset changes; Seasonal Gear derives equipment slots from game data and restores the starting gearset on failure. After an applicable native gearset-change request, Gear Updater, Highest Combat Job, and Seasonal Gear restoration own a three-second window that clicks the first ready Yes/No prompt without inspecting its text and suppresses duplicate requests until normal activation verification resumes. Current Job Equipment does not use that prompt path. These paths use bounded polling and verified native saves without SimpleTweaks commands or blocking sleeps.

Retainer Equipping considers only retainers enabled in AutoRetainer for the current character. Combat completion and allocation use AutoRetainer-compatible average item level; gathering uses total Perception only. Its three source modes are inventory only, inventory plus Armoury Chest excluding saved-gearset items (default), and all inventory/Armoury gear. Player-equipped containers remain excluded, the non-unique filter is independent, and distinct physical items are allocated across both ring slots. AutoRetainer's collect-only state is checkpointed and restored on every exit path. Its yellow `WIP` Main Window row shows live readiness and can run only this ordered-engine task; an explicit click ignores the Retainer Equipping scheduling checkbox but does not run Misc Commands or any other configured task.

FC Buff Refill keeps a persistent Seal Sweetener II stock ledger per Free Company ID. VERMAXION activation is disabled by default per character: the task still reconciles live stock and follows the existing purchase flow at zero stock, but positive stock completes without opening the activation menu or decrementing the ledger. The default-off `Maintain configured Seal Sweetener II stock target` option instead forces a live read and treats the purchase quantity as the desired final stock, buying only the shortfall. If the same run will activate one action, it buys one replacement first so final stock remains at least the target; an already-active action is topped off without another activation. Enabling `Allow VERMAXION to activate Seal Sweetener II` preserves the opt-in activation path, and a verified VERMAXION activation decrements the ledger once.

Fishing stock is an ordered global catalog with explicit per-account/per-character enabled and target values. Versatile Lure defaults to enabled at 22; Plump Worm, Ragworm, and Krill default to disabled at 99. Catalog default changes never silently overwrite characters: use the row sync, all-catalog sync, or `Apply Default to ALL` controls. ADS receives each exact missing quantity in catalog order. Optional bait failures are reported without forfeiting fishing; Versatile Lure permits continuation when at least one remains and blocks at zero. If no saved Fisher gearset exists, or ten equip requests do not verify Fisher, VERMAXION reuses or buys exactly one Weathered Fishing Rod and equips it without creating or changing a saved gearset. Before any current-character or post-relog fishing preparation starts, VERMAXION also reads the live native Fisher level. Unavailable live state waits fail-closed, and a character at or above the configured cap is rejected even if XADB's saved roster is lower; only an explicit `Always Fish` selection can bypass that cap.

Ocean Fishing has one global provider choice. The compatibility default, **VerMAXION + AutoHook**, persists AutoHook `AutoOceanFish` off and retains VERMAXION's existing in-duty placement, bait, facing, `/ahstart`, `/ac cast`, and recovery behavior. **AutoHook AutoOceanFish** persists that setting on and ensures AutoHook is enabled before any relog or duty entry; VERMAXION then performs no in-duty baiting, movement, facing, casting, placement, or recovery, while retaining preparation, registration, result handling, cleanup, and return. The provider is snapshotted and locked for an active Fishing run. VERMAXION synchronizes AutoHook immediately when the provider changes and again before every run; if the Boolean setting or its static `Save()` method cannot be read and written, startup fails with a setup status. The persisted `AutoOceanFish` value remains aligned afterward rather than being restored.

Route selection uses the game's two dialog families instead of localized destination names. Indigo selects dialog entry 0. Ruby selects entry 1 and covers Ruby Sea, Thavnair, and Unknown Island destinations. The requested global or per-character family falls back safely to entry 0 when entry 1 is unavailable. Legacy serialized `Thavnair` preferences remain compatible, behave as Ruby, and are hidden from both dropdowns.

Beside the global Ocean Fishing provider, **Fixed locations** independently chooses one of the 32 built-in fishing positions at random; multiple characters may choose the same position. **Continuous rail** randomly chooses along the existing rail ranges. These two modes ignore passenger rosters, nearby players, reserved slices, and player-clearance relocation. **Spacing mode** explicitly enables settled passenger assignment, unclaimed fallbacks, and geometry-capped player clearance, including the initial three-second arrival check. Missing or invalid `OceanFishingPositioningMode` values default to Fixed locations on both new and existing installations; legacy rail settings cannot enable spacing. Explicit selections persist across restarts. The controls apply to **VerMAXION + AutoHook** positioning and are disabled during an active Fishing run.

Positioning pauses during boarding and route transitions. Reaching a fishing point within 0.5 yalms stops vnavmesh and applies outward character rotation—the camera is not rotated. Casting waits for one continuous second of stopped-path settlement and facing readback within 0.05 radians, then sends `/ahstart` followed by `/ac cast` in the same tick: one attempt on the existing three-second cadence, with recovery after five unacknowledged attempts. Genuine navigation or fishing failures retain bounded recovery within the selected mode; placement cannot exhaust its retry budget before two minutes have elapsed. A valid Fishing/Gathering acknowledgement locks normal voyage movement. The exception, only when VERMAXION owns in-duty fishing, is full-inventory recovery: the exact English `ErrorMessage` with no player sender, `Unable to gather. Insufficient inventory space.`, starts an onboard Merchant & Mender sell attempt through `/ays itemsell`, waits for AutoRetainer busy then idle, and returns to the saved fishing position and facing. Identical player chat, duplicate recovery triggers, inactive runs, and messages outside an active Ocean Fishing duty are ignored. Unavailable merchant/state and return navigation have timeout recovery paths; an AutoRetainer operation that remains busy is still awaited. Voyage completion interrupts this recovery for result handling. Other client-language messages are not matched.

Fishing relogs retain the ordinary four-minute timeout unless character select is observed. In that case, they allow the existing five-minute character-select recovery plus a one-minute completion margin, without extending registration deadlines. A final AutoRetainer handoff with no active fishing run may complete while logged out, but still respects service-owned work, result windows, duties, transitions, occupation, combat, and external-plugin blockers.

Optional idle inn parking recognizes inns through the ECommons territory catalog and yields to AutoRetainer postprocessing or busy/unreadable state. Already-parked characters stay in the inn. The existing `OceanIdleInnParkExitLeadMinutes` configuration key is retained for compatibility and controls whether there is enough time to enter an inn before the next venture; it no longer causes an inn exit.

Four replayable setup wizards cover Default & Sync, FC Buff, Fishing, and Retainer Equipping. They stage edits and write only the current account's Default Config after explicit Apply; they never start automation or silently change existing characters.

## How It Works

1. AutoRetainer finishes retainers/subs on a character
2. AR fires post-process event → Vermaxion picks it up
3. Vermaxion runs enabled tasks while retaining and restoring any external-plugin state it owns
4. Vermaxion signals AR to continue to the next character

Each AutoRetainer/manual run records a structured plan for every catalog entry: runnable, disabled, not due, blocked, or unsupported, with a concrete reason.

Enabled Mini Cactpot and Chocobo Stables also start when the logged-in character is idle and their reset or training cooldown is due, with both global and character automation enabled. These runs retain normal eligibility, AutoRetainer suppression, and cleanup, and wait for fishing, other owned work, travel, duties, queues, and interaction menus. These idle starts wait at least five minutes between Mini attempts and one hour between stable attempts per character; existing AutoRetainer and manual triggers remain available.

Config > Marketboard contains per-profile onion buying controls. Enable the purchase and set positive unit-price and total-gil limits (including tax) to buy one Thavnairian Onion through Emptor API 5 on the current world, only when your own rank 10-19 stabled chocobo is capped and inventory has none. Onion use remains manual. With buying disabled, the existing unfinished free-onion quest acquisition remains available.

## Requirements

- **AutoRetainer** (required for post-process hook)
- **Ocean Fishing:** XA Database, AutoRetainer, Lifestream, AutoHook, and vnavmesh. ADS provides ordered fishing-stock purchases and is the only supported repair provider.
- **Retainer Equipping:** AutoRetainer and a reachable retainer bell through the configured Lifestream route.
- **Mini Cactpot:** Lifestream (`/li saucer`) and vnavmesh; Saucy is optional per feature.
- **Chocobo Racing:** Choke-abo is optional for Always Race and required for Target Pedigree mode.

Ocean Fishing does not require Questionable. It does not manage AutoHook presets, choose bait dynamically, or use local/self repair.

The task dashboard distinguishes `Ready`, `Missing`, and `Needs setup`. Mini Cactpot, Jumbo Cactpot, and Fashion Report accept either enabled TextAdvance or XA Slave's enabled Skip Dialogue setting; required Saucy readiness also verifies that its Mini Cactpot configuration is accessible. Mini Cactpot uses Lifestream's `/li saucer` route and vnavmesh rather than Teleporter. Fishing reports whether AutoHook `AutoOceanFish` matches the selected provider. FC Buff Refill and Fishing require ADS and block startup when it is not loaded. Fishing also blocks run acquisition if the required AutoHook synchronization cannot be completed.

Character-select recovery never opens, navigates, or backs out of character select. Its automatic timer arms only while `CharaSelect` is visible and resets as soon as it is hidden. Both the automatic and manual paths require only that visible addon, then invoke `_CharaSelectListMenu` callbacks `29, 0` and `21, 0` before accepting the resulting OK confirmation. The dashboard's Advanced diagnostics shows the global state, live `m:ss` countdown, and any blocking reason.

## Commands

The dashboard keeps account/character scope, engine status, FULL STOP and run controls above the scrolling task area. **Overview** shows due and blocked tasks; **All Tasks** includes scheduled work and manual utilities; **Favorites** shows starred tasks. Search filters the selected view by task or required plugin. Advanced controls are collapsed.

All dashboard widths use **Favorite / Task / Actions** rows inside separate collapsible **Due now**, **Blocked**, **Scheduled later**, **Complete** and **Manual utilities** panels. Favorites is a flat panel. Readable timing, cadence, ownership and readiness appear beneath the task name; full details remain in tooltips. Run/Resume and Settings share the first action line; additional actions follow underneath. **CPU campaign** continues permanent Verminion challenges separately from the weekly goal. **Global → Display & DTR → Auto width the columns** measures Favorite, reserves 180 scaled pixels for Actions and gives Task the rest. Turn it off to resize dividers; V3 manual widths are saved separately for each view/group.

The dashboard identifies the runtime character as **Current** and keeps the existing global Enabled checkbox. Status and controls sit beside scope at content widths of at least 760 logical pixels and beneath it otherwise. Tabs sit above search and task scrolling. Short status uses its natural height; long diagnostics scroll within three lines. All windows support 520-logical-pixel widths; Dashboard, Settings and Verminion need at least 620 logical pixels of height to keep their fixed headers and scrolling editors usable.

Configuration has **Characters**, **Global**, **Task Order**, **Marketboard** and **About** tabs. Characters provides account/profile scope pickers and a grouped task sidebar. Character search, sorting, filtering and context operations are inside the character picker. Below 760 logical content pixels, choose a task above the full-width editor. Dashboard Settings and `/vmx chocobo settings` retain the selected editing scope. Account rename, defaults, synchronization, wizards and confirmations keep their existing behavior.

The shared slate theme uses cyan selection/progress, near-white text and headings, muted metadata, a mint main Ready indicator, amber blockers and red FULL STOP. Built-in bold Axis headings are 28 logical pixels for Dashboard/Settings and 20 for sections; body text keeps the default font. Enable **Global → Display & DTR → Compact UI** for tighter padding and gaps. Typography, control sizes, colors and content stay the same. Operator-rendered screenshots still need comparison with the supplied references at default/compact widths 520, 700 and 1100 and scales 100%, 150% and 200%; headless geometry and contrast checks do not establish visual acceptance.

Fish collection separates **Targets**, **Characters**, **Supplies** and **Alerts**, with scope/status and collection controls above them. Targets has a viewing-character selector, inspection-based Readiness column and selected-fish panel. Verminion separates **Run**, **Minions & guide**, **Purchase limits** and **Tournament**, with permanent cleared progress, FULL STOP, Resume and purchase/prize warnings above scrolling. Campaign and mission/weekly controls have separate panels. The registrable editor uses **Item / ID / Action** with an internally scrolling list and a separate **Bulk actions** panel for confirmed clear/default-list operations; **Import/export** retains previews and cancellation. Debug keeps saved reload selection, status and red FULL STOP above its searchable task list.

| Command | Description |
|---------|-------------|
| `/vermaxion` | Open main window |
| `/vmx` | Open main window |
| `/vmx on/off` | Enable/disable the global automation master |
| `/vmx run` | Manual trigger |
| `/vmx cancel` | Cancel current run |
| `/vmx config` | Open config window |
| `/vmx chocobo settings` | Open Chocobo Racing settings in the selected editing scope |
| `/vmx debug` | Select one manual task to attempt after the next plugin reload |

In `/vmx debug`, checking a task saves it for the next reload without starting it immediately. After character registration, VERMAXION runs FULL STOP cleanup, checks the task's current manual availability, and invokes its normal dashboard action once. The window can stay closed. Configuration-only stubs cannot be selected, and an unavailable task reports its reason without a debug retry. The task keeps its existing prerequisites and scheduling; `Dispatched` means its manual action was invoked, not that it completed.

The checkbox stays selected for subsequent reloads. Uncheck it to cancel pending startup; choosing a different task cancels the old pending attempt and saves the replacement for the next reload. FULL STOP (also `/vmx stop`) cancels a pending attempt for the current reload while preserving the saved selection. The debug window and existing `[DebugReload]` log entries show pending, dispatched, or blocked status.

## Installation

See [how-to-import-plugins.md](how-to-import-plugins.md)

## Status

2026-07-30 - Added opt-in automatic Register Registrables inventory discovery while preserving personal-list behavior as the default. Automatic runs ignore the personal list, snapshot the four loaded main bags once in bag/slot order, deduplicate item IDs, select only the eight ADS-classified direct registrable action types that remain locked, and fail closed when inventory or native registration state is unreadable. The fixed queue rechecks before use and after seven seconds, advances on verified registration despite duplicate copies, and exhausts only after three still-present locked attempts. Focused registration/recovery/catalog verification passes 64 tests, the full Debug x64 suite passes 497 tests, and the isolated plugin build succeeds with the existing `PInvoke.User32` warning only. Live-game testing was out of scope.

2026-08-01 — Ocean Fishing now uses one shared 1.5-yalm clearance policy for other-player rejection, initial start gating, and recovery-point separation. The 32-sample bound, one-second stopped-vnavmesh gate, outward-facing verification, paired `/ahstart` then `/ac cast` cadence, recovery behavior, and permanent post-acknowledgement movement lock are unchanged. Focused Debug x64 fishing tests pass 143/143, the full Debug x64 suite passes 497/497, and live verification was not run.

2026-07-29 — Fixed the Jumbo Cactpot second/third-ticket follow-up confirmation without changing the guarded first-ticket or payout paths. Shared per-account saves now merge locally changed records against the newest valid disk state under a cross-process lock, so one stale client cannot erase unrelated character changes; malformed files recover from one last-known-good backup or fail closed when neither copy is valid. The JSON schema and `SaveCurrentAccount()` workflow are unchanged. Focused verification passes 65 tests, the full Debug suite passes 470 tests, and the Debug x64 plugin build has only the existing `PInvoke.User32` warning; live Jumbo and multi-client verification were not run.

2026-07-25 — Ocean Fishing startup now revalidates the current character's live native Fisher level immediately before preparation. Unreadable state blocks and retries; a normal candidate at or above the configured cap is rejected before job change, travel, queue, or cast even if cached XADB data is lower, while explicit `Always Fish` remains an intentional override. Focused coordinator/policy verification passes 179 tests, the full Debug suite passes 452 tests, and the Debug x64 plugin build succeeds with the existing `PInvoke.User32` warning only. Live-client verification remains pending.

2026-07-24 — Ocean Fishing now uses randomized continuous Henchman rail ranges with the starboard obstruction gap preserved, live three-yalm player clearance, a one-second stopped-vnavmesh gate, and outward character-facing verification before its first cast or acknowledgement. Blocked and failed points resample without fixed-slot cycling; post-ack movement remains locked. Focused fishing policy verification passes 143 tests and the full Debug suite passes 448 tests; live multi-client acceptance remains pending.

2026-07-23 — Added per-FC confirmed-action stock accounting, ordered ADS fishing-stock catalog recovery, bounded Weathered Fishing Rod fallback, AutoRetainer-aware retainer equipping, and four replayable setup wizards. Deterministic verification covers 412 tests and the Debug x64 solution build; native/live-game behavior remains not executed and must not be treated as live validation.

2026-07-23 — Native gearset changes now own a ready-only three-second Yes/No window for Gear Updater, Highest Combat Job, and Seasonal Gear restoration. Ocean Fishing now gates its first start/acknowledgement on the existing 0.5-yalm rail threshold, leaves facing settlement independent, and preserves all post-start in-place retries. Deterministic coverage expands the suite to 420 tests; live acceptance remains pending.

2026-07-12 — Ocean Fishing now resolves non-positive lure targets to the default 22 and uses fresh Henchman-envelope random rail destinations on voyage entry, route changes, and failed fishability retries, with no Henchman runtime dependency. Verification: all 266 tests pass, the Debug x64 solution build succeeds with only the existing PInvoke.User32 NU1601 warning, and multi-client runtime acceptance remains pending.

2026-07-02 — Ocean Fishing reliability overhaul: each registration window now caches one ordered candidate queue (`AlwaysFish` first, then XA Database Fisher level), treats the full XADB roster as authoritative for every character including the logged-in character, and excludes unknown levels unless overridden. Missing unlock/gearset/lure failures advance immediately; ADS/travel failures retry the same character twice at 3s/10s; registration closure and post-queue failures stop. Lifecycle restoration now retains ownership until AutoHook, AutoRetainer multi-mode, and YesAlready are verified. Registration text is loaded from localized `CtsIkdEntrance_00663` rows 4/10, locked Arcanists' Guild shard 43 gets one verified attunement attempt, optional `/ays discard` and `/ays itemsell` cleanup runs before return, and return succeeds only after observed Lifestream activity or a territory change.

2026-07-02 - Completed the Henchman 2.0.6.6 `OnABoat` parity audit and replaced the first-pass fishing lifecycle with native VERMAXION ownership. Intermediate relog characters are now observed and retried instead of treated as terminal failures. The complete run owns a named YesAlready pause lease, snapshots and conditionally restores AutoRetainer multi-mode and AutoHook, validates quest 69379, uses verified Limsa/aethernet travel, selects `Register to board.` internally, separates queue registration from Commence and duty entry, requires Ocean Fishing territory/status, verifies rail fishability with facing/fallback positions, handles zone transitions and `IKDResult`, waits for return settlement, and cleans up on every terminal path. Added the adjacent `T` account-level test mode without changing scheduled attempt guards or `AlwaysFish` flags.

2026-07-02 live verification - 151 tests pass and the Debug x64 solution builds successfully (only the existing PInvoke.User32 NU1601 resolution warning remains). W: and X: both auto-reloaded the shared dev DLL. The first dual-client `T` run verified run-state capture, YesAlready/AR ownership, W target arrival/startup, and X's bounded idle retry after an unobserved relog. It also exposed a Lifestream IPC normalization defect (`/li limsa` was passed where `limsa` is required); that defect is fixed and regression-tested, and both interrupted test runs restored AutoRetainer multi-mode before the final DLL reload. The final-build Dryskthota wait and 20:00-20:15 UTC registration-through-return checkpoints remain pending a second `T` activation on both clients and the real opening.

v0.0.0.1 — Initial scaffold. Core architecture complete, game interaction stubs need in-game testing.

2026-07-02 — Recovered VERMAXION account configs after the content-ID account regression. W: restored primary account `<ACCOUNT_ID_1>` with 87 characters and 5 fishing-enabled characters; X: restored primary account `<ACCOUNT_ID_1>` with 108 characters and 0 fishing-enabled characters. Generated one-character account files were backed up and quarantined, and global `LastAccountId` now points at the restored primary accounts.

2026-07-02 — Account selection now resolves by existing character membership, preferring the largest matching account for duplicate membership and adding unknown characters to the currently selected valid account. Fishing relog now releases VERMAXION/AutoRetainer ownership, waits for idle conditions, sends `/ays relog` without `/ays reset`, retries unobserved commands, and fails explicitly on registration expiry or wrong-character arrival. Ocean Fishing now runs a full queue flow through FSH equip, repair/lure prep, Limsa travel, Dryskthota interaction, queue confirmation, departure wait, boat positioning, casting, result close, and configured return.
