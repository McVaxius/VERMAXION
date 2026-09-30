# VERMAXION custom deliveries, stables and fishing bundle

Updated 2026-09-30. Sole checkpoint for this bundle; unrelated work remains in
the existing LAZYPARASITE.md, apart from requested personal-name redaction.
DevHub statuses are unchanged and are
not delivery evidence. Source started clean at 8a459d5, version 0.6.0.0.

## Accepted scope and decisions

- Order: custom deliveries and narrow bug fixes; first-stage acceptance;
  I433 chocobo stables; then discuss I420–I428 before fishing research/build.
- Import awgil/ffxiv_satisfy revision 1ab3f9f and required compatible support
  source under DeliverySupport; no original library package dependency.
- Weekly custom deliveries default disabled. Retain VSatisfy settings,
  Artisan crafting/purchases and Questionable gathering. Allow multiple
  delivery types/jobs; equal routes prefer crafting, mining, botany, fishing.
- Custom-delivery gathering must use Wiggly Questionable (`WigglyQuest`), not
  Punish Questionable. Use the installed provider's native gathering contract;
  do not change its delivery queue/settings or hand over VERMAXION's turn-ins.
- Default fishing bait is Versatile Lure, item 29717. Normalize saved/entered 0
  to 29717; preserve other selected bait IDs. Client config files stay read-only.
- Custom-delivery movement must retry pathfinding when stuck. Reuse the existing
  12-second tracker, stop the old route before repathing as in FrenRider/ADS/
  LootGoblin, and measure XZ progress so a jump cannot hide a stall. Use native
  cancellation-capable queries and NPC-height projection. No separate retry
  system, settings or tracker.
- Force owned custom-delivery turn-ins through the specific scrip-cap warning;
  still require a native allowance change. Production must match planned turn-ins:
  147/150 means three items, less any eligible inventory and remaining allowances.
- Use the existing Mini/Jumbo Cactpot approach pattern for delivery vendors and
  turn-in NPCs: known waypoint first, then live-object resolution, spawn waiting,
  interaction range/close approach, menu/dialogue progression and verified UI
  opening. Do not fail immediately because an NPC is not yet visible.
- The user confirmed the Firmament fix worked and requested rank-quest handling:
  cap production at the next quest/rank boundary, run only the pending required
  quest through Wiggly Questionable and resume with refreshed requests/allowances.
  Reuse the native single-quest API and current task cleanup; add no saved quest
  tracker, priority-list changes, settings or provider fallback.
- NPC policies: closest verified progress below 150 with deliveries capped at
  150, or bonuses only. Respect character/NPC allowances. Unknown achievement
  progress stays unknown; data refresh must work with windows closed.
- Complete custom-delivery fishing through existing AutoHook presets/bait;
  require casting readiness, collectible inventory target and owned-state
  cleanup. Surface missing bait, gear and position prerequisites.
- I443 changes only the AutoHook configuration lookup. I435 cancels/releases
  interrupted VMX-owned AR work through existing cleanup; no new relog system.
- I436 requires a supplied log/reproduction; none is presently supplied.
- Debug x64 build in isolated output; inspect diff; no broad test suite.
- Account 1 (W:\) is the sole first-stage runtime target. Prepare the build,
  then request readiness before using existing Lazyparasite workflow. Disable
  VSatisfy through native plugin controls and verify unloaded before tests.
- The user keeps both existing and isolated native development paths. The
  marker-02 debug window identifies the isolated build; do not request removal
  of the older path. The shared watched source output remains untouched.
- I433 must be defined after first-stage acceptance, covering feeding,
  cleaning/broom stock and progression decisions; notify before FTP1 testing.
- No version bump, publication, source backups, extra reports or DevHub repair.

## Unfinished work

- [x] Custom deliveries: VSatisfy 1ab3f9f feature source imported; compatible
  clib 1.0.42 support source adapted under DeliverySupport without its package.
- [x] Custom deliveries: disabled-by-default per-character settings, overview,
  ordering, manual action and /vmx debug selection implemented and compiled.
- [x] Custom deliveries: policies, multiple types/jobs, eligibility/bonus
  indicators, native NPC indexing, character-correlated achievement samples
  plus observed allowance deltas, and independent framework refresh compiled.
