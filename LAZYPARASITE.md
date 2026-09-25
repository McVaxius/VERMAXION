# Verminion bot development checkpoint

Scope: implement the approved CPU Verminion plan in VERMAXION, beginning with live
battlefield observation and control proof. Reuse the existing debug reload hook,
logging, task identity, configuration, cleanup and AutoRetainer ownership. The
approved plan selects this single checkpoint, mode/progress configuration, bounded
failure limits and focused regression coverage. No additional machinery selected.

Runtime target: `R:\XIVLauncher3`, with Lord of Verminion selected in `/vmx debug`.
Source edits, Debug builds, DLL replacement and this client's selected reload tests
are authorized. Do not change release versions or operate other clients.

## Current attempt

- 2026-09-24: clean initial worktree, master ahead of origin/master by one commit.
- Source version is 0.5.0.3; observed prior loaded binary reports 0.5.0.2.
- Target's enabled dev-plugin path is `z:\VERMAXION\VERMAXION\bin\x64\Debug\VERMAXION.dll`;
  automatic reload is enabled. This shared source output is the intended reload route.
- Selection `verminion_queue` was saved at 22:54:15 -04:00, pending next reload.
- Expected next executable marker: `verminion-control-20260924-01`.
- Intent before build: inspect owned roster, saved palette, completed stage count,
  available LoVM duties and the opened duty-finder UI using declared client structs.
  Stop this first inspection without queueing or counting a match.
- Bounded runtime evidence: `R:\XIVLauncher3\dalamud.log`, read with shared
  read/write/delete access; current first-build state is pending, not dispatched.

## Findings and next action

The old task selects a player battle and increments attempts after unknown exits;
neither is acceptable for the approved CPU bot. ClientStructs declares the palette,
completed stage count, LoVM objects and UI arrays, but no typed battlefield director
or battle-command interface. Capture live UI contracts before strategy investment.

All acceptance items remain unverified: summon/select/move/ability/read/result proof,
10 consecutive farming wins, setup, exact weekly accounting, 24 campaign clears,
tournament operations, FULL STOP/reload matrix and character purchase accounting.

Attempt 01 built successfully (existing PInvoke package warning). Fresh marker
loaded at 23:00:44 -04:00; one dispatch at 23:00:47; inspection stopped at 23:00:50.
Observed zero completed stages, an empty 23-slot palette, 32 owned minions, and
Master Battle CFC 576 (Hard 577, Extreme 578, NPC tournament 579). Opening that
duty while locked left an unrelated roulette selected, proving native selection
verification is necessary. No queue or result was counted.

Next attempt 02, intent before build: inspect prerequisite quest flags, travel
through existing `/li saucer` and `/li Minion Square` commands, then take one
bounded nearby-NPC/menu snapshot. No battle queue or tutorial command yet. Expected
marker `verminion-control-20260924-02`; attempt is pending build/reload.

Attempt 02 verified: marker at 23:02:58 -04:00, one dispatch at 23:03:01,
Gold Saucer and Minion Square travel reached territory 388; capture stopped at
23:03:41. All three prerequisite quests are already complete (434, 435, 1431),
so this character cannot prove their first-time execution. Minion Square aethernet
is 89; tables are EventObj 2006529. No result recorded.

Attempt 03 intent before build: approach the nearest observed table, interact
once and capture its setup menu. Replaced the old unsafe queue/unknown-exit loop;
no unknown result, cancellation or timeout can reach Complete. Added a saved
FULL STOP pause respected before reload dispatch; explicit manual Run clears it.
Expected marker `verminion-control-20260924-03`, pending build/reload.

Attempt 03 reached the table but stopped at 2.8 yalms, beyond the existing
interaction helper's 2.0-yalm limit. It timed out at 23:08:42 with no result.
Attempt 04 corrects approach distance and bounds interaction calls to every two
seconds. Intent: retry this corrected approach and capture the menu once.
Expected marker `verminion-control-20260924-04`, pending build/reload.
Existing regression suite: 812 passed, 0 failed on this worktree before this fix.

