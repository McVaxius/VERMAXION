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
- **Lord of Verminion** — Queue 5 intentional fails per week
- **Mini Cactpot** — 3x daily via Saucy plugin
- **Jumbo Cactpot** — Weekly submission (Saturdays)
- **Chocobo Racing** — Configurable daily races via Chocoholic plugin
- **Ocean Fishing** — Ordered per-account character fallback, verified queue/voyage lifecycle, and optional post-voyage discard/sell cleanup

## How It Works

1. AutoRetainer finishes retainers/subs on a character
2. AR fires post-process event → Vermaxion picks it up
3. Vermaxion runs enabled tasks while retaining and restoring any external-plugin state it owns
4. Vermaxion signals AR to continue to the next character

## Requirements

- **AutoRetainer** (required for post-process hook)
- **Ocean Fishing:** XA Database, AutoRetainer, Lifestream, AutoHook, and vnavmesh. ADS is the only supported repair provider and is required only when fishing repair is enabled.
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

2026-07-12 — Ocean Fishing now resolves non-positive lure targets to the default 22 and uses fresh Henchman-envelope random rail destinations on voyage entry, route changes, and failed fishability retries, with no Henchman runtime dependency. Verification: all 266 tests pass, the Debug x64 solution build succeeds with only the existing PInvoke.User32 NU1601 warning, and multi-client runtime acceptance remains pending.

2026-07-02 — Ocean Fishing reliability overhaul: each registration window now caches one ordered candidate queue (`AlwaysFish` first, then XA Database Fisher level), treats the full XADB roster as authoritative for every character including the logged-in character, and excludes unknown levels unless overridden. Missing unlock/gearset/lure failures advance immediately; ADS/travel failures retry the same character twice at 3s/10s; registration closure and post-queue failures stop. Lifecycle restoration now retains ownership until AutoHook, AutoRetainer multi-mode, and YesAlready are verified. Registration text is loaded from localized `CtsIkdEntrance_00663` rows 4/10, locked Arcanists' Guild shard 43 gets one verified attunement attempt, optional `/ays discard` and `/ays itemsell` cleanup runs before return, and return succeeds only after observed Lifestream activity or a territory change.

2026-07-02 - Completed the Henchman 2.0.6.6 `OnABoat` parity audit and replaced the first-pass fishing lifecycle with native VERMAXION ownership. Intermediate relog characters are now observed and retried instead of treated as terminal failures. The complete run owns a named YesAlready pause lease, snapshots and conditionally restores AutoRetainer multi-mode and AutoHook, validates quest 69379, uses verified Limsa/aethernet travel, selects `Register to board.` internally, separates queue registration from Commence and duty entry, requires Ocean Fishing territory/status, verifies rail fishability with facing/fallback positions, handles zone transitions and `IKDResult`, waits for return settlement, and cleans up on every terminal path. Added the adjacent `T` account-level test mode without changing scheduled attempt guards or `AlwaysFish` flags.

2026-07-02 live verification - 151 tests pass and the Debug x64 solution builds successfully (only the existing PInvoke.User32 NU1601 resolution warning remains). W: and X: both auto-reloaded the shared dev DLL. The first dual-client `T` run verified run-state capture, YesAlready/AR ownership, W target arrival/startup, and X's bounded idle retry after an unobserved relog. It also exposed a Lifestream IPC normalization defect (`/li limsa` was passed where `limsa` is required); that defect is fixed and regression-tested, and both interrupted test runs restored AutoRetainer multi-mode before the final DLL reload. The final-build Dryskthota wait and 20:00-20:15 UTC registration-through-return checkpoints remain pending a second `T` activation on both clients and the real opening.

v0.0.0.1 — Initial scaffold. Core architecture complete, game interaction stubs need in-game testing.

2026-07-02 — Recovered VERMAXION account configs after the content-ID account regression. W: restored primary account `4000174C01C65D` with 87 characters and 5 fishing-enabled characters; X: restored primary account `4000174C2E9532` with 108 characters and 0 fishing-enabled characters. Generated one-character account files were backed up and quarantined, and global `LastAccountId` now points at the restored primary accounts.

2026-07-02 — Account selection now resolves by existing character membership, preferring the largest matching account for duplicate membership and adding unknown characters to the currently selected valid account. Fishing relog now releases VERMAXION/AutoRetainer ownership, waits for idle conditions, sends `/ays relog` without `/ays reset`, retries unobserved commands, and fails explicitly on registration expiry or wrong-character arrival. Ocean Fishing now runs a full queue flow through FSH equip, repair/lure prep, Limsa travel, Dryskthota interaction, queue confirmation, departure wait, boat positioning, casting, result close, and configured return.