- [x] Custom deliveries: Artisan purchases/crafting, Questionable gathering,
  and AutoHook rod fishing with qualifying collectible targets implemented.
  Task-owned AutoHook preset, bait, enabled/autostart and Collect state are
  restored through cleanup; new starts and normal engine handoffs wait for
  pending fishing cleanup. Runtime behavior remains unverified below.
- [ ] Custom deliveries: verify actual crafting, mining, botany and fishing
  deliveries, policy selection/caps, closed-window refresh and cleanup in game.
- [ ] Custom deliveries: verify corrected WigglyQuest dependency status and
  native gathering, cancellation and request ownership on Account 1.
- [ ] Custom deliveries: clean retest of the reported Idyllshire wall stall,
  automatic stuck recovery, arrival and cancellation of pending movement.
  Marker-05 vendor/NPC arrival now verified; obstructed repath and pending-query
  cancellation still need live verification.
- [ ] Custom deliveries: verify scrip-cap confirmation, actual allowance credit,
  and exact production near 150, including existing lower-quality eligible items.
  Marker-06 scrip-cap confirmation and six Adkiragh turn-ins are verified below;
  the near-150 partial quantity and lower-quality gathering cases remain pending.
- [x] Custom deliveries: native marker-07 Firmament route completed; the user
  confirms it worked and six Ehll Tou allowances plus successful cleanup are
  verified. Missing-spawn and out-of-range branches remain separate unverified cases.
- [ ] Custom deliveries: verify satisfaction-boundary production, required rank
  quest dispatch/completion/resume and quest cancellation through Wiggly Questionable.
- [x] Custom deliveries: normalize missing/default/saved/entered bait 0 to
  Versatile Lure (29717), preserving other custom values; marker-04 compiled
  and updated focused persistence regression passed. Fresh marker-04 startup and
  saved bait 29717 were observed; fishing consumption remains unverified.
- [ ] Custom deliveries: verify Artisan cancellation during queued pre-crafting,
  and AutoHook queued casts/rod-down/restoration during stop and cancellation.
- [ ] I443: current Configuration.C lookup implemented and compiled; existing
  accessor/save checks pass. Current-version Ocean Fishing live check pending.
- [ ] I435: disconnect cancellation/release implemented and compiled; verify
  interrupted VMX-owned AR postprocessing and normal fishing relog in game.
- [ ] I436: Cactpot missing-condition evidence and any justified narrow fix.
- [x] First stage: targeted Debug x64 compilation, focused checks and diff review.
- [x] First stage: Account 1 readiness, VSatisfy unloaded evidence and fresh
  loaded identity. The user confirms marker custom-deliveries-20260930-02 in
  the debug window; the fresh snapshot includes version 0.6.0.0.
- [ ] First stage: live data/selection, actual deliveries, fishing and
  cancellation verification.
- [ ] First-stage acceptance from the user.
- [ ] I433: define and implement stables, notify before FTP1 live testing,
  verify feeding/cleaning/stock/progression and obtain acceptance.
- [ ] I420: discuss agreed fishing outcome, research/build and verify.
- [ ] I421: discuss agreed fishing outcome, research/build and verify.
- [ ] I422: discuss agreed fishing outcome, research/build and verify.
- [ ] I423: discuss agreed fishing outcome, research/build and verify.
- [ ] I424: discuss agreed fishing outcome, research/build and verify.
- [ ] I425: discuss agreed fishing outcome, research/build and verify.
- [ ] I426: discuss agreed fishing outcome, research/build and verify.
- [ ] I427: discuss agreed fishing outcome, research/build and verify.
- [ ] I428: discuss agreed fishing outcome, research/build and verify.
- [ ] I452: AutoHook/Ocean Fishing reports a missing Service.Configuration
  setting after an AutoHook update (Clawdeen Wolf, 2026-09-30). Added to the
  fishing follow-up at the user's request. Likely overlaps I443's stale lookup;
  keep its reported save/setup symptom and current-version runtime verification
  pending. Supplied review packet dhp-review-3fbc979113607c4d9e3c is context only.