Attempt 04 timed out at 23:10:49: pathing to the table center runs into its
collision mesh. Attempt 05 approaches an offset point and invokes the declared
native interaction with range/line-of-sight checks within four yalms, rather than
the generic helper's fixed two-yalm center limit. Intent: capture the resulting
menu, then stop. Expected marker `verminion-control-20260924-05`.

Attempt 05 verified table interaction and the menu at 23:12:34 -04:00:
Verminion Challenge / Play Guide / Cancel. Attempt ended without a result.
Attempt 06 intent: select the observed challenge entry, then the official guide's
Stage 1: Tutorial label if present; capture its admission controls and stop.
Expected marker `verminion-control-20260924-06`. Existing pause regression passed.

Attempt 06 loaded at 23:14:02 but did not dispatch: the previous setup menu
keeps generic player-available false, preventing character registration. No new
action ran. Attempt 07 permits read-only registration for the explicitly selected
character's verified Verminion menu (or LoVM battle), then reuses the existing
cleanup/dispatch sequence. The service can resume that exact setup menu.
Expected marker `verminion-control-20260924-07`; intent remains tutorial admission.

Attempt 07 verified challenge selection and Stage 1 selection. At 23:16:32 the
game presented its Verminion-specific "Are you prepared for battle?" confirmation;
no admission or result was claimed. Attempt 08 accepts this exact owned prompt,
waits for PlayingLordOfVerminion and captures the initial battle UI after settling.
Expected marker `verminion-control-20260924-08`, pending build/reload.

Attempt 08 accepted the exact tutorial confirmation at 23:18:47 but did not
observe PlayingLordOfVerminion; timed out at 23:19:45 without completion. Outcome
is unresolved, not a failed match. Attempt 09 adds a start/timeout snapshot of the
Verminion and admission UI to identify the missing transition. It allows identity
registration in Minion Square only for the selected debug character; service
readiness remains guarded. Expected marker `verminion-control-20260924-09`.

Attempt 09 timed out at 23:22:24, still in territory 388 with no admission popup
or battlefield UI. Attempt 10 captures relevant recent game system/error messages
and fills only empty palette slots until three distinct owned minions are verified
through native readback. No vendor purchases. Then reattempt tutorial admission.
Expected marker `verminion-control-20260924-10`, pending build/reload.

Attempt 10 verified native palette slots 0/1/2 = owned minions 32/41/50, with
no purchases. Admission still timed out at 23:25:55; game messages captured so
far only show the new Verminion Active Help entry. Attempt 11 uses the observed
native Yes button and captures ten seconds into admission (before a possible
queue popup expires), including queue identity and ActiveHelp. No match counted.
Expected marker `verminion-control-20260924-11`, pending build/reload.

Attempt 11 resolved admission: at 23:27:15 native queue Ready, Regular/552,
ContentsFinderConfirm title node 49 = Stage 1: Tutorial; Commence button node 63,
registered ButtonClick parameter 8. Earlier captures missed the expiring popup.
Attempt 12 intent: reload and reconcile that exact queue if still present, otherwise
start only after the native queue is clear. Verify title and native queue identity
before pressing Commence. Expected marker `verminion-control-20260924-12`.

Attempt 12 verified Commence at 23:29:39, territory 506 and native queue InContent
552, then PlayingLordOfVerminion at 23:29:50. Capture at 23:30:02 shows the tutorial
waiting for its first summon, palette capacity 0/60 (node 67); palette slot 0 is
node 34 with registered DragDropClick parameter 0. Other owned slots 35/36 exist.
No minions or results counted. Service stopped; tutorial remains in progress.

Attempt 13 intent: reload into this same tutorial without queueing, invoke that
observed single summon event only while capacity remains 0/60, then capture queue,
capacity and tutorial-message readback. Expected marker `verminion-control-20260924-13`.

