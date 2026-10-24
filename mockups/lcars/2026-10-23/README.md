# VERMAXION LCARS concepts

Approved LCARS design concepts. Implementation is selected for the repositories in Z:\myrepos.txt, with the rollout tracked in Z:\!todo\2026-10-22GOATFUCK0002.md. These images are design references, not runtime or test evidence.

- [Main window comparison](01-main-regular-compact.png)
- [Settings and supporting owners](02-settings-mini-and-auxiliary.png)
- [Exact source-checked prompts](PROMPTS.md)

The image links are the planned sheet filenames; root review publishes the final generated artwork. Regular/compact compare spacing at the same readable text size. A separate Mini, wizard, access loader or browser view stays a separate owner. Selected supporting views are shown at useful scale; other source windows and conditional states retain their existing layouts and actions, rather than each receiving an illegible thumbnail. Generated text and sample values are proposals, not test results.

## Dedicated Mini window

- [Beastmaster Mini, regular and compact](03-beastmaster-mini-regular-compact-v1.png).

This is the real separate Beastmaster Mini owner, including banner controls,
character, run actions, board/goal/team/progress and applicable route steps.
It is not a density variant of the Main task dashboard. Source action gates and
native titlebar geometry remain authoritative.
## Source scope

Source commit: `073c211110ec1e32fe9dabcd2251dba13f24a1a6`. Existing dirty paths were inspected by count (20); none are included or changed by this mockup task.

Main is a tabbed task dashboard with account/character scope, not only a character list. Existing dirty source remains untouched.

## Verified controls and owners

- VERMAXION/Windows/MainWindow.cs:330 — exact action strip; source Cancel conditional at 346; main tabs/search at 539.
- VERMAXION/Windows/MainWindow.cs:634 — task rows; Verminion at 715; Custom Deliveries at 733.
- VERMAXION/Windows/ConfigWindow.cs:180 — exact Settings tabs; Task Order heading at 256.
- VERMAXION/Windows/BeastmasterWindow.cs:307 — character selector; Pause/Stop/FULL STOP at 366; BeastmasterMiniWindow.cs — separate mini owner.

## Existing UI source inventory

- `VERMAXION/Models/DadTimeWindow.cs`
- `VERMAXION/Windows/BeastmasterMiniWindow.cs`
- `VERMAXION/Windows/BeastmasterWindow.Beasts.cs`
- `VERMAXION/Windows/BeastmasterWindow.Repair.cs`
- `VERMAXION/Windows/BeastmasterWindow.Run.cs`
- `VERMAXION/Windows/BeastmasterWindow.Schedule.cs`
- `VERMAXION/Windows/BeastmasterWindow.Status.cs`
- `VERMAXION/Windows/BeastmasterWindow.Supplies.cs`
- `VERMAXION/Windows/BeastmasterWindow.cs`
- `VERMAXION/Windows/ConfigWindow.cs`
- `VERMAXION/Windows/DebugWindow.cs`
- `VERMAXION/Windows/FishCollectionWindow.cs`
- `VERMAXION/Windows/IslandSanctuaryWindow.cs`
- `VERMAXION/Windows/MainWindow.cs`
- `VERMAXION/Windows/RegistrableConfigWindow.cs`
- `VERMAXION/Windows/VerminionWindow.cs`

## Shared appearance proposal

Auto / Picker / LCARS remain in that order, with LCARS third. Preserve native actions, IDs, saved scope and semantic status colours. Krangle obfuscates identifying text. Proposed shared UI tones default off; titlebar speaker and Settings use the same value, with no volume slider. Browser controls remain in the browser header; existing script-free owners do not gain unsupported timed fade. This appearance contract is approved for the allowlisted rollout; implementation and local verification remain separate from artwork approval.
