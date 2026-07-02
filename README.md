# VERMAXION


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
- **Henchman Management** — Stop/start around task execution

## How It Works

1. AutoRetainer finishes retainers/subs on a character
2. AR fires post-process event → Vermaxion picks it up
3. Disables Henchman → runs enabled tasks → re-enables Henchman
4. Signals AR to continue to next character

## Requirements

- **AutoRetainer** (required for post-process hook)
- Saucy (Mini Cactpot), Chocoholic (Chocobo Racing), Henchman, Lifestream — optional per feature

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

v0.0.0.1 — Initial scaffold. Core architecture complete, game interaction stubs need in-game testing.

2026-07-02 — Recovered VERMAXION account configs after the content-ID account regression. W: restored primary account `4000174C01C65D` with 87 characters and 5 fishing-enabled characters; X: restored primary account `4000174C2E9532` with 108 characters and 0 fishing-enabled characters. Generated one-character account files were backed up and quarantined, and global `LastAccountId` now points at the restored primary accounts.

2026-07-02 — Account selection now resolves by existing character membership, preferring the largest matching account for duplicate membership and adding unknown characters to the currently selected valid account. Fishing relog now releases VERMAXION/AutoRetainer ownership, waits for idle conditions, sends `/ays relog` without `/ays reset`, retries unobserved commands, and fails explicitly on registration expiry or wrong-character arrival. Ocean Fishing now runs a full queue flow through FSH equip, repair/lure prep, Limsa travel, Dryskthota interaction, queue confirmation, departure wait, boat positioning, casting, result close, and configured return.