Attempt 13 verified first palette interaction: dispatch 23:32:26, game message
137 at 23:32:33 says "You have successfully selected a minion! It will now be placed
into your summoning queue." Queue array changed at indexes 102-105/133. No spawned
minion observed yet; capacity text remained 0/60 while tutorial narration advanced.
The tutorial remains active; no completion credited. Attempt 14 reads the latest
native tutorial instruction (system-message prompt glyph) and captures its full
current narration. It will only repeat the first command if that exact instruction
and its initial capacity are still present. Also require native tutorial duty 552
before battle commands. Expected marker `verminion-control-20260924-14`.

Attempt 14 loaded and reached a fresh tutorial at 23:35:05 -04:00, stopping at
"Use the movement keys to shift your viewpoint around." Previous admission exited
at 23:33:42 without result evidence; no clear is credited. Attempt 15 intent:
use the current Move Forward binding for one second at this exact prompt, with
release on cleanup, then handle the verified initial hotbar instruction. Only
prompts newer than this run's start are actionable, avoiding previous admissions.
Capture and stop at the next unknown instruction. Expected marker
`verminion-control-20260924-15`; pending build/reload.

Attempt 15 verified: loaded at 23:41:47 -04:00, initial summon at 23:42:18,
camera input at 23:42:33, then fresh Gate A instruction at 23:42:50. Native
Buffalo Calf object index 2 has HP 440/440 and position near Gate B; capacity
is 10/240. Actual spawning and camera input are now observed. No stage clear.
Full regression suite: 813 passed, 0 failed. Attempt 16 intent: exercise the
observed first gate-icon ButtonClick (node 73, registered parameter 7), then
summon once and verify Gate A through tutorial narration and object position.
Its gate meaning is a research hypothesis until readback. Expected marker
`verminion-control-20260924-16`; pending build/reload.

Attempt 16 resumed the existing tutorial at 23:44:59 but did not click: node
type 1015 is a component identifier, not a ComponentType.Icon assertion.
Attempt 17 removes that incorrect type assumption, retaining the exact observed
node/event/listener guards and adding component/position readback. Same bounded
Gate A test. Also replaces queue-state numeric checks with the installed
ContentsFinderQueueState enum, whose values were verified through ILSpy.
Expected marker `verminion-control-20260924-17`; pending build/reload.

Attempt 17 verified Gate A: node 73 is a RadioButton, registered parameter 7.
At 23:46:47 the tutorial advanced and a second Buffalo Calf appeared near
(-16, 0, 17), versus Gate B near (0, 0, 17). Attempt 18 intent: satisfy the
fresh "Summon several minions" instruction with exactly three spaced hotbar
commands, then capture the next instruction. No result credit. Expected marker
`verminion-control-20260924-18`; pending build/reload.

Attempt 18 verified summoning queue at 23:48:25: game confirms queued units
and maximum queue size 10. Tutorial replaced them with one Wayward Hatchling
at (0, 0, 17), HP 410/410, asking for left-click selection and right-click movement
into a yellow circle. Attempt 19 intent: read field objects, screen projections,
and registered LoVM viewport listeners to establish this control contract.
No speculative battlefield click yet. Expected marker
`verminion-control-20260924-19`; pending build/reload.

Attempt 19 loaded at 23:51:33 after correcting a compile-only type-name error.
Observed a unique unnamed marker EventObj 2005110 at (-3, 0, 8), plus the hatchling
near (0, 0, 17). LovmMiniMap registers RawInputData on the viewport manager.
Attempt 20 intent: send a bounded synthetic left-click to that observed listener
at the hatchling's live screen projection, capture selection, then right-click
the marker's projection. Input structs are local copies; no host cursor movement
or global mouse injection. This is an unverified control hypothesis, judged only
by unit position and tutorial readback. Expected marker
`verminion-control-20260924-20`; pending build/reload.