- [ ] I451: Verminion sometimes requires window focus (chatgpt9000,
  2026-09-30). Added to the fishing follow-up at the user's request despite being
  a separate Verminion issue. Identify the exact action and missing background
  input condition from fresh evidence before a scoped fix; preserve existing
  battle strategies. Packet dhp-review-b646af13acbb63153c04 is context only.

## Verification and blockers

- Fresh Git inspection: clean before this bundle; current version 0.6.0.0.
- Existing unrelated checkpoint was inspected and remains unchanged. Shared
  watched output will not be written during first-stage compilation.
- Initial isolated plugin build passed: zero errors and one existing NU1601
  PInvoke.User32 warning, 40.87 seconds. Command:
  `dotnet build VERMAXION\VERMAXION.csproj -c Debug -p:Platform=x64 -p:OutputPath=Z:\VERMAXION\VERMAXION.Tests\bin\Debug\CustomDeliveryPluginVerification\ --no-restore -v:minimal`.
- Artifact: `VERMAXION.Tests\bin\Debug\CustomDeliveryPluginVerification\VERMAXION.dll`.
  The first verified build used startup marker `custom-deliveries-20260930-01`.
  The pending isolated rebuild uses `custom-deliveries-20260930-02` and exposes
  the loaded marker/DLL path in the existing debug window for capped-log checks.
- Focused test command:
  `dotnet test VERMAXION.Tests\VERMAXION.Tests.csproj -c Debug --no-restore --filter 'FullyQualifiedName~AutoHookConfigurationAccessorTests|FullyQualifiedName~AutomationCatalogTests|FullyQualifiedName~CustomDeliveriesConfigurationTests' -v:minimal`.
  All 20 selected cases passed. Includes one new persistence/clone-isolation
  regression for delivery settings; catalog counts include the new task.
- Fresh `git diff --check` passed. Reviewed the host diff and imported source;
  no original support package or version change. Existing Ocean Fishing
  placement/cadence/recovery code, Verminion strategies and unrelated
  unrelated checkpoint behavior/history remain intact; personal-name references
  in existing repository text are being removed at the user's explicit request.
- Watched `VERMAXION\bin\x64\Debug\VERMAXION.dll` retains its 2026-09-30
  03:10:11 UTC timestamp. At marker-02 readiness, no bundle scenario had
  dispatched. No completed live delivery, cancellation or disconnect result
  is claimed; the later Run/movement failure is recorded below.
  No client control, client-file writes or DevHub mutation performed.
- Imported fishing positions are map centres and may require a suitable
  shoreline position/facing. Existing valid casting positions are preserved;
  unavailable bait, rod/gear or position is surfaced as a prerequisite failure.
  Spearfishing requests explicitly fail; automatic spearfishing is not delivered.
- Artisan's supported SetEnduranceStatus(false) cancels owned endurance, but
  CraftItem's queued pre-start work has no verified supported abort. Do not
  claim complete crafting cancellation until this window is verified/resolved.
- AutoHook cancellation keeps ownership while waiting for native rod-down and
  queued-action settling, with a visible unresolved-stop warning. Unload can
  only attempt restoration if framework updates end before rod-down; that
  native cancellation remains unverified. Live tests must inspect restoration
  and absence of a late recast before judging cleanup complete.
- I436 blocked on the promised missing log or a concrete reproduction.
- I451/I452 were added as later todo items, not authorization for a new
  DevHub workflow, multi-client test, backups or broad validation. No supplied
  packet evidence establishes current runtime success.
- Account 1 readiness is confirmed. The user disabled VSatisfy through native
  controls; the current troubleshooting snapshot excludes it from LoadedPlugins
  and PluginStates. The prior snapshot showed it loaded, so the unload is
  verified. Questionable is also loaded under internal name WigglyQuest.
- The 2026-09-30 11:54:42 UTC snapshot includes both the retained version
  0.5.0.3 development entry and the isolated version 0.6.0.0. The user confirmed
  the latter's marker-02 debug window. The main Dalamud log reached its 100 MiB cap and is stale;
  it cannot prove this attempt. Use fresh bounded troubleshooting snapshots and
  the existing debug/task UI, which now shows the compiled marker and DLL path.
