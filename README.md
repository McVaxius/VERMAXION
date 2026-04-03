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