- Only Account 1's native development entry may be changed for testing. The
  ready artifact is isolated from the shared watched output; do not overwrite
  the shared DLL or reload another client.
- Marker-02 isolated Debug x64 rebuild passed in 20.09 seconds, zero errors and
  the same existing NU1601 warning. The earlier 20 focused cases cover unchanged
  configuration/catalog/accessor code; no broad suite was run. Shared watched
  output remains untouched.
- Personal-name references were removed from this checkpoint and 32 other
  existing text files across VERMAXION, the two housing worktrees, AutoParty,
  DDuck and the documentation repository. No new report or source backup was
  created; Git history was not rewritten. Existing unrelated task content is
  retained. Use neutral user/operator references in future conversation/text.
- The user enabled custom deliveries on Account 1 and confirms the overview
  displays the expected status/details. A false missing-Questionable dependency
  warning was then reported. No completed delivery is established by that UI.
- Inspected installed WigglyQuest 7.5.27: its WigglyQuest.IsRunning IPC exists,
  but StartGatheringComplex and Stop IPC do not. The adapter uses its native
  GatheringPointRegistry and GatheringController Start/Update/Stop contracts,
  the same gathering calls used by its delivery controller. VERMAXION drives
  only its owned request and uses existing travel before gathering; replacing
  or unloading the provider cannot authorize stopping a different request.
- Marker-03 rebuild intent: isolated Account 1 output only, after bait-default
  and Wiggly gathering fixes. A fresh read-only snapshot still has DebugTaskId
  null and one enabled delivery character, with explicitly saved bait 0. This
  build may reload the isolated development entry; no debug action is armed.
  Shared watched output timestamp remains the baseline.
- Marker-03 isolated Debug x64 build passed in 22.39 seconds, zero errors and
  the existing NU1601 PInvoke.User32 warning. Artifact timestamp: 2026-09-30
  12:15:32 UTC. The focused CustomDeliveriesConfigurationTests case passed;
  it checks the new/missing-field default, explicit bait 0, custom bait JSON
  round-trip and independent copied job selections. Fresh diff whitespace
  validation and affected-source personal-name checks passed. Shared watched
  DLL remains at 03:10:11 UTC; version remains 0.6.0.0. Marker-03 load, corrected
  dependency UI, native gathering, deliveries and cancellation remain unverified.
- First delivery attempt: the user clicked Run and reports immediate wall
  obstruction in Idyllshire. Manual renavigation is a known workaround. The
  movement coroutine previously issued only once and waited for distance;
  it did not tick the existing navigation recovery helper. Dispatch/movement
  is user-observed; no completed delivery or precise loaded marker is established.
- The user intends to cancel and delete the attempt's items for a clean retest.
  The user subsequently confirmed both were done and requested DLL replacement.
  Cancellation is required before reloading active work; manual item cleanup is
  a retest choice, not a prerequisite for replacement.
- Marker-04 source uses a bait property that normalizes 0 on deserialization
  and assignment, removes the equipped-bait selection fallback, and reports
  the missing bait name/ID. Movement samples the existing recovery helper every
  500 ms while no pathfind is pending, retaining the existing cancellation/
  scoped navigation stop. Build verification will use unwatched output
  `VERMAXION.Tests\bin\Debug\CustomDeliveryCompilation\`; the normal Account 1
  development entry still points to CustomDeliveryPluginVerification.
- Marker-04 unwatched Debug x64 compilation passed in 21.85 seconds with zero
  errors and the existing NU1601 warning. Artifact timestamp: 2026-09-30
  12:35:13 UTC. All seven focused cases passed: the updated delivery-setting
  regression plus six existing ground-navigation recovery cases. No broad
  tests ran. Fresh diff whitespace validation passed. The Account 1 watched
  isolated DLL is still marker-03 at 12:15:32 UTC, and shared watched output
  is still 03:10:11 UTC. No reload or dispatch of marker-04 has occurred.
- After cancellation confirmation, replaced the DLL in the existing isolated
  Account 1 development output with the verified marker-04 compilation:
  CustomDeliveryPluginVerification\VERMAXION.dll, 2,392,064 bytes, artifact
  timestamp 2026-09-30 12:35:13 UTC. Copy succeeded. Shared watched output remains
  at 03:10:11 UTC. Actual marker-04 reload and the clean route retest remain
  unverified; no agent-controlled client actions or client-file writes occurred.
- Rechecked the replaced DLL directly with ILSpy: its compiled marker and
  DebugBuildMarker are both custom-deliveries-20260930-04. Artifact copy is
  verified; the earlier visible config window did not establish automatic reload.
- The user manually reloaded and clicked Run for the clean delivery retest.
  This is user-reported reload/dispatch, not observed completion. Fresh read-only
  Account 1 settings now serialize bait 29717 where the prior snapshot had 0;
  the bait correction is persisted. DebugTaskId remains null. Native dev settings
  confirm automatic reload enabled on the isolated added path and disabled on
  the retained old path. The troubleshooting snapshot remains at 12:09:28 UTC
  and cannot establish this attempt's load or delivery result. Do not replace
  the DLL during the current run.
- Clean retest failed: the user reports no repath and the character stopped in
  an apparently unrelated spot. Marker-04 movement recovery is not live-verified
  and must not be treated as a delivered fix. The exact task status and runtime
  build marker have not been supplied for this failure.
- Installed vnavmesh 1.2.3.14 inspection confirms that native MoveTo rejects a
  new request while its pathfind task is pending. VERMAXION's command return
  establishes command handling rather than that native acceptance; the direct
  SimpleMove.PathfindAndMoveTo IPC exposes the native bool. These are verified
  contract facts, not an established cause of this run's stall. Existing stall
  detection uses any 0.5-unit position change and resets when player availability
  is false; the current caller also skips checks during pending pathfinding.
  Need the native row's exact task status before selecting a correction.

- Fresh bounded Account 1 log inspection supersedes the earlier stale-log
  assumption: the current main log is readable (~17 MB). It shows marker-04
  startup at 08:39:13 local and manual reload at 08:48:47, Run at 08:48:57,
  Adkiragh crafting selected for six deliveries, purchases at 08:49:25-26, and
  repeated recovery/pathfinding to <-70.5, 193.75, 23.25>. Adkiragh's imported
  world position is <-60.36989, 206.5078, 26.17703>. Requests did dispatch and
  finish pathfinding, but used the wrong floor; arrival/turn-in failed. The
  existing five-minute watchdog cleaned the attempt at 08:55:21 local.
- Read current FrenRider FollowService, ADS ExecutionService and LootGoblin
  StateManager recovery code. All stop the previous route before repathing.
  Installed vnavmesh 1.2.3.14 confirms NearestPointReachable has bounded XZ/Y
  extents. PointOnFloor searches thousands of units down and excludes candidates
  above the probe. Path.Stop and /vnav stop stop following but cannot cancel
  SimpleMove's pending query. Native Nav.PathfindCancelable returns a cancellable
  Task<List<Vector3>>; Path.MoveTo follows only the task-owned completed result.
- Marker-05 implementation uses bounded NPC-height projection and raw NPC
  position for interaction arrival; fishing map centres retain floor projection.
  Existing recovery measures XZ progress for deliveries only. Every recovery
  stops the old path, then requests a new path from the current position.
  Cancellation reaches the native query and prevents following a late result.
  Movement diagnostics record requested/resolved target, current position,
  distance, path state, recovery and completion/arrival without per-frame logs.
- Marker-05 handles the specific English currency-compensation warning during
  the owned NPC turn-in, both before Request and after committing. Native Addon
  text rows 5452/12579/13481 establish the warning template. Arbitrary Yes/No
  prompts remain rejected. Each completed turn-in logs its allowance change.
  Other client languages and live scrip-cap behavior remain unverified.
- Production planning is shared with focused checks: 147/150 permits three,
  character/NPC/rank caps can reduce it, unknown progress permits no ClosestTo150
  route, and eligible inventory reduces crafting. Verified installed Wiggly
  Quantity is an inventory total at requested collectibility. Its request now
  adds only the missing eligible count to existing target-quality inventory,
  preventing lower-quality eligible inventory from causing extra production.
  Runtime gathering and exact Artisan production remain pending.
- Marker-05 unwatched Debug x64 compilation passed in 21.11 seconds, zero
  errors and the existing NU1601 PInvoke.User32 warning. Ten focused cases
  passed: delivery settings/count/prompt checks and ground-navigation recovery,
  including delivery jumps not resetting the stall window. No broad suite ran.
  Existing Ocean Fishing movement/cadence callers use the unchanged recovery
  default. Fresh diff whitespace validation passed.

- Replaced only the existing isolated Account 1 test DLL:
  `Z:\VERMAXION\VERMAXION.Tests\bin\Debug\CustomDeliveryPluginVerification\VERMAXION.dll`,
  2,402,304 bytes, artifact timestamp 2026-09-30 13:20:39 UTC. The replaced
  binary contains marker-05 and no marker-04. The build produced no separate
  PDB. The shared watched DLL remains at 03:10:11 UTC. No client files changed.
- Fresh finite log snapshot confirms Account 1 loaded marker
  `custom-deliveries-20260930-05` at 09:23:34.609 local (-04:00), version
  0.6.0.0. DebugTaskId was null before replacement. This proves reload, not a
  new delivery dispatch or completion. The latest prior task transition was
  SettlingTask -> SignalingARDone -> Idle after the failed marker-04 attempt.

- Marker-05 live retest selected Adkiragh at verified progress 144/150, planned
  six, eligible inventory zero. Artisan was dispatched for exactly six at
  09:32:35.586 local. Arrival at the vendor (09:32:33.057) and Adkiragh
  (09:33:47.928) is verified in the movement log, both on the correct floor.
  The user confirms NPC pathing works and will retain crafted collectibles.
- The live overflow prompt was `Unable to receive the following items:`
  followed by the purple/orange crafting scrip list and `Proceed?`. Marker-05's
  full-sentence matcher rejected it repeatedly; no delivery completion is
  established. The user requested matching only that stable phrase, with no
  item-list or final-question requirements. Marker-06 implements a
  case-insensitive substring match within the existing owned turn-in scope.
- The supplied /dd inspect invocation appears in Account 1's log at 09:33:56
  as DDuck read-only inspection. VERMAXION's existing prompt diagnostics
  independently establish the exact visible warning above.
- FULL STOP is verified at 09:35:08.624 local: RunningCustomDeliveries -> Idle,
  SelectYesno close callback, services reset, navigation stopped and owned AR
  suppression released. Keeping the crafted items requires no cleanup before
  DLL replacement. On the next Run, eligible inventory reduces production.

- Marker-06 unwatched Debug x64 build passed in 43.90 seconds, zero errors and
  the existing NU1601 warning. All three focused CustomDeliveriesConfigurationTests
  cases passed, including the exact observed prompt and variations in item list,
  placement and final question. Fresh diff whitespace validation passed.
- Replaced only the existing isolated Account 1 test DLL with marker-06:
  `VERMAXION.Tests\bin\Debug\CustomDeliveryPluginVerification\VERMAXION.dll`,
  2,402,304 bytes, artifact timestamp 2026-09-30 13:36:51.303 UTC. Compiled marker
  and copied marker are verified. Shared watched DLL remains at 03:10:11 UTC.
  DebugTaskId is null; no client action or client-file change was performed.

- Fresh bounded Account 1 log confirms marker-06 loaded at 09:37:30.024 local
  (-04:00), version 0.6.0.0. Prompt acceptance and completed turn-ins on this
  build still require the next native run.

- Marker-06 native Run at 09:45:31 local selected Adkiragh at 144/150 with six
  eligible collectibles already held and toProduce=0. The relaxed overflow
  matcher clicked the warning for each item. Native allowances verify all six
  turn-ins at 09:45:35-46; the next route selected Ehll Tou at 77/150 with six
  character allowances remaining. No additional Adkiragh crafting dispatched.
- The Firmament route travelled through Ishgard (territory 418) to territory 886,
  arrived at Anna's waypoint at 09:46:47.735 local, logged one native interaction
  at 09:46:47.771, then remained in the shop-opening wait. No NPC-not-found error
  is recorded. The old shop helper treated a missing immediate selector as success
  and never retried/progressed dialogue or a later menu while awaiting the shop.
  The user is teleporting elsewhere for a fresh test.
- Marker-07 replaces that single-attempt shop wait and the immediate missing
  turn-in NPC failure with the existing Cactpot approach/retry pattern. It resolves
  current objects by NPC base/instance ID, waits near known coordinates for spawn,
  closes within the existing native-helper interaction range, progresses Talk,
  handles the correct shop selector after it becomes visible and retries until
  the owned UI opens. Use the existing 90-second interaction wait/cancellation;
  add no separate tracker, setting or retry service. Interaction diagnostics
  distinguish loaded/targetable/range/menu/dispatch/opened states.

- Marker-07 initial unwatched Debug x64 compilation passed in 31.70 seconds,
  zero errors and the existing NU1601 PInvoke.User32 warning. Review identified
  that owned Talk/menu progression must precede the NPC targetable check because
  an event can temporarily make its NPC untargetable; corrected before deployment.
  A second targeted compilation will verify that correction. No native interaction
  coverage is claimed from the earlier configuration/quantity/prompt checks.
- Bounded Account 1 snapshot confirms the old Anna shop wait persisted after
  the user's teleport to territory 979. Existing cleanup ran at 09:55:45 local,
  then released owned AR suppression and returned to Idle at 09:55:47.257.
  Outcome was PartialFailure. DebugTaskId remains null. Teleporting alone did
  not stop the task; fresh native cleanup evidence establishes replacement readiness.

- Final marker-07 correction compiled to the unwatched output at 14:00:44 UTC.
  Captured final Debug x64 build verification passed in 4.77 seconds, zero errors
  and the same existing NU1601 warning. Reviewed both interaction callers and
  the current Cactpot close-approach helpers. Fresh diff whitespace validation
  and affected-file personal-name checks passed; version remains 0.6.0.0.
- Replacement intent: verify marker-07 inside the final compiled DLL, then copy
  only that DLL into the existing isolated Account 1 development output after
  checking the latest bounded task transition is Idle and DebugTaskId is null.
  The shared watched output and client files stay untouched. Confirm a fresh
  startup marker separately before judging the next native route.

- Replaced only the existing isolated Account 1 DLL with the final marker-07
  compilation. Latest bounded engine transition was SignalingARDone -> Idle
  at 09:55:47.257 local and DebugTaskId was null before the copy. The copied
  binary contains marker-07 and no marker-06. No agent-driven client action or
  client-file edit occurred. Native reload and the fresh route remain unverified.

- Fresh bounded Account 1 reload evidence confirms marker
  `custom-deliveries-20260930-07` at 10:02:14.934 local (-04:00), version
  0.6.0.0; native loading finished at 10:02:16.061. The isolated copied DLL is
  2,405,888 bytes, artifact timestamp 2026-09-30 14:00:44.711 UTC. The shared
  watched DLL remains at 03:10:11.863 UTC. Built, copied and loaded are verified;
  no fresh marker-07 route dispatch or Firmament vendor result is claimed.

- Marker-07 native retest verified six Ehll Tou deliveries at 10:06:50-10:07:03.980
  local (-04:00), followed by successful engine completion and Idle at
  10:07:10.616. The user confirms the route worked. The 12 weekly character
  allowances were used across the Adkiragh and Ehll Tou batches. Separate
  fishing, gathering, obstructed recovery and cancellation cases remain pending.
- Additional bounded marker-07 evidence verifies Anna's delayed menu dispatch at
  10:05:29.701 and owned shop opening at 10:05:30.330 local, purchases/closure,
  then an Artisan request for exactly six at 10:05:31.311. This confirms the
  delayed vendor selector path, actual crafting dispatch and the later native
  delivery completion, without relying on the user's report alone.
- Rank-quest research: the current Lumina SatisfactionNpc layout exposes
  RankParams[].Quest and SatisfactionNpcParams[].SatisfactionRequired. XIVAPI v2
  (https://v2.xivapi.com/api/sheet/SatisfactionNpc) confirms four rank-5 quest
  entries: Ehll Tou 3890 (An Ode to Unity), Anden 4716 (Every Anden of the Rainbow),
  Margrat 4816 (The Pride of Labyrinthos), Nitowikwe 5240 (The Weight of a Train).
  The generic guide (https://ffxiv.consolegameswiki.com/wiki/Custom_Deliveries)
  establishes the normal high-quality rank counts 3, 9, 15 and 21 cumulative.
  Runtime uses native satisfaction/reward/quest-completion data rather than
  hard-coding achievement totals or this NPC list. Local Wiggly quest paths
  include all four quests; its installed 7.5.27 single-quest IPC is verified.
- Marker-08 implementation caps each batch at the native satisfaction boundary,
  produces nothing while a rank quest is pending, dispatches that quest only
  through WigglyQuest.StartSingleQuest and resumes after native completion and
  cleanup. A gate reached by the last batch is handled even if weekly allowances
  are exhausted. Existing policies/jobs and later delivery allowance caps remain.
  Pending quest and satisfaction state are displayed in the existing overview.
- Marker-08 initial unwatched Debug x64 compilation passed in 32.31 seconds,
  zero errors and the existing NU1601 warning. Focused boundary checks and final
  ownership review are in progress before copying into the isolated test output.
- All four focused CustomDeliveriesConfigurationTests passed, including the new
  rank-boundary regression: two items remaining, existing inventory, allowance
  limits, a full satisfaction bar, pending-quest production zero and final-rank
  production after quest completion. These are planning/configuration checks;
  they do not establish native quest execution. Diff whitespace and affected-file
  personal-name checks passed. Ownership review added a character check before
  cancelling or continuing the single quest; recompilation verifies that change.
- Installed Wiggly's single-quest controller suppresses NextQuest transitions
  and stops when its selected quest finishes. No priority insertion or persistent
  Wiggly setting is needed for this handoff. Missing/locked routes, stop conditions,
  provider replacement and interrupted execution surface failures instead of
  allowing further production. Fresh native acceptance/stop evidence is pending.
- Final marker-08 ownership correction compiled successfully in 19.61 seconds,
  zero errors and the existing NU1601 warning. Artifact: 2,416,640 bytes at
  2026-09-30 14:27:03.280 UTC in CustomDeliveryCompilation. Reviewed the focused
  diff against current workspace commit 7dac549 (the prior bundle was committed
  externally during this turn); no Git mutation was performed by this work.
- Replacement intent: verify marker-08 in the compiled binary, check the latest
  Account 1 engine transition is Idle and DebugTaskId remains null, then copy
  only the existing isolated test DLL. The shared watched output remains untouched.
  Native rank-quest testing needs a character/NPC with a pending gate or fewer
  than six items to that gate; the just-completed character's weekly allowances
  are exhausted. Do not dispatch a new scenario or change another client.

- Copied the final marker-08 DLL only to CustomDeliveryPluginVerification after
  the latest Account 1 transition was SignalingARDone -> Idle at 10:28:31.518
  local and DebugTaskId was null. Copied marker-08 is verified; marker-07 is
  absent. Shared watched DLL retains 03:10:11.863 UTC. No agent-controlled client
  action, client-file mutation, version bump or publication occurred.

- Bounded reload evidence confirms marker-08 at 10:28:43.007 local (-04:00),
  version 0.6.0.0, with loading finished at 10:28:44.156. It was then unloaded
  at 10:28:54.602-55.703. The latest observed startup at 10:29:02.670 identifies
  version 0.5.0.3 and marker verminion-control-20260929-435. Thus marker-08 loaded
  once, but the current observed entry is older and cannot verify the new gate
  behavior. Retain both native development paths as the user chose; use the
  isolated entry with marker-08 for the next eligible test. Do not recopy the
  shared DLL or edit native development settings. Final whitespace/name checks
  passed; no native marker-08 quest dispatch/completion/cancellation is claimed.

Next: the user loads the isolated development entry with marker-08 before an
eligible rank-quest case. Native quest dispatch/completion, automatic delivery
resumption and FULL STOP remain unfinished. Other bundle verification and later
stages retain their existing order and acceptance requirements.
Actual delivery completion, fishing and remaining cancellation cases stay pending.
Later stages require first-stage acceptance and fishing discussion.
