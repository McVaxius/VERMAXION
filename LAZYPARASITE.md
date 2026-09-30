# Verminion bot development checkpoint

SELECTED MISSION CORRECTION IMPLEMENTED / FTP1 STAGE 12 RETRY ACTIVE:
The machinery interview is complete. The user's clarification chooses a saved
mission number (default 2, recommended) and separate repeat count. Do not ask
for that choice again. Source now uses MissionRepeat at existing mode value 1,
VerminionMission=2 and VerminionRepeatCount=5. The old weekly victory target is
ignored. Native Run starts a fresh matching-clear count; Resume/reload retain
it. Admission attribution excludes campaign and unrelated duties. Tutorial
replays count only a positively observed victory, never an inferred exit or
preexisting unlock. Weekly reset does not erase a manual replay's progress.
RunIsCampaign retains completed campaign intent for Resume while normal weekly
scheduling remains independent; reload cannot launch another mode instead.
legacy weekly targets cannot be resumed as new mission runs. FULL STOP guards,
purchase caps, current tactics and rosters are preserved. Only the enum reference
changed in VerminionBattleStrategy.cs; no battle decision or roster changed.
The isolated model test project passes 47 Verminion lifecycle cases and all
916 cases. Final plugin build passed in 19.85 seconds with zero errors and one
existing NU1601 warning. Its output is VERMAXION.Tests\bin\Debug\
VerminionPluginVerification\VERMAXION.dll. The watched DLL still has its
22:09:09 build timestamp; no native build/reload was requested during this
active campaign. Expected next marker: verminion-control-20260929-435;
release version remains 0.5.0.3.
The new mission UI and actual replay/stop behavior remain unverified in game.
FTP1's existing marker434 cleared stages 3-11 with positively credited native
victories (stages 1-2 were preexisting unlocks). Stage 12 then lost at 22:44:20,
22:48:29 and 22:52:47 EDT on 2026-09-29, stopped at the three-attempt limit at
22:53:01, and a later native Resume at 22:54:46 restarted the unchanged strategy.
Save at 22:57:48: campaign true, unpaused, mask2047, Stage12 attempts1,
pending17/duty563,16 weekly matches/9 wins. Later save at 23:08:09 has Stage12
attempts1, pending20/duty563,19 weekly matches/9 wins and remains unpaused.
This is another unchanged-strategy retry batch, not a verified Stage12 clear.
The unchanged strategy subsequently cleared Stage12: read-only save at 23:12:27
has mask4095, Stage13 attempts1, pending21/duty564,20 weekly matches/10 wins.
This verifies a later clear, not a reliable three-attempt Stage12 outcome.
No full FTP1 campaign certification.
Source review and git diff --check passed. Next: deploy through Z:\vmx.bat only after a
terminal or explicit stop boundary. Keep concurrent chocobo work intact.

434 UI LOADED / FTP1 STAGES3-5 WON / STAGE6 ACTIVE:
Z:\vmx.bat succeeded in51.33 seconds with zero errors/two existing NU1601
warnings. Native marker434 startup22:09:12.875 EDT on2026-09-29 and one debug
dispatch22:09:16.163 verified. The saved campaign resumed through its normal
path; no pause bypass or battle-code change was introduced. Updated button
labels/explanations are compiled and loaded; human visual review is pending.
Stage3 Victory credited at22:11:59.552 to5 matches/1 win. Stage4 Victory
credited at22:15:14.790 to6 matches/2 wins. Stage5 Victory credited at
22:18:03.566 to7 matches/3 wins. Each advanced to the next stage normally.
Read-only save at22:21:54 has mask31/nextUncleared6,CampaignRequested=true,
paused=false,Stage6 attempts1,pending match8/duty557,7 matches/3 wins,
zero gil/MGP spending. Existing stages1-2 were unlocked before this test;
those are not fresh observed victories. Stage6 has no verified result yet.
The campaign continued after weekly participation reached five matches,
providing fresh separation evidence for the permanent and weekly goals.
No further builds/reloads during the admitted campaign. Keep strategies and
roster fixed and retain normal three-attempt stage stops. All24 certification
and remaining broader acceptance are still incomplete.
The requested selectable mission with repetition count remains unimplemented.
Its standing machinery interview is the next decision: include after this
unchanged-strategy test, use existing Stage2 farming/manual replays, or skip.
Do not add saved fields or change repeat accounting before the actual choice.
The old campaign-choice blocker has been resolved by the native campaign start;
do not report the running campaign as blocked by that old missing selection.

434 UI CLARITY BUILD INTENT / FTP1 CAMPAIGN NOW SELECTED:
The user requests a clearly separate Complete or continue 24 CPU challenges
button and clearer labels for the weekly controls. Updated VerminionWindow
and MainWindow labels/layout; explain permanent campaign progress and selected
weekly behavior above the Run buttons. Expose the campaign button in Favorites
on its own line. Win X is now Stage 2 win farming; CPU rewards is now
Tournament checks / prizes. Existing enum/config values and all run/stop
callbacks remain unchanged. Added the canonical changelog entry and marker434.
No battle strategy, roster, accounting, purchase cap or retry changes.
The requested choose-a-mission/repeat-count feature is still unimplemented;
its additional saved settings/accounting require the standing machinery choice.
Do not silently equate this label correction with delivery of that option.
Read-only FTP1 save now has CampaignRequested=true, paused=false, mode1,
pending0/duty0,3 matches/0 wins,mask3,spending0. Native FULL STOP at21:56:37.286
was followed by an explicit native run at21:56:37.964. That start observed
the previously abandoned Stage2 battle without adopting its admission; its
Defeat at21:57:26.080 had credited=False. Result closure21:57:29.822 was
verified, then the service reached Failed with no matching saved admission.
This is a terminal closed result; no owned pending match remains. The user
has selected the campaign after stopping, so the prior missing-choice blocker
is resolved. Build with Z:\vmx.bat; existing debug Resume may continue that
saved campaign only through the ordinary pause, readiness and roster checks.
Expect native marker434/version0.5.0.3. Preserve concurrent chocobo/UI edits.
Diff check passed. Label-only changes need the build/reload and human visual
check; do not add implementation-mirroring UI tests or claim visual proof.

FTP1 WEEKLY START CLARIFIED / MANUAL COLLECTION STILL PENDING:
The user clarifies that Run weekly goal was clicked manually; the idle CPU
matches were the deliberately selected Participation behavior, not a wrong
DLL or unexpected choice. Preserve the earlier native reload/dispatch evidence
without interpreting the manual weekly choice as a campaign-start bug.
The user is manually acquiring/registering Zu hatchling and Wayward Hatchling.
No agent client action, source edit, configuration change or reload is needed
while that collection is pending.

FTP1 MANUAL MINION COLLECTION / USER-CONTROLLED FULL STOP:
The user will use FULL STOP and acquire minions manually. That action is
announced, not yet verified in native evidence. Do not build/reload, resume,
start purchases/quests or send client input while manual collection is pending.
The existing /vmx v Check planned minions button provides roster ownership and
manual acquisition sources. Wait for the actual return/start/resume instruction.
Read-only build identity verification found version0.5.0.3, marker433, native
loader startup21:36:34.622 EDT after Z:\vmx.bat updated the output DLL.
FTP1's configured enabled development entry is
D:\temp\VERMAXION\VERMAXION\bin\x64\Debug\VERMAXION.dll on its host; this
is the client-side workspace route, while the local build output is under Z:.
Native watcher reload and one debug dispatch were observed. The hardcoded
expected-path log is not used as proof of the physical assembly location;
the assembly reports unknown because the loader uses an in-memory load.
No new source/config change or client operation. Broader goal remains blocked
and incomplete; manual collection does not establish campaign completion.

FTP1 CAMPAIGN START STILL MISSING / BLOCKED AUDIT PASSED:
Read-only snapshot at21:43:35 EDT on2026-09-29 still has Participation,
CampaignRequested=false, paused=false, campaign stage/attempts0/0,
mask3/nextUncleared3,1 weekly match/0 wins and pending match2/duty553.
The requested campaign-start action has remained absent at three consecutive
revalidated boundaries:21:39,21:41 and21:43. The previous turn made progress
on explicit Participation defeat/cleanup evidence, but did not select or
advance the requested FTP1 campaign. No authorized source edit or native
agent control path can select it; no independent required build work remains.
Mark the broader goal blocked pending the existing native campaign-start
action, not complete. This changes agent goal status only: do not stop,
reload or claim a terminal result for the existing participation admission.
No code edits, additional log polling, client input or external config changes.
Do not repeat the already supplied /vmx v FULL STOP / Clear all challenges
instruction. Resume from fresh FTP1 evidence after the actual action/reply.

FTP1 FIRST PARTICIPATION RESULT VERIFIED / SECOND MATCH ADMITTED:
Native You Lose was observed at 21:41:21.741 EDT on 2026-09-29 with match
length3:36. Verified Defeat credited once at21:41:21.753:1 weekly match,
0 wins. Native result closure was verified at21:41:25.557. The existing
Participation run then admitted match2/duty553 at21:41:38.313 and observed
the Stage2 battlefield at21:41:44.986. This provides fresh FTP1 travel,
admission, explicit defeat accounting and result cleanup evidence; it does
not prove a campaign victory. The saved campaign choice remains false and
mask3 is existing unlock progress. No source edits, reloads or spending.
Previous goal turn was no progress toward campaign selection. This turn adds
positive native result/cleanup evidence, so the full goal remains active.
Do not interrupt/reload or infer results from the saved pending match alone.
The existing native campaign-start action remains pending; do not re-prompt.

FTP1 CURRENT ATTEMPT REVALIDATED / CAMPAIGN CHOICE UNCHANGED:
Read-only snapshot at 21:39:55 EDT on 2026-09-29 still has Participation,
CampaignRequested=false, paused=false, pending match1/duty553, zero credited
matches/wins and zero spending. Saved clear mask3/nextUncleared3 reflects the
previously observed menu unlock; neither Stage1 nor Stage2 has a fresh victory
in this FTP1 test. No later terminal native result appears in the bounded
attempt window; do not infer completion or restart the owned admission.
The previous goal turn made progress by completing Z:\vmx.bat and verifying
the fresh native reload/dispatch. No new build, source edit or client action
was taken in this continuation. The campaign-start action remains pending;
the existing participation run must remain under its normal lifecycle.
Do not re-prompt or change the saved goal/config externally.

FTP1 BUILD PASSED / FRESH433 RELOAD / PARTICIPATION MATCH ACTIVE:
Z:\vmx.bat succeeded in 27.37 seconds with zero errors and two existing
NU1601 warnings. The current worktree needed no additional source repair.
Preserved the concurrent chocobo model/UI edits; git diff --check passed.
Output DLL updated at 21:36:33; FTP1 native marker433 startup was recorded
at 21:36:34.622 EDT on 2026-09-29, version0.5.0.3 unchanged. One debug
dispatch at 21:36:39.050 followed native travel to Gold Saucer/Minion Square.
The service resumed the saved Participation goal, not a campaign request.
Existing menu availability proved sequential unlock through Stage3; this
does not constitute a fresh Stage1 or Stage2 observed victory. Match1/duty553
was positively admitted at 21:37:09.893 and Stage2 battlefield observed at
21:37:19.474. No new result/clear is claimed from admission or this build.
Do not reload again while the owned match is active. Campaign selection still
requires the existing native /vmx v action. While Participation is active,
the user can use FULL STOP followed by Clear all challenges to select/start
the requested campaign. External input/config edits remain revoked; no agent
control of that button is available. Preserve pause and uncredited cancellation
behavior. No strategy/roster changes, additional source edits or cap changes.
Personal names are excluded from new task records; prior new-entry prose
was corrected. FTP1 campaign testing and certification are still pending.

FTP1 BUILD FAILURE REPAIR AUTHORIZED / BUILD INTENT:
The user reports Z:\vmx.bat failing and asks to fix that first. Limit source
changes to the actual compiler/build failure; keep Verminion strategies, roster,
release version and marker433 unchanged. Preserve concurrent chocobo/UI work.
The latest FTP1 read-only save has no pending match/duty/acquisition and no
campaign request; no native campaign admission has occurred. Reproduce the
failure using the existing batch command before selecting a repair.
Keep personal names out of new task records; corrected the prior entry below.

FTP1 TEST SELECTED / NO CODE EDITS / CAMPAIGN START ACTION NEEDED:
The user selected one more complete character test on Free to Play 1, with
updates through Z:\vmx.bat and no code edits. Current routing resolves FTP1
to Y:\; do not return to C:, R3 or R7. Read-only global configuration confirms
Enabled=true, DebugTaskId=verminion_queue and a character-bound selection.
Native marker433 logged Pending: next plugin reload at 21:29:14.181 EDT on
2026-09-29. The matching current account character has Participation mode,
paused=false, zero campaign clears/matches/wins, pending match/duty 0/0,
CampaignRequested=false and zero gil/MGP/certificate purchase caps.
This debug action calls ResumeTask, which resumes CampaignRequested; rebuilding
now would start the participation goal rather than request challenges 1-24.
No build or client action dispatched. No source/config edits or spending.
Existing /vmx v -> Clear all challenges is the native campaign start and saves
the campaign choice for later reloads. No verified native agent command
transport exists for this button; external input/config edits stay revoked.
Request that single existing UI action, then observe the native campaign and
its normal three-attempt stage limits through bounded milestone snapshots.
Missing roster or purchase requirements must be reported from actual game
evidence; do not silently raise this character's zero caps or alter strategies.
The previous target blocker is resolved; this is the first campaign-start
blocker boundary of the resumed FTP1 test. Broader goal remains active.
Preserve unrelated worktree changes, including the new ConfigWindow.cs edits.

PENDING TARGET REVALIDATED / BLOCKED AUDIT THRESHOLD REACHED:
The next-character question was sent once after marker433 reconciliation;
no character/readiness answer has arrived. This continuation revalidated local
PID3228 as live and the single saved character as24/24(mask16777215),
21matches/13wins,pending0/duty0,campaignRequested=false,nextUncleared25.
Previous goal turn was no progress: it confirmed unchanged completion and
asked for the missing target. The same target/evidence blocker is present at
three consecutive boundaries:433 checkpoint, that turn, this continuation.
No active Verminion attempt is awaiting completion and no independent approved
implementation/build work remains at this boundary. Mark the broader goal
blocked, not complete; resume once the next C: character and readiness are
supplied. Keep first-time setup/tutorial, human window verification, remaining
live lifecycle boundaries and tournament conditions explicitly unverified.
No new build, match, client control, external config change or spending.

433 UX REFRESH LOADED / COMPLETED-GOAL RELOAD VERIFIED / NEXT TARGET NEEDED:
Z:\vmx.bat succeeded45.40seconds, zero errors/two existing NU1601 warnings.
Exact433 native startup21:05:58.627EDT2026-09-29; one debug dispatch
21:06:05.166. Existing weekly participation goal completed immediately,
releasing YesAlready, with no new admission or credit. Read-only save still
full24mask16777215,21matches/13wins,pending0/duty0,Stage24 attempts3,
spending2400gil/10000MGP. Final diff check passed. Two preview-string updates
are compiled and loaded; window visual verification is still pending.
Input failure investigation found no log entries from any source20:30:58
through20:31:07. Last queued click20:30:57.919 and failure20:31:08.092 bound
the completion gap; this does not establish why the client/input stalled.
Leave the existing timeout/control behavior unchanged without stronger evidence
and the required scoped approval. No more campaign runs or builds needed now.
Previous turn made progress: corrected stale UX evidence, built/reloaded433,
verified completed-goal suppression and clarified the unresolved input evidence.
Remaining runtime acceptance needs first-time setup/tutorial character,
human window check and open tournament conditions; Join remains disabled under
the user's current tournament UI-only scope. Await a named next character on
the authorized C: client and its logged-in readiness; do not switch characters,
control focus/input, alter external configs or revisit R3/R7 on stale goal text.
This is the first pending-target blocker audit turn; keep the broader goal
active, not complete or blocked yet. Ask once, wait for the actual answer.

ACCEPTANCE AUDIT / UX EVIDENCE REFRESH433 BUILD INTENT:
Read-only diagnosis of attempt1:queued double-click20:30:57.919EDT; no recorded
native completion before failure20:31:08.092. Whole-log timestamp counts also
show no entries20:30:58 through20:31:07. This establishes a logging/input
completion gap, not its cause; no timeout, input, rendering or strategy edits.
Existing accounting regressions cover cancellation/unknown result rejection,
partial participation, weekly reset, character cloning and winning limits.
Historical224 farming matches118-127 have ten consecutive Mammet Stage2 wins
and exact27->37victory stopping; the later local pass reused existing progress
and stopped10/10. These are separate from this local Stage24 clear.
Current UI still says Stage15 revised final-phase pursuit awaits verification,
although425 replay positively cleared with that code. Correct that text and
update Stage24's existing preview with this latest third-attempt8:10 clear,
the uncredited input failure/defeat and remaining repeated-win limitation.
Change only those two UI strings and marker432->433. Preserve all unrelated
source changes; no new machinery, files, dependencies or release version.
Before build read-only current save:mode0(Participation),target5,matches21,
wins13,campaignRequested=false,paused=false,full24mask,pending0. Native campaign
Complete is confirmed and no subsequent manual-service start is in the bounded
log. Existing debug weekly Resume must finish without admission/credit.
Build with Z:\vmx.bat and verify exact433 startup/normal completed-goal stop.
Remaining runtime gates need suitable first-time character, human window
verification and available tournament conditions. Tournament Join is still
intentionally disabled per the user's UI-only testing scope; full original
CPU tournament execution is not delivered or accepted. Do not claim overall
goal completion. Next input needed: target character/readiness for fresh setup.

STAGE24 WON ATTEMPT3 / CURRENT CHARACTER24OF24 / CAMPAIGN STOP VERIFIED:
Native You Win20:49:52.900EDT2026-09-29,matchlength8:10; verified Victory
credited once20:49:52.948 to21matches/13wins. Native completedStages25 and
saved full24-stage mask16777215,pending0/duty0 confirm campaign completion.
Observer released79ground commands; result closure20:49:58.521 verified.
Service InspectingProgress->Complete20:50:03.633:All24 challenges are verified
complete; YesAlready ownership released. No fourth admission or further
reload. Gentleman roster/targeting/ability/reinforcement decisions stayed at
425; only the approved warning observation/cancellation repair changed.
Reviewed run:attempt1 input-settlement failure,unresolved/uncredited;
attempt2 verified Defeat;attempt3 verified Victory. Warning lifecycle/tower
arrivals/cleanup and no false credit have fresh runtime evidence. The existing
input-settlement failure remains unresolved; one successful3-attempt run does
not prove repeated reliability or eliminate that limitation.
Final watched marker432,version0.5.0.3; all912VMX tests passed for the warning
build before subsequent unrelated source edits. Final git diff --check passed.
Save21matches/13wins,GilSpent2400/MgpSpent10000. No external client input or
config mutation. Preserve unrelated CharacterConfig/ChokeAbo changes.
Check planned minions is implemented and compiled; visual verification remains
pending. Next human character test can use /vmx v to check planned minions and
their manual acquisition sources before starting. Fresh localStage1 replay,
first-time unlock execution and live tournament acceptance remain pending;
broader goal is incomplete and must not be marked complete from this milestone.

FINAL ATTEMPT3 REVALIDATED LIVE / BAHAMUT1709HP:
After automatic continuation, local PID3228 remains live. Read-only snapshot
20:49:29EDT2026-09-29 confirms pending35/duty575,Stage24 attempts3/unpaused,
23/24clears,20matches/12wins. Latest native order20:49:23.814:Bahamut1709/20000,
SendWave,ready4/deployed3,capacity210/240. More tower arrivals verified at
20:47:55.533,20:48:31.357,20:49:12.728. No Victory/Defeat yet; preserve owned
admission and bounded controls. New unrelated worktree changes appeared in
CharacterConfig.cs and ChokeAboTargetCycleModels.cs; preserve them. No build
or runtime control changes. Previous goal turn made progress: warning fix,
912tests, native observer/tower proof and bounded retry/result evidence.

ATTEMPT2 VERIFIED DEFEAT / FINAL NATIVE ATTEMPT3 ACTIVE:
Native You Lose20:40:52.628EDT2026-09-29,matchlength6:15; verified Defeat
credited participation once20:40:52.701 to20matches/12wins. Observer released
with50native ground commands20:40:52.718; result closure20:40:57.798 verified.
Existing automatic sequence admitted match35/duty57520:41:10.596, battlefield
20:41:16.972. Saved attempts3/unpaused,clears23/24(mask8388607),pending35/575.
No edits/reloads/budget reset or strategy/roster changes. Observer/tower fixes
are live verified; Stage24 victory remains unverified. Let final attempt run;
a third unsuccessful attempt must preserve the existing paused limit stop.

432 RECOVERY VERIFIED / STAGE24 ATTEMPT2 ACTIVE:
Marker-only build succeeded30.54seconds, zero errors/two NU1601 warnings.
Exact432 native startup20:33:43.107EDT2026-09-29, one debug dispatch
20:33:51.949. Native return to inventory/progress/setup was positively
observed; no unmatched result credit manufactured. Attempt2 admitted
match34/duty57520:34:04.904; battlefield20:34:11.212. Saved20:34:50:
unpaused,attempts2,pending34/duty575,23/24clears(mask8388607),19matches/12wins.
Existing stage budget retained, no reset or strategy/control changes. Let
attempt2 finish; if needed only one admission remains in this reviewed run.

ATTEMPT1 INPUT FAILURE /432 NATIVE RECONCILIATION BUILD INTENT:
Native failure20:31:08.092EDT2026-09-29:Background battlefield input did not
settle; no result recorded. Observer released with62ground commands and
cleared its battle-local state. Last Bahamut4348/20000; no Victory/Defeat.
Saved state20:32read:unpaused,Stage24 attempts1,pending0/duty0,
23/24clears(mask8388607),19matches/12wins unchanged. The abandoned native
match33 may remain in battle/result; do not adopt it or manufacture credit.
Marker-only431->432 and Z:\vmx.bat invoke the existing debug Resume path.
It must passively observe an unmatched battle/result for cleanup without
control/credit, or prepare the next remaining attempt only after positive
return. Do not reset the budget or change control/strategy/roster. This is
the established unresolved-attempt recovery used earlier in the frozen pass.
Native warning lifetime/tower-arrival fix already has positive evidence;
the input-settlement failure is an existing independent terminal path.

431 NATIVE TOWER LIFECYCLE AND ARRIVALS VERIFIED / ATTEMPT1 ACTIVE:
Tower slot14/effect65 created20:26:38.484EDT; visual array empty20:26:38.581;
native effect0 removal20:26:58.590 (20.1seconds). No premature clear transition.
Second tower retained a safe pending order across circle removal20:27:13.798;
verified individual movement20:27:15.170 and26.123. Later tower arrival observed
20:27:51.838,healthy400/400; other attackers received individual combat orders
while protecting that occupant, native removal20:28:08.554. Another healthy
tower arrival20:28:30.866, removal20:28:43.726. This verifies full tower
warning retention, native removal, movement and arrival under current rendering
settings. Existing battle decisions unchanged. Bahamut7078/20000; one remaining
visible Twintania1266/2280 at20:28:40.710, normal orders continue. No result
or Stage24 clear yet. Continue admitted33/duty575 without reload/source changes.

431 NATIVE CIRCLE LIFECYCLE VERIFIED / ATTEMPT1 CONTINUES:
First captured circle20:25:21.352EDT:slot11,effect64,validated path/geometry,
known=true/active1. Visual array empty20:25:21.439, but circle escape retained
through20:25:23.445. Actual native effect0 removal20:25:28.299, clear transition
20:25:28.333; about6.95seconds, consistent with historical7second lifetime.
Two later circle create/remove cycles also observed with correct retention.
This verifies native callback ABI, scoped director ownership and warning
retention/removal for circles under the current rendering settings. It does
not prove DPS causation, tower arrivals or Stage24 victory. Last readback
20:26:02.506:8Gentlemen,capacity240/240,Bahamut18635/20000. No edits/builds,
input/config changes or result credit. Continue native attempt1 to tower/result.

431 NATIVE RESUME ACCEPTED / STAGE24 RETEST ATTEMPT1 ACTIVE:
User clicked Resume. Native preparation20:24:36.058EDT, Stage24 selection
20:24:41.189 and battlefield transition20:24:50.3452026-09-29 observed.
Read-only saved state20:24:57:unpaused,attempts1,pending33/duty575,
23/24clears(mask8388607),19matches/12wins,spending2400gil/10000MGP.
No new result credited. Native ground-command hook/callbacks and corrected
warning lifetime are not yet in this early snapshot. Let the existing native
attempt and retries run; do not build/reload during its owned admission or
change strategy/roster. Next bounded milestone checks first ground creation,
removal and preserved tower-order execution.

431 RELOAD VERIFIED / WAITING FOR ONE NATIVE RESUME ACTION:
Z:\vmx.bat succeeded23.48seconds, zero errors/two existing NU1601 warnings.
All912/912 VMX tests passed; final git diff --check passed. Exact431 native
startup observed2026-09-29 20:21:06.655EDT, version0.5.0.3 unchanged.
Debug selection verminion_queue consumed into paused observation20:21:12.248:
duty0,resultUnknown,pending0,19matches/12wins,no admission or result credit.
Read-only saved state confirms paused=true,Stage24 attempts3,clears23/24
(mask8388607),pending0/duty0,GilSpent2400/MgpSpent10000. Reload preserved the
stage-limit pause. Runtime nativeGroundCommand callbacks/removal duration and
the corrected Stage24 replay remain unverified; do not claim a clear or repair
verification from compilation/unit tests. No external input/config mutation.
Next: user clicks Resume in /vmx v once; normal native Resume resets the
reviewed stage budget and starts up to three attempts. Observe native warning
creation/clear, retained tower orders/arrivals, explicit results and terminal
stop through bounded milestone snapshots. Keep425 battle decisions/roster
unchanged. No further code/build/reload needed to resume this paused task.

STAGE24 WARNING FIX APPROVED /431 BUILD INTENT:
User said yes and /goal resume to repairing warning observation/cancellation
and retesting with the existing Gentleman roster and normal three-attempt limit.
Implement a Stage24-scoped native E7CA10 observer using the disassembled
director/slot/effectId/x/z/scale ABI. Always forward Original. Only process
the validated current director, framework thread, owned active task and existing
540second window. Capture validated paths/geometry after creation; retain them
through empty render snapshots until native removal or replacement. Seed visible
existing warnings once on attachment; unknown creations hold commands, not a
confirmed clear. Cleanup disposes the observer and clears battle-local state.
No roster/targeting/reinforcement/ability changes; release0.5.0.3 unchanged.
One focused regression added; all912/912 existing VMX tests passed with the
attachment case included (6seconds); git diff --check passed. First build failed
because API15 Hook<T> has no static FromAddress; use the plugin's existing
GameInterop.HookFromAddress API instead. Old DLL remained untouched. Rebuild
through Z:\vmx.bat next. Expected compiled marker
verminion-control-20260929-431, build via Z:\vmx.bat, watched artifact
Z:\VERMAXION\VERMAXION\bin\x64\Debug\VERMAXION.dll. Local C: client PID3228,
root %USERPROFILE%\AppData\Roaming\XIVLauncher. No C: DLL copy needed.
DPS legacy foreground no-render is configured; causation remains unproven.
Current saved paused=true/23clears/Stage24 attempts3/pending0. The paused
reload guard remains intact. A native explicit Resume must reset the reviewed
stage budget; there is no approved external input/config path or verified
native command transport to perform that action from the agent. Finish build
and reload verification before asking for that single native action if needed.
No external client input/config mutation, DPS changes, vendor work, new scripts,
reports, dependencies or signatures; goal remains incomplete.

STAGE24 THIRD DEFEAT / CAMPAIGN LIMIT PAUSED; PURCHASE FIX DELIVERED:
Native You Lose2026-09-29 19:54:16.208EDT; verified Defeat credited
participation once19:54:16.254 to19matches/12wins. Result closure verified
19:54:21.168. Existing stage-limit guard stopped19:54:35.682 before a
fourth admission. Saved paused=true,stage24 attempts3,pending0/duty0,
23/24 clears(mask8388607),GilSpent2400/MgpSpent10000,purchase/acquisition
both null. Do not reload to bypass the pause or reset the attempt budget.
Local frozen425 pass:Stage20 cleared attempt3;21,22,23 cleared attempt1;
24 defeated all3. No battle-decision/control/roster changes were made.
ADS invariant confirmation and VMX explicit reserved-purchase recovery are
implemented, built and positively verified through one native purchase and
registration. Recorded checks:173/173 ADS shop tests and42/42 Verminion
lifecycle tests passed; full911-test suite was not run (prior425 suite910 passed).
Current VMX430/ADS34 markers and release versions are unchanged. Git diff
--check passed for VMX; source changes remain uncommitted. No external client
input or config mutation was used. Check planned minions is implemented in
425; visual verification remains pending. Fresh localStage1 replay,first-time
unlock and live tournament acceptance remain pending; overall goal incomplete.
Next required decision: permission to repair Stage24 warning observation/
cancellation before another native retest with the existing Gentleman roster.
Reader-loss cause is not yet proven; the short-lived warning and cancelled
tower orders are observed. User froze battlefield/control changes, so leave
the bot paused while this decision is pending. Existing persisted goal status
is blocked with its obsolete R3 objective; do not claim overall completion or
create a duplicate goal.

STAGE24 READ-ONLY FAILURE DIAGNOSIS / STRATEGY STILL FROZEN:
Attempt2 directorGround snapshots show each tower present then an empty
effect list about0.14-0.19seconds later (19:46:58.697/.838,
19:47:33.660/.808,19:48:08.664/.809,19:48:43.811/44.005).
The corresponding ground-warning transition clears the pending tower
selection/movement in ObserveFinalStageOmenTransition before it can complete;
no successful tower-arrival evidence was found in this bounded attempt window.
This is an observed cancellation path, not proof of why the native reader
loses the warning. No reader/control/strategy correction has been authorized
or implemented. Let attempt3 finish, then respect the existing stop if it fails.

STAGE24 ATTEMPT2 DEFEAT / FINAL NATIVE ATTEMPT3 QUEUED:
Verified Defeat2026-09-29 19:48:55.417EDT credited participation only:
18matches/12wins,clears23/24 retained. Bahamut11537/20000 and both
Twintania adds still alive at result; all friendly stones gone. Native
retry admitted match32/duty575 at19:49:16.838,stage24 attempts3/unpaused.
No budget reset, strategy/control change, reload or build. Battlefield entry
is not yet in this snapshot. Let the final admission settle; a third failure
must retain the existing paused stop rather than trigger further work.

STAGE24 ATTEMPT1 DEFEAT / NATIVE ATTEMPT2 ACTIVE:
Verified Defeat2026-09-29 19:44:23.095EDT credited participation only:
17matches/12wins,23/24 clears retained. Exit to progress19:44:27.365;
normal sequence admitted match31/duty575 at19:44:40.323,battlefield
19:44:48.172. Saved attempts2/unpaused; no strategy/control/source edits,
reload or budget reset. This verifies a normal frozen-strategy retry after
explicit defeat. Let attempt2 run; preserve the three-attempt stop if needed.

STAGE23 WON FIRST ATTEMPT / STAGE24 FIRST ATTEMPT ACTIVE:
Native You Win2026-09-29 19:39:22.272EDT; verified Victory credited once
19:39:22.307 to16matches/12wins. Result closure19:39:26.864. Stage24
admission match30/duty575 saved19:39:40.229,battlefield19:39:48.043;
attempts1/unpaused,clears23/24(mask8388607). Bounded19:41:23 readback shows
Bahamut15814/20000HP and friendly stones5000/4880/4460. No strategy,
roster,control,build/reload or budget change since425. Final stage retains
its540second control window and600second explicit-result timeout. Do not
interrupt the owned admission; await phases/result and native terminal stop.

STAGE23 FIRST ATTEMPT ADMITTED:
Match29/duty574 saved2026-09-29 19:36:33.622EDT; battlefield19:36:41.321.
Bounded19:37:38 readback shows Clockwork Twintania16848/18000HP and friendly
stones4855/4400/5000. Saved22/24,15matches/11wins,pending29/574,attempts1,
unpaused. Native frozen Gentleman strategy is active; no result yet. Preserve
the owned admission and wait for its result/Stage24 transition without builds.

STAGE22 WON FIRST ATTEMPT / STAGE23 TRANSITION:
Native verified Victory2026-09-29 19:36:15.882EDT credited once15matches/11wins.
Exit reached progress inspection19:36:20.478; clears22/24(mask4194303),pending0,
unpaused. Stage23 preparation19:36:25.596 and menu selection19:36:28.690
observed; its admission is not yet in this snapshot. No source/control edits,
build/reload or retry-budget reset. Let native Stage23 and24 run and verify
their admissions/results. All spending/reservation evidence remains intact.

STAGE21 WON FIRST ATTEMPT / STAGE22 ACTIVE:
Native verified Victory2026-09-29 19:33:16.955EDT credited once14matches/10wins.
Exit reached progress inspection19:33:21.983. Stage22 admission match28/duty573
saved19:33:35.572, battlefield19:33:43.654,attempts1/unpaused. Permanent clears
21/24(mask2097151). Strategy/control code still unchanged; no reload or
counter reset. Next observe Stage22-24 through the existing native sequence.

STAGE20 WON ON FROZEN-STRATEGY ATTEMPT3 / STAGE21 ACTIVE:
Native You Win at2026-09-29 19:30:16.626EDT, match length3:37. Victory
credited once19:30:16.663:13weekly matches/9wins. Result closure verified
19:30:21.102; permanent clears20/24(mask1048575). Stage21 admission
match27/duty572 saved19:30:34.026, battlefield19:30:42.326,attempts1/unpaused.
The first two Stage20 failures remain unresolved/uncredited; the third
existing attempt cleared without a strategy, control or budget change.
No further build/reload is needed while native campaign transitions run.
Spending2400gil/10000MGP and cleared purchase/acquisition stay verified.
Next: observe Stage21-24 results and transitions; preserve each stage limit.

430 LOADED / STAGE20 FINAL ATTEMPT ACTIVE:
Marker-only Z:\vmx.bat succeeded23.28seconds, zero errors/two existing NU1601
warnings. Native430 startup2026-09-29 19:25:44.713EDT; one debug dispatch
19:25:51.436. Stage20 admission match26/duty571 verified19:26:04.676,
battlefield19:26:12.655. Bounded19:29 snapshot shows live background selection,
camera and movement commands, pending26/571,stage20 attempts3,unpaused.
Saved19/24(mask524287),12matches/8wins; purchase/acquisition cleared and
spending2400gil/10000MGP retained. No new result or clear is observed yet.
Strategies remain the425 baseline. Let the owned admission settle without
reload/build; if it fails, preserve the existing three-attempt paused stop.

STAGE20 ATTEMPT2 UNRESOLVED / FINAL NATIVE RETRY430 BUILD INTENT:
Second frozen-strategy admission match25/duty571 at19:20:18.134EDT, battlefield
19:20:24.931. It stopped19:22:52.688: opening battle group unavailable; no result
recorded. Saved19/24,12matches/8wins,pending0,attempts2,unpaused. Neither failed
attempt proves Victory/Defeat; no match/win/clear credit manufactured.
Change compiled marker429->430 only, build through Z:\vmx.bat. Existing native
Resume may observe/close an unmatched result, stopping without credit, or
prepare the final allowed admission after positively observing return. Never
rebuild during an owned active admission, change strategy or reset the budget.
If attempts3 fails, respect its existing paused stop and report the evidence.

429 LOADED / EXISTING NATIVE RETRY PREPARING STAGE20:
Marker-only Z:\vmx.bat succeeded30.57seconds, zero errors/two NU1601 warnings.
Native429 startup19:19:54.467EDT; one debug dispatch19:20:05.159. Start observed
the character outside the old battle and used normal inventory/progress setup;
no unmatched result credit or manufactured admission. Stage20 selection
19:20:13.402 follows, with12matches/8wins and19/24 still recorded. Verify the
new queue and its attempt count before judging this retry. Strategy remains
the425 baseline; no selection/reinforcement change, no reset/budget bypass.

STAGE20 ATTEMPT1 UNRESOLVED / NATIVE CLEANUP429 BUILD INTENT:
First frozen-strategy attempt stopped19:17:16.973EDT: no selectable unit remains
in the battle group. No result was observed/credited; matches12/wins8,mask524287,
attempts1,pending0,unpaused. The native battle/result may remain open.
Use marker-only429 build via Z:\vmx.bat to invoke the existing debug Resume.
Its unmatched-admission path observes the abandoned battle without control or
credit, closes an observed result, and stops. Verify positive return before any
fresh admission. Do not edit selection, replacement tactics, roster or other
battle strategy; do not reset/bypass the existing three-attempt budget.
Purchase recovery is already verified, with pending purchase/acquisition cleared
and spending2400gil/10000MGP. No additional vendor request is needed.

428 NATIVE RECOVERY AND GIL PURCHASE VERIFIED / STAGE20 ATTEMPT1 ACTIVE:
42/42 VerminionLifecycleTests passed (3seconds). Z:\vmx.bat succeeded30.36
seconds, zero errors/two existing NU1601 warnings. Exact428 startup2026-09-29
19:14:46.626EDT; one native debug dispatch19:14:53.968, version0.5.0.3 unchanged.
Recovery19:14:54.028 verified unchanged299769gil/1068556MGP/items0 and retained
the original reservation. ADS accepted one original-ID guarded request19:14:59.120.
Native owned Yes for item6005/2400gil succeeded19:15:02.410; exact one-item
purchase completed19:15:02.687. VMX positively verified/credited the purchase
once19:15:03.378 and registration19:15:11.627. Both pending receipt/acquisition
are cleared; cumulative spending2400gil/10000MGP. This verifies the ADS34 numeric
fix and VMX428 recovery through the ordinary providers, without external input.
Stage20 native admission match24/duty571 verified19:15:25.659; battlefield
19:15:34.472. Saved snapshot: unpaused19/24,12matches/8wins,Stage20 attempts1,
pending24/571. All battle strategies remain the425 baseline. No reload/build
during this fight; let normal stage retries and transitions run. Next bounded
milestone checks Stage20 result/exit and Stage21 admission. Campaign20-24 and
fresh local Stage1 replay remain unverified; first-time unlock/tournament checks
also remain pending. Native FULL STOP during this recovery is not a new verified
case, although the existing lifecycle regression suite passes.

USER INCLUDED NATIVE RESERVED-PURCHASE RECOVERY /428 BUILD INTENT:
After the cost/benefit choice and reload proof, user explicitly said 'so do it'
to resolving the saved ADS purchase through the native route. Implement the
narrow recovery in existing coordination/progress, with one focused regression.
An explicit native Run/Resume may recover once only when ADS reports empty
idle state, minion identity/ownership/inventory and currency before-state agree,
and current caps/reserve still authorize it. Reservation and original operation
ID remain; ADS owns all vendor travel/UI/submission and validates the quote
again. Unknown, changed, active or unrelated provider state stays unsuccessful.
No spending or match/clear credit until positive receipt/result evidence.
Build428 through Z:\vmx.bat, unchanged0.5.0.3; run existing Verminion tests.
Native selected debug hook should reconcile then send one guarded ADS request.
No strategy, roster, battlefield control, retry limit or external input change.
Before build local19/24,pending match0,unpaused; item6005/2400gil receipt and
dispatched acquisition retained. ADS34 culture-invariant matcher already loaded.

C: CLIENT DLL AUTHORIZATION / DIRECT DEVELOPMENT OUTPUT VERIFIED:
User confirmed this local C: client permits DLL replacements and related
build/reload work. Its enabled VERMAXION development entry points directly to
Z:\VERMAXION\VERMAXION\bin\x64\Debug\VERMAXION.dll, with AutomaticReloading
and StartOnBoot true. PID3228 responding. Building updates the exact watched
DLL; no separate C: copy is needed for this configured route.427 startup and
native dispatch are already verified. This does not approve strategy changes
or expand external keyboard/mouse control. Purchase recovery choice remains
pending; current handoff preserves the unresolved reservation.

427 MARKER-ONLY BUILD / RELOAD AND NATIVE DISPATCH VERIFIED:
Z:\vmx.bat succeeded26.83seconds, zero errors/two existing NU1601 warnings.
Exact427 native startup2026-09-29 18:52:50.965EDT, version0.5.0.3 unchanged.
Saved debug action consumed18:52:58.975 and dispatched18:52:59.229 once.
Normal saved-acquisition reconciliation stopped18:52:59.240 with the same
lost ADS operation status; no purchase was resubmitted and receipt remains
unresolved. Requested rebuild/reload is complete; purchase recovery was not
implemented or implicitly approved. All battle strategies remain unchanged.

USER-REQUESTED MARKER-ONLY RELOAD427 / BUILD INTENT:
User explicitly requested a pointless textual edit and recompile to force reload.
Change only the compiled Plugin.DebugAttemptMarker426->427 and run Z:\vmx.bat.
Keep version0.5.0.3, all strategies and attempt limits unchanged. Saved native
verminion_queue selection remains the continuation route. This request does not
select or implement the proposed purchase recovery; existing safeguards remain.
Verify exact427 native startup and this load's debug outcome after the build.

426 NATIVE DEBUG DISPATCH VERIFIED / LOST ADS STATUS STILL BLOCKS PURCHASE:
Z:\vmx.bat succeeded49.96seconds, zero errors/two existing NU1601 warnings.
All41 existing VerminionLifecycleTests passed, zero failed/skipped (3seconds).
Native startup426 at2026-09-29 18:47:16.145EDT, version0.5.0.3 unchanged.
Debug cleanup consumed18:47:24.167 and dispatched normal ResumeTask once
18:47:24.545. Native current funds299769gil/1068556MGP match the reserved
before-state. Reconciliation then stopped18:47:24.579: ADS no longer reports
this acquisition.19/24,pending match0,unpaused,receipt/acquisition retained,
gilSpent0/MgpSpent10000; no new purchase or battle was admitted.
This proves the selected Lazyparasite route works. No external input, focus,
debug/provider configuration edit or strategy change was used. Only the VMX
compiled marker and existing changelog/checkpoint changed.
The user is choosing whether to include a narrow recovery branch for the lost
purchase handoff. It must verify negative purchase evidence, keep reservation
and spending guards, and permit one fresh native guarded ADS attempt only;
unknown/inconsistent state must still stop. No implementation of this new
recovery behavior yet. Existing house-rule gate requires the actual choice.

USER CLARIFICATION / NATIVE DEBUG CONTINUATION426 BUILD INTENT:
User explicitly confirmed the enabled Verminion debug selection is the native
route for the agent to continue this run. Earlier instructions to purchase and
click Resume manually were an incorrect restriction on that authorized route;
Resume is hidden while unpaused. Reuse saved verminion_queue selection bound
to this local character; do not change debug settings or send external input.
Only Plugin.DebugAttemptMarker changes425->426; all battle strategy code,
rosters, targeting, movement, abilities and retry limits remain unchanged.
Build using Z:\vmx.bat, version0.5.0.3 unchanged. Expected426 startup should
dispatch the existing ResumeTask once after ordinary cleanup. Native Start
will observe current funds/ownership and reconcile the saved acquisition.
Before build:19/24,pending match0,unpaused,campaign requested; saved item6005
purchase reserves2400gil, old acquisition dispatched. Do not clear receipt or
resubmit from confidence. Inspect the native outcome before any dependent edit
or additional reload. ADS invariant fix34 is already positively loaded.

ADS INVARIANT CONFIRMATION FIX BUILT / 173 SHOP TESTS PASS / NATIVE RELOAD:
Debug x64 ADS0.9.4.3 built through dotnet test ADS.Tests/ADS.Tests.csproj
--configuration Debug -p:Platform=x64 --no-restore --filter
FullyQualifiedName~Shop. All173 shop tests passed, zero failed/skipped, duration
11seconds, including the four-culture regression for the exact hatchling prompt.
Only two existing ExplorerSnapshotExportService obsolete-distance warnings.
Native local-client log positively confirms guarded-vendor-20260929-34 startup
2026-09-29 18:36:09.622EDT from Z:\ADS\ADS\bin\x64\Debug\ADS.dll, version
unchanged. No agent reload command, external input or focus change was sent.
VMX425 DLL remains17:05:06 and its source strategies were not touched.
Fresh pre-build and post-reload saved state:19/24,12matches/8wins,pending match0,unpaused;
item6005 receipt still reserves2400gil, gilSpent0/MgpSpent10000, acquisition
pending. Reload does not restore the expired ADS confirmation token or prove a
purchase. The user must complete that saved Wayward Hatchling purchase through
normal client actions, then use /vmx v Resume; verify actual item/currency
reconciliation before any new acquisition or Stage20 admission. Do not mutate
the saved receipt, dispatch externally, rebuild VMX or change battle decisions.
Stage20-24 and the fresh local Stage1 replay remain pending. Native acceptance
of a fresh prompt with the corrected matcher is not yet verified.

ADS GIL CONFIRMATION FIX / BUILD INTENT, VMX425 FROZEN:
Source access is restored after the sandbox restriction; user resumed and
explicitly requested culture-invariant numeric matching for comma/period formats.
ADS started clean at1d15c75, version0.9.4.3. ShopConfirmationToken now compares
whole numeric tokens to invariant integer formats, supporting comma, period,
NBSP and narrow NBSP groups without stripping malformed separators. Both gil
and currency-preview paths use the same matcher; ownership, item, quantity,
expiry and single-consumption checks remain. One focused regression covers
the exact Wayward Hatchling prompt under invariant/en-US/de-DE/fr-FR cultures,
wrong prices/quantities/items, mixed grouping, expiry and reuse.
Expected ADS startup marker guarded-vendor-20260929-34. Next build/test the
existing Debug x64 projects and shop regressions; no release version change.
Only ADS development output may update. Do not rebuild VMX425 or change its
roster, openings, targeting, movement, recovery, ability policy or retry limits.
Local client PID3228 responding. The protected2400gil acquisition is unresolved;
the earlier no-Yes failure is not a verified purchase or permission to redispatch.
After offline verification, resolve receipt through normal user/provider actions
before continuing Stage20-24. No external input/focus/provider-config mutation.

425 FROZEN CAMPAIGN PASS / STAGES15-19 CLEAR, STAGE20 VENDOR BLOCKED:
Stage15 match19 recovered all eight Gentlemen17:17:21.913EDT, reached final phase
17:17:50.064, and produced explicit Victory17:18:16.343, credited once8matches/
4wins/mask32767 (15/24). It cleared the first attempt after the user's Resume;
the earlier local recovery timeout and two defeats remain part of this pass.
Special was submitted17:17:58.175; do not infer its effect from that alone.
Exit positively verified17:18:20.840. Normal Stage16 match20/duty567 queue
17:18:33.645 and battlefield17:18:42.719 are observed; saved attempts1/pending20.
Stage16 match20 produced verified Victory17:21:40.323 on its first attempt,
credited9matches/5wins/mask65535 (16/24). Return verified17:21:45.043; normal
Stage17 match21/duty568 entered17:22:04.119 and produced verified Victory
17:24:37.155 on its first attempt, credited10matches/6wins/mask131071 (17/24).
Return verified17:24:41.970; Stage18 admission selection17:24:52.154 followed.
Stage18 match22/duty569 entered17:25:01.838 and produced verified Victory
17:27:32.346 on its first attempt, credited11matches/7wins/mask262143 (18/24).
Return verified17:27:37.276. Stage19 match23/duty570 queue17:27:49.992 and
battlefield17:27:56.116 observed. Saved17:28 snapshot: unpausedStage19 attempts1,
pending23. Stage19 produced verified Victory17:35:54.853 on its first attempt,
credited12matches/8wins/mask524287 (19/24). Return verified17:35:59.458.
Stage20 requested one capped Wayward Hatchling through ADS17:36:09.605;
accepted17:36:14.553. ADS validated item6005/GilShop262574/row2/price2400, but
rejected the owned prompt 'Purchase 1 wayward hatchling for 2,400 gil?'17:36:20.636.
No Yes was dispatched; ADS terminal failure ui-mismatch reports acquired0/1.
VMX correctly stopped17:36:20.735 and preserved its unresolved2400gil receipt
and dispatched acquisition. Saved state: unpaused19/24,pending match0, gilSpent0,
MgpSpent10000. Native shop/dialog may remain; do not externally operate it or
repeat the purchase. PID3228 responding. VMX source/build425 unchanged.
ADS source is clean and its development DLL atZ:\ADS\ADS\bin\x64\Debug\ADS.dll
is enabled on this same local client. Source diagnosis: ContainsExactDisplayNumber
uses an ungrouped long.ToString, so it cannot match the game's grouped price.
Under the existing hard rule to fix all vendor operations in ADS, prepare a
small ADS numeric-format correction and one focused regression, using existing
shop token/quantity/cost safeguards. This is outside battle decisions; no roster,
tactics, movement, special, VMX strategy or retry-limit edit is authorized.
No live config mutation, resubmission, vendor input or new acquisition machinery.
No external input, focus change, strategy edit, rebuild or attempt-limit bypass.
Fresh Stage1 replay and20-24 remain pending. After the concrete ADS fix and
verification, resolve the existing protected receipt through normal user/provider
actions before another campaign admission; do not clear it from confidence alone.

425 USER RESUME / STAGE15 RETRY1 ACTIVE:
Fresh native Resume17:14:21.569EDT left the existing setup menu through inventory
inspection. Queue ownership17:14:36.444 is match19/duty566; battlefield observed
17:14:43.938 and opening17:14:48.944. Saved state is unpaused mask16383 (14/24),
Stage15 attempts1/pending19/566. Local PID3228 responding. Source marker remains
425 and no reload followed its verified startup. This is a user-started retry,
not an agent dispatch or attempt-limit bypass. No build, strategy or live config
edit. The previous full-suite verification remains910/910 passing.
Leave this attempt uninterrupted. Next bounded evidence milestone around
17:18EDT checks recovery/final phase and result; no log tail or rapid polling.
Count only explicit result evidence and let normal retries/transitions proceed.
No strategy change without explicit user permission. Goal is active, not blocked.

425 EXISTING REGRESSION SUITE / NATIVE RETRY STILL PENDING:
Automatic goal continuation revalidated the current checkpoint/source and local
client. PID3228 is responding; saved state remains paused14/24,Stage15 attempts3,
matches7/wins3/pending0. Native425 paused-guard record shows no new admission.
Ran the existing full test assembly once with Debug --no-build --no-restore:
910/910 passed, zero failed/skipped, reported duration6seconds. This includes
the41 Verminion cases for accounting, reset, isolation, purchases, unknown results
and pause/reload behavior. No code, strategy, build, client setting or game input
changed. This advances offline regression verification, not battlefield evidence.
Next runtime action is the user's ordinary /vmx v Resume on425. Leave the goal
active while this first post-freeze continuation awaits that action; no polling
loop, rearmed debug hook or attempt-limit bypass. Stage15-24 and fresh Stage1
replay remain unverified locally; first-time unlock and live tournaments pending.

HARD USER RULE / STRATEGIES FROZEN FOR PRODUCTION TESTING:
No further strategy change without asking and receiving explicit user permission.
This includes roster/composition, openings, targeting, movement decisions,
recovery tactics and ability policy, including changes described as control fixes
when they alter battle decisions. Current425 is the unchanged testing baseline;
do not revert, tune, rebuild or change the retry limit on this instruction.
The current objective is to measure whether a few attempts suffice for clears
and assess production readiness. Observe results and attempts, preserve positive
credit and stop safeguards, and report failures before proposing any strategy fix.
Native saved state remains paused14/24,Stage15 attempts3,pending0. Testing awaits
the user's ordinary Resume; external client input/config mutation remains revoked.

USER STEERING / RETRY WITHOUT FURTHER STRATEGY EDITS:
User explicitly resumed the development goal and challenged strategy changes
after losses. Keep425 unchanged for the next native retries; a failed attempt
does not itself justify a source edit. Existing /vmx v Resume resets the paused
stage's attempt budget and allows another bounded three-attempt run, preserving
earned clears and result accounting. Do not raise the limit or mutate live state.
Historical330 Gentleman Stage15 lost its first attempt with Odin389/17000HP,
then cleared15:12:57.322 on its second attempt without deploying the prepared333
change. Earlier132's Stage15 clear used24 Succubus, a different composition.
Current local failures were one recovery timeout and two confirmed defeats on
422/424, with nearby/stranded attackers suggesting an execution issue. This is
not proof that the guide roster or strategy needs replacement; different client
and later control code are established differences, their causal role is not.
Fresh saved state remains paused14/24,Stage15 attempts3,pending0. External input
remains revoked. User-controlled native Resume is needed; /goal resume does not
invoke the game action. No further build, roster or special change this turn.

425 BUILT / LOADED / PAUSED GUARD VERIFIED:
vmx.bat succeeded42.86seconds, zero errors/two existing NU1601 warnings.
41/41 VerminionLifecycleTests passed; git diff --check passed. Exact425 native
startup2026-09-29 17:05:12.019EDT, version0.5.0.3 unchanged, expected development
output Z:\VERMAXION\VERMAXION\bin\x64\Debug\VERMAXION.dll. At17:05:18.279
the selected verminion_queue hook consumed its load and observed paused state:
duty0/resultUnknown/pending0/matches7/wins3, no admission or credit. Saved
mask16383 (14/24), Stage15 attempts3, pending match/duty0 and pause=true agree.
The source now reads majority melee arrival, follows smaller Odin movements,
does not abandon group movement merely because its anchor is near, and avoids
an extra selection at the final-phase transition. Guide-directed special use
is unchanged. The regression covers one near anchor with seven stranded units,
majority arrival, boundary and empty army. This does not prove a native clear.
/vmx v now has Check planned minions, deriving all five unique requirements
from stages1-24 and reading ownership for the active character. Missing entries
show short manual sources; the button starts no quest, purchase or match and
adds no saved setting. Its visual check remains pending. User wants this roster
preflight available for the next character test, ideally without further code
changes after this character finishes Stage24. Acceptance is not complete.
Next: normal user-controlled /vmx v -> Resume after reviewing the concrete fix,
then bounded stage/result snapshots. Do not clear pause, reset attempts or send
game input externally. No further build during a native attempt. Local Stage1
fresh replay and stages15-24 remain pending; first-time unlock/tournaments too.

424 STAGE15 LIMIT STOP / 425 FIX AND ROSTER CHECK INTENT:
Stage15 attempt2 reached a fully healed army at16:46:44.089EDT, but ended in
verified Defeat16:48:07.876. Attempt3 also ended in Defeat16:53:01.950; return
was verified and the three-attempt limit stopped the campaign16:53:19.599.
Saved character state: paused, mask16383 (14/24),7weekly matches/3wins,
Stage15 attempts3, pending match/duty0. No fourth admission or false clear.
The isolated recovery branch itself remains unverified despite the healed army.
Fresh final-phase snapshots show Gentlemen several metres behind Odin; the
five-metre hold condition can suppress pursuit before the army reaches melee.
Review the linked guides, correct arrival/pursuit without bypassing the saved
pause or attempt limit, and add a focused regression. Retain guide-directed
special use unless new evidence supports a change; button submission alone
does not establish its effect. User requested a /vmx v button checking the
whole planned roster, with short manual acquisition sources for missing minions.
Use the current stage requirements, no new saved settings or acquisition route.
Build425 through vmx.bat, version0.5.0.3 unchanged. Native debug may reload,
but must respect the character's pause. Further battle testing needs normal
user-controlled Resume. No external client input, focus or live config edits.
Local Stage1 replay and stages15-24 remain pending; earlier R7 clears do not
prove this pass. Continue toward Stage24 once the user resumes after this fix.

424 BUILT / LOADED / FRESH STAGE15 ATTEMPT2:
vmx.bat succeeded37.67seconds, zero errors/two existing NU1601 warnings.40/40
Verminion tests passed, including the new isolated-recovery regression. Exact424
startup16:43:44.858EDT, version0.5.0.3. Existing debug hook dispatched once at
16:43:52.407 from outside the abandoned battle; no stale result was credited.
Fresh Stage15 CPU queue is match17/duty566, battlefield observed16:44:13.010.
Saved16:44:53 snapshot: unpaused campaign, mask16383 (14/24),5matches/3wins,
Stage15 attempt2/pending17/566. No external client input or live config mutation.
Do not build/reload during this attempt. Verify individual recovery, final phase,
positive result and Stage16 transition from the next bounded milestone. Native
Stage15 recovery fix and stages15-24 clears remain pending on this local pass.

424 ODIN RECOVERY FIX / BUILD INTENT:
423 Stage13 Victory16:28:18.490 and Stage14 Victory16:31:30.119, both first
attempts. Local mask16383 (14/24),5matches/3wins. Stage15 admission match16/duty566
was observed16:32:10.613, but the recovery phase failed16:35:46.319 after90seconds.
The admission was abandoned; no Stage15 result, match, win or clear credited.
Saved state is unpaused campaign, Stage15 attempt1, pending0. Native battle may
still be active or on its result screen; it is no longer owned for control/credit.
At16:35:13.403 seven Gentlemen are400/400HP near GateB (z21-23); one is188/400HP
at(5.9,-7), and repeated group returns choose the seven already healed units.
Add a Stage15 decision to select the separated attacker individually once the
main party has returned. Reuse the existing verified single-unit command and
five-second order cadence after20seconds of retreat; keep the90-second bound.
Add one focused regression test. Build424 through vmx.bat, version unchanged.
Expect40 Verminion tests. User-selected debug reload may observe/close the
abandoned battle without credit, then stop; do not invent another admission for
it. A fresh next attempt is allowed only after native exit is verified. No client
input, focus changes or config edits by the agent; retain the user's debug choice.

423 STAGE12 VICTORY / STAGE13 ACTIVE:
Fresh local Stage12 Victory credited at16:25:17.562EDT on the first423 attempt,
third total attempt after422's two defeats. Native key framing and pursuit ran
with foreground=False. Result closure positively verified16:25:23.860; automatic
Stage13 admission match14/duty564 and battlefield observed16:25:44.235.
Saved state16:28:15: unpaused campaign, mask4095 (12/24),3matches/1win,
Stage13 attempt1 pending14/564. No external input, focus/config changes or new
purchases/quest work. The framing fix has an observed clear; the60-second exit
bound handled this six-second exit, but a fresh >30-second exit remains pending.
No build/reload during Stage13. Next bounded milestone checks stages13-14 and
the Gentleman Stage15 transition. Stage1 replay and13-24 clears remain pending.

423 BUILT / LOADED / ONE NATIVE DEBUG DISPATCH:
vmx.bat succeeded in51.97seconds, zero errors/two existing NU1601 warnings.
All39 Verminion tests passed; git diff --check passed. Exact423 startup is
2026-09-29 16:21:04.303EDT, version0.5.0.3. Existing debug hook consumed cleanup
at16:21:17.079 and dispatched once16:21:17.467, followed by inventory/progress
inspection and Stage12 admission selection16:21:27.826. No agent-sent client
input, focus change or live config mutation. Source and DLL use423.
Saved pre-queue snapshot is unpaused campaign,2matches/0wins, mask2047,
attempts2/pending0. Next evidence must confirm the new admission and its result.
Do not rebuild during the attempt. Neither the framing fix nor60-second exit
bound has a fresh native success yet. Three failed stage attempts still stop.
16:24:54EDT regression milestone: new admission match13/duty563, Stage12 attempt3,
counts2/0 and mask2047. Camera MOVE_LEFT framing followed by ground pursuit is
observed with foreground=False and the same nonzero Gentleman selection readback.
Boss HP1496/12000 at16:24:52.248. This proves background key framing executes;
the clear, final pursuit arrival and result-exit transition remain unverified.

423 FRAMING AND RESULT EXIT FIX / BUILD INTENT:
422's Stage12 attempts1 and2 ended in positively observed Defeat at16:07:54.996
and16:13:24.465EDT. Saved counts2matches/0wins, mask2047, attempts2, pending0.
The second result exit timed out16:13:54.592, but FrenRider positively observed
the return to territory388 at16:14:10.506 (about45seconds after Quit). The service
is terminal Failed, not an active battle. Saved campaign remains unpaused.
First defeat's final state has eight healthy Gentlemen stranded away from Demon
Brick at2900/12000HP. A16:07:05 camera click reports no native minimap collision;
subsequent pursuit uses the cached selected group while damage remains stopped.
Skip minimap framing after unit selection; use existing camera keys for pending
same-type selection and movement. Allow60seconds for result exit, prioritizing
positive return evidence before timeout. No retry loop or configuration added.
Build423 via vmx.bat; release version stays0.5.0.3. Run existing39 Verminion tests.
Root debug selection is user-saved verminion_queue bound to this character; its
existing native hook may Resume after reload. Do not send external client input
or alter the saved pause/debug settings. Expect Stage12 attempt3 if ready in
Minion Square; a third failure must retain the normal campaign stop. Verify the
exact423 startup and one native debug dispatch before evaluating the attempt.

USER STARTED NORMAL CAMPAIGN / DEBUG SELECTION SAVED:
After the Codex update, user selected Verminion in /vmx debug and sent the normal
run command. Root VERMAXION.json confirms DebugTaskId=verminion_queue bound to
the current character; the selection is pending the next plugin reload. The
user's run already started on422 at16:02:32.602EDT: inventory inspection, normal
Gold Saucer/Minion Square travel and progress reads, then Stage12 selection at
16:03:21.907. Saved campaign is unpaused, mask2047,0matches/0wins and no pending
match/duty/purchase/acquisition in this pre-admission snapshot. Local PID3228
is responding. External client control remains revoked. Do not rebuild during
this active run or redispatch it. Next bounded evidence should determine Stage12
admission/outcome and the automatic next-stage transition; no success assumed.
16:06:46EDT read-only milestone: Stage12 attempt1 is active, saved match11/duty563.
Eight Wind-up Gentlemen are deployed (240/240 capacity); Demon Brick was3888/12000
HP at16:06:38.457. Pursuit and isolated-unit rejoin orders are observed. This
proves the required registered roster is available in battle; no minion purchase
or quest rerun occurred. Counters remain0/0, mask2047. Outcome still pending.

422 NORMAL RESUME FIX / BUILT, LOADED AND TESTED:
vmx.bat completed with zero errors and two existing NU1601 warnings; release
version remains0.5.0.3. Exact native422 startup is2026-09-29 15:53:52.053EDT,
marker verminion-control-20260929-422. Output DLL remains
Z:\VERMAXION\VERMAXION\bin\x64\Debug\VERMAXION.dll. All39 existing Verminion
tests passed using Debug --no-build --no-restore; git diff --check passed.
Read-only saved state confirms paused campaign, mask2047 (11/24), Stage12 next,
week2026-09-29T09:00Z,0matches/0wins and no pending match/duty. No run dispatched.
Run/Resume from an existing Verminion menu now enters inventory registration,
closing only known Return/Cancel menus first. Unexpected admission during
registration fails without item use. Queue/battle reconciliation is preserved.
Git remains unstaged against baseline79ca290: VerminionService.cs, Plugin.cs,
CHANGELOG.md and this checkpoint. No external client input, foreground change,
provider settings or runtime dispatch occurred after control was revoked.
Next action is user-controlled /vmx v -> Resume. Verify inventory registration,
Gentleman ownership and Stage12 admission from a subsequent bounded read-only
evidence snapshot. Native verification of this fix, stages12-24 and the fresh
Stage1 replay remain pending; do not claim overall acceptance or auto-resume.

CLIENT CONTROL REVOKED / GENTLEMAN AVAILABLE (USER STEERING):
User explicitly says stop taking control of the client; normal users must run
Verminion through its own UI. Do not send input, change focus, open/click windows,
alter provider settings, or trigger live actions from external helpers. This
supersedes the earlier client-control authorization and turn-in intent below.
User reports Her Last Vow complete and Wind-up Gentleman available. Future ADS
rules remain user-owned. No quest/duty rerun or additional provider setup needed.
Source inspection confirms /vmx v -> Resume uses the saved campaign intent,
registers unregistered inventory minions, travels, checks/prepares the roster,
and selects the first unfinished challenge. Last verified clear mask2047 means
Stage12 next. User must initiate the ordinary action; use authorized read-only
evidence afterward. Gentleman ownership/registration and Stage12 admission have
not been freshly verified by the agent. Only this checkpoint changed this turn.

USER COMPLETED DUTY85 / QUEST TURN-IN RESUME INTENT (2026-09-29):
User completed Big Keep and will author future ADS duty rules. The prior route
choice is resolved; do not create ADS rules or run this trial again. Fresh native
chat shows duty completion7:24, Enkidu obtained/registered, and Her Last Vow
objective fulfilled. Questionable is idle; the reward turn-in remains. Current
local client is still PID3228, now1366x728, SAM80. Saved VMX mask2047/0matches/
0wins, paused, all pending operations absent. Git is clean at79ca290 with421.
Temporarily enable Q Stop and remove blacklist502 through native settings, then
use the permanent VMX Gentleman acquisition action once for the final turn-in.
Restore those two settings afterward. No duty85 override or FR/ADS changes needed.

LOCAL TRIAL STOPPED / PROVIDER SETTINGS RESTORED (421, 06:46 EDT):
Final421 verification: 39/39 existing Verminion tests passed; diff check clean.
Duty85 first fight defeated Enkidu at06:36. ADS then exhausted useful live
objects and held Heading Scout1, unable to progress between arenas. At
06:41:53.958 Questionable's native five-minute no-movement check stopped its
duty wait; DAD requested /ads leave, and ADS verified exit06:42:04.091.
VMX reported incomplete acquisition06:41:55.689 and cleaned its reservation.
No trial completion, minion reward, Verminion match, win or clear was credited.
This run included one native target diagnostic, so it is not an unattended
trial pass. No ADS, DAD, FrenRider or BotologyUpdates source/rule changes made.

Restoration verified from saved provider config: WigglyQuest blacklist502 is
back (12 entries), Stop.Enabled=false, empty stop/priorities, duty85 override
absent. FrenRider disabled, eight-man ADS=false, maturity3. DAD remains installed
with network off. Character is safely outside duty, Her Last Vow at trial step;
current job WHM72 after the provider's earlier switch. VMX remains paused.
Saved new week0/0, mask2047 (11/24), spending0gil/10,000MGP/0certificates, and
no pending match/duty/purchase/quest handoff. Stage1 fresh replay and12-24 pending.

Remaining acquisition blocker: ADS needs the transition route between Big Keep
arenas. Its existing BossFight/object/waypoint rule format is available, but no
territory396 rule exists in the inspected BotologyUpdates shards. Before adding
persistent encounter rules, resolve the current AGENTS.md include/lean/skip gate:
include a small ADS data-only route using existing architecture; lean is native
manual transition during the trial; skip leaves Gentleman and Stage12 blocked.
Do not repeat this acquisition unchanged or install AutoDuty. No new action is
armed on reload. After three consecutive goal turns with the same unanswered
route decision, the goal was marked blocked. Campaign acceptance is incomplete.

LOCAL DUTY85 UNSYNCED / TARGET DIAGNOSTIC INTENT:
DAD joined Battle in the Big Keep unsynced=true at06:29:10.720 EDT; FrenRider
ADS handoff accepted06:29:24.676. Trial combat active on WHM72. At06:31 Gilgamesh
was72.6%, then healed back to88.6% by06:32 with Enkidu alive. No clear or reward.
Test one native /target Enkidu command to determine whether existing ADS/rotation
keeps the required add target. This is manual diagnostic input; any later clear
cannot be described as a fully unattended trial attempt. No vendor action involved.

421 LOADED / ACTIVE ACQUISITION RELOAD VERIFIED:
vmx.bat passed zero errors/two existing warnings. Exact421 startup06:26:35.863
EDT; the bounded06:24-06:26 native window has only the original06:24:19.582
submission. Saved handoff remains minion21/quest502, attemptedtrue, not cancelled
or ownership-released. Native quest continues while VMX observes after reload.
The new preparation guard passed the existing39 Verminion tests in build420;
421 only clears its instance reference in Reset. git diff --check passes.

421 RESET / RELOAD VERIFICATION INTENT:
Clear the instance-only prepared acquisition reference on existing Reset, including
character changes and FULL STOP. Current quest502 is already submitted/owned, so
this reset cannot authorize another submission. Build421 via vmx.bat during its
provider-owned travel, then verify that reload observes the same handoff without
another StartQuest. Release version unchanged; do not interrupt Questionable.

420 NATIVE HANDOFF VERIFIED / QUEST502 RUNNING:
Blacklist preflight rejected at 06:23:32.463 EDT with no dispatch. After the
single UI blacklist removal, preparation began06:24:16.441 and exact quest502
started06:24:19.582 acceptedTrue. Native UI shows Her Last Vow and VMX-owned
0/1 acquisition; saved DispatchAttempted=true/CancellationRequested=false/
OwnershipReleased=false. VMX stays paused, campaign mask2047/counters0/0.
Questionable owns travel and duty85; observe that transition without redispatch.
Restore blacklist502, Stop.Enabled=false, duty85 override absent, and FR eight-man
ADS false/maturity3 after acquisition. Current job WHM72 (provider changed it
in the earlier rejected-route investigation); trial effectiveness remains unknown.

420 BLACKLIST REJECTION VERIFIED / ACQUISITION RETRY INTENT:
Native VMX reports Her Last Vow blacklisted and blocks before submission. Removed
only quest502 through Questionable's Blacklist UI (12 entries ->11; saved502false).
Other entries are preserved. Restore502 afterward. No owned handoff remains.
Retry the ordinary acquisition once; expected current quest502 before StartQuest.

420 BUILT / LOADED / BLACKLIST TEST INTENT:
Build passed zero errors/two existing NU1601 warnings; 39/39 Verminion tests
passed. Exact420 native startup is 06:22:47.394 EDT. No pending acquisition;
Verminion remains paused after reload. Click Acquire once with quest502 still
blacklisted to verify preflight rejection before any provider mutation/dispatch.

420 BUILD / RELOAD INTENT:
419 built successfully (zero errors/two existing warnings); no acquisition was
invoked. Review caught that an unsubmitted saved preparation must not dispatch
after reload. Bind deferred submission to the exact preparation object held only
by this service instance; reloaded preparations clean up and require a new action.
Build420/version unchanged and verify blacklist rejection before removing quest502.

419 BUILD / RELOAD INTENT:
Current WigglyQuest config blacklists quest502. Its installed 7.5.27 IPC starts
immediately after SetNextQuest, while accepted-quest priorities resolve on the
provider update. Add native blacklist preflight and use the existing saved
DispatchAttempted flag to wait for matching current-quest/priority/stop readback
before one dispatch. A mismatched selection cleans up without starting anything.
No provider source, new runner, configuration field, or release version change.
Build via vmx.bat, run existing Verminion tests, verify419 startup and the native
blacklist rejection first. Then remove only quest502 from the provider blacklist
through its UI for this approved acquisition; restore its original blacklist
entry and the other recorded provider settings after the test.

418 ACQUISITION ACCEPTED BUT PROVIDER SELECTED ANOTHER QUEST:
Native handoff accepted quest502 at 06:16:41.139 EDT, but WigglyQuest's
IPCQuestSelection chose already-accepted quest1083 and switched SCH70 to WHM72.
VMX correctly reported outside-owned-route work and did not claim the quest.
Stopped the unintended test run via /wqst stop; saved acquisition is now null,
VMX remains paused, priorities/stop conditions are empty and counters unchanged.
No reward or trial claimed. Investigate native accepted-quest selection before
another dispatch. WQ stop/duty85 and FR eight-player settings still need the
recorded restoration after the acquisition attempt is resolved.

418 QUEST DISPATCH INTENT (06:16 EDT):
Clear all challenges stopped before Stage 12 admission for missing Gentleman.
Saved week is now 2026-09-29T09:00Z, 0 matches/0 wins, mask 2047 retained.
WigglyQuest.json (not the stale Questionable.json) confirms Stop.Enabled=true,
empty conditions/priorities, duty85 override UnsyncSolo, and the DAD bridge's
duty gate enabled. Native Verminion menus were closed. The ordinary acquisition
button is enabled with no DAD blocker. Click it once now for remaining quest502;
expect provider-owned travel and duty85, VMX paused, no automatic campaign restart.

418 VERIFIED / LOCAL CAMPAIGN AND QUEST HANDOFF INTENT:
vmx.bat succeeded with zero errors and two existing NU1601 warnings; 39/39
Verminion tests passed. Exact native 418 startup: 06:09:36.377 EDT. DAD's
local bridge is installed; its network coordinator remains off. FrenRider's
eight-player ADS handoff is now enabled with maturity 0 (original false/3);
restore those two settings after acquisition. FrenRider itself remains off.
At 06:14 the saved weekly counters still show the old week and 10/10, while
the UI shows 0/0. Permanent mask 2047 and spending 0 gil/10,000 MGP/0
certificates are retained; all pending operations are null/zero. Verify weekly
save on the next run transition. Next: invoke Clear all challenges once to
restore campaign intent and its Stage 12 roster gate, then verify the native
WigglyQuest stop/unsynced duty 85 controls before the ordinary acquisition.

418 BUILD / LOCAL RELOAD INTENT:
DAD0.7.1.1 installed through native Dalamud UI. Native duty IPC registered
06:02:25.261EDT and bridge patched WigglyQuest7.5.27 at06:02:29.552. No quest
dispatched. WQStop.Enabled is nowtrue(originalfalse), duty85 override1/UnsyncSolo
(originalabsent); whitelist remains empty/defaultmode0. Restore these after the
acquisition. FR settings still untouched/disabled. Network DAD stays off.
Source inspection found417's readiness check incorrectly used dad.IsReady, which
requires network transport. Replace that check with the existing dad.Duty.IsStopped
IPC for both UI and dispatch; unavailable/busy still block. No DAD code change or
network coordinator setup. Marker418/versionunchanged. Build via vmx.bat; verify
exact startup and ordinary acquisition availability before any quest dispatch.
Native UI also shows the new weekly reset0matches/0wins with11permanentclears;
verify saved reset/accounting as part of the next setup transition.

LOCAL DAD APPROVED / ACCESS RESTORED (2026-09-29):
User chose Include for adding our released DAD to the local C: client. The
intervening access blocker is resolved: Z: checkpoint is readable and local
PID3228 has a window again. This approval supersedes the pending dependency
choice below; do not ask again. Use the ordinary Dalamud plugin installer for
DAD, verify readiness and the Questionable bridge, then configure the existing
provider controls for Her Last Vow/unsynced duty85. Preserve all saved facts.
No R3/R7 actions, AutoDuty, game restart, new runner or release-version change.
Record each actual dispatch/receipt before continuing campaign Stage12.

LOCAL PASS IDLE AT DEPENDENCY DECISION (417):
Stage2 Victory credited20:15:38.190 EDT; native UI subsequently shows Complete:
Weekly victory target reached,10/10wins/0remaining. No further match admitted.
Saved facts:10matches/10wins,mask2047(stages1-11),pendingmatch/duty0,purchase and
both acquisition recordsnull. Stages2-11 have fresh first-attempt wins; Stage1
was already cleared and still needs a fresh replay. Stages12-24 remain pending.
Weekly settings restored through UI to Participation/target5; caps and spending
unchanged(0gil/10,000MGP/0certificates). CampaignRequested is nowfalse after the
weekly test; use Clear all challenges for the next campaign run. No selected
debug reload action. WigglyQuestStop.Enabledfalse/conditions/prioritiesempty;
FrenRider settings untouched and disabled. DAD absence blocks the existing
Gentleman acquisition before any quest dispatch. Pending choice: add/enable our
released DAD on this local client or provide Gentleman through existing/manual
questing; do not bypass the handoff or install a provider before that choice.
417build passed0errors/2NU1601warnings;39/39Verminiontests passed; exact startup
20:10:34.337 and missing-DAD native UI verified. No build/test process remains.

417 STAGE 2 EXACT-TARGET TEST DISPATCHED:
All39 existing Verminion tests passed. Native saved modeWinX/target10 verified
before Run weekly goal; nine prior wins retained. Service reported9/10 and one
remaining, then saved match10/duty553 and entered Stage2 at20:13:09.629 EDT.
Do not count a result until native evidence. Restore target5/modeParticipation
after the exact stop; retain all earned clear/match/purchase facts.

417 BUILT / LOADED / PREREQUISITE UI VERIFIED:
vmx.bat passed0errors/two existingNU1601warnings; exact417 native startup verified
20:10:34.337 EDT. Native/vmxv now shows missing DAD beside a disabled Gentleman
acquisition action. WigglyQuest Stop.Enabled restoredfalse; Conditions and priorities
verified empty; no quest dispatched. Next independent check: temporary WinX target10
from nine saved wins, replay Stage2 once through Run weekly goal, verify exact stop.
Original weekly settings modeParticipation/target5; restore after the check. No
new minion purchase, provider installation or challenge-fact reset is authorized.

417 BUILD / LOCAL RELOAD INTENT; QUEST NOT DISPATCHED:
Stage11 Victory20:05:06.058; Stage12 stopped20:05:21.165 for missing Gentleman,
mask2047/nine matches/nine wins, no pending match or purchase/acquisition. Enabled
WigglyQuest's existing Stop setting through UI (originalfalse; emptyConditions).
The ordinary Acquire button then refused before quest dispatch because the local
client lacks DAD: dad.Duty.IsStopped was unregistered. No priority or stop quest
was inserted; no saved quest acquisition exists. Correct the UI/action to show
the DAD prerequisite and check existing readiness before the duty IPC. Marker417,
release version unchanged. Build via vmx.bat; verify fresh startup and disabled
action. Restore only WigglyQuest Stop.Enabled=false before awaiting a dependency
decision. Do not install DAD or bypass its ownership gate without a resolved choice.

LOCAL STAGE 11 VICTORY / GENTLEMAN ACQUISITION PREPARATION:
Native20:05UI shows11/24 clears, nine matches/nine wins, Stage12 next. Stages3-11
passed first attempt. Wait for the missing-Gentleman gate to stop the service before
quest work. Intent: enable existing WigglyQuest stop conditions (originalfalse,
emptyConditions), then use VMX's ordinary Acquire Wind-up Gentleman action. Only
Her Last Vow502 is expected unfinished; observe the provider-owned route. For
duty85 use existing unsynced entry and ADS/FrenRider, not AutoDuty or a new runner.
No DAD installation is authorized/needed speculatively. Restore any specific test
provider settings after acquisition; no provider settings have changed yet.

LOCAL STAGE 10 CLEARED / STAGE 11 ACTIVE (416):
Stage10 Victory credited20:01:53.356 EDT; eight matches/eight wins. Stage11
admitted20:02:16.519, match9/duty562. Stages3-10 all passed first attempt locally.
Next expected roster gate is Stage12/Gentleman. Keep that gate intact; then use
existing Questionable acquisition after configuring its native stop control.

LOCAL STAGE 9 CLEARED / STAGE 10 ACTIVE (416):
Stage9 Victory credited19:59:00.539 EDT; seven matches/seven wins. Native evidence
includes lever approach, carrier action and bomb detonation readback, then the
vulnerable Slime Puddle phase. Stage10 admission verified19:59:23.794, match8/duty561.
Stages3-9 all passed first attempt locally; no manual battle input or source change.

LOCAL STAGE 8 CLEARED / STAGE 9 ACTIVE (416):
Stage8 Victory credited at19:53:41.319 EDT; six matches/six wins. Stage9 admission
verified19:54:10.595, match7/duty560. Stages3-8 all passed first attempt locally.
Read-only quest preflight: installed WigglyQuest7.5.27 has empty priorities and
stop conditions disabled; no duty85 override or auto-unsync. FrenRider is disabled
with eight-player ADS handoff disabled. Her Last Vow sequence2 leads to duty85,
Battle in the Big Keep. Configure existing provider controls when the campaign
stops for Gentleman; do not install DAD speculatively or reimplement quest steps.
No provider configuration changed yet. Normal background battle remains active.

LOCAL STAGE 7 CLEARED / STAGE 8 ACTIVE (416):
Native UI at19:51shows7/24 cleared, five weekly matches/five wins and Stage8
admitted on attempt1. Stage7 passed its first attempt with Zu Hatchlings; no
manual battle intervention. Purchase total remains10,000MGP and zero gil/certificates.

LOCAL STAGE 6 CLEARED (416):
Saved progress now mask63, four matches/four wins; Stage6 took one attempt.
No pending purchase/acquisition; spending remains0gil/10,000MGP/0certificates.
The campaign continues automatically to Stage7. Fresh local verified clears are
3-6; stages1-2 were already earned and still require separate replay evidence.

416 HANDOFF VERIFIED / LOCAL STAGE 6 ADMISSION:
Native sequence: challenge Return19:39:50.414, parent Cancel19:39:51.387, ADS
acceptance19:39:52.453. ADS completed exactly one Zu Hatchling at19:39:58.817;
VMX verified10,000MGP/zero gil/zero certificates once at19:39:59.615 and minion
registration at19:40:07.656. Stage6 selected19:40:18.827; admission pending in
that snapshot. This verifies416's corrected menu handoff. No duplicate request.
Allow the existing campaign to run; normal battlefield control needs no focus.

416 BUILT / LOADED; HANDOFF REPRODUCTION INTENT:
vmx.bat succeeded with zero errors and two existing NU1601 warnings; all 908
existing tests passed. Exact416 native startup is verified at 19:35:05.390 EDT.
Fresh local PID3228 capture and bounded logs reconcile the prior failed acquisition;
no new ADS request or battle dispatched. Reopen the targeted Verminion table's
challenge list, then invoke Clear all challenges once to exercise the fixed
Return/Cancel transition before ADS dispatch. Expected purchase: one Zu Hatchling,
10,000 MGP, through ADS only. Continue stages6-24 after the verified receipt.
Focus/control is authorized by the user's latest explicit resume.

416 BUILD / LOCAL RELOAD INTENT:
ADS accepted a Zu request at 19:30:41.585, then stopped before interaction/spend
because the Verminion parent menu appeared during navigation. VMX only cancelled
that parent at 19:30:42.693. Failure reconciled with no purchase reservation or
acquisition left. Saved counts remain three matches/three wins, clear mask31.
Fix: leave the challenge list in the existing acquisition state's throttled menu
branch, then require the native event/selection/dialogue to close before any new
ADS dispatch. Do not close foreign UI or add vendor controls to VMX. Marker416;
release version unchanged. Build with vmx.bat and run existing tests. Local dev
auto-reload is allowed; no debug task selected, so no automatic new run expected.
After exact416startup, resume once from the challenge menu to verify the fix.

CONTROL RETURNED / LOCAL ADS AND CAPS SETUP INTENT (2026-09-28):
User explicitly returned control and requested resume. The previous focus blocker
is resolved. Fresh read-only configuration still has stages 1-5, three matches and
three wins, no pending match/purchase/acquisition, and zero MGP cap. Continue the
local C: pass: load existing guarded ADS build and set the recorded test caps using
UI, then resume Stage 6. Do not operate R7/R3 or reset any earned state.

ADS LOCAL LOAD VERIFIED / STAGE 6 RESUME INTENT:
Fresh ADS startup at 19:29:28.061 EDT confirms Z:\ADS\ADS\bin\x64\Debug\ADS.dll,
guarded-vendor-20260928-33; installed entry disabled and development entry enabled.
VMX caps were verified in saved local configuration: 223,588 gil / 40,000 MGP /
four certificates / 50,000 gil reserve. No purchase submitted yet. Next invoke
Clear all challenges once; expected ADS Zu acquisition for 10,000 MGP before
Stage 6 admission. Do not duplicate any saved purchase request.

Local C: client PID 3228 remains the sole target. Installed VMX 0.5.0.2 was disabled;
Z:\VERMAXION\VERMAXION\bin\x64\Debug\VERMAXION.dll was added and loaded through
Dalamud UI. Exact 415 startup verified at 19:01:43.235 EDT. /vmx v renders status,
progress and run/stop controls above scrolling details. No debug reload task selected.
No source or build change this turn. Do not operate R7/R3.

Clear all challenges dispatched once at 19:02:37 EDT. Prerequisite quests 434, 435
and 1431 are complete. Native challenge menu proved stages 1-2 already cleared;
those facts were preserved. Stage 3 victory: 19:06:09.669; Stage 4 also verified
and credited; Stage 5 victory: 19:12:01.934. All three passed their first attempts.
Saved local clear mask 31 (stages 1-5), three matches and three wins. Stage 6 was
not admitted: at 19:12:17.042 it stopped for missing Zu Hatchling and the zero MGP
cap. No pending battle or purchase. Battle control works despite the black 3D scene.

Next UI work after returned focus: load the existing guarded-purchase ADS x64 build
through its development-plugin UI, preserving one loaded ADS instance; set the
carried-forward test caps (223,588 gil, 40,000 MGP, four certificates, 50,000 gil
reserve) through VMX settings. Clear all challenges then continues Stage 6; ADS
acquires Zu for 10,000 MGP. Local caps are now verified saved: 223,588 gil / 40,000 MGP / four certificates /
50,000 gil reserve. The installed ADS entry is disabled; the x64 development path
was added through Dalamud UI and enabling has been requested. Every vendor
action remains in ADS. Its source is clean; the prepared build is
Z:\ADS\ADS\bin\x64\Debug\ADS.dll (3,281,920 bytes).

Local roster has Mammet and Airship; Zu, Wayward Hatchling and Wind-up Gentleman
are missing. Her Last Vow (502) is already accepted at sequence 2; later acquisition
must use the existing Questionable handoff. No speculative DAD installation.
Fresh stage 1-2 replays are still needed without erasing earned clears; the normal
campaign intentionally skips them. No replay feature has been implemented.
The goal remains incomplete. Wait for the user's focus-return signal for UI work.

LOCAL ADS DEPENDENCY PREFLIGHT (no focus/control):
Installed enabled ADS0.9.4.2 DLL exposes ordinary GetCapabilitiesJson and
GetShopPurchaseStatusJson strings, but neither aligned UTF16 nor UTF8 metadata/
string reads contain StartGuardedShopPurchase,CancelShopPurchase or the guarded/
certificate capability keys. This supports an older API, but a loaded capability
response remains authoritative. Do not attempt purchases before that check.
Current Z:\ADS source already registers the guarded endpoint and advertises
guardedShopPurchases=1/achievementCertificatePurchases=1; no new vendor code needed.
Any required update should reuse that existing ADS build through the local client,
not move vendor work to VMX. No ADS/source/client file changed for this inspection.
Still no focus-return message; no foreground/input/screenshot attempt this turn.

415 BUILT; LOCAL LOADING AWAITS RETURNED FOCUS:
vmx.bat succeeded0errors/two existingNU1601warnings;diff-checkclean. This changed
only the window layout and debug marker/path; existing908passing behavioral tests
were not needlessly rerun. Human/native layout check remains pending. Local client
still runs installed0.5.0.2; no dev path or debug selection has been written.
No active build/testsession. No focus attempts since the user's instruction.
Read-only inspection confirms ADS,FrenRider,Lifestream,vnavmesh,WigglyQuest and
YesAlready installed. DAD is not in this client's installedPlugins list; do not
install anything speculatively. Minion ownership, native completed stages and
whether quest acquisition is necessary await the loaded415 observation.
Next remains disable installed VMX, add/load415fromZbin through local Dalamud UI,
inspect /vmx v, then use Clear all challenges on this character. Wait for the
user's focus-return message for those UI steps; no requirement for ongoing focus
once the already-tested background battle controls are running.

415 LOCAL UX BUILD INTENT (focus still withheld):
Source review found DrawSettings' long text ahead of all run/stop actions. Move
status and controls above a native scrolling details child, put weekly mode/caps
under an expandable heading, and separate the stage-follow button from the slider
row. Default height650instead720. No behavior/accounting/battle change; no new
config setting/file/dependency. Build415 via vmx.bat; existing908tests cover the
unchanged behavior. Native layout verification waits for returned focus/local
development-plugin loading. Expected debug path now names the intended local
Z:\VERMAXION\VERMAXION\bin\x64\Debug\VERMAXION.dll. Do NOT copy to R7.
The optional API15docs path from the routing reference was unresolved by the
path resolver; it is not needed for this native ImGui change or supported UI load.

LOCAL C CLIENT CAMPAIGN REPLAY SELECTED (2026-09-28):
User explicitly selected another character on this PC/C: for challenges1-24 and
authorized direct local client control. This supersedes R7 as current test target;
do not operate/copy to R7. Goal is active again. Current gamePID3228, executable
C:\ff14\SquareEnix\FINAL FANTASY XIV - A Realm Reborn\game\ffxiv_dx11.exe.
Loaded Dalamud modules positively resolve client root to
%USERPROFILE%\AppData\Roaming\XIVLauncher (Hooks15.0.3.5). One current character
in its active VMX account; identifiers kept out of this checkpoint.
Installed VMX0.5.0.2 is enabled, with no VMX dev load path, no Verminion progress/
purchase fields yet and no selected debug task. Tested source/build414is0.5.0.3.
Opened /xlsettings and /xlplugins via local UI after restoring the minimized game.
No DLL/config changes, purchases, movement or campaign start yet. Old VMX must
be disabled before adding/loading the tested development DLL to avoid /vmx conflict.
Latest user steering: they are busy on this PC and will give focus back later.
STOP foreground activation, SendKeys, mouse movement and foreground screenshots
until they do. Continue only background/read-only preparation meanwhile. Last UI
shows Plugin Installer over Dalamud Experimental settings; no field edits made.
Ephemeral UI helpers in functions store are not files; do not invoke while focus
is withheld. One bounded screenshot artifact is in local temp/vmx-local-ui.png.
Next: load tested VMX through existing Dalamud UI when focus returns, verify its
marker and native character/minion/challenge state, inspect /vmx v human UX and
use Clear all challenges. Preserve existing earned state and separate this new
replay evidence from R7's observed24clears. Tournament matches remain out of scope.

BLOCKED AUDIT 3 (after414,2026-09-28 21:13:33UTC):
Previous goal turn was no progress (audit2). This continuation found no further justified
independent change: current source still deliberately stops before tournament
Join and retains unknown allowance/reward state. Fresh saved R7mode0/unpaused,
pendingmatch/rewardnull,all24;414artifact unchanged. Last native tournament821
observation21:04:27.7710699Z says matchesbegin11:00October3,hiddencountersnull.
No active build/test/session exists; this is not a verified process wait. No log
watch, reload, configuration edit, new UI attempt or match performed this turn.
Open-period menus/registration/allowance and actual prize offer/receipt remain
unavailable. First-time setup needs a suitable locked character; broader native
battle/lifecycle acceptance is outside the currently selected tournament-UI pass.
No hypothetical combat/UI behavior added to substitute for missing evidence.
Revalidated clock, source414/Join-disabled and saved mode0/unpaused/pendingnull;
the last native notice and observation timestamp are unchanged. No fresh live
registration or prize evidence exists; no process is being awaited.
Classify this turn as no progress at the same runtime-evidence/scope blocker, third
consecutive occurrence since414delivery. The blocked threshold is satisfied;
mark the goal blocked, not complete. Resume requires an open registration/prize
opportunity or a newly selected feasible verification scope. No automatic match,
world change, acquisition, alternate-client action or repeated idle reload follows.
Build414/908passingtests and all earlier unverified acceptance gaps remain intact.

2026-09-28 17:12 EDT 414 DELIVERY VERIFIED:
-Exact414startup17:10:47.276;home/currentWorld93,bothDC5 at17:10:50.907;
already-met participationComplete17:10:50.937;dispatch17:10:50.938. No admission.
-Fresh mode0/unpaused/campaignfalse;sequence90/pending0;86matches/33wins/all24;
caps223588gil/40000MGP/4certificates,reserve50000;spent2400gil/10000MGP/4certificates;
pending reward/purchase/minion/questnull. No configuration edit for this delivery.
-414build passed0errors/two existingNU1601warnings;908/908tests passed;diff-check
clean. Registration title binding is delivered; its period transition is regression
covered, not live verified. No active tool/test/probe/admission remains.
-Previous turn was progress; this turn also made concrete source/test/deployment
progress, so it is not a no-progress blocked turn. Overall goal remains incomplete.
Current runtime scope stays tournament UI without matches. Open registration,
allowance, prize offer/acceptance, period transitions and full579combat/Join await
appropriate conditions. First-time setup and earlier lifecycle gaps remain open.
Do not treat another idle reload or closed-period replay as proof of those cases.

414 BUILT / DLL-ONLY COPY INTENT:
vmx.bat passed0errors/two existingNU1601warnings;908/908fulltests passed. Source
review/diff-checkclean; no old boolean registration/allowance call remains.
Copy414 under existing mode0/unpaused/sequence90/pending0/86matches/33wins/all24/
spending/empty reward-and-acquisition guards. No config change or live match.
Expected native evidence is loaded414 and ordinary already-met participation
completion only. Period rollover and open entry still require suitable runtime.

414 PERIOD-IDENTITY SOURCE / BUILD INTENT (2026-09-28):
Previous goal turn made verified progress (413reward flow,907tests,native routing).
Current source review found registration was only a boolean. After a tournament
period change it could incorrectly accept a new title's allowance as registered
or exhausted, bypassing the Recordkeeper/reward opportunity. Store the title only
in this run after a matching434or435response; require exact title equality before
entry preparation or15/15handling. On a changed fresh title, clear the old request/
confirmation and inspect the Recordkeeper again. No saved observation authorizes
registration. A434response naming another period stops for a fresh inspection.
Focused regression covers mismatched response/periods, identical counters, hidden
allowance and absent registration after reload. Existing caller tests use titles.
Build414 via vmx.bat then full tests sequentially. Guarded DLL-only mode0deployment
will verify loading/unchanged completed participation only; do not rerun closed
UI as proof of this unavailable transition. No Join/match or mode change planned.
All prior acceptance gaps remain; release version unchanged0.5.0.3.

2026-09-28 17:08 EDT 413 DELIVERY VERIFIED:
-Exact413startup17:05:11.311;home/currentWorld93,bothDC5 at17:05:14.864;
participationComplete17:05:14.905;selecteddispatch17:05:14.906. Clean idle exit,
no new admission. Source and deployed marker413; version remains0.5.0.3.
-Fresh saved mode0/unpaused/campaignfalse;sequence90/pending0;86matches/33wins/
all24mask16777215. Caps223588gil/40000MGP/4certificates,reserve50000;spent2400gil/
10000MGP/4certificates. Pending reward/last reward/purchase/minion/questnull.
Tournament821 snapshot savedUTC21:04:27.7710699Z with hidden countersnull.
-412/413builds passed0errors/two existingNU1601warnings.907/907fulltests passed
on identical product code;413 changed the attempt marker only. Final diff-check
clean. Bounded412native closed-period UI pass17:04:31.973 verifies preserved menu
routing, not registration or a prize. No active tool session/probe/admission.
-Delivered prize flow, safe persisted intent/acknowledgement/exact MGP receipt,
pending-operation exclusion and shared UI status. Model/text tests cover evidence
failures, serialization, cancellation-state retention, resets, isolation, caps
and exact-once credit. Native FULL STOP/reload at prize acceptance is NOT proved.
-Current UI pass cannot advance live entry/claims: no open tournament or claimable
prize. Next native step needs an actual registration/allowance/prize opportunity;
no further closed-period replay can prove those branches. Tournament Join remains
disabled as requested for this UI-only pass. Full579combat/Join and earlier setup/
lifecycle gaps remain incomplete. No user action/permission request is pending.
Goal active/incomplete; this turn made implementation and runtime progress, so it
does not count as a repeated no-progress blocked turn. Preserve Cactpot work.

412 NATIVE CLOSED-PERIOD PASS /413 RESTORE-COPY INTENT:
Exact412startup17:04:15.127;dispatch17:04:18.734;home/currentWorld93,bothDC5.
Fresh native821notice17:04:27.780;Upcoming/Nothing closure17:04:30.939; terminal
Complete17:04:31.973 confirms ordinary routing with the new reward code present.
No reward, registration, queue or match was exercised. Hidden counters remained
unknown.413same-product marker build passed0errors/two existingwarnings.
Guarded mode2->0 copy413 now; require exact413startup and already-met goal.
907passing tests cover the delivered reward model/text; no new live prize proof.

412 BUILT / CLOSED-PERIOD COPY INTENT:
vmx.bat passed with0errors/two existingNU1601warnings;907/907fulltests passed;
diff-checkclean. Fresh saved mode0/unpaused,sequence90/pending0,86matches/33wins/
all24;caps223588gil/40000MGP/4certificates,reserve50000;spent2400gil/10000MGP/
4certificates;pending reward/purchase/minion/questnull. Guarded mode0->2 copy412
will exercise current closed-period UI with Join disabled. This cannot prove a
reward claim; no prize currently available. Restore mode0 only after terminal
evidence, with marker413 built from the same product code. All earned facts stay.

412 REWARD-FLOW SOURCE / BUILD INTENT (2026-09-28):
-411 remains the verified loaded DLL; no active admission. Fresh bounded log
snapshot still ends with its 16:43:49.122 already-met participation completion.
-Implement owned440 rankings/observed Close,441/442 exact total,444 prize prompt,
and445 acknowledgement. Keep reward820 identity separate from current821 notice.
Save immutable per-character acceptance intent before Yes; no resubmission after
interruption. Save acknowledgement before exact currency reconciliation/receipt.
Unknown balances, failed saves and timeouts retain unresolved intent. Explicit451
rejection can clear only an unacknowledged intent with unchanged currency.
-Pending claims block competing admissions/purchases. FULL STOP retains intent;
cleanup only declines an owned exact prize prompt or closes the owned ranking.
New focused lifecycle/text tests cover both serializers, stop/reset/clone and
character isolation, missing or inconsistent evidence, caps, duplicate completion
and unchanged weekly/campaign/spending facts. These tests are not live UI proof.
-Build412 with vmx.bat, then run full tests sequentially. Planned guarded mode0->2
deployment exercises ordinary closed-period routing only, with no Join or match.
Restore mode0 with a distinct compiled marker after terminal412 evidence. Updated
deployment guards reject any pending tournament reward. No reward is currently
available; open-period UI and actual claims remain unverified. Version0.5.0.3.

2026-09-28 16:46 EDT 411 CLEAN DELIVERY VERIFIED:
-Exact411startup16:43:45.469,home/currentWorld93 and bothDC5 at16:43:49.100;
selecteddispatch16:43:49.123 and participationComplete16:43:49.122. No new admission.
-Fresh saved mode0/unpaused/campaignfalse;sequence90/pending0;86matches/33wins/
all24mask16777215;caps223588gil/40000MGP/4certificates,reserve50000;spent2400gil/
10000MGP/4certificates;purchase/minion/quest requestsnull. Snapshot821withhidden
counters remains saved atUTC20:40:56.2715332Z. All facts/spending unchanged.
-Delivered409native-duty identity and checked admission saves.904/904fulltests
passed on identical product code.409/410/411vmx.bat builds passed with0errors/two
existingwarnings;diff-checkclean. Temporary rankings probes are removed. Native
queue/Commence/save-failure replay remains pending; idle startup does not prove it.
-410 newly verified a neutral LovmRanking screen (511values/15nodes) and Close
node14/ButtonClick0. This can support the pending reward UI implementation even
though the info panel has no personal results. No reward was offered/claimed.
-Next meaningful independent work: implement owned Recordkeeper reward dialogue
and the observed rankings closure, exact prize confirmation, persisted claim
intent, and positive acknowledgement/currency reconciliation. Keep its prior
tournament identity separate from the current information panel title. Reuse
the captured439-445/451 game text; no guessed callbacks or new live match.
-Full579combat/Join, open-period UI and reward verification, and earlier setup/
lifecycle gaps remain open. User's no-tournament-match instruction is unchanged.
No active tool session/test/probe remains. Goal is active/incomplete; this turn
made source and native research progress, not a no-progress blocked turn.

410 NATIVE RANKINGS VERIFIED /411 RESTORE-COPY INTENT:
410 exactstartup16:40:43.617,dispatch16:40:47.176. Native LovmRanking opened after
the separate207agent Show.16:41:00.314 capture:511AtkValues/15nodes; observed close
buttonnode14,registeredButtonClickparam0,labelClose; headings Rank/Name/Points.
Terminal probe cleanup16:41:00.315. This disproves a broad assumption that all
rankings UI was unavailable;407 only proved no personal results in the info panel.
No registration, reward offer/acceptance, or battle was exercised. The411 clean
build passed0errors/two existingwarnings. Restore only mode2->0 and copy411 under
earned guards. Next independent source work can use this observed Close control
for the owned440 rankings->441/442 offer->444 exact confirmation->445 thank-you
reward flow, with persisted claim intent and positive receipt evidence; that flow
is NOT implemented or verified yet. Do not mistake the neutral ranking screen
for a claimable reward, or current821 info title for an older tournament's reward.

411 CLEAN BUILD INTENT:410 was copied under the reviewed mode2/earned guards.
Remove every temporary rankings-probe field/state/helper/branch now and build411
while410 performs its one-shot inspection. Final product code equals tested409.
Do not restore mode or copy411 until410's terminal evidence is reconciled.

410 BUILT / COPY INTENT:vmx.bat passed0errors/two existingwarnings;diff-checkclean.
Guarded mode0->2 and copy410; one bounded native rankings Show/read/close only.
The904passing tests cover retained409product logic; this diagnostic's native
outcome remains pending. Keep all earned counts/caps/spending/receipts unchanged.

410 TEMPORARY NATIVE RANKINGS PROBE / BUILD INTENT:
409 final exactstartup16:37:20.984 and selecteddispatch/participationComplete
16:37:24.611/.610 verified. Earlier409load was still pending before that reload;
no match or credit. Installed AgentId enum declares LovmRanking207 separately from
the information panel's results heading. Probe this existing native agent Show
after a fresh owned info read, only in Minion Square/Home World with no preexisting
LovmRanking. Wait4seconds, capture that addon once and close only the owned window.
No registration, prize confirmation, queue or battle. It may expose ranking/close
controls independently of personal rewards; an opened agent alone proves nothing.
Build410, guarded mode0->2 copy; remove probe for411 and restore mode0 only after
terminal410 evidence. No new dependencies/tools/files or permanent debug option.

409 BUILT / COPY INTENT:vmx.bat passed0errors/two existingNU1601warnings;
904/904 full tests passed;diff-checkclean. DLL-only copy to the approved R7 watched
path under mode0/unpaused/sequence90/pending0/86matches/33wins/all24/spending/empty
request guards. No configuration write or new match. Expect exact409 startup and
normal already-met participation completion. Generalized live queue/Commence and
failure-to-save behavior remain unverified natively; do not claim those from idle
startup or model tests. New579 entry and battle controls remain disabled.

409 SOURCE/BUILD INTENT (independent admission groundwork; no new match):
Prior408 turn made source/runtime progress. Replace stage-derived queue identity
with the native CPU duty ID in coordinator/interaction; retain it across reload
and owned cancellation. Installed ClientStructs enum is None/Pending/Queued/Ready/
Accepted/InContent; ignore stale popped entries while Pending/Queued, reject mixed
entries and unrelated live pops. Commence now requires a matching saved admission
and successful TrySaveAccount for a newly observed queue. Apply the same checked
save to direct observed admission. Tutorial/challenge selection retains stage IDs.
Existing non-challenge admissions receive bounded result observation, not tutorial
or boss controls. New tournament Join remains disabled; this does not implement or
claim tournament combat, new admissions or runtime queue verification. One focused
regression covers queue shapes, player-mode rejection,579reload/cancellation,
exactly-once weekly credit and no campaign/tournament-counter fabrication.
Build409 via vmx.bat, full tests, then guarded mode0 DLL-only copy and verify idle
already-met completion. Do not manufacture a queue or reset earned results.

2026-09-28 16:28 EDT 408 CLEAN DELIVERY VERIFIED:
-Exact408startup16:27:11.718;native home/currentWorld93 and bothDC5 agree at
16:27:15.274;selected dispatch and already-met participationComplete16:27:15.313.
-Fresh saved mode0/unpaused/campaignfalse,sequence90/pending0,86matches/33wins,
all24mask16777215;caps223588gil/40000MGP/4certificates,reserve50000;spending2400gil/
10000MGP/4certificates;purchase/minion/quest requestsnull. Snapshot821/October3,
hidden countersnull, observationUTC20:26:14.5440773Z survives clean reload.
-Delivered additional explicit-entries-closed and fresh15/15 allowance handling,
both leading only to remaining participation.903/903tests passed on identical
405/406productcode;408 removed407temporaryprobe and changed marker only. All
vmx.bat builds passed;diff-checkclean. No probe symbols remain. Version0.5.0.3.
-Closed-period ordinary flow has fresh405/407 evidence. New470/15-of-15 branches
have regression coverage, not native verification.407 positively observed no
result information/buttons on this character, so reward UI cannot be inspected
through this panel now. No tournament Join, claim, battle or further spend occurred.
-No active process/test/admission or temporary setting. User's no-tournament-match
instruction remains binding. Full goal still incomplete: open-period entry and
reward UI need suitable runtime conditions; full579 battle coordination, claims,
and earlier first-time setup/lifecycle acceptance remain open. This turn made
implementation and native research progress; it is not a no-progress blocked turn.

407 NATIVE RESULTS-PANEL BLOCKER /408 RESTORE-COPY INTENT:
407 exactstartup16:26:01.925,dispatch16:26:05.443.16:26:14.554 native panel13values/
21nodes:node13 is TEXT Tournament Results, node16 TEXT There are no results to
display. Only Edit Hotbar node21 was an exposed button. Exact Results-button
lookup dispatched nothing; no LovmRanking opened. Ordinary closed-period completion
16:26:18.717. This is direct evidence that this character cannot expose the result/
reward UI through this panel now; a field named TournamentResults was not proof
of a button. Existing recorded reward dialogue rows remain source evidence only.
408 build passed0errors/two existingwarnings; temporary probe entirely removed.
Restore mode2->0 and copy408 under existing earned-state guards. Require408 startup,
normal already-met participation completion and preserved progress/snapshot.

408 CLEAN BUILD INTENT:407 copied under guarded mode2. Remove all temporary
results-probe fields/state/capture/close code for clean408 while407 runs its bounded
check. Product logic is identical to tested405/406. Do not copy or restore mode
until407terminal evidence is known. If a Results button was available, use captured
controls for subsequent implementation; otherwise retain the actual blocker.

407 BUILT / PROBE COPY INTENT:vmx.bat passed0errors/two existingwarnings;
diff-checkclean. Existing903tests cover retained product logic; this temporary
native UI probe has compile/readback verification only. Guarded mode0->2 and
copy407 now. No further runtime action until its bounded terminal capture.

407 TEMPORARY RESULTS-UI PROBE / BUILD INTENT:
406 exactstartup16:23:13.138,participationComplete16:23:16.679 and selecteddispatch
16:23:16.680 verify normal restored mode0. No match or acquisition occurred.
Probe only the owned Gold Saucer Verminion information panel: bounded node/value
capture and one click on an enabled registered button with exact Tournament Results
label. If absent, continue normal closed-period check. If present, wait3seconds,
capture LovmRanking/panel once, close only the probe-owned ranking/info and stop.
No registration/claim/queue/match. This determines whether reward-UI research can
progress before an open tournament or prior participation. Build407, then guarded
mode0->2 copy. Remove all temporary probe code for408 and restore mode0 after its
terminal evidence; keep version and all earned/accounting state unchanged.

405 NATIVE CLOSED-PERIOD VERIFIED /406 RESTORE-COPY INTENT:405startup16:22:07.957,
dispatch16:22:11.492;native821notice16:22:20.553;Upcoming/Nothing16:22:23.670;
actual closure/Complete16:22:24.703. Saved mode2/unpaused,sequence90/pending0,
86matches/33wins/all24;dated snapshot refreshedUTC20:22:20.5380108Z, hidden
countersnull. No admission/result/purchase.406 marker-only build passed. Restore
only mode2->0 and copy406 with unchanged earned facts/caps/spending/requests;
require406 exact startup and normal already-met participation completion.

406 MARKER-ONLY BUILD INTENT:guarded405 copy dispatched. Build406 with identical
product code while405 owns the bounded closed-period UI check; no settings/copy
until terminal405 evidence. Expected no admission/credit/purchase; then restore
mode2->0 under earned-state guards and copy406. No tournament Join enabled.

405 BUILT / COPY INTENT:vmx.bat passed0errors/two existingNU1601 warnings;
903/903 full tests passed;diff-checkclean. Copy405 with only guarded mode0->2
change, retainingsequence90/pending0/86matches/33wins/all24,spending and empty
requests. Replay the refactored owned Upcoming -> Nothing -> actual menu closure
path, then stop at already-met participation with no match. Exact470 dialogue
and visible15/15 branches remain unavailable natively and have regression evidence
only. After terminal405, restore mode0 with clean406 marker and verify reload.

405 SOURCE/BUILD INTENT (active continuation; no tournament match):prior turn was
progress (404 persisted-display implementation and native reload verification).
Bounded12MB prior-log read recovered installed GoldSaucerTalk421-470 at15:49:34:
434/435 explicitly next427, so retain existing successful-registration menu flow;
do not add a speculative direct-close fallback.470 explicitly ends(next0) with
entriesclosed; distinguish it from prerequisite/unknown rejection and advance only
that recognized owned dialogue. A freshly registered ongoing tournament with15
visible matches likewise proceeds to remaining weekly participation, without
claiming reward completion. Reuse transient eligibility and owned-menu closure;
future runs still inspect opportunities. Extend existing focused regression.
Rows439-445 establish reward-expiry, ranking, amount, exact Accept your prize?,
and thanks dialogue, but the native ranking interaction/claim evidence is absent;
do not invent those controls or claim an implemented reward collector.
Build405/tests; then guarded mode0->2 closed-period replay, restore mode0 in406.
No tournament Join or additional purchases. Whole goal remains incomplete.

2026-09-28 16:16 EDT 404 FINAL DELIVERY VERIFIED:
-403 exact startup16:12:04.408;native notice and closed-period completion verified
 below.404 exact startup16:13:06.702;normal participation completion16:13:10.177
 and dispatch16:13:10.178. Home/currentWorld93 and bothDC5 still agree.
-Snapshot survives actual404 reload and subsequent account saves:821title,
 Matches begin at11:00a.m.10/3/2026, all three hidden countersnull, observation
 UTC20:12:16.981583Z unchanged. New fields are display history only. The shared
 Verminion/settings UI shows the date and notice; rendered UI was not captured.
-Mode0/unpaused/campaignfalse;sequence90/pending0/86matches/33wins/all24;caps
 223588gil/40000MGP/4certificates,reserve50000;spent2400gil/10000MGP/4certificates;
 all acquisition/purchase/quest requestsnull. No match, reward or purchase in403/404.
-902/902 full tests passed on403productcode;404 is marker-only. Both vmx.bat builds
 passed0errors/two existingNU1601warnings;diff-checkclean. Version remains0.5.0.3.
 No active tool session or temporary test setting. ADS and unrelated work unchanged.
-Entry UI work remains bounded by closed registration. Open-period confirmation,
 registered response, visible allowance and sole checked Master Tournament selection
 need native evidence. Join is deliberately disabled; no tournament battle is
 authorized by this UI-only pass. Reward collection/full CPU tournament coordination
 and earlier setup/lifecycle acceptance gaps remain open. Whole goal is incomplete;
 no user pause or new blocking decision was requested. Stay on Home World.

403 NATIVE SNAPSHOT VERIFIED /404 RESTORE-COPY INTENT:
403 consumed/dispatched16:12:07.838/.904;home/currentWorld93 and bothDC5 verified.
Native821notice16:12:17.021;closed menu16:12:20.117;actual menu closure and Complete
16:12:21.148. Saved LastTournamentInfo contains exact821title/October3notice,
nullable hidden counters and UTC20:12:16.981583Z. Sequence90/pending0/86matches/
33wins/full24mask unchanged; no battle, reward or purchase.404 marker-only build
passed0errors/two existingwarnings. Restore only mode2->0 and copy404 under earned
state guards; then verify startup/normal already-met completion and saved snapshot
survival.902tests passed on identical403productcode; no tournament Join enabled.

404 MARKER-ONLY BUILD INTENT:403 DLL was copied under the reviewed mode0->2
and earned-state guards. Prepare404 using identical product code and tests while
the bounded UI check runs. Do not copy404 or restore mode before terminal403 is
observed. Next read should reconcile403 startup, notice/menu completion and saved
snapshot; no live log monitoring or tournament battle. Then guarded restore/copy.

403 BUILT / COPY INTENT:vmx.bat passed with0errors/two existingNU1601 warnings;
902/902 full tests passed;diff-checkclean. Fresh saved R7 facts remain mode0,
unpaused,campaignfalse,sequence90/pending0,86matches/33wins/all24;caps223588gil/
40000MGP/4certificates,reserve50000;spent2400gil/10000MGP/4certificates;requestsnull.
Temporarily set only mode0->2 with semantic/concurrent-write guards and copy403.
Expect native821 notice -> owned Upcoming/Nothing -> completed weekly goal, with
dated last observation saved and no match admission. Restore only mode2->0 in404
after terminal403 evidence; verify the snapshot survives that reload. No Join.

403 SOURCE/BUILD INTENT:continue the authorized entry-UI work without a tournament
match. Persist only the last validated display and UTC observation time in existing
per-character progress. Show that dated history in the shared settings UI; correct
the obsolete entirely-unavailable mode description. Saved observations are never
fed to registration, entry, result accounting or scheduler completion. Fresh native
info and owned registration response remain mandatory. One focused regression
covers both serializers, weekly-reset independence, clone/character isolation and
hidden counters replacing the previous period. Build403 with vmx.bat, then tests.
After passing, temporarily select CPU rewards under unchanged earned-state guards
for the closed-period no-match check, then restore Participation in clean404.

402 FINAL RECONCILIATION:startup16:03:57.065 and dispatch16:04:00.588 verified;
normal already-met participation immediately completed. Native home/currentWorld93
and bothDC5 agree. Saved mode0/unpaused/campaignfalse,sequence90/pending0,
86matches/33wins/all24;spending2400gil/10000MGP/4certificates; no purchase, acquisition
or quest request.901/901 tests passed on identical401 product code;402 marker-only
build passed. No active match or handoff. Open-period entry and tournament battles
remain unverified; user narrowed this pass to entry UI and no tournament match.

401 NATIVE CLOSED-PERIOD COMPLETION VERIFIED /402 COPY INTENT:
401 startup16:02:06.627;821notice16:02:19.233; exact Nothing16:02:22.373;
actual menu-closure gate passed16:02:23.404, then Complete with weekly participation
already met. No match/queue/result/purchase. This proves the completed-week branch;
partial-week continuation/accounting has the focused regression, not a fresh battle.
402 marker-only vmx.bat passed. Restore only mode2->0 and copy under existing
sequence90/pending0/86matches/33wins/all24/spending guards; require402 startup and
normal already-met participation completion before final delivery claim.

402 MARKER-ONLY BUILD INTENT:401's bounded closed-period replay may continue.
Build402 with identical product code/tests; do not copy or edit mode until401
terminates. Restore only mode2->0 under full earned-state guards, copy402, and
verify already-met participation completion.401/402 do not enable tournament Join.

401 BUILT / NO-MATCH REPLAY INTENT:vmx.bat passed0errors/two existingNU1601
warnings;901/901fullsuitepassed;diff-checkclean. Runtime remains completed400.
Guarded mode0->2 and copy401 only if sequence90/pending0/86matches/33wins/all24,
caps/spending/receipts and manual pause are unchanged. Expected owned Upcoming
menu -> Nothing -> observed menu closure -> Complete with weekly goal already met.
No new admission is needed or intended. Preserve all earned facts; restore mode0
with marker402 after observed terminal401 result. Registration and Join stay closed.

401 SOURCE/BUILD INTENT (autonomous continuation; no tournament battle):
Source review found closed registration failed before fulfilling CPU mode's
remaining participation requirement. Reuse the ordinary Stage2 loss path after
positive owned Upcoming menu evidence and verified menu closure. A transient
closed-period observation may complete this run, but is never saved or supplied
to scheduler eligibility; later runs still inspect tournament/reward opportunities.
Cancelled/unknown/reloaded admissions keep existing result accounting. Also apply
NeedsBattleRoster before BeginTravel purchases: the prior unconditional Required
lookup could request an unnecessary Mammet despite the later admission exemption.
Reuse the existing policy and add one focused partial-week/cancellation/reload/
weekly-reset test. Extend actual menu-closure waiting to the registration refresh,
and log positive registration once per run. Build401, tests, then temporary mode2
replay only under sequence90/86matches/33wins/all24 guards: already-met participation
must finish without ANY match. Restore mode0 in clean402 delivery afterward.

2026-09-28 15:55 EDT 400 FINAL DELIVERY VERIFIED:
- Exact400 startup15:54:13.827; selected dispatch15:54:17.364 immediately completed
  already-met weekly participation. Native home/currentWorld93 and bothDC5 agree.
- Saved Participation0, unpaused, campaignfalse, sequence90, pending0,86matches,
  33wins, full24mask16777215. Caps223588gil/40000MGP/4certificates, reserve50000;
  spending2400gil/10000MGP/4certificates; acquisition/quest/purchase all null.
- Product registration/entry-preparation code is built and delivered.900/900full
  tests passed on identical399logic;400 is marker-only, vmx.bat passed, diff clean.
  Temporary world-return, sheet-dump and finder-bypass code is removed.
- Verified native Home World return, Master Tournament CFC579 display/highlight,
  and ordinary closed-registration menu/Nothing/status handling. Checkbox was
  unavailable and checked selection remained0 during the closed-period probe.
  No tournament registration, Join, queue, battle, result or additional spend.
- Next native tournament821 begins11:00a.m.10/3/2026. Open-period confirmation,
  registered Talk readback, visible allowance, checked sole selection, actual Join
  and subsequent NPC battles/rewards remain unverified. Entry preparation deliberately
  stops before Join under this user's UI-only instruction. Do not claim full goal
  completion or restore an automatic battle test without a later authorized step.
- User's conditional Home World permission is resolved and exercised. No user
  decision is pending from that question. Stay on Home World after this UI pass.
  First-time setup and earlier remaining lifecycle/campaign-readback gaps stay open.
- No active tool session, VMX match/acquisition or temporary test remains. Existing
  Cactpot/Jumbo work is preserved; I434 remains review-only with its prior conclusion.

399 ORDINARY CLOSED-PERIOD VERIFIED /400 RESTORE-COPY INTENT:
399 startup15:52:52.509; sameHome/current93 verified15:52:56.041. Native821notice
15:53:05.127, owned NPC menu and exact Nothing selection15:53:08.291, followed by
explicit registration-closed status with next start11:00a.m.10/3/2026. No guessed
registration, allowance, Join, match or result.400 marker-only vmx.bat passed;
399productcode retains900/900fullsuite verification. Guarded mode2->0 restoration
and watched DLL copy now; verify400load and already-met participation completion.
User's UI-only pass does not authorize a tournament battle. Final delivery remains
an entry-preparation implementation with open-period/check-box/Join verification
pending; full goal completion is not claimed. No temporary travel/probe remains.

400 MARKER-ONLY BUILD INTENT:prepare clean final delivery while399owns its
bounded closed-period check. Product logic/tests unchanged from399's900/900pass.
Do not copy or restore mode until399terminal NPC exit is observed. Then guard
mode2->0 as the only semantic config change and copy400; require exact startup,
already-met participation completion, sequence90/pending0/all24 and unchanged
earned/spending/receipt facts. Stay on Home World; no further travel or match.

399 BUILT / ORDINARY CLOSED-PERIOD REPLAY INTENT:vmx.bat passed,900/900fulltests
passed,diff-checkclean. No temporary travel/sheet/finder override remains in source.
398 is terminal with no selected entry, admission or credit. Copy399 under mode2,
sequence90/pending0/all24/earned guards. Expect fresh821notice, owned NPC422 menu,
exact Nothing exit and explicit closed-period status. No registration/Join/match.
After that verify400 marker-only delivery with guarded mode2->0 restoration.

398 NATIVE TERMINAL /399 FINAL BUILD INTENT:
398 loaded15:49:21.978. Installed rows428/434/435/469/470 match the implemented
confirmation and response text.15:49:36.494 native list selected Master Tournament
row10; the standard checkbox child5 was unavailable15:49:37.499, and15:49:38.510
readback remained highlightedRegular579/selectedCount0. No Join, queue, credit or
registration. Do not call this a verified checked entry; only display/list selection
is verified while registration is closed.399 removes the probe, retains safe refusal
without sole-selection proof, and must now be rebuilt after that wording correction.
Then full tests and guarded mode2 copy for native ordinary closed-period handling.

399 CLEAN SOURCE/BUILD INTENT WHILE398 UI PROBE RUNS:remove the temporary
game-sheet dump and debug finder bypass entirely. Ordinary CPU rewards now follows
the owned Recordkeeper menu/registration policy; closed menu reports the native
notice and selects Nothing. Keep the permanent strict Master Tournament selection
and cleanup path, with Join still disabled. Show remaining displayed allowance and
explain UI-only readiness in the window. Build399 now; do not copy before398's
terminal selection/cleanup outcome. Then replay399's real closed-period path,
restore mode0 and deliver a clean marker. Registration during an open tournament
and actual joining remain explicitly unverified.

398 BUILT / COPY INTENT:31/31 lifecycle tests passed, including the new entry
policy regression. Concurrent build/test compilation collided on the shared test
assembly; both terminated, then sequential vmx.bat passed0errors/two existing
warnings. Diff-check clean. Guarded-copy398 with mode2 and all sequence90/earned
facts unchanged. Native test selects/checks only Master Tournament, captures the
readback and clears only its own sole selection; never presses Join. Then remove
the temporary sheet/finder override and verify the ordinary closed-period path.

397 FINDER VERIFIED /398 IMPLEMENTATION AND PROBE INTENT:
397 loaded15:38:14.485, observed CFC579 Regular at15:38:30.100, selectedContent0;
native list distinguishes Player Tournament198 from Master Tournament579. Join is
node74/param8; it was never pressed. Installed GoldSaucerTalk422/427/434/435 and
469 supply registration semantics; next398 captures rows421-470 once to verify
the full rule/confirmation/rejection text against the installed game data.
Implement exact owned menus, confirmation and dialogue checks, positive registration
readback, fresh allowance, only-579 finder selection and cleanup. No Join implementation
or battle dispatch in this pass. Add one focused regression for unsafe prompts,
closed/exhausted/unknown allowance and mixed/player duty selections.398 still uses
the temporary selected debug finder probe;399 must remove it and verify production
closed-period behavior. Build398/tests before copy; all earned state preserved.

397 BUILT/COPY INTENT:vmx.bat passed0errors/two existingwarnings;diff-checkclean.
Copy with mode2/sequence90/pending0/all24/earned/spending guards and no config
write.396 is terminal.397 only reads data and opens/closes CFC579; no Join path.

396 HOME WORLD/NPC VERIFIED /397 ENTRY-UI PROBE INTENT:
396 startup15:33:39.408; native15:33:42.998 shows home/currentWorld93 and bothDC5.
Ordinary travel reached Minion Square.15:34:08.042 info now821st tournament,
matches begin11:00a.m.10/3/2026, counters hidden. Owned NPC1011594 menu15:34:16.669:
Ask about the upcoming tournament. / Ask about Lord of Verminion tournaments. /
Nothing. No registration or reward offered, no queue.397 opens exact typed CFC579
Master Tournament through the existing native agent, inspects and closes only its
own selected finder. Never press Join. Include one bounded game-sheet text/menu
capture to obtain registration labels without guessing unavailable UI. Build/test
before copy, retain mode2 temporarily and all earned/spending/config facts.

396 COPY INTENT:395 Lifestream TaskWaitUntilInWorld and final TaskNotify completed
15:32:47.808/.822; territory132 and selected character registration15:32:53.273.
396 clean build passed; temporary world-travel code removed. Guarded mode0->2
change and watched DLL copy may now use the ordinary CPU-mode setup/travel path.
Native Home World equality must still be read by396. Existing inspection never
selects registration or a match; capture the first Recordkeeper menu for next work.

395 TRAVEL ACCEPTED / 396 CLEAN BUILD INTENT:395 loaded15:31:44.471, single
ChangeWorldById accepted15:31:47.942. Lifestream reports GuestToHome and selected
Return to Home World15:31:59.599. No VMX task or battle dispatched. Source396
removes the temporary travel branch entirely; build while Lifestream owns travel,
but do not copy until native login/travel completion is evidenced. Then guarded
mode0->2 and copy396 to run existing info/Recordkeeper inspection only. Registration
and final entry will be implemented from its observed menus; no queue is permitted.

395 BUILT / COPY INTENT: vmx.bat passed with zero errors and the two existing
NU1601 warnings; diff-check clean. Temporary one-shot travel branch uses the
installed ChangeWorldById(uint)->bool contract and rechecks runtime ownership.
Copy only the watched R7 VMX DLL under unchanged earned/config guards. Require
exact395 startup and native travel acceptance before interpreting any world move.
No tournament registration, queue or battle in395; participation config stays0.

USER DECISION / 395 HOME WORLD UI-TEST INTENT:
The user authorizes returning R7 home if tournaments require it, and narrows this
pass to registration/entry UI with no tournament battle. The official World Visit
guide explicitly disallows Lord of Verminion tournament entry while visiting:
https://na.finalfantasyxiv.com/lodestone/playguide/contentsguide/worldvisit/
All 24 clears and existing battle routines remain; harder tournament wins are not
claimed. Use installed Lifestream 2.5.5.2's native ChangeWorldById IPC for one
guarded return to the observed Home World, with no focus/input automation. The
temporary selected-reload branch must require unpaused Participation, sequence90,
pending0, all24, no acquisitions and idle providers, and dispatch no Verminion run.
Build395 through vmx.bat; copy only after fresh unchanged-state guards. Remove the
temporary travel branch before the next delivery. Then inspect Recordkeeper/menu
UI and stop before any tournament queue or battle. No new vendor operation or cap.
The prior travel decision is resolved; goal-tool status may still read blocked
because only the user/framework can resume it. Authorized UI work continues now.

2026-09-28 07:40 EDT 394 DELIVERY RECONCILED:
- Fresh bounded R7 evidence confirms 394 startup at 07:33:41.898 and the one
  selected dispatch at 07:33:45.373, completing already-met weekly participation.
- Saved mode is Participation (0), unpaused, campaign false, sequence 90,
  pending 0, 86 weekly matches, 33 wins and all-24 mask 16777215. Spending remains
  2,400 gil, 10,000 MGP and 4 certificates; purchase, acquisition and quest are null.
- No client/config/source change or new admission in this reconciliation.
  Reviewed tournament read/cleanup paths and existing lifecycle evidence; no new
  proven defect warrants another build or replay of already-passing tests.
- The existing Home World travel question is unanswered. Leave R7 on OCE; actual
  registration/menu research, NPC matches and reward verification need that answer.
  Three consecutive turns now end at this blocker after the prior independent
  reader delivery. Current source still requires Home World access for the next
  Recordkeeper inspection; the separate first-time setup check needs a locked
  character. No independent progress remained. Goal status is now blocked, with
  completion unclaimed; resume after the user's travel decision or changed access.
  Do not repeat the question or treat an automatic continuation as travel approval.
- First-time setup and remaining runtime checks stay pending. Existing 2026-09-26
  entries already document queue/battle FULL STOP, paused reload and rejection of
  the later cancelled result; do not erase that evidence or infer fresh R7 replay.
- Mini Cactpot I434 remains review-only: the supplied log and VMX's hardcoded
  /li home support a VMX housing assumption, not a proven user configuration error.

07:33 393 REPLAY VERIFIED /394 RESTORE-COPY INTENT:393loaded07:32:48.222,
dispatch07:32:51.718, ready-panel info07:32:55.760 followed by HomeWorld gate.
Parent-loading guard accepts ready native data; hidden counters remainunknown.
No registration/admission/credit.394marker-only vmx.bat passed;50focusedtests
cover identical393code including stale populated text during loading. Restore
mode2->0 only and copy394 under full earned-state guards, then verify startup
and already-met participation completion. Travel question is still unanswered.

394 MARKER-ONLY BUILD INTENT:build final delivery while393owns its bounded UI
read; do not copy until393terminal HomeWorld gate or timeout is observed. Battle,
reader and tests unchanged from393(50passed);392priorfullsuite899passed.394copy
must restore only mode2->0 under current sequence90/pending0/all24/earned guards.
No new tournament registration, world travel or battle is intended.

393 BUILT / REPLAY INTENT:vmx.bat passed,50/50focusedtests passed,diff-checkclean.
Guard mode0->2 only and copy393 for the same visitor information-only run. Verify
the loading guard admits the ready native panel, still reports hidden counters
unknown and stops before registration. Then restore mode0 with delivery394.
All24clears/sequence90/86matches/33wins/spending retained; no travel authorized.

07:30 392 DELIVERY VERIFIED /393 FRESHNESS GUARD INTENT:392loaded07:29:16.313,
weeklyComplete07:29:19.761 after guarded semantic mode2->0 restoration.899/899
fullsuitepassed; no earned/spending facts changed. Prior293evidence included a
parent GoldSaucerInfo "Receiving data..." notice; checking only child title/notice
could accept old populated text while the parent still loads.393 checks bounded
visible text in both panels and rejects cached facts during that notice. Extend
existing regression; build/test before copy. No world travel, NPC registration
or active battle. Travel choice remains pending;391visitor info remains verified.

07:27 391 NATIVE INFORMATION VERIFIED /392 DELIVERY INTENT:
391startup07:26:45.690,dispatch07:26:49.105, native Verminion tab7/param5 clicked
07:26:51.128.07:26:53.147 read820thtournament notice ending10:45a.m.9/28/2026;
hidden counters stayedunknown, then HomeWorld gate failed as expected. NoNPC
registration/travel/admission/credit:seq90/pending0/86matches/33wins/all24mask.
Mode2 is temporary test setting and must be restored0.392 only updates marker,
UI status wording and registration-block message; reader unchanged. Build392,
fullsuite, then guarded semantic mode2->0 restoration and copy. Require392load
and already-met participation completion with counters unchanged. Travel answer
stillpending; no permission inferred from automatic goal continuation.

391 BUILT / UI-ONLY REPLAY INTENT:final vmx.bat passed after correcting the SDK
enum name;50/50focusedtests passed,diff-checkclean. Bound R7 fresh config is mode0,
unpaused,campaignfalse,pending0,sequence90,86matches,33wins,all24mask16777215.
For this authorized CPU-mode test, temporarily change only VerminionMode0->2,
compare full JSON semantics and guarded-copy391. Expected visitor flow opens the
information panel, reads notice/visible counters, closes its own window, then
fails the existing Home World gate without NPC interaction/travel/admission.
Restore mode0 after the observation and deliver a fresh marker; do not change
any earned facts, caps, receipts, pauses or travel preferences. Travel answer
remains pending.391native result still unverified at this intent boundary.

391 SOURCE/BUILD INTENT (travel answer still pending): independent lifecycle
review found no new proven accounting defect. SDK LovmRanking is an empty7228-byte
declaration; no typed registration/allowance contract. Revisited existing293native
proof for GSInfoMinionBattle (13values, title10/notice11, counters5/7/9 and hidden
components). CPU rewards now reads this panel before the Home World gate, using
the verified GoldSaucerInfo Verminion button7; hidden counters remain unknown.
No registration or credit inferred. Reuse owned-window cleanup on stop, and keep
registration/world travel blocked. Add focused display-policy regression. Build
391/tests before any authorized R7 UI-only replay; mode0 remains unchanged so far.
390remains loaded; all24clear/sequence90/86matches/33wins/spending unchanged.

2026-09-28 07:15 EDT VERIFIED DELIVERY / NEXT DECISION:
-390FINAL loaded07:15:01.469, dispatched07:15:04.972 and Idle->Complete weekly
participation at that same timestamp. No new admission or credit: sequence90,
pending0,86matches,33wins,mask16777215,CampaignRequested=false,unpaused,mode0.
-All24campaign stages now have observed clears. Final90/389victory07:08:48.527;
native match length7:51; campaignComplete07:08:57.243. Keep Gentleman-only roster.
-390ordinary-stage24 gate removed; battle code remains389.898/898fullsuitepassed,
vmx.bat passed; final DLL at R:\parasite\vmx\VERMAXION.dll. ADS33 unchanged.
-Spending/caps/receipts retained:2400gil,10000MGP,4certificates; caps223588/40000/4,
gilreserve50000; no acquisition/quest/receipt pending. No config edit this context.
-Tournament implementation/live registration, NPC allowance and rewards remain
open. Fresh native07:08:48.514 confirms homeWorld93/current87, homeDc5/current9.
Need user's travel choice because this test character was deliberately sent to
OCE. Do not return worlds on assumed authority; request permission for R7 alone.
-Other acceptance gaps: first-time setup on a suitable locked character; remaining
FULL STOP/reload boundaries during setup/queue/battle; Stage15 post-prompt replay
and Stage23 special-effect readback. Earlier ten consecutive Stage2 victories and
exact victory stopping remain verified. No remaining gap is a false completion.
-Cactpot I434 remains reviewed only, no edits: VMX unconditionally requests /li
home after Mini; supplied log shows missing private house. Narrow fix remains
finish locally after owned-menu cleanup/readiness, pending user's selected scope.
Goal remains active; next interaction is the R7 tournament travel decision.

07:17 390 FINAL BUILT / COPY INTENT:final vmx.bat zeroerrors/twoexistingNU1601
warnings,898/898fullsuitepassed,diff-checkclean. No Zu experiment symbols remain.
Fresh completed-config shows CampaignRequested=false, participation mode0/target5,
unpaused, pending0/sequence90/86matches/33wins/mask16777215; spending2400gil,
10000MGP,4certificates unchanged, acquisition/receipt/quest null. Copy390 under
these guards. Expected selected reload finishes already-met participation without
queue/admission/credit.07:08:48.514native homeWorld93/current87 and homeDc5/current9
confirm tournament Home World blocker remains. No world travel authorized here.

07:14 390 FINAL BUILD INTENT:result nodeLovmResult/23 explicitly7:51 at
07:08:48.513, contradicting earlier inferred six-minute game limit. Final390
retains389Gentleman combat code and replaces only obsolete admission/UI claims:
ordinary stage24 enabled, special development defeat stop removed in favor of
normal three-attempt campaign policy. Zu experiment fully removed before this
build. Rebuild390 via vmx.bat, run suite, verify no residual experiment and then
guarded copy with completed mask/pending0/sequence90. Expected reload must finish
already-cleared campaign without another admission or credit. No config edits.

07:11 ALL24 VERIFIED /390 EXPERIMENT REMOVED:389 match90 explicitVictory
07:08:48.527; credit86matches/33wins; final Complete07:08:57.243 confirms mask
16777215/pending0/sequence90/attempt3/unpaused. This is the Gentleman-only389
composition, not the undeployed390Zu experiment. Removed only that experiment's
source changes; retain389tower reassignment and its898/898fullsuite result.
390must be rebuilt before any copy. Next inspect exact match length (the prior
six-minute-limit interpretation is contradicted by this result), update UI and
admission verification claims, then proceed with remaining lifecycle/tournament
acceptance within R7 scope. No earned facts, spending or limits reset.

07:08 390 SOURCE/BUILD INTENT:389fullsuite898/898passed.90had tower arrivals
07:02:38,03:16,03:55,05:11,06:16 but boss HP stalled11719 across03:07-04:00;
individual attacker orders lose too much movement time. Prepare vendor adaptation:
two already-owned Zu (450HP/speed3/cost10 each per ffxiverminion.com/minion-83)
handle towers; seven Gentlemen attack. Separate minion types avoid pulling the
occupant with attacker group/dodge clicks. Reuse defender reserve/summon accounting,
require both roster entries, rally Zu centrally only between warnings, dodge an
exposed Zu individually. No purchase or spending changes. Build/test390, then
require90explicit result/closure and check automatic attempt3 pause versus manual
FULL STOP before any guarded development reset/copy. No native390evidence yet.

07:01 389 LOADED /90 ACTIVE:exact startup07:00:08.140,
dispatch07:00:11.566, match90queued07:00:24.958, InDuty07:00:32.023.49focused
tests passed before copy. Preserve90through explicit result; attempt3, earned
facts unchanged on admission. No source changes beyond389tower reassignment.

07:00 389 BUILT / COPY INTENT:vmx.bat passed0errors/two existingNU1601warnings;
49/49focusedtests passed and diff-check clean.89 explicitDefeat06:58:02.742,
closure06:58:06.289;85matches/32wins/23clears, pending0/sequence89/attempt2/unpaused.
Copy389 only under these guards, with no config change. Require exact389loaded
marker before evaluating expected90 (third campaign attempt). Keep its result
and automatic pause if failed; no blind reset or extra admission.

06:58 389 SOURCE/BUILD INTENT WHILE89 ACTIVE:388 loaded06:52:14.453,
dispatch06:52:17.835, match89queued06:52:30.764. Native pending dodge preservation
observed06:56:48.721. Tower06:56:13 selected210HP unit; circle pulled it away and
06:56:20 readback had172HP outside tower. Later orders retained this ineligible
candidate while healthy replacements stood at gate;06:56:33 tower expired and
06:56:35 readback showed those replacements400->42HP.389 reselects a healthy
candidate after interrupted movement, retaining living occupants inside or still
travelling on a verified order. Add recorded-position regression and build/test.
Do not copy until89 explicit result/closure. Preserve all config/accounting.

06:52 388 FINAL BUILT/COPIED: final build passed and48/48 focused tests passed.
Retain pending dodge identity across successive unrelated transitions until its
short command finishes; unsafe destinations and no-circle state still cancel.
Guarded copy verified sequence88/pending0/unpaused/attempt1 and empty acquisition/
receipt/quest; saved config unchanged. Next require exact388 startup, then observe
new89 admission and result. All facts remain84matches/32wins/23clears/spent4.

06:50 388 REBUILD INTENT /88 DEFEAT:387 explicitDefeat06:47:04.702,
closure06:47:08.558; native4:52/Bahamut5713HP.84matches/32wins/clears8388607/
sequence88/pending0/attempt1/unpaused/spent4. Safe pending tower preservation
observed46:36/46:48. Extend already-built-but-undeployed388 to preserve a pending
circle escape when unrelated warning changes leave its destination safe; new
danger still interrupts it. This prevents the same all-warning cancellation from
starving dodges. Rebuild388 and rerun48focused tests before guarded copy for89.
No existing admission, result or budget rewritten; all24 clear still unverified.

06:46 SOURCE/BUILD388 READY, DEPLOYED387/88 ACTIVE:388 build via vmx.bat passed
with existing warnings,48/48 focused tests passed and diff check clean. Do not
copy during88. Latest06:46:06 native Bahamut6623/20000, one visibleTwintania642/2280;
army replacing casualties.387 tower arrivals43:52,44:21,44:57; no captured safe-
pending-order preservation event yet. Full clear and388 native overlap handling
remain pending. Match88 entered06:41:46 with roughly26s preparation before the
six-minute game timer; inspect terminal result/closure before guarded388 copy.

06:43 388 SOURCE/BUILD INTENT, DO NOT COPY DURING88:387loaded06:41:22.197,
dispatch06:41:25.587,88queued06:41:38.539/InDuty06:41:46.227.48focusedtests passed387.
Automatic stage limit reset was guarded against sequence87/pausedtrue/attempt3/
pending0 and semantically changed only pause and attempt batch; no manual stop
observed after the verified automatic failure. Current88 is attempt1.
Offline review of87's63 dodge orders found one06:37:39.878 destination inside
another simultaneously visible circle.388 excludes all current circles, recomputes
cached unsafe endpoints and stops if no sampled point is safe. Extend existing
regression. Build/test388 locally; deployed387 must finish88 before any copy.

06:40 387 INTENT /87 DEFEAT:386InDuty06:33:28.900, individual attacks during
towers verified from06:35:29, with protected-unit identity distinct and arrivals.
Defeat06:38:07.859/native4:13 length; closure/review06:38:11.634 automatically
paused at attempt3.83matches/32wins/no24clear. Repeated circles cancelled the same
pending safe gate-to-tower order06:37:50,51,57,38:00; tower expired38:02 before
arrival.387 preserves an existing tower's pending command only if its whole
segment avoids all circles. Add origin/path/destination/expiry cases to existing
regression. Build/tests before copy; inspect bound automatic pause and absence of
later manual FULL STOP, then reset only that pause/attempt batch after this fix.
Do not change earned matches/wins/clears or spending. Final clear remains pending.

06:34 386 LOADED /87 QUEUED: exact386startup06:33:05.122,
dispatch06:33:08.476, CPU queue match87/stage24 at06:33:21.636. Final build
passed and48/48 focused tests passed, including actual-overlap eligibility.
No settings changed on copy: attempt2 before admission; this is attempt3.
Keep87 through its own result before any reload, automatic-pause review or retry.
ADS33 remains loaded, certificate acquisitions verified, cap4/spent4 unchanged.

06:31 386 INTENT /86 DEFEAT:385 recorded explicitDefeat06:24:33.152 and closure
06:24:36.867. Native LovmResult node23="Match Length: 6:00". It is the game's
six-minute limit, not the longer development observer limit.82matches/32wins/
clears8388607/pending0/sequence86/attempt2/unpaused; certificates4 remain credited.
Five tower arrivals and bounded alternate-individual selection worked, but whole
army movement stayed held throughout towers. Last Bahamut6077HP; one add remained
visible (disappearance alone is not a verified kill).
Both published guides re-read; retain Gentleman roster.386 sends other individual
attackers during tower-only periods, excludes the protected occupant from native
hit validation, reuses bounded candidates and spaces per-unit orders10s. Circle
priority unchanged. Add focused scheduling regression; build/test then guarded
copy386 for one fresh match, retaining attempt2 and all earned facts.

06:23 385 CONTROL PROOF: match86 continues. At06:20:09-11 three rejected tower
candidates were followed by verified healthy individual06:20:12.036 and movement
06:20:13.843, without aborting the match. Tower arrivals06:21:00.508,
06:21:30.972 and06:22:38.008 observed (last400/400HP). At06:22:50 Bahamut10900/20000;
chosen Twintania954/2280, other1344/2280. Keep the admission until explicit result;
do not reload or reset attempts. A full clear is still unverified.

06:19 385 ACTIVE: ADS33startup06:16:20.132;385startup06:17:28.286 and dispatch
06:17:31.676 verified. Match86 queued06:17:57.942, Stage24InDuty06:18:07.519.
No reload while this admission is active. First circle response observed; tower
candidate correction and result remain pending. Entire VMX suite896/896passed
on385; ADS166/166passed32 before diagnostic-only cleanup33. Both builds passed
with existing warnings and diff checks clean. Certificate cap4/spent4 retained.

06:17 CERTIFICATE ACQUISITIONS VERIFIED /385 INTENT:384loaded06:14:59.585,
dispatch06:15:02.956. Cursor exact purchase06:15:09.164, once-only2certificate
receipt06:15:10.011, registration06:15:19.072, standaloneComplete06:15:19.089.
Odin and Cursor consumed4 total of cap4; no item purchase gil/MGP.47 VMX tests
passed384. Remove temporary ADS category diagnostics/build33; update VMX UI385
to actual claims. Initial checked recent-items filter remains untested. Build385,
then guarded unpause of automatic standalone completion only; retain stage24,
attempt1, sequence85, earned clears, budgets, receipts. Copy385 and require exact
startup before evaluating Stage24 selection correction. No new acquisition request.

06:14 ODIN VERIFIED /384 INTENT: ADS32loaded06:12:01.723;383loaded06:12:45.965,
dispatch06:12:49.299. Exact live Odin7561 cost2/callback12 verified06:12:55.866;
owned confirmation06:12:55.954, acquired1/currency delta verified06:12:56.323,
VMX credited certificates2 once06:12:56.396. Registration06:13:06.481 and ordinary
standalone Complete06:13:06.490. Saved state paused=true/cap4/spent2/pending-null/
acquisition-null/sequence85. This is automatic completion pause, not user FULL STOP.
Build384 with bounded tower candidate correction. Save standalone Cursor6212/51
request and unpause only for that request; compare full JSON excluding those two
intended fields before write/copy. Do not start battle until verified completion.
ADS32focused166passed. No fresh campaign/tournament/unlock evidence.

06:12 ADS32/VMX383 INTENT: ADS31 loaded06:10:14.438,382loaded06:10:41.498/
dispatched06:10:44.856. Native06:10:51.366 proves Others event receiver IS addon,
ButtonClick25/param3/target=node. Next tick exposes30Others rows despite radio
selected=false: Odin7561/price2/callback12, Cursor6212/price2/callback8. Thus31
verifies category transition; waiting for cosmetic radio state is incorrect.
Keep exact row/currency/price validation and use visible rows after one dispatch.
Build32/383 then confirm prior timeout/empty saved request before another request.
31 ADS166tests passed;382 build passed. No purchase or registration verified yet.

06:10 ADS31/VMX382 INTENT: exact381startup06:02:58.970 and dispatch06:03:02.303;
ADS30 sent Others click06:03:08.089 without crashing, but radio never selected.
Timed out06:03:27.907/VMXfailed06:03:28.658. No purchase callback, spend or receipt;
54 certificates claimed previously.47 VMX tests passed on381;166 ADS tests on30.
Fresh bound state remains unpaused/pending0/sequence85/cap4/spent0/no acquisition.
Add bounded receiver identity and next-tick visible item/price/callback readback;
dispatch is not proof of selection. Build31/382, verify ADS startup, save one
guarded ordinary Odin request and copy completed382. No purchase guard relaxed.

06:02 381 COPY INTENT: user explicitly closed Returner's Bounty and said continue.
Fresh bound state unchanged: unpaused/pending0/sequence85/cap4/spent0/no acquisition,
receipt or quest.381 build completed06:01 and remains ready; ADS30 was loaded after
restart. Save one fresh ordinary Odin request and copy381; require exact381startup
before evaluating this attempt. Native category/receipt/registration remain pending.

06:01 ATTEMPT RECONCILIATION: build381 passed0errors/two existing warnings, but
copy was issued before build completion was observed and copied the older380 DLL.
Do not claim381 loaded. The new saved request dispatched06:01:05.170 and ADS again
stopped06:01:05.236 on unexpected confirmation before vendor interaction; VMX failed
06:01:06.183. No purchase callback/receipt/spend or category test. Do not issue
another attempt until the unrelated Returner's Bounty confirmation is resolved.
Keep built381 for the next deliberate copy; no duplicate re-dispatch or false credit.

381 INTENT: user reports R7 loaded and supplies crash-20260928055003.tspack under
R:\XIVLauncher7. Read ZIP in memory: its crash.log confirms the same prior
AddonShopExchangeCurrency.ReceiveEvent+0x571 -> ADS node-click/category stack;
not a new crash or live proof of30. Latest state pending0/sequence85/unpaused,
certificate cap4/spent0/no acquisition or receipt. Build marker381 via vmx.bat,
then one guarded standalone Odin request; existing ADS start/UI guards must still
reject an unrelated prompt. I434 followup reviewed: installed Lifestream's Home
route explicitly uses private-house aetheryte lookup; VMX hardcodes /li home and
has no Mini destination setting. User misconfiguration is not established. VMX's
unnecessary mandatory housing dependency is fixable; Cactpot remains review-only.

2026-09-28 380 ATTEMPT / RETURNER PROMPT BLOCKER: exact380startup05:54:42.270 and
dispatch05:54:45.640 observed. ADS accepted Odin05:54:46.853 then failed05:54:46.897
before vendor interaction with unexpected-confirmation; VMX terminal05:54:47.913.
Bounded YesAlready evidence identifies the unrelated Returner's Bounty deferral
confirmation (next opportunity at next login). ADS did not accept it. No purchase
callback/receipt/spend. ADS30 category correction still has no native validation.
User must resolve the reward choice in R7 before another deliberate guarded test.
I434 review recommendation: replace MiniReturningHome/MiniWaitingForHome with
local owned-menu cleanup and settled completion, including already-3-ticket entry;
update settings hint and a focused completion/cleanup regression. Preserve Saucy
ticket flow, Jumbo, scheduler and AutoRetainer; no new config. Source671/672 and
supplied11:46:03.055/.123 log prove /li home -> missing private house. Source684
then fails after12seconds; supplied log stops earlier, so that consequence is a
source finding, not an observed terminal result. No Cactpot edit or DevHub mutation.

2026-09-28 R7 RESUMED / 380 COPY INTENT: user restarted R7 and resumed the goal.
ADS30 startup05:51:25.250 is verified. Deployed VMX379 loaded05:51:21.425, reconciled
the saved dispatched Odin request05:52:19.104, and failed without resubmission.
Fresh bound state: unpaused/pending0/sequence85, certificate cap4/spent0, no purchase,
minion acquisition or quest handoff. Save one fresh ordinary standalone Odin request
with full-config comparison, then copy already-built380. Verify receiver-routing
correction through native category transition and exact receipt; do not infer credit
from dispatch. Review-only devhub187/I434 is complete: logs prove Mini Cactpot's
hardcoded /li home requests an unavailable private house; recommend local settled
completion after owned menu cleanup. No I434 source or DevHub records changed.

380 BUILD COMPLETE: vmx.bat passed0errors/two existingNU1601warnings; UI text and
compiled marker only since379. Not copied; deployed379 remains. ADS30 build passed
0errors/two existingobsolete warnings;166focused tests passed. Client crash is the
live blocker; no additional dispatch, receipt, certificate spend or campaign credit.

ADS30 BUILT / R7 RESTART REQUIRED: source/build30 correct registered-event routing;
166/166 focused ADS tests pass, including native callback dispatch with a non-null
data block, copied registration, correct listener and invalid-event rejection.
Both diff checks pass. ADS30 is built at the existing watched output, NOT confirmed
loaded after the crash. Heap event/listener memory was absent from the minidump:
the wrong-receiver explanation is supported by unsafe helper routing and handler
behavior, but actual radio listener identity still needs live observation. Do not
claim the game-side correction verified. Update VMX380's UI to state claims/travel
verified and purchase/registration pending, then build via vmx.bat without copying.
Latest deployed VMX remains379; preserve dispatched Odin request and all caps.
Next: user restarts R7, verify startup/idle state and reconcile saved request before
any new deliberate acquisition. No automatic restart or background log monitoring.

CRASH / ADS30 CORRECTION INTENT: ADS29 loaded21:12:13.873;165 focused tests passed
after fixing Plugin.GameGui reference (initial build failed; earlier test ran28).
VMX379 loaded21:12:45.129/dispatched21:12:49.171. R7 crashed21:12:56 while invoking
TryClickAddonNodeButton from EnsureAchievementCategory. Crashhandler stack confirms
this call; dump shows access violation reading0x10 (r15=0) inside shop ReceiveEvent,
with ButtonClick25/param3/null event data passed by ADS. No category acknowledgement,
purchase callback, reservation or receipt. Bound21:13:48 state remains unpaused,
pendingMatch0/sequence85/certSpent0; saved Odin request DispatchAttempted=true.
DO NOT clear or redispatch it. Claim54 remains; no purchase or registration verified.
Fix the shared node-click helper to dispatch a copied event to its registered
listener with non-null event data, rather than assuming the addon owns the event.
Add one native-receiver regression, build ADS30 and run focused tests. No client
restart is authorized. Native verification needs user restart; preserve the saved
dispatched request so reload reconciles it without another purchase or battle.

ADS29/VMX379 INTENT: ADS28 loaded21:08:45.579, VMX378 loaded21:09:15.402/
dispatched21:09:19.444. Snapshot21:09:26.150 shows radios8Weapons/9Armor(selected)/
10Accessories/11Others, registered ButtonClick params0..3. Node12 is the recent
items checkbox. No purchase callback; failure/cleanup follows. Replace wrong
dropdown assumption/temporary node dump with observed Others event, native radio
state and recent-filter clearance; support verified minion itemAction853 only.
Keep exact live/global row/currency validator. Build29/379, focused tests, verify
ADS marker then guarded standalone Odin request/copy. No certificate spend yet.

ADS28/VMX378 INTENT: ADS27 loaded21:06:22.025, VMX377 loaded21:07:03.495/
dispatched21:07:07.551;165ADS tests passed. Snapshot21:07:14 shows itemTreeList20
but its repeated renderer children consumed the bound before other root controls.
Failed before purchase callback/acquired0. Inspect all root controls/one component
level excluding item tree20, plus at most60 addon strings; same safe rejection.
Build28/378 then verify ADS startup and guarded bound request before VMX copy.

ADS27/VMX377 DIAGNOSTIC INTENT: the21:00:46.492 attempt's
category probe found only node3's job filter; no category or purchase callback.
ADS closed shop and parent menu by21:00:47.509. Fresh bound state21:04:54 is
unpaused/pending0/sequence85, no acquisition or receipt, cap4/spent0/stage24.
Capture at most160 shop nodes with two component levels, labels and registered
events, then fail safely. Do not guess category callback or purchase row.
Build ADS27 watched output and VMX377 via vmx.bat; verify ADS startup before one
fresh guarded Odin request and VMX copy. Claim54 stands; purchase unverified.

376 COPY INTENT: corrected native method spelling to GetAsAtkComponentDropdownList
and wait for AgentShop readiness/empty labels before category inspection. ADS26
build passed0errors/two existingwarnings; exactstartup20:59:37.935;165/165 focused
ADS tests passed including combined row proof. VMX376 built, marker-only since
370's47tests. Diff-checks pass. Save fresh ordinary Odin request/copy376 conditional
on the same bound idle guards/full-config comparison. Native category selection
and purchase remain unverified; no certificate spend has occurred.

ADS26/VMX376 INTENT:375 snapshot20:53:42.373 proves visible count3 describes
only the first reward category: IDs7545/9460/27923, exact prices1/1/2, callbacks
0/1/2 matching the global Agent receive array. Odin is absent from visible arrays.
Select the requested item's localized ItemUICategory using a unique live dropdown
label and its declared native SelectItem; wait for target row before validation.
For owned Jonathas1769813 only, validate visible rows against their global receive
callback indexes, displayed certificate identity and exact price. The native
combined shop name is blank; verified owned exchange/menu replaces that name
check only for this guarded currency kind. Existing shops retain name checks.
One regression covers filtered/global mapping and mismatched IDs/prices/counts.
Build26/376 and test; runtime category transition and purchase still unverified.

375 COPY INTENT: both builds pass0errors/existing warnings;ADS25 exactstartup
20:52:49.306 observed;diff-check passes. Save fresh standalone Odin request only
with bound idle/no-receipt guards and complete semantic comparison, then copy375.
Capture combined row data; unchanged validator prevents spending on unknown layout.

ADS25/VMX375 DIAGNOSTIC INTENT:374 native20:50:22.002 shows3325AtkValues,
blankAgentShopName,33Agent receive rows, total33atvalue3 but3atvalue4. Currency
icon65059 uniquely identifies21172; displayed balance54at86. Odin isAgent row12,
not its sheet row8. Existing validator therefore correctly rejected this combined
shop before callback; cleanup complete20:50:23.026. Extend the same bounded
snapshot to existing item/price/callback arrays and target offsets across3400
values maximum, leaving purchase validation unchanged. Build25/375, then one
fresh guarded request after matchingstartup/idle checks. No spend or receipt.

374 COPY INTENT: ADS24 build passed after correcting a local diagnostic variable
name; exact24startup20:49:41.854 observed. VMX374 built with existing warnings.
Save one fresh standalone Odin request/copy374 only with the bound idle guards
and full semantic comparison. This attempt gathers the rejected currency-window
layout and must not buy unless all existing validators pass. Cap4/spent0; claimed54.

ADS24/VMX374 DIAGNOSTIC INTENT:373 dispatched20:47:21.955. ADS23 opened the
currency shop20:47:28.491; native readiness arrived, but existing row-layout
validator rejected it20:47:28.656. No purchase callback or receipt. Both shop and
parent menus closed by20:47:29.671. Add one bounded mismatch snapshot of value
counts/header, currency identity, shop name and receive rows to establish the
actual layout before changing validation. Build ADS24 and marker-only VMX374;
do not infer transaction success or change cap4/spent0. Prior tests164ADS/47VMX.

373 BUILT / ADS23 WATCHER RECOVERY: both builds pass0errors/existing warnings;
164/164 ADS tests pass including currency-kind/confirmation checks. ADS23 reload
20:45:43.110 failed on the observed transient ADS.json build lock. Trigger settled
DLL once, verify exact23startup, then fresh guarded Odin request with unchanged
cap4/spent0 and no pending match/purchase/acquisition. VMX373 marker only since
370's47tests; no battle behavior changes. No certificate spent yet.

ADS23/VMX373 INTENT:372 dispatched20:43:19.191. ADS22 selected localized exchange
entry0 at20:43:27.419; native ShopExchangeCurrency opened, but certificate offers
still expected ShopExchangeItem. Timed out20:43:42.723 with acquired0/no receipt;
ADS closed currency shop and parent menu20:43:43.724. Add a certificate-specific
currency-exchange kind using the existing native currency validator, callbacks
and preview-confirmation token. No callback values guessed. Build/test ADS23 and
marker-only VMX373; preserve cap4/spent0 and verify idle before fresh request.

372 COPY INTENT: ADS22 exact startup20:42:43.182 confirmed after one settled
watcher trigger. Save a fresh ordinary standalone Odin acquisition only with
bound unpaused/pending0/sequence85/no receipt or handoff/cap4, semantic-compare
the complete config, then copy built372. Claim balance54 is not purchase credit.
Observe unique localized menu selection, live shop validation and exact2certificate
receipt; preserve this operation through its terminal result and ADS cleanup.

372 BUILD READY / ADS22 RELOAD CHECK: both builds pass0errors/existing warnings,
diff-checks pass. ADS reload20:41:47.937 failed; read its exception and, only if
the known transient ADS.json build lock, trigger the settled DLL watcher once.
No VMX copy or fresh request before exact ADS22 startup. Native claim0->54 is
verified; purchase/registration remain unverified. Existing164ADS/47VMX tests
cover unchanged policy code;22changes only native menu recognition.

ADS22/VMX372 INTENT:371 dispatched20:39:10.075; native city transfer27 accepted
20:39:18.783, arrived133 at20:39:25.321, approached/interacted with Jonathas
20:40:11.956. Certificate balance rose0->54 at20:40:13.500: first claim verified.
Purchase failed20:40:20.919 on the outer ENpcData menu step, acquired0/no receipt;
ADS sent owned SelectString cancel. Jonathas directly enters CustomTalk, so
recognize that outer step only when its unique localized exchange entry is
actually visible; then select that entry for the SpecialShop link. No guessed
callback index. Build ADS22 and marker-only VMX372; no new purchase until loaded
ADS22 and saved empty receipt/handoff are verified. Cap4/spent0 remain.

371 COPY INTENT: ADS21 startup20:37:39.114 verified;164/164 focused ADS tests
passed including city transfer/no-arrival/cancellation/character change. Both
ADS21 and VMX371 builds pass0errors with existing warnings;diff-checks pass.
VMX371 changes only its marker since370's47/47 tests. Save one fresh standalone
Odin acquisition with cap4 unchanged, conditional on pausedfalse/pending0/seq85/
no receipt or handoff; semantic-compare everything else before copy371. Observe
city transfer and actual vendor dialogue. No battle or certificate spend inferred.

ADS21/VMX371 INTENT:370 loaded and dispatched its saved Odin request; ADS20
resolved the exact2certificate offer but failed20:33:05.048 with no-route before
travel/callback/spending. Receipt/handoff empty; cap4/spent0/pendingMatch0 remain.
Old Gridania133 lacks a main aetheryte, while Gridania2 is in132. Extend ADS's
existing route with main2 -> Lancers'Guild27 -> actual133 arrival, then ordinary
NPC navigation. Transfer IDs verified against Aetheryte sheet. Add cancellation,
character-change and no-arrival regression cases. Build ADS21 (watched) and
VMX371 marker via vmx.bat; test before saving one fresh Odin request/copy.

370 NATIVE ACQUISITION INTENT: ADS20 exact startup20:31:52.781 confirmed after
settled-output watcher trigger. VMX370 built/tested47/47. Fresh bound state still
unpaused/pending0/sequence85 with no purchase/handoff. Set only this character's
certificate cap to4 and save a standalone, not-yet-dispatched Odin request using
the ordinary acquisition model, then copy370 to R:\parasite\vmx\VERMAXION.dll.
Full semantic comparison must preserve all other config/progress. This reload
reconciles/dispatches the saved acquisition, not another Stage24 admission. Owned
Odin or its item prevents a purchase. Completion pauses Verminion after registering;
no stage composition is changed. Require exact370startup and actual ADS evidence.

ADS20 RELOAD RECOVERY / VMX370 BUILT: ADS watched build attempted reload20:25:06
but its manifest was still locked by the build. No ADS20 startup was observed.
Build is finished; trigger the existing watcher once on the settled DLL, then
require exact ADS20 startup. VMX370 vmx.bat build passed0errors/two existing
NU1601 warnings;47/47 focused Verminion/dependency/Lifestream/UI-close tests pass.
No vendor dispatch, VMX copy, certificate cap change or spend yet.

VMX370 BUILD INTENT: certificate cap/receipt accounting and separate Odin/Cursor
acquisition actions added. Claims/travel/menus/purchases stay in ADS; the existing
saved acquisition identity now distinguishes standalone registration and completion.
Default cap0; no runtime settings changed. UI excludes shared-form Minion of Light
until form selection is proven. ADS20 build passed0errors/two existing obsolete
warnings;160focused shop/catalog/IPC tests passed. Build VMX370 via vmx.bat and
run focused accounting/lifecycle tests before choosing a native acquisition test.
Do not copy370 yet: a reload would otherwise restart the selected Stage24 test.

ADS20 BUILD INTENT: add opt-in Jonathas visit to the existing guarded runner,
managed certificate balances, localized unique exchange-menu selection, owned
Talk advancement/cleanup, and separately reported positive claim deltas. Actual
post-dialogue funds and saved caller authorization still gate purchases. No
vendor dispatch selected yet; R7 VMX369 is failed/idle after UNKNOWN85. Build
the established watched x64 ADS output and run focused shop tests. VMX certificate
cap/accounting and an explicit shortlist acquisition action are the next source
batch. Native claim/menu/receipt behavior remains unverified.

ACHIEVEMENT ACQUISITION APPROVED: user selected "Include ADS claims and purchases"
after the cost/maintenance interview. Extend ADS's existing guarded purchase flow
and VMX's character-owned purchase accounting with a separate certificate cap.
All travel, NPC dialogue, claims, purchases and cleanup remain ADS-owned. Defaults
stay zero; no blanket purchase of the reviewed catalogue or unverified roster swap.
369 match85 failed19:11:08.471 when an overlapping tower click selected a low-HP
Gentleman. No observed result: UNKNOWN/uncredited. Native duty exit observed by
ADS19:12:50.495; bound pending0/sequence85/attempt1/mask8388607,81matches/32wins,
unpaused. No VMX reload until a deliberate next attempt is recorded. ADS source
build is watched by R7 at Z:\ADS\ADS\bin\x64\Debug\ADS.dll; its existing route
and version0.9.4.2 remain in scope. Current loaded/source VMX369, ADS19.

369 FRESH ATTEMPT: exact startup19:05:56.005/dispatch19:05:59.971; entered24
19:06:19.723. Native readiness was already outside84, so369started match85;
84remains UNKNOWN/uncredited (no observed outcome/closure). Bound pausedfalse/
pending85/sequence85/attempt1 verified; Bahamut14722HP at19:07:54.479. Keep this
admission intact through outcome.369is both current source and deployed DLL;
no build pending.46/46relevant tests passed. Result-preserving cleanup's native
replay remains pending because369did not find the old result still open.
All12achievement-vendor items are reviewed; shortlist in previews. No ADS edits
or certificate actions in this continuation. Vendor/claim implementation remains
ADS-only, and no FATE minion is required by the current roster.

369 BUILT / RESET-COPY INTENT: final vmx.bat0errors/two existingNU1601warnings;
46/46relevant tests and diff-check pass. Fresh bound config pausedtrue/pending0/
sequence84/attempt3/81matches/32wins agrees with automatic19:00:16.473failure;
no later manual FULL STOP in bounded log. Reset only automatic pause and attempt
count with full JSON semantic comparison and exact pre-write text check, then
copy369. It must preserve any visible result through cleanup and credit nothing
without a matching admission. Reconcile exact369startup and resulting state
before another reload. All caps, spending and character binding remain intact.

368 FAILED SELECTION /369 FIX INTENT: match84failed19:00:16.473, no result
credited, automatic third-attempt pause. Intended tower unit40007106but native
click hit40007109; both were400HP Gentlemen in19:00:12.291snapshot. Existing
exact-single guard rejected a valid overlapping friendly unit.369uses existing
nonexact-single selection for towers only, verifies actual selected unit alive
and>=halfHP, then tracks that actual ID for movement/arrival. Other mechanics
retain exact identity. No new retries or selection transport. Initial369guidance
build passed before this addition; rebuild and run relevant tests. Review
automatic pause against fresh config/log, reset only its pause/attempt count,
then copy369: pending0 means passive observation of any ongoing battle/result,
never a replacement admission inside it. New admission only from ordinary native
readiness after it ends. UNKNOWN84 must remain uncredited. No ADS/currency edits.

368 LOADED /369 PREVIEW INTENT: exact368startup18:55:57.941, dispatch18:56:01.874;
fresh24opening observed. Preserve its third admission through result closure.
369aligns stale UI/admission text with implemented circle/tower responses and
the540s development window; correct Stage20preview's obsolete fresh-start gap
using335's recorded first mixed-opening clear. No tactic changes in369yet.
Build via vmx.bat before any copy; marker-only/guidance changes need no repeated
unit tests after368's46/46. No claim of Stage24completion or certificate support.

368 BUILT / COPY INTENT: final vmx.bat0errors/two existingNU1601warnings;
46/46relevant tests and diff-check pass.367match83control cutoff18:53:46.869,
explicit Defeat18:55:14.266 credited81matches/32wins; closure18:55:17.758.
Bound pausedfalse/pending0/sequence83/attempt2 verified. Copy368for third reviewed
attempt, preserving configuration. Verify exact loaded marker and near-target
dodges/tower arrivals across the extended bounded control window. No reset of
attempt limits, accounting, roster caps or acquisition settings.

368 ADDITIONAL TACTIC INTENT: first368build0errors/two existingNU1601warnings;
46/46Verminion/task-dependency/Lifestream/UI-close tests passed.367 now verifies
stable circle destinations and a single tower movement: second tower order
18:53:20.176, verified movement18:53:22.024, arrival18:53:25.499at286HP, warning
cleared18:53:40.239. No repeated selection while travelling. Boss damage is slow:
escape sides chosen from tiny army-center offsets spread the fight across the
board. Choose the same six-yalm escape ring's point nearest the attack target;
do not change inferred radius. Extend development control360->540s within the
existing600s result timeout, since367reached adds near its control cutoff.
Add target-direction geometry assertion. Rebuild368and run focused checks.
Preserve367admission until result/closure; no config, certificate or ADS changes.

368 INTENT WHILE367RUNS:367startup18:47:22.761/dispatch18:47:26.655; fresh
Stage24opening observed. Preserve this admission through its own result.
Fix observed366startup evidence loss: ForceStop's generic UI cleanup can preserve
LovmResult only for the selected Verminion debug reload; suppress its fallback
ESC in that case so Verminion reads/closes the result. Explicit FULL STOP keeps
normal cleanup and pausing. No accounting rules changed. Build368and relevant
tests; do not copy over367's unresolved fight. Native reload-result replay pending.

366 RECOVERY CLOSED UNKNOWN /367 COPY INTENT: startup cleanup closed an already
visible LovmResult at18:44:20.825 before Verminion sampled its outcome. At
18:44:25.910 native duty identity was0 and recovery failed as ended without
observed result. Thus82is closed with UNKNOWN outcome and no credit, not a
verified defeat. No repeat admission occurred.367vmx.bat passes0errors/two
existingNU1601warnings;diff-check passes. Copy367with bound pausedfalse/pending0/
sequence82/attempt1; no settings edit. New attempt tests stable escape points and
one verified movement order per tower. Preserve startup cleanup's result-evidence
gap as a remaining lifecycle issue, particularly for reloads on a result screen.

365 FIRST TOWER PROTECTION VERIFIED /367 REPLAY INTENT: compare full native
party snapshots18:41:20.625 and18:41:29.267 around18:41:26.663tower disappearance.
Occupant4005169E:400->42HP. Other7units retained identical HP except one248->190
(58), so none took the358tower hit. This verifies one occupied tower resolution,
not the whole stage.366 remains in passive orphan-result recovery.367 changes
only compiled reload marker for the corrected circle/single-tower replay after
that recovery finishes. Build367; do not copy before explicit uncredited result
closure and matching pausedfalse/pending0/sequence82/attempt1.43relevant tests
passed on identical366battle code; do not repeat them for marker-only input.

366 LOADED FOR RECOVERY: exact startup18:44:17.036; dispatch18:44:20.904;
resumed24passively with pending0. No additional combat admission. First365tower
occupant was400HP at18:41:20.625 and42HP at18:41:29.267 after tower clearance;
the other minions' protection still needs a paired full-party comparison.
365first circle left the first four attackers at400HP in18:38:59.520 readback,
but a newly spawned replacement took358damage. Later circles lost units while
the escape point reversed.366's stable destination/travel fixes await fresh run.

366 BUILT / PASSIVE RECOVERY COPY: final vmx.bat zero errors/two existing
NU1601warnings;43/43relevant tests and diff-check pass. Copy only when bound
config remains pausedfalse/pending0/sequence82/attempt1. This reload reconciles
the abandoned live battle; it must neither send combat orders nor credit its
result. No settings changes. Next exact366 startup and uncredited result closure.

365 TOWER ARRIVAL /366 RECOVERY INTENT: first tower at(5.980,9.220) appeared
18:41:06.724. Individual4005169E arrived18:41:14.759 at(5.655,9.508),HP400/400;
warning cleared18:41:26.663. Second tower appeared18:41:41.757 at(8.470,-6.620).
The chosen unit was travelling toward it, but repeated re-selection failed at
18:41:57.771 and abandoned82without result. Bound pausedfalse/pending0/seq82/
attempt1/80matches/32wins verified. Add to366: once single selection and its
movement click are verified, let that order finish rather than selecting again;
reassign on death or an interrupting circle. First366build passed before this
addition, so rebuild and rerun43tests. Copy366 ONLY for existing passive orphan
result reconciliation; no new admission or credit. Preserve earned counters.
After positive uncredited result closure, a new marker is required for replay.

365 LOADED /366 INTENT: exact365 startup18:37:46.172; dispatch18:37:50.040;
entered24at18:38:09.968. Native circle at18:39:54.557(-1.220,-3.690) caused
successive escape destinations(-5.463,-7.933), then(4.780,-3.690), then back;
observed selection+movement clicks completed, but this reversal is unsafe.
366 retains one destination per native circle and prioritizes an exposed anchor
instead of a nearby denser group outside it. Update Stage24 preview to implemented
but unverified responses. Build/test366; preserve365's ongoing admission through
positive result/closure before copy. No victory or tower protection claim yet.

365 BUILT / RESET-DEPLOY INTENT: vmx.bat zero errors/two existing NU1601
warnings;43/43Verminion/task-dependency/Lifestream tests and diff-check pass.
Regression setup now observes the native unit baseline before requesting a
replacement, matching the production lifecycle. Fresh config still shows the
automatic pause/pending0/sequence81/attempt3 and matching spending caps; bounded
log window has explicit defeat+closure+attempt-limit failure and no later
manual FULL STOP. Reset only that automatic pause and stage attempt count,
validate full JSON equivalence except those fields and recheck raw text before
write, then copy365 to R:\parasite\vmx\VERMAXION.dll. Next verify exact loaded
marker, dispatch, native dodge/tower orders and actual position readbacks.

365 INTENT: reconciled364match81 explicit Defeat18:31:03.169, credited80matches/
32wins; closure18:31:06.776 and automatic third-attempt pause18:31:06.785.
Bound account+debug-character pending0/sequence81/attempt3/mask8388607 verified.
364paired red-circle image18:27:18.420 matches slot11gtae at(-0.020,0,11.070),
active18:27:15.958..18:27:23.000; tower identity already visually confirmed363.
Complete the interrupted source edit: cancel stale pending orders on native omen
transitions, dodge exposed groups with conservative clearance, single-select one
healthy Gentleman for towers and verify arrival. Hold ordinary movement while
warnings exist; allow bounded summons. Remove image preparation delays/unused
request state. Ground reads continue beyond the64-sample logging cap. Geometry
regression includes arena edges and replacement-capacity accounting. Build365
via vmx.bat and focused tests before deployment. Reviewed automatic failure can
be reset for this changed tactic only after checking no later manual stop;
preserve every other character/config/progress/spending field. Runtime response
and clear remain unverified; ordinary Stage24 admission stays guarded.
Achievement review: all12Jonathas items considered; Odin/Cursor/WHM Light guide
alternatives displayed. No FATE requirement. ADS certificate claims/purchases
still open; no certificate spend/config change occurred.

364 BUILT / DEPLOY INTENT: vmx.bat zero errors/two existing NU1601warnings and
diff-check pass; preceding363relevant suite42/42.363match80 Defeat18:25:10.191,
credited79matches/32wins; closure18:25:14.140. Native slot14 path
vfx/omen/eff/m0117_trap_01s.avfx appeared18:22:59.986 at(5.66,0,2.60),
scale(3,7.2,3),cleared18:23:20.150. Existingimage18:23:07shows a tower to the
right of center stone, consistent with that position during the sole active
ground effect. Later towers20seconds each at(8.75,9.10),(12.23,-3.63),(-11.90,-9.41).
Short gtae omen remains7seconds/~17s cadence; its paired image still needed.
Copy364 only pausedfalse/pending0/sequence80/attempt2; no configuration edits.

363 NATIVE GROUND READ VERIFIED /364 INTENT: startup18:20:34.546, dispatch
18:20:38.414. First empty collection18:21:03.534;slot11path
vfx/omen/eff/m0117_gtae_01s.avfx at(-0.62,0,11.19),scale(3,7.2,3)
18:21:29.950,removed18:21:36.951;nextsamepath(-9.62,0,3.02)18:21:47.014.
Thus native classes, ownership, resource/path, coordinates and lifecycle resolve.
Image18:21:32.374 was subsequently overwritten by unrelated selection retry
at18:21:57; no visual identity claim from that artifact.364 suppresses only
Stage24 selection-retry images, preserving paired ground snapshots at existing
path. Build364; preserve363match80 through closure. No tactic change yet.

363 BUILT / DEPLOY INTENT: final vmx.bat zero errors/two existing NU1601warnings;
42/42 Verminion/task-dependency/Lifestream tests and diff-check pass.362match79
Defeat18:19:43.155 credited78matches/32wins; closure18:19:46.981. Bound
pausedfalse/pending0/sequence79/attempt1 verified. Copy363 without settings edits.
Next verify director ground paths/coordinates against its bounded images; no
warning or tower tactic is inferred yet. All24-stage completion remains open.

363 REVIEW: first build zero errors/two existing NU1601 warnings. Before copy,
also log the first empty ground collection and dispose the scoped process/module
lookup's process handle. Rebuild363 and diff-check;362match79 still unresolved
at18:17:27, focused add2124HP. No runtime or configuration changes yet.

362 LOADED /363 INTENT: exact362 startup18:14:28.413, dispatch18:14:32.283,
entered24at18:14:52.635; code captured18:14:57.700. NativeE7CA10 takes an
effect ID and x/z, calls8588E0, stores VfxData in director+718[index*8],
registers its listener atdirector+620 and removes/clears it on ID0.390630
reads VfxData+1B8 then writes matrix atinstance+20..50 (translation+50).
363 reads slots11..29 of the proven30-slot array on framework thread, checks
published VfxData table215ED80 and VfxResourceInstance215ED70, listener ownership,
finite arena coordinates, then declared resource/path fields. At most64changed
samples,4distinct-path images in existing360s probe; no retained pointers or new
hook. No hazard semantics yet. Replace604image trigger with these ground effects.
Build363; preserve362match79 through explicit result/closure before copying.

362 BUILT / RESET-DEPLOY INTENT: vmx.bat zero errors/two existing NU1601 warnings;
diff-check passed.361match78 explicit Defeat18:12:51.730 credited77matches/32wins;
closed18:12:55.624 with automatic third-attempt stop. Bound pausedtrue/pending0/
sequence78/attempt3 verified18:13:56; no later manual stop in bounded evidence.
Reviewed these failures; next diagnostic follows actual native effect calls.
Reset only this automatic pause and attempt count, compare full JSON semantics
and exact pre-write text identity, then copy362. Preserve all progress/spending.

361 LOADED /362 INTENT: startup18:07:54.325, dispatch18:07:58.235, entered24
18:08:18.265;24576code bytes captured18:08:23.314.30VfxData entries include
facility/tutorial markers, so do not assume tower ownership. NativeE77FD0 creates
by ID through858CF0, applies a matrix through390630, registers a director listener.
362 replaces the large capture with4096remaining director code bytes atE7BBD0,
1024at the observed858CF0call target,128at390630; gated to observed setupRva
E75BD0/content800A and image bounds. Never invoke these unknown methods.
Build362; preserve361match78(third attempt) through closure before deployment.
Match77 add HP was564in last snapshot then absent while other add remained;
only target transition is directly verified, not an exact zero-HP death sample.

361 BUILT / DEPLOY INTENT: vmx.bat zero errors/two existing NU1601 warnings;
diff-check passed.360match77 Defeat18:07:10.561 credited76matches/32wins,
closure18:07:14.504. Its target switched from the focused add back to Bahamut
before defeat (boss9990HP); add-target transition now observed. Copy361 only
with pending0/sequence77/attempt2/pausedfalse; no configuration changes.
Next inspect expanded code in memory, retaining ordinary Stage24 admission block.

360 LOADED /361 INTENT: exact360 startup18:02:11.084, dispatch18:02:14.950,
entered24at18:02:34.724. Corrected guard captured4096code bytes18:02:39.763.
In-memory capstone inspection shows30VfxData pointers at director+6C0, ticked
by virtual294(E762F0) through390870;390B80 is published VfxData.SetVfxDataListenner.
This is separate from the inspected scene list. No object-memory casts added.
The first window ended during update routineE76820.361 widens this same once-only
executable-code window to24576bytes to include its nearby update/effect routines.
Build via vmx.bat; preserve360admission through result closure before deployment.

360 BUILT / DEPLOY INTENT: vmx.bat completed with zero errors and the two
existing NU1601 warnings; diff-check passed.359 match76 explicitly Defeat at
18:00:17.712, credited75matches/32wins; closed18:00:21.683 with review stop.
Bound pending0/sequence76/attempt1/stage24 and pausedfalse verified18:01.
Copy360 without configuration changes. Next verify exact startup and capture
the bounded native director code through the corrected content-type guard.
Achievement alternatives remain reviewed/displayed; ADS certificate handling
is still unimplemented. No certificate claim or purchase is authorized by caps.

359 LOADED /360 INTENT: startup17:55:54.771, dispatch17:55:58.663, entered24
17:56:18.700. Native director is now event800A0003 but same verified tableRva
21D7330/slot2E75BD0. Low EventId entry changes across battles;359's full-ID guard
therefore correctly produced no code read, but was too specific.360 checks the
stable800Acontent-type half instead. No other behavior changes. Build360 and
diff-check; previous359relevant suite42/42. Preserve359match76 through closure.
No warning semantics established; active log57.3MB at17:58, below100MiB cap.

359 BUILT / RESET-DEPLOY INTENT: vmx.bat0errors/two existing NU1601warnings;
42/42relevant tests and diff-check pass.358match75 Defeat17:53:45.869 credited
74matches/32wins; closure17:53:49.865/automatic third-attempt stop17:53:49.874.
Observed regroup orders during reinforcement stream17:51:27.954/17:52:48.522;
actual group recovery remains unverified. Timeline states observed3/6/13 only;
0static/910actor/0director callbacks. Bound pausedtrue/pending0/sequence75/
attempt3/stage24 confirmed17:55:16; no later manual stop. Reviewed this failure;
359 changes the state-discovery probe using verified native class/code addresses.
Reset only automatic pause/attempt count with full JSON equivalence/raw identity
check, then copy359. All earned progress, caps and character binding stay intact.

358 LOADED /359 INTENT: startup17:49:17.927, dispatch17:49:21.682. Native
director17:49:46.651 event800A0001/content7/tableRva21D7330 exactly matches
ClientStructs main ida/data.yml VerminionDirector. Class overrides cluster at
E75BD0..E762F0; slot2=E75BD0.357/358 only read declared vtable addresses, no
unknown function calls.359 records one4096-byte executable-code window at
that live slot2, bounded to this process's main executable, once per probe via
existing log. Use existing installed Python capstone for in-memory inspection;
no new tool/file, signature, hash, dependency or character-memory dump. This
locates the native battle-state owner after casts/scene/director updates failed.
Also reset observing flag on probe initialization failure. Build359; preserve
358match75 to result closure. Timeline's initial native state0/0/0,slot0=3.

358 BUILT / DEPLOY INTENT: vmx.bat0errors/two existing NU1601warnings;42/42
Verminion/task-dependency/Lifestream tests and diff-check passed. Verified bound
pausedfalse/pending0/sequence74/attempt2/stage24 at17:48:56. Copy358 without
configuration changes. Verify its exact marker, independent regroup cadence,
actual unit arrival and new timeline/director data.357 never deployed.

358 INTENT:357 built0errors/two existing NU1601warnings, not copied.356match74
Defeat17:46:11.775 credited73matches/32wins; closure/review stop17:46:15.359.
Probe ended0static/771actor/0director samples. Source review found regrouping
shares bossWaveUtc with frequent reinforcements; waves can indefinitely postpone
its15s threshold.358 gives24 its own transient regroup timestamp, leaves12's
timing intact and reuses the existing policy at6rather than10yalms separation.
354 observed units stopped around(6,2) while boss/attacking party were(15,4),
so the old threshold also missed them. Focused policy regression extended.
Retain357's bounded native timeline/director identity reads. Build358 through
vmx.bat and deploy only at bound closed74/pending0/pausedfalse/attempt2 boundary.

356 LOADED /357 INTENT: exact356 startup17:41:59.283, dispatch17:42:03.074,
entered24at17:42:23.090. Director hook initialized17:42:28.159 but no update
samples through17:43:57(first attack cycle); do not infer warnings from it.
Broader Verminion/task-dependency tests39/39passed.357 records up to64changed
native Bahamut timeline states using declared Timeline fields, and once reads
up to16Director identities plus their declared300-entry vtables, only addresses
within this process's executable image. No unknown method calls, signature or
raw instance-memory dump. These reads investigate missing mechanics, not tactics.
Build357;356match74 remains owned until explicit result/closure. No config edits.

356 BUILT / DEPLOY INTENT: vmx.bat0errors/two existing NU1601warnings;28/28
focused tests and diff-check passed.355 match73 Defeat17:40:37.324 credited
72matches/32wins; closure/review stop17:40:41.232. Bound pausedfalse/pending0/
sequence73/attempt1/stage24 verified17:41:38. Copy356 without config edits.
Next: exact356 marker and native director-update delivery/categories.355's
one regroup command has not yet proven its isolated group's actual arrival.

355 COVERAGE /356 INTENT: exact355 startup17:35:56.038, dispatch17:35:59.808,
entered24~17:36:19. Scene walk includes all observed draw roots(2initial,10with
army),439-447objects,382BgObject,24VfxObject,0Decal; complete within512bound.
First604image's model paths are permanent arena assets; no warning identified.
Stage24 isolated-group order observed17:38:18.119; actual rejoin still unverified.
Inspect compiled ECommons3.2.0.17 DirectorUpdate (existing nine-argument full
callback, unlike older local source). VMX has no prior initialization.356 reuses
that hook for at most64framework-thread event/category/six-argument samples in
the existing360s probe, disposes only its owned registration on cleanup. No new
signature, dependency, external tool or gameplay inference. Build356; preserve
355match73 through result closure before deploying. Achievement purchases pending.

355 BUILT / RESET-DEPLOY INTENT: corrected a local-name compile collision, then
vmx.bat0errors/two existing NU1601warnings;28/28focused tests and diff-check pass.
354 match72 explicit Defeat17:33:49.121 credited71matches/32wins; closure17:33:53.080
and automatic third-attempt stop17:33:53.088. Boss9990HP; no Stage24 clear.
Verified bound pausedtrue/pending0/sequence72/attempt3/stage24 and no later manual
stop. Reviewed failure; new355 tests scene coverage and isolated-group pursuit.
Reset only this automatic pause and stage attempt count, preserving all other
JSON semantics with exact pre-write identity check; copy355 afterward. Do not
override manual FULL STOP or change spending, earned facts or character binding.

354 LOADED /355 INTENT: exact354 startup17:29:20.695, dispatch17:29:24.440,
entered24at17:29:44.389. First single selection001recipient17:30:08.156;
second click added nearby partner17:30:08.426. Later second click added two
recipients17:30:39.282. Group extension works; separated units can be left behind.
636 repeats about5s apart with ordinary38HP losses; do not interpret every636
as a large attack. First60417:30:49.609, clear image17:30:51.901 shows red circle
with overhead orb near the right stone. Header/target-info hiding now verified.
An attacker had already disappeared before first604; its meaning remains
unproven and no reversed phase rule is justified. Scene still446objects/24VFX.
355 adds declared object-table draw roots to the same512object/64effect scene
probe, type/coverage counts, and model paths only at the two image snapshots.
Reuse Stage12's existing isolated-group pursuit on24 with one regression update.
Build355; preserve354's match72 through explicit result/closure before copy.

354 BUILT / DEPLOY INTENT: vmx.bat0errors/two existing NU1601 warnings;
diff-check and27/27focused lifecycle/Lifestream tests passed.353 match71 explicit
Defeat17:25:54.492 credited70matches/32wins; closure/review stop17:25:58.133.
Bound pausedfalse/pending0/sequence71/attempt2/stage24 verified17:29. Deploy354
without settings changes. Next: exact marker, repeated boss-effect HP timing,
selection001 recipients and first604 image. Clears remain1-23.

354 INTENT: remove353's unproven636->604 gate-retreat state, preserving Stage19.
353 stopped attacking after its second636 and never received an inferred ending
604; boss stayed18089HP. Controls released17:24:03.519 at360s, admission71 still
pending. Capture at most16repeated636/604 event HP/position samples and32selection
001recipients within the existing actor hook. No gameplay meaning inferred.
Move the single effect image to first604 and include underscore-prefixed Lovm
panels in temporary hiding, with existing restore. Two-add image remains.
Re-read both published Stage24 guides: Gentleman-only is explicitly supported;
circles require avoidance, pillars require one healthy occupant. Build354 through
vmx.bat; deployment waits for71 result closure. No new hook/signature/dependency,
artifact, purchase or config change. Selection completeness still unresolved.

353 CONTRADICTORY DAMAGE EVIDENCE: loaded17:17:39.680, dispatched17:17:43.359,
entered Stage24 at17:18:03.459. Pending71 remains unresolved at17:22:53;
69matches/32wins, clears1-23, attempt2, pausedfalse. Preserve admission to closure.
At17:18:53.512 two attackers had400HP; first636 at17:18:58.645 was followed
34ms later by42HP on both. At17:19:18.688 the next636 coincided with362->4HP
on another unit. This contradicts calling636 an advance warning;604 meaning
remains unknown. Do not reverse meanings without evidence. First retreat moved
one attacker toward GateB while two remained near(-14.4,5); verified anchor
selection is not proof the intended group moved. Reassess phase response and
group selection before the next attempt. No source/runtime change yet.

353 BUILT / DEPLOY INTENT: vmx.bat0errors/two existing NU1601 warnings;
diff-check and27/27 focused tests passed.352 match70 Defeat17:14:44.666 credited
69matches/32wins; closure17:14:48.565, review stop17:14:48.566. It returned to
Bahamut (9080HP at17:14:38) with eight units; no campaign completion. Deploy353
at bound pausedfalse/pending0/sequence70/attempt1; no settings/attempt reset.
Verify actual636->604 retreat, gate arrival, survivor HP and resumed pursuit.

353 INTENT /352 VISUAL EVIDENCE: exact352 startup17:10:22.891, dispatch
17:10:26.604, entered17:10:46.564.63617:11:46.311; paired image/native snapshot
17:11:58.714-729 shows red ground marking beside Bahamut;60417:12:01.461.
Only extra visible Lovm addon was LovmTypeInfo (hidden/restored); header is still
present, but this warning is visible. Screenshot-to-world fit from native stone/
gate projections agrees within0.001pixel; the rough marking bounds do not prove
a precise center/radius. Test a conservative gate retreat during636->604, reusing
Stage19 phase interruption/retreat and holding new attack waves until604 arrives.
Read every validated boss event before bounded-path logging deduplication. No
timer-based resumption if604 is absent; existing observation/result timeouts apply.
This is an experimental tactic, not verified dodge/tower handling; ordinary24
stays blocked. Build353;352 pending70 must resolve before deployment.

352 BUILT / DEPLOY INTENT: vmx.bat0errors/two existing NU1601 warnings;
diff-check passed.351 match69 Defeat17:08:45.186 credited68matches/32wins;
closure17:08:49.066 followed by automatic third-attempt stop17:08:49.082.
Static creation0/actor860callbacks,13paths. Review changed352 capture (real
visible addon enumeration plus later timing), reset only this automatic pause/
attempt count after confirming no later manual stop, and copy352. Preserve all
earned facts/caps/receipts and JSON semantics. Do not classify636 as a warning
without the image. Minion of Light item6987 has ItemAction.Data[0]=67; WHM row69
is separate, so registration/form-selection behavior still requires native proof.

352 INTENT /351 IMAGE: exact351 startup17:03:58.602, dispatch17:04:02.335,
Stage24 entered17:04:22.651. Effect63617:05:17.936, image17:05:20.281,604
17:05:32.996. Image rendered but the boss remained under HUD; LovmStatus did not
hide the actual header. Enumerate up to256loaded addons and temporarily hide
visible Lovm panels during the existing capture, excluding result/ready/confirm;
log actual names and restore through existing cleanup. Defer the single636
image request until10seconds after the event, then existing2second preparation,
to inspect closer to the observed15second damage event. Controls continue while
the delayed request waits. No movement inferred.351 still owns pending69;
build352 and await explicit result/closure before any copy or automatic-limit reset.

351 BUILT /350 RESOLVED: vmx.bat0errors/two existing NU1601 warnings and
diff-check passed.350 match68 Defeat17:03:02.410 credited67matches/32wins;
closure17:03:06.000 and review stop. Faster pursuit delivered1.6second orders;
selected add fell to18HP17:02:45.353 then disappeared, and Bahamut targeting
resumed17:02:47.221 at9717HP. No zero-HP sample, so direct kill evidence remains
limited. Static creation also delivered0callbacks;946actor callbacks/12paths.
Deploy351 at pausedfalse/pending0/sequence68/attempt2 boundary for the first636
image; no reset or spending. Current owned healer is Wind-up Ananta (cost20,
speed2,200HP/250poppet healing); Amalj'aa58 also owned. These are not adopted
strategies. Achievement Minion of Light remains the guide's faster healer option.

351 INTENT:350's first boss effect636 at16:59:16.117 preceded604 and party
losses16:59:31.309; this is correlation, not warning identity. Request one image
on first636 through its existing actor callback, deferred to the battle service
(no capture/reentrant UI mutation in the hook). Use the existing image artifact,
hide/restore LovmStatus for this capture, and pair image with a native snapshot.
Retain the separate single add-phase capture. No filename-driven gameplay.
Build351 while350 owns its current admission; deployment waits for closure.

350 LOADED: startup16:58:06.853, dispatch16:58:10.600, Stage24 entered
16:58:30.753 after copy at pausedfalse/pending0/sequence67/attempt1 boundary.
No configuration changed. Current admission remains unresolved; preserve it.
The official LoVM guide confirms right-click specifies a ground destination,
supporting the observed need to refresh moving-target pursuit rather than
assuming an enemy-lock command. Static-creation and add-kill evidence pending.

350 BUILT: vmx.bat0errors/two existing NU1601 warnings; diff-check and27/27
focused lifecycle/Lifestream tests passed. Copy intent is the reviewed348 result
boundary below, without settings changes. Require exact350 startup before using
new pursuit or static creation evidence; no Stage24 clear has occurred.

350 INTENT /348 RESOLVED:348 match67 Defeat16:54:28.555 credited66matches/
32wins; closure16:54:32.116 followed by deliberate observation-review stop.
Delayed background two-add image16:52:40.589 is readable; no warning identity
established. All sampled scenes still24VFX/0decals. Add HP1110 remained unchanged
16:53:45-16:54:23; snapshots show army trailing x4-12 while add moves x12-17.
Commands land at the enemy's ground position but repeating full selection every
five seconds loses pursuit. Reuse Stage12's existing1.5second pursuit decision,
verified-group reuse and0.25second readback on24, preserving parties in melee.
Retain349's creation-event probe and UI correction (349 built0errors/two NU1601
warnings and diff-check passed, never copied). Build350, deploy only at bound
pending0/pausedfalse/sequence67/attempt1 closed state; no attempt reset planned.

349 INTENT:348 remains in its admitted observation. Previous completed probes
delivered zero StaticVfxRun callbacks; replace that subscription with the existing
StaticVfxCreate event, retaining the same window/64path/owner/thread bounds and
cleanup. Report path/source only; constructor transforms cannot prove placement.
No extra hook, dependency, retained native pointer or movement assumption. Correct
Stage23's stale prerequisite text to its observed clear (special effect still
unverified). Build349; do not copy while348's result is unresolved.

348 LIVE: automatic third-attempt reset/copy succeeded with all other JSON
semantics unchanged. Exact startup16:49:24.198, dispatch16:49:27.991, entered
Stage24 at16:49:47.700. Native camera read6 initially, then10.5 at16:49:59.787
after six background wheel events. Opening scene439objects/24effects/0decals;
no truncation. Current match67 unresolved; await delayed two-add image/result.

348 BUILT / DEPLOY INTENT: vmx.bat0errors/two existing NU1601 warnings and
diff-check passed.347 was not copied. Reset only the reviewed automatic attempt
pause/counter after verifying346's closure and no later user stop; preserve all
other JSON semantics and confirm raw account text is unchanged before writing.
Copy348 to R:\parasite\vmx\VERMAXION.dll, require exact348 startup and inspect
camera distance/declared decals/delayed image. No certificate or vendor action.

348 INTENT /346 RESOLVED:346 match66 explicit Defeat16:47:11.101, credited
65matches/32wins; closure16:47:14.913, automatic third-attempt stop16:47:14.920.
347 built0errors/two existing NU1601 warnings; diff-check passed, not deployed.
Native source confirms scene Decal has texture/transform/visibility fields; the
current scan excluded this ground-marking type. Include decals in the existing
512object/64effect snapshot bound, no extra hook or sink. Retain347's camera
and delayed background image. Build348, then review/reset only the automatic
attempt limit at the verified closed boundary for this changed diagnostic.
Do not override manual FULL STOP or change earned facts/caps/receipts.

347 INTENT:346's PrintWindow image16:45:02.455 rendered the game at624x441
without focus, but panels and narrow camera framing obscure the battlefield.
The two-add snapshot fired immediately without hiding panels. Reuse existing
boss camera zoom (six native wheel events with distance readback) for Stage24;
prepare/hide panels and wait two seconds before its single image, restoring
panels afterward. No mechanic IDs, new artifact, signature or movement rule.
Build347 with vmx.bat; preserve346's pending Stage24 result. A further attempt
requires review of its expected automatic third-attempt stop, never FULL STOP.

346 LOADED: vmx.bat succeeded0errors/two existing NU1601 warnings; diff-check
and27/27 focused lifecycle/Lifestream tests passed.344 match65 explicitly Defeat
16:35:53.028, credited64matches/32wins; normal result closure16:35:56.555 and
observation-review stop. Both adds remained alive (798/2280 and2124/2280);
Bahamut10900/20000HP. Hooks released:0static/850actor callbacks,13paths.
Verified bound pausedfalse/pending0/stage24/attempt2/sequence65 before copying346;
no config edits, attempt resets, vendor action or new tactics. The pre-copy
checkpoint insertion failed on line endings; this entry reconciles that gap.
Exact346 debug dispatch16:41:55.915, entered Stage24 at16:42:16.441; current
result unresolved. Actor callbacks arrive on framework thread. Preserve current
admission through closure; inspect one-shot background two-add image when ready.
Observed clears remain1-23; achievement alternatives remain researched options.

346 INTENT:345 built0errors/two existing NU1601 warnings and diff-check passed,
uncopied.344 reached the two-add phase16:34:39.966; initial Twintania HP2280 each,
and existing targeting ordered one add from16:34:45.694. At16:35:12 boss10900HP,
adds2202/2124HP; one-add kill/return not yet verified. Actor paths636/604 attach
to Bahamut, but their mechanic meanings are unverified. Add one two-add phase
image via existing verminion-control.bmp. In background use PrintWindow CLIENTONLY
|RENDERFULLCONTENT on this process's window; never activate it or capture another
window's pixels. Rendering may be blank and must be inspected before any claim.
This uses the existing capture/artifact path, with no new tool or output file.
Build346; preserve344 match65 until explicit result/closure. No new tactics yet.

344 ACTOR CALLBACK PROOF /345 INTENT: exact344 startup16:31:16.963, dispatch
16:31:20.638; match65 admitted16:31:33.454, entered16:31:40.822. Both existing
ECommons hooks initialized16:31:45.855-857. First actor callback16:32:01.436
frameworkThread=True. First paths comp_popf,chr_pop1t,lovm/eff/012,lovm/eff/016
targeted minion Base472 at observed gate positions. Returned actor-VFX pointer
interpreted as ECommons.VfxStruct had NaN scale/invalid transform; do NOT use
that layout for actor effects.345 removes those raw transform reads and reports
only resolved object-table caster/target IDs and positions. Static scene VfxObject
layout remains verified for arena effects. Build345; keep344's match65 running
through its six-minute observation/result. Warning identities still unknown.

342 MATCH64 RESOLVED /344 COPIED: explicit Defeat16:29:15.601 credited
63matches/32wins; closure16:29:19.265 followed by observation-review stop. No
runningEffect rows observed during342's120second probe despite hook initialization.
Verified pausedfalse/pending0/stage24/attempt1/sequence64 and latest automatic
result-closure stop, then copied344 without config changes. Next: verify344's
exact marker, callback delivery/thread, and later-phase evidence within360s.

344 BUILT: vmx.bat0errors/two existing NU1601 warnings;27/27 focused tests and
diff-check passed.344 remains uncopied. Its six-minute window and actor/static
event readbacks are unverified;342 still owns match64.343 was built but never
deployed. Preserve64 through its result and normal closure before344.

344 INTENT:343 built0errors/two existing NU1601 warnings, uncopied. All120second
control probes stopped above half Bahamut health and never reached a verified
add/tower phase. Increase ONLY this debug observation's control window to360s
(still within the existing600s result timeout), using one constant for service
and effect observer and updating the existing UI. This avoids concluding warning
reads are unavailable solely from early-phase sampling. Retain343's static/actor
delivery diagnostics and all ordinary-admission gates/accounting rules. Build344;
342 still owns unresolved match64, so no deployment until its explicit closure.

343 INTENT:342 exact startup16:24:10.074, dispatch16:24:13.755, match64
admitted16:24:26.622/entered16:24:34.153. Static VFX hook initialized16:24:39.188,
but no runningEffect rows through16:25:57. Distinguish absent native callbacks
from thread filtering; add bounded first-callback/thread/count evidence, and
include ECommons' existing ActorVfxCreateEvent for attached effects. Keep native
reads on the recorded framework thread,64path/120second bounds and unsubscribe/
disable both owned hooks on every StopOwnedMovement path. No custom signature,
dependency, sidecar or gameplay tactic. Build343; preserve342 match64 until
its explicit result/closure. No additional attempt reset planned for this copy.

341 THIRD-PROBE RESULT /342 COPIED: explicit Defeat16:23:29.200 credited
62matches/32wins; closure16:23:32.702; automatic third-attempt stop16:23:32.718.
Reviewed this diagnostic failure and selected342's changed event-based read.
At16:24 verified bound pausedtrue/pending0/stage24/attempt3/sequence63/mask8388607
and the latest operation was that explicit result-closure automatic stop. Cleared
ONLY VerminionPaused and CampaignStageAttempts, with JSON semantic equality for
all other data and raw-file identity checked before writing, then copied342.
No manual FULL STOP overridden, earned facts/caps/receipts unchanged. Await exact
342 startup and effect events.340/341 scene snapshots exposed only24permanent
bg arena effects; no AreaObject or cast warning identities established.

342 BUILT: vmx.bat0errors/two existing NU1601 warnings;27/27 focused tests and
diff-check passed.342 remains uncopied pending341 match63. The existing event
probe uses only framework-thread native reads and64distinct paths; runtime
delivery and warning identity are still unverified. No additional vendor work.

342 INTENT:341 exact startup16:14:19.568/dispatch16:14:23.206; Stage24 match63
admitted16:14:36.098, entered16:14:43.212. AreaObject/cast diagnostics still
empty; scene traversal succeeded439-445objects/24effects/unboundedFalse, but
only permanent bg arena effects appeared through16:16:18.379. Sampling may miss
short-lived warnings, or their effects may not share World.ChildObject.
Use the already referenced ECommons StaticVfxRunEvent, backed by declared SDK
VfxObject.Update, to read first occurrence of at most64effect paths during the
existing120second window. Framework-thread/duty/owner checks; no effect writes,
new dependency, custom signature or new log sink. Unsubscribe and disable this
plugin's unused run hook at StopOwnedMovement. Build342; DO NOT deploy during
unresolved341 match63. A subsequent diagnostic attempt requires review of the
automatic third-attempt stop; never clear manual FULL STOP.

339 RESULT CLEANUP VERIFIED: match62 explicit Defeat16:12:25.804 credited once
as participation61matches/32wins, result closure16:12:29.349 and deliberate
observation-review stop16:12:29.350. Saved pausedfalse/pending0/stage24/attempt2;
no victory or Stage24 clear. The120second release preserved this result admission
until resolution. Deploy built341 at this resolved stop for the third bounded
probe, now with AreaObject/native-state/scene-effect diagnostics. No attempt reset
or cap mutation; third failure retains the existing automatic limit.

341 BUILT: vmx.bat succeeded0errors/two existing NU1601 warnings; diff-check
passed. No tactics changed after339's27/27 focused tests.341 remains uncopied.
339 verified control release at16:05:34.845 while retaining pending62; last
known boss14540/20000HP. Result still pending as of16:10; live log is advancing,
so silence alone is not a result. Preserve the admission until outcome/timeout.

341 DIAGNOSTIC INTENT:340 built0errors/two existing NU1601 warnings and remains
uncopied. Add scene VFX paths/positions/scales to the same Stage24 snapshots,
using declared ClientStructs World/Object/VfxObject/resource members, bounded to
512objects/64effects and local cycle detection. Ground warnings need not be
ObjectTable entries or battle casts. No hook, signature, dependency, new log
sink, scene mutation or mechanic-ID assumption. Build341; await339 match62's
explicit outcome/closure before deployment. At16:07:20 pending62/attempt2,
pausedfalse and60matches/32wins; controls released16:05:34.845.

340 DIAGNOSTIC INTENT: the first120second Stage24 probe captured no casts or
new tower EventObj entries. Existing snapshots excluded AreaObject and object
state. Include AreaObject on Stage24 and native GameObject.EventState/instanceID
in the existing bounded field snapshot. No inferred mechanic IDs or control
changes. Build340; keep339's current second Stage24 probe uninterrupted through
its explicit result. Do not deploy merely to collect a mid-battle sample.

339 LIVE RECONCILIATION: exact339 dispatch16:03:14.692. Startup snapshot was
already territory388/playingfalse/currentDuty0, not the orphaned native battle.
Consequently normal debug dispatch started a SECOND Stage24 probe (entered
16:03:34.810). This was not the intended passive recovery; do not describe it as
that proof. Old match61 remains unresolved/uncredited; counters stayed60/32 and
mask8388607.339 now retains the second probe's admission for explicit-result
closure, then stops on defeat. Preserve the current battle. No config mutation.

339 COPIED FOR RECOVERY: vmx.bat0errors/two existing NU1601 warnings;27/27
focused tests and diff-check passed. Verified latest operation is335's automatic
Stage24 cutoff, saved pausedfalse/pending0/stage24/attempt1/sequence61, then copied
339 to the authorized DLL path. No config mutation, new admission or credit.
Verify exact339 startup and passive result closure before another attempt.
Stage23 special dispatched15:57:25.221 at3168/18000HP; one-second readback still
showed8Gentlemen and no ATK/DEF buff, so dispatch is not verified ability effect.

339 RECOVERY INTENT:335's Stage24 observation ended16:00:06.911 and abandoned
match61 without a result. The native battle is unresolved.338 built0errors/
two existing NU1601 warnings;27/27 focused tests passed; it remains uncopied.
Correct the existing resume path: an in-progress battle with no matching saved
admission receives no commands or fabricated admission. Observe only its explicit
result, close it, then fail uncredited without a new dispatch. This is recovery
of335's orphaned observation, not a new strategy attempt. After build/tests and
fresh pausedfalse/pending0/automatic-cutoff evidence, reload339 for that recovery;
never clear a manual FULL STOP. Keep all earned counters and purchase state.

338 INTENT: update Stage20/23 guidance to their observed fresh clears. Retain
337's Stage24 passive-result cleanup correction. Build with vmx.bat; do not
replace335 during the unresolved Stage24 match61 merely to deploy this candidate.
No vendor changes, certificate claims or spending. Versions unchanged.

335 STAGE23 PASSED: first-attempt Victory15:57:42.862, credited60matches/32wins;
result closure15:57:46.711. Fresh clears1-23. The guarded337 copy missed its
two-second safe window and did not mutate the runtime. Stage24 match61 is now
admitted; preserve it.335 still has the old120second observation cutoff;337's
passive-result fix remains uncopied. Stage24 outcome/mechanics unresolved.

335 STAGES21/22 PASSED: first-attempt Victories15:51:48.244 and15:54:44.426,
credited58matches/30wins then59matches/31wins. Result closures15:51:51.830
and15:54:48.129. Fresh clears1-22. Stage23 match60 admitted15:55:01.183;
at15:56:25.229 eight Gentlemen deployed, Twintania14400/18000HP. Preserve
this unresolved battle.337 remains built/tested and uncopied; its Stage24
passive-result cleanup fix is not loaded. No achievement claim or purchase.

335 STAGE20 PASSED: first fresh mixed-opening Victory15:48:52.236; credited
57matches/29wins; result closure15:48:55.887. Four Wayward defenders and their
special observed from the new admission, addressing the earlier mid-battle-load
verification gap. Stage21 match58 admitted15:49:08.932. Fresh clears1-20.
One guarded copy check found21 already admitted and correctly retained335.
337 remains built/tested27/27 and uncopied. Copy only if fresh closure evidence,
saved pending0 and unchanged run binding agree; otherwise keep the current fight.

ACHIEVEMENT MENU RESEARCH: XIVAPI's game text sheet
custom/001/CmnDefAchievementReward_00107 contains "Exchange achievement
certificate." (row5), plus introductory certificate-award dialogue (rows10-13).
This establishes localized text candidates, not the actual claim trigger/menu
state. ADS's shop runner currently advances selection menus, not these Talk
pages. Do not infer callbacks or successful token claims from sheet text.
337 focused tests passed27/27. No ADS edits or runtime vendor action this context.

335 STAGE19 PASSED: first-attempt Victory15:45:15.573, credited56matches/28wins;
result closure15:45:19.238. Stage20 match57 admitted15:45:32.097. Fresh clears1-19.
Airship bunker retained center1050HP while killing Gilgamesh, after the observed
buff retreat and return.337 build passed0errors/two existing NU1601 warnings;
focused tests running.337 remains uncopied because20 is already admitted.

337 INTENT: code review found Stage24's120second diagnostic cutoff abandoned
its pending admission while leaving the native fight unresolved, preventing a
clean next reload. After120seconds release owned controls but retain admission
for passive explicit-result observation within the existing600second timeout.
A verified defeat closes the result and stops for mechanics review, without
automatically repeating this development observation. FULL STOP/unknown result
still abandon without success. No new persistent setting or retry mechanism.
Build/test337; deploy only at a reconciled safe boundary.335 still owns Stage19
match56; at15:43:59.731 boss3056/16000HP/center1050HP. Do not reload this battle.

335 STAGE18 PASSED: explicit first-attempt Victory15:37:02.270, credited
55matches/27wins. Stage19 match56 admitted15:37:18.683. Fresh clears1-18.
Keep335 through the Airship bunker test;336 is built but not deployed.

335 STAGE17 PASSED: explicit Victory15:34:04.901 on first attempt, credited
54matches/26wins. Stage18 match55 admitted15:34:21.648. Fresh clears1-17.
336 guidance build passed0errors/two existing NU1601 warnings and is not copied.
335 continues uninterrupted; next new control evidence is18/19 results, then
the Gentleman23 and bounded Bahamut24 observation. No additional vendor action.

336 INTENT: guidance-only update to record335's first-attempt Stage16 clear.
Same battle behavior. Build336 but keep335's Stage17 match54 uninterrupted;
do not deploy a cosmetic evidence update over an unresolved battle.

335 STAGE16 PASSED: first mixed-roster attempt produced explicit Victory
15:31:09.182, credited53matches/25wins. Mammet Arcana attackers with4Zu defenders
cleared; the preceding Zu-only run lost3times. Stage17 match54 admitted
15:31:25.560. Fresh clears1-16. Keep335 running. Update guidance to this fresh
result in the next source batch; one clear does not establish repeatability.

335 STAGE16 OPENING: match53 verified6Mammets at GateB15:28:33.832, four Zu
defender requests15:28:40-43 and interception15:28:56.269. Nasty Peck dispatched
15:29:15.644. Enemy center stone reached0HP in15:29:55.792 snapshot while our
center remained4935HP; final outcome still pending. Do not reload this battle.
Achievement review cross-checked Companion stats through XIVAPI. Actual Jonathas
shop1769813 encodes certificate Item21172 literally withCostType0/UseCurrencyType4;
ADS therefore selects its Item balance path, not its virtual18 mapping. Installed
ClientStructs documents21172 inCurrencyManager.ItemBucket. Need a native balance
comparison before changing that path; no automatic claim/menu callbacks inferred.

335 LOADED:27/27 focused Verminion/Lifestream tests passed. Cleared only the
resolved automatic pause and Stage16 attempt count, verified JSON semantic
identity otherwise, then copied335. Exact startup15:27:53.795 and dispatch
15:27:57.580. Stage16 match53 admitted15:28:05.466, entered15:28:12.608.
Observe this mixed composition before further changes. No achievement purchase.

335 BUILT: vmx.bat passed0errors/two existing NU1601 warnings. Focused tests
running.330 match52 lost15:25:49.906, left result15:25:53.484 and automatically
stopped after3attempts15:26:03.604. Saved pausedtrue/pending0, matches52/wins24,
mask32767/stage16/attempts3. No later manual FULL STOP in the bounded snapshot.
After tests pass, fresh335 test may clear only this automatic pause/attempts;
preserve earned facts, spending, receipts and settings. Require exact335 marker.

335 INTENT:334 build passed0errors/two existing NU1601 warnings;26/26 focused
tests passed, not deployed. Stage16's Zu-only first two attempts lost, with
third match52 still pending at the last observation. Guide16 explicitly calls
for Arcana-strength attackers (Kidragora or any Mammet), plus optional Odin
support. Zu counters critters but lacks Arcana strength. Reuse existing mixed
roster behavior:6Mammets/lane,4Zu defenders,40capacity reserve, the same guarded
summons/interception/specials used on6/20. Both minions already acquired; no
vendor work or new configuration. One focused regression covers requirements
and attack/defense capacity. Build/test335; resolve52 before any deployment.

334 INTENT: add the reviewed achievement-vendor guide alternatives to existing
stage previews, with Odin/Cursor ownership and explicit ADS claim/purchase gaps.
Update Stage15 guidance to its one clear in two attempts, distinguishing333's
undeployed replacement change. No new configuration, roster requirement or
purchase. Build334 through vmx.bat; leave330's unresolved Stage16 match52
(third attempt, admitted15:21:40.369) uninterrupted. Match51 lost15:21:23.892.

ACHIEVEMENT VENDOR REVIEW (2026-09-27): user explicitly requires considering
Jonathas minions and readily claimable certificates. All NPC interaction,
certificate claims, shop travel, buying and cleanup belong exclusively in ADS.
Review https://ffxiv.consolegameswiki.com/wiki/Jonathas and ffxiverminion minion
pages36/49/51/54/68/69/70/72/73/74/76/77/84/85/167/288. All12 listed minion items
cost2certificates; Minion of Light and Wind-up Leader have multiple forms.
Guide-backed shortlist: Odin76 (Monster,cost30,speed4) supports16/22 and appears
in19; Cursor51 (Gadget,cost15) has50%DEF down15s and the23 alternate uses4Cursor+
6Gentlemen; WHM Minion of Light69 (Poppet,cost10,speed3) heals100HP/150for poppets
and is an explicitly harder healer alternative for24. Do not infer Verminion
form control from overworld random/Grand Company summon behavior.
Other reviewed roles: Beady Eye36 cheap230potency AoE special; Warrior of Light77
230potency AoE special/highDEF; Nanamo84 AoE autos/low-HP selfbuff; Gilgamesh85
AoE autos/300potency withdrawing special; Firion167 highHP/ATK,lowDEF; Goblin49
enemy AP drain; Black Chocobo Chick54 delayed trap; Komainu288 trap disarm;
leaders72/73/74 provide summon-speed/cleanse/slow utility. These are candidates,
not verified substitutions or new required purchases. Prefer registered/inventory
minions, buy only a selected requirement and never infer certificate balance.
ADS source already maps virtual currency18toItem21172, discovers CustomTalk
shop links and has guarded purchase receipts. No certificate-claim implementation
found. Jonathas NPC1008145/CustomTalk721003 references shops1769813/1769845/
1769846/1770948; actual menu path, claim action, balance and receipt need runtime
verification. No achievement vendor action/spend occurred. No new cap/config.

330 STAGE15 PASSED: explicit Victory15:12:57.322 on match49, its second attempt;
credited49matches/24wins/mask32767. Fresh clears1-15. Stage16 match50 lost
15:17:10.422; match51 admitted15:17:27.200, second attempt and unresolved.
333 build passed and26/26 focused Verminion/Lifestream tests passed.333 remains
undeployed; its Stage15 replacement correction is not part of the observed clear.
Preserve330's current bounded run and pending result before any reload.

333 INTENT: 330 Stage15 match48 lost15:08:37.500 with Odin389/17000HP. Healing
regroup completed15:07:15.636 and special dispatched at4203HP15:08:15.964, but
the final-phase snapshot shows a replacement still at the gate. Stage15 omitted
the existing prompt-replacement path (otherwise four units or60seconds). Include
15 alongside12/23/24 in that path and preserve the main army's pursuit timestamp
when replacements depart. Keep initial eight-unit assembly/final movement guards.
Extend the existing reinforcement regression with15, and reuse special-effect
readback for15/23 (dispatch alone is not buff/withdrawal evidence). Build/test333;
330 retains match49, second attempt, entered15:09:01.363. No config or copy now.

330 STAGE14 PASSED: first-attempt Victory15:03:52.863, credited47matches/23wins.
Stage15 match48 admitted15:04:09.666, entered15:04:16.878. Fresh clears1-14.
Keep330 uninterrupted for the previously unverified Gentleman/Odin substitution.

330 STAGE13 PASSED: first-attempt Victory15:00:55.687, credited46matches/22wins.
Stage14 match47 admitted15:01:12.379, entered15:01:19.888. Fresh clears1-13.
332 build passed zero errors/two existing NU1601 warnings and remains undeployed;
only guidance/diagnostic changes beyond the working330 battle behavior. Continue.

330 STAGE12 PASSED /332 BUILD INTENT: positive Victory14:58:00.555, credited once
as45matches/21wins/mask4095. Final330 tactics cleared on their third attempt after
two defeats, not a repeatability pass. Stage13 match46 admitted14:58:17.104,
entered14:58:24.661. Keep330 running uninterrupted.331's radius/facing diagnostic
was built but never copied.332 retains that small diagnostic addition and updates
Stage11/12 guidance to the observed clears. Build only; do not replace the working
campaign merely for guidance/diagnostic changes. No runtime config mutation.

331 DIAGNOSTIC INTENT: 330 match43 lost14:49:34.684 at boss664/12000HP, all eight
Gentlemen alive, roughly2.2-4.2yalms behind the boss at the final snapshot. The
current fixed two-yalm melee suppression may not match this boss's actual size.
Add native HitboxRadius and Rotation to the existing bounded minion snapshot
line; do not infer a new attack threshold yet. Build331 (same330 tactics), then
deploy only after a reconciled safe boundary.330's second match44 was already
starting; preserve its result and bounded attempts. No added sink or file.

330 LOADED: exact330 dispatch14:45:36.141; match43 admitted14:45:43.908. Eight
Gentlemen deployed by14:46:57.298; boss11480/12000HP. Named boss target and camera
distance10.5 retained. Rear-geometry correction needs an actual result; keep the
bounded run uninterrupted. No new purchase, configuration or source candidate.

330 DEPLOY INTENT: 329 match42 lost14:44:04.434; automatic three-attempt stop
14:44:18.122. Saved pausedtrue/pending0, stage12/attempts3, matches42/wins20,
mask2047. No later manual FULL STOP observed.329 verified named-boss opening and
camera10.5, but no clear (match40 got to508HP; match42 last read2328HP). Deploy
the built/tested330 rear-geometry correction after clearing only this automatic
pause and stage attempt count. Preserve all other settings and earned facts.

330 BUILT: vmx.bat passed zero errors/two existing NU1601 warnings;25/25 focused
tests pass, including stable rear arrival across four orientations. 329 match40
lost14:35:40.199; match41 admitted14:35:56.670, entered14:36:03.908. Prefer the
next positively resolved post-match gap for330, if fresh saved pending0 and
out-of-duty transition agree before another admission. Preserve attempt count
and all other config in that case; otherwise wait for the automatic stop. No
reload of unresolved41 and no polling loop. Expected result milestone ~14:39:55.

330 INTENT: 329 is live in Stage12 match40. Opening wave targets Demon Brick;
native camera distance10.5 observed14:32:15.044. Boss6436/12000HP14:34:12.656.
Source review found contradictory rear geometry: target is -0.8 along facing,
but any anchor closer than -1 was sent on a 2-by-2 detour. A correctly positioned
unit therefore left melee again. Detour only from the front half-plane; retain
the rear target. Add one focused four-orientation/both-sides/stable-arrival test.
Build/test330 only; preserve329's pending result and its bounded run. Do not
deploy into an unresolved battle or claim either strategy has cleared yet.

329 DEPLOY INTENT: 328 match39 lost14:30:40.890; automatic three-attempt stop
14:30:54.570. Saved pausedtrue/pending0, stage12/attempts3, matches39/wins20,
mask2047. No later manual FULL STOP observed. Final329 rebuild passed zero
errors/two existing NU1601 warnings; 24/24 focused tests passed before its
camera-log filter correction. Clear only automatic pause and stage attempt count,
then copy329. Verify named boss opening, camera distance/selection and explicit
outcome. Preserve all earned facts, budgets, receipts and other settings.

329 VALIDATION: vmx.bat passed zero errors/two existing NU1601 warnings and
24/24 focused tests passed. Extend the existing camera snapshot's duty filter to
563 as well as570 so Stage12 zoom has native distance readback, then rebuild329.
328 still owns its second attempt (match38, entered14:22:38.372); no deployment
or saved progress edits while that bounded run remains active.

329 INTENT: 328 match37 first wave targeted Wind-up Dullahan14:18:52.696, while
Demon Brick still exposed390HP versus the add's445HP. The guide explicitly
ignores adds. Select the named Demon Brick on Stage12, waiting if absent instead
of attacking an add. A native image14:20:01 also shows the boss filling most of
the small viewport; reuse Stage19's bounded/idempotent camera zoom for Stage12
through the same existing native input. Retain328's verified press/release and
selection checks. Build/test329 only;328 keeps its bounded attempts until their
terminal result. No new roster, purchases, configuration or control mechanism.

328 LOADED: exact328 marker consumed/dispatched14:18:16.230/.463. New Stage12
match37 positively queued14:18:24.215 and entered14:18:31.614. Native preparation
summons now observed250ms apart. Pursuit cadence/contact and final result still
need observation; keep this bounded run uninterrupted. Automatic reload cleanup
logged FULL STOP before its normal owned dispatch; this is not a manual stop.

328 DEPLOY INTENT: 327 match36 lost14:17:02.127; explicit automatic three-attempt
stop14:17:15.779. Saved pausedtrue/pending0, stage12/attempts3, matches36/wins20,
mask2047. No later manual FULL STOP observed. Rebuilt328 passed zero errors/two
existing NU1601 warnings; the cadence/strategy tests passed24/24 before the
diagnostic-only correction. Clear only automatic pause and this stage attempt
count, then copy the rebuilt artifact to R:\parasite\vmx\VERMAXION.dll. Preserve
all earned facts, caps, spending, receipts and other settings. Verify328 startup,
new admission, actual pursuit cadence and explicit outcome before more changes.

328 REBUILD INTENT: first vmx.bat build passed with zero errors/two existing
NU1601 warnings; 24/24 focused tests passed. Before deployment, also consume the
one-time Stage 12 contact observation when an unfocused screenshot is unavailable,
preventing the shortened cadence from repeating this diagnostic. No battle
decision change. Rebuild the same not-yet-deployed marker. 327 remains live;
match35 lost and match36 (third attempt) was admitted14:13:11.345. Saved counters
35matches/20wins, mask2047, pending36. Reconcile its terminal result before copy.

328 INTENT:327 loaded14:04:43.974 and match34 entered normally. Isolated-group
return dispatched14:06:24.634; position readback14:07:40 confirms all8Gentlemen
near boss. But movement decisions remain about5-6seconds apart and units trail
3-5yalms; match34 lost14:08:44.813, matches34/wins20. Match35 already admitted.
Stage12-only timing correction:250ms decision/post-move checks,1.5second moving
target refresh, retaining native frame completion/press release and queue guards.
Build/test328 only.327 keeps its existing bounded attempts until terminal state;
then a fresh run can verify actual cadence, contact and result. No new machinery,
minion, purchase or configuration field.

327 DEPLOY INTENT:326 match33 Defeat14:03:29.283; automatic three-attempt stop
14:03:43.463. Saved pausedtrue/pending0, stage12/attempts3, matches33/wins20,
mask2047. No later manual FULL STOP observed. Fresh test of the source-tested
isolated-unit correction: clear only automatic pause and stage attempt count,
then copy327. Preserve all earned facts/caps/spending/receipts. Require exact
startup, fresh admission, observed isolated-group rejoin and explicit result.

327 BUILT, NOT DEPLOYED: vmx.bat passed0errors/two existing NU1601 warnings;
24/24 focused Verminion/Lifestream tests pass, including isolated-group return
and no intervention while the whole army is travelling.326 remains live.
Keep its pending result/attempt accounting intact before a fresh327 run.

327 INTENT:326 match31 lost13:55:02 with boss1808/12000HP and8Gentlemen alive;
one remained far behind. Match32 snapshot13:57:53.429 shows4Gentlemen at boss
(16,0,7) and4 isolated near(-2,0,-8),(-1,0,-9),(3,0,-2),(2,0,-4). A dispatched
wave does not prove all units arrived. Add Stage12-only isolated-group return
through existing verified selection/movement, at most once per15second wave
interval and only with an observed group near the boss. No new minion, purchase,
UI control or persistent setting. Add focused separation/transit regression;
build/test327 but retain326 until its current bounded attempts end.

326 LOADED: exact startup13:51:00.408; observed owned Gentleman21 and its
palette slot, normal character-bound dispatch13:51:04.313. Fresh Stage12 match31
positively queued13:51:12.051. Config edit was checked to change only pausedtrue
tofalse and Stage12 attempt count3to0. Totals30/20, mask2047, spending2400gil/
10000MGP and caps223588gil/40000MGP all preserved. Observe this fresh strategy.

326 DEPLOY INTENT:323 Stage12 match30 lost13:48:48.914; automatic three-attempt
stop13:49:02.681. Saved pausedtrue, pending0, stage12/attempts3, clears2047,
matches30/wins20. No subsequent manual FULL STOP observed. The reviewed and
tested Gentleman pursuit now gets one fresh bounded development run: clear only
this automatic paused flag and CampaignStageAttempts, preserve every earned
fact, receipt, budget and setting, then copy326 to R:\parasite\vmx\VERMAXION.dll.
Require exact326 startup, palette/owned-minion admission and actual Stage12 result.

326 BUILT: vmx.bat passed0errors/two existing NU1601 warnings. Stage12 now uses
Gentleman boss pursuit instead of interception; preserves rear movement and the
three-second contact refresh, promptly deploys single replacements and never
withdraws an action party.23/23 focused Verminion/Lifestream tests pass, including
Stage12 cost/capacity, movement and special guards.326 is not deployed.323 still
owns Stage12 match29 (second attempt); latest boss health11750/12000 at13:42:49.
Wait for the existing bounded run's terminal state before any copy/config edit.

326 INTENT:323 Stage12 match28 lost13:38:45.665 with Demon Brick11238/12000HP,
matches28/wins20. Match29 is already running under the existing bounded retry.
Use the published Gentleman-only Stage12 strategy: rush/follow boss from behind,
ignore adds, send replacements promptly; disable withdrawing special. Sources:
https://na.finalfantasyxiv.com/lodestone/character/28572768/blog/4629440/ and
https://ffxiverminion.com/stage12-breakout. The current character owns Gentleman.
Reuse existing pursuit/queue/control behavior; no purchases, quest dispatch or
config changes. Build/test326 only, keep323 live until a reconciled stop before
fresh testing.326 supersedes325 and includes its guidance corrections.

323 STAGES10/11 PASSED: positive credited victories13:29:12.366 and13:32:08.420,
both first attempts. Matches27/wins20. Stage12 match28 positively queued
13:32:24.754, entered13:32:32.131. Fresh campaign clears1-11; keep323 running.
Earlier devhub181.md now contains DDuck I419 mini stock controls; current clean
DDuck source/changelog already implement clickable Phoenix/grip icons/counts and
editable targets. Read-only reconciliation only; no DDuck edits/build/client use.

323 STAGE9 PASSED: positive Victory13:26:16.676, credited once; matches25/wins18.
Fresh clears1-9 now observed. Bomb pickup/status, placement, detonation and final
slime attacks completed in the uninterrupted run. Continue10 onward.325 source
has only guidance/comment changes beyond the tested323 behavior and is not live.

325 SOURCE NOTE: after its successful build, stage guidance's generic wording
now distinguishes character clears from repeat-win reliability, and one stale
Stage9 comment says "attacker" instead of the old minion name. Rebuild before
eventual deployment.323 still runs unchanged. Stage8's exact positive result was
13:22:01.594; Stage9 has observed carrier pickup, placement and trap detonation.

325 BUILT /323 CONTINUES:325 build passed0errors/two existing NU1601 warnings;
22/22 focused tests pass after removing the untested mixed-roster experiment.
Do not copy for guidance-only changes during the ongoing323 campaign.
Stage8 advanced to9 with matches24/wins17; Stage9 match25 positively queued
13:22:18.106 and entered13:22:25.327. Bomb lever movement observed13:22:46.581.
No additional vendor request or configuration mutation.

325 SOURCE /324 SUPERSEDED: retain the roster that actually cleared Stage7,
documenting its one win/two losses. Remove only324's not-deployed mixed-roster
experiment and its new test, avoiding an untested replacement in the deliverable.
323 stays live and continues the campaign. Rebuild325 before any later copy;
the currently built324 DLL must not be deployed. No progress or config mutation.

323 STAGE7 PASSED WITH LOSSES: match23 Victory13:19:04.101, credited once;
matches23/wins16/clear mask127, pending0 at13:19:07. Earlier two Stage7 attempts
were defeats. The Zu-only roster has one live clear in three attempts, not a
reliability pass. Keep323 running into8.324 is built but NOT COPIED; its mixed
Mammet/Zu Stage7 remains untested. Do not reset any earned challenge or counters
to force a replay. Resolve the untested source change before a later deployment.

324 BUILT: vmx.bat passed0errors/two existing NU1601 warnings; focused tests23/23.
323 remains live. Stage7 match22 also lost13:14:36.841 (matches22/wins15);
match23, the third allowed attempt, entered13:15:00.602. Wait for a reconciled
terminal result and the automatic three-attempt stop before copy324. If that
specific limit is reached, the corrected development attempt may clear only its
paused flag and Stage7 attempt count; preserve receipts, caps, earned counters,
clear mask and all other settings. A manual FULL STOP must remain respected.

324 BUILD ONLY:23/23 focused Verminion/Lifestream tests passed, including mixed
Stage7 ownership and simultaneous attacker/defender capacity reservations. Build
through vmx.bat without copying; that script only builds local outputs. Keep323
live until its existing bounded attempts reconcile.324 also includes Stage6's
verified strategy guidance. No release version, purchase cap or spending changes.

324 SOURCE INTENT:323 Stage7 match21 lost13:10:21.301, correctly credited only
participation (matches21/wins15). It destroyed side stones but left center B
at1725HP near the end. Existing bounded campaign retry already queued match22.
Prepare mixed Stage7: six Mammets per lane plus four Zu defenders using the
existing Stage20 defense/capacity path. Both minions already owned. Keep16's
unobserved composition unchanged. Run test-only compilation while323 continues;
do not build/copy VMX until its current admissions reconcile and run stops.

323 STAGE6 PASSED: positive Victory13:06:14.706, credited once; matches20,
wins15. Airship attackers and Zu defenders cleared the Imp phases. Automatic
transition positively queued Stage7 match21 at13:06:31.553; battlefield13:06:38.768.
Keep323 uninterrupted. Stage7/16 Zu compositions and Stage15 Gentleman remain
unverified; update public guidance as each observed result becomes available.

323 OBSERVED: exact startup13:00:37.753 on R7. Lifestream readiness returned
aetheryte63 at13:00:41.660; arrived Minion Square and read progress13:00:44.681.
Stage6 positively queued as match20 at13:00:58.608, battlefield13:01:06.056.
Zu already registered; no vendor request or repeat purchase. Keep this campaign
attempt uninterrupted and inspect its result before another deployment.
Saved account selection uses VERMAXION.json LastAccountId plus DebugTaskCharacterKey;
other account files may contain the same character key and are not the active record.

323 DEPLOY: build passed, saved character unchanged/unpaused with no pending
admission or purchase/acquisition. Copy323 after Verminion/Lifestream tests pass.
Require its startup marker and Lifestream readiness/arrival evidence; do not
repeat vendor work. ADS19 remains loaded.

323 INTENT:322 loaded12:52:14.388. Navmesh accepted the four-waypoint approach
from(-46.55,1.60,29.09) to(-62.99,2.88,51.90), but our distance gate timed out
12:53:18.199. No battle admitted. Use Lifestream.GetActiveAetheryte as its own
readiness signal before its local teleport API; retain the60second approach
bound and capture one15second position snapshot if readiness stays absent.
Build/test323 via vmx.bat, then copy after saved idle/unpaused state confirmation.

322 DEPLOY: build passed with only the two existing NU1601 warnings; saved R7
remains unpaused, clears31, wins14/matches19, MgpSpent10000, no pending match or
purchase/acquisition. Copy322 after the focused Verminion checks pass. Verify
the new shard approach, actual Minion Square arrival and Stage6 result.

322 INTENT:321 loaded12:46:45.283, registered Zu item7565 with positive unlock
readback12:46:56.070. No repeat purchase. Lifestream accepted local aethernet89
but immediately reported Destination could not be found(3), because the vendor
counter is outside shard range; VMX travel timed out12:48:26.085. Its documented
AethernetTeleportById requires a nearby shard. Add a bounded native navigation
approach to a loaded Gold Saucer aetheryte before this existing IPC. This is
Verminion-table travel after ADS finishes its vendor task. Build322 via vmx.bat,
test, copy only with unchanged unpaused/no-pending saved state. ADS19 stays live.

321 DEPLOY: ADS19 build passed and loaded12:45:49.703.160/160 focused ADS
tests pass, including post-result cleanup dispatch and the previously failing
inn regressions. Temporary recovery and startup probe are absent from19.
Copy built321 now to register the already purchased Zu and resume campaign6.
Saved spending10000MGP and all progress remain unchanged; no new purchase needed.

ADS19 BUILD INTENT: remove18's completed one-off recovery entirely. The ordinary
ownership-bound cleanup remains.18 tests159/160 passed; remaining failure was
the temporary recovery reading an incompletely mocked plugin config path in an
inn test. Removing the recovery eliminates that dependency. Build/test19 before
copy321; no new purchase or configuration change.

18 RECOVERY OBSERVED: ADS18 loaded12:44:03.404, matched the exact verified
exchange and sent close=true cancel12:44:03.468. No new purchase. Saved state
still unpaused, clears31, wins14/matches19, MgpSpent10000, no pending match or
receipt/acquisition. Both builds passed. After focused tests pass, copy321 to
resume registration from the owned inventory item; require unlock readback and
Stage6 admission. Remove the temporary ADS recovery code after this boundary.

ADS18/VMX321 INTENT:17 loaded12:41:23.897. Its bounded snapshot proves the
same NPC1011039 is targeted, shop=true/menu=false; the exchange itself remained
open. FireCallbackInt(-1) did not close it. Use the existing helper's explicit
close=true cancel.18 includes a one-time DEBUG recovery, restricted to R7 config
path, this NPC, exact known shop name, one unregistered Zu item and observed
post-purchase1,653,997MGP. It only closes the positively verified exchange, never
buys. Remove after recovery.17 tests158/160 passed; the temporary diagnostic
caused two null-service test failures and is replaced by guarded recovery.
Build/test18 and321; copy321 only after18 cleanup and receipt-free saved state.

ADS17 BUILD INTENT:16 compiled but reload12:38:51 failed on a transient build
lock of ADS.json; no16 startup. VMX320 registration timed out12:38:26 with the
Zu receipt already reconciled.17 routes cleanup ticks through the existing
runtime interface, sends each addon cancel once, and adds the late-parent-menu
lifecycle regression. Build/test17 and verify load/read-only UI snapshot. No
VMX copy or purchase dispatch while current UI ownership is being resolved.

ADS16 INTENT:320 loaded12:33:16.557. ADS15 validated native MGP row9, confirmed
and purchased exactly one Zu12:33:25.874. VMX reconciled item7565/10000MGP once
12:33:26.290. Saved MgpSpent10000, pending receipt/acquisition empty. Registration
has not dispatched yet. Single purchases currently close shop once, dropping
cleanup before a parent menu can return. ADS16 continues its existing cleanup
within ten seconds and same-character/target ownership. Include one bounded
DEBUG read-only UI snapshot to resolve the current registration blocker. Build
ADS only; do not reload VMX or repeat the already completed purchase.

320 DEPLOY: both builds passed. ADS15 loaded12:32:03.453;159/159 focused ADS
tests pass, including MGP routing/token coverage and existing tomestone checks.
Saved character is unpaused with no pending match/purchase/acquisition/quest,
clears31, wins14/matches19,0MGP spent. Copy320 now for native exchange validation;
no configuration edits. Require positive purchase/registration evidence.

ADS15/VMX320 INTENT:319 loaded12:27:53.709; ADS14 read MGP correctly, reached
NPC1011039 and opened ShopExchangeCurrency12:28:18. Old catalog classified the
UI as SpecialShopItem, so the runner timed out12:28:38 before purchase callback;
acquired0, VMX failed12:28:39.125. Add explicit MGP shop kind using the existing
currency adapter and exact preview/cost confirmation, preserving old kind values.
Extend the existing MGP regression to cover adapter classification and token
identity/cost/single-use. Build15 and320; test and require ADS15 load plus idle
saved state before copying320. No progress, budget or setting changes.

319 DEPLOY: both builds passed; ADS14 loaded12:25:48.750.159/159 targeted
ADS tests pass after correcting the native balance read. Saved state remains
unpaused with no pending match/purchase/acquisition/quest and0MGP spent.
Copy319 for the corrected Zu request, then observe receipt/registration before
judging Stage6. Source-only VMX marker change from tested318; no config edits.

ADS14/VMX319 INTENT (2026-09-27 12:26 EDT):318 loaded12:24:05, but ADS refused
Zu12:24:10 before travel/submission: Mgp identity now correct, balance source
incorrect. VMX's native inventory read shows1,663,997MGP; ADS CurrencyManager
returns insufficient currency. Read MGP through InventoryManager item29, matching
the observed native source. Build ADS14 and VMX319, run relevant tests, require
ADS14 load and no saved pending receipt/admission before copying319. Preserve
all caps, earned progress and settings.318 acquired0; no purchase submitted.

318 DEPLOY INTENT (2026-09-27 12:24 EDT): both builds passed; ADS13 positively
loaded12:20:48.676.159/159 focused shop/catalog/reliability/IPC/repair tests pass,
including the literal MGP regression. Saved bound R7 state12:23 confirms no
pending match/purchase/acquisition/quest, unpaused, clears31, wins14/matches19,
spending2400gil/0MGP and unchanged caps/reserve. Copy tested318 to the authorized
R:\parasite\vmx\VERMAXION.dll now. Verify exact load, ADS Zu receipt and minion
registration, then ordinary campaign Stage6. No configuration mutation.

ADS13/VMX318 INTENT:317 loaded12:18:02, requested Zu through ADS12:18:08.
Catalog found permanent vendors but represented10000MGP as Item:29. Exact Mgp:29
guard correctly refused before travel/submission; acquired0/receipt0, failed
12:18:08.713. Correct ADS's literal CostType0/Item29 normalization to Mgp while
preserving ordinary item tokens. Add catalog regression for both identities.
Build13 and318, test, require13 startup and saved idle receipt-free state, then
copy318 for the corrected acquisition. No budget/progress changes or FATE work.

317 FINAL BUILD: ADS12 loaded12:15:28 and both builds passed. Full VMX suite
888/888 passed with the new roster,30-point Odin summon accounting and25%/20%
Gentleman special thresholds. Final317 source corrects Fishing's old conditional
ADS help text and the README dependency explanation. Rebuild through vmx.bat,
then copy317 after the unchanged idle character/receipt check. No config edits
are needed. Zu acquisition is already allowed within the40,000MGP cap.

ADS12/VMX317 INTENT (2026-09-27 12:15 EDT): User rejects a volatile FATE-based
Baby Bat requirement and asks for alternatives. No FATE was run.315's vendor
wait failed12:07:13 with targetAvailable=false/acquired0; saved progress remains
unpaused, mode0/target5/campaigntrue, wins14/matches19/clears31/pending0 and
no purchase/acquisition/quest. Budgets223588gil/40000MGP, reserve50000,
spending2400gil/0MGP unchanged.316 ADS dependency changes built,98tests pass,
but were not deployed while acquisition was active.317 includes them.
Replace Baby Bat with Zu Hatchling on6/7/16: permanent10,000MGP stock, Monster,
cost10, AoE autoattack, +60 party ATK special. For15 use the linked Gentleman
guide; eight30-cost units, withdrawing special only at25%HP with8survivors.
This character already owns Gentleman. Preserve old Bat offer only for receipt
reconciliation; no selected stage requests it. All changed strategies remain
native-unverified. Sources: https://ffxiv.consolegameswiki.com/wiki/Zu_Hatchling,
https://ffxiverminion.com/minion-83 and the Gentleman-only Lodestone guide in UI.
Remove ADS's temporary Nonoroon startup probe. Build ADS12 and VMX317, test,
then copy317 only after matched ADS12 load and saved idle/no-receipt check.
Resume ordinary campaign Stage6 and allow ADS's capped Zu acquisition.

316 BUILD ONLY / EXPLICIT FOLLOWUP: User requires ADS for FC Buff Refill and
Fishing. Add ADS to their existing dependency displays (Fishing unconditionally),
FC eligibility/manual start and Fishing BeginRun/Start guards. Vendor Stock's
display now names its ADS provider too. Build316 through Z:\vmx.bat; do NOT copy
over active315 acquisition.315 resumed the saved campaign and ADS accepted
Baby Bat12:02:13, teleported12:02:20; no purchase result yet. Require terminal
acquisition and saved reconciliation before a later VMX reload.

TEN-WIN ACCEPTANCE PASSED /315 DEPLOY INTENT (2026-09-27 12:01 EDT): live313
completed Stage2 sequences10-19 without a reload or loss. Positive credited
victories:11:33:45,11:36:42,11:39:38,11:42:35,11:46:03,11:49:01,11:51:57,
11:54:54,11:57:51,12:00:47. Complete("Weekly victory target reached")12:00:56.
Snapshot12:01:17: wins14/matches19/sequence19/pending0, clear mask31, unpaused,
no purchase/handoff, spending2400gil/0MGP. Exact stopping and ten consecutive
Stage2 Mammet victories are verified on R7. No campaign clear beyond5 inferred.
Restore ONLY mode0,target5,CampaignRequested=true; preserve all earned facts,
attempts, budgets and reserve. Copy tested315 to R:\parasite\vmx\VERMAXION.dll.
ADS11 remains loaded. Campaign resumes6; more than an hour elapsed since the
previous terminal vendor absence. ADS must obtain fresh live NPC availability
at the approach before any purchase and retain its five-minute bound; this is
one resumed acquisition, not recurring reload/retry. Verify315 load and receipt.

315 FINAL BUILD INTENT: first315 build passed and74 focused VMX tests pass.
The source review also guards FC activation/cleanup against ADS's still-open
vendor dialogs. Rebuild315 before eventual deployment. ADS character-change
cleanup regression passes (3/3 IPC compatibility tests, x64 test project).
The first test-only compile used stale AnyCPU references and failed; selecting
Platform=x64 resolves the current ADS reference without rebuilding/reloading it.
Live313 saved state11:55:13: wins12/matches17, sequence18 pending, no purchase,
caps/spending unchanged. Keep the ten-win run uninterrupted through wins14.

VMX315 BUILD ONLY: ADS11 loaded11:51:35; build passed0errors/2existing warnings,
156/156 focused tests pass. Build315 through Z:\vmx.bat, keep313 live until the
ten-win scenario ends.315 includes fishing vendor delegation, generic cleanup
deferral, accurate initial farming stage and selling help. No test config change
or VMX copy at this build boundary. Native FC/selling verification remains open.

ADS11 BUILD INTENT: Continue the explicitly resumed goal. Live VMX313 remains
uninterrupted for the ten-win test. Sequences10-15 have verified victories,
latest11:49:01 (weekly wins10); sequence16 queued11:49:17. No VMX copy until
the target14 is terminal. ADS11 tightens selling cleanup: reset only after
observed/current busy on the same character; close dialogs only with the tracked
vendor's visible shop. AutoRetainer reset is global, not correlated; document
that limitation. VMX source fixes initial farming-stage text and selling help.
Build ADS11 and run focused tests; these paths remain native-unverified.

ADS10/VMX314 BUILD INTENT:313 loaded11:30:34 and selected WinTarget14 with
baseline4. Stage2 match10 won11:33:45 (wins5), match11 won11:36:42 (wins6).
Match12 admitted11:36:59, in duty11:37:06. Do not reload VMX during this ten-win
test. Build314 only; keep313 running until terminal. Original scenario must be
restored afterward as recorded below. ADS09 loaded11:29:59; no vendor dispatch.
Source now delegates fishing food/repair approach and NPC selling to ADS's
existing utility runner and AR's existing sell-list command. Correlated sale
status/cancel, idle admission and busy-to-idle completion; no new sell config.
Generic VMX cleanup leaves ADS vendor dialogs alone. Build/test, inspect all
vendor callsites again. These FC/fishing paths are not selected for native tests.

313/ADS09 INTENT: ADS08 loaded11:24:44, no target in its bounded nearby list.
141 focused ADS tests and74 VMX Verminion/FC/fishing-stock tests pass. Both builds
passed; small followups make vendor-presence diagnostics explicit and reject FC
action receipts on item requests. FC buying migration is source/test verified,
not native verified. Fishing still owns vendor approach/selling, and engine
cleanup still names shop addons; the full hard vendor boundary is not complete.
Use the approved independent Stage2 ten-win acceptance run while Baby Bat is
unavailable. Current bound R7 character: weekly wins4/matches9,5/24 clears,
no pending admission/purchase/handoff, unpaused. Set only its mode WinTarget,
target14 and CampaignRequested=false before copying313. Original mode0,target5,
CampaignRequested=true must be restored after this test. Do not reset actual
results, clear mask, campaign attempts, receipts, caps or reserve. Require ten
consecutive positive wins and exact stop at14; never label partial wins a pass.
Build with Z:\vmx.bat, require loaded313. ADS09 build only observes availability
at startup; it does not buy or operate FC/fishing.

ADS08 BUILD INTENT:07 loaded11:13:41 after the settled-output watcher trigger.
Its read-only snapshot still found FATE enemies and no Nonoroon. No VMX retry.
Implement FC actions through ADS's existing shop runner, separate action/item
inventory, exact native ECommons exchange rows and credit deltas. VMX now only
requests quantity, observes its owned result and activates from reconciled stock.
Build ADS08, run shop regression and build VMX through Z:\vmx.bat. FC/stock live
execution is not selected; Verminion remains the sole runtime scenario. Do not
copy a VMX build merely to repeat the still-blocked Baby Bat request.

ADS07 RELOAD RECOVERY: x64 build passed,0errors/2existing warnings. R7 attempted
reload11:11:36 but its manifest was momentarily locked by the build; ADS unloaded
and the reload failed. The finished manifest is readable. Retrigger the existing
DLL watcher once now that build output has settled; require the compiled07 marker
and read-only vendor snapshot before any purchase. No client restart or VMX reload.

RESUMED 2026-09-27: /goal resume is sufficient authorization to continue; the
user explicitly corrected the mistaken pause. R7 remains the sole runtime target
(the goal tool's R3 text is stale). Preserve unrelated Cactpot/equipment edits.
ADS06/VMX312 finished10:52:18: vendor absent at the five-minute limit, acquired0,
no receipt; VMX entered Failed. Fresh saved state11:07: pending match/purchase/
handoff all empty, unpaused,5/24 clears, next6, spending2400gil/0MGP unchanged.
ADS07 INTENT: build the existing x64 watched output for one DEBUG read-only
availability snapshot near Nonoroon. No VMX reload or redispatch before evidence
of a returned vendor. Official game data and the community guide distinguish
FC action purchases (FreeCompanyExchange) from ADS's current FccShop item path;
FC vendor migration remains outstanding. UI text now includes Baby Bat and
distinguishes older purchase evidence from the unverified ADS handoff.

312 READY: both builds passed; ADS133/133 focused tests passed including vendor
return after45seconds and absence through the five-minute bound (no commands,
no success). Copy312 after the matched ADS06 startup and saved idle-state check.
No recurring reload/redispatch is authorized by this test; observe this one wait.

ADS06/VMX312 INTENT:311 loaded and ADS05 accepted10:42:26.722. Diagnostic at
10:42:48.816 proves landed/unmounted at the mill and no BNpcBase1307; Salthound
FATE enemies are present. Acquired0, no callback, terminal failure10:42:49.391.
Do not infer a UI/control failure from this absence.06 gives missing vendors the
remaining EXISTING five-minute run bound before failing, and only starts its
ordinary20-second shop-opening clock when the NPC appears. One bounded absence
snapshot at overall timeout; no combat, extra automatic run or unlimited retry.
Build06 and312, test, verify06 startup, then copy312 for this bounded availability
wait. Main campaign still5/24 complete, next6, receipts unchanged. FC-buff vendor
migration remains a separate outstanding part of the hard vendor ownership rule.

311 DEPLOY: both builds passed, ADS05 loaded10:40:18.160.131 focused ADS tests
passed, including landing waits preserving interaction attempts;18 VMX tests
passed before marker/API-name-only311. Fresh R7 saved check confirms idle,
unpaused, no match/purchase/handoff pending. Copy311 now for the corrected ADS
interaction attempt. Preserve caps, reserve, spending and campaign progress.

ADS05/VMX311 BUILD INTENT:310/ADS04 reached the approach, then failed10:37:09.007
after three interaction checks with acquired0/no purchase callback. VMX cleared
the handoff10:37:09.760 and failed without a receipt. Correct ADS to let native
landing/dismount and absent-NPC waits use the existing20-second opening timeout,
without consuming an NPC-interaction attempt. Capture one bounded nearby-NPC
diagnostic at that timeout.05 also removes the temporary catalog preview and
finishes the gil-only stock API rename;311 matches it. Build both, then copy311
only after confirming ADS05 loaded and unpaused/no-purchase state. No attempt,
campaign, spending or cap resets; main selected scenario remains Verminion.

310 LIVE:18 focused tests passed; copied310 and loaded10:35:55.998. Ordinary
handoff accepted10:36:00.832. ADS teleported to Camp Bronze Lake, resolved the
offline floor, issued flight navigation10:36:14.612, and stopped at vendor
approach10:37:02.908. Purchase/receipt remains unverified; do not interrupt ADS.
Source-only: removed ADS's one-off read-only preview, and narrowed the new stock
endpoint to ADS.StartGilShopPurchase so stock cannot spend other currencies and
always closes its shop. This API rename requires the next ADS and VMX builds;
neither newer source is deployed. Guarded minion API is unchanged.

310 DEPLOY INTENT: combined VMX build through Z:\vmx.bat now passes (0errors,
2existing NU1601 warnings). ADS04 loaded10:34:06.898; preview10:34:07.884
selected Baby Bat item6187, GilShop262404/row34, BNpcBase1307, territory139,
exact2400gil; canPurchase=true, unresolvedRouteCount0. This verifies catalog and
route selection only, no travel or purchase. After focused VMX tests pass and
saved unpaused/pending0 state is checked, copy310 to R:\parasite\vmx\VERMAXION.dll.
Allow only normal capped minion acquisition, retaining approved caps/receipts.
VendorStock migration is source/build-only; FC-buff vendor code remains pending.

VMX310 BUILD RETRY: the separately edited Cactpot service and tests have now
changed to follow their new policies. Re-run Z:\vmx.bat against the combined
worktree without changing those policies.310 also includes the ADS-only stock
service; no live stock/FC-buff actions selected. R7 remains the sole test target.

ADS04 BUILD INTENT:03 loaded10:31:19.833. Read-only Baby Bat preview10:31:20.938
proves catalog exception fixed and shop found, but unresolvedRouteCount1/no-route.
Nonoroon has no usable parsed layer placement. Add its existing published map
location to ADS's existing offline placement resource (BNpcBase1307, territory139,
map19, size100/offset0; map11.8/24.7 -> world-484.527/159.844). ADS resolves the
floor and requires the live targetable NPC before interaction. Build04 and use
the same single preview before any purchase; no VMX vendor code restored.

ADS03 BUILD INTENT: VMX310 cannot currently build because the unrelated
CactpotRecoveryPolicy edits also changed its evidence enum and completion
contract without updating callers/tests. Preserve that work; no310 copied.
VendorStockService now delegates missing quantities to ADS's correlated ordinary
purchase IPC. FC-buff vendor migration remains outstanding. ADS03 includes that
IPC and one DEBUG read-only Baby Bat catalog preview through its existing runner,
plus full preview exception diagnostics. Build/reload ADS's existing R7 path to
diagnose the catalog independently; the probe submits no travel or purchase.
Remove this temporary preview after the route is established. Versions unchanged.

310 BUILD RETRY: the first build failed because concurrent Cactpot policy edits
added chat-type/speaker arguments while three callers retained the old signature.
Adapted only those callers to the new signature, retaining the policy changes.
ADS02 focused shop/catalog/IPC regression passed130/130. No new deployment yet.
Repository scan also found legacy vendor work in VendorStockService and
FCBuffService; the user's all-vendors-through-ADS rule still requires those paths
to be migrated. Do not claim the whole-repository vendor boundary is complete.

310 BUILD/DEPLOY INTENT (2026-09-27):309 loaded10:12:42.963 and ADS rejected
Baby Bat acquisition10:13:11.672 during catalog resolution, before purchase.
ADS02 build passed (zero errors, two existing obsolete-distance warnings) and
guarded-vendor-20260927-02 loaded10:15:06.005. It skips missing battle-handler
rows and logs catalog exception stacks; its runtime correction is unverified.
Build VMX310 through Z:\vmx.bat, run focused regression tests, and copy only
the authorized R7 watched DLL after checking no purchase/admission is pending.
310 includes the ADS-only handoff, pre-Saucer acquisition and UI explanation.
Versions unchanged; no counters, caps or pause changes. Preserve unrelated
equipment/Jumbo Cactpot edits. Continue the full resumed goal after acquisition.

309 DEPLOY INTENT: the R7 watched file was externally refreshed with old307 at
10:01:49, causing another native-vendor attempt; it failed10:05:33 without a
reported purchase. Replace it now with built309, which contains NO direct vendor
code. ADS's existing R7 development route is enabled at
Z:\ADS\ADS\bin\x64\Debug\ADS.dll; its build auto-reloaded10:11:32 with marker
guarded-vendor-20260927-01. ADS build passed,130 shop/catalog/IPC tests passed
before the later native flight/reentrant-stop additions; VMX build and18 tests
passed. Observe only ADS acquisition after309; no308 copy. Source's newer
pre-Saucer acquisition optimization is not yet in the309 artifact; keep artifact
and future build evidence separate. No campaign counter or caps changed.

CURRENT HARD RULE / ADS MIGRATION (2026-09-27): user requires ALL vendor
interaction and buying through Z:\ads, with no exceptions. No VMX vendor travel,
NPC interaction, shop observation/callback/confirmation/cleanup code may remain.
307 remains loaded and failed/inactive, no purchase pending.308 was built but
NEVER copied. Do not deploy it. The full goal remains resumed; work now migrates
acquisition to ADS's existing purchase runner before more runtime attempts.

309 source replaces VMX shop commands with a correlated ADS single-item handoff.
Keep minion requirements, capped spending authorization, saved receipt accounting
and inventory registration in VMX. ADS owns discovery, navigation, UI and purchase.
ADS's existing checkpoint callbacks are being exposed through guarded purchase
IPC with a matched cancellation ID; validate the cap/reserve before submission
and confirmation. Existing unguarded callers remain unchanged. Source builds,
regression tests and live deployment are pending. Versions stay VMX0.5.0.3 and
ADS0.9.4.2. ADS was clean at this migration's start; do not restore discarded
combat-rule/ADS changes. Preserve unrelated current VMX equipment edits.
Found ADS only catalogs/interacts with EventNpc vendors; Nonoroon is BNpcName1237
and Baby Bat appears in GilShop262404 row34. Its BattleNpc catalog link and live
absence need further research in ADS. No native vendor actions are authorized
through VMX even for diagnostics.

308 INTENT:307 loaded/dispatched09:46:32; native travel reached territory139,
mounted/took off and flew to Poor Maid's Mill. Vendor absent09:47:42.094;
failed closed with no purchase. Existing snapshot was too early to classify
absence.308 adds bounded nearby BattleNpc identities to the existing shop
snapshot on absence, and resumes the required purchase in its current territory
after inventory registration instead of travelling to Saucer first. Build/copy
308 for a fresh bounded vendor observation; preserve all facts/caps/admissions.

RESUMED307 DEPLOY INTENT (2026-09-27): user explicitly resumed the full goal.
Fresh R7 config/log inspection confirms Stage1-5 clears, next6, pending match0,
pending purchase null, caps223588/40000, reserve50000, spending2400/0, unpaused.
Deploy built307 to the authorized watched R:\parasite\vmx\VERMAXION.dll and
verify startup before evaluating the Baby Bat route. Continue campaign afterward.
Prior changes are now in Git3fc8065; preserve unrelated current equipment and
changelog edits. No pause after individual stages or builds is requested.

CURRENT307 BUILT, NOT DEPLOYED; PAUSED (2026-09-27): user requested a pause
after this source/build checkpoint to reload the CLI. Z:\vmx.bat passed with
zero errors and two existing NU1601 PInvoke warnings. Fixed the missing native
ActionManager/ActionType imports found by the first build. Fresh existing
Verminion tests passed18/18 with Debug --no-build --no-restore and filter
FullyQualifiedName~Verminion. Use the test project's default Any CPU platform:
the solution maps its x64 build to Any CPU; a separate x64 test invocation
selected an older output with no matching tests and is not validation evidence.
git diff --check passed. No deployment, reload or runtime action occurred.
Do not copy the DLL, reload VMX, or start another runtime attempt until the user
resumes. Version remains0.5.0.3; built artifact is
Z:\VERMAXION\VERMAXION\bin\x64\Debug\VERMAXION.dll. Dirty files: existing
CHANGELOG.md, LAZYPARASITE.md, tutorial lifecycle test/progress model, Plugin.cs,
and the three Verminion service/strategy/interaction files. No commit made.

306 is the last verified loaded build. Fresh uninterrupted Stage1sequence5
cleared00:15:32; Stage2sequence6 cleared, Stage3sequence7 cleared00:21:23.914,
Stage4sequence8 cleared00:24:45.856, Stage5sequence9 cleared00:27:42.307.
The run failed before Stage6 at00:27:56 for missing Baby Bat. No battle or
purchase is pending. Saved mask31, next stage6, sequence9/pending0/duty0,
weekly9matches/4wins, losses0, campaign requested, pausedFalse. Character caps
223588gil/40000MGP, reserve50000gil, own receipts2400gil/0MGP. Do not copy
another character's receipts or reset progress. Sole runtime target R:\XIVLauncher7.

307 removes the temporary first-three replay path and adds the ordinary Baby
Bat vendor offer: item6187/companion26,2400gil, BNpcName1237 in territory139.
Use existing Lifestream travel to Camp Bronze Lake, existing flight eligibility
(aether-current set19), native mount/takeoff/landing, and navmesh floor lookup
near world(-484,160). Preserve existing capped reservation/confirmation/receipt
and registration flow. Nonoroon normally sells the minion; nearby FATEs can
temporarily remove him. No FATE execution or general progression was added.
Guide: https://ffxiv.consolegameswiki.com/wiki/Junkmonger_Nonoroon
Item: https://www.garlandtools.org/db/doc/item/en/3/6187.json
Vendor travel, actual menu, purchase and Stage6 continuation remain UNVERIFIED.
Build307 and focused tests passed; native route evidence remains pending.
After explicit resume, copy only the watched VMX DLL to
R:\parasite\vmx\VERMAXION.dll, verify307 startup, then observe this bounded route.
ADS/BotologyUpdates changes stay discarded. Full campaign/tournament acceptance
remains unfinished. The goal is paused for the requested CLI reload; the former
blocked reason was stale, not a current duplicate-plugin blocker. This checkpoint
supersedes the old active-Stage2 note.

Historical306 evidence (2026-09-27 00:15 EDT): fresh Stage1sequence5 queued
00:08:41.026, entered00:08:48.260; all commands completed without intervention.
Special enabled00:12:26.552, healing00:12:42.642, gate transfer00:13:13.954,
stone/shield/final stone completed. Native Victory00:15:32.051 creditedTrue
consumed the matching tutorial admission while matches4/wins0 remained unchanged.
Native Challenge Log refresh00:15:41.137 reported5 participations, wins still0.
Automatic Stage2sequence6 queued00:15:48.977, entered00:15:56.304. That attempt
subsequently cleared and advanced through Stage5, as recorded above.306 is
built/copied/loaded; version0.5.0.3 unchanged. Full830
tests last passed with304's new tutorial accounting, later native selection and
Debug shortcut cleanup builds passed; no native input claim rests on unit tests.
Caps remain223588gil/40000MGP with50000gil reserve; own spending2400gil/0MGP.
No ADS/BotologyUpdates changes restored. Full campaign/tournament acceptance is
still unfinished. Sole runtime remains R7. Goal-tool blocked status is stale;
user-resolved duplicate ownership is not a current runtime blocker.

306 build passed zero errors/two existing NU1601 warnings; diff clean. Copying
only watched VMX DLL. Fresh admission/result and Stage2 transition are pending.

305 loaded00:03:38.453 and dispatched00:03:41.834. Accepted a nearby friendly
special candidate; special executed00:03:52.935. All remaining lessons completed,
fresh conclusion/Victory00:06:58.902; creditedFalse, matches3/wins0, exit safely
failed00:07:02.485 because sequence4 was abandoned. This verifies the corrected
overlapping-friendly selection and protects cancelled/failed result accounting.
Now build/copy prepared306 for a fresh Stage1sequence5 with unchanged limits.

306 source-only preparation while305 finishes: remove the unverified generic
DutyCompleted-event replay shortcut. Tutorial replay now advances only through
its observed explicit Victory plus matching admission; a raw exit fails closed.
305 special enabled00:03:52.935 and tutorial advanced to healing00:04:12.079,
then gate transfer00:04:43.356. Hold306 build/copy until current abandoned
sequence4 finishes; source marker306 is NOT yet built or loaded.

305 build passed zero errors/two existing warnings; diff clean. Copy watched DLL
for the narrowly corrected friendly special selection. Loaded marker and native
outcome pending;304 full suite830/830 remains the latest full test result.

305 intent:304 loaded23:58:24.442, fresh Stage1sequence4 queued23:58:40.965,
entered23:58:48.311. Owned summon slot4 and all combat lessons passed. At
00:02:20.706 special selection hit another living friendly Hatchling overlapping
the requested one; exact-ID guard rejected it00:02:21.542 and abandoned sequence4.
Specials may use ANY friendly Hatchling in the nearby party; relax exact-ID only
for the selection-only special path. Preserve exact selection for regrouping,
bomb carriers and other movement. Build/copy305; if still in tutorial, prove
special/completion controls without recreating sequence4 or crediting its result.
Fresh complete replay remains pending. No counter/cap/attempt reset.

304 build passed (zero errors/two existing NU1601 warnings), diff clean. All830
tests passed. Copying watched DLL for a fresh tutorial admission; verify exact304
loaded marker and admission. Keep existing302/303 control evidence separate from
the required uninterrupted fresh replay.

304 intent:303 passed healing, gate transfer, Stone B and Shield commands. Native
tutorial conclusion index306 and Victory result observed23:55:38.373; closure
23:55:42.301 correctly failed credit because pending0. No DutyCompleted event was
observed. This exposed that RecordResult deliberately rejects duty552, so even
a fresh admitted tutorial result could not continue. Add separate admission-bound
tutorial completion (Victory only, permanent Stage1 clear, no direct weekly
match/win increment), retaining native Challenge Log refresh for participation.
One focused regression covers unknown/defeat/stale/cancelled/duplicate/reloaded
admissions and preserving weekly/spending/other-character facts. Build304/test,
copy watched DLL and allow a FRESH Stage1 admission from Minion Square. No attempt
reset, cap change, old-result adoption or reload during its useful battle.

303 live: exact marker loaded23:52:12.359, dispatched23:52:15.781 into existing
Stage1. Isolated Hatchling exact native hit23:52:24.804, move23:52:26.824,
readback23:52:32.669. Execute Action enabled and clicked23:52:34.693. Tutorial
advanced to healing23:52:50.812. This verifies party regrouping and special;
continue remaining instructions without another reload. Pending0 remains;
this abandoned admission must not receive a replay clear or weekly result.

303 built successfully (zero errors/two existing NU1601 warnings), diff clean.
Copying the watched DLL now; load, regrouping and ability verdict pending.

303 intent:302 loaded and dispatched23:49:52.232. Clear click and exact friendly
hit verified23:49:59/23:50:00; Execute Action still disabled23:50:03.372. One
Hatchling remains at(1.39,0.63), with three around(-3,8); the official guide
requires four charged nearby units. Source303 uses the existing single-unit
selection/movement path to bring that straggler into the cluster, waits for the
ordinary movement readback, then checks the special. Exact hit verification now
also applies to this tutorial single-unit move. No new admission or result;
sequence3/pending0/Stage1/weekly2matches0wins/spending2400gil0MGP retained.
302 focused lifecycle tests passed17/17. Build/copy303 via existing authorized
paths, unchanged version. Observe regrouping and special before judging success.

302 build passed (zero errors, two existing NU1601 warnings); diff check passed.
Copy the built x64 Debug DLL to the existing watched VMX path for the bounded
selection test. Exact loaded marker and native special-action result are pending.

302 intent: caps reconfirmed for the bound R7 character at223588gil/40000MGP,
50000gil reserve, own spending2400/0.301 loaded23:41:46.380 and consumed the
reviewed attempt reset at23:41:49.409. Remove that temporary reset. Its visible
Hatchling click hit a friendly unit, but Execute Action remained disabled and
the run failed23:41:57.850. Pending admission stays0; no clear or win credited.
Official guide reconfirmed: four charged nearby minions, then select one and
press Execute Action. Source302 reuses existing clear-then-select with exact
friendly hit verification for the tutorial special; ability waits for selection
readback. This isolates selection from possible party-proximity failure. Build
with Z:\vmx.bat, preserve0.5.0.3, then copy only watched R:\parasite\vmx DLL.
If physically still in the abandoned tutorial, observe controls only; never
recreate its admission. If outside, the existing bounded replay may admit a fresh
attempt. Verify302 marker and actual result before any completion claim.

301 build passed zero errors/two existing warnings; diff check passed. Copied
watched DLL for the reviewed visible-target/owned-slot correction. Load/dispatch
and the fresh tutorial verdict remain unverified until the next bounded snapshot.

301 correction intent: third replay accepted all briefing commands and both
movement/combat exercises. At23:37:52 special-action selection chose the isolated
1HP Hatchling near the top UI; its model center falls outside the existing safe
click bounds. Three healthy alternatives were visible around(146,200). The code
now iterates living friendly candidates and skips projected points outside those
bounds, using the same native input path. Failure23:37:57.911 abandoned sequence3
without credit.300 compiled but was not copied.301 includes the generic owned-slot
briefing fix and this visible-candidate correction. Exact R7/bound selection/
sequence3/pending0/Stage1attempts3 may reset ONLY the development attempt count
after this reviewed correction, and only after the ordinary saved-pause guard
has returned for any FULL STOP. Preserve mask3, weekly counts and spending2400.
If still physically in the tutorial, resume its control proof without recreating
the admission; if outside, allow a fresh bounded replay. Build/copy301 after
success; remove the one-shot retest reset once observed. No goal clear claimed.

299 progress: Hatchling registration verified23:33:37.233; fresh Stage1sequence3
saved23:33:51.354, entered23:33:58.405. Owned-slot summon accepted: tutorial
advanced through camera23:34:27, Gate A23:34:44, queued minions23:34:59, then
single-unit selection/movement23:35:17. No clear yet. Ownership, not merely a
matching saved palette ID, is required for briefing summons. Historical original
proof used owned32/41/50 at slots0/1/2 (not Hatchlings), so do not introduce an
unnecessary tutorial minion requirement. Source300 uses first occupied REGISTERED
palette slot and removes the temporary early-Hatchling purchase condition. The
already bought Hatchling is still required forStage20 and its receipt remains.
Keep299 running this useful attempt; hold300 until a suitable result boundary.

299 native outcome: client was already outside tutorial on reload, so no Retreat
action or observer ran. Existing campaign flow reached Minion Trader; Hatchling
purchase submitted23:33:27.145, exact2400gil confirmation23:33:28.146, item/currency
receipt verified once23:33:29.163. Registration then started. Source-only300
removes the unused Retreat observer AND its unused generic dropdown-helper change;
do not retain untested code for a cleanup that proved unnecessary. Keep current
attempt uninterrupted until its result. No purchase cap or receipt reset.

299 compile passed, zero errors/two existing warnings; diff check passed. Copied
only watched VMX DLL. Await actual marker and cleanup/preflight verdict.

299 intent: second tutorial dispatched actual Hatchling slot5 at23:25:43.944,
but no summon/readback followed; timeout23:26:44.481 abandoned sequence2 with
no bot credit. The character has an unowned Hatchling entry already on its palette.
Acquire the Stage20-required Hatchling early using existing2400gil capped flow
before another tutorial admission. This tests real ownership and remains useful
for the campaign. Temporary replay preflight requests it only if still missing.
First clean the abandoned tutorial: its observed LovmPalette83 is a native
DropDownList with registered ListItemClick. Extend existing list helper to its
declared nested List. On exact R7/bound character/sequence2/pending0/unpaused/
duty552 only, dispatch label Retreat once and capture LovmConfirm/SelectYesno
three seconds later; do not confirm an unseen prompt or credit any result.
If already outside, normal debug dispatch/preflight runs instead. Remove this
temporary cleanup observer after evidence. Build/copy299; version unchanged.

298 loaded23:25:05.266 and dispatch23:25:09.649. Actual runtime was already
outside the failed tutorial; it did not adopt/recreate sequence1. Native weekly
Challenge Log now reports1 participation/0 wins, which was read rather than an
outcome inferred from timeout. Fresh Stage1 sequence2 saved23:25:22.544; entered
23:25:29.831. This is a fresh corrected-slot attempt with its own saved admission.
Await summon/control readback and positive completion; replay still atStage1.

298 built successfully (zero errors/two existing warnings), diff check passed,
copied watched DLL. Runtime load and Hatchling-slot correction remain unverified.

298 correction intent: Stage1 sequence1 entered23:20:49.781 but summon slot0 at
23:21:03.910 did nothing; timeout23:22:04.411 abandoned admission without credit.
Native palette shows empty slot0 and Hatchling3 in slot5, unlike old-client layout.
All three tutorial summons now use existing FindPaletteMinion(3). Active tutorial
reload reads the latest current instruction (old floor excluded all pre-reload
prompts). Preserve pending0, mask3, attempts1 and all counters/caps. Resume only
this existing physical tutorial through the bound reload hook; never recreate
sequence1 or credit its abandoned result. Fresh tutorial replay completion gate
still requires saved admission; if it finishes, observe/control proof separately
and perform a fresh admission for the replay verdict. Build/copy298 only after
successful compile. One-shot launcher/cap setter is removed. No ADS/BT changes.

297 live verified23:20:11.754; cap application and character-bound selection saved
23:20:14.864; RunChallenges accepted23:20:14.900. Current-character config confirms
223588gil/40000MGP caps and50000reserve, spending0/0, no old receipts transferred.
Inventory item24635 registered23:20:23.118; travel to388 and weekly progress0/0
verified. Menu highest available3 at23:20:37.520 proves TWO old clears (mask3),
correcting the earlier interpretation of native completedStages3 as a count.
Stage1 admission sequence1 saved23:20:40.438. Source-only298 removes the one-shot
launcher/cap setter; retain temporary first-three replay until its evidence is
complete. Do not copy during tutorial unless a concrete correction is needed.

297 build passed zero errors/two existing NU1601 warnings;17/17 focused existing
Verminion lifecycle tests passed. Watched DLL copied. Await exact297 marker,
initial-dispatch verdict and persisted cap/binding readback before claiming a run.

297 supersedes undelivered296: user explicitly requests the same purchase caps as
the previous test character. Read-only source-config inspection confirms gil cap
223588, MGP cap40000, minimum gil50000. Apply these three settings only to R7's
current initial-idle character through its existing ConfigManager before dispatch;
preserve its own spending receipts (do NOT transfer old4800/40000 spending).
296 compiled successfully but was never copied. Build/copy297 with the same
guarded first-three replay and native campaign continuation. Expected marker297;
version0.5.0.3 unchanged. Earlier zero-cap restriction is superseded.

295 verified23:14:51.235, inspection23:14:54.294: single development load,
territory144/duty0, no active run/pause/admission, saved mask/sequence/counts0;
native completedStages3. Level80 job27; all Saucer prerequisites complete;
owned Mammet/Airship/Gentleman, missing Baby Bat and Wayward Hatchling. Gil/MGP
caps both0 and retained. No Hildibrand acquisition needed on this character.
296 intent: one-shot guarded existing RunChallenges dispatch on R7's observed
initial idle state, using SetDebugTaskSelection to bind only its current character.
Temporary Debug replay uses existing SelectedChallengeStage for stages1-3,
preserves native clear mask, advances2/3 only on credited victories, and requires
a fresh native completion event plus exit for tutorial replay (no weekly credit).
Replay stages retain three-attempt admission limit. Then ordinary campaign
continues from native first uncleared stage. FULL STOP prevents reload dispatch;
the one-shot launcher cannot rearm after sequence changes. Remove temporary
launcher/replay after evidence. Build via Z:\vmx.bat/copy only watched VMX DLL;
expected marker296. Do not buy minions under this character's zero caps.

LATEST USER STEERING: user discarded ADS and BotologyUpdates changes. Fresh Git
status is clean in both repositories. Preserve that decision; do not restore the
regression, territory rules, index or changelog edits. DAD changes are separate.

295 R7 inspection intent: user confirmed duplicate cleanup. Installed entry now
disabled in profile; native unload finished23:11:45.646. Development entry remains
enabled, but its command handlers need fresh registration after the duplicate.
Build via Z:\vmx.bat and copy only R:\parasite\vmx\VERMAXION.dll. Temporary
Debug-only one-shot inspection is confined to XIVLauncher7 after character
registration. It reads setup, native clears, roster and this character's own
saved progress/caps; it performs no binding, travel, queue, purchase or Resume.
Expected marker verminion-control-20260926-295, unchanged version0.5.0.3. Remove
the temporary observer after evidence; choose the campaign/replay path from actual
native progress without erasing clears. No old-client run is authorized.

R7 HANDOVER VERIFIED / DUPLICATE PLUGIN BLOCKER (2026-09-26): user confirmed
development enablement and stronger job, and explicitly requested all24 challenge
missions tested again from the beginning. Exact294 startup verified23:06:18.068,
version0.5.0.3/client-native A:\ff14\parasite\vmx\VERMAXION.dll. Gold Saucer144
arrival already observed23:03:43.885. However installed0.5.0.2 remains enabled
alongside dev294: native duplicate-assembly warning23:06:17.997, duplicate /vmx
and /vermaxion registration23:06:19.021, DTR ownership conflict23:06:19.022;
Dalamud profile confirms installed and development entries enabled. No new run,
binding, purchase, client mutation or build performed. Current debug selection
is empty. Ask user to disable only installed0.5.0.2, then reload dev0.5.0.3 to
restore its command registration. After confirmation verify single ownership,
inspect current character's own progress/roster/caps, bind the existing debug
selection, and test from Stage1 without erasing existing clears. R7 is the sole
runtime target; earlier R3 restrictions below are historical.
ADS/DAD inquiry: ADS remains the focused regression+changelog only; user explicitly
keeps it. DAD task changes are setting-name/readback compatibility and checked-duty
selection proof, with60 tests/eight lab scenarios and observed unsynced Big Bridge
clear18:48:04, exit18:48:36, quest completion18:49:12. These helped admission and
quest continuation, not Dragon's Neck combat. DAD reconnect-window edits belong
to separate I429 work; preserve them. No DAD/ADS edits performed this continuation.

294 build and watched-file copy completed; R3's last observed marker is still293,
so294 reload is NOT verified. Its six-second inspection had already closed,
and saved paused/no-dispatch was confirmed; no active old-client test remains.
R7 routing changed while the user prepared it: logs23:01:29 scanned/attempted
A:\ff14\parasite\vmx\VERMAXION.dll and current Dalamud config now enables that
same shared path. Earlier D:\temp path was missing. R7 also updated its installed
VMX to0.5.0.2 at23:01:34; do not mistake that old registrables marker for294.
Check the actual active dev assembly after the user finishes travel/setup, before
binding or running Verminion. R7 DAD duty IPC registered22:59:32 and Q bridge
patched7.5.27 at23:01:40. OCE travel toSophia was in progress at23:02:19. No
Gold Saucer arrival observed yet; no new-client control or config mutation done.
User asked whether Z:\ads was edited: fresh Git diff confirms exactly the33-line
positional-rule regression and4-line CHANGELOG entry. No ADS production code or
DLL changes. Explained the separate Botology rule edits/disabled Typhon row.

RUNTIME TARGET CHANGE (user instruction): next testing client is explicitly
R:\XIVLauncher7, replacing R:\XIVLauncher3. User is loading the stronger
character and travelling to OCE first; DO NOT begin automation until it is in
the Gold Saucer after that travel. Read-only routing inspection while loading
is authorized. R7 exists; fresh23:00:50 territory132 and23:01:02 registration.
R7 debug selection is empty, Enabledtrue; do not copy R3's character binding or
progress. R7 Dalamud dev location is enabled at client-native
D:\temp\VERMAXION\VERMAXION\bin\x64\Debug\VERMAXION.dll, not the R3 parasite
path. Verify its actual marker before runtime testing. ADS/FrenRider/WigglyQuest
are present/loaded by available evidence; DAD readiness still needs checking.
294 clean build passed. Finish copying clean294 only to old R3's watched DLL
to remove its temporary UI inspection code, preserving its saved pause. No
further old-client runs. No new-client automation, world travel or saved debug
binding until the user's travel/Gold Saucer prerequisite is met.

USER STEERING: switch to a stronger character on the same authorized client and
start campaign verification from the beginning. Current test character remains
stopped. Asked which replacement character on R:\XIVLauncher3 and when logged in;
await actual identity/readiness before rebinding debug or running its automation.
Inspect its real unlocks/minions/progress; never erase existing character clears
to simulate a fresh run. Dragon's Neck was already unsynced with BMR AI enabled;
the new gear may change viability. No trial or ADS extension approval inferred.

293 tournament information proof22:58:29-32EDT: native GoldSaucerInfo Verminion
tab node7 accepted. GSInfoMinionBattle loaded13values/21nodes. value1/title node10
="The 820th Lord of Verminion Tournament";value2/info node11="Matches available
until 10:45 a.m. 9/28/2026". value3false; value5/7/9 ints0 with labels6/8/10
Matches:/Wins:/Points:. These counters' components were NOT visible, so do not
infer registered status or exhausted allowance from zero values. value11 empty,
12"Available:", result node16"There are no results to display." Native parent
close accepted22:58:32.438. No registration/claim/battle/travel/settings change.
ConstString diagnostic support now positively captured the labels and parent
"Receiving data..." text. A later reader must wait for server loading and use
visibility plus registered/claimable-state evidence, not only the raw zero ints.

294 clean-delivery intent before character handover: removed both inspection
fields, arming block, update callback and method. Keep only ConstString support
and existing paused diagnostics/permanent minion handoff. Build Z:\vmx.bat and
copy only watched DLL after success; require paused or character-bound blocked
reload, no dispatch. Debug selection stays on the previous character for now.

293 build passed zero errors/two existing warnings; copied watched DLL for the
correct observed Verminion tab inspection. No run/counter/quest/provider changes.

292 observation verified: load22:56:44.936, native GoldSaucerInfo opened22:56:48.
Tab label is "Verminion", native node7/ButtonClick param5 (22:56:51.462).
"Lord of Verminion" did not match, so no tab was selected; owned window closed
22:56:54.482. No account settings/progress changed.293 corrects only the exact
observed label/node for the same six-second information capture and includes
ConstString support in existing addon diagnostics. Build/copy293 after success,
inspect GSInfoMinionBattle once, close the owned parent, then clean hooks.

292 independent tournament research intent (ADS choice still unanswered): current
ClientStructs declares AddonGSInfoMinionBattle TournamentMatches/Wins/Points/Info
components and TournamentResults node. GoldSaucerModule holds only the palette;
GoldSaucerManager has GATE/lottery state, not a tournament contract. Official
guide freshly reconfirmed15matches, NPC strength by preceding victories, and
Recordkeeper reward claims. Gold Saucer information may expose usable tournament
readback away from Home World even though Recordkeeper rejected interaction.
Selected paused/idle388/sequence129/no admission or acquisition only: open native
/goldsaucer once, capture after3seconds, attempt exact visible Lord of Verminion
tab via its registered native button, capture GSInfoMinionBattle after6seconds,
then close the owned parent. No registration, battle, world travel or config
change. Build/copy292 through authorized route; remove temporary inspection
hooks after evidence. Permanent acquisition remains unchanged. Not a full CPU
tournament implementation or a claim of live eligibility.

291 verified delivery (2026-09-26): clean watched DLL loaded22:49:46.411EDT;
22:49:49.883 paused/no-dispatch and duty0/resultUnknown/pending0/109matches/
37wins. Native abandoned result cleanup already exited to388. Saved facts:
modeWinTarget,target37,pausedtrue,campaigntrue,Stage23attempts3,mask4194303(22/24),
sequence129,pending0,no acquisition,GilSpent4800,MgpSpent40000. All temporary
queue/battle/result lifecycle test fields, methods and calls are removed; the
permanent Questionable required-minion action remains. Build Z:\vmx.bat passed
with zero errors/two existing NU1601 warnings, same0.5.0.3;829/829 tests passed,
diff checks pass. No active runtime test, build or tool session remains.
This turn adds live queue/battle FULL STOP, paused reload, and late cancelled
result rejection evidence. Goal remains active/incomplete. the user asked whether
trials used BossMod/unsynced; answered with observed FR AI-on/unrestricted-entry
evidence and absent installed Dragon's Neck module. That question is not approval
for cast-aware ADS expansion; the existing include/lean/skip choice stays pending.
Next campaign work still needs Gentleman acquisition/trial mechanics, then23/24;
Stage24 towers/circles and CPU tournaments remain unimplemented. Do not reset
campaign failures or start another trial without a justified strategy change.

290 later-result acceptance verified22:47:30.492EDT: native Defeat while service
inactive/paused,pending0,109matches/37wins,mask4194303,Stage23attempts3. No credit.
Native Quit accepted once22:47:30.492; exit388 confirmed22:47:32.072. This closes
the live queue/battle FULL STOP plus paused-reload/no-result-credit checks.
291 clean build already passed zero errors/two existing NU1601 warnings. Copy
only watched VMX DLL now, then verify291 paused/no-dispatch outside duty and
saved accounting. All temporary lifecycle launch/stop/observer/restore code is
removed. Permanent required-minion Q handoff remains. No provider settings changed.

290 build passed zero errors/two existing NU1601 warnings; copied watched DLL.
Source-only291 removes abandoned-result deadline, arming block, callback and
observer method. Only permanent paused readback and Q acquisition remain.
Compile291 now; copy only after290 cleanup verdict/native exit is reconciled.

289 clean reload verified22:44:04.046;22:44:07.499 readback duty553,resultUnknown,
pending0,109/37 and paused/no-dispatch. No adoption of abandoned battle.
290 cleanup intent: keep service paused/inactive. Only exact selected sequence129
with no admission/acquisition may observe native result for at most6minutes.
On visible result log actual outcome and unchanged counters, invoke existing
native Quit once without credit or Resume. If already outside, do nothing.
Build/copy290; remove this temporary observer for final291 only after settled.
Git baseline changed externally to efa8f3e22:36:13 during work; it captured286
and permanent prior changes. No commit was made by this agent. Preserve the
committed permanent acquisition; remove this task's temporary lifecycle hooks.

288 battle-stop verified: load22:42:34.106; native weekly Run22:42:37.552,
queue sequence129 saved22:42:50.456, battle553 entered22:42:57.473. Selection
and movement input positively observed22:43:20-24, then movement readback and
/vmx stop22:43:24.868. Settled22:43:28.333: activefalse,pausedtrue,duty553,
nativeQueuefalse,pending0,109/37,mask4194303,Stage23attempts3. Original target37
and campaigntrue restored22:43:28.341. The physical battle continues with no
bot ownership/result eligibility. After289 build succeeds, copy clean289 now
to verify paused reload does not resume this battlefield. Later result cleanup
must not create an admission, count a result, or automatically Resume.

288 build passed, zero errors/two existing warnings, copied only watched DLL.
Source-only289 removes the entire temporary battle test, including launch,
timer/restoration fields and the movement-boundary stop. Retain one ordinary
paused-reload diagnostic for native duty/result and saved counters, with no
action or credit. Compile289 now; DO NOT copy until288 settles and restores.

287 clean paused reload verified22:40:00.980/load and22:40:04.492/no-dispatch.
288 intent: selected same character, exact restored baseline sequence128 from
idle Minion Square388. Ordinary weekly Run at temporary target38 admits Stage2.
After verified selection/movement readback, invoke native /vmx stop during the
battle, with180second outer bound and stop on service failure. Read settled
pause/pending/native duty/counters after3seconds, restore target37/campaigntrue.
Preserve Stage23attempts3 and22/24 mask. Build/copy only watched VMX DLL. Following
clean reload must not adopt the abandoned battlefield or credit a later result.
Remove all temporary test hooks afterwards; permanent Q acquisition remains.

286 queue-stop verified: native Stage2 sequence128 saved22:38:41.602; /vmx stop
invoked before Commence; owned queue withdrawal22:38:42.009. Settled22:38:45.044:
activefalse,pausedtrue,nativeQueuefalse,duty0,pending0,109/37,mask4194303,
Stage23attempts3. Original target37/campaigntrue restored22:38:45.058. Fresh
saved config matches; real MatchSequence128 retained. All829 VMX tests passed.
After clean287 build succeeds, copy only watched DLL to verify saved pause
prevents dispatch. No test launcher or queue-stop hook remains in287 source.

286 loaded22:38:08.494EDT, guarded weekly Run accepted22:38:11.863. Native
travel reached Minion Square, existing109/37 observed, Stage2 selection22:38:36.
Do not copy another DLL until the real queue-stop or180second bound settles.
Source-only287 removes all temporary lifecycle fields/methods/calls and queue
boundary trigger. Build now for subsequent clean paused/no-dispatch verification;
permanent required-minion acquisition remains. No runtime completion claim yet.

User's BossMod/unsynced question checked before286 delivery:284 logs show native
unrestricted entry, FR BossMod AI on at22:18:20.758 and22:18:24.487 with passive
tank preset, movement prohibition disabled. Q initially set BMR off22:18:13.574,
then FR enabled it after entry. Installed BMR7.5.6.19 class inventory has ARR
trials1-9 and treasure-hunt Ultros/Typhon, but no Dragon's Neck encounter module.
This does not authorize the still-pending ADS cast-aware expansion.286 built
successfully, zero errors/two existing NU1601 warnings; copy for the independent
native LoVM queue-stop test now. No trial/provider settings changed.

286 intent: independent approved LoVM lifecycle check while the ADS cast-aware
choice remains unanswered. Saved baseline freshly verified paused, no acquisition,
pending0, modeWinTarget/target37, campaigntrue,109/37,mask4194303,Stage23attempts3,
sequence127. Temporarily invoke ordinary weekly Run with target38 on the selected
character from idle145. At the actual saved Stage2 native queue (sequence128),
invoke existing /vmx stop before Commence. Bound the test to180seconds and stop
on service failure. After3seconds read native queue/service/counters and restore
target37/campaigntrue while preserving pause, actual sequence and campaign attempts.
No trial/provider changes, general quest execution or campaign Resume. Build via
Z:\vmx.bat, copy only watched VMX DLL, require286 startup and fresh result evidence.
Remove temporary trigger/observation code for the following clean paused reload.

285 verified delivery: ADS hot-loaded304 active rules/67 shards22:26:33.120EDT
with only the unreliable Typhon row disabled. Clean VMX285 loaded22:26:33.881;
paused/no-dispatch22:26:37.286. Native trial stop/exit/restoration is complete,
and all temporary trial test code is removed. Same0.5.0.3. Build/diff checks pass;
last unchanged-logic tests remain829 VMX and32 ADS rules plus the strengthened
focused assertion. Fresh saved state remains paused,pending0,no acquisition,
109/37,mask4194303(22/24),Stage23attempts3. No active runtime/tool/build session.
This goal turn made progress: live evidence disproved the fixed-position rule,
isolated the missing cast condition, and removed the unreliable active behavior.
The next trial change would expand ADS's existing rule schema and movement
execution beyond the requested static row edits. A user machinery choice is
required before adding that capability: include cast-aware ADS movement and
tests; lean alternative is manual completion of blocked trials; skip leaves
Gentleman acquisition blocked. Do not implement it before the actual choice.
Goal remains active/incomplete; this is not a third consecutive no-progress turn.
Independent LoVM FULL STOP queue/battle acceptance also remains available after
the pending user decision. Do not resume failed trials or reset campaign limits.

285 cleanup intent after284: rule selected Typhon22:19:46.853 at9.0y, but returned
to Ultros after0.136seconds as Typhon walked out of its region. A later Severe
Snort started22:23:01.672 at(-258.6865,19.074574,17.903667), over4y from282's
recorded point. At+5seconds boss=(-258.07587,19.074574,18.112427), while player
remained(-260.24994,19.074574,10.145461), targetingUltros. At+11seconds the cast
ended; at the300second bound22:23:13.408 the player was being knocked to
(-264.0671,18.171013,-3.8427072). No trial completion or post-knockback survival
claim. The test positively disproves reliable fixed-position activation.
Manual command was NOT overwritten by FR startup: native bootstraps22:18:20.758
and22:18:24.487 both precede Manual22:18:28.763. Native cancellation verified
22:23:15.461, exit22:23:23.259, restoration22:23:23.294 (81absent,false/3/disabled).
Disable ONLY the new Typhon experiment in source/client; retain known Ultros row.
285 clean source already built zero errors/two existing warnings, no temporary
trial methods/fields/calls. Copy285 after disabled JSON validation and verify
paused reload. Reliable handling now needs cast-aware ADS movement beyond the
currently authorized static rows; do not invent a VMX trial controller or add
rule schema/ongoing control machinery without the required user choice.

284 loaded22:18:09.460EDT; permanent acquisition accepted22:18:13.393; native
Manual command accepted22:18:28.763. Test deadline22:23:13.394 unless an observed
post-Snort actor reset or quest204 completion ends it earlier. Source-only285
removes all temporary test methods, fields and calls. Compile285 now but DO NOT
copy while284 owns the test: its timed stop/restoration must settle first.
No new run/counter reset, provider setting or rule change is part of285.

284 rule-test intent: previous goal turn made progress with native cast3117
position, regression coverage and ADS rule deployment. Fresh selected configs
still match283 idle/pause/pending0/no handoff and restored Q/FR settings. Invoke
the permanent acquisition action once from guarded145. Temporarily set only
Q81solo1 and FR eight-playertrue/threshold0, using its selected profile. Native
Manual targeting once after combat keeps the ADS chosen target. Bound test to
300seconds, earlier positive post-Snort actor reset or quest204 completion.
Capture actual player/boss positions/HP/target at cast start and5/11/18/30seconds;
the first snapshot checks approach, later ones distinguish knockback survival
from reset. Native Q/DAD/FR/ADS own all trial actions. FULL STOP and restore the
owned provider settings after exit. Do not credit any LoVM progress from this.
Build284 via Z:\vmx.bat then copy only watched VMX DLL after success. Remove
temporary test methods/fields/calls after settling; keep permanent acquisition.

283 verified delivery: clean VMX loaded22:13:11.288EDT and respected saved pause
without dispatch22:13:14.634. All temporary trial launch/observation/restore
methods, fields and calls are absent. Permanent minion acquisition and clearer
provider setup guidance remain. Build via Z:\vmx.bat passed, zero errors/two
existing NU1601 warnings.829/829 VMX tests passed;32/32 ADS rule tests passed,
then the strengthened rule-ID assertion passed its focused test. Diff checks
pass. ADS hot-loaded305 active rules/67 shards at22:13:09.681 after replacing
only the owned142 shard; the new Typhon positional priority is loaded, NOT
battle-verified. ADS runtime source/DLL unchanged; only its existing test and
changelog changed. No publication or commit.
Fresh saved facts: paused=true,pending0,no acquisition,109matches/37wins,
mask4194303(22/24),Stage23attempts3,GilSpent4800,MgpSpent40000. Q duty81 override
absent; FR disabled/eight-playerfalse/threshold3/RotationType0 restored. No active
trial, build or tool session remains. Goal active/incomplete. Next work is a
bounded live test of the now-measured positional rule using native Manual
targeting and approved native ADS handoff settings, checking actual approach,
Severe Snort survival and explicit trial completion separately. No VMX trial
controller should be added. Gentleman reward/registration,Stage23/24 clears,
Stage24 tower/circle logic,CPU tournaments and remaining LoVM acceptance stay open.

283 clean-delivery intent and measured rule:282 captured real Typhon3366 casting
Severe Snort3117 at22:09:53.172EDT: boss/caster=(-254.5551,19.074574,19.027554),
player=(-250.22295,19.106905,21.78736), cast remaining9.67seconds. FULL STOP then
cancelled22:09:55.222; native duty exit22:10:15.059; restoration22:10:15.099
verified81absent,FR eight-playerfalse/threshold3/disabled. No clear is claimed.
Add one Botology142 BossFight row for real Typhon3366, priority5, matched within
1y of that exact measured position; existing Ultros priority10 remains. Native
ADS supports the actor-position condition and one-last-boss ghost; reaching a
boss still yields at5y and does NOT provide a persistent center hold. RSR Auto
can override actual target. The manual command was needed to reach this capture.
Regression32/32 passed; strengthened rule-ID assertion passed its focused test.
Source-only283 removes every temporary trial method/field/call and includes the
permanent provider-setup UI guidance.283 build passed zero errors/two NU1601
warnings. After JSON/diff validation, install ONLY this shard in the authorized
existing ADS DEFAULT store and copy the clean VMX DLL. Require fresh283 paused
reload; no quest/campaign restart. Next justified test is the new positional rule
with native Manual targeting, verified ADS ownership and a bounded stop. Position
approach, knockback survival, trial clear and Gentleman acquisition remain open.

282 loaded22:08:14.391; native product acquisition accepted22:08:18.258 and
/rotation manual accepted22:08:36.294 after combat. Its180second bound expires
22:11:18.258. Source-only283 removes all observation/setup/restoration methods,
fields and calls. It also adds permanent acquisition-panel guidance about the
existing solo-unsynced settings, FR eight-player ADS handoff and maturity gate;
the product does not silently change provider settings. Rebuild283 after this
UI addition; only copy after282's native stop/exit/restoration is verified.

282 deployment intent:281 expired22:07:04.288 without3117; cancellation verified
22:07:06.341, native exit22:07:16.101 and restoration22:07:16.154 (81absent,
FR eight-playerfalse/threshold3/disabled).282 compiled with zero errors and two
existing NU1601 warnings. Deploy the reviewed manual-target observation now,
with the same180second bound and same cleanup. No clear attempt/accounting reset.

281 loaded22:04:00.354, product acquisition accepted22:04:04.284. ADS now owns
the duty, approaches Ultros and arms its combat ghost. Observed casts3134
(Megavolt) and3127(Fireball), verified against XIVAPI. Initial attempts reset
before Severe Snort. The180second bound expires22:07:04.284. Source-only282
adds one native /rotation manual command during owned combat, preserving saved
RotationType0, to reproduce264's successful observation of the later cast.
Do NOT deploy282 while281 remains active; first require stop/exit/restoration.
If281 captures the needed cast, discard this extra experiment and clean source.
ADS's new single regression passes with31 existing rule tests:32/32 total.
It verifies position-based boss priority switches and same-name helper exclusion
using synthetic coordinates. ADS runtime source/DLL have not been changed.

281 corrected observation intent:279 expired22:00:40 without cast3117, cancelled
owned Q22:00:42, exited22:00:57.136 and restored solo81=-1/FRdisabled22:00:57.186.
ADS remained ObservingOnly because FR's restored eight-playerfalse prevented
its handoff. This was not a valid ADS mechanic observation.280 source cleanup
built but was NOT deployed.281 repeats the same180second PRODUCT acquisition
observation with only the already-approved temporary FR eight-playertrue/
threshold0 and duty81solo1. Guard the selected FR profile and original settings;
restore false/3/disabled and absent81 after stop/exit. Include up to12 distinct
native cast snapshots on real bosses and any3117 caster, so helper-owned casts
are identifiable. Capture real boss3366 position alongside actual3117 caster,
then FULL STOP. No movement, target or trial controller is added to VMX. Build
and deploy281 only after success; remove all temporary source after settling.
Same0.5.0.3,22/24,pending0,Stage23attempts3,109/37. No counter reset.

279 built with zero errors/two existing NU1601 warnings, loaded21:57:36.270EDT,
and permanent acquisition accepted21:57:40.069. DAD accepted the checked solo
CFC81 and entry142 occurred21:57:51.424. Observation expires22:00:40.070 if
cast3117 is not captured first. All829 VMX tests passed. Source-only280 removes
the entire launcher/observer/restoration block and both fields. Do not copy280
until279 has stopped its own acquisition, exited and restored settings, or its
explicit pending cleanup has been resolved. No campaign counters reset.

279 observation intent: guide research confirms Severe Snort can be survived
near the center, and Imp can trigger from either boss reaching80%. ADS keeps
one last-reached BossFight ghost, so a higher-priority Typhon rule matching only
his center position may produce the needed approach after Ultros. It cannot be
assumed to hold center or repeat while Typhon stays there. Previous snapshots
do not capture his Severe Snort position; do not invent coordinates.
Use the permanent Acquire action once from exact selected paused145/stage23/
attempts3/pending0 with native Q/DAD idle. Temporarily reserve only absent duty81
override as UnsyncSolo1. Keep FR eight-playerfalse/threshold3 and rotation0.
Capture real boss3366 position only during cast3117, then ordinary FULL STOP;
180second maximum even if no cast appears. Restore duty81 override and originally
disabled FR after native exit. No trial controller, targeting, movement, quest
steps or campaign Resume added. This is observation, not another clear claim.
Build279 via Z:\vmx.bat, copy only the authorized VMX DLL after success, verify
load/start/cast/stop/exit/restoration separately. Remove temporary observation
code after it settles; permanent required-minion handoff remains in the bot.

278 verified delivery: final Debug/x64 build passed with the same two NU1601
warnings and no errors. All829 tests passed, zero skips; Git diff check passed.
Copied only the DLL to the authorized watched path.278 loaded21:35:40.079EDT
and ordinary paused/no-dispatch message21:35:43.419 verifies the final reload.
No lifecycle launch, stop timer, observer or provider-restoration hook remains.
Permanent admission explanations and registration-time Before-AR release remain.
Live PRODUCT acceptance now proves native acquisition start, persisted handoff,
active reload with no replay, truthful idle ownership after registration, FULL
STOP, native priority/reward-stop cleanup, duty exit and cancelled reload.
Reward quest completion/registration are still UNVERIFIED and incomplete.
Fresh saved facts: paused=true,pending0,no handoff,109matches/37wins,
mask4194303(22/24),Stage23-attempts3,GilSpent4800,MgpSpent40000. Native settings
restored as recorded277; no active quest/duty test or tool/build session remains.
Goal remains active. Next useful research is a SOLO-specific Dragon's Neck
strategy: the group guide's Ultros-first order led to Imp/Severe Snort failures.
Do not infer that higher damage or changing targets solves it. Existing ADS
position-matching BossFight rules may merit inspection if a guide and actual
cast-position evidence support a center-position response; none is implemented
or verified. Google fetch was stopped after hanging; GamerEscape returned403.
Stages23/24, tower/circle logic, CPU tournaments and remaining LoVM acceptance
remain open. Do not restart the failed clear without a justified strategy change.

278 final-source intent:276's final owned duty exit verified21:30:28.010.
277 loaded21:31:36.752 and restoration verified21:31:40.179: native priorities0,
rewardStopAbsent, solo81OverridePresent=false. Saved configuration independently
confirms81 absent/stop502 absent and FR disabled/eight-playerfalse/threshold3,
RotationType0/exit delay20 unchanged. No temporary provider settings remain.
Remove the last restoration block; no lifecycle launch/observer/timer remains.
Keep the permanent admission diagnostics and registration-time gate release.
Update existing changelog with LIVE lifecycle proof; reward acquisition and
registration remain pending. Build278 through Z:\vmx.bat, full tests, diff check,
then deploy only the authorized DLL and verify paused/no-restart after reload.
Same0.5.0.3 and same campaign/accounting facts. Overall goal remains active.

277 cleanup intent:276 loaded21:29:22.160. Registration21:29:49.197 released
actual Before-AR gate(WaitingForWorldReady->Skipped; suppressionowned=false),
then active handoff observation21:29:49.198 reported VMXbusy=false/territory142,
attempted=true/cancelled=false and NO new dispatch. This positively verifies the
ownership correction. Ordinary FULL STOP21:29:54.223; product cancellation and
native priority/reward-stop cleanup verified21:29:56.256. Remove all lifecycle
launch/observer/timer fields and code.277 restores only duty81 override1->absent
and original FR disabled state, after strict idle145/Qstopped/DADstopped/no
handoff/no pending match/priority0/no stop502 guards. Require observed exit before
copy. No campaign/accounting/settings reset. Then remove restoration in278.

276 intent: previous274 exit verified21:26:02.068 after its owned cancel.
275 loaded21:27:41.662 and accepted the bounded product acquisition21:27:45.446.
Remove its launcher and reload276 to verify the registration-gate correction
against the active saved handoff. The observation must report VMXbusy=false;
then ordinary FULL STOP runs after five seconds. No native settings/counters
change. Build/copy276; retain the same180second275 bound until reload.

275 ownership correction intent:274 loaded21:24:54.587 and observed the saved
active handoff21:25:03.289(attempted=true,cancelled=false,territory142), with no
new quest submission. FULL STOP21:25:08.312 stopped native Q21:25:08.330; product
cleanup reported cancelled21:25:10.372. ACTIVE reload/no replay and cancellation
are now verified. The same readback showed VMXbusy=true because constructor
Before-AR startup's unstarted gate survived until later in the framework tick.
Narrow permanent fix: after registration loads the CURRENT character's handoff
and matches its Owner to contentId, call existing SkipBeforeArForLogin before
debug/manual dispatch and retainer setup. This releases actual gate ownership;
do not mask AutomationStatus. Repeat only the active-reload ownership assertion
through the product action, with180second bound, then276 observe/stop. Require
idle145/Q stopped/DAD stopped; no counter resets or new native setting changes.
Duty81 override1 from270 remains the only pending provider-setting restoration.
Build/copy275; verify the reload reports VMXbusy=false after the correction.

274 intent:273 loaded21:23:42.464 and product Acquire accepted21:23:46.220.
The same saved solo81 override1 was checked before copy. Remove the launcher;
274 only observes the active saved handoff and invokes ordinary FULL STOP five
seconds after its reload observation. Current273 has a180second bound from that
accepted timestamp. No other action/settings/counter changes. Build/copy274 now.

273 active-reload test intent:271 accepted product acquisition21:19:40.108;
DAD bridge started CFC81/territory14221:19:40.362. Its90second FULL STOP fired
21:21:10.112 before272 loaded. Native Q stopped21:21:10.127; product observer
verified cancellation/removed handoff21:21:12.182.272 loaded21:21:23 and ordinary
paused/no-dispatch21:21:40.331, then ADS clicked Abandon duty21:21:40.383 and
observed exit21:21:43.920. Thus product start/cancel/cleanup and cancelled reload
are verified; ACTIVE reload is not. Saved pending0/Stage23-attempts3/pause=true
unchanged. Repeat only that remaining lifecycle case from stopped idle145, using
the same PRODUCT action and a180second bound to allow build/reload. No clear
attempt or campaign Resume. Existing duty81 override1 remains reserved from270;
no new settings changes.273 launch must be removed immediately in274, which
observes saved handoff and stops it after five seconds. Build/copy273.

272 intent:271 loaded21:19:36.367. Before cleanup VMXbusy=true with summary
plugin load while logged in; DAD/AR were idle. Ordinary FULL STOP released that
unstarted Before-AR gate. Permanent Acquire accepted21:19:40.108 with a saved
handoff(minion21, six quests204..502, DispatchAttempted=true, Cancellation=false,
OwnershipReleased=false). VMXbusy=false/VMXactive=false prove it does not publish
parent duty ownership. No native admission/result claim yet.270 had no submission.
Remove the launcher.272 only observes the EXISTING handoff on reload, logs that
it did not dispatch, then invokes the ordinary FULL STOP five seconds later,
provided the same in-memory handoff still exists.271's90second stop remains its
bound until reload. Preserve all counters; duty81 override1 still awaits restore.
Build/copy272 now. Require observed reload, cancellation, native stop/cleanup and
exit separately. Do not start another quest if any observation is missing.

271 intent:270 built/loaded21:16:41.874 but product wrapper rejected21:16:45
with its generic existing-automation message; no handoff record and no native
quest submission occurred. Only duty81 override was reserved as1; it still needs
restoration. Improve the permanent rejection message to identify the real owner.
The temporary test skipped the normal debug action's initial FULL STOP cleanup.
271 uses that existing cleanup, logs prior ownership and invokes the same product
Acquire wrapper once; it never bypasses an admission check. Require the reserved
duty81 override to still equal1, and retain the90second bounded owned-record stop.
Native reload/dispatch remains unverified until positive evidence. Build/copy271.

270 lifecycle-test intent: previous turn made progress, and269 remains the last
verified loaded DLL. The actual trial clear remains blocked, but permanent
handoff lifecycle acceptance can advance independently. From exact selected idle
territory145/paused/Stage23-attempts3/pending0, with WQ and DAD stopped, invoke
the PRODUCT AcquireVerminionMinion(21) wrapper once. Temporarily set only WQ's
absent duty81 override to UnsyncSolo(1), so any native admission stays solo.
All other native settings stay as restored; FR eight-player remains false/3.
Arm one ordinary FULL STOP after90seconds for this bounded cancellation test.
Next build will remove dispatch and observe the saved handoff across reload,
then invoke FULL STOP. This is an intentional lifecycle test, not a trial-clear
retry or campaign Resume. Restore only duty81 override and original FR disabled
state after native shutdown; no counters or purchase caps change. Version stays
0.5.0.3. Build/copy270 only to the authorized watched DLL after build success.

269 verified delivery: Z:\vmx.bat Debug/x64 build passed, zero errors and the
two existing NU1601 PInvoke warnings. All829 tests passed, zero skips;17 focused
Verminion lifecycle tests also passed. Git diff check passed. Copied only the
DLL to R:\parasite\vmx\VERMAXION.dll. Loaded269 at21:10:52.847EDT and ordinary
paused/no-dispatch message at21:10:56.102 positively verified the final reload.
No temporary trial observer, rotation command or legacy cleanup code remains.
Permanent required-minion acquisition stays in the standalone window with native
Questionable ownership, saved cancellation and explicit pending-cleanup messaging.
Fresh saved state unchanged: pause=true, Stage23 attempts3, pending0,
mask4194303(22/24),109 matches/37 wins, no new acquisition record. Legacy trial
run is stopped, exited and its temporary native provider settings restored as
recorded below. Full permanent handoff runtime acceptance remains pending; do
not claim the legacy direct run verified it. Overall goal remains active/open.

269 intent and current boundary:268 loaded21:08:26.661. Native preflight showed
territory145/WQrunningfalse/DADstoppedtrue. Cleanup21:08:30 positively read zero
priorities and no reward stop502. Saved native config independently verifies no
trial76/81/85 overrides, FR disabled/eight-playerfalse/threshold3/RotationType0;
exit delay20 unchanged. Legacy acquisition is stopped and temporary provider
settings are restored. No reward or campaign progress;22/24, Stage23 attempts3,
pending0, pause=true remain. Do not automatically restart the failed trial.
Remove ALL temporary trial diagnostics, rotation commands and cleanup blocks.
Keep the permanent acquisition implementation, ownership, UI and FULL STOP fix.
Build269 using Z:\vmx.bat, test the final source, deploy only the authorized DLL,
and require a fresh269 startup plus the ordinary paused message. Same0.5.0.3.
Permanent acquisition live start/reload/cancel/cleanup remains unverified: the
legacy direct route was deliberately never adopted into the new handoff record.
Next acquisition research must handle Dragon's Neck Imp/Severe Snort using the
existing stack before another justified attempt; ADS target priority alone and
RSR Manual alone did not clear it. Static ADS position rules have no cast/status
condition; no coordinates or mechanic rules were invented. Campaign23/24 and
CPU tournament acceptance remain open; the overall goal is active.

268 intent:267 loaded21:06:25, exact preflight confirmed native reentry142,
WQ automatic quest204 and DAD active with the six original priorities. Native
WQ stop21:06:31.654 set Manual; its DAD bridge already requested ADS leave.
Explicit RSR cancel/FR off followed; ADS accepted leave and recognized/clicked
Abandon duty21:06:35.806. Exit/ADS release observed21:06:39.284. No trial clear.
Run only the remaining owned-setting restoration from idle territory145 with
WQ stopped, preserving all unrelated settings/counters. Build/copy268; this
attempt must not stop or restart any newly running Questionable quest.

267 intent:266 built/loaded21:02:32, cleanup guard rejected21:03:04 without any
mutation. Bounded logs explain why: native WQ requeued Dragon's Neck after its
timeout, and ADS is again cycling resets in142. Do not wait another hour or
restart it. From the exact recorded paused/Stage23-attempts3/pending0/no-handoff,
quest204/six-priority/stop502 context, stop the failed legacy WQ route. If native
Q is stopped but character is back in142, stop its RSR/FR combat and call the
existing ADS.LeaveDuty once, leaving provider settings for the observed exit.
No direct UI/input/trial implementation and no success credit. The same cleanup
can restore owned settings only outside duty after DAD is stopped. Log preflight
truth to distinguish native retry from other guard failures. Build/copy267.

266 cleanup intent: native time-expired message20:56:40.295; ADS released20:56:46;
DAD explicitly failed the bridge20:56:53 because DutyCompleted was absent. WQ
ran its native DisableCombatPluginsForDuty20:56:53.835.265 loaded20:57:28 and
read territory145/WQrunningtrue/current204/DADstoppedtrue at20:57:31; trial204 and
reward502 incomplete.265 did not restore Auto because the native trial had ended;
WQ's combat shutdown supersedes the temporary mode test.17 lifecycle tests passed.
Stop this known failed legacy Q run only at exact quest204, outside duty/queue/
combat, with its unchanged six priorities/stop502 and Stage23-attempts3/pending0.
Once native Q stops, remove only those six priorities/stop502, remove trial76/81/85
overrides only if still1, restore selected FR eight-player true/0 to false/3,
and restore originally disabled FR through /fr off. Preserve exit delay20 and
all other provider/user settings. No quest restart, battle admission or counter
reset. This is cleanup of the recorded257 test, not adoption by permanent handoff.
Build/copy266 after success; remove the temporary cleanup block after readback.

265 intent:264 loaded20:52:17, native /rotation manual accepted20:53:04.
All three snapshots held target3367(Ultros); at20:53:49 both bosses still had
~100kHP and player had Imp613, verified from XIVAPI as allowing only Imp Punch.
Severe Snort cast3117 observed20:54:33, another arena reset20:54:49. Target-mode
control passed, trial clear failed. Native one-minute-left message20:55:29 means
previous timer estimates were inaccurate; await actual exit/failure. Remove all
three-snapshot diagnostic fields/tick block now.265 restores original RSR Auto
once if still in territory142 and takes one provider/quest-completion readback.
No native priorities, trial queue, campaign counters or persistent FR settings
change. Build/copy265; remove this last restoration block after observation.

264 intent:263 actual target stayed Typhon despite ADS selecting Ultros. Gear is
71% or better; WAR77 mainhand16329 is the level60 Augmented Shire weapon. The
trial continues resetting without clear. FR's existing ADS-owned combat branch
bootstraps once then holds; RSR Auto can select its own target. Test the native
/rotation manual command once under selected-character/paused/territory142 plus
WQ-running/ADS-owned guards. No FR saved config change or quest dispatch. Observe
three target/HP snapshots at5/25/45 seconds. RSR original runtime mode was Auto;
restore it after diagnosis if the duty lifecycle has not already done so. Build
and copy264 only to the authorized watched DLL; no counters or provider settings.

263 intent: ADS hot-loaded304 rules/67shards20:39:55 and selected BossFight Ultros.
This verifies native rule adoption/target priority. A fresh opening still reset
20:41:39, so priority alone has not produced a clear.31 ADS precedence/shard/
semantics tests passed; the ADS source worktree stayed clean. The new Botology
shard/index/changelog are uncommitted and its one context is installed on the
authorized client. Read three bounded post-rule snapshots at0/20/40seconds and
include existing equipment-condition, main-hand and available-job readers once.
This distinguishes failed damage/gear eligibility from remaining arena mechanics.
No gear change, quest control or new rule is part of263. Build/copy263.

Trial142 rule intent:262 loaded20:36:57, observed real bosses3366(Typhon) and
3367(Ultros), each158990maxHP. At20:37:43 Ultros150890/Typhon152886; at20:37:55
target switched toTyphon while both lived (Ultros129443/Typhon115064). Player
still had75741/77560HP. This proves damage splitting, not the exact wipe mechanic.
Guide https://ffxiv.consolegameswiki.com/wiki/The_Dragon%27s_Neck says focusUltros.
Add one BossFight row scoped to territory142/CFC81/base3367 in BotologyUpdates,
priority10; no helper actors, coordinates, quest logic or combat actions.
Validate shard/index and test on the authorized client's existing DEFAULT ADS
store by adding only that missing context and index entry. Preserve all unrelated
rules. ADS's existing file reload handles it; no ADS DLL reload/start/stop needed.
Keep the native Q chain running. Verify target selection and actual clear separately.

262 observation correction:261 loaded20:35:07 and remains paused. The fight has
more than eight untargetable same-name helper actors; increasing a row limit did
not capture boss health. Filter the observed live target base IDs3366/3367
(seen in260/261 player target reads) instead, at most two actors. Native targeting
switches from3367 to3366 during combat while ADS falls back to generic CombatHold.
No claim about the reason for the switch or boss damage yet. Build/copy262 for
the same three read-only snapshots; all existing quest actions remain native.

261 observation correction:260 successfully loaded20:33:03.265 and respected
pause20:33:10.351. ADS owns duty142 in0.9.4.2; Q is running. Player is WAR77,
77560/77560HP, targeting base3367. The initial two enemy rows were untargetable
same-name NPCs3368/3369 with2433HP, so they cannot explain boss damage. Expand
the bounded read to at most eight matching NPCs to include the actual targets;
retain exactly three snapshots/no actions. Build/copy261, then evaluate actual
target HP before selecting an ADS rule.260's constructor fix is live-verified.

260 recovery intent:259 reached its assembly marker20:30:41.665 but plugin
construction FAILED with NullReferenceException in the new acquisition guard:
BeginBeforeArLoginPendingFromPluginLoad runs at constructor220, before
VerminionService is created. No diagnostic snapshots or quest commands ran.
Fix both early Before-AR entry guards to tolerate pre-service initialization;
the registered-character path still checks the saved handoff before execution.
Build260 and replace the watched DLL to recover the authorized plugin, then
require successful startup and the bounded read-only trial snapshots. Q owns
the existing run independently; no counters, Q priorities or client settings change.

259 intent: goal is active again. Fresh bounded logs through20:25 show repeated
ADS combat/transition cycles in The Dragon's Neck (CFC81/territory142), without
clear evidence. XIVAPI confirms the duty identity; its public guide explains that
all players leaving the arena causes a wipe and describes Typhon knockbacks.
Do not infer whether positioning, target selection or damage is responsible.
Add three read-only ADS/native combat snapshots over24seconds through the existing
character-bound paused Debug branch. No quest actions, duty starts/stops, settings
changes or counters. Build259 and copy only its DLL to the watched R:\parasite\vmx
path.257's direct launcher left VMX owner0/paused; unloading it does not own the
Questionable run.259 preserves pause, never adopts that legacy run, and only logs
ADS phase/objective plus current player/NPC HP, position, target and status IDs.
Remove the temporary snapshot code after observed evidence; use the existing ADS
rules/stack for any proven trial correction. This is not another quest controller.
259 build passed after correcting quotation syntax in the temporary diagnostic;
same two dependency warnings, zero errors. Copy259 now after checking the selected
saved character still has pause=true, Stage23-attempts3 and no pending match.
Loaded marker and read-only snapshots remain to be observed.

258 local verification complete: final Z:\vmx.bat Debug/x64 build passed with
zero errors and the same two NU1601 PInvoke dependency warnings. All17 focused
Verminion lifecycle tests passed, then the full suite passed829/829 with zero
skips. New regression covers prepared-versus-dispatched quest ownership, native
quest transitions, cancellation persistence, released ownership, character/settings
copy isolation, weekly reset and no battle/campaign credit. Git diff check passed.
The observer and FULL STOP paths have source/build coverage only; the permanent
handoff has not been dispatched on the live client. Build258 stays local at
Z:\VERMAXION\VERMAXION\bin\x64\Debug\VERMAXION.dll; loaded257 is unchanged.
No live mutation, deployment or reload was performed during this correction.
Before later live testing, resolve the directly launched257 chain and restore its
recorded temporary native settings. Do not adopt it from old quest-ID metadata.
Next permanent-handoff tests: idle admission, DAD trial admission without a VMX
parent hold, reload without dispatch, FULL STOP and precise native-setting cleanup.
Campaign22/24, Stage23 attempts3, Stage24 mechanics and tournament acceptance remain
open. The goal remains paused; no campaign attempt counter was changed.

258 intent (local implementation only): latest user correction keeps required-minion
acquisition as permanent bot functionality. Do not remove the capability or leave
it as a test-only workaround. Added a standalone-window acquisition action for
the required Gentleman route. WigglyQuest receives unfinished native priorities
and a stop after Her Last Vow; it owns all quest/trial execution. VMX remains
paused and inactive, blocks new local automation, and observes saved per-character
ownership without replaying submission after reload. FULL STOP cancels only the
owned route; cleanup removes only reserved priorities/stop. Native reward quest
completion is not registration, victory or campaign credit. Explicit Resume
re-enters ordinary setup. No shared DAD ownership bypass, trial runner, provider
configuration changes or automatic character progression added. Remove257's
temporary launcher and old Debug quest-by-quest acquisition, preserving ordinary
Gold Saucer single-quest setup. Legacy Hildibrand metadata cannot stop a directly
launched native run. Version0.5.0.3 stays fixed; expected local marker258.
Build through Z:\vmx.bat; no copy/reload while the native acquisition is unresolved.
Goal tool currently reports paused. This correction does not restart campaign work.

257 outcome carried forward: loaded19:45:07.368EDT; native priority/stop prepared
19:45:10.972; StartQuest204 accepted19:45:11.002; DAD accepted CFC81/territory142
19:45:11.254; The Dragon's Neck unsynced entry19:45:17.987; FrenRider's ADS inside-duty
start accepted19:45:35.104. No observed clear or Gentleman reward yet. One bounded
existing-log snapshot during258 work saw later territory142 transitions through
19:53:35, but no completion evidence in its sampled window. No live commands,
settings, reloads or quest probes were issued. Keep the six native priorities,
stop502, three duty-mode overrides and FR restoration obligations below pending.
The active257 run has no new258 handoff record and must not be adopted implicitly.

257 intent: acquisition is authoritatively stopped after the duty81 timeout;
there is no running quest to interrupt. Honor "let Questionable do the quests"
by one direct provider launch while VMX stays paused, rather than another VMX
parent-owned quest. Inspected installed WigglyQuest7.5.26 APIs and resolver: its
manual priority list wins before MSQ, AddStopQuest creates a native Stop condition,
and StartQuest uses normal automatic questing. Native priorities will be exactly
204,490,491,492,493,502; native Stop after502 bounds the approved acquisition.
Current originals: priority list empty, persistencefalse, clearOnCompletionfalse;
Stop.Enabled=true, Conditions empty, CommandAfterStop empty; MSQ priority0 stays
unchanged. Remove only these six priority entries and the502 stop after the run.
Keep the previously recorded duty-mode/FR restoration obligations as well.
The one-load Debug hook requires selected character, unpaused exact204 checkpoint,
Stage23-attempts3/pending0, native204 sequence4, WQ stopped, DAD stopped, no native duty/queue/combat,
readable funds above reserve+1000, and no unrelated priority/quest-stop entries.
Normal cleanup then persists VMX pause; require its automation status idle before
setting/reading back native WQ priorities and its stop, then submit StartQuest204
once. No quest steps, trial runner, ownership bypass, counter reset or new IPC.
Build Z:\vmx.bat and deploy only this hook to R:\parasite\vmx\VERMAXION.dll after
successful build. Expected marker257, version0.5.0.3 unchanged. Remove the temporary
launch block after its observed dispatch; no further reload during active questing.
257 final build passed (two existing PInvoke warnings, zero errors). Native204
sequence4 is the preflight guard, rather than the stopped provider's UI selection.
Copy the built DLL now; dispatch and provider-owned quest continuation are not yet
verified. The current paused gate still takes precedence over this one-load action.

256 local-only result: implement the Final Coil guide's one-add focus using
existing enemy observations and group orders. XIVAPI Companion rows78/165 confirm
the names wind-up Bahamut/clockwork Twintania, not native stage IDs or mechanics.
Require Bahamut by name before choosing either add. Commit to one living add when
both appear, retain that identity through temporary absence, and stop add focus
after its observed HP0. An observer starting with one surviving add stays on the
boss. No battle/result credit derives from this local phase state. Native tower
and circle signals remain unknown; ordinary24 admission stays blocked. No quest
logic, client settings, copy or reload changes. All16 Verminion lifecycle tests
passed. Z:\vmx.bat Debug/x64 build passed with the same two PInvoke dependency
warnings and zero errors; Git diff check passed. Source/local build256; last
deployment254, whose load remains unverified. Version0.5.0.3 unchanged.
Bounded existing-log windows and a saved-progress read were taken after the local
work to check acquisition without interrupting it. Saved quest is now204,
providerWigglyQuest, pausefalse, campaigntrue, Stage23-attempts3, mask4194303,
sequence127/pending0/matches109/wins37. This is progress since quest166, not proof
of the later trial or minion acquisition. No quest dispatch, reload or new probe.
Fresh existing log resolves the attempt: WQ reached204 sequence4 at19:20:06.803
and DAD accepted a LocalDuty bridge for CFC81/territory142 at19:20:07.041. No
admission was observed. At19:25:07.037 WQ reported its300-second entry timeout,
stopped its duty bridge, and became Manual; VMX failed the owned prerequisite at
19:25:07.255. Acquisition is STOPPED, not still running. Stage23 remains blocked
by missing Gentleman. Existing source still publishes active VMX prerequisite
work as busy and DAD's local queue requires externalHeld=false; this is the
previously proven parent/child ownership conflict. No fresh executor diagnostic
was injected, so do not claim its exact current blocker from these logs alone.
Do not blindly restart the same parent-owned quest. User-directed Questionable
ownership, existing stop rules and the three failed Stage23 attempts remain intact.

255 local-only result: latest user steering also says "dont try to become
questionable." Leave the active quest provider alone. No client reads, copy or
reload in this pass. Re-read both final-stage guides: the Lodestone guide explicitly
uses Gentlemen for both23 and24; its24 strategy dodges plain circles and assigns
one healthy unit to each pillar. Align24's required roster/cost and replacement
orders with that guide. Disable its four-caster-withdrawing special to retain
tower units. Keep ordinary24 admission blocked: native tower/circle recognition
and add handling remain unimplemented. Show missing ownership and the guide in
the window. All15 Verminion lifecycle tests passed; Z:\vmx.bat Debug/x64 build
passed with the two existing PInvoke dependency warnings, no errors. Git diff
check passed. Local marker verminion-control-20260926-255, version0.5.0.3 unchanged;
output Z:\VERMAXION\VERMAXION\bin\x64\Debug\VERMAXION.dll. No deployment: watched
DLL remains254 with load unverified. No campaign attempts or character settings
edited. Next live work requires acquisition to finish and the minion to be
registered; leave Questionable undisturbed. Stage23/24 clears, Stage24 native
mechanics, CPU tournament execution and remaining lifecycle checks stay pending.

Latest user steering: "just let questionable do the quests." Leave the existing
Questionable/WigglyQuest acquisition running; do not interrupt questing with more
reloads, probes or manual quest actions. No goal pause was requested. Deployment
254 removed all temporary FATE probes and was copied to the selected watched DLL;
its loaded marker has not been checked yet. Last positive quest completion1441,
current saved quest166 at the last read. DAD's two fixes and Big Bridge clear/exit/
turn-in are verified; later trials and Verminion23/24 are still pending. Preserve
the recorded temporary provider settings until acquisition ends, then restore.

Scope: implement the approved CPU Verminion plan in VERMAXION, beginning with live
battlefield observation and control proof. Reuse the existing debug reload hook,
logging, task identity, configuration, cleanup and AutoRetainer ownership. The
approved plan selects this single checkpoint, mode/progress configuration, bounded
failure limits and focused regression coverage. No additional machinery selected.

Runtime target: `R:\XIVLauncher3`, with Lord of Verminion selected in `/vmx debug`.
Source edits, Debug builds, DLL replacement and this client's selected reload tests
are authorized. Do not change release versions or operate other clients.

238 intent: first trial stopped17:52:49 before queue because WigglyQuest7.5.26
checks Meta.AutoDutyModeEnum/Unsynced/DutyModeEnum, but Dad only handled legacy
names and lacked two readbacks. Narrow DadDutyIpcService compatibility fix plus
existing runtime regression now pass27 Questionable tests. Current headless/lab
builds passed; progression/cancel/unload/FrenRider/direct-solo scenarios all exited0.
Dad0.7.1.0 Debug build passed with six existing nullable warnings; version retained.
Generated lockfile-only build churn restored; no new dependencies or trial runner.
Fresh saved state matches127/pending0/109-37/mask4194303/Stage23-attempts3,
quest1318/providerWigglyQuest/priorityfalse/pausefalse, spending4800+40000.
Use the verified installed Dalamud temporary plugin commands:238 invokes only
/xldisableplugintemp dad from the guarded idle selected Debug checkpoint. It does
not start acquisition. After native unload evidence, replace only installed
dad0.7.1.0/dad.dll, then239 enables Dad. Remove temporary command code and use240
to resume acquisition only after provider registration and compatibility readback.
No campaign attempts/settings reset. No client restart or OS input.
238 loaded18:07:35.661, guarded disable submitted18:07:39.140 and native Dad
unload finished18:07:40.373. Replace the installed Dad DLL and build/copy239
now;239 requires Dad absent before submitting its temporary enable and returns
without quest dispatch. The original installation remains enabled persistently.
239 loaded18:08:53.423; Dad enable submitted18:08:56.561, load and IPC
registration completed18:08:57.416 from the replaced installed DLL.240 replaces
the temporary enable action with read-only legacy/Meta IPC agreement checks for
all three checked keys, then resumes the ordinary owned quest handoff. Remove
that temporary proof after its observed result; no setting writes in240.
240 loaded18:10:23.184; all three IPC legacy/Meta readbacks agreed18:10:26.776
(Looping/False/Support), proving the fixed provider is loaded. Acquisition resumed
18:10:26.863; first native Dad bridge session366/76/LocalDuty/loops1 started
18:10:27.109. This is dispatch evidence, not trial entry/completion.241 removes
all temporary command/proof code and is source/build only until trial ownership
and result settle. Do not reload blindly while the duty session is unresolved.
241 revised intent: no register/commence/entry evidence followed18:10:27.206
selection clearing; the uncapped main log remains fresh. Inspect the queue's
actual blocker instead of bypassing ownership gates. Normal authorized unload/
cleanup will cancel the unresolved owned quest.241 refuses a new dispatch while
native queued/bound/in-combat, then reuses the ordinary quest handoff and records
one read-only executor-status snapshot five seconds afterward. No trial success
or attempt reset is inferred. This temporary diagnostic must be removed afterward.
241 loaded18:16:29.528, snapshot18:16:38.105 confirms QueuePreparing blocked by
externalHeld=true/postArReady=false; available/worldStable/ARavailable=true,
ARbusy/multimode=false. VMX's active unlock parent blocks its own WQ child duty.
240's attempt had already timed out18:15:27.599; no trial admission/completion.
Lean acquisition path selected within existing authority: use WQ's existing
StartSingleQuest directly for the test character's trial quest while VMX stays
paused. Do not expand the shared IPC/ownership protocol or add a trial runner.
242 removes the diagnostic and, after ordinary cleanup plus idle/native/no-duty
guards, saves VerminionPaused=true and starts only quest1318 through existing WQ
IPC. Preserve Stage23-attempts3 and all results/spending.243 will remove this
temporary single action; its reload must respect the pause and leave the direct
WQ quest alone. After positive quest completion, guarded development unpause can
resume VMX acquisition; ordinary campaign Resume would reset attempts and is wrong.
242 loaded18:21:49.700; saved pause and direct quest1318 accepted18:21:53.296.
The independent WQ->Dad attempt passes ownership safety and maps/selects duty76,
but rehydrates every six seconds before registration. No trial admission yet.
243 removes the direct-start action and uses the paused reload branch for one
read-only DAD executor/native-selection snapshot. It must not run cleanup or
restart WQ. VMX stayed idle for this direct attempt, so its unload owns no quest.
This distinguishes the native mapping issue from the confirmed parent ownership
hold. Keep both limitations explicit; no shared ownership protocol was changed.
243 loaded18:23:48.842, paused without cleanup18:23:52.079. Native selected
agent Regular76 but interfaceSelectedId20021/HasRouletteSelected=true/queue=false/
unsynced=true. Snapshot also shows externalHeld again during reload;244 captures
the parent's exact activity plus the existing bounded Finder snapshot to identify
the live selection mapping. Read-only paused branch only; no new dispatch.
244 loaded18:26:41;18:26:44.557 native UI shows Battle on the Big Bridge and
1/1 selected while DAD waits for interfaceSelectedId20021 (stale roulette/reward
detail) rather than76. No Join was issued.245 adds the exact selected-content
vector to the existing bounded Finder diagnostic and repeats a read-only paused
snapshot. Verify the selection's actual contents before changing the DAD proof.
245 loaded18:28:52.945;18:28:56.152 reports exactly one selectedContent Regular76.
WQ's direct attempt ended without trial admission; executor is now absent.
Focused linked-production native-reader regression reproduced expected76/actual20021.
Fix only the regular selection readback to require one SelectedContent entry of
Regular type; reject empty/multiple/roulette and keep existing mapping/stability
checks. Rename its adapter property for clarity and update the virtual adapter.
Testing/building this second DAD correction precedes another local reload attempt.
Reload-time externalHeld was VMX BeforeAutoRetainer/WaitingForWorldReady, not an
unpaused Verminion task; the direct WQ run did progress to exact selection normally.
246 intent after DAD tests/build: replace the temporary diagnostic with a guarded
/xldisableplugintemp dad in the existing paused branch. Require no WQ/DAD run,
no native queue/duty/combat, quest1318 and retained Stage23-attempts3. Once native
unload is confirmed, copy the fixed existing Dad DLL, enable with247, and verify
its renamed CheckedRegularDutyId reader before one direct quest1318 retest.
246 pre-copy verification: all60 focused DAD tests and all eight selected current
headless lifecycle scenarios passed (direct solo; unstable/wrong/stale selection;
cancel before register; deferred restoration; Questionable progression/FrenRider).
Native/live behavior remains unverified. Fresh bounded log confirms245 is loaded,
the direct WQ attempt timed out18:26:53 with no admission, and DAD remains idle.
Copy246 now; its runtime guards must still refuse any active WQ/DAD/native duty.
246 loaded18:39:41; guarded disable submitted18:39:44 and native Dad unload
finished18:39:45.623. Replace only installed dad0.7.1.0/dad.dll with the tested
build, then build/copy247.247 requires Dad unloaded and WQ/native duty idle before
the existing temporary enable command. Acquisition remains paused and unstarted.
247 built/loaded18:41:22, Dad enable18:41:26; native load/IPC registration
completed18:41:26.957 and WQ bridge patched18:41:27.073.248 verifies the loaded
assembly's CheckedRegularDutyId property returns76, then starts only incomplete
quest1318 using WQ's single-quest IPC while Verminion remains paused. Existing
idle/native/progress guards apply. Remove this temporary start block after its
one observed dispatch; no trial result or campaign attempt reset is inferred.
248 reached territory366/Big Bridge by18:43:03, WQ entered its wait-for-exit step.
FrenRider ADS.StartDutyFromInside accepted18:43:05.447; ADS navigated to Gilgamesh
and entered CombatHold18:43:21.538. Native unsynced-entry messages also observed.
This verifies admission and FR/ADS handoff, not completion.249 removes the entire
temporary proof/start block and preserves the ordinary paused-reload return.
Build source cleanup now; do not interrupt Dad/ADS/WQ ownership during the trial.
248 loaded reader returned76 and direct WQ accepted18:42:50.692. DAD joined
exact CFC76 once18:42:53.334.249 cleanup loaded18:47:28.504 without redispatch.
Big Bridge DutyCompleted observed by ADS, FR and Dad18:48:04.474-.482; FR began
its existing exit-only takeover. Completion is verified; exit and quest1318
turn-in remain pending. Full current VMX suite passed825/825; both diffs clean.
250 prepared intent: resume only when native quest1318 completion, idle overworld,
WQ stopped, Dad stopped and retained Stage23-attempts3/pending0 are all proven.
Clear only the development acquisition pause and use ordinary Debug cleanup and
dispatch; never use campaign Resume while paused or reset its failed attempts.
Build250 now; copy only after bounded exit/turn-in evidence. Remove the temporary
unpause block once observed. Normal acquisition should continue with quest1438.
Exit confirmed18:48:36 (WQ wait-for-instance-exit completed); quest1318 native
completion wait passed18:49:12.021 and its tasks ended18:49:13.057.250 build
passed; copy now. Dad successful cleanup reported unavailable /vbmai off and
/wrath auto off commands; WQ continued its normal combat-disable and turn-in.
These warnings do not invalidate the observed clear/exit but remain visible.
250 resumed the ordinary acquisition: quest1438 dispatched18:50:01.800 and
accepted18:50:09.659. Thus first trial clear, exit and turn-in are all verified.
251 removes the temporary post-trial unpause block; build only for now, leaving
the current overworld quest undisturbed. Acquisition continues through1438,
1439,1440,1441,166,202,203 to204/CFC81. At that trial, use the same deliberate
paused-parent/direct WQ action instead of weakening DAD ownership checks.
Quest1438 completion verified18:53:35.244; quest1439 accepted by18:53:47.663.
251 cleanup build passed, not copied. Still live250, now well past its one-shot
1318 guard. Retained results/attempts remain unchanged. First-trial runtime needs
no ADS rule change. Second/third trial runtime and all Verminion23/24 clears remain
pending. Re-read both published guides before the next campaign attempt; the
linked Lodestone guide explicitly supports Gentleman-only Stage23/24, whereas
ffxiverminion's alternate23 also uses Cursor (unowned, two achievement certificates).
Keep the selected Gentleman-only strategy; no extra currency or minion acquired.
251 copy intent: remove the dormant development unpause action from the loaded
plugin during the current overworld quest. Guard saved unlock1439/1440, pausefalse,
pending0 and Stage23-attempts3 before copy. Normal unload/Debug cleanup may stop
and resume that owned single quest; no trial or new campaign attempt is permitted.
251 loaded18:54:57.131, resumed1439 at18:55:01.051; quest1439 positively completed
18:55:37.344. Current owned quest1440 (Seeds of Rebellion) reached sequence2 by
18:57:38.524. No temporary plugin enable/disable/start/unpause code remains in
source or loaded251. Dad's two corrections are loaded. No ADS rules changed.
252 intent: quest1440 sequence2 stalled in combat; bounded log shows repeated
"Unable to attack FATE target. Your level is too high" against MandragoraQueen
2954. Existing route specifies FateEnemies for2950-2954 after the Mandragoras
event interaction. Add one read-only current/synced FATE plus native enemy-FATE
snapshot to existing CaptureSetup, then rebuild/reload252. No sync command yet;
first establish the exact native event and current sync state. Quest timeout and
campaign/result/spending guards remain. Remove temporary probe after resolving.
252 loaded19:05:48, but its normal startup diagnostic cannot run because current
combat prevents character registration. Unload stopped the owned WQ route;
remaining combat is unresolved.253 performs the same read-only CaptureSetup once
on the framework thread before registration, gated by the saved Debug character
and loaded player identity. It does not bypass registration or dispatch work.
Installed WQ uses a one-time /lsync attempt based on its PublicEvent reader;
native sync state is still needed. Keep this as temporary existing-hook research.
Quest1440 completed19:09:22.649;1439/1440 now both positively complete.253
loaded19:09:06.752; by its setup snapshot sequence2 had already ended, so the
temporary FATE-specific diagnostic did not execute. No level-sync command or FR
setting change was made. Current quest1441 (A Case of Indecency) started19:09:22.700.
Do not claim a proven sync fix from this recovery.254 removes both temporary FATE
probe blocks, builds and reloads only on the same ordinary overworld checkpoint.
Expanded bounded evidence resolves252's delayed diagnostic:19:07:46.788 reported
level50/syncedtrue/currentFate336/syncedFate336, with a matching Mandragora corpse.
Quest1440 advanced sequence2->3 at19:07:47.521. Thus the event did sync and clear
before253; no new FATE control or persistent setting was necessary.
254 build passed. Initial copy guard refused because quest1441 had already
completed19:11:09.294 and166 started19:11:09.346. Fresh saved state confirms
166/pausefalse/pending0/Stage23-attempts3/weekly109-37. Copy the cleanup254 only
with this updated ordinary-overworld guard; this is not a trial dispatch.

Resume235: the user installed Dad0.7.1.0 and FrenRider1.3.1.9. Fresh bounded
evidence confirms Dad duty IPC registered17:37:07.249 and its existing bridge
patched WigglyQuest7.5.26 at17:37:07.675. FrenRider loaded17:37:14.670 and
reports readable ADS ownership17:37:14.810. The installation decision is
resolved; do not ask again or install any additional provider. ADS is0.9.4.2.
Intent235: update the compiled attempt marker and include our actual stack in
the existing setup diagnostic, build through Z:\vmx.bat, then guard the saved
234 pause/quest1207/127-pending0/109matches-37wins/mask4194303/Stage23-attempts3/
spending4800+40000/caps223588+40000/reserve50000. Clear only the deliberate
setup-test pause and deploy235. Ordinary campaign Resume must not reset the
three attempts. Continue the owned Hildibrand chain through existing WigglyQuest
and Dad/FrenRider/ADS; add rules only if an observed trial failure requires them.
No campaign attempt reset, new trial controller, dependency or version change.
235 built/loaded17:40:39.119, resumed1207 at17:40:42.731 with the expected
six loaded providers. Quest sequence10->11 observed17:41:19.716. Facts retained.
236 intent: the installed WigglyQuest settings have DefaultDutyMode=Support,
AutoUnsyncOverleveled=false and no overrides;76/81/85 are whitelisted. Current
FrenRider profile has eight-player ADS handoff=false/threshold3. Apply existing
settings through a temporary selected-Debug setup block: only those three trial
mode overrides become UnsyncSolo; only this character's eight-player handoff
becomes true/threshold0 so untested trials can be observed. Leave other families,
follow target, combat settings and enable state unchanged. Save through each
provider's own methods. No queue command or new trial runner is introduced.
Original settings for restoration: all three trial overrides absent; selected
FR AdsEightManEnabled=false, AdsEightManMaturityThreshold=3. Remove temporary
setup code immediately after runtime/config readback and restore these specific
settings after acquisition testing. Do not broaden to other characters/duties.
236 built/loaded17:46:46.403 and applied the existing settings17:46:50.025.
Saved readback confirms trial76/81/85 modes1(UnsyncSolo), selected FR eight-player
handoff=true/threshold0, FR still disabled outside its existing Dad duty entry.
1207 completed17:42:15.419;1315 completed17:45:32.815. Quest1316 resumed
17:46:50.053. Intent237: remove the temporary setup block, build and copy while
the owned overworld quest is running. No more provider setting writes on startup.
Preserve235/236 result/attempt facts and use the existing bridge for first trial.
237 built and loaded17:48:29.577; owned1317 resumed17:48:33.234. Temporary
provider setup block is removed.1316 completed17:47:24.319;1317 completed
17:49:19.486;1318(The Three Collectors) dispatched17:49:19.542, accepted and
at sequence1 by17:49:22.695. Seven of21 Hildibrand quests have positive clears.
Next live milestone is sequence6/CFC76 (Big Bridge), using the existing solo
unsynced Dad queue and FR->ADS handoff; no trial result or rule change claimed.

Standing constraint reaffirmed by the user: AutoDuty is forbidden on every client.
the user explicitly selected the existing AIDS/ADS, DAD and FrenRider stack for
the required Hildibrand trials. Inspect its existing execution/handoff first;
at most add necessary ADS rules in Z:\botologyupdates. Do not build a special
trial runner or new integration layer. Repositories: Z:\ads, Z:\dad\dad,
Z:\frenrider. No other client or publication is authorized.
Do NOT install or propose installing it. No AutoDuty was installed or configured.
Its earlier installation question is resolved/rejected. Hildibrand acquisition
remains authorized using already installed capabilities; verify any trial control
before dispatch. This is separate from Verminion, which does not use AutoDuty.

Hildibrand authorization (2026-09-26): the user answered "Allow Hildibrand
acquisition work" to the pending scope question. ARR Hildibrand quests and its
required trials are now authorized for this same test character, to obtain
Wind-up Gentleman. This supersedes earlier pending-decision/no-quest-chain
instructions for this character only. Preserve Stage23 attempts3 until minion
registration and an explicit reviewed development Resume. Local routes contain
disabled duties76/81/85, so inspect installed-provider behavior before execution.
Intent230: build a Debug-only21-quest completion/acceptance/sequence snapshot
plus current level/job and loaded duty providers. Copy with unchanged settings,
facts and spending; the existing CPU Home World gate ends this read-only run.
No quest, trial, world transfer or campaign battle is dispatched by230.
230 build passed after fixing diagnostic-only interpolation/import/API errors;
earlier failed candidates were never copied. Deploy230 now; config guards passed.
Installed WigglyQuest7.5.26 uses policy instead of the old Enabled route field,
but still needs AutoDuty.ContentHasPath. No AutoDuty install folder was found;
the new loaded-provider readback will distinguish dev-loaded availability.
230 loaded16:49:01.466; native16:49:04.590 shows all21 Hildibrand quests
unaccepted/incomplete. Current job21(Warrior),level77; loaded providers are
WigglyQuest, BossModReborn and RotationSolver, with no AutoDuty. Funds268788gil.
231 reuses the owned single-quest handoff for an explicitly seeded Hildibrand
chain, gated to this character's selected Debug run. It does not opt ordinary
users in, and it does not implement or auto-enable trial execution. Each quest
retains the existing15minute timeout and native completion requirement.
Intent: build/test231, then guard existing127/pending0/109/37/mask4194303/
stage23-attempts3/spent4800+40000/caps/reserve unchanged. Set CampaignRequested
true, modeWinTarget (target37 preserved), and UnlockQuestId1204/providerempty/
priorityfalse to seed the now-authorized acquisition. Do NOT reset campaign
attempts. It will stop for a quest/trial blocker or at Stage23's retained limit
after minion registration. No new minion spending or trial settings changes.
231 build passed; all824 tests passed and diff check clean. Apply guarded seed
and copy231 now. No campaign attempt reset is part of this acquisition start.
231 dispatched16:52:05.664 after the guarded seed. Quest1204 was accepted
16:52:31.764, reached sequence2 at16:53:41.800, and WigglyQuest began its
FateEnemies combat step16:54:53.262 using RotationSolverRebornModule with BMR
passive movement. Preserve this attempt (15minute quest limit from16:52:05).
Installed WigglyQuest supports FATE sync; no FATE/quest success has been observed
yet. Do not confuse "Combat started" with a completed fight or completed quest.
Trial research: WigglyQuest opens Duty Finder when AutoDuty is unavailable; it
does not auto-queue that unsupported step. Required trial sequences are1318/6
for76,204/4 for81,502/4 for85. Installed BMR7.5.6.19 has generic AI/rotation IPC
and /bmrai commands but no Gilgamesh/Ultros trial-specific modules. No duty,
BossMod settings, additional plugin, or trial control has been changed yet.
1204 advanced sequence2->3 at16:59:30.136, positive completion of the FATE
requirement; quest turn-in is still pending at that observation.
AutoDuty upstream has existing routes for territories366(Big Bridge),142
(Dragon's Neck),396(Big Keep), at github.com/ffxivcode/AutoDuty/master/AutoDuty/Paths.
Asked the dependency-gate choice: include AutoDuty installation on the same
authorized client and its three solo unsynced trial runs, lean alternative of
manual trial clears, or skip and stop acquisition at the first trial. Await an
actual answer before installing a plugin or expanding trial machinery. Continue
independent authorized quest steps; Hildibrand acquisition itself is approved.
1204 completion verified17:00:49.465; owned quest cleanup and1205 handoff
followed17:00:49.527.1205 accepted17:00:57.939. Fresh saved state1205/WigglyQuest/
priorityowned/unpaused,109matches/37wins/pending0/stage23-attempts3. This proves
the first acquisition quest and its automatic next-quest handoff; not all21.
1206 completion verified17:03:37.256 and1207 dispatched17:03:37.397; first
three quests complete. Intent232: verify the planned FULL STOP during setup.
Temporary Debug-only code calls the existing FullStop once, five seconds after
this selected character's Hildibrand handoff becomes active. Build and copy232
with all current quest/progress/config facts untouched. It resumes the owned
quest, then must persist pause, stop WigglyQuest and release its inserted priority
without changing campaign attempts or result facts. Remove this temporary proof
code immediately after readback; next233 reload must remain paused. Only then
perform a guarded development unpause preserving Stage23 attempts3. Do not
install AutoDuty or queue any trial while that separate choice remains pending.
232 build/diff checks passed; guard confirms active1207 with retained127/109/37/
mask4194303/attempts3/spending4800+40000 and owned priority. Copy232 now without
settings edits. The prior231 suite passed824; this is a temporary runtime proof.
232 loaded17:06:01.514 but the intended FULL STOP proof did NOT execute:
startup reached Failed17:06:11.464 because displayed Warrior level was22,
previously77;1207 is accepted at sequence10. Its normal startup cleanup stopped
WigglyQuest and removed the owned priority. Saved pause remainsfalse,quest1207,
providerWigglyQuest/priorityfalse and all Verminion facts unchanged. Do not call
this a successful FULL STOP test.233 retains the temporary one-shot test and
replaces the displayed-level gate with native GetClassJobLevel(job,false), as
already used by this repo's equipment runtime. Capture both levels to verify.
Intent: build233, guard the failed/owned1207 facts, then copy with no config edit
to resume setup and repeat the still-pending stop proof. No trial/provider change.
233 loaded and proved unsynced level77 despite displayed22 at17:08:35.823.
Quest1207 resumed17:08:35.881; the one-shot existing FullStop ran17:08:40.908,
WigglyQuest stopped17:08:40.915 and Verminion became Idle17:08:40.922.
Saved pause=true, owned quest priority=false, quest retained. This positively
verifies FULL STOP during setup, not queue/battle stops. Fresh bounded readback
reconfirmed the stop evidence before234 preparation.
Intent234: remove both temporary proof fields and its update block, build/test
and copy with pause still true. Verify the exact new startup marker and refusal
to restart before a separately guarded development unpause. Preserve all facts,
quest1207 and Stage23 attempts3. No trial dispatch is part of234.
234 built successfully through Z:\vmx.bat;824/824 tests passed and diff check
passed. Guarded copy retained pause=true and all quest/result/attempt facts.
Loaded17:14:27.936; refused automatic dispatch17:14:31.107 because Verminion
is paused. This completes the setup FULL STOP plus paused-reload proof.
Current marker234, version0.5.0.3. Temporary stop-test code is fully removed.
Do not reset campaign attempts when clearing this deliberate development pause.

Own-stack inspection: Z:\dad\dad\Services\DadQuestionableReflectionBridge.cs
already supports WigglyQuest and routes its existing duty calls to dad.Duty IPC.
DadDutyIpcService owns unsynced local queue/completion/cleanup. FrenRider already
hands supported duties to ADS. No new trial runner/integration is needed.
BotologyUpdates has no dedicated142/366/396 shards; absence alone does not
justify adding rules because ADS provides general progression/combat behavior.
No ADS, DAD, FrenRider or BotologyUpdates source/config changes were made.
Client inventory: R:\XIVLauncher3\installedPlugins has ADS0.9.4.2; neither dad
nor FrenRider has an installed-plugin directory. DevPluginLoadLocations contains
only the enabled VMX DLL. Old config folders and historical DevPluginSettings
entries exist for Dad/FrenRider, but are not active plugin load locations.
Next required choice: enable our released Dad/FrenRider on this test client
before exercising the existing bridge. Do not install anything without that
target-specific choice. Keep the verified pause while that decision is pending.
Independent verification while that choice is pending: inspected scheduler and
reload pause gates plus owner-bound cancellation; no production change needed.
Added one focused model regression for a pending match crossing weekly reset
and serialization/reload, with game participation arriving before its result.
It checks exactly-once credit, stale/unknown rejection, retained campaign/gil
facts and a later cancelled admission. All13 VerminionLifecycleTests passed
against the rebuilt test project; diff check passed. This is accounting coverage,
not a live reset/cancellation proof. Client DLL remains234 and paused; no copy,
unpause, quest dispatch, dependency install or trial action in this continuation.
Blocked audit after the third consecutive turn with this pending decision:
fresh filesystem/config read still finds ADS installed, no Dad/FrenRider install
directories or dev load entries, and only VMX configured as a dev plugin.
Selected character remains paused at quest1207 with Stage23 attempts3,
pending0 and109/37 weekly facts. Independent focused accounting verification
is complete. Await the user's existing installation choice; do not ask it again,
install plugins, clear the pause or substitute an external duty provider.
Goal is blocked, not complete; campaign remains22/24 and tournament acceptance
remains pending. Resume the existing plan once the decision/setup changes.

Resume229 (2026-09-26): fresh bounded log/config inspection confirms228 loaded
14:54:05.366, interacted with1011594 at14:54:13.625, and the game rejected it
14:54:13.635: Home World only. No dialog appeared;228's captured-dialog message
was inaccurate.127/pending0/109matches/37wins/mask4194303/stage23-attempts3,
spent4800gil+40000MGP/reservationnull remain unchanged; modeCpuRewards/unpaused,
campaignfalse. Ten consecutive Mammet farming wins remain verified.
229 checks native Home/Current World before setup and interaction, exposes the
requirement in settings and distinguishes visible-dialog evidence from absence.
Its setup snapshot includes world/DC IDs to assess the existing travel route.
Intent: build with Z:\vmx.bat, run existing tests, then deploy229 unchanged
settings to verify the Home World blocker. No world transfer, campaign attempt,
purchase or tournament admission is requested. Hildibrand decision stays pending.
229 Debug build and824 tests passed; diff check clean. Installed Lifestream2.5.5.0
has ChangeWorldById, CanVisitSameDC and CanVisitCrossDC, but ChangeWorld checks
cross-DC first. Do not invoke it without current world/DC and capability checks.
Deploy229 now after the same saved-fact guards; settings stay unchanged.
229 loaded15:03:59.820. Native15:04:02.987 reports currentWorld87/currentDc9
and homeWorld401/homeDc6. At15:04:03.001 Start failed at the new Home World
gate before setup/travel/NPC interaction. This verifies the away-world guard;
the absent-dialog branch remains source-verified only. No world transfer was
requested. The existing service fails when ContentId disappears, and Plugin
resets services on character change; cross-DC logout continuation is not ready.
Final229 source/built/deployed/loaded agree, version0.5.0.3,824 tests passed.
Selected runtime settings remain CpuRewards/target37/unpaused/campaignfalse;
serviceFailed with Home World requirement. No battle, purchase or active travel.
Preserve the Stage23 three-attempt stop and pending Hildibrand scope decision.
Review caution: native CompletedLoVMStages reports23 while positively verified
clears remain1-22 (mask4194303). The currently unused CompletedStages reader
must NOT be treated as a count of proven clears without resolving its semantics.
Existing model tests cover cancellation/unknown outcomes/reload/reset and separate
goals, but live FULL STOP during setup/queue/battle remains unverified. No extra
farming run was needed after the already verified ten-win streak.

Build223 preparation (2026-09-26):222 loaded14:03:30.429 and resumed117 at
14:03:33.564.117 LOST14:04:48.209, credited once99matches/27wins; closure
14:04:51.821. Normal three-attempt pause persisted14:05:01.945. Fresh config
confirms117/pending0/mask4194303/attempts3/run3/losses2/pausedtrue, spending
4800gil/40000MGP, no pending purchase. No counter or purchase edits made.
Guide review: ffxiverminion.com/stage23-the-binding-coil uses strong poppets
with support. The linked Lodestone guide /character/28572768/blog/4629440/
specifically clears23 with Wind-up Gentleman alone, stream replacements and
the final20% special. Website minion21 confirms400HP/90ATK/75DEF/speed4/cost30;
Power of Deduction buffs allies but withdraws its four casters. Require eight
survivors before casting, and block the ordinary earlier special/probe path.
223 replaces the failed Zu requirement with this explicit guide roster and
documents Her Last Vow (ARR Hildibrand level50). The native117 ownership list
lacks21. It is not sold for gil/MGP; do not automate that unrelated quest chain.
Keep campaign attempts3 and its pause; no more speculative roster admissions.
Intent: build223 through vmx.bat and run existing regressions. Deploy first
with the saved pause intact to verify no auto-restart on reload. Then perform
the independent approved Stage2 Mammet farming acceptance with an exact target
of37 weekly wins (ten above27), preserving all campaign attempts/results and
purchase facts. That separate run has not been configured or dispatched yet.
Expected marker verminion-control-20260926-223; version remains0.5.0.3.
223 build succeeded;824/824 tests pass, including the guide-minion prerequisite,
30-point queue reservations and prevention of sacrificing the last attack party.
Diff check passes. Deploy223 with pausedtrue and all saved facts unchanged;
verify the paused reload before setting up the separate farming run.
223 loaded14:17:06.146 and explicitly refused dispatch14:17:09.254 because
Verminion is paused. This verifies saved-pause reload suppression while idle;
it is not a FULL STOP-during-battle test. No admission or progress change.
Intent for224: same code, new executable marker for the settings-only test.
Guard117/pending0/99matches/27wins/mask4194303/pausedtrue/campaign23-attempts3/
run3/losses2/spent4800+40000/reservationnull and unchanged caps/reserve.
Set weekly WinTarget37, CampaignRequested=false, SelectedChallengeStage=2,
and unpause this separate authorized farming test. Leave CampaignStage23 and
CampaignStageAttempts3 untouched; EnsureRun establishes the new weekly limit
from the changed target. No fabricated wins or clear bits. Build224, then push.
This tests ten consecutive Stage2 Mammet wins and exact stopping at37; three
consecutive losses or30 attempts still stop it. Do not resume the campaign.
224 build passed and guarded settings/copy completed. Loaded14:19:01.387,
dispatched14:19:04.727, Stage2 match118 saved14:19:12.523 and entered14:19:19.793.
This is the first current-Mammet farming acceptance match. Preserve this run.
Source-only225 corrects a reviewed lifecycle gap: Start's existing-menu resume
could skip ContinueWeeklyGoal and queue after an already completed target.
Reuse completion handling at Start (outside duty with no pending admission)
and before challenge selection. Completion closes only the observed challenge
menu. Weekly Run from pause preserves campaign attempts; only campaign Resume
resets them. Campaign limit text also includes a currently missing roster item.
Intent: build/test225 independently while224 runs; HOLD deployment until the
ten-win test reaches a resolved completion or a concrete correction requires it.
Then use225 to verify completed-target reload does not admit an extra match.
225 first build and824 tests passed. Final source adds the same early-menu guard
for unavailable CPU rewards (prevent ordinary-challenge substitution) and links
the exact Gentleman-only guide in the stage preview. Rebuild before any copy.
118 WON14:21:50.309, credited100/28; closure14:21:53.940.119 admitted14:22:06.801,
entered14:22:14.215. No reload during this streak. Local Questionable has21 ARR
Hildibrand routes, but Her Last Vow502 includes a disabled automated duty step
for85 (Battle in the Big Keep). That source does not prove an unattended minion
acquisition route. No Hildibrand quest was started or changed.
Final225 build passed, still undeployed.226 now includes a bounded Debug-only
CPU rewards menu inspection for later independent tournament research: the
selected character may approach ENpc1011594, interact once, capture the native
dialog after3seconds, then stop. Maximum60seconds; no registration, claim or
admission. Exact NPC ID backed by XIVAPI ENpcBase event2949121;1010479 is the
unrelated Triple Triad Recordkeeper and1011609 has no event. Live NPC/menu
contract remains unverified. Release CPU rewards remains unavailable. Build226
without copying while the current224 farming streak continues.226 supersedes
the held225 candidate and includes its goal/attempt lifecycle corrections.
226 build and diff checks passed. Still deployed224; no tournament interaction
or reload performed.119 WON14:24:46.410 (101/29),120 WON14:27:42.176 (102/30):
three consecutive current-Mammet Stage2 victories.121 entered14:28:05.391.
226 all824 tests passed.121 WON14:30:37.925 (103/31),122 WON14:33:33.477
(104/32): five consecutive Stage2 victories.123 entered14:33:57.485.
Asked the user whether to extend this test character's scope to ARR Hildibrand
quests/trials for Wind-up Gentleman or wait for him to provide it. This decision
is pending: do not begin that quest chain or infer approval from elapsed time.
Independent farming and tournament-control research remain authorized.
123 WON14:36:28.712 (105/33),124 WON14:39:23.143 (106/34),125 WON14:42:19.972
(107/35). Eight consecutive Stage2 Mammet victories under224;126 entered
14:42:43.959. Source/built226 is held. Keep target37 and preserve current126.
126 WON14:45:16.617 (108/36), ninth consecutive Stage2 win.127 admitted
14:45:33.360; preserve this final required match before any deployment.
127 WON14:48:13.181, credited109matches/37wins. Target completion14:48:21.907.
Ten consecutive Stage2 Mammet wins are verified for118-127 on224, no intervening
loss/unresolved result/reload. The final fight's native input frames repeatedly
show foreground=False. This satisfies the current-roster farming streak and
exact stopping from partial weekly progress27->37; no128 admitted.
Intent: guard127/pending0/109matches/37wins/target37/run10/limit30/losses0/
campaignfalse/stage23-attempts3/mask4194303/spent4800+40000/reservationnull and
unchanged caps/reserve, then deploy the built/tested226 without config edits.
Require fresh226 and immediate completed-goal handling with no extra admission.
226 loaded14:49:29.996. Fresh observation14:49:33.167: territory388/playingfalse.
At14:49:33.182 it went directly Idle->Complete for target37; no128 admitted.
This verifies completed-target reload from the idle world; an open-menu variant
has source guards but is not independently proven by this observation.
Intent227: compile the updated Stage2 verification text and new settings marker.
Guard the same127/pending0/109/37/target37/campaignfalse/stage23-attempts3/
caps/spending/reservation facts. Change ONLY mode WinTarget->CpuRewards for the
already prepared60second NPC-menu inspection, then push227. No battle, signup,
reward claim or additional spending is implemented by this diagnostic. Return
to the retained campaign blocker after resolving the captured menu contract.
227 built and copied after exact guards; mode alone changed toCpuRewards.
Loaded14:51:07.390, inspect state14:51:15.638, one navigation request to the
recordkeeper79.88/0.44/49.00 at14:51:15.670, then bounded timeout14:52:15.661
before any interaction. No registration/claim/battle/result occurred. Source
used a2yalm cutoff despite GameHelpers.GetValidInteractionDistance returning4
for EventNpc (the same mismatch previously corrected at the Minion Trader).
228 reuses that helper and captures nearby objects on the bounded timeout.
Intent: build/diff-check then push228 to repeat only this corrected inspection;
preserve127/pending0/109/37, campaign23-attempts3 and all spending/caps.

Build218 preparation (2026-09-26):217 loaded00:06:18.171/resumed00:06:21.224.
Stage23 match113 LOST00:09:44.104, credited96/27; closure00:09:47.535.
Third Nero match114 admitted00:09:57.688, entered00:10:07.797. Preserve it.
217 sends main-army pursuit independently, but Nero's army still collapses near
6000bossHP. Guide reread: stage23-the-binding-coil recommends immediate pressure,
streaming replacements and supportive specials. Native/guide Nero special only
triggers enemy traps; it does not contribute this encounter's damage or defense.
Observed114 wasted repeated5second movement sequences with no queued replacement
before filling its army.218 keeps two summon requests in flight before pursuit
and extends existing Cargo party-readiness selection to Stage23. Return to the
owned MSQ Airship, whose earlier best attempt left1680HP; no new purchase needed.
Intent: build218 through vmx.bat and run existing tests, but hold deployment until
114 resolves. If it loses, require the normal three-attempt pause before applying
a guarded reviewed development Resume. Preserve all result/spending facts and
50000gil reserve. Source marker218, loaded217 until verified otherwise.
114 LOST00:15:06.472, credited97/27; closure00:15:10.006 and normal campaign
limit pause00:15:20.128. All three Nero attempts lost. Intent after218 checks:
guard sequence114/pending0/97matches/27wins/mask4194303/stage23/attempts3/
losses3/pausedtrue/spent4800gil+30000MGP/reservationnull. Clear only Resume's
run/stage attempt counters, losses and pause. Keep caps223588/30000 and reserve
50000 unchanged. Push218 for a fresh corrected Airship opening.
218 strategy build PASSED; all823 tests passed, including production-priority
and closed-gate pursuit assertions. Rebuilding once to include the standalone
window's corrected roster explanation, with no further strategy changes.
Final218 build/diff checks passed. Exact guarded Resume and DLL copy completed;
matches97/wins27/mask4194303/spent4800+30000/caps/reserve unchanged. Verify218
startup before evaluating the new Airship attempt. No running build/test session.
218 loaded00:18:15.911/dispatched00:18:19.215. Fresh Stage23 match115 admitted
00:18:24.279, entered00:18:34.154; first two Airship summons00:18:40/41.
No result yet. Preserve this full-opening test and its normal attempt limits.
115 on218 reached3300bossHP00:22:37, then2680HP00:24:36. Native00:22:15/17
snapshots show eight Airships actually in melee with Cargo ATK Up but mostly
low HP; the army collapsed00:22:20-38. This is not another projection failure.
219 source adds one bounded recovery using existing gate movement/healing:
with six or more Airships and aggregateHP<=55%, retreat before collapse; refill
to nine and wait for95%HP within6yalms, then attack again. Existing90second
bound retained. Guide uses healing support; this owned-roster adaptation avoids
another minion requirement. Build219 now; reconcile115 before deployment or
record adoption of its normal successor. No attempt/pause/purchase resets.
219 build/diff checks PASSED.115 is still active00:26:08 at2620bossHP with
eight living Airships after replenishment. Intent now: deploy the correction
into this same115 to rescue the observed attrition failure, preserving its saved
admission and first-attempt count. No new match, result, spending or Resume edit.
219 loaded00:26:32.681/resumed115 at00:26:35.775. Recovery triggered00:26:41.848
with boss2220HP, but115 LOST00:27:05.161 before completing healing; credited
once98/27, closure00:27:09.284.116 admitted00:27:19.411, entered00:27:29.370.
This second attempt is the first full opening with early HP-triggered recovery.
Preserve116, attempts2 and unchanged spending. No more source changes after219.
116 positively verified early recovery: all nine retreated00:29:56.546 at9500
bossHP, met the full-army/95%HP/near-gate readback00:30:29.870, then reengaged.
Cargo fired00:30:50,00:30:58 and00:31:11; boss6800HP00:31:18. No result yet.
116's healed army still lost members rapidly against accumulated adds, with boss
4960HP00:32:40. Source-only220 prepares Zu Hatchling83/item7565, permanent
Minion Trader10000MGP, cost10/450HP/30ATK/25DEF/speed3/area auto-attack; Nasty
Peck buffs its party by60ATK for10seconds. Verified via XIVAPI Companion83,
CompanionTransient83, Item7565 and ffxiverminion.com/minion-83; vendor price via
consolegameswiki Minion_Trader. This adapts the guide's rush to the observed add
attrition with a common vendor minion. Extend existing exact exchange guards and
cumulative tests; no new purchase mechanism. Stage23 returns to uninterrupted
pressure (Odin's recovery unchanged). Build/test220 while219 continues; do not
deploy the new roster into an active Airship battlefield. Reconcile the result
boundary and any successor before purchase/setup. Spending cap would need only
30000->40000 under existing MGP authorization; reserve50000gil remains unchanged.
116 timed out00:37:29.417 with last boss1740HP; no result recorded and its
admission abandoned. Facts remain98/27/mask4194303, attempts2.220 build passed;
822/823 tests passed. The remaining test's accessible-minion allowlist still
omits83; the new cumulative-MGP and queue cases passed. Correct that fixture
and rerun.220 was copied before this test result was inspected; no fresh battle
or purchase is authorized by that copy, and the exhausted cap remains unchanged.
Intent: deploy220 to reconcile the old battlefield/result with116 ineligible,
without raising the MGP cap yet. The new missing Zu requirement and exhausted
30000cap prevent new purchase/admission outside battle. Do not recreate116 or
reset its attempt. Require fresh native setup/result evidence before raising the
cap for the already authorized10000MGP purchase and next campaign attempt.
220 loaded13:57:02.639 after the quota interruption. Native13:57:05.767 is
territory388/playingfalse/queueNone;116 has ended with no credited result.
Setup blocked13:57:15.905 before purchase/admission for missing Zu and exhausted
30000MGP cap. Corrected the existing test allowlist for83; all823 tests now PASS.
Fresh config: sequence116/pending0/98matches/27wins/mask4194303/attempts2/run2/
losses1/unpaused/spent4800+30000/reservationnull; funds268788gil/602432MGP.
Intent: compile221 marker, then guard those facts and raise only MGP cap30000 to
40000 for one Zu purchase under the user's existing authorization. Preserve attempt2,
all results, spending, gil cap223588 and reserve50000. Push221 and require its
fresh marker, exact10000MGP receipt and registration before the third admission.
221 build/diff checks passed; guarded cap-only update and DLL push completed.
No counters, pauses, receipts or other currency settings changed.
221 loaded13:59:30.979/dispatched13:59:34.324. Zu offer row/index1/item7565
verified13:59:43.438 at10000MGP/balance602432, submitted once13:59:43.449.
Exact receipt13:59:45.453 (0gil/10000MGP), inventory use13:59:46.453, registration
verified13:59:53.505. Cumulative spending4800gil/40000MGP; expected balances
268788gil/592432MGP. Next admission uses the remaining third attempt; no reset.
117 admitted14:00:04.544, entered14:00:14.584. Six initial Zu summons verified
at60capacity; first wave ordered14:00:37.867. This is the third campaign attempt;
preserve its stop limit. Source-only UI/changelog now show the verified purchase,
but loaded221 is unchanged. Hold further reloads for actual corrections/evidence.
117 at14:02:04 has19Zu and boss15230HP. Source reveals party-readiness probes
still require full capacity/no pending queue (inherited from nine Airships).
The24-unit army loses members before filling, leaving charged older parties
unexamined while replacement selections dominate.222 allows Stage23 probes only
when action=Wait even below capacity/with pending summons, keeping production and
movement decisions ahead of probes. Existing native readiness still gates casts.
Intent: build222 and deploy this correction into117 with unchanged roster,
admission, attempts3,98/27 and all purchase facts. Include UI purchase evidence.
222 build/diff checks passed. Deploy now to the existing117; no config edits.
Latest823 tests cover unchanged queue, roster, lifecycle and purchase accounting.

Build 211 preparation: Stage23 match109 LOST23:32:17.681 with Twintania1680HP;
credited92matches/27wins and closed23:32:21.272. Match110 admitted23:32:34.093,
entered23:32:41.348 and is still active on209 (second attempt). Mask4194303.
Inspected23:31:40/41 movement snapshots: the four reserve Airships have no
logged slow statuses. Guide stage23-the-binding-coil explicitly recommends
immediate pressure and streaming replacements. Source holds lone full-capacity
reserves60seconds and requires four deployed units to pursue.211 fixes Stage23:
send reserves after15seconds or once full with no pending queue, pursue with any
survivor after5seconds and refresh after a2-yalm boss move. Add focused regression.
Build through vmx.bat and test; do not reset match/campaign counts.210's prepared
Stage24 observation remains included. Deploy only after resolving110, or record
a concrete correction of the same live battle. Preserve the third-attempt stop.
211 build PASSED. The parallel test compile conflicted with the solution build's
test assembly write; rerun tests sequentially against the completed build.
110 LOST23:39:05.108, credited once93/27. Later snapshots prove its waves reached
the boss; no evidence supports changing background control or buying a new minion
yet. Intent after tests pass: deploy211 at this resolved result boundary,
preserving any111 admission and third-attempt limit. No progress or cap edits.
All823 tests now PASS. The new fixture needed the same initial roster observation
as production before summon requests are allowed; corrected the fixture only.
Proceed with the recorded211 DLL push, no config changes or extra admission.
211 loaded23:41:03.295 and resumed Stage23 at23:41:06.391. Its first observed
order23:41:12.464 sent the one waiting Airship with eight deployed at225/240,
confirming the full-capacity delay is removed. Preserve active third attempt111.
111 LOST23:45:44.990, credited94/27; closure23:45:48.737 and normal three-attempt
pause23:45:58.901. Boss3980HP before result. Reinforcements now deploy in small
waves and reach the boss, but the neutral roster still loses the damage race.
212 replaces Stage23 with permanent-vendor Wind-up Nero tol Scaeva (poppet,
cost20/ATK50/DEF60/speed3), following the guide's poppet rush. Native load snapshot
23:41:06 lists34 owned minions with no174. XIVAPI Companion174 and Item14096
confirm the mapping; consolegameswiki Wind-up_Nero_tol_Scaeva lists30000MGP at
this same Minion Trader. Implement the ECommons ShopExchangeCurrency basic
reader (count4, funds86, currency icon87, item1066+i, cost456+i, index1310+i),
requiring live MGP icon/balance, item and exact price. Confirmation stays narrow
until observed. Preserve reservation and receipt safeguards for both currencies.
Intent: build212 and test, then guarded development Resume from111/pending0,
94/27/mask4194303/attempts3/spent4800/0/pausedtrue. Raise only MGP cap0->30000
under the user's existing authorization; preserve223588gil cap and50000reserve.
No invented receipt, ownership or battle credit. Require fresh212 and native
purchase evidence; unknown confirmations remain blocked without resubmission.
212 build and all823 tests PASS; diff check passes. Fresh saved state confirms
pausedtrue/111/pending0/94/27/attempts3/runAttempts8/losses3/spent4800/0, no
pending purchase, gil cap223588/reserve50000/MGP0. Apply only the recorded MGP
cap and reviewed Resume fields, then push212. Release version stays0.5.0.3.
212 dispatched23:50:12.238, reached MGP shop23:50:22.369 and rejected the quote
before reservation/submission. Native3325 values: count4=8 UInt, balance86=632432
UInt, icon87=65025 Int. The reference reader assumed UInt;213 uses the observed
Int and adds bounded offer-row diagnostics on rejection. Native row4 is Nero,
displayed30000MGP, AgentShop receive item14096/count1. Build/push213 to correct
this concrete setup failure only after confirming no pending purchase/match;
do not reset counters, caps or pause again. Funds268788gil/632432MGP.
213 build PASSED; previous823 tests cover unchanged strategy/accounting. Fresh
guards confirm111/pending0/no purchase/spent4800/0/cap30000/reserve50000 and
unpaused. Push213 now; no config edits. Native exchange contract still pending.
213 loaded23:52:17.353, verified row4/index4/item14096/30000MGP at23:52:35.607
and reserved/submitted once23:52:35.617. Confirmation was rejected23:52:36.605:
MGP uses prompt "Exchange 30,000 MGP for the following item?", separate name15,
item14=14096 UInt, and visible quantity atSelectYesno/3/2/7=1. ButtonsYes8/No11.
214 validates those observed fields exactly (decode SeString before comparison).
Preserve the existing reservation and funds268788/632432; DO NOT resubmit or
clear it without positive cancellation/receipt evidence. Build/push214 to fix
confirmation; the reload's standard cleanup may cancel the visible prompt.
Reconcile that outcome explicitly before any new request, as for Mammet201/202.
214 build PASSED and diff check passed. Fresh config guards confirm the same
pending14096/30000/before632432MGP/268788gil and zero MGP spent. Push214 without
editing the reservation, counters or budgets; observe confirmation or cancellation.
214 loaded23:55:36.563 and refused the missing confirmation at23:55:39.623.
The213 failure cleanup fired ShopExchangeCurrency(true,-1)23:52:36.605 after
rejecting the prompt; no Yes dispatch occurred.214's fresh start observation
has no visible SelectYesno, still territory388/playingfalse, and native funds
remain268788gil/632432MGP. The checkout was cancelled without payment.
Intent: compile marker215, then clear only that exact cancelled reservation
under111/pending0/94/27/mask4194303/spent4800/0/before268788/632432 guards and
push215 for a new checkout using the corrected confirmation. Preserve all
attempts, caps, pause and purchase totals. Do not add an automatic reservation
reset; this reconciliation relies on the captured checkout and cancellation.
215 build/diff checks passed. Exact guarded reservation cancellation and215 DLL
push completed; no spending or battle facts changed. Await native confirmation,
receipt and registration before evaluating the poppet strategy.
215 loaded23:58:12.368; fresh quote and one submission23:58:30.572. Positive
receipt23:58:32.586 confirms item14096 acquired for30000MGP with no gil change;
native inventory use23:58:33.588 and registration verified23:58:40.677. Spending
now4800gil/30000MGP, reservation null, balances268788gil/602432MGP before any
later match rewards. Stage23 setup resumed23:58:45.723. Preserve next admission
112 and normal three-attempt limit. Source-only UI now records the MGP purchase
and registration evidence; no reload needed for those text changes alone.
112 is active on215, Stage23 first Nero attempt. Saved94/27, pending112/duty574,
stage attempts1 and confirmed spent4800/30000 with null reservation.216 source
adds Stage24 to the existing geometry-checked minimap route (same live gate/stone
validation) and bounded map captures, alongside UI purchase evidence. Build216
now but retain215 until112 settles; do not reload it for UI updates alone.
216 build PASSED.112 has twelve Nero units at full240 capacity by00:00:30 and
boss12024/18000HP at00:01:24; no result yet. Keep deployed215 until the result.
112 midfight evidence exposes a coordination bug: each replacement wave resets
bossOrderUtc/Position for the older army, even though only gate units were
selected. At00:02:42 Twintania is0.686,2.762, old Nero units remain2.334,3.036
after its prior2.365,3.128 position, and the six-yalm arrival radius treats them
as in contact. Boss stalls near6500HP while adds kill them.217 keeps Stage23's
main-order time/position when reserves are sent (unless no deployed army), uses
one-yalm boss displacement and1.5-yalm arrival radius. Other bosses unchanged.
Build217, then deploy this correction to the same112 if still active or its
ordinary successor; preserve admission, stage attempts and every purchase fact.
217 build/diff checks PASSED.00:05 snapshot shows the next Nero battle has
started on215 (boss17184HP at00:05:48); reconcile112's explicit result, then
push217 to correct the pursuit logic in that existing successor, without reset.
112 LOST00:04:13.413, credited once95/27; closure00:04:16.923.113 admitted
00:04:27.027 and entered00:04:36.986. Push217 now for this second Nero attempt.

Build 206 preparation: Stage20 match101 explicitly LOST22:56:00.274, credited
once to84/24; closure22:56:03.925. Match102 admitted22:56:16.810 and entered
22:56:23.809, second campaign attempt. The first Mammet run destroyed B/C but
left A until late. Side-group commands took 10-12seconds of keyboard camera
travel; the guide stage20-guiding-light explicitly permits Mammets in its split
stone opening. Extend existing observed minimap framing to ordinary stone stages
with unchanged geometry/readback guards. Compile source through vmx.bat while
the running205 stays untouched; preserve pending102 and its attempt limit.
Deploy only at a reconciled result/idle boundary, or with a separately recorded
reason to correct the same live attempt. No pause/counter/budget changes yet.
206 build PASSED through vmx.bat, all821 tests passed, diff check passed. Match102
explicitly LOST23:00:14.584, credited85/24; closure23:00:18.154. The normal loop
admitted third match103 at23:00:30.993 and entered23:00:38.357 before the snapshot.
Intent now: push the compiled camera correction into that early live attempt,
preserving saved103/duty571/attempts3 and all spending/pause facts. This corrects
the observed slow opening; it is not a new admission or reload-only experiment.
Require fresh marker206 and adoption of103. A loss still reaches the normal
three-attempt stop. Do not reset its accounting for this deployment.
206 loaded23:01:39.670, adopted103 at23:01:42.743. Match103 LOST23:03:51.557,
credited86/24; closure23:03:55.274; the campaign limit paused23:04:05.435 with
pending0/sequence103/attempts3. The minimap extension issued no shortcut: ordinary
maps include enemy structures so the Stage9 fixed marker indices are wrong.
207 source matches native minimap rows by friendly kind and live X/Z before
validating node bounds/scale, with Stage20's bounded geometry capture enabled.
The observed row contract is count4 and four-int rows starting5, with friendly
gates768 and stones512; node mapping still needs the live geometry check.
Stage20 source now requires Wayward Hatchling, common2400-gil vendor minion3:
guide/minion-3 confirms60ATK/speed3/cost15/critter, compared with Mammet001's
25ATK/speed1. Keep the split opening with four per lane and actual-cost queue
reservations; use Choco Shuffle near enemies. Native roster ownership is checked
before entry and the existing capped vendor path can acquire it if absent.
Intent: build207 via vmx.bat and run tests; then explicitly apply reviewed
development Resume only from this proven campaign-limit pause, under exact
103/86/24/mask524287/pending0/attempts3/spent2400 guards. Clear run attempts,
consecutive losses and stage attempts just as Resume does; preserve every result,
purchase, cap and50000-gil reserve. This is a justified new strategy test, not an
automatic reset or claim of a Stage20 clear. Expected fresh marker207.
207 build PASSED and all822 tests passed. Guarded development Resume and DLL
push completed without changing results, budgets or reservations.207 dispatched
23:07:42.322. Wayward Hatchling was missing: one2400-gil purchase reserved and
submitted23:07:51.477; exact receipt verified23:07:53.478. Registration was
positively verified23:08:01.561, so the inventory-agent registration route now
has live readback evidence. Total spending4800gil/0MGP; expected balance268788gil
if no unrelated spending. New Stage20 match104 queued23:08:15.341. Preserve it
while observing four-Hatchling groups and minimap calibration. No clear yet.
104 entered23:08:22.604, first four-Hatchling order23:08:43.888 and special23:09:03.
The207 marker assumption was rejected by its geometry guards (no map clicks).
23:08:27 native data resolves the mapping: count16, row13=GateA ->node60002,
row14=GateB ->60001, row8=friendlyStoneB ->60007. Nodes are assigned in reverse
row order (60000 + count - 1 - row).208 source corrects that exact mapping;
compile/test now but preserve the active104 until its result is reconciled.
104 LOST23:11:45.609, credited87/24; closure23:11:49.549.105 queued23:12:02.439,
entered23:12:09.743, LOST23:15:27.424, credited88/24 and closed23:15:31.408.
Hatchlings alone barely damaged stones.208 now retains six Mammet attackers per
lane with four Hatchling guards using the existing defensive controller. Require
both owned minions, reserve the defenders' actual60capacity, track both queues,
and use Choco Shuffle only by a selected Hatchling near enemies. The guide's
optional defenders support this mixed adaptation.208 build and all822 tests pass.
Intent: push at this verified105 result transition, preserving any106 admission
that occurs during copy and its third-attempt count. No further Resume/counter,
purchase or pause changes. Both minions were prepared by previous admissions;
native palette validation remains mandatory. Observe the corrected minimap and
mixed formation, then reconcile its explicit outcome before another change.
208 loaded23:16:05.218 and resumed106 at23:16:08.295. First Mammet order23:16:41,
four inherited Hatchlings sent to defend23:16:49, Choco Shuffle23:17:03. Still no
minimap shortcut.23:16:13 proves node IDs persist while count rises16->25, so
reverse total-row mapping was also rejected.209 source removes that assumption:
derive candidate centers from observed collision16 bounds (175x210 covering
50x60 world units), match visible marker components at current GateA/B/StoneB
positions, then independently validate their horizontal/vertical scale as before.
This is supported by map center497,301, GateB497,378 atworld0,22 and StoneB497,308
atworld0,2, each3.5px/yalm. No raw node pool/array index correspondence is assumed.
209's first compile caught Bounds member names and a captured address; corrected
using declared Width/Height and separate center locals. Final build pending.
Do not push until106 outcome or a recorded reason; preserve third-attempt limit.
106 WON23:19:26.645, explicit You Win, credited once89matches/25wins; friendly
center stone1360HP survived. All enemy stones0. Closure23:19:30.625. Stage20 mixed
Mammet/Hatchling roster has one observed clear (opening inherited four Hatchlings
from207 before208 supplied Mammets, so its fresh-start ordering still needs a test).
Mask should advance1048575 (stages1-20); no result was invented for any loss.
209 final build PASSED. Intent: push the compiled camera-marker correction during
this result transition, preserving any new Stage21 admission and its saved facts.
No settings, spending, pause or attempt reset. Stage21 remains the current guide
Mammet baseline; inspect its result before selecting any new composition.
209 loaded23:20:09.167, adopted Stage21 match107 at23:20:12.232. The corrected
minimap finally dispatched23:20:52.932 with verified3.5px/yalm, followed by
selection/movement readbacks23:20:55.818/23:20:57.101. Enemy B3140HP then, with
all friendly stones still positive. Preserve this attempt; no result yet.
Source-only UI text now records Stage20's actual mixed-roster clear caveat and
both verified vendor purchases. It is not yet rebuilt/deployed (next marker210).
Stage21 match107 WON23:22:28.453, credited once90/26; closure23:22:32.072.
Enemy stones0 and friendlyC5000HP at result. Multiple map clicks (23:20:52,
23:21:47,23:22:08) led to native selection/movement and a positive result; the
geometry-based marker lookup has live proof. Expected mask2097151, next Stage22.
Source-only UI now also reports Stage21's one-clear evidence. Compile210 now,
but do not push over Stage22 merely for these UI text changes. No gameplay,
configuration or pause changes are needed for the current approved Mammet test.
210 UI candidate built successfully, not pushed.209 continued Stage22 match108:
queued23:22:44.912, battlefield23:22:52.245, WON23:25:24.426, credited once91/27.
Friendly stone HP660/2410/4715, enemy all0. Closure23:25:28.337 and Stage23 setup
23:25:33.385. Expected mask4194303 (stages1-22). Source-only UI/tactics now also
report the Stage22 Mammet clear; these latest text edits are not in the first210
compile. Keep209 running for Stage23's Airship attempt; Stage24 remains gated.
210 source now prepares the next necessary Stage24 control proof. Ordinary and
release admission remains blocked; only a Debug build with the existing saved
verminion_queue selection bound to the current character can enter the existing
120-second observation. Capture first-seen native casts (8 action IDs maximum)
and EventObj types (24 maximum), plus the existing15-second snapshots. No guessed
tower/circle logic or completion claim. The normal timeout abandons the pending
match, and the three-attempt limit is preserved. This is within the authorized
single-client research loop, not a new debug setting, script, or logger. Compile
the candidate now; deploy only after Stage23 settles or another explicit reason.

Latest authorization: the user approved using this character's gil as needed while
leaving 50,000 gil, and MGP as needed. This resolves the pending Mammet budget
question; do not ask again. Add the gil reserve to the existing purchase settings
and enforce it at each spending boundary. Expected build 197, same 0.5.0.3.
Deploy 197 with caps still zero first, so the loaded plugin understands the new
reserve field before any spending is enabled. This also captures actual funds.
After confirming that load, guarded settings update on the selected character will set
gil reserve 50,000 and cumulative gil cap 223,588 (fresh 273,588 balance less
the reserve; actual funds still bound every purchase). MGP cap stays zero until an implemented route needs
it; no MGP minion is required for Stage 20. Preserve pause, sequence 100, pending
0, clear mask 524287, 83/24, and all purchase receipt facts. Then use a marker-only
198 build/push to reach the existing one-Mammet purchase path. This prevents the
older 196 schema from dropping an unknown reserve setting during unload/save.
Native shop contract, item registration and the resulting Stage 20 admission
remain unverified. No repeat submissions for unresolved reservations.
197 build PASSED via vmx.bat; all 821 tests passed, including exact reserve
boundary, changed balance/reserve, reload/copy persistence and no battle credit.
197 DLL pushed after fresh guards confirmed the idle sequence100/83/24 state
and zero caps. No configuration changed for this first schema-loading push.
197 loaded 22:24:51.671 and exposed the reserve field; zero-cap roster rejection
again left sequence100 unchanged. Native funds at 22:24:54.903 are 273,588 gil,
631,582 MGP and zero Mammet items. Marker-only 198 build PASSED. Intent now:
change only selected-character gil cap 0 -> 223,588 and reserve 0 -> 50,000,
then push 198. Preserve all other file text, pause, progress and purchase facts.
198 loaded 22:26:50.743, settings persisted with cap223588/reserve50000/MGP0.
Shopping began 22:26:59.041 and timed out 22:29:59.070. No reservation or submission
was logged; last saved progress remains sequence100/pending0/spent0/83/24.
Intent for 199: capture native vendor/menu state once at ten seconds or rejected
shop readback, and honor the existing navigation API's rejected dispatch instead
of marking it requested unconditionally. Same approved budget and reserve; no
new accounting identity or source strategy change. Build/push only after checking
there is still no unresolved purchase or match. Diagnose this failed setup path
before claiming purchase success. Preserve MGP and other character settings.
Historical native menu snapshot at 22:26:53.981 confirms table entries Challenge,
Battles, Tournament, Play Guide, Cancel. The timed-out path used repeated raw -1
callbacks. Before first 199 deployment, use the observed native Return entry to
leave the challenge list and Cancel to close the parent table menu, then navigate.
Keep the ten-second capture to verify the actual location/menu if it still stalls.
199 loaded 22:33:19.640. The native Return/Cancel route exited successfully:
22:33:43.869 snapshot shows available=True, setupMenu=False, no target, player
(80.665,-0.000001,42.666), trader (82.414,0.438,44.938), about 2.90 yalms apart.
Navigation stops there but the shop's 2.5-yalm threshold prevents interaction.
The existing EventNpc helper accepts 4 yalms. Intent for 200: use that same
GetValidInteractionDistance value in the shop approach gate. Build/push with
fresh no-reservation/no-match guards; no budget, count, or pause changes. This
corrects the observed setup stall before purchase, not a battle retry/reset.
200 verified the approach correction: trader interaction succeeded 22:35:55.052.
Native Shop 22:35:57.024 shows three entries, Mammet at row1 for2400 gil, balance
273588. Generic AgentShop receive/cost counts are both0, so the guarded quote
reader correctly rejected before reservation/spending. Intent for201: use the
basic gil-shop contract from ECommons/UIHelpers/AddonMasterImplementations/Shop.cs
(upstream NightmareXIV/ECommons): count value2, item IDs441+i, prices75+i,
callback(true,0,index,amount). Locally observed price76=2400 and name15=Mammet.
Require expected item ID, parsed exact name and price together, validate counts/
types/current-stock tab, retain currency/reserve/receipt guards. Capture native
ID fields in the existing bounded shop snapshot. Fresh no-purchase/no-match
guards required for deployment; no changes to cap223588/reserve50000/MGP0.
201 loaded22:39:29.608. Verified native row1/item6004/price2400 at22:39:47.864;
one reservation and one Shop submission22:39:47.874. Pending receipt retains
before-gil273588/MGP631582/items0. Snapshot22:39:53.781 confirms purchase quantity1
and price2400, but buttons are OK(node8,param0) and Cancel(node11,param1), not
Yes/No. No funds or item receipt yet. DO NOT resubmit/clear this reservation.
Intent for202: use observed buttons. On startup, adopt only this existing visible
confirmation when vendor/offer/one-item prompt and saved pre-purchase funds/items
match exactly; proceed through the normal fresh cap/reserve checks and never fire
another Shop callback. Unknown/missing confirmation remains blocked. Preserve
pending reservation and all progress through build/push. Existing201's incorrect
No cleanup does not close this OK/Cancel prompt, so it may remain after timeout.
202 loaded22:43:16.069. The debug hook's required FULL STOP cleanup fired -1
on SelectYesno and Shop at22:43:19.120, before service dispatch; native Shop closure
was observed22:43:19.296. Startup snapshot22:43:19.172 still shows273588 gil,
631582 MGP, zero Mammet items, and no owned Mammet. There was never an OK/Yes
confirmation click for the sole201 Shop submission. Consequently the exact
reservation is now a positively cancelled checkout, not a paid receipt.202
correctly refused an absent confirmation and left it reserved; no retry occurred.
Intent before203: under exact selected-character/cap/reserve/sequence/facts and
reservation guards, clear only this cancelled PendingPurchase (no spending,
match, victory, pause, or ownership edits). This is a one-time development
reconciliation supported by the observed cancellation and unchanged receipt;
do not add automatic unknown-reservation resets. Marker-only203 should then
start a fresh checkout with the corrected native OK/Cancel actions. Keep the
standard unresolved-reservation refusal for missing evidence.
203 loaded22:46:09.633. Fresh row1/item6004/2400 quote22:46:27.938, reservation
and one Shop callback22:46:27.962, native OK22:46:28.952, receipt verified once
22:46:29.971. Exact receipt means gil271188, MGP631582 and one acquired Mammet.
Persisted spent2400/MGP0, pending purchase null; all campaign facts still100/83/24.
Registration at22:46:30.069 was rejected with action status579 (LogMessage:
"Cannot execute at this time."); native Shop closure followed22:46:30.172.
Intent for204: reuse GameHelpers.GetItemActionStatus before issuing minion use,
check at one-second intervals within the existing300-second registration bound.
No repeated use when not ready. Native acquisition is verified; registration and
Stage20 admission remain pending. Build/push with spent2400/pending-null guards;
never repurchase this item. Update standalone purchase-evidence text accordingly.
204 loaded22:49:13.453; native funds271188/MGP631582/MammetItems1 at22:49:16.548
verify the retained purchase. Item status was ready, but shared ActionManager
UseAction(Item,6004,extraParam65535) returned False22:49:16.593. No registration
or admission occurred. Intent for205: use declared AgentInventoryContext.UseItem
for the exact verified minion bag/slot, then the existing seven-second positive
unlock check. This replaces the item-use route only for Verminion, with no
unknown-outcome retry and no purchase duplication. Slot/ownership/853 item-action
guards remain mandatory. Build/push with spent2400/pending-null/sequence100 guards.
205 loaded22:52:07 (see fresh startup record), native setup at22:52:10.538 has
271188 gil/MGP631582/MammetItems0 and the roster gate accepts owned Mammet.
No205 inventory-agent use request was logged: ownership became registered before
its startup, so do not claim that new item-use route has a native execution proof.
The earlier ActionManager false return was not positive evidence of failure to
register; only subsequent ownership establishes success, irrespective of cause.
Stage20 queue match101 saved22:52:24.368, battlefield22:52:31.733. Six Mammets
were summoned at B and the first objective order sent22:52:53.021; A-gate summons
followed22:53:01. Preserve this live attempt: no build/push merely for diagnostics.
Next evidence target: Mammet opening, stone damage and explicit Stage20 result;
three-attempt campaign limit remains in force. Source/deployed marker205,
version0.5.0.3; latest full suite821 passed before the subsequent native fixes.

Historical result before spending authorization: match 100 WON at 22:09:55.972, credited
exactly once to 83 weekly matches / 24 wins; clear mask 524287 (stages 1-19).
Result closure advanced to Stage 20 at 22:10:04.867. At 22:10:09.896 the roster
gate stopped before admission: Mammet #001 is not registered and the cumulative
gil cap remains zero. Saved pending match is 0, sequence 100, no purchase or
spending, campaign true, pause false. Do not raise the purchase cap without
the user choosing a budget. The Stage 20 guide explicitly supports Mammets; no
rare-minion substitution or unverified result is needed to bypass this gate.

Intent before build/push 196: now safely between matches, correct the expected
native debug path, show the exact Mammet cap blocker, report current-roster
Stage 19 evidence in the standalone window, and avoid carrying Stage 19's attempt
count into the Stage 20 display. Build through Z:\vmx.bat, then copy changed
runtime artifacts to R:\parasite\vmx (DLL last). Verify fresh startup 196 and
one dispatch through the existing hook. Expect Stage 20 to remain blocked with
sequence 100, pending 0, 83/24, caps zero; no gameplay/config/cap mutation.
One Airship victory verifies the Stage 19 clear and Cargo activation, not a
repeatability streak or the full campaign. Tower/AoE behavior on Stage 24 remains
unimplemented; the reread guide requires dodging circles, assigning a fast unit
to every tower, and handling the two Twintania adds. Keep its admission gate.
Retreat evidence resolved: at 22:03:44.718 (buff ended), all nine Airships were
at Z=20.89-23.15 near Gate B, each 465/465 HP; the strategy read all nine ready
and none deployed, then dispatched them against Enkidu. At 22:04:02 they were
back near the center (Z=1.24-2.98). This verifies first-window retreat, recovery
and return, despite the earlier selection mismatch. Build 196 through vmx.bat
PASSED (0 errors; existing NU1601 warning), output version remains 0.5.0.3.
196 delivery VERIFIED: all 820 tests passed; git diff --check passed. The guarded
copy changed only VERMAXION.dll, comparing all 10 runtime files byte-for-byte.
Automatic unload began 22:16:20; fresh startup 196 appeared 22:16:21.851 with
the corrected A:\ff14\parasite\vmx expected path. The existing hook resumed
22:16:25.119, then the Stage 20 roster gate stopped 22:16:30.198 with the new
explicit 2,400-gil cap requirement. Fresh config remains sequence 100, pending
0, 83/24, clear mask 524287, zero caps/spending and no purchase reservation.
Copy-triggered automatic reload is now proven on the new route. No live settings
were changed. The next campaign step needs Mammet #001; ask the user whether to
allow a cumulative 2,400-gil cap for its one purchase/registration. Do not infer
that permission from the existing zero-cap purchase implementation. Other missing
runtime acceptance remains open; full goal is active and incomplete. No running
tool/build processes remain at this checkpoint.

New deployment route selected by the user: create `R:\parasite\vmx` and push the
current plugin there. The user will enable this dev-plugin location. Intent:
copy built marker 195 / version 0.5.0.3, its manifest/deps file, existing runtime
dependency DLLs and output images. Do not change Dalamud's configured paths or
enable/reload the new entry on the user's behalf. Future authorized iterations
can build with vmx.bat then copy the updated artifacts to this explicit folder.
No new deploy script, dependency, backup, logger or persistent mechanism added.
The gameplay Resume remains prepared (pause false, sequence 99, pending 0,
campaign Stage 19, zero purchase caps); enabling the selected debug plugin can
dispatch it. The old Z: development entry should be disabled before enabling the
new entry so only one VERMAXION instance owns the run. Source build still logs
its historical expected Z: path; native load diagnostics must confirm the newly
selected route. No new build is required just to copy the existing verified DLL.
Copy completed: created the requested folder and images subfolder; copied and
verified all 10 runtime files by direct byte comparison (no hashes). DLL and
manifest both report 0.5.0.3. Target: `R:\parasite\vmx\VERMAXION.dll`, marker 195.
Await the user enabling that location before runtime verification. No client settings
or source code were changed by this deployment; no plugin was enabled by the agent.
New route ENABLED by the user and verified: client-native path is
`A:\ff14\parasite\vmx\VERMAXION.dll` (agent view `R:\parasite\vmx\VERMAXION.dll`).
195 loaded 22:01:04, consumed/dispatched once 22:01:07, verified Stage 19 queue
match 100 at 22:01:22, entered battlefield 22:01:34. AutomaticReloading is true
for the new entry. The load/dispatch blocker is resolved. Preserve match 100;
do not rebuild/push over the active attempt merely to test reloading. Observe the
Airship opening, selection/movement, Cargo, buff retreat and explicit result.
The old blocked-audit entries below are historical. Full objective stays active
and incomplete. Copy-based automatic reload still needs its first observed push.
Match 100 opening evidence: Airships summoned at 25 capacity each, first wave
sent 22:02:07, all nine deployed by 22:02:48 at 225/240 capacity. Cargo requested
22:02:29, 22:02:56 and 22:03:04; subsequent native snapshots show ATK Up (962)
on the friendly Airships. Gilgamesh reached 12,344/16,000 HP at 22:03:08.
Buff-phase detection immediately requested gate retreat; a selection mismatch
appeared 22:03:09. Retreat arrival/survival and outcome remain unverified. Preserve
owned match 100 while investigating this bounded phase observation.

## Resumed after reboot — 2026-09-25

Current resumed run: the user manually reloaded and explicitly resumed the goal.
Fresh evidence confirms 194 loaded 21:47:59 and respected the saved pause at
21:48:02. The unavailable-load blocker is resolved; do not carry its audit forward.
Fresh selected configuration: debug verminion_queue, pause true, mode WinTarget,
target 10, campaign true, stage 19, sequence 99, pending 0, attempts 0/0, losses 0,
totals 82/23, clear mask 262143, caps/spend zero, no pending purchase.
Intent before mutation: apply the established development Resume only under those
exact guards. Budgets are already reset; change only this character's pause to
false, preserving all other file text and facts. Build marker 195 through vmx.bat
to dispatch once through the existing debug reload hook. Verify load/dispatch and
new owned admission separately. Test Stage 19's one-Airship-group defense, Cargo,
background selection and buff retreat; no OS focus/cursor/global input changes.

195 watched build PASSED via vmx.bat, DLL timestamp 21:51:28, zero errors and the
existing NU1601 warning. Guarded Resume changed only the selected character's
VerminionPaused true -> false; exact file text otherwise preserved. Attempt
budgets were already zero and remain so. Fresh saved state still sequence 99,
pending 0, campaign true, stage 19, 82/23, caps zero: no new admission or result.
Bounded post-build evidence shows 194 remains latest startup; main log last
updated 21:49:31, preceding the build. Client settings still enable the exact Z:
DLL path and AutomaticReloading=true. Manual loading works; watched reloads are
still unverified. Next required user operation: toggle VERMAXION automatic reload
off/on and reload it once, to attempt rearming the file watcher after the host
outage. The already-authorized Stage 19 run is now prepared to dispatch at that
reload; preserve its reviewed budgets and results. No further build/log loop.
Goal remains incomplete; this is the first new automatic-reload failure after
the successful manual 194 load, not a continuation of the old blocked audit.
195 resumed audit: BLOCKED after three consecutive turns with the same missing
automatic-load evidence (the prepared Resume/build turn and two continuations).
First turn made progress by confirming manual 194 load and preparing 195; neither
continuation observed its load or admission. Latest bounded snapshot: main log
5,017,702 bytes, updated 21:53:42, still only 194 startup and its saved-pause
acknowledgement. Fresh config: pause false, sequence 99, pending 0, stage 19,
82 matches/23 wins, attempts 0/0. No runtime mutation, rebuild, result credit or
running tool handle in these continuations. The reviewed Resume remains prepared
for the requested watcher toggle/manual reload; goal blocked status does not
change that saved run selection. Preserve budgets and facts; never invent an
admission. Need the already-requested user reload operation before meaningful
native control/strategy verification can continue. Full objective is incomplete.

Prior resumed audit: BLOCKED after three consecutive resumed turns with the
same unavailable reload evidence. After the prior blocked status, the controller
became active again and the user confirmed being in-world. The first resumed turn
requested the specific plugin reload; the second and third metadata checks found
the main log unchanged (4,976,890 bytes, 21:39:31) and the 194 DLL unchanged
(2,033,664 bytes, 21:41:34). Last verified startup remains 187. No new log scans,
builds, gameplay or config mutations during those checks. The already-requested
manual VERMAXION reload on R:\XIVLauncher3 is the required external change.
No active tool/process handle exists to justify waiting on a running build. The
full objective remains unfinished; preserve the gameplay pause and verify startup
194 before the established guarded Resume. Do not keep rebuilding or polling.

Prior blocked audit: the unavailable reload evidence has persisted across the
resumed 192 build turn, the 193 setup implementation/build turn, and the 194
post-reconnect build turn. This continuation revalidated the same condition:
last startup 187, latest main-log timestamp 21:39:31, with no 192-194 startup.
Source and watched DLL are 194; no process is awaiting build completion. The 193
setup change was meaningful source progress and its full 820-test suite passed,
but native setup, controls, campaign and tournament verification now require an
actual current plugin load. No alternative runtime transport is authorized.
Fresh config remains paused, sequence 99, pending 0, totals 82/23, clears 1-18,
stage 19, attempts 0/0, caps zero, no pending purchase. No gameplay/config change.
Mark goal BLOCKED pending the already-requested manual VERMAXION dev-plugin reload
on R:\XIVLauncher3. On resume, verify startup 194 and saved state before the
established guarded development Resume. Do not repeat rebuild/log loops or claim
the overall objective complete. No additional source or artifact work is pending
for this setup change; live acceptance and remaining feature work are unfinished.

Previous runtime intent: the user explicitly confirmed in-world after reconnect at
approximately 21:40. Prior 193 build occurred while reconnecting; latest bounded
log still shows 187, but is fresh through 21:39:31. Trigger one marker-only 194
build through vmx.bat now that world loading has finished. Keep the runtime pause;
do not infer reload success or start gameplay without the exact startup marker.
Source behavior is the already-built/tested 193 setup fix; all 820 tests passed.
194 watched build PASSED through vmx.bat (zero errors; existing NU1601 warning).
Whitespace check passed. No test rerun for this marker-only change. Await the
bounded post-build startup observation; saved gameplay pause remains unchanged.
194 post-build snapshot still shows only startup 187; latest main-log entry is
21:39:31, while 194 DLL was built at 21:41:34. Selected character remains paused,
sequence 99, pending 0, totals 82/23, stage 19, caps zero. No battle/config action
taken. Ask for one manual VERMAXION reload in Dev Plugins on R:\XIVLauncher3;
further rebuilds alone have not provided fresh load evidence. Goal remains
incomplete. Source setup work is progress; native reload/purchase/campaign and
tournament acceptance remain pending. No active build or research process left.

Previous continuation: previous turn made progress by building the requested 192.
the user has now reported a disconnect and reconnection/loading into world. This is
new external-state evidence; do not treat stale pre-reconnect logs as a current
reload failure. Gameplay remains paused until a fresh startup is verified.

193 source corrects the first-entry setup gap: the three-owned-minion gate used
to reject before purchases could help. Reuse the existing shop/reservation path
for only the missing entry minions (Mammet #001, Wayward Hatchling, Cherry Bomb),
requiring the whole entry plan's gil within remaining caps and funds. Distinguish
unknown ownership, missing minions, and insufficient palette slots. Purchases and
registration remain sequential and receipt-based. No runtime caps changed.
Sources: ffxivcollect.com/api/minions/1, /2, /3 confirm each costs 2,400 gil at the
Minion Trader. xivapi/ffxiv-datamining master csv/en/GilShopItem.csv shop 262574
contains only items 6003, 6004, 6005, with no quest or achievement requirement.
Intent: isolated Debug compile and focused/full existing tests; watched 192 is
left intact while reconnection settles. Live entry/purchase verification pending.

193 isolated compile PASSED after fixing a UI local-name collision; full suite
820 passed, whitespace clean. Entry planner regression covers existing ownership,
duplicate/zero IDs, zero/insufficient/exact cumulative caps, sequential receipts,
reload reservations and no fabricated battle credit. Fresh runtime config remains
paused true, sequence 99, pending 0, totals 82/23, stage 19, caps zero and no pending
purchase. Post-reconnect log advances to 21:19:31 but latest startup is still 187.
Next action: watched build via `Z:\vmx.bat` with pause retained, expecting startup
193. Do not apply development Resume until that exact load has been observed.

193 watched build PASSED through vmx.bat, zero errors and existing NU1601 warning;
DLL timestamp 21:24:05. Bounded post-build snapshot still contains latest startup
187 and latest main-log timestamp 21:19:31 (after the reported reconnect, before
this build). The source setup fix is built/tested, but live reload remains
unverified; no development Resume or new match has been dispatched. Need a fresh
193 load, through the watched route or one manual dev-plugin reload, before Stage
19 can resume. No repeated log-watching loop or alternate control route added.

Previous resumed attempt: the user requested a small executable edit and rebuilding
through `Z:\vmx.bat`. Expected startup marker: `verminion-control-20260925-192`.
Intent: run that exact existing script, which builds the solution Debug/x64 to
`Z:\VERMAXION\VERMAXION\bin\x64\Debug\VERMAXION.dll`, then take one bounded
reload evidence snapshot from R:\XIVLauncher3. Keep the saved gameplay pause until
the new load is confirmed. Version remains 0.5.0.3. This is a fresh resumed audit;
the prior blocked state below is historical. No runtime mutation or result claim.

192 requested build completed: `Z:\vmx.bat` PASSED (zero errors; existing NU1601
dependency warning). Watched DLL timestamp 2026-09-25 21:10:39 local; output path
matches above. `git diff --check` passed. No behavior changed since the previously
passing 819-test suite, so tests were not repeated for the marker-only edit.
The post-build bounded main-log snapshot still has startup 187 at 18:53:39 and
saved-pause acknowledgement at 18:55:13; newest log entry is 21:05:02, before the
build. 192 load remains UNVERIFIED. Fresh selected-character config is unchanged:
paused true, sequence 99, pending 0, attempts 0/0, totals 82/23, stage 19, clear
mask 262143 and purchase caps/spend zero. No gameplay or config mutation performed.
Read-only route inspection confirms the Z:\VERMAXION\VERMAXION\bin\x64\Debug
DLL is enabled in this client's dev-plugin locations and AutomaticReloading is
true. Built DLL and manifest remain 0.5.0.3. No routing settings changed. The
requested small edit/build is complete; live acceptance needs fresh 192 startup
evidence before the established guarded Resume. Campaign remains at Stage 19/24.

Prior blocked audit: the same unavailable reload evidence has persisted for three
consecutive goal-work turns (the post-file-host reconciliation and two automatic
continuations). Previous turn was progress: 191 purchase implementation and 819
passing tests. This turn's fresh read still shows only 187 startup at 18:53:39;
log contains unrelated activity through 20:49:31 but no 190/191 startup. The
watched DLL is 190; isolated/source is 191. No build/test process remains live.
Fresh saved state: paused true, sequence 99, pending match 0, attempts 0/0,
82 matches / 23 wins, clears 1-18 (262143), caps zero, no pending purchase.
Independent source changes are built/reviewed; native shop, background strategy,
remaining campaign and tournament work now need actual client observations.
Goal status: BLOCKED pending the already-requested manual VERMAXION reload on
R:\XIVLauncher3. Do not broaden the control route, activate the game, restart a
client, or manufacture runtime credit. After reload, verify the exact loaded
marker and saved pause before deploying 191 and applying a reviewed Resume.
The full objective remains incomplete. No further runtime/config mutation here.

Final 191 isolated build PASSED after the review fixes; whitespace check PASSED.
Full suite: 819 passed. No pending build/test sessions. This continuation made
source/test progress, but the same unverified client reload blocks live acceptance.
191 remains isolated/source only; watched artifact 190 has no verified startup.
Latest verified runtime remains paused 99 / stage 19, totals 82 matches / 23 wins,
clears 1-18, caps/spending zero. No further runtime changes made this continuation.
Goal stays active and incomplete; resume runtime only with fresh client evidence.

Review also removed the early saved-stage roster rejection: refresh the game's
challenge menu/unlocks first, then check minions/palette or start the capped vendor
route before admission. This avoids demanding minions for a stage already cleared
manually since the previous run. UI preview remains based on saved progress until
that refresh. No new live result or reload evidence claimed.

191 isolated build PASSED and all 819 tests PASSED, including purchase caps,
insufficient funds, owned/inventory duplicates, serialization, Stop/reset/weekly
reset, exact currency+item evidence, duplicate receipts and character isolation.
Final review tightened confirmation amount matching (no numeric substring match)
and preserves Verminion facts/reservations/pause during character-settings reset.
Final isolated compile pending for those two review fixes. No watched deployment,
runtime config mutation, purchase, or new battle in this continuation. Need the
manual client reload already requested before native shop/battle verification.

Goal controller now reports ACTIVE. Last turn made implementation/verification
progress; this continuation's bounded reload check still found no 190 startup
(latest other-plugin log timestamp 20:32:36). Continue source work without client
mutation. Watched DLL remains 190, saved runtime pause true, no new admission.

191 source adds the Mammet gil vendor path and per-character pending-purchase
reservation/receipt accounting. A request cannot fire unless reservation saving
succeeds; ConfigManager now exposes the existing save result without changing
its other callers. Verify both item acquisition and exact gil/MGP change, keep
unknown reservations across reload/Stop/reset, and prevent duplicate requests.
Only this required vendor route is implemented; MGP vendor execution and other
vendors remain pending. Zero-cap test settings are unchanged. UI shows reservations
and acquisition limitations. Isolated compile passed before the final save-result
guard; intent now: final isolated build and full regression suite, no watched build.

Fresh acquisition research corrected an earlier assumption: Baby Bat's vendor
requires the FATE Poor Maid's Misfortune at Poor Maid's Mill, not an always-present
vendor at Memeroon's Trading Post. Source: https://ffxivcollect.com/api/minions/26.
Mammet /api/minions/2 confirms 2,400 gil at the Minion Trader; Airship /api/minions/52
confirms the starting-city Envoy quest. xivapi/ffxiv-datamining master csv/en Item,
GilShopItem, GilShop, ENpcBase, ENpcResident and Level rows confirm item 6004,
shop 262574, NPC 1011595 and position (82.4139, 0.411926, 44.9377), territory 388.
Native shop quote checks use the installed AgentShop structure; row/cost layout
must still be observed live and any mismatch fails before purchase. No signature,
dependency, external artifact or independent logger added.

190 watched DLL was produced 20:26:32, but bounded reload diagnostics contain no
190 startup/unload/dispatch. Last verified startup remains 187 at 18:53:39. Log
contains other-plugin activity through 20:26:32, but target-plugin routine output
ends 20:14:30 near the file-host outage. The automatic reload route is unverified;
do not claim a 190 runtime result or infer a crash. No repeated watcher/log loop.
Fresh config reconciliation after the attempted deployment still showed sequence
99, pending 0, attempts 0, totals 82/23, mask 262143, caps/spend zero. Restored only
the saved runtime pause to true under those exact guards, preventing a delayed
reload from starting gameplay. Resume budgets remain reset; no result credited.
Next required runtime action: manually reload VERMAXION once on R:\XIVLauncher3
to establish a fresh 190 startup, keeping the saved pause. Then reconcile state
and apply the established reviewed Resume before the next bounded Stage 19 test.
User-operation is required because the authorized watched route did not provide
load evidence; no foreground control, game restart or alternate transport added.
Source/build/tests remain valid: isolated and watched builds pass, 818 tests pass,
version 0.5.0.3. Standalone window visual verification remains pending. The larger
goal is incomplete; user resumed work, but orchestration status still says paused.

190 final isolated and watched builds PASSED (existing NU1601 warning only).
Guarded Resume matched paused-99 exactly and persisted only the established run
budget resets/unpause, retaining campaign/facts/caps/sequence. Await fresh load and
admission/result evidence. Full suite 818 passed. Do not reload an active attempt.

File-host reboot also completed; both shares and saved paused-99 state rechecked
unchanged. 190 isolated build PASSED and all 818 tests PASSED before that reboot;
review then corrected defender-role identification for Stage 15 and kept service
failure details visible while paused. Whitespace clean. Next: compile final source,
then guarded Resume only on the exact paused-99 state and watched Debug build 190.
Preserve facts/caps/sequence, resetting only the established Resume attempt budgets.
Verify 190 load, admission, Airship movement/retreat, Cargo effect and actual result.
No window focus, OS cursor or global input changes. Goal work was resumed by the
user; the goal-control tool still reports paused (no assistant resume operation).

the user explicitly resumed work after reboot. Fresh bounded evidence confirms saved
paused-99 state survived: totals 82/23, mask 262143, pending 0, attempts 3/3,
losses 3, caps/spending zero. Fresh client log: 187 loaded 18:53:39, then respected
the saved pause at 18:55:13. No runtime/config mutation or Resume yet. Git was
clean at restart (HEAD 9f7667f); prior work is committed by the user. Preserve it.

Read all Stage 2-24 strategy pages linked by https://ffxiverminion.com/guides and
the official guide https://na.finalfantasyxiv.com/lodestone/playguide/contentsguide/goldsaucer/lovm/.
The official guide confirms three owned minions, Stage 2 unlock for other modes,
four-unit specials, neutral gadgets, 15 tournament matches and harder NPCs after
wins. Private chosen opponents are supported by Player Battle (Non-RP); I412's
paired roles and AFK behavior remain unimplemented/unverified and no second-client
runtime is authorized. No Ocean Fishing/DevHub changes are needed for this review.

Stage guides explicitly allow Mammets instead of Kidragoras for ordinary stone
attacks. Boss guides require Imp switches (6), two bomb phases (9), rear attacks
and defending a stone (12), full army/final burn (15), retreat when Gilgamesh buffs
and center defense (19), focus Twintania (23), dodges/towers/adds (24). They often
use rare minions; their published rosters cannot simply become our prerequisites.

190 source selects accessible adaptations: Mammet #001 for ordinary stone stages;
Baby Bat for critter-heavy 7/16 and Odin 15; Airship for 4/6/9/12/19/23/24, with
four Baby Bat defenders on 6. Stage 19 uses one Airship group (up to nine / 225
capacity) to reduce selection contention. These replacements are UNVERIFIED.
Stage 24 is blocked before admission until tower/attack handling exists. Sources
for stats: https://ffxiverminion.com/minion-2, /minion-26, /minion-52. Airship's
auto-attack is single-target; Cargo buffs allies in an area by +20 ATK (+40 for
gadgets) for six seconds. Actual buff effect still requires native observation.
Supplemental vendor-page requests were unavailable; vendor hints need an in-game
check before purchasing. Purchase caps remain zero and purchases unimplemented.

190 also adds the standalone `/vmx v` window, launch buttons, shared settings and
stage/roster/prerequisite details. Requirements are checked after item registration
before travel and again before admission. Intent: isolated compilation plus tests,
then review before watched deployment; do not reset runtime attempts yet.

## Goal additions and prior requested pause — 2026-09-25

the user explicitly requested recording these requirements in the goal and pausing
it for now. They extend the approved plan; the full original acceptance criteria
remain outstanding. Do not resume implementation, builds, deployment or gameplay
until the user resumes the goal.

- Consult relevant Verminion guides before continuing strategy implementation or
  runtime attempts. Use the official guide for rules and unlocks and stage guides
  for tactics; record the relevant sources in this existing checkpoint. Compare
  guide advice with current game data and observed results rather than treating
  advice as a verified clear.
- Select specific minions and tactics for each of all 24 challenges, including
  their roles, composition, summoning costs, opening, specials and boss phases.
  Choose the repeatable farming roster explicitly as well. Prefer broadly
  available minions; Wuk Lamat and other rare or expansion-specific collection
  minions must not be baseline prerequisites. Such minions may be optional only.
- Reassess Wind-up Airship as a baseline candidate and verify its area special
  in battle. Previous selection/movement failures confounded roster comparisons;
  they do not establish that Airships are ineffective. Do not assume a single
  roster is suitable for every stage without guide and runtime evidence.
- Check the chosen strategy's required minions before starting wherever game
  data permits. Require the selected composition to be available before battle;
  report exactly what is missing and how to obtain it. Reuse authorized setup
  and registration handling. Purchases remain subject to the existing cumulative
  per-character gil/MGP caps, both default zero; never raise caps silently.
- Expand the existing configuration UI so users can understand each mode, the
  selected stage and strategy, required/owned/missing minions and acquisition
  requirements, readiness/blocking reasons, goal and verified progress, purchase
  limits/spending, and pause/Resume state. Explain roster choices and separate
  unverified strategies from observed clears. Keep the information actionable.
- Retain the background-control constraint: no game-window activation, OS cursor
  movement or global input injection. Development remains limited to the already
  authorized client and reload route.
- Consider standalone use for all Verminion features through `/vmx v`, with the
  same Verminion interface launchable from the main window and settings. Include
  setup, weekly modes, campaign, tournament opportunities, prerequisites,
  progress and existing Start/Stop/Resume controls. Reuse the task/service paths
  so manual use does not require the broader scheduled automation to be running
  and still respects ownership, accounting and FULL STOP.
- Include the narrowly scoped proposal in `Z:\devhub179.md`, selected ticket
  I412, in the goal's review: optional paired-player win trading, with one player
  attacking and the other not defending while avoiding AFK penalties. Consider
  feasibility, role selection and how it would fit the standalone interface
  before implementation. This is a proposed extension to the CPU plan, not an
  implemented feature or authorization to operate a second client. Preserve
  working behavior and make only necessary changes. The packet's unrelated
  Ocean Fishing W40 metadata is context only and does not change this goal.

These standalone/interface and I412 additions were recorded at the user's explicit
request while the goal remained paused. No DevHub records or runtime state were
changed; `Z:\devhub179.md` is the supplied reference, not a separate checkpoint.

Resume from guide research and a stage-by-stage accessible roster review, then
implement prerequisite checks and clearer configuration in the existing code.
Do not resume the obsolete Tora-jiro/Goobbue Stage 19 experiment unchanged.

## Latest state at pause

Carried-forward runtime evidence (not re-read during this pause-only update):
match 99 LOST at 13:30:11 on 2026-09-25 (-04:00), credited once to 82 matches /
23 wins; the three-attempt pause was observed at 13:30:25. Stages 1–18 are cleared
(mask 262143); Stage 19 remains unfinished. Matches 85 and 95 remain uncredited.
The last loaded marker is `verminion-control-20260925-187`; source/isolated build
189 includes 188's zoom guard and 189's selection reframe/defender recall changes,
but neither change has runtime verification. The accessible-roster revision and
new UI/prerequisite requirements above are not implemented. Last full suite:
817 passed under 186; subsequent 187–189 isolated compilation passed. Version
remains 0.5.0.3.

Before a future authorized Resume, reconcile fresh runtime and saved state:
expected paused true, sequence 99, pending 0, stage/run attempts 3, consecutive
losses 3, totals 82/23, mask 262143, both purchase caps/spending zero. Preserve
unknown outcomes and existing progress. Logging was nearing the main 100 MiB cap;
independent logging was not selected. Do not infer results from missing evidence.
This pause update performs no runtime/config mutation, build, deployment or Resume.
The historical entries below are superseded by this state where they disagree.

## Attempt history

99 reached the first buff at 13:28:37 (boss 12,574, ATK Up 962/500 for 30 seconds).
All four Tora selection clicks returned the boss ID instead; no retreat movement
was issued. Most of the army died by 13:28:55, boss 11,570, capacity only 40.
The buff ended 13:29:13. 99 still owns its result; HOLD watched reload.
Source 189 retains 188's idempotent zoom, reframes once after the first failed
Stage 19 selection using the existing minimap, and recalls defenders after the
attacker retreat command rather than waiting for attacker arrival. Still requires
native friendly selection before any movement. No roster or config changes.
Compile isolated; review 99's result/pause before another guarded Resume.

99 remains under 187. At 13:28:26, boss HP 13,612/16,000 and central stone
4,655/5,000; sustained damage is materially better than 97/98. Still no clear.
188 isolated build PASSED; whitespace clean. Hold watched deployment through
99's actual result. Do not change roster or Resume while this result is pending.
Need observe the first buff/retreat phase; current code recalls attackers only,
so defender survival is an explicit remaining concern. Fresh 99 avoids 98's
possible repeated-zoom confound; do not attribute all differences to the flag fix.

98 LOST 13:25:39, credited once to 81 matches / 23 wins; boss 14,706/16,000.
99 admitted 13:25:56, battlefield 13:26:03; third permitted attempt remains live
under 187. No new Resume/config edits. 188 source-only fixes repeat zoom on reload:
read native distance and skip wheel events once the verified 10.5 distance is
already reached. Keep the six-event bound. No roster, geometry, goal or input-gate
change. Compile isolated and HOLD watched reload until 99 settles and pauses.

187 watched build PASSED and dispatched 13:21:24, resuming Stage 19's next owned
admission. Native 13:21:58-13:22:06 confirms inactive false during click sampling,
restoration to the saved flag, and foreground false. This proves the local flag
handling only; command reliability and the result remain pending. Hold the battle
through its result. No Resume or config changes since the reviewed paused-96 run.
Verified the existing ECommons keyboard helper sends window messages only to the
current process; it does not use global keyboard injection or change focus.

97 LOST 13:20:42, explicitly credited once to 80 matches / 23 wins. Boss remained
15,316/16,000; roster still unproven. 187 isolated build PASSED, whitespace clean.
Intent: watched 187 during the reconciled result/setup interval; preserve any
subsequent admission and existing remaining two-attempt budget. No new Resume or
config edits. Verify exact startup, frame flag restoration with foreground false,
whole-group movement and final outcome. Do not infer success from button requests.

186 dispatched 13:16:43; 97 admitted 13:16:50 and entered 13:16:58. Eight Goobbue
Sproutlings reached the +3X central rally by 13:18:30; boss lost 580 HP. Four
Tora-jiro remain at gate despite the 13:18:15 ground order. Keep 97 uninterrupted.
Source-only 187 tests a concrete input inconsistency: completed clicks alternate
WindowInactive true/false while the virtual device reports focused. Temporarily
clear that client flag alongside the sample and restore it at the frame boundary.
No OS activation/input/cursor changes, roster change or config mutation. Compile
isolated; HOLD watched deployment until 97's explicit result/reconciled admission.
All 817 tests passed for 186; 187 runtime effect remains unverified.

186 isolated build PASSED; all 817 tests PASSED; whitespace clean. Intent now:
apply reviewed Resume only if selected character still matches paused 96 exactly
(79/23, mask 262143, pending 0, stage/run attempts 3, losses 2, zero caps/spend).
Preserve all facts and sequence, reset only existing Resume budgets, then build
the watched Debug output. Verify 186 startup and whole match 97 without reload.
No change to window focus, OS cursor, global input, version or purchase caps.

Source 186 replaces the failed Airship/Bell roster with owned type counters:
four Tora-jiro (65 ATK, critter, cost 20) for the poppet boss, eight Goobbue
Sproutlings (55 ATK, monster, area attack, cost 20) for critter adds. Total 240.
Attackers hold the stone unless Gilgamesh enters; defenders share the exposed
+3X rally. Removed obsolete gadget-buff probing and defender trap-disarm casts.
Retains 185's bounded diagnostics and one-second ability readback. No purchases,
input geometry changes or config changes. Compile isolated and run regressions
before guarded Resume of paused 96. Review result and positions; no clear claimed.

96 LOST 13:04:32, credited once to 79 weekly matches / 23 wins. Three-attempt
pause confirmed 13:04:46. Fresh saved-state read confirms paused true, pending 0,
sequence 96, stage 19 attempts 3, run attempts 3, consecutive losses 2; campaign
mask 262143, both caps and spending zero. 95 remains uncredited. 184 remains the
loaded artifact; isolated 185 passed and is source only. No pending tool sessions.
Reviewing 96's failed central defense before a new guarded Resume. No stage 19
clear or Airship ATK effect claimed. Log snapshots remain bounded and unfocused.

184 loaded13:00:27/dispatched13:00:31;96 admitted13:00:44, battle13:00:51.
Third permitted attempt, still running. HOLD all watched reloads through96's
explicit result. Source185 reduces repetitive routine snapshots to one paired
sample per15seconds (opening/phase/result retained independently), because
Dalamud log was95,207,095bytes at13:04, approaching its100MiB cap. Adds a native
snapshot1second after19 special requests to verify actual effect, not just button
execution. No new sink/files/dependencies or game tactics. Compile isolated185;
184 retains96. No Resume/settings edits since paused93, source-only185 pending.

184 watched build PASSED, whitespace clean. No pending build/test sessions.
Reconcile fresh184 startup and96 admission next; do not reload its battlefield.
Source and watched artifact match184; version remains0.5.0.3. No settings edits
since guarded Resume of paused93. Overall objective remains incomplete.

95 ended UNOBSERVED across183 reload.183 loaded12:58:56/resumed12:59:00;
native12:59:05 confirms territory388/playingFalse/queueNone, then explicit
uncredited failure. Saved state: matches78/wins23/mask262143, pending0/seq95,
pausedFalse, attempts2/stage19attempts2/losses1. Never invent credit for95.
Intent184: executable marker only to dispatch the existing third permitted
attempt from this reconciled outside-battle state. No Resume/counter/config edits.
Retain183's rally correction and Airship/Bell roster. Leave the next whole battle
uninterrupted through its explicit result; last full817 tests PASS under181.

183 isolated compile PASSED. Intent: watched183 now, correcting the idle stone
collision while preserving95/current admission and its existing remaining
attempt budget. No Resume/settings edits. Reconcile load and result separately;
last known confirmed totals78matches/23wins, mask262143.

182 loaded12:55:15/resumed95 at12:55:19. Airship native action fired12:55:53 and
12:56:25; ATK status effect itself not yet captured. Four-Airship movement proven
12:56:40->41 (all four advance toward the requested point), foregroundFalse.
Source183 corrects an actual rally collision: native friendly StoneB is(0,0,2),
enemy StoneB(0,0,-2); old attacker idle offset -4Z lands exactly on the enemy
stone model. Airships repeatedly ended nearz-8 while idle. Rally +3X beside the
defended stone instead. Preserve both roster and owned95/current admission.
Compile isolated183 first. No Resume/settings edits;95 remains attempt2.

94 LOST12:53:04, credited once78matches/23wins.95 admitted12:53:20, entered
12:53:28; second attempt under181.182 isolated compile PASSED. Intent watched
182 now before95's first central engagement; same Airship/Bell roster, admission
and attempt budget retained. No Resume or settings edits. Need positive support
buff/status and final result evidence before claiming the composition works.

181 loaded12:50:28/resumed94 at12:50:32; all817 tests PASS.94 center fell from
4855 at12:51:20 to100 at12:52:58 while Tinker specials fired and Airship's buff
never did. Source182 addresses support selection starvation: when four nearby
Airships can buff four Bells that lack native962(ATK Up), prioritize inspecting
that party over repeated movement. The ally buff need not wait for enemy contact.
Native readiness still gates casting; existing probe bounds/cadence remain.
This changes no roster/cost/input/counters. Compile isolated182;181 still owns94
or its subsequent permitted admission. No Resume/settings edits since paused93.

180 loaded12:48:13/dispatched12:48:16;94 admitted12:48:24, battle12:48:32.
At12:50:01 all eight Tinker's Bells are gathered near center with400HP; four
Airships positively present at220/240 total capacity and moving from the gate.
Regrouping has native position evidence. First airship buff/result not yet seen.
181 isolated compile PASSED. Intent: watched181 while94 is still early, preserving
its admission/budget/roster and pause. This narrowly repairs the click-hit lifetime;
no Resume or settings edits. Keep the actual result owned and separately verify
181's load, selection evidence and the roster's native ATK buff.

180 watched build PASSED after guarded Resume matched paused93 exactly. Load,
94 admission and composition proof still need reconciliation. Source181 fixes a
concrete hit-sample lifetime bug found during crowded-selection review: the
frame hook overwrote clickedMinion/timestamp on EVERY virtual hover frame after
a completed click, allowing moving enemies to replace its sample. Capture only
on that click's release, and use ReadBattlefieldClickHit for the same-type check
instead of live hover info. No input geometry, roster or counters changed.
Compile181 isolated first and HOLD watched pending180's fresh native evidence.

93 LOST12:45:34, credited once77matches/23wins. Three-attempt pause confirmed
12:45:48. Saved12:47:24 state: pausedTrue/pending0/sequence93, mask262143,
stage19attempts3/runattempts3/losses3, modeWinTarget/target10, zero caps/spending.
180 isolated compile PASSED. Reviewed gadget-buff composition warrants guarded
Resume of this exact state and watched180, preserving all facts and sequence.
The new Airship palette must be prepared before admission. No Stage19 clear or
retreat survival claimed; no new control method introduced.

Source180 candidate follows93's observed poor damage (boss15768 at12:45 despite
center contact). Owned setup proves Airship52:465HP/50ATK/75DEF/cost25/gadget,
special +40 gadget ATK. Replace four Aymerics with four Airships alongside eight
Tinker's Bells (220capacity total). The Bells' base20ATK struggles against45DEF
adds; Airship's observed description supplies a relevant area buff instead of
Aymeric's unused shield action. Probe both same-type action parties, and treat
220/240 as filled when another25-point attacker cannot fit. Keep six-request and
replacement reservations. Test changed mixed25/15 costs. No purchases. Compile
isolated180; HOLD watched until93 settles, since palette must be prepared outside
battle.179 still owns93, third attempt; no Resume/reset/account edits.

179 watched build passed; loaded12:39:35/resumed92 at12:39:38.92 LOST12:40:00,
credited once76matches/23wins.179 had only a short end-of-match interval; the
next permitted93 is the useful fresh test. No Resume or config edits, third
attempt remains. Leave179 loaded and reconcile93's admission, role movement,
local defense and explicit result. Current source matches watched179.

179 isolated build PASSED and diff whitespace clean. Intent: watched179 now,
retaining92/current owned admission and remaining attempt budget. Same roster;
this addresses the observed defender-command starvation without a new Resume.
Verify load and actual attacker departure before claiming the fix works.

178 loaded12:36:52/resumed92 at12:36:56. Tinker's special fired12:37:35.
92 at12:37:58 exposes movement starvation: four Aymerics summoned from12:37:18
remain at gate while repetitive defender orders run. Source179 computes attacker
intent before Stage19 defender work, alternates pending movement between roles,
and timestamps defensive wave dispatch too. Retreat still overrides defense.
No roster/input/count changes;177 full817 passed,178 builds passed. Compile179
isolated first;178/current92 still owns the result. No Resume or account edits.

91 LOST12:36:06, credited once75matches/23wins.92 admitted12:36:22 and entered
12:36:30, second permitted attempt.178 isolated build PASSED with anchor fix.
Intent: watched178 now at92's opening; retain the same roster, owned admission
and attempt budget. No Resume/settings edits. Native91 defeat snapshot confirms
surviving defenders strandedz-9..-12 while Enkidu attack the center stone.

Source178 fixes idle defender dispersion observed91 at12:34:11: four Tinker's
Bells atz-6, three atx7/z4, and one gate reserve; army health mostly full while
boss15926/16000 and B4345. Idle groups never received a regroup order. Regroup
the farthest deployed cluster onto ground two yalms ahead of the stone when no
local threat remains. Defender interception now shares the attacker's six-yalm
containment and refreshes live target position before its ground movement order.
Honor the selected Stage19 cluster anchor before density when picking a clickable
member; otherwise the idle regroup could select the already-gathered majority.
No roster/cost/attempt changes. Compile isolated178 before watched deployment;
177 still owns91. No Resume/reset while that admission is pending.

177 watched build passed, loaded12:31:19/dispatched12:31:22. Match91 admitted
12:31:30, battlefield12:31:38. Guarded Resume matched paused90 exactly and
preserved74matches/23wins/mask262143/zero caps and spending. Native wheel zoom
confirmed: camera distance6 at12:31:43 ->10.5 at12:31:50 after six events;
foregroundFalse remains observed. Eight Tinker's Bell deployment and Aymeric
summons verified by12:32:32. All817 tests PASS on final177, no failing checks.
Continue91 to buff/retreat/result evidence; no new candidate or reset pending.

177 final isolated build PASSED (including wheel/retreat changes). 89 lost
12:24:47 and 90 lost12:29:33; both explicitly credited once. Saved state at
12:30:11 is pausedTrue, pending0, sequence90, matches74/wins23, mask262143,
stage19attempts3/runattempts3/losses3. The bounded176 run has settled outside
battle without a clear. Reviewed177 now warrants guarded Resume of exactly this
state and watched deployment, preserving all facts/sequence/caps/spending.
Intent: prepare owned Aymeric/Tinker's Bell palette outside battle, verify177
load/dispatch, camera distance and actual new roster, then reconcile its result.
No foreground activation, OS cursor movement, or global input is permitted.

177 candidate also addresses a concrete retreat failure. In88, seven surviving
Succubus kept EXACT x/z positions from12:20:04.233 to12:20:05.506 after the gate
order. The reused selection had just undergone minimap framing. Always freshly
select the attacker group for19's retreat. To reduce camera jumps, exercise six
native -1 wheel events at19 battle setup through the existing frame/device hook;
no mouse buttons, OS pointer or focus changes. MouseWheel=-1 is declared in
ClientStructs InputData.cs and wheel zoom is documented in the official guide.
Capture native camera distance/projection after the bounded sequence. This is
not a minimap-right-click experiment (that control is not documented). Recompile
isolated177; new zoom/retreat and high-defense roster all remain unverified.
176 stays loaded while89/current run settles.177 must prepare both new owned
palette entries outside battle; do not deploy into a living old army.

176 loaded12:16:09/dispatched12:16:13; guarded Resume matched paused87 exactly.
88 admitted12:16:21. Local containment confirmed12:17:41 and12:18:32: targets
leaving the stone are rejected before movement. B2730/boss14955 at12:19:12.
Source-only177 now prepares4Aymeric(cost30,465HP/80ATK/75DEF) +8Tinker's Bell
(cost15,400HP/20ATK/60DEF,AoE); all owned, no purchases. Compared with published
Morbol/Gentleman bunker (both75DEF), prior roster25/40DEF collapsed during basic
attacks before the boss buff phase. Tinker native special deals30 area damage
and reduces enemyDEF30%; use defender parties, skip Aymeric's shield action.
Reserve120 capacity for each role and keep six-request queue bounds. Focused
regression updated for eight defenders and mixed15/30 costs. Compile/test isolated
177; HOLD watched until88/current bounded run settles. Do not replace a living
Succubus army. No new Resume or account edits after paused87.

87 LOST12:13:56, credited once71/23. Limit pause confirmed12:14:10; pending0,
sequence87, stage19attempts3/runattempts3, consecutiveLosses2, mask262143.
176 isolated compile passed. Reviewed local-target containment correction now
warrants a fresh bounded test, keeping175's4Goobbue/16Succubus composition so
its effect can be evaluated. Intent: exact-state guarded Resume then watched176;
retain every confirmed fact, both zero caps/spending and sequence. No false credit
for85. Do not claim Stage19 clear or buff-retreat survival from this failed run.

87 first Succubus special fired12:11:51;16 attackers fielded by12:11:57. B4555HP
at12:12:01; boss15739HP. Source176 fixes live target drift: targets are chosen
within6yalms of the bunker, but pending movement refreshed their position after
selection even when they had left (85 orderedGilgamesh11:59:30, final movement
captured himx-16 by11:59:38). Immediately recheck the surviving defended stone
before a19 attack order; cancel/reassess if enemy left its6yalm area. No roster,
attempt or purchase changes. Compile isolated176 first.175 still owns87's result.

175 watched build passed; loaded12:10:13/resumed87 at12:10:17.87 was admitted
12:09:54 and entered12:10:01 under174, but only the shared Goobbue opening had
been summoned before reload. Native12:10:40-47 verifies Succubus attackers and
no Uma army;175 began before attacker deployment. Third permitted attempt;
70/23/mask262143 remain the confirmed totals. No settings edits or Resume.
Retain175 for native buff, add defense and explicit result evidence. No source
candidate or tool session pending.5focused tests pass; prior full suite817pass.

86 LOST12:09:38, credited once70/23; no clear. Result is positively reconciled.
Intent: watched175 immediately after this settled result, retaining the existing
third-attempt budget. Both rosters share the initial Goobbue opening; verify the
next admission's actual attacker roster and queue after load before calling it
a fresh175 test. No Resume, purchase or account edits.175 isolated build and5
focused lifecycle tests passed. This deployment must not credit85's unknown exit.

174 loaded12:04:34/dispatched12:04:38.86 admitted12:04:51, battlefield12:04:58,
second attempt of the current bounded run. At12:08:00 both outer stones gone,
B3670HP, boss15404HP; defender losses start12:08:02. No result yet observed.
175 IS SOURCE/ISOLATED ONLY: isolated compile and all5 VerminionLifecycleTests
pass; whitespace clean. No pending tool sessions. Last full suite817passed171.
Do not deploy the roster switch into a living Uma army: retain174 through86 and
any remaining87, reconciling native results and saved accounting. If174 clears19,
keep that proven roster and discard only175's unneeded Succubus roster candidate.
If the bounded run pauses without a clear, the reviewed +40 monster-buff roster
may then justify guarded Resume after exact paused-state reconciliation and a
watched175 build. No further Resume or account edits have occurred since84.
Loaded174 still uses4Goobbue/8Uma;175 candidate is4Goobbue/16Succubus.

174 watched build passed; load/admission not yet reconciled. Source-only175
prepares an owned-roster alternative for repeated add overruns: keep4Goobbues,
replace8Uma(cost20 each) with16Succubus(cost10 each). Succubus's observed +40
monster ATK area special also buffs Goobbue AoE, unlike Uma's +20 own-party buff.
This reuses15/16's verified minion and native special; no purchases or new control.
Focused queue/capacity assertions updated for10-point requests. Compile/test
isolated175, but HOLD watched deployment until174's fresh attempt settles and
its defender movement has been reviewed. No Resume/reset or config edits.

173 watched build passed/loaded12:02:31, resumed85 at12:02:35, but85 ended
without a result observed12:02:40. No credit:69/23/mask262143, pending0,
sequence85, stage19attempts1/runattempts1, pausedFalse. Explicitly unresolved
result; do not repair totals from inference. No additional settings changes.
Intent174: executable marker only to dispatch the next permitted fresh attempt
under173's reviewed contact fixes. Preserve remaining two attempts; no Resume
or counter reset. Confirm174 load/admission and allow the result to settle before
another watched reload so result observation is not interrupted.

85 center B fell by12:01:51; boss13899HP at12:01:53. Defender stale-contact
correction173 isolated compile passed; final review confines changed arrival
threshold/rounding to19. Intent: watched173 now, preserving85 or whichever owned
admission is current and its existing attempt limit. No settings or Resume.

85 native12:00:14 confirms172 exposed rally: Uma atz-0.3..-3.9, versus stone
model obstruction. B3510HP, boss15494HP; twelve adds mostly480/480HP approach
fromx-2/z5 while four Goobbues standx2..3/z3..4. A ground-only defender order
was kept30seconds for the same moving add. Source173 refreshes Stage19 defenders
at5seconds while most remain outside2yalm melee; leaves engaged groups alone.
Attacker arrival reassessment likewise requires2yalm contact, not6. No roster,
attempt or purchase change. Compile isolated173; hold watched until85's next
phase/result evidence. No new Resume;85 is first attempt of this run.

172 watched build passed; loaded11:57:54/dispatched11:57:58. Guarded Resume
matched exactly paused84 and preserved all confirmed facts.85 admitted11:58:06,
battlefield11:58:13. Background click evidence still foregroundFalse11:58-59.
No new source candidate or tool session. Continue85 through buff/defense/result
milestones; do not alter attempts while it is owned. Stages19-24 remain pending.

84 LOST11:56:04, credited once69matches/23wins; boss13251HP. Three-attempt
pause confirmed11:56:18, pending0/sequence84/stage19attempts3/mask262143.
172 isolated compile and whitespace passed;817tests passed on171. Reviewed
movement-priority/exposed-rally correction warrants the next bounded test.
Intent: guarded Resume of that exact paused84 state, preserving all result
facts, sequence, both zero caps and spending, then watched172. Reconcile load
and new admission; no Stage19 clear or buff retreat success claimed yet.

171 loaded11:50:25/resumed83 at11:50:29.83 LOST11:52:07, credited once68/23;
boss9743HP.84 admitted11:52:23, third allowed attempt, fresh171.817tests pass.
Source172 corrects priority: Stage19 optional single-party readiness probes ran
before movement decisions, potentially deferring pursuit through every candidate.
Now probe only when its ordinary action is Wait; preserve Stage15 final probing.
Idle Stage19 attackers use exposed ground4yalms in front of the stone, avoiding
the same stone-model obstruction already corrected for defenders. No roster,
caps or accounting change. Compile isolated172 first;84 still owns its result.
Do not Resume/reset before reconciling the third attempt and exact saved state.

82 LOST11:47:37, credited once67/23; boss15565HP. Center held through11:46:29
then fell to converging adds.83 admitted11:47:54, second attempt under170.
Source171 fixes a concrete selection ownership bug in FollowBoss: native party
inspection selects ONE member and sets groupSelectionVerified, but movement
reuse treated it as the whole group. Reuse now also requires !groupSelectionOnly
and no singleUnitSelection. Ordinary party orders reselect the nearby type after
an ability probe. No roster, attempt or accounting changes. Intent: compile then
watched171 preserving83 or the current owned admission and its pause. No Resume.

82 milestone11:46:29 under170: centerB still4555HP with both outer stones gone;
boss15565HP. Goobbues now engage (one80/410HP), and Uma82 dispatched against adds
11:46:23. The new exposed defense holds B longer than prior placements. No native
boss buff has occurred yet in this attempt, so early-warning retreat is still
unverified. Retain170; let82 reach its phase/result within normal time/attempt
limits instead of changing the now-functional deployment. No settings edits.

170 final isolated/watched builds and whitespace passed;817tests passed before
the final probe-priority review (those service changes are compile-checked).
Guarded Resume matched paused81/pending0/66/23/stage19attempts3/mask262143 exactly;
all facts, sequence, zero caps/spending preserved.170 loaded11:43:10/dispatched
11:43:14.82 admitted11:43:21, battlefield11:43:29. Exposed defender positions
verified11:44:48: four Goobbuesx-0.4..-1.9,z-0.5..1.1 beside/front ofB, versus
previousz8..9.5. Center4555HP at that snapshot, boss still16000 atouterA. Keep
170 running for phase/ability/result evidence. No source-only candidate or tool
session remains. Stage19 clear, stages20-24, purchases/tournaments, FULL STOP
runtime cases and ten fresh background farming wins remain open.

170 final review keeps defender deployment ahead of optional party probes and
requires four nearby same-type units before probing Stage19 specials. This
prevents ability inspection from delaying stranded groups/defense when only a
partial party is in combat. Rebuild this final source before watched deployment.

169 built/loaded11:39:43/resumed owned81 at11:39:47.81 LOST11:40:31, credited
once66matches/23wins; boss10894HP. No counter edits. Important correction to the
168 diagnosis:81's every-frame observer still saw962 at23.79sec,964 at26.88sec,
966 at29.97sec. Buffs are staggered about3sec each; requiring ALL three delayed
retreat6sec independently of pending controls.170 now enters on ANY of962/964/
966 and leaves after all expire, keeping168's phase preemption. This observed
phase correction plus169's exposed defense/action-party checks warrants a fresh
bounded test after build/tests and exact paused81/pending0/66/23/mask262143/
stage19attempts3 reconciliation. Use existing reviewed Resume accounting only;
preserve match sequence/results and zero caps/spending. Version0.5.0.3 unchanged.

81 native11:36:47 explains weak center defense: all four Goobbues had410HP and
stoodz7.9-9.5, behind the stone atz2, while Gilgamesh attacked atz2.8. The direct
stone-center order left them outside melee.169 also sends defenders to an
exposed point4yalms beside the stone, or to an observed nearby Enkidu position.
Orders stay ground-only so a moving add cannot drag the group out of the defense
area during camera work. Keep four defenders/eight Uma and all limits unchanged.
Compile this geometry correction; deploy169 to the still-owned81 after build,
or retain its limit pause if81 has already settled. No Resume/reset authorized
by this deployment. Native arrival/damage and any victory still require evidence.

80 LOST11:34:43, credited once65/23. The168 add-special condition dispatched82
at11:34:02 with boss still16000HP; the collapsed army could not recover.81
admitted11:34:59, third allowed attempt, fresh168. Source-only169 reuses Stage15
native action-party probing for Stage19 at full capacity, against nearby adds
or boss. Selecting defenders previously left Uma specials unattended until the
next movement order. Keep attacks active while selecting living party members;
wait10seconds after the bounded candidate set, clear probes at phase transitions.
No composition, budget or accounting change. Compile separately; hold watched
169 until81's phase result or a concrete runtime failure warrants intervention.

168 isolated/watched builds,817tests and whitespace passed. Loaded11:33:12 and
resumed owned80 at11:33:16. No account edits.80 still had untouched16000HP boss
and center4605HP; outer stones were gone. Review phase-preemption effectiveness
only once a fresh native buff occurs. Queue-ownership change is built but live
FULL STOP acceptance remains open. No source-only candidate or tool session.

79 LOST11:29:43, credited once64/23. Early Goobbue defense and Uma special are
verified: native82 at11:27:22, boss13700HP then;79 ended with boss9705HP. Center
survived until11:29:38. Stage19 minimap framing confirmed scale3.5 and subsequent
unit selection/movement.80 admitted11:30:00, second permitted attempt under167.
Native962 had only23.45seconds remaining when detected11:27:36: pending controls
postponed phase observation by about6.5seconds.168 now observes phase ahead of
selection/camera steps, cancels their stale destination, prioritizes retreat,
and allows defender replacements once attackers reach their gate. Area special
may target nearby adds even while the boss is farther away. Includes reviewed
queue-ownership fix. Intent: compile then watched168 preserving80/attempt2 or
whatever next owned admission is current, no Resume/reset or purchase changes.

Source-only168 fixes a lifecycle gap found in review: Start formerly adopted any
matching queue with admissionConfirmed=true, even after its saved match had been
abandoned. Now resume requires the existing saved match/duty. Abandon persists
its removal before requesting native CancelQueue, guarded by the same character
and exact single-stage queue. Reload of an owned admission remains supported.
Reuse the existing native cancellation method used by ChocoboRace; no new state
or tools. Live FULL STOP/queue cancellation acceptance remains pending. Compile
this candidate separately and hold watched deployment while79 runs under167.

167 isolated/watched builds,817tests and whitespace passed. Guarded reviewed
Resume applied to exactly paused78, preserving all confirmed facts and zero
caps/spending.167 loaded11:25:29, dispatched11:25:33.79 admitted11:25:41 and
entered19 at11:25:48. Three Goobbue opening requests verified11:25:54-56 at60
capacity. No pending build/tool session. Continue79 through explicit evidence.

167 isolated compile passed. Saved selected character reconciled: pauseTrue,
pending0/sequence78/stage19attempts3,63matches23wins/mask262143, zero purchase
caps/spending. Reviewed early-defense/area-special/gate-recovery corrections now
justify one new bounded run. Intent: after tests pass apply existing Resume to
that exact state, then watched167; no result fact or sequence will change.

78 LOST11:21:16, credited once63matches/23wins; boss11474/16000HP. Limit paused
11:21:30. Center fell before the final stone. Roster evidence confirms owned
Uma-no-unicolt529:480HP/65ATK/35DEF/cost20/speed3, area120 special plus partyATK20.
Source167 adds a reviewed strategy correction to the camera change: summon four
Goobbues first (reserve initial60 for defenders), keep them on the center stone,
then eight Uma attackers defend the center/nearby boss and adds. Their damage
special replaces Tora's slow. Recover at the gate during native962/964/966 phase.
No purchases. After build/tests, guarded development Resume may reset only the
exact paused78/pending0/63/23/stage19attempts3/mask262143 state. Preserve all result
facts, sequence, caps/spending. Fresh attempt required to test this opening.

Source-only167 extends the existing Stage9/12 minimap camera path to Stage19.
Current code already tries that helper for19, but the interaction duty gate
rejected570. Reuse the live gate/stone geometry consistency checks and subsequent
projection validation; include570 in the existing bounded minimap node capture.
No new control method, signatures, input injection, files or attempts. Hold the
watched deployment until78 settles; loaded166 remains responsible for its result.

77 LOST11:16:53, credited once62/23.78 admitted11:17:09, its third permitted
Stage19 attempt.166 built successfully and loaded11:18:11/resumed78 at11:18:14;
no counters/settings changed. Native pursuit now reaches boss: HP14679/16000
at11:19:47 (previous77 barely87damage). Goobbues target Enkidu; B2990HP and
C4680HP at11:19:46. Continue78 to its explicit result; no clear claimed.

77 mid-fight evidence11:16:16: boss15913/16000HP atx11 while all8Tora were at
x-10, the previous destination. Goobbues had been repeatedly ordered onto the
moving boss, contrary to the intended stationary add-defense role. Source166
uses existing Stage12 three-second pursuit/verified-selection reuse for Stage19,
and excludes Gilgamesh from its defenders' nearby threats. No new controls,
roster or budget. Intent: compile then watched166 preserving current77/any next
ordinary admission and all attempt/result facts. No Resume or settings changes.
Expected verminion-control-20260925-166; version remains0.5.0.3.

165 watched build succeeded,817tests and whitespace passed. Loaded11:13:10;
76 had already exited and remains uncredited. New match77 admitted11:13:26,
Stage19 entered11:13:33 under the unchanged second attempt. Native background
selection/movement verified11:14:05-17; first Tora group crossed from gate toward
Gilgamesh with foregroundFalse and no click-settlement failure. Continue77;
no further build or settings edits until a concrete strategy observation.

164 built and loaded; admission76 reached Stage19 at11:08:22, then stopped
uncredited11:08:53. Its selection-clear click remained queued when the next
selection was issued96ms later. The prompt clock was sampled before native
projection/panel work and did not prove the device consumed its release.
Saved76/pending0/61matches23wins/stage19attempts1/mask262143, caps/spending0.
Source165 now waits for the actual pending native click to clear before reading
selection or issuing another command, with a2second settlement bound. This
preserves the existing click protocol and never changes focus or the OS cursor.
Intent: isolated compile/tests, then watched165 preserving current admission
facts, attempt count and pause; do not recreate abandoned76 or reset its budget.
Expected verminion-control-20260925-165; version0.5.0.3 unchanged.

164 final isolated build/817tests/whitespace passed. Fresh11:07:15 state exactly
pausedtrue/pending0/sequence75/stage19attempts3/61matches23wins/mask262143/caps0.
Intent now: apply the existing reviewed Resume accounting for the role/pursuit
and stranded-unit corrections, then watched164. Preserve confirmed progress and
purchases. Fresh new admission is required; all prior75 results remain terminal.
Expected verminion-control-20260925-164, version0.5.0.3 unchanged.

75 LOST11:04:22, credited once61/23; closure11:04:26, campaign limit paused
11:04:36. Goobbues reached center, but boss remained15255/16000HP. Keeping both
parties at the stone left attackers idle while Gilgamesh was elsewhere.164 now
separates roles through existing controls: Goobbues defend the stone; Tora chase
Gilgamesh. Native buff transitions immediately reconsider orders, and exposed
attackers retreat before fresh deployments/defender updates during the buff.
Includes the observed stranded-gate correction. After final compile/tests, a
reviewed development Resume may reset only the exact paused75/pending0/61/23/
stage19attempts3 state. Preserve all result facts and zero caps. No new attempt
has yet been dispatched. Source164; loaded163.

75 admitted11:00:10. Mixed deployment verified: four Goobbues reached the center
by11:02:32, all410HP; eight Tora exist. Three Tora stayed at their gate despite
11:01:53 SendWave and were still there11:02:34. The dispatched-ID set excluded
these stranded units permanently, so FollowBoss kept selecting the center party.
Source164 rechecks Stage19 units still at gates after15seconds and permits a
partial departure at full capacity when no summons remain. No extra retries or
budgets; native positions drive the existing rally decision. Preserve75/attempt3
and all facts; no Resume/reset. Deploy this specific correction after compile.

Fresh saved state11:00:34: pending75/duty570, Stage19 attempts3, pausefalse,
60weeklymatches/23wins, clearedmask262143 (1-18), gil/MGP caps and spending0.
Loaded163, mixed Goobbue/Tora strategy. Let75 settle before another watched build
unless fresh evidence identifies a concrete correction. Reconcile its outcome
before any new admission or Resume. Remaining: Stage19-24 clears, background
farming repeatability, FULL STOP runtime cases, purchases and CPU tournaments.

163 loaded10:59:23/resumed pending74 at10:59:26. Goobbue defender requests began
10:59:32; the earlier battle's damage was already severe.74 LOST10:59:53, credited
once60matches/23wins. Next ordinary campaign admission is the third allowed
Stage19 attempt, now with the mixed composition available from the start. No
Resume/reset authorized just by reaching the limit; review fresh result evidence.
Source and watched build both163; no source-only candidate or tool session remains.
All817tests and whitespace checks passed. Version0.5.0.3, zero purchase caps intact.

163 final isolated compile and817tests passed. Fresh74 evidence10:58:44 shows
boss10342/16000HP, four attackers remaining in combat with four replacements ready.
The second attempt also loses groups to clustered Enkidus. Intent now: watched163,
preserving whichever owned admission is current and any saved pause, no settings
edits. Goobbue is already on the native palette, so existing-field recovery can
summon defenders as capacity frees. Stage24 research stays bounded120seconds.
Expected verminion-control-20260925-163; version unchanged.

Stage19 match73 LOST10:54:52, credited once59/23; boss12458/16000HP survived.
Twelve Tora-jiro fell to adds; center4705HP was then lost in about40seconds after
retreat. Native962/964/966 buff detection and retreat dispatch are confirmed;
winning phase behavior is not. Second74 admitted10:55:09. Palette already has
Goobbue41. Source163 now also reuses Stage6's existing four-defender deployment
for four Goobbue Sproutlings (AoE monster55ATK,cost20) in Stage19, reserving80
capacity and leaving160 for Tora. Tora prioritizes the boss near the stone while
Goobbues intercept adds. Skip the Goobbue trap-disarm special without traps.
Includes focused existing-test reservation coverage. No attempt/counter edits;
load this observed-failure correction into74 after compile/tests, preserving it.

Stage19 first attempt running under162;12 Tora-jiro at240capacity. At10:52:35,
Gilgamesh14482/16000HP, friendlyB4705HP, all three stones still positive. Native
central defense commands are working; no clear or buffed-phase proof yet.163
isolated build passed before a final null guard on the optional collision logger;
that guard avoids dereferencing a missing AtkStage during teardown. Source163
still undeployed; keep current attempt intact until a result/observed failure.

New source-only163 (replaces the withdrawn flank candidate): permit one bounded
Stage24 phase observation once campaign reaches it. Use already-owned Haurchefant
with existing boss controls; capture native casts/positions/field objects every
15seconds for at most120seconds, then stop uncredited unless an explicit owned
result already occurred. The normal three-attempt gate remains. No tower/dodge
mechanics are assumed or claimed. Native observations are required to implement
those mechanics. No impact to Stage19-23 recipes; hold deployment until transition.

Stage18 match72 WON10:50:39 under162, credited once58matches/23wins. Friendly
C3645HP survived. The prepared flank-only163 was not deployed and has now been
withdrawn: retain the strategy that produced this clear. Source matches loaded162
again; the isolated output still contains the unused163 candidate. No watched
reload required. Continue normal Stage19 admission and inspect its native phases.
Verified campaign mask now262143 (1-18). No settings or attempt resets.

162 loaded10:48:44/resumed pending72 at10:48:47. Owned stats confirm Wuk speed1,
35ATK/25DEF/cost10 with Arcana strength; Haurchefant85ATK/speed4/cost30. No owned
minion has an HP-healing special. Stage24 remains gated. Source-only163 changes
Stage18 to A/C/B gate order: the observed first center group died without stone
damage, while both flanks succeeded. Use existing six-Wuk groups and converge
from fallen flank stones; no new controls, roster or budget. Hold watched163
until72 settles, and verify on a fresh admission if another attempt is needed.

Stage18 match71 LOST10:46:57, credited once57/22. Last enemy B1205HP survived;
its initial six Wuks caused no center-stone damage before dying. Other two stones
fell. Native foes include Goobbue, Slime, Golem, Baby Bat, Mindflayer and birds.
Second match72 admitted10:47:14, battlefield10:47:21. Intent before watched162:
load the compiled roster/cast diagnostics while retaining pending72 and attempts2;
no composition or budget change. Need owned combat stats for this observed loss
as well as the later bosses. Expected verminion-control-20260925-162.

Stage17 match70 WON10:43:03, credited once56/22; closure10:43:06. Stage18
match71 admitted10:43:19, battlefield10:43:27, first opening order confirmed.
Loaded161. Isolated162 passed; hold watched deployment until Stage18 settles.
No settings changes. Sequential background clears16/17 now have positive results.

161 loaded10:40:31/resumed Stage17 pending70 at10:40:34, preserving its admission.
Source-only162 enriches existing bounded setup snapshots with owned minion combat
stats/special descriptions and existing cast snapshots with native target position.
This resolves the available healing/tower roster for Stage24 without purchases or
new diagnostics files. Hold watched162 until the ordinary campaign reaches a
useful transition; no new strategy or settings changes are included.

69 WON10:39:39, credited exactly once55matches/21wins; Stage16 clear verified.
Last friendly stone640HP remained. Group redirect across the field eventually
brought the army to the final stone. Closure10:39:43, Stage17 match70 admitted
10:39:56. This is the first complete background battle victory with selection,
movement, buffs and native result evidence under160. Intent before watched161:
deploy the reviewed opening/reinforcement distinction during Stage17 entry,
preserving pending70, all facts and pause. No settings/attempt resets. Isolated161
passed; expected marker verminion-control-20260925-161, version0.5.0.3 unchanged.

Source-only161 review tightens160's preservation condition: mark a reinforcement
selection explicitly. OpeningComplete advances when selection is requested, so
it was not sufficient to distinguish the final opening group from reinforcements.
Opening failures still stop; only marked late reinforcements use the ordinary
cadence deferral. Hold watched161 for match69's result; loaded160 unchanged.

160 loaded10:34:12/dispatched10:34:16. Earlier68 had already left duty, still
uncredited. Fresh69 admitted10:34:29 and battlefield10:34:36. Opening selection,
group movement and buff dispatched with actual foregroundFalse. Retain160 while
this result-preservation correction is exercised. No additional settings edits.

68 failed10:31:20 on an obscured late reinforcement, abandoned with no credit;
saved54/20, mask32767, pending0, sequence68, stage16attempts3, pausedtrue. The
prepared160 directly fixes this failure path without issuing unverified orders.
Fresh817 tests passed. Intent before watched160: guarded development Resume using
that exact saved state after this reviewed correction; reset only existing run/
stage attempt fields and pause, preserve all result facts and zero caps/spending.
If physical68 remains, resume without recreating its abandoned match. Expected
verminion-control-20260925-160. Background special confirmed in159: native ATK Up
status962 on six units10:29:25 after82 at10:29:24. No Stage16 clear yet.

159 loaded10:27:45; prior67 had ended outside duty, still uncredited.68 admitted
10:28:02 under the unchanged third-attempt budget. Source-only160 extends native
selection checks to every ordinary stage, and reuses the existing boss behavior
for obscured late ordinary reinforcements: issue no unverified order, preserve
current attacks/result observation, reconsider at the existing20second cadence.
The old proof-stage failure abandoned67 despite two destroyed enemy stones.
Opening control failures and exact carrier failures still stop; no loss/attempt
limit is removed. Hold watched160 until68 settles unless a concrete failure needs
this correction. No settings/counter edits. Loaded159; candidate160 not deployed.

67 stopped10:24:58 on six failed late group selections, abandoning its identity
without result credit (54/20 retained). Two enemy stones had fallen.159 now also
preserves a button-up virtual pointer for up to3seconds between related clicks,
and answers same-thread game cursor queries between native frames. The old OS
path left the cursor in place until movement cleanup; the new path prematurely
snapped to the real outside cursor after every release. This is a scoped game-read
substitution only, never OS cursor/focus mutation; ownership/expiry/Stop clear it.
Intent: watched159 to resume the physical67 if available, never recreate its
abandoned admission; preserve all attempt facts and any saved pause. Expected159.

158 loaded10:20:08/resumed66; its earlier single-unit delays led to Defeat10:20:51,
credited once54matches/20wins.67 admitted10:21:08, attempt2, clean corrected input.
This run positively proves group movement: six friendly Succubi all crossed the
center by10:22:18 after the10:21:42 order, while six later spawns stayed at their
gate. Native82 buff dispatched10:22:07. Both input frames and native troop positions
are required evidence; foregroundFalse verified in157/158 logs, no OS mouse calls.
Source-only159 reduces diagnostic input logging to release edges and reuses the
existing one-second observation delay after ordinary attack buffs, so their native
status can be captured before expiring. Hold watched159 until67 settles. No settings
or accounting changes. Current loaded158; next expected159 when intentionally built.

157 loaded10:16:35;66 admitted10:16:51. ActualforegroundFalse throughout the
first selection and right-click frames proves background dispatch. Native hit
1073768741 verified10:17:22, order10:17:25. But10:18:01 shows the other five
members still near GateB; only the clicked unit attacked. Earlier small all-unit
position deltas were insufficient group-movement proof. Double-only flags do not
expand the selection.158 supplies pressed+double together on the same-type step,
matching batched click edges, and retains all hit guards. Reload pending66 without
changing attempt1 or any facts. Expected158. Only single-unit background control
has sufficient evidence; group/special/Stage16 acceptance remains open.
Stage24 research: https://ffxiverminion.com/stage24-the-final-coil requires tower
soaks, lethal circle avoidance, healing and Twintania adds; keep its gate until
native telegraph/tower state and phase commands are observed.

156 loaded10:15:16 and resumed abandoned65 at10:15:20, but its physical duty
ended10:15:25 without an observed result. Correctly no credit or replacement
admission.157 intent: settings-only marker reload from this resolved outside-duty
state to test the corrected background single/double/release path on a clean
Stage16 admission. No config, counters or pause edits. Expected157.

65 had failed10:13:28 on exhausted selectable candidates, so155 loaded10:14:19
but correctly remained paused10:14:22. No credit; no155 input ran. Intent156:
after the now-reviewed release/double-click fixes, guard the exact paused saved
state pending0/sequence65/stage16attempts3/53matches20wins/mask32767/caps0,
apply existing development Resume accounting and reload. If the physical65 still
exists it remains abandoned/uncredited. Expected156; same0.5.0.3.

155 deployment now warranted:154 repeatedly selects a friendly on its first click
but its same-type second click clears index17 (10:12:39-41). Native mouse-message
code confirms +0x14 is double-click, distinct from pressed +0x10.155 explicitly
supplies that event for the existing same-type selection step. Native down/double
handlers set held+pressed or held+double respectively; release stays +0x18.
Include removal of window messages and real foreground observation. Preserve
pending65 if still owned and its third attempt; do not reset pause/counters.
Expected155. Normal single selection and bomb carrier semantics are unchanged.

154 loaded10:09:42;65 admitted under the existing third-attempt budget.
Correct release offsets produced positive native selection10:10:31 (inactivetrue),
all six units advanced after the10:10:34 order, native82 special dispatched10:10:57,
second group selected10:11:08 and ordered10:11:11. Battle65 continues; no win yet.
Prepared155 removes the now-unneeded window-message experiment and logs actual
OS foreground equality (Framework.WindowInactive alone may reflect message state).
Keep155 source-only until65 settles unless an observed control failure warrants
reload. No settings changes. Retain154 as this battle's loaded marker.

153 loaded10:06:14/resumed64; right-click proof10:06:25->31 did NOT move any
unit toward the requested lateral destination. No credit; stopped10:06:31.
Native151 code capture revealed a concrete installed-struct mismatch:
MouseDevice.Update cursor base2AE8CA8, ProcessMouseInputMessage button-up writes
2AE8CC0 (+0x18), RepeatCounter result2AE8CC4 (+0x1C). Installed fields label
+0x14 Released and +0x18 HeldThrottled, so our samples erased real release edges.
154 corrects those three button-edge offsets locally, removes the movement probe,
and restores all native hit guards. No inferred function/address is executed;
RVA values above are research evidence only. Intent: watched154, preserve abandoned
64 and all attempt/result facts; no Resume/accounting edits. Expected154.

152 loaded10:03:55;64 admitted10:04:12, failed10:04:47 without credit.
The diagnostic destination was offscreen (gate origin+6X), so no movement was
sent; this cannot reject the hover/readback hypothesis.153 corrects the probe
origin to the actual clicked unit plus3X, a short lateral order. Preserve all
accounting/attempts and resume the existing physical64 if still present; never
recreate its abandoned identity. Expected153; no pause/Resume edits.

151 loaded10:00:39 and stayed paused. Declared input queries confirm pressed/down
read the propagated filtered button fields. A remaining ambiguity: index17 is
hover information, not durable selection, so its absence may reject a working
selection before any movement command.152 makes one bounded diagnostic right
click after the first unverified Stage16 selection, records native units before
and six seconds after, then fails without inferring selection/result. No normal
strategy bypass and no credit from the diagnostic. Remove temporary code captures.
Intent after isolated build: clear only this diagnostic pause, retain sequence63,
attempt1/pending0/53matches20wins/mask32767/caps0. Expected152. Actual movement
is required; input dispatch alone still proves nothing.

150 loaded09:58:13/resumed abandoned63, failed09:58:28 with no credit.
Primary, secondary and UI pressed/released edges propagate correctly; no native
cursor caller could be unwound through the managed detour. Remove that trace.
151 intent: diagnostic pause only (no result/budget change), read bounded bodies
of declared InputData queries and mouse Update while paused. No new battle or
control dispatch. The native consumer still has to be identified. Expected151.

149 loaded09:54:44; match63 failed09:55:35 after six unverified group candidates.
No result credit.817 tests passed. Window messages plus device samples remain
insufficient.150 traces bounded GetCursorPos native callers and primary/secondary
button edges to identify the actual consumer; no inferred address is executed.
Intent: watched150 using existing accounting and remaining attempt budget, with
no further Resume/reset. Preserve abandoned63 if its physical battle survives.
Expected20260925-150; no background selection/movement claim.

149 isolated build passed. Review added own-window button release on pre-sample
expiry and direct hook disposal (normal service Reset already releases input).
Fresh saved state exactly matches paused/pending0/sequence62/stage16attempts3,
53matches/20wins/mask32767 and zero caps/spending. Intent now: guarded reviewed
Resume accounting, then watched149. No existing result credit will change.

148 native handler is camera-only: RawInputData readsPAD_MOUSE_L, pointer events
update minimap/camera coordinates. It does not select battlefield minions. Remove
all temporary code-body capture and RVA reads.149 combines native device samples
with own-window WM_MOUSEMOVE/button messages, so both sampled and window-event
input paths receive the command. Earlier window messages alone did not reach
Framework; device samples alone reached it but did not select. All messages target
this client's window, with owned releases on stop; no global input/focus calls.
After isolated compile, reviewed development Resume may restart the exhausted
control attempt budget only for paused/pending0/sequence62/stage16attempts3/53/20.
Preserve result facts and zero caps. Expected149; selection remains unverified.

147 constructor read confirms the original vtable atRVA22C65D8 (LEA at16E1F62).
148 reads its declared ReceiveEvent entry and captures8192 bounded code bytes,
still without executing any inferred address or entering battle. Expected148.

146 loaded09:46:20, paused. Named factory resolves to executableRVA134850 and
calls constructor16E1F50 after allocatingCC0 bytes.147 follows that observed call
with a bounded read-only constructor snapshot, guarded by the factory identity;
no inferred native call is executed. Expected147; no admissions or input.

145 loaded09:44:07 after the battle ended; addon absent, pause preserved.
146 reads the named LovmMiniMap factory from declared RaptureAtkModule tables
while outside battle. This enables native code inspection without another
admission or failure-budget reset. Expected146; no live controls.

144 confirmed the current addon receiver is Dalamud's managed vtable wrapper.
145 uses public IAddonLifecycle.GetOriginalVirtualTable for the same read-only
bounded code capture. Still paused, no controls/admissions. Expected145.

143 loaded09:41:15 and stayed paused; no handler bytes captured at constructor
time.144 defers this single read to the normal service framework update and
reports why a handler is unavailable. No control dispatch or Resume. Expected144.

142 loaded09:38:59, match62 admitted09:39:16 (third allowed attempt). Game's
GetCursorPos import is queried2-3times during the synthetic frames, but selection
still unverified. Failed09:39:51, no credit; saved campaign pause reached.
143 read-only intent: keep that pause and capture the declared LovmMiniMap
ReceiveEvent body, without invoking it, to trace the actual battlefield consumer.
No more admissions or budget reset. Expected143; existing53matches/20wins remain.

141 in-viewport flag remainsfalse through frame end, filtered coordinates/buttons
are valid, but no minion readback; failed09:35:58 without credit.142 intent:
also answer the game's own GetCursorPos import with the synthetic screen point,
only during the owned native frame on its thread. This is a read-result override
inside this process, not OS cursor movement or input to another application.
Count those queries to verify this separate position source is used. Preserve
all accounting/attempt limits. Expected142; no background victory claim.

140 positions/buttons reached filtered input with collision=none, but every
sample reports Cursor.IsCursorOutsideViewPort=true despite its valid synthetic
point. Failed09:34:10 with no credit.141 intent: give the existing native cursor
that point's in-viewport flag during the device sample and restore the original
flag at frame end, with the device buffer. This changes no OS cursor or window
focus. Preserve abandoned61 and all admission budgets; expected141.

139 device samples propagate into Framework and UI with the requested positions
and buttons, proving the source boundary. No minion selection yet; failed
09:32:41 without result credit.140 intent: supply a neutral positioning frame
with correct cursor delta before the button press, matching the natural
move-then-click sequence, and capture filtered state/collision/outside flag.
Keep abandoned match identity and all accounting; no Resume/reset edits.
Expected140; background selection still unverified.

138 loaded09:27:52 and confirmed paused09:27:55. AtkModule.HandleInput capture
shows ordinary UI collision processing; changing its input is not sufficient.
Installed structs declare MouseDeviceInterface.GetData (virtual4), returning the
device's CursorInputData, and InputDeviceManager.MouseDevice.139 replaces the
unsuccessful AtkModule hook with this device getter, retaining natural processing
and restoring the device-owned buffer at native frame end. No raw event calls,
new signatures, OS mouse injection or focus mutation. Remove temporary code dump.
Intent after isolated compile: clear only the diagnostic development pause,
preserve all budgets/results (53/20, pending0, abandoned60), then test selection
and actual movement. No campaign win is claimed. Expected20260925-139.

137 native samples also produced no minion readback through frame end; failed
09:25:08 without credit.138 intent: pause the development run without changing
result/attempt facts, capture one bounded4096byte body of the already-declared
AtkModule.HandleInput for read-only control-flow inspection, and remain paused.
The supplied minidump contains globals/exception code but not this function.
No automatic battle retry while tracing the actual cursor consumer. Expected138.

136 loaded09:21:05, match60 admitted09:21:22, no selection verified and failed
09:21:57 without credit. Native cursor samples reached HandleInput with live
bindings, consumed0, but no minion readback changed. The UI boundary ended before
battlefield consumers could read the sample.137 intent: keep the transient UI
and Framework cursor samples through the same natural frame, restore them in
the declared Framework.Tick hook's finally, and retain only the immediate native
hit readback for the existing selection check. No OS input/focus calls. Resume
the existing physical battle without recreating abandoned60; if already outside,
normal admission/attempt gates apply. No further accounting edits. Expected137.

135 verified loaded09:11:01; reload dispatch blocked09:11:05 by the saved
campaign pause. Match59 had already stopped09:10:33 without credit; pending0,
53matches/20wins/mask32767/stage16attempts3. Returned to388 at09:14:46.
136 intent: remove all Verminion OS focus/cursor/global mouse code, including
the screenshot helper. Camera keys use ECommons messages to this process's
window. Use installed ClientStructs AtkModule.HandleInput at its normal frame
boundary, substituting/restoring only live cursor samples and change flags;
retain native bindings and event dispatch, never call a raw receiver manually.
This is a control candidate, not yet a verified selection or movement contract.
After isolated compile, apply reviewed development Resume only for the exact
paused/pending0/sequence59/stage16attempts3/53matches/20wins state, preserving
all result facts and zero purchasecaps/spending. Use the existing native readback
checks to stop on failed selection. Expected20260925-136, version0.5.0.3.

USER STEERING: the user is actively using the target PC. Background controls are
now required; do not focus its game window or move/inject the global cursor.
135 immediate containment intent: disable new OS battlefield mouse presses and
focus/cursor mutation while retaining release cleanup for already-owned input.
Current Stage16 match59 admitted09:10:03; preserve its identity/results, but a
control stop is not a result.134 loaded09:09:46 and started fresh from Minion
Square; abandoned58 remained uncredited (53matches/20wins). No pending result
was recreated. Next: research verified in-client/background commands. Never
restore the raw AtkInputData receiver experiment that previously crashed.
Expected20260925-135; unchangedversion. This replaces live focus authorization.


134 reconciliation intent:58 stopped09:06:31 with no result credit. The blank
field point projected successfully, but native own-window focus request was
rejected; no mouse input was sent. This is an input-availability failure,
not a strategy result or failed geometry search. Preserve abandoned58 and
stage16attempts2, totals53/20. No Resume or accounting edits. Reload once to
observe/close any existing result without recreating its admission. Carry the
source-prepared Stage15 final-reload probing fix. Expected20260925-134.
Do not force focus using another process or operate another client.


133 runtime loaded09:05:53/resumed Stage16 at09:05:57; native queue drained
09:06:02.58 is the second Stage16 admission. At reload twelve old Wuk units
occupied120capacity; new Succubus groups are assembling through the existing
queue. Preserve58 and its attempt budget. No purchases or config edits.
Source-only next134: final-phase party probing no longer depends on the
in-memory odinRegrouped flag, allowing a final-phase reload to find charged
parties too. Native phase/party readiness still gates every command. Do not
reload58 merely for this later-stage lifecycle fix; next marker134 on deployment.


133 intent: Stage16 match57 lost09:04:41; credited53/20, closed09:04:44.
One enemy stone remained3170HP. Native enemy roster consists of Wayward,
Storm, Serpent, Princely and Heavy Hatchlings, matching the guide's critter
army. Extend the verified Stage7 Succubus counter opening and native attack
buff to16. Preserve the next normal admission and stage attempt budget; no
Resume or result/config changes. If58 has begun, existing Wuk units consume
capacity while Succubus groups assemble; never invent a new admission.
Expected20260925-133, unchangedversion. Stage15's verified132 logic preserved.


Stage15 CLEAR:56 won09:00:26 (4:21), credited once52matches/20wins.
Single-minion selection09:00:05 exposed first native buff; a different party
selection09:00:15 exposed a second buff. Explicit You Win09:00:26 proves the
full24 Succubus/early gate recovery/one-third-health party rotation strategy.
Loaded132 is the proven Stage15 baseline. Closure09:00:29; Stage16 match57
admitted09:00:42/battlefield09:00:49. Stages1-15 cleared(mask32767).
Source matches132; both purchasecaps and spending remain0. Next milestone:
observe57's ordinary objectives/result, then16-18 and prepared19 boss logic.
Do not reload merely for documentation. Next deliberate marker133.
All817 tests passed under131;132 build and whitespace check passed.
No pending tool sessions. External baseline commit830a8dd remains unchanged;
current edits are unstaged. Preserve unrelated ChocoboRace change in that commit.


132 loaded08:57:54/resumed56 at08:57:58, preserving admission56 and attempt1.
At resume boss15777/17000HP; the first final cast now uses a single selected
friendly minion, followed by candidate rotation and native82 readiness checks.
Source matches132; build/diff checks pass, full817 tests passed under131.
No pending tool sessions. Next milestone:56's healed final phase and result.


132 intent before56 final phase: use the new single-minion selection for the
first final buff as well as subsequent probes. The official special contract
requires one selected member of a nearby charged party; avoid selecting the
entire army when only its special is requested. Leave existing attacks intact.
Preserve56/admission/attempt1 and all result/budget facts; no Resume mutation.
Expected20260925-132, unchangedversion.131 isolated/watched builds passed,
and current regression suite passed817/817. No stage15 clear yet.


131 runtime: loaded08:55:20, dispatched once08:55:23. Reviewed Resume persisted;
56 admitted08:55:31, first reviewed attempt. Totals51/19, mask16383 and caps/
spending0. Source matches131. Next: observe early recovery and additional native
party selection during final phase, then explicit result. All prior clears1-14
remain; no stage15 success yet. Changelog consolidated to current behavior;
detailed experiments remain in this checkpoint and Git. No build sessions pending.


131 intent:55 early retreat08:48:34 at10015HP, safely disengaged8650HP.
All24 healed08:49:15, finalphase08:50:13 at5605HP, buff08:50:15 at5374HP.
Only one final buff executed; lost08:50:45 with515HP, credited51/19.
Three-attempt pause08:50:59. Official guide confirms action readiness depends
on selecting a charged nearby four-minion party.131 selects another nearby
friendly candidate without issuing movement, then checks native82 readiness;
6.5sec buff spacing, bounded living roster probes. Avoid redundant final pursuit.
After build checks apply reviewed Resume only for pausedtrue/pending0/sequence55/
stage15attempts3/51matches/19wins/mask16383/capsandspending0, no later FULL STOP.
Preserve every result fact. Expected20260925-131, unchangedversion0.5.0.3.


130 runtime:55 admitted08:45:18/battlefield08:45:25.130 loaded08:46:18,
resumed55 once08:46:22. Pending55 is preserved, attempt3; totals50/19 and
clearedmask16383. No purchases. Await early recovery, full24 redeployment and
actual result before any further strategy change. Source matches130.


130 intent:129 loaded08:43:19/resumed54 at08:43:22, queue drained08:43:27.
54 withdrew08:44:25 at6510HP, but retreat latency left5228HP before units
escaped. Boss stopped moving then; all stones were destroyed08:45:01 with
5228HP still remaining. Defeat credited50/19. This disproves treating25% as
an exact final-phase trigger.53 crossed roughly33% at08:40:08 and lost33sec
later;54 crossed roughly33% during retreat and lost about30sec later.
130 recovers at60% to leave a withdrawal margin, then saves the healed army's
buff until boss<=one-third HP. Final party concentration also uses one-third.
Preserve next normal pending match and third-attempt budget; no Resume/reset.
Expected20260925-130, unchangedversion. Stage15 remains unverified.


129 intent:53 full24 departure08:38:28, firstbuff08:38:58. Early damage strong,
but finalphase08:40:25 had only11 deployed,4 atgate.53 lost08:40:45 with1874HP;
credited49/19, closed08:40:49.54 admitted08:41:02/battlefield08:41:09.
129 preserves54 and attempt2. Add one gate recovery below40%/above25%HP,
requiring24 living,95%HP,near gate and20sec before redeployment;90sec bound.
Use existing minimap camera path for15 after offscreen group diagnostic.
No Resume/accounting/config edits. Expected20260925-129, unchangedversion.
All817 tests passed128;129 isolated build precedes watched deployment.


128 runtime: loaded08:36:20, dispatched once08:36:24 after standard cleanup.
Reviewed Resume persisted. Match53 admitted08:36:32; result still pending.
48verified matches/19wins, clearedmask16383; both purchasecaps/spending0.
DebugDLL length1978880 (compiletimestamp08:34:18, then reused by watched build).
Source matches128. Observe full24 opening, native buffs and explicit result;
do not treat admission as success. No pending build/test sessions.


128 deployment intent:52 lost08:34:53 with1598HP, credited48matches/19wins;
closed08:34:57 and three-attempt pause08:35:07. The healed mixed army failed
three times. Deploy the prepared24-Succubus opening and6.5sec native buffs.
Apply reviewed Resume only for pausedtrue/pending0/sequence52/stage15 attempts3/
48matches/19wins/mask16383/capsandspending0, and no later FULL STOP. Reset only
pause and existing run/stage attempt budgets; preserve all result facts.
Isolated128/all817 tests/diff check pass. Expectedmarker20260925-128,
unchanged0.5.0.3, solely the existing watched DebugDLL path. New composition
is unverified; do not infer a clear from build or dispatch.


128 source-only preparation (all817 tests now pass): 127 loaded08:15:52/dispatched08:15:56.
50 admitted08:16:04. Gate regroup08:19:32 positively completed08:20:18 with
all twelve healed, but50 lost08:21:45 with2626HP; credited46/19.
51 admitted08:22:02, lost08:28:31 with357HP; credited47/19.
52 admitted08:28:47/battlefield08:28:54, third allowed attempt, still pending.
Do not reload this mixed-army battle merely to change composition.
The repository was externally committed as830a8dd at08:23:51; its contents
match the inherited127 work. No commit was made by this continuation.
Preserve that baseline, including the unrelated ChocoboRace change.
Source-only next composition uses24 owned Succubus82 before engaging Odin,
bounded six-request queue and6.5sec buff interval. Mixed-party/regroup logic
removed from source; old127 remains loaded. Isolated build and lifecycle5/5
pass, diff whitespace check passes. Wait for52's explicit result and normal
attempt pause before considering reviewed Resume. No purchases or budget edits.


127 source/test intent:48 lost08:05:51(3:59), boss1144; credited44/19.49 admitted
08:06:08. An unexpected same-marker126 reload occurred08:06:47/resumed08:06:50;
DebugDLL timestamp08:06:42, length1983488. It still lacks the source-only separate
wave fields; cause unresolved. Do not attribute that reload to127 or isolated
output.49 lost08:10:02(3:20), boss798; credited45/19, pause08:10:16.
Native final48 capture showed a Haurchefant and Alphinaud still travelling from
the gate; surviving Haurchefants also lost pursuit cadence to new departures.
127 separates wave/pursuit timers and tries one half-health gate regroup/heal,
requiring four of each party, >=95%HP and gate proximity before attacking again.
No healing minion or purchase is needed. Uses the already proven gate control,
but this phase is unverified.90sec recovery bound; preserve final buff reserve.
After isolated checks, apply reviewed Resume only for pausedtrue/pending0/
sequence49/stage15 attempts3/matches45/wins19/mask16383/caps0 and no later FULL
STOP. Preserve all result facts. Expected20260925-127, unchanged version0.5.0.3.

Current as of 08:03 -04:00,2026-09-25:126 is loaded08:01:01/dispatched08:01:04.
Stage15 match48 admitted08:01:17/battlefield08:01:26, second reviewed attempt.
The complete three-party opening reached240capacity before engagement; Alphinaud
special positively dispatched08:03:09.43verified matches/19wins, stages1-14
cleared (mask16383). No purchases, caps0. All817 tests and diff whitespace check
pass under126. Source matches watched DLL; no source-only changes or pending
tools. Next: observe48's final buff/DEF-down timing and explicit result before
changing its composition. Stages19/23 enabled but unverified;24 gated. Vendor,
tournament and remaining lifecycle runtime acceptance are still outstanding.

126 fresh test intent:125 loaded07:59:04/resumed07:59:08; native empty queue
reconciled07:59:13. Abandoned47 lost07:59:48(6:24), credited=False, totals43/19.
Closure07:59:52 stopped without recreating admission. Marker-only126 starts
the next normal attempt with all three parties assembled before engagement,
reserved final buff, DEF-down support, obscured-party skip, and queue-drain
reconciliation present from admission onward. Preserve attempt1/pending0 and
all facts/caps; no Resume/reset. Stage19/23 remain enabled but unverified.

125 intent:124 loaded07:54:04/resumed07:54:08.47 stopped07:56:02 when all four
Alphinauds were obscured by Gulool/Odin models; native collision was none, and
hit readback selected the wrong type. No credit;47 remains abandoned. All three
parties and both offensive/defensive specials had executed before that stop.
125 leaves an obscured boss group's current orders active, restores panels/input,
and permits the existing20/40second strategy cadence to reassess; six candidates
remain the per-order bound, and carrier identity/match timeout remain strict.
Also includes source-prepared reload queue reconciliation: observed139-element
LovmQueueList index102 changed1->0 as last Haurchefant spawned07:54:08/13, native
queue row disappeared and living count11->12. New requests wait for empty queue,
living orders continue, 90sec bound. Isolated pre-skip build passed.
Expected20260925-125; preserve abandoned47/counts/attempt1/caps0. No Resume edit.

124 intent:123 loaded07:52:40/dispatched07:52:43; reviewed Resume persisted;
47 admitted07:52:51/battlefield07:52:58.123 starts the three-party composition.
Before groups engage, add the guide's complete-army opening: hold movement
until four of each party exist, while allowing all reserved summons. A reload
with already-deployed units preserves their control. Expected20260925-124,
preserve47 and attempt1, no further Resume/count/purchase changes.

123 reviewed test intent:46 lost07:45:56(4:10), boss1255/17000; credited43/19.
Three-attempt pause confirmed07:46:10.45 had no result and remains uncredited.
Current cleared mask16383 (1-14), pending0, sequence46, stage15 attempts3,
caps/spending0. Corrected nameplate hiding passed repeated group selections.
The Gulool buff07:45:26 expired at the start of Zantetsuken.123 saves it between
50% and25%HP, adds four Alphinaud damage/DEF-down supports and reserves capacity
for four each of Gulool(20), Haurchefant(30), Alphinaud(10). All three pending
queues share the240 cap; relevant lifecycle test extended. Isolated build passed.
Before watched123, apply existing Resume accounting only if the exact paused
facts above still hold and no later FULL STOP exists. Preserve every result,
clear, purchase cap and spending fact. Expected marker20260925-123; version0.5.0.3.

122 intent:121 loaded07:37:42/resumed07:37:46.45 stopped07:38:20 after six
friendly-selection failures; no result credit. Captures07:38:19 show native
collision LovmNamePlate/node5 and Odin hit readback over nearby Haurchefants.
Temporarily hide/restore that existing HUD panel along with the others during
field input. Also keep Stage15 parties already in melee attacking, choose its
dense Haurchefant group, and capture native casts in bounded snapshots.
Isolated build/all817 tests passed before this final panel-list change. Preserve
abandoned45 and its attempts2; never recreate its admission. On reload observe
any existing battlefield/result, otherwise use the remaining normal admission.
No Resume or accounting edits. Expected20260925-122; Stage19/23 unverified.

121 intent:120 loaded07:31:36/dispatched07:31:40, preserving Stage15 match44.
Stage14 was positively credited before that admission (41matches/19wins).
44 lost07:35:04(3:06), boss1262/17000 after Zantetsuken; credited42/19 exactly.
45 admitted07:35:21, battlefield07:35:28. Eight Haurchefants lack final DPS.
121 uses Gulool546 attack buffs with four Haurchefant173 attackers, sharing
240 capacity and sending both parties onto Odin. Existing live Haurchefants
remain controlled while natural losses free room for the buff party. Preserve45
and attempt2; no resets/purchases/pause mutation. Also includes source-prepared
Stage19 Tora-jiro bunker/buff retreat and Stage23 Haurchefant boss attack.
Stage24 stays gated. Isolated pre-mixed build and lifecycle5/5 passed; watched
build will validate the final composition edit. Expected marker20260925-121.

120 deployment intent: Stage13 match42 won07:27:38, credited40/18, closed07:27:42.
Stage14 match43 admitted07:27:55/battlefield07:28:02; at07:30:51 only enemyB2255
remains and both friendly outer stones exceed3800. Deploy the prepared Stage15
Haurchefant composition now before its admission. Preserve43 and all result,
pause and purchase accounting; unchanged Stage14 orders reconcile on reload.
Expected marker20260925-120, same0.5.0.3. No settings or Resume mutation.

Stage12 CLEAR:41 won07:24:25(8:03), credited once39matches/17wins. Last central
stone remained strong throughout final burn. Closure07:24:28; Stage13 match42
saved07:24:41/battlefield07:24:48. Loaded119 is now the proven Stage12 baseline.
Source-only next120: Stage15 uses Haurchefant173 rather than Gulool546. Its guide
identifies Odin as a monster with65DEF; Haurchefant85ATK/poppet affinity is a better
fit than Gulool50ATK. Keep final25%-HP concentration, choosing a dense party.
Do not reload42 merely for this later-stage change. Build isolated and deploy at
an appropriate transition before Stage15; next marker120. Stages19/23/24 gated.

119 intent:40 lost07:15:33(4:19), credited38/16;41 saved07:15:49/battlefield07:15:56.
118 loaded07:16:30/resumed07:16:34. At07:18:49 five Haurchefants stood atB and
three atX10-11, while retained selection kept commanding the remote three even
when strategy chose the main group.119 only reuses selection within3yalms of
the chosen anchor; recalls distant survivors when no nearby threat, narrows
defense radius12->6yalms and flank detour6->2. Five minions moving together have
positive before/after readback07:16:46/48. Preserve pending41/attempts2/caps0.

Pending118 intent:40 is active; at07:14:17 StoneB4365, boss11494, all8 deployed
until casualties began. Concentration holds the stone longer but adds retargeting
and repeated movement can interrupt melee.118 keeps a live nearby target, holds
groups already within2yalms, and gathers replacements from their actual gate.
Routine movement snapshots keep minions/statuses/Lovm totals, omitting repeated
full UI dumps; admission/results/failures remain detailed. Include actual command
target names in strategy logs. Preserve40/counts/pause/caps; no new admission intent.

Current verified state as of 2026-09-25 07:10 -04:00:

- Loaded marker `verminion-control-20260925-117`, loaded07:10:28,
  dispatched07:10:32. Fresh Stage12 match40 saved07:10:40, battlefield07:10:47.
- Watched DLL `VERMAXION/bin/x64/Debug/VERMAXION.dll`: timestamp07:10:24,
  length1971200, version0.5.0.3. Source matches117; no source-only code remains.
- Stage12 now uses up to8 Haurchefants (30cost), concentrated on one surviving
  stone. It intercepts3+ nearby adds first, otherwise engages a nearby boss/add,
  and returns to the stone when no enemies are within12yalms. Native DEF special,
  flanking, live ground destinations, dense-group selection and panel cleanup
  remain. The previous Alphinaud/Gulool pursuit recipes failed.
- Verified totals37 weekly matches/16 wins; cleared mask2047 (stages1-11).
  Match38 exited without a readable result and remains uncredited. No purchases;
  both caps0. Reviewed Resume before117 reset only run/attempt budgets.
- All817 tests passed earlier this continuation. Current30-cost/no-gate
  lifecycle regression5/5 passes; current build and git diff whitespace check pass.
- Stage15 is enabled with owned Gulool groups and final25%-HP burst, but untested.
  Stages19/23/24 remain gated; ordinary stages through22 are implemented.
- Next: inspect40's actual defense, damage and result before changing/reloading.
  Preserve its saved admission. Next deliberate marker118. No pending tools.
- Remaining scope: Stage12 onward clears, capped vendor execution/accounting,
  CPU tournaments/rewards, first-time unlock runtime proof, partial-week/manual
  wins/reset/character matrix, FULL STOP and reload during each lifecycle phase.

Earlier iteration intents and observations (superseded by current state above):

-117 intent:39 lost07:06:34(3:56), credited37/16; attempt gate paused07:06:48.
  Source-only replacement Stage12 strategy uses up to8 owned Haurchefants,30cost,
  one surviving stone, adds priority when3+ are nearby, and boss engagement only
  within12yalms of that stone. Preserve flanking/ground commands and native DEF
  specials. Remove the wheel trial: input changed projections, but did not show
  useful wider battlefield coverage. Isolated build and lifecycle5/5 pass.
  Apply reviewed Resume only for pausedtrue/pending0/sequence39/stage12 attempts3/
  matches37/wins16. Preserve all result facts, clears and zero caps. Expected117.
-115 loaded07:01:47/dispatched07:01:51; match39 saved07:02:04, active. At07:04:07
  boss11434/12000 and StoneB4780; controls continue but camera/movement remains
  slow. The113 contact image covers only a small part of the board. Intent before116:
  eight ordinary mouse-wheel down notches once at Stage12 observation, then
  capture image/projection readback. Official guide documents wheel camera zoom.
  Normal own-window SendInput only; no raw native handlers. Isolated build passed.
  Preserve39 and third-attempt limit. Earlier Gulool buff has positive status962
  ATK Up readback06:57:51; native offensive execution is verified.
-114 loaded07:00:36/dispatched07:00:40.38 had ended without a readable result;
 07:00:45 service failed without credit. Saved36/16, pending0, stage12 attempts2,
 pausefalse. Intent before115: use the one remaining normal attempt with the
 corrected selection, ground/flank pursuit, buff range and cancellation cleanup
 from admission onward. Marker-only reload; no Resume/reset/config mutation.
-113 loaded06:56:40/dispatched06:56:43. Offensive special verified06:57:42,
  defensive special06:57:53.38 remains pending. At06:58:06 a replacement guard
  was selected while panels were hidden, then commands stopped. Source's dead
  enemy early return omitted restoring those panels, making later capacity reads
  fail. Intent before114: restore panels/input on dead target/group cancellation;
  use Gulool's ally buff within10yalms while closing. Preserve live38 and all
  accounting. No further composition change until this corrected run is observed.
-112 loaded06:53:26/resumed06:53:29.37 lost06:54:28, credited36/16. Match38 saved
  06:54:45, battlefield06:54:52. The contact image shows guards dispersed and
  no ready special. Review found the actual group click picked the furthest
  forward unit even when FollowBoss chose a dense cluster. Intent before113:
  click inside the densest four-yalm cluster; use ground coordinates for all
  enemy destinations, including tall Dullahan adds. Official controls specify
  right-click destination, not native enemy lock-on. Capture first selected
  attack-group contact. Preserve pending38 and second-attempt accounting.
-111 loaded06:50:00/dispatched06:50:04; fresh match37 saved06:50:11, battlefield
  06:50:19. Ground commands and retained-selection refreshes execute. At06:51:43
  a Gulool was104/480HP near the boss, showing the frontal approach still exposes
  it to crush; guards also chased the boss away fromB. Intent before112: flank
  around the front, keep guards on adds, and capture one contact image for native
  selection/ability readback. Preserve pending37 and its first-attempt accounting.
  Focused lifecycle tests5/5 and diff whitespace check passed under111.
- Stage12 match36 lost06:47:47, credited35/16; the three-attempt gate persisted
  pause06:48:01.110 loaded06:48:35 and correctly refused dispatch because paused.
  Intent before111: reviewed development Resume after ground pursuit/interception
  fixes. Require pausedtrue, pending0, stage12/attempts3, sequence36,35matches/16wins;
  reset only the run/attempt budget as Resume does, preserve results/clears/caps.
  This is a new bounded test of corrected controls, not automatic loss retry.
- 108 resumed34 at06:37:51; defeat06:38:08 credited once33/16. Fresh Stage12
  match35 admitted06:38:25. At06:41:56 boss remained10982/12000 and StoneB1590;
  ground pursuit from afar remains ineffective. Intent before109: direct attack
  from afar, rear reposition only when nearby and frontal. Prepare Gulool546
  (20cost, poppet ATK buff) for the next admission; the current live palette
  retains Alphinaud. Four Haurchefant guards remain.817 tests passed, including
  variable-cost reservations and no-gate living-unit orders; isolated build passed
  before the final pursuit/palette-continuity change. Preserve35 and all counts.
- Intent before108: correct Stage12 ground-only pursuit and continue living-unit
  orders while all gates are unavailable.107 deployed four Haurchefant guards at
  StoneB, but boss damage was only about1400 by06:37. The live match34 remains
  pending; preserve its accounting and resume it through the authorized reload.
  Isolated build passed. Expected marker20260925-108; no settings/count changes.
- Current marker target: `verminion-control-20260925-111`, version0.5.0.3.
  Build/reload is in progress after the reviewed Stage12 development Resume.
  Matches34/35/36 were verified defeats; saved totals35/16 and clears1-11.
  Research matches27/28/29 were abandoned and receive no weekly credit.
- Core control proof has live evidence: admission, owned palette setup, spawning,
  camera, gates/queue, single/group movement, combat, Execute Action, gate healing
  and transfer, structure attacks, explicit defeat and result closure. Stage1
  concluded and unlocked Stage2. Attempt77 won Stage2 at03:03:09 (You Win,3:02,
  150MGP). Distinct campaign progress must not be inferred from the raw native
  CompletedLoVMStages byte; it increased after a tutorial replay.
- Attempt 20 caused a native access violation in AddonLovmMiniMap.ReceiveEvent.
  The failed input-dispatch experiment was removed completely in build 21.
  The user restarted the target. Native mouse input now moves the selected unit
  after temporarily hiding obstructing HUD panels. No raw-input handler call
  remains. No further client crash has been observed through attempt 63.
- Mode/cap/progress models, UI, scheduler and bounded goal loops are implemented.
  Native participation5/5 completed without another admission in build79. Build80
  won twice and stopped exactly at wins3. Ten consecutive credited Stage2 wins
  verified through03:53:21, with exact stop at target10 on03:53:30. Build81 adds
  setup handoff/minion registration and sequential-menu challenge reconciliation.
  First-time setup, purchases, later campaign stages and tournaments remain pending.
- Worktree contains this task's uncommitted changes; no version bump or publication.
- Unrelated concurrent edits to ChocoboRaceService.cs appeared during this work;
  preserve them. A duplicate load of marker 63 was observed, so continue verifying
  the actual marker and current instruction after every reload.
- 2026-09-24: clean initial worktree, master ahead of origin/master by one commit.
- Target's enabled dev-plugin path is `z:\VERMAXION\VERMAXION\bin\x64\Debug\VERMAXION.dll`;
  automatic reload is enabled. This shared source output is the intended reload route.
- Selection `verminion_queue` remains saved. FULL STOP's saved pause still takes
  precedence. Latest credited totals: wins16/matches35, saved clears1–11(mask2047).
- Stage6 match20 lost05:21:47 (6:42), credit once across94/95 reloads; closure
  05:21:51. Add targeting/specials work, but the last stone fell before the boss.
- Defense strategy is deployed for its first Stage12 test;817 tests pass, including combined
  attacker/defender capacity reservations. It adds4 owned Haurchefants guarding
  the middle surviving stone, independent spawn confirmation and replacement,
  and defensive abilities. Palette preparation must occur outside battle.
- Next action: reconcile111's load/dispatch, then verify ground pursuit refreshes,
  guard interception and actual Stage12 result. Stage9 bomb sequence
  and minimap camera now have live proof. No Stage12 clear is claimed yet.
  Ordinary stone routes are implemented through22; unsupported boss mechanics
  (19/23/24) remain gated. Stage15 Gulool/final-burst logic is enabled but untested.
  No later clears are claimed.
- Bounded runtime evidence: `R:\XIVLauncher3\dalamud.log`, read with shared
  read/write/delete access. Crash record:
  `R:\XIVLauncher3\dalamud_appcrash_20260924_235358_501_7260.log`.

## Findings and next action

The old task selects a player battle and increments attempts after unknown exits;
neither is acceptable for the approved CPU bot. ClientStructs declares the palette,
completed stage count, LoVM objects and UI arrays, but no typed battlefield director
or battle-command interface. Capture live UI contracts before strategy investment.

Remaining acceptance: first-time setup and partial-week
runtime accounting, 24 campaign clears, tournament
operations, FULL STOP/reload matrix and character purchase accounting.

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

Attempt 20 loaded at 23:53:52 and entered the selection handler at 23:53:58.
The native call did not return to the next diagnostic; the target log stopped
advancing at that timestamp (only 7.9 MB, not the log-size cap). Selection and
movement are NOT proved. Suspected native crash/hang; do not repeat this path.
Attempt 21 intent: remove the experimental RawInputData dispatch entirely and
build a safe replacement that stops at the unknown movement instruction.
Expected marker `verminion-control-20260924-21`. No game process restart or close
is authorized/performed; live evidence availability must be reconciled afterward.

Crash record confirms C0000005 in AddonLovmMiniMap.ReceiveEvent, called by
VerminionGameInteraction.TryClickBattlefield. The entire experimental method and
its service dispatch were removed. Build 21 succeeded (existing NU1601 warning).
No marker 21 runtime acceptance is claimed while the target client is unavailable.

Post-removal verification: all 813 tests passed, zero failed/skipped;
`git diff --check` passed. ILSpy inspection of the actual output DLL confirms
the experimental TryClickBattlefield method is absent. Native input listener
observation remains read-only. Repository history advanced externally during
this session; preserve that history and the current uncommitted crash-removal fix.

2026-09-25: user reopened the target. Marker 21 loaded at 00:03:47 and dispatched
at 00:05:13, confirming replacement/restart. User-provided .tspack is not yet found
under mounted R:; exact location requested. Existing crash dump independently
resolved the failure: native function at module+634E00 indexes the binding table
at the declared InputData.Keybinds field (0x9B8). The crashed local input's
NumKeybinds and Keybinds were both zero; input 39 reads address 39*11 = 0x1AD.
Attempt 22 fixes this exact cause by copying the live declared AtkInputData base,
preserving vtable/binding state and validating the table, while modifying only
local transient samples. Intent: one left-click selection, capture, then stop;
no movement command. Expected marker `verminion-control-20260925-22`.

Attempt 22 loaded at 00:08:45. At 00:08:51 the corrected handler returned with
679 live bindings and no crash, confirming the specific null-table fix. However,
no selected-unit UI appeared; selection is unproved. Attempt 23 intent: disable
that no-effect action and read native tooltip labels on the observed LoVM controls.
Expected marker `verminion-control-20260925-23`; pending build/reload.

Attempt 23 read native palette tooltips: Battle List, Main Commands, Execute
Action, Trigger Trap, Withdraw and UI lock. Official controls confirm map left-click
moves the camera, while battlefield left-click selects units. Attempt 24 replaces
the no-effect direct minimap event call with two samples at the game's natural
PreReceiveEvent input boundary: one down and one release. Retains the native input
object and its bindings, does not move the host cursor, scopes dispatch to this
character/tutorial, and cancels/releases on cleanup. One selection attempt and
readback only. Expected marker `verminion-control-20260925-24`.

User could not locate the .tspack and explicitly approved proceeding with the goal
using the existing dump/logs while away. The authorized scope is unchanged.

Attempt 24 loaded at 00:16:26. Natural input down/up samples at 00:16:32
produced no observable unit/UI-array selection changes. Removed that temporary
listener and sample mutation. Attempt 25 uses standard WM_MOUSEMOVE/LBUTTONDOWN/
LBUTTONUP messages targeted to the current game process's own MainWindowHandle,
with a bounded 150 ms press and release on cleanup. No OS cursor movement or
other-window input. Intent remains one selection and readback only.
Expected marker `verminion-control-20260925-25`.

Attempt 25 sent both window messages successfully at 00:20:33 but selection
is still unresolved from available UI readback. The action-party list may not
reflect selection of a single unit, so attempt 26 follows selection with a bounded
right-click on the observed tutorial destination marker and checks unit position
and tutorial advancement. This tests the actual outcome instead of assuming the
selection display contract. Expected marker `verminion-control-20260925-26`.

Attempt 26 loaded at 00:22:48, dispatched at 00:22:51 and timed out at 00:23:55
without advancing the movement instruction. No result credited. Attempt 27 uses
the declared current scene camera projection, logs its ground-ray round trip and
compares the normal control-camera projection. It also samples the two declared
Framework cursor states at down/up to determine whether window messages reach
the battlefield controller. Same bounded selection/movement test; no raw receiver
calls or OS cursor movement. Expected marker `verminion-control-20260925-27`.

The initial 27 build inadvertently retained compiled marker 26 (the multi-file
patch had rejected atomically). Build/tests passed, but this is not verified marker
27. Fresh logs resolve its dispatch at 00:29:53: the previous tutorial had ended,
the task re-entered Stage 1 at 00:30:08. Corrected compiled marker to 27; next build
intentionally resumes that verified tutorial. Full suite: 813 passed, 0 failed.

Attempt 27 loaded at 00:30:55 and dispatched at 00:30:58. The current scene and
control projections agree; ray-ground readback is within 0.1 yalm of the marker.
At both clicks, Framework still reads cursor 1207,-32 and no held button, proving
PostMessage did not provide battlefield mouse input. Attempt 28 uses SendInput
only after the current process's game window is foreground and WindowFromPoint
confirms its client surface. It positions the OS cursor on the projected unit/
marker, bounds each press to 150 ms, and releases/restores on stop/unload or after
the movement snapshot. Restoration preserves intervening user movement. No other
client is addressed. Expected marker `verminion-control-20260925-28`.

Attempt 28 loaded at 00:34:31; foreground cursor positions now match both targets,
but Framework held buttons are still empty and tutorial timed out at 00:35:38.
Attempt 29 keeps the same bounded action solely to inspect OS button state,
UI filtered/unfiltered cursor samples, native UI collision and ImGui capture at
down/up. Also includes the previously omitted LovmNamePlate event contracts.
Expected marker `verminion-control-20260925-29`.

Attempt 29 loaded/dispatched at 00:38:59. At selection, OS, Framework and UI all
report LBUTTON, but UIFilteredCursorInputs is cleared. Native collision identifies
ChatLog node 16 over the hatchling. The destination has no UI collision and its
RBUTTON reaches the filtered input. ImGui capture is false. Attempt 30 temporarily
hides the existing ChatLog addon using declared Hide without a close callback,
restores it after the snapshot or Stop/unload, and repeats selection/movement.
Expected marker `verminion-control-20260925-30`.

Attempt 30 loaded at 00:40:27. Chat hiding worked, exposing a second overlapping
surface: LovmPalette node 4 also covers the hatchling at this window size/camera.
Attempt 31 temporarily hides/restores both observed obstructing addons for the
same movement proof. Destination remains unobstructed. Expected marker
`verminion-control-20260925-31`; no game result credited.

Attempt 31 loaded at 00:41:25. Both mouse buttons reached unfiltered battlefield
input. The hatchling moved from the gate to (-2.73, 8.44), near marker (-3, 8),
but the same tutorial instruction remained at 00:42:31. Selection/movement now
have positive positional evidence; destination acceptance is unresolved. Attempt
32 requests one game-native screenshot, copies only that fresh image into this
client's existing plugin-config folder for visual inspection, then stops. It does
not issue another movement command. Expected marker `verminion-control-20260925-32`.

Attempt 32 loaded at 00:45:32, requested a native screenshot successfully, but
its thread directory field did not identify an accessible directory. No image
was copied, so visual readback is still unverified despite the generic stop text.
Attempt 33 additionally reads the declared private ScreenShotLocation (read-only
0x78 FileAccessPath field, verified in installed structs) and photo result before
copying the fresh native image. Same diagnostic-only action, no movement.
Expected marker `verminion-control-20260925-33`.

Attempt 33 reports native screenshot error 4: the configured screenshot directory
does not exist on the target. Attempt 34 scopes that one native photo to the
existing authorized plugin-config folder via the declared screenshot thread path,
then restores the original in cleanup. No persistent game setting is changed.
Removed private-field inspection. Expected marker `verminion-control-20260925-34`.
Fresh full suite after control changes: 813 passed, 0 failed/skipped.

Attempt 34 loaded/dispatched at 00:49:04; native photo error 4 remained. Its
request re-resolves the configured directory, so changing the thread copy is
insufficient. Attempt 35 uses the existing named ScreenShotDir ConfigEntry setter
to point the one capture at the plugin directory, saves the exact original string,
and restores it after capture/Stop/unload. This temporary existing game preference
is not a new configuration schema or logger. No selection/movement is dispatched.
Expected marker `verminion-control-20260925-35`.

Attempt 35 dispatched at 00:51:24 just before the tutorial returned to Minion
Square at 00:51:26; photo outcome is unresolved, cleanup restored the preference.
Attempt 36 re-enters the tutorial through the proven route, completes the proven
opening instructions and requests its image only on the hatchling movement prompt.
It stops after image verification, without movement. Expected marker
`verminion-control-20260925-36`.

Attempt 36 repeated the opening successfully but produced no image at 00:55:18.
Native screenshot availability/request state remains unresolved. Removed native
photo scheduling and temporary configuration changes. Attempt 37 takes one GDI
snapshot of this process's foreground game client rectangle, using existing
Windows APIs and no new dependency. The single BMP is overwritten in the target
plugin folder; capture ends the attempt without movement. Expected marker
`verminion-control-20260925-37`.

Attempt 37 succeeded at 00:57:12: one 624x441 GDI image is available as
`R:\XIVLauncher3\pluginConfigs\VERMAXION\verminion-control.bmp`. It confirms severe
world pixelation and chat/palette covering much of the field. A yellow patch is
visible near screen (320,207), which differs from the EventObj projection (205,161).
Attempt 38 repeats only this image with the proven temporary chat/palette hiding,
restoring both in cleanup. This resolves the visible destination before another
movement command. Expected marker `verminion-control-20260925-38`.
The original ScreenShotDir is verified unchanged in the native client config.

Attempt 38 captured the field without chat/palette at 00:59:31, but the world is
rendered at 5% resolution by this client's CustomResolution2782 configuration.
Inspected the installed 1.1.6.0 plugin: its registered /gres command sets gameplay
scale and saves it. Attempt 39 reads the exact prior scale from that existing
configuration, uses /gres 1 only for the single capture, and restores the saved
scale through the same command on cleanup/Stop/unload. No new setting/dependency.
Expected marker `verminion-control-20260925-39`; still no battle movement in this
visual inspection attempt.

Attempt 39 captured a clear image at 01:03:49. Verified the custom game scale
restored to 0.05 in that client's plugin configuration. The yellow circle is at
screen center (312,214), straight ahead of Gate B; EventObj 2005110 is not that
destination. Attempt 40 restores the movement proof, projecting world (0,0,12)
to the observed circle and checking the tutorial transition. No photo or graphics
setting change in this attempt. Expected marker `verminion-control-20260925-40`.

Attempt 40 loaded/dispatched at 01:05:15. Projection (0,0,12) matches the visible
circle at (311.6,212.5); hatchling reached (-0.015,11.948) at 01:05:32. Tutorial
advanced at 01:05:45 to: "Left-click and drag the cursor to select all the
hatchlings, then use right-click to move them to the yellow circle." Four hatchlings
are now around (0,12). Selection and movement are positively verified. Attempt 41
adds one bounded clear image before stopping at any unhandled tutorial instruction,
using the already verified scoped graphics/panel restore. This captures the next
destination without guessing. Expected marker `verminion-control-20260925-41`.

Attempt 41 captured the group instruction at 01:07:43. The next yellow circle is
screen (415,163), corresponding to world (3,0,8). Four hatchlings are visible near
(0,12). Attempt 42 computes a selection rectangle around those four observed unit
positions, holds left, drags to its opposite corner, releases, then moves the group
to (3,0,8). Every pointer operation validates this process's foreground client
surface; stop/unload releases input and restores panels/cursor. Expected marker
`verminion-control-20260925-42`. Unhandled next instructions now capture one image
before the attempt stops.

Attempt 42 loaded/dispatched at 01:10:12 and timed out at 01:11:18. The drag's
bottom-right endpoint (411,232) overlaps LovmMiniMap node 16, clearing the filtered
button/release sample. The destination right-click remains unobstructed. Attempt
43 reverses the same selection rectangle (top-right to bottom-left) to finish on
clear field, then records selection state before moving the group. Expected marker
`verminion-control-20260925-43`.

Attempt 43 reached clear endpoints and both button samples passed the UI filter,
but the group still did not move; timeout at 01:13:56. Attempt 44 replaces cursor
warping during the held drag with a real SendInput mouse-move sample (absolute
virtual-desktop coordinates, still guarded to the own game client), so the game
receives motion deltas as well as position. Captures one image while the selection
drag is held before release, then checks movement/advancement. Expected marker
`verminion-control-20260925-44`.

Marker 44 loaded at 01:15:52 and dispatched at 01:15:55. The previous tutorial
had returned to Minion Square; this load re-entered at 01:16:10. It is replaying
the verified opening before the drag test. Do not judge the existing BMP as a
new held-drag capture until a matching fresh image timestamp is logged.

Attempt 44 replayed single-unit movement successfully and drew a visible selection
rectangle during the group drag at 01:17:50, but the group did not move. The 5%
CustomResolution override may affect the game's object picking. Attempt 45 prepares
normal gameplay rendering before any battlefield input and retains it through the
proof, then restores the original scale on Stop/unload/failure. Panels and cursor
still restore after each movement command. This isolates the suspected resolution
interaction; it does not claim that cause is proven. Includes the pending tooltip
owner-path observation improvement. Expected marker `verminion-control-20260925-45`.

Attempt 45 drew a correct visible rectangle enclosing all four hatchlings at full
rendering, but timed out at 01:21:15. Normal rendering did not resolve selection.
Tooltip owner paths now identify Execute Action as LovmPalette node 82 (param 2),
Trigger Trap node 81 (param 3), and Withdraw node 85 (param 4).
Attempt 46 tests the official alternate group control: select one hatchling,
click that selected unit again to select nearby units of the same type, then move
to the observed circle (3,0,8). Uses its live object ID/position for the second
click, with bounded press/release and normal rendering retained for this isolated
test. Expected marker `verminion-control-20260925-46`.

Attempt 46 passed group selection/movement: all four hatchlings reached the circle
around (3,8) at 01:23:58. The tutorial spawned two Kidragora at (-3,8), (-3.6,8.4)
and advanced at 01:24:08 to "Select the wayward hatchlings and send them against
your foes." Attempt 47 removes the unsuccessful drag path, uses the verified
same-type selection on the leftmost hatchling (away from the right-side HUD), and
right-clicks the observed enemy position. Expected marker
`verminion-control-20260925-47`; no game result has yet been credited.

Attempt 47 loaded at 01:27:34 and dispatched at 01:27:37. All four hatchlings
engaged the two Kidragora; enemy HP fell to 210 at 01:27:51, and the tutorial
advanced at 01:28:11 to defending Arcana Stone B. Two new Kidragora appeared at
(1,0,1) and (1.4,0,0.7). Attempt 48 applies the verified group attack to that
observed instruction. Expected marker `verminion-control-20260925-48`.

Attempt 48 compiled before the new instruction handler was applied (a rejected
multi-file patch); it has no new combat behavior. Attempt 49 includes that handler.
Expected marker `verminion-control-20260925-49`, same bounded Stone B test.

Attempt 49 defended Stone B and advanced at 01:32:42 to "Send the wayward
hatchlings to defeat the behemoths." Two Baby Behemoths were observed at (-3,8)
and (-3.7,8.3). Attempt 50 targets those observed foes. Expected marker
`verminion-control-20260925-50`. Fresh suite: 813 passed, 0 failed/skipped;
`git diff --check` passed. No weekly match or challenge result credited.

Attempt 50 engaged the Baby Behemoths and advanced at 01:34:20 to selecting an
individual hatchling and clicking Execute Action. Four hatchlings remain; one
behemoth remains at 268 HP. Attempt 51 selects one hatchling, releases input and
restores the palette, then invokes its observed node 82 ButtonClick (param 2).
Expected marker `verminion-control-20260925-51`; special action is not yet verified.

Attempt 51 loaded/dispatched at 01:35:21/24 but stopped at 01:35:31 because the
palette action was unavailable immediately after Show. Attempt 52 restores the
palette when the selection mouse button is released, allowing a frame before
the native action click; captures the visible palette before that click.
Expected marker `verminion-control-20260925-52`.

Attempt 52 dispatched the action button at 01:36:51, but no advancement followed.
The readback shows the Baby Behemoth's 268/430 HP in the selected/hovered unit
panel, indicating the first hatchling's click overlapped the enemy model. The
official guide confirms the button executes directly without ground targeting.
Attempt 53 selects the rightmost hatchling (clear of the behemoth), captures the
selection before action, then tests Execute Action again. Expected marker
`verminion-control-20260925-53`.

Attempt 53 dispatched at 01:38:36 but still did not advance. The unit detail
array still describes the behemoth; it may reflect hovering instead of actual
selection. Attempt 54 captures the visible UI immediately before Execute Action
and checks/logs the native button's enabled state, avoiding further blind dispatch
against a disabled control. Expected marker `verminion-control-20260925-54`.

Attempt 54 proves Execute Action disabled after selection (01:40:13). Reviewing
input ordering found the ability path restored the cursor outside the game in
the same frame as mouse-up, before the game could sample release. Attempt 55
leaves cursor/panels untouched until the next one-second step, captures selection,
then restores panels and clicks on the following step. This matches the verified
movement path's release ordering. Expected marker `verminion-control-20260925-55`.

Attempt 55 passed Execute Action at 01:42:25 (enabled=True). At 01:42:41 the
tutorial confirmed the special defeated the behemoth and advanced to healing
all four hatchlings at a gate. Lovm array 17 becomes a unit object ID during
successful selection; fields 18-24 describe that unit. Attempt 56 selects the
group, moves the camera backward for one second to frame friendly Gate B at
(0,0,22), then sends the group there. Current camera projects that gate below
the client area, so the framing step is necessary. Expected marker
`verminion-control-20260925-56`.

Attempt 56 framed Gate B successfully at screen (312,230) and moved all four
hatchlings to its ground position at z22. HP did not recover and the instruction
timed out at 01:45:45. The guide says minions must enter a gate; ground movement
near its object may be insufficient. Attempt 57 only captures the current gate
view and stops, without repeating movement, to identify the entry target.
Expected marker `verminion-control-20260925-57`.

Attempt 57 found the old instance expired and re-entered at 01:47:27. It replayed
every opening/combat step through the special instruction without intervention,
but selected the overlapping behemoth at 01:50:50 (Lovm[17] now an enemy ID) and
correctly rejected the disabled action. Attempt 58 chooses the hatchling farthest
from the behemoth, adds actual object IDs/battalions to existing snapshots, and
will frame/capture the gate after the special succeeds. Also includes stage-named
duties in existing setup observations. Expected marker `verminion-control-20260925-58`.

Attempt 58 still selected the behemoth despite choosing the most separated
hatchling. It verified CharacterData.Battalion=0 for friendly hatchlings and =1
for the enemy, and Lovm[17] exactly matches GameObjectId of the selected unit.
Attempt 59 replaces the special's near-ground click with the declared native
GetCenterPosition output, retaining the working mouse timing. Expected marker
`verminion-control-20260925-59`.

Attempt 59 selected a friendly hatchling using native GetCenterPosition and
executed its special at 01:54:15; tutorial advanced to healing at 01:54:34. The
gate capture at 01:54:39 shows a floating marker above the ground circle. Attempt
60 uses Gate B's native center for the right-click, panning backward only when
that target projects outside the client area. Expected marker
`verminion-control-20260925-60`.

Attempt 60 verified healing at 01:57:01: every hatchling reached 410/410 HP and the
tutorial advanced to sending them to another gate. Native Gate B center is
(0,0.31,24), versus ground position (0,0,22). Attempt 61 selects the healed group,
frames Gate A with the Move Left binding if needed, and right-clicks its native
center. Includes a result-window observation branch before battle-state checks;
that branch records no completion until result interpretation is verified.
Expected marker `verminion-control-20260925-61`.

Attempt 61 moved the camera left, but Gate A still projected outside the window
at x=-129 after one second; it stopped at 01:59:07 without clicking off-window.
Attempt 62 frames the group and destination from projected bounds using half-second
camera steps, with a maximum of eight adjustments per group command. It preserves
the verified click/release sequence and checks both stages before input. Expected
marker `verminion-control-20260925-62`.

Attempt 62 framed both positions, but LovmPartyList intercepted the selection
clicks at (472,241); filtered input was cleared and the group stayed at Gate B.
Attempt 63 temporarily hides/restores the party list and minimap alongside the
already handled chat/palette panels. Still restores only original live instances
and only panels that were initially visible. Expected marker
`verminion-control-20260925-63`. Fresh suite after camera framing: 813 passed,
0 failed/skipped; `git diff --check` passed.

Attempt 63 loaded/dispatched at 02:03:32/36 and transferred all four hatchlings
from Gate B to Gate A. At 02:04:00 the tutorial advanced to attacking enemy Arcana
Stone B. The same marker loaded again at 02:04:07 and observed the new instruction;
there was one dispatch per load and no repeated gate order. Attempt 64 targets the
observed enemy stone (base 2006537) using its native center. This longer walk/attack
step has a bounded 180-second instruction timeout. Expected marker
`verminion-control-20260925-64`.

Attempt 64 moved the group through the stone's attack area and the tutorial
confirmed attacking at 02:06:27, then issued "Attack the enemy's Arcana Stone B."
The elevated center projection maps to ground (3.6,0,-11.4), so units continued
beyond the stone. Attempt 65 handles the attack instruction and uses the stone's
ground position (0,0,-2), retaining its 180-second bound. Expected marker
`verminion-control-20260925-65`.

Attempt 65 held the hatchlings at the stone and advanced at 02:08:52 to destroying
the enemy Shield using four newly spawned Cherry Bombs. The enemy Stone B HP
fell from 4885 to 2995. Attempt 66 generalizes same-type group selection for those
bombs, uses native centers for floating minion selection, and frames points below
the top HUD with small edge margins. It sends the bombs two yalms in front of the
observed enemy Shield (8,0,-16). Expected marker `verminion-control-20260925-66`.

Attempt 66 loaded at 02:11:44 and dispatched at 02:11:47. The old instance ended
at 02:11:50 before its next command, causing a safe failure at 02:11:52 with no
match or clear credited. Attempt 67 captures unexpected-end state before stopping
and starts a fresh tutorial replay to exercise the accumulated controls and the
pending Shield command. Expected marker `verminion-control-20260925-67`.

Read-only preparation while attempt 67 replays: installed Lumina exposes minion
HP/cost/type on Companion, and Attack/Defense/Speed/HasAreaAttack/StrengthArcana/
StrengthShield/StrengthEye/StrengthGate plus special text on CompanionTransient.
The public XIVAPI ContentsNote sheet identifies participation rows 66 (1 match),
47 (3), and 67 (5); it contains no Verminion victory challenge. Native ContentsNote
has completion bits and selected-tab DisplayIds/DisplayStatuses, which still need
live interpretation before partial-week accounting. Existing RegisterRegistrablesService
uses Plugin.UnlockState.IsItemUnlocked and GameHelpers.UseItem for registration;
reuse those contracts. No setup/progress behavior was added during this research.

Attempt 67 replayed all implemented instructions uninterrupted from admission at
02:14:25 through Shield destruction at 02:20:40. The fresh 02:20:45 image shows
"You destroy Arcana Stone B!" while stopped at "Shatter Arcana Stone B completely."
No result has been credited. Attempt 68 handles that observed instruction with
the existing stone attack and a 180-second bound. Reload's initial observation
will reconcile the current instruction before any further command. Expected
marker `verminion-control-20260925-68`.

Attempt 68 loaded at 02:25:14, dispatched once at 02:25:17, and found authoritative
CompletedLoVMStages=1 in Minion Square. Fresh recent system messages include the
tutorial conclusion and the next challenge becoming available. This establishes
an observed Stage 1 clear from attempt 67; the service has not credited weekly
participation or wins. Attempt 68 began another tutorial replay. Attempt 69 adds
bounded observation of Dalamud's existing duty completion event and broader
completion-text capture, without interpreting that generic event as a victory.
It will resume the current tutorial before the final result. Expected marker
`verminion-control-20260925-69`.

Attempt 69 loaded/dispatched at 02:28:36/39, continued the group instruction and
reached the second Stone B attack. Stopped safely at 02:32:23 after the shared
8-step camera bound was exhausted: five steps to frame the unit at Gate B plus
three toward the stone, which still projected at y118 (required y120). Only one
hatchling had transferred to Gate A in this replay, so group-selection reliability
remains open despite tutorial advancement. Attempt 68's earlier group failure
was caused by HowTo intercepting input. Attempt 70 includes HowTo in scoped panel
cleanup and bounds each selection/destination framing leg independently at eight.
It resumes the live tutorial; when outside it chooses Stage 2 after the observed
permanent tutorial clear, verifies the native stage-specific queue/title, and only
captures Stage 2's initial battlefield. No strategy or result credit added yet.
Expected marker `verminion-control-20260925-70`. Tests: 813 passed on build 69.

Attempt 70 resumed at 02:35:27 and destroyed the Shield, then unnecessarily tried
to select a straggler at Gate B when the active attackers were already shattering
Stone B. Full-field framing exhausted eight half-second steps at 02:36:29.
Attempt 71 treats that narration as waiting on the existing attack, skips another
stone order when a hatchling is already attacking, and allows at most sixteen
half-second camera steps per framing leg for a full-field traversal. Also captures
AtkValue strings to interpret result/selection UI. If the tutorial has concluded,
it will enter and inspect Stage 2. Expected marker `verminion-control-20260925-71`.

Attempt 71 loaded/dispatched at 02:37:52/55. The tutorial had ended, and the raw
CompletedLoVMStages byte was now 2 despite replaying Stage 1. Do not treat that
field alone as distinct campaign progress: its semantics remain uncertain. The
fresh tutorial conclusion establishes Stage 1 only. Stage 2 admission succeeded
at 02:38:11 (CFC553), with the same palette gate controls and initial capacity0/60.
Attempt 72 resumes this existing empty Stage 2 match and observes its intentional
CPU loss for at most600seconds. It will capture the normal result UI without
crediting a win or completion. Expected marker `verminion-control-20260925-72`.

Attempt 72 observed Stage 2 defeat at 02:42:13. LovmResult node3 explicitly reads
"You Lose", with Quit button node48/ButtonClick0. Lovm number fields6,7,8 are zero,
and9,10,11 retain5000, supporting friendly/enemy stone HP grouping. Raw completion
byte stays2 after the defeat. No victory, match or weekly completion was credited.
Attempt73 recognizes only this observed defeat text, leaves through the observed
Quit control, and captures the existing Challenge Log via its native agent. Reload
cleanup may already close the result; outside battle this build goes directly to
Challenge Log inspection, then stops. Expected marker `verminion-control-20260925-73`.
Added per-character mode/cap/progress models and a focused lifecycle regression:
unknown/duplicate/abandoned results, partial-week reconciliation, exact victory
remaining, reload, clone isolation, reset and cumulative cap boundaries. Runtime
accounting is not yet connected; no purchase behavior or mode UI was added.

Attempt73 reached Challenge Log inspection at02:46:34. Native ContentsNote was
Loaded, default Battles tab1, and participation flags66/47/67 were all true.
Attempt74 selects the observed Gold Saucer category through the existing native
list helper, waits two seconds, captures matching progress rows, then closes its
owned log window and stops. Expected marker `verminion-control-20260925-74`.
Fresh full suite814 passed, zero failures/skips; git diff --check passed.

Attempt74 selected Gold Saucer via native list row9 and read Loaded tab10 at
02:49:11. Rows66/47/67 report1/3/5, matching all three completion flags. Attempt75
reconciles that participation into per-character progress without adding victories.
It prepares owned Wind-up Wuk Lamat in an empty palette slot (no purchase), then
queues Stage2. A separate C# opening decision class requests six cost10 Arcana
attackers at B, A, C in order, observing capacity and actual spawned groups before
each movement. Each group command first clicks verified empty field to clear old
selection, then uses the existing two-click control. Input guards now allow only
verified CPU duties552-579. One90second bound per deployment and600seconds per
battle; results remain uncredited and unrecognized victory text stops for inspection.
Expected marker `verminion-control-20260925-75`.

Attempt75 loaded at02:53:29 and dispatched once at02:53:32. Stage2 admitted at
02:53:54. Six Wuk Lamats spawned and all six moved forward after the new blank-field
selection reset. Six more spawned at GateA, but camera framing oscillated sideways
at y763-891, exhausting the bound at02:55:09. Attempt76 fixes vertical framing
before horizontal framing, preventing near-camera perspective magnification from
causing that oscillation. It also reads the observed stone HP fields6-8/9-11 and
reconciles an already destroyed objective or six minions already at that stone
before deploying another group. The live match remains unresolved; do not credit
any result from its partial attacks. Expected marker `verminion-control-20260925-76`.

Attempt76 loaded/dispatched at02:57:49/52. Enemy StoneB was already destroyed;
this was recognized, and the opening advanced to the six waiting at GateA. The
old match ended before their movement completed: explicit defeat at02:58:04,
Quit dispatched, and result closure verified in Minion Square at02:58:08. No
victory or weekly completion credited. Attempt77 repeats the same opening in a
fresh Stage2 battle, including the corrected camera ordering, to assess a full
three-gate deployment. Expected marker `verminion-control-20260925-77`.

Attempt77 dispatched at02:59:20 and admitted at02:59:40. It deployed all three
groups without another camera failure. The03:01:42 readback shows enemy StoneB
already destroyed, but identifies mirrored enemy labels: enemyA is x+16, enemyC
x-16, while friendlyGateA is x-16. The current opening sent its side groups across
the field. A source-only correction now maps GateA to enemyC and GateC to enemyA,
including their HP indices. Do not build until attempt77's current result has been
captured; the live marker77 still runs the original crossed side routes.

Attempt77 WON Stage2 at03:03:09. LovmResult node3="You Win", node23="Match Length:
3:02", value18=150MGP. Enemy stone fields9-11 are all0; friendlyB335 andC4625 remain.
The proof stopped for interpretation and credited no weekly win. Source now reads
both observed You Win/You Lose texts. The source-only side-route fix is also pending
build. Match still may be on its result screen; reconcile fresh state on reload.
The generic DutyCompleted event did not produce an observed LoVM event, so result
UI remains the positive match-evidence source. Full suite814 passed after build77.

Attempt78 will include mirrored side routes and recognize both observed result
texts. Normal match admission now saves an identity before crediting results;
matching duty/result evidence credits exactly once, unknown/failure/real FULL STOP
abandons the pending identity, and ordinary unload preserves it for reconciliation.
Existing orphan result77 will not be retroactively credited without saved admission.
Winning run attempts and consecutive losses persist across reload; limits initialize
once per week/mode/target and require Resume after exhaustion. The current proof
still stops after one result; goal loops/UI remain pending. Focused lifecycle tests
pass with three-loss and six-unresolved-attempt limits, including reload continuity.
Expected marker `verminion-control-20260925-78`; goal is one new accounted win.

Attempt78 WON at03:12:28 with match length2:32. The saved admission was credited
exactly once: matches6/wins1. Result closure verified03:12:31. This is the second
consecutive Stage2 win (first with connected accounting), now using direct side
routes. No other stages or tournament outcomes were inferred.

Attempt79 intent: connect the weekly mode UI, settings-only defaults copy,
mode-aware scheduler and result loop. Preserve existing Participation selection;
this already-complete character should read5/5 and finish without another admission.
Win X loops only credited results and stops exactly at its target or saved bound.
CPU rewards stays independently eligible but reports its unimplemented interactions.
Purchases remain disabled; caps are exposed with cumulative spending. Expected
marker `verminion-control-20260925-79`; pending build/test/load verification.

Attempt79 loaded03:19:28, dispatched once03:19:31, completed at03:19:36 after
native participation readback; no new match admitted. Debug build passed and full
suite815 passed. Attempt80 selects Win X=3 for only the saved debug character in
the authorized client's existing account config, retaining its wins1 and all other
progress. Intent: two consecutive matches, exact stop at wins3, six-attempt cap.
Expected marker `verminion-control-20260925-80`; no purchases or other clients.

Attempt80 loaded03:20:47, dispatched once03:20:51. Match2 admitted03:21:11 and won
03:24:09 (2:32), credited matches7/wins2. The loop admitted match3 at03:24:32 and
won03:27:27 (2:29), credited matches8/wins3. Closure and native participation read
finished03:27:36 with Weekly victory target reached. No extra admission. Full
suite815 passed with mode boundaries, settings-only copy and stage reconciliation.

Attempt81 intent before build: select Win X=10 only on the saved debug character,
preserving its wins3 and all other state. Seven further wins should finish the
reliability run (already four consecutive observed, three saved). Include setup
quest handoff and minion-only inventory registration, ownership cleanup on Stop,
and sequential challenge-menu progress reconciliation. Menus in attempt80 showed
exactly stages1/2/3 after the Stage2 victories. Earlier menu offered only1/2; this
supports marking preceding stages from the highest sequential available entry.
Stage24 is never inferred from availability. Tutorial exits must verify Stage2
unlock before continuing. Character changes now abandon only the original owner's
pending identity. Expected marker `verminion-control-20260925-81`, pending build.
First-time unlock execution cannot be proved by this already-unlocked character.

Attempt81 built successfully, loaded03:29:50 and dispatched once03:29:53. No
unregistered inventory minions were found; native participation and prerequisite
inspection proceeded. Sequential menu reconciliation recorded clears1/2 at03:30:03;
match4 admitted03:30:14. The seven-match Win X run remains active; do not rebuild
until its outcome is resolved. Source-only changes AFTER build81 add the separate
campaign action, saved stage-attempt cap, debug Resume preserving the chosen goal,
and Stage3's shared stone opening. Those changes have NOT been plugin-built or
live-tested. Stage4+ deliberately stop before admission while strategy work remains.
Next verification is an isolated Debug compile with OutputPath
`VERMAXION\bin\x64\VerminionCheck\`, which does not touch the watched Debug DLL
or reload the client. Full suite815 and diff whitespace checks passed for this
source. The loaded runtime remains marker81's weekly loop; campaign is not loaded.

Isolated compile passed at03:34:26. Watched Debug DLL timestamp remained03:29:46,
confirming no runtime replacement. Subsequent source review corrected dashboard
completion/next-eligible display after mode changes and retained progress while
paused. Recompile only the same isolated output for these final source changes.
Current tests:815 passed; git diff --check passed. The ordinary Debug build remains
81 and the next deployed marker must be82 or later.

Build81 runtime milestones: match4 won03:33:11 (2:31), credited matches9/wins4;
match5 won03:36:33 (2:33), credited matches10/wins5. Both closed and continued.
Match6 admitted03:36:57 and was unresolved in the latest bounded snapshot. This
is six consecutive observed wins since build77, five saved; target10 requires
five further credited wins. No first-time quest/item use, later campaign clear,
tournament, live FULL STOP/reload matrix or purchasing acceptance is claimed.

Final isolated compile of the current source passed at03:40:02, with only the
existing NU1601 PInvoke warning. Watched DLL still03:29:46; no reload performed.
No tool sessions remain pending. Goal remains active; next action is resolve the
ongoing build81 farming run before deploying campaign work.

Research for later campaign work (no later-stage strategy implemented):
https://ffxiverminion.com/stage3-the-first-move repeats the three-stone opening.
https://ffxiverminion.com/stage4-little-big-beast describes a Goobbue boss without
enemy structures; follow it between friendly stones, use damage groups/healing,
and handle its frontal knockback. These are external guidance, not runtime proof.

Source-only campaign work continued while build81 farmed: Stage4 now has a bounded
boss strategy using owned Wind-up Alphinaud(130), cost10. Summon six per wave atB,
send surviving groups to the live enemy, follow position changes/knockback, and
replace losses within capacity. After group movement, use Execute Action only when
its native button is enabled. Do not infer a clear from boss HP or disappearance.
The native result remains required. Stage5+ still stop before admission. Unit
coverage exercises delayed capacity/queue limits, survivor groups and follow pacing.
Next action: isolated compile/test only; watched marker81 remains in its active run.

Stage4 isolated build passed and suite816 passed. Source now also routes Stage5
through the stone opening and Stage6 through boss groups, targeting living Imps
before the largest remaining enemy. Guide sources:
https://ffxiverminion.com/stage5-turning-tribes and
https://ffxiverminion.com/stage6-off-the-deepcroft. These are initial strategies
awaiting native observation; no clears beyond2 are claimed. Stage7+ remains gated.
Intent: one more isolated compile for these routes while the farming run finishes.

Farming match6 won03:39:55 (2:32), match7 won03:43:16 (2:31), and match8 won03:46:38
(2:31), all credited once. Saved totals now matches13/wins8. Match9 admitted03:47:02
and remains unresolved in the latest snapshot. Nine consecutive observed Stage2
wins since77; two further saved wins remain to exact target10. Main log still
active at~16.6MB, well below its known cap. Keep watched Debug DLL unchanged.

Farming acceptance passed: match9 won03:50:00 (2:32), credited matches14/wins9;
match10 won03:53:21 (2:31), credited matches15/wins10. Closure verified03:53:25 and
the task completed at03:53:30 without another admission. All ten accounted matches
won consecutively with the direct-side opening; build77's earlier research win is
additional and is not needed for the ten-win acceptance claim. No purchases.

Attempt82 intent before mutation/build: set CampaignRequested=true for the saved
debug character, preserving all weekly results, caps and pause. Deploy the current
source via the ordinary watched Debug output. New debug Resume keeps the saved goal.
Expect Stage3, then the initial Stage4 boss strategy, Stage5 stones, Stage6 Imp/boss
phases. Any unsupported later stage stops before admission. Three failed stage
attempts require explicit Resume. If the old DLL drops the newly added campaign
field while unloading, build82 will stop at its already-complete weekly goal;
reconcile that evidence before another settings/reload attempt. Expected exact
marker `verminion-control-20260925-82`; runtime outcomes are not yet known.

Attempt82 built, loaded03:55:13 and dispatched once03:55:17. The campaign field
survived reload; no settings retry was needed. It selected unfinished Stage3 and
saved admission match11 at03:55:37. Stage3 remains unresolved in the startup
snapshot. Weekly counts remained matches15/wins10; earlier challenge clears1/2
were preserved. Await the actual result/unlock before another watched build.

Source-only changes after loaded82: extend the objective decision class for Stage7
using owned Wind-up Cid133 (cost30, strong against gates) at B/A/C, then five Wuk
Lamats per stone group. Combined maximum cost240; native capacity and independent
queue-request bounds remain mandatory. Stage8 reuses the ordinary stone opening.
Guides: https://ffxiverminion.com/stage7-rivals and /stage8-always-darkest; owned
Cid stats/strength verified from https://ffxiv.consolegameswiki.com/wiki/Wind-up_Cid.
Stage9 requires bomb lever pickup, placement and detonation; those controls still
need native research. No Stage7+ strategy is loaded yet. Intent: isolated compile
and tests only, leaving active build82's Stage3/4 campaign sequence uninterrupted.

Stage3 attempt1 LOST03:59:49 (3:46), counted as matches16/wins10. Native result
Lovm fields9/11 were0 and field10=95: only enemy StoneB remained. Multiple healthy
Wuk Lamats remained at the already-destroyed side stones. The decisive missing
behavior is reassignment after stone destruction, not proof that another roster
is required. Source now sends surviving side groups to the lowest-HP remaining
stone after the opening, paced20seconds. Missing HP readback cannot select a target.
The current loaded82 auto-started its second Stage3 attempt: match12 at04:00:13.
Do not reload while its result is unresolved; let the saved three-attempt limit
settle this run, then deploy the targeted correction. Source-only Stage7/8 extension
compiled and suite817 passed before adding this reassignment; verify the latest
correction via isolated build/tests. No new roster or purchase was selected.

Stage3 match12 WON04:03:11 (2:32), credited once as matches17/wins11 and unlocked
Stage4. A shared-output build04:02:14 caused another marker82 load04:02:18 and
one resumed dispatch04:02:22. The actual watched DLL lacked the source-only
ReinforceRemainingStone method, so the win does not verify that correction.
Stage4 match13 admitted04:03:35. Boss Goobbue Sproutling had8000HP; its first six
Alphinaud requests spawned, but moved into combat before six waited at GateB.
The request budget remained exhausted and no reinforcements followed. Boss HP
fell to6243 with two survivors by04:07:07, then the service failed04:07:40 with no
result. Native Execute Action enabled, dispatched and disabled during combat.
Another shared-output build04:11:24 loaded old marker82 at04:11:28 and resumed
this same pending match, summoning again. These reloads were not our deliberate
watched builds. No false result credit has been observed.

Source correction: observe distinct friendly Alphinaud IDs each update, seed
existing units as the reload baseline, release queued-request reservations only
when a new unit appears, and reserve capacity for unobserved requests. Movement
alone cannot reset requests. Follow orders take precedence when due; missing
spawn evidence remains bounded to90seconds. Intent: isolated build/tests only
while the currently resumed Stage4 result is reconciled. Version stays0.5.0.3.

Stage4 match13 result resolved04:14:00: You Lose,10:00. The prior local failure
had abandoned its pending admission; credited=False and totals remained17/11.
Result closed04:14:04 and the task stopped. Saved CampaignStageAttempts=1 and
VerminionPaused=false; no user FULL STOP is being cleared. Isolated boss fix build
passed and all817 tests passed. Intent before watched build83: deploy spawn-based
reinforcements plus the pending ordinary-stage corrections through the existing
Debug output; resume campaign Stage4 attempt2. Expected exact marker
verminion-control-20260925-83. No settings change or additional admission while
an earlier result remains unresolved.

Attempt83 loaded04:15:05, resumed the saved campaign once04:15:08 and admitted
Stage4 match14 at04:15:28. Early reinforcement readback:11 Alphinauds deployed
at04:16:19, additional summon requests04:16:33-39,17 alive at04:16:58. This
confirms the first-wave stall is corrected; Stage4 victory remains unresolved.

Source-only: route ordinary stone stages10/11/13/14/16/17/18/20/21/22 through the
existing objective strategy. Read their individual strategy pages under
https://ffxiverminion.com/guides; each recommends splitting stone attackers.
Stage9 remains blocked pending bomb pickup/drop/detonation readback. Later boss
mechanics researched there:12 frontal crush/gate destruction,15 burn at25%,19
Gilgamesh recovery burst and Enkidu defense,23 Twintania burst,24 AoE/towers/adds.
Do not claim these mechanics or later ordinary clears from source-only routing.
Attempt83 reinforcement proof reached23 living Alphinauds at04:17:38, but the
SendWave selection failed04:17:41: none of the four nearby cardinal ground points
was both empty and visible in the624x440 window. The unit itself was near the
bottom edge; the only empty candidate projected offscreen. The service abandoned
match14 without credit, and the battle continues. Correction expands the bounded
empty-ground search to32 cardinal/diagonal points at distances2/4/6/8, still
requiring native projection, visible screen position, and no nearby game object.
Intent before watched build84: resume this known ongoing Stage4 battle after its
local selection failure to verify the correction. Its abandoned admission stays
abandoned; any result must not receive weekly credit. No new match is dispatched
until the existing battle result is resolved. Expected marker
verminion-control-20260925-84; includes source-only later ordinary routes.

Attempt84 loaded04:19:45, dispatched once04:19:49 into the existing Stage4 battle.
The expanded empty-ground search worked; summoning and following continued.
Native You Win appeared04:21:33 (5:39), credited=False because the earlier
selection failure abandoned admission. Result closed04:21:37; totals remained
matches17/wins11. This is an observed Stage4 clear but not an uninterrupted run.
The next menu must verify Stage5 unlock before saving permanent progress.
Native tooltip verifies LovmPalette81=Trigger Trap;82=Execute Action.
Intent before build85: continue the saved campaign through unlock reconciliation;
add minion status details only to existing bounded battlefield snapshots for
later boss/bomb research. Expected marker verminion-control-20260925-85.
Attempt85 loaded04:22:45, dispatched once04:22:48. The native challenge list
verified Stage5 unlock04:22:58, saving the preceding Stage4 clear without adding
weekly credit. Stage5 match15 admitted04:23:09. At04:25:13/33 the new reassignment
selected enemy StoneA after StoneB was destroyed. Stage5 result remains pending.
Watched output85 includes spawn reservations, expanded empty-ground selection,
ordinary routes through22, and bounded status snapshots. All817 tests passed
before85;85 built successfully (only existing NU1601 warning). No purchases.

Tournament research: official guide confirms15 matches and NPC difficulty based
on prior tournament victories; Recordkeeper registers/claims, ranking board or
table menu shows standings. Existing ClientStructs LovmRanking is an empty7228-byte
struct, not a verified readback contract. Use native menus for observation rather
than inventing offsets. Existing ADS purchase handoff exposes no price-cap input;
do not use it to bypass cumulative spending limits.
Stage5 match15 LOST04:27:20 (3:45), credited once as matches18/wins11. Result
readback: enemyA/B destroyed,C665HP; many healthy Wuks remained near friendly
GateA, while8 attacked C. Shared-output builds caused unrequested same-marker85
reloads04:23:55 and04:27:34. The first resumed the opening mid-match and produced
extra delayed units; the second occurred after result closure, before next queue.
Source correction now includes friendly-gate survivors in final objective
reinforcement, prioritizing the largest group over isolated surviving units.
Intent: isolated compile only while85 runs its next bounded Stage5 attempt.
Public XIVAPI Companion/CompanionTransient rows confirm owned Wuk remains the
best cheap Arcana attacker (cost10,ATK35,speed1); other owned Arcana strengths are
Bacon Bits(cost30,ATK20,speed1) and Domakin(cost20,ATK20,speed1). No owned healer
was found among32 captured minions. Keep purchases disabled at the saved zero caps.
Correction to pending-attempt assumption: the unrequested04:27:34 reload happened
while the Stage5 menu was being selected. Its dispatch04:27:38 failed the busy
character gate; it never admitted a second Stage5 match. No retry loop ran.
Current source additionally reduces ordinary post-movement readback from6seconds
to1second so the next gate queues while earlier attackers walk. Tutorial/boss
ability delay remains6seconds and mouse-up still occurs before cursor restoration.
Intent before build86: deploy both observed Stage5 corrections through watched
Debug output, resume Stage5 attempt2 after its settled local failure. Keep saved
weekly totals18/11, Stage4 clear, zero caps and pause intact. Expected marker
verminion-control-20260925-86. No ongoing unresolved match is being interrupted.
Attempt86 loaded04:31:30 and dispatched04:31:34, but again stopped before admission.
Cause resolved from fresh evidence: CanObserveVerminionForReload allowed any
registered character in territory388, bypassing cleanup readiness. At dispatch,
no Verminion menu or queue remained, but OccupiedInQuestEvent was still true;
it cleared0.31seconds later. Narrow this exception to an observed Verminion menu
or current CPU battle, preserving the existing30-second cleanup wait. Mere
presence in Minion Square is insufficient. Intent before build87: verify this
corrected setup/menu reload path, then the same Stage5 attempt2 corrections.
No battle began under86 and no accounting/pause setting is changed. Expected
marker verminion-control-20260925-87.
Another unrequested marker86 load around04:33:12 began Stage5 setup at04:33:16
and queued04:33:26 before the planned87 deployment completed. Marker87 loaded
04:33:34 and resumed the actual Stage5 battlefield04:33:39. Thus87's build raced
with an external load; do not claim its readiness fix as an isolated menu test.
The earlier admission had not yet saved BeginMatch, so this battle has no pending
identity and cannot receive weekly credit. No retroactive identity is fabricated.
Source-only fix now saves BeginMatch on a verified matching CPU queue before
Commence, and preserves that identity when resuming the same queue. Queue attempts
remain distinct from result credits; cancellation/timeout still abandon them.
Intent: isolated build/tests while87's current Stage5 battle settles; no deliberate
watched reload until its result is reconciled.
The queue-before-Commence correction compiled separately and all817 tests passed.
At04:35:24 loaded87 was still running Stage5, having finished its third lane and
redirected four middle-stone survivors to enemyC at3290HP. No result yet. The
next deliberate deployment should use88 after reconciliation, and must include
the source-only admission persistence fix. Ordinary losses are not yet replenished
after the opening (idle units are redirected); implement bounded observed-spawn
replacement before claiming the full ordinary resource-management requirement.

Stage5 under87 WON04:36:21 (2:19). Reassignment moved10 survivors from destroyed
C to A at04:35:55; A fell while friendlyB2930/C5000HP remained. Weekly credit
stayed false because the admission reload had no saved match identity. Closure
verified04:36:25. This verifies the faster opening and remaining-stone moves.
Intent before watched build88: deploy the verified-queue persistence fix, resume
the saved campaign, reconcile Stage6 unlock, then test Imp priority and boss
reinforcements. No live match remains unresolved. Expected exact marker
verminion-control-20260925-88; no version/config/cap/pause change.
Attempt88 loaded04:38:48 and dispatched once04:38:52. Native Stage6 unlock
verified04:39:02, saving Stage5 clear. CPU queue match16 was saved04:39:05 before
battlefield entry04:39:12, verifying the earlier persistence point. Current boss
is Minute Mindflayer; six opening Alphinaud requests sent04:39:18-23.
Source-only correction from the Stage6 guide's actual minion listing: accept both
Infant Imp and Imp as invulnerability adds (the prior code only recognized Imp).
Smallshell remains excluded from priority. Native add naming is not yet observed;
compile/test separately and inspect the first phase before a deliberate reload.

Native Stage6 phase observed04:41:57: Minute Mindflayer4191/6000HP has status981
Invincibility; Infant Imp380/380HP at(-10,0,-5). Friendly Paralysis is status988.
The loaded88 exact Imp match leaves the add untargeted. The prepared helper fix
compiled separately and all817 tests passed. Saved config freshly verifies
pending16/duty557,attempt1,pausefalse,clears31,weekly18/11. Intent before build89:
controlled reload into this known active match to deploy the actual add-name
correction. Preserve admission identity and result accounting; do not start a new
battle or clear pause. Expected marker verminion-control-20260925-89. Success is
native add death, loss of invincibility and eventual positive result evidence.
Attempt89 targets the real Infant Imp, but its HP only fell380->352 by04:43:57
while the Mindflayer remained invincible. The follow selector chooses the single
unit closest to the add, separating it from the main army near the boss. Correction
selects the anchor with the largest nearby friendly damage group, using target
distance only to break ties. Intent before build90: controlled reload into the
same saved match16/duty557 to move the main group onto the add. No new admission
or accounting change; observe add death and resumed boss damage. Expected marker
verminion-control-20260925-90.
Attempt90 dispatched04:45:02 into the same saved Stage6 battle. Add priority/main
army movement now has positive phase evidence: the first Imp died and boss HP
fell4191->2988; a second Infant Imp appeared04:45:29 and was gone by04:45:40.
The strategy returned to Minute Mindflayer04:45:48 and HP fell again by04:45:59.
Continue observing match16 through its remaining phases/result; do not claim a
Stage6 clear yet. Main log still usable; no failure/extra admission recorded.

Fresh startup identity reconciled:89 loaded04:43:17/dispatched04:43:21;90 loaded
04:44:59/dispatched04:45:02. Stage6 phase3 required crossing from the right Imp to
the left Imp; by04:47:44 the strategy returned to the boss at1785/6000HP. Dullahan
adds at friendlyA destroyed its last3520HP before the army could finish the boss.
Match16 LOST04:47:57 (8:20), credited=True exactly once across both controlled
reloads. Totals19 matches/11 wins; result closure04:48:01. Fresh Stage6 attempt2,
match17, saved its native queue04:48:14 under loaded90. Let this clean attempt test
phase handling from the start; the first run spent several minutes waiting on
wrong-name/group fixes, so it does not yet establish a defensive-strategy failure.
No pending build/test sessions remain. Latest build succeeded;817 tests passed
before the native main-group selection correction, which has live phase evidence.
Version0.5.0.3, purchase caps0, user pause preserved, unrelated Chocobo edits intact.
Source-only ordinary resource work while90's clean Stage6 attempt runs: extract
the already-proven spawn reservation into a shared pure tracker, retain the boss
behavior, and replenish ordinary attackers after the opening. New requests reserve
native capacity, stay at most6 outstanding, and release only on distinct observed
spawns. New groups use the gate nearest the weakest surviving stone; idle gate
readback uses the existing10-yalm opening radius. Movement waits for4 replacements
unless the target is nearly dead or no more can be summoned; destroyed-stone
survivors remain immediately actionable. Missing spawn readback stops after90s.
Intent: isolated build/test only, no watched reload during the active Stage6 run.Match17 LOST04:56:10 (7:24), credited once, totals20/11. Closure04:56:14;
third Stage6 attempt match18 queue saved04:56:27, battlefield04:56:34. Marker90
remains loaded. Source-only boss pacing now prioritizes changed targets after10s,
reinforcement summons, then stationary refresh after40s (previously20s); repeated
selection/movement had starved summons. Ordinary replacement build and817 tests
passed earlier; the newest pacing assertions still require isolated verification.
Intent: compile/test this correction, then controlled reload into saved match18
if it remains active. Do not reset stage attempts or manufacture a new admission.
Pacing correction isolated build succeeded and817 tests passed. Intent before
watched build91: deploy ordinary loss replacement and boss pacing into active
Stage6 match18; preserve pending identity and stage failure limit. Expected marker
verminion-control-20260925-91, version0.5.0.3. No attempt-count/config reset.
91 loaded04:58:30/dispatched04:58:33 into the same pending match18. Capacity rose
to240/240 by05:00:04, proving reinforcements no longer starve. However the first
Infant Imp remained380HP despite move orders. Native input at05:00:51.844 shows
right-button collision LovmQueueList/node9 at client(30,127), intercepting the
left Imp destination. Add LovmQueueList to the existing temporary hide/restore
set. Intent before watched92: correct this observed input obstruction in the same
Stage6 match18; no admission/count reset. Expected marker20260925-92.

92 loaded05:01:39/dispatched05:01:43. Hiding LovmQueueList let the first left Imp
die by05:02:18; the boss reached1788HP and phase3 Imps were cleared by05:05:01.
Match18 nevertheless LOST05:05:19 (8:19) after earlier delays. Credit once:21/11.
Closure05:05:23, and the three-attempt limit paused the campaign05:05:33. No clear.
Source correction for next test: read selected friendly entity from observed Lovm
array17, check native Execute Action readiness and target distance<5 plus no981
Invincibility, then cast opportunistically. Do not waste offensive actions at a
gate while a group travels. Boss movement readback becomes1second; tutorial stays6.
Also preserve the densest-group anchor when selecting within its local roster.
Isolated build passed. Intent before watched93: resume after the reviewed failure
limit and deploy all fixes from a fresh Stage6 start. Native/config confirms no
pending match, totals21/11, mask31, stage6 attempts3, pause produced by the limit,
zero caps. Apply existing Resume accounting (clear stage/run attempts and losses),
then build expected marker20260925-93. This is a justified development retest;
FULL STOP pause is not bypassed and no result is fabricated.
93 fresh match19: native offensive specials fired near the boss05:08:13,05:09:09,
and05:09:37. First Imp phase handled05:09:53-05:10:14; special near the Imp at
05:10:09 reduced380->211->59HP. Three boss specials05:10:30/33/36; second Imp
phase began05:10:47. This is positive ability-timing evidence, not a clear yet.
Source-only selection correction isolated build passed: selected entity readback
at05:08:18 was the enemy boss after a friendly click, showing model obstruction.
Prefer frontmost group members and verify actual friendly selection before the
second click and before movement; use at most6 observed candidates. Ordinary and
tutorial selection remain unchanged. Hold this change for94 after match19 settles
unless another concrete obstruction requires earlier deployment. No source tests
cover native hit testing; compiled only and needs live evidence.
Match19 LOST05:14:16 (6:21), credit once22/11; closure05:14:20, next queue
match20 saved05:14:33 under93. Ability timing works and all Imp phases were
handled, but the last boss movement left healthy minions in the middle while the
boss/adds reached the left stone. Selection readback/movement still needs stronger
proof. Intent before94: deploy bounded friendly-selection validation and capture
any mismatch (including LovmPalette number data and the existing image), plus
ordinary reload reconciliation for surviving partial groups. Resume match20 with
its admission intact; no pause/cap/count changes. Native index17's selection
interpretation must be tested through both clicks and camera movement; do not
claim the new guard verified merely from its build. Expected20260925-94.
94 loaded05:16:38/dispatched05:16:41 into match20. It detected a mismatched group
selection05:17:20, chose another member and continued; first Imp died by05:18:00,
second phase by05:18:58, boss2037HP at05:19:34. No result yet. Source-only95
correction: treat uncommanded spawns within10yalms of Gate B as ready (observed
spawn positions are15-18 while gate is22;5yalms misclassified many as deployed).
After15seconds reissue movement when fewer than half the deployed minions are
within6yalms of the objective. This addresses the observed healthy army stranded
far from a moved boss. Isolated build passed. Intent before95: controlled reload
into the same pending20; keep admission/accounting/caps/pause intact. Expected
verminion-control-20260925-95. This arrival policy remains to be verified live.

Source-only96 candidate: Stage6 prepares owned Haurchefant173(cost30,HP530,ATK85,
DEF45,speed4; defensive special+40DEF). Reserve120 capacity for4 guards after
initial60-capacity preparation. Independent confirmed spawn reservations include
both queues; guards defend middle stone or another surviving stone, replace
losses, and reselect for defensive specials against nearby adds. Alphinaud remains
the boss/Imp attacker. This is justified by repeated late losses to stone attacks;
all Imp phases themselves now have live control evidence. Isolated build and817
tests passed05:25, including shared capacity/casualty replacement cases. Do not
deploy during match21: its palette lacks this new defender and must be prepared
outside battle. Zero purchase caps remain intact; no purchase code was executed.
95 identity now reconciled: loaded05:20:16, dispatched05:20:19. Match20 loss05:21:47
(6:42), totals23/11, closure05:21:51. Fresh match21 admitted05:22:03 before entering
battle05:22:11. Let the clean95 attempt settle; normal third-attempt stop applies.
Match21 under95 reached174/6000HP at05:28:45, then service FAILED05:28:48 because
the selected minion was unavailable during its second click. The pending identity
was abandoned, so any later result must receive no weekly credit. Battle outcome
is currently unresolved; the surviving army may have finished after control stopped.
The96 candidate now reframes moving units before the second click, allows surviving
boss group members to continue, and returns to battle decisions after a group is
lost instead of abandoning result observation. It uses immediate native minion-info
hit readback only before camera/pointer changes, retains the confirmed group anchor
for specials, and aims attack clicks at the live enemy model rather than stale
ground coordinates. Native target IDs join the existing bounded battle captures.
Latest isolated build passed;817 tests passed for defender capacity/accounting.
Intent before watched96: reconcile match21's actual result first, preserving its
abandoned no-credit status, then follow verified campaign unlocks. Do not reset the
third-attempt limit or pause. Old in-progress battles retain their existing palette;
all new Stage6 admissions prepare Haurchefant before entry. Expected20260925-96.

96 loaded05:32:23/dispatched05:32:26. Stage7 unlock05:32:36 positively confirms
Stage6 clear (mask63), without crediting abandoned match21. Stage7 match22 LOST
05:36:41 (3:28), totals24/11, closure05:36:45. Match23 LOST05:40:49 (3:18),
totals25/11, closure05:40:53. Gate suppression delayed first Wuk order nearly
98seconds and both attempts lost. Third match24 queued05:41:06, entered05:41:13.
Intent before watched97: load the corrected direct six-Wuk-per-lane stone opening
into pending24, preserving its identity and three-attempt count. One Cid may
already exist; no count or pause reset. This removes the unused Cid requirement
for future Stage7 admissions. Source also permits Stage9's bounded bomb-control
capture, then stops before unknown commands. Isolated97 source compile produced
its DLL05:41:00. Expected startup verminion-control-20260925-97; version unchanged.
97 build passed, loaded05:41:48 and resumed pending24 at05:41:51. Stage7's third
attempt continues with one earlier Cid and the new direct Wuk opening. No fresh
admission or counter reset occurred. Gate suppression remains unused strategy
code for now; direct opening requires fresh-start verification if this late
switch loses. Guide research confirms Stage9 bomb transport/drop/trigger and
later boss mechanics still need their actual native state/control contracts.

Match24 LOST05:45:05 (3:26), totals26/11, closure05:45:08; the third-attempt limit
paused05:45:19. Native foes are critters (Wolf Pup, Coeurl Kitten, Black Coeurl).
XIVAPI CompanionTransient confirms Wuk speed1,35ATK,25DEF; owned Succubus82 is
monster,speed3,55ATK,25DEF,cost10, with a +40 monster ATK group special. This
motivates a composition correction rather than repeating the losing poppets.
98 changes Stage7's opening/replacements to six Succubi per lane, keeps native
friendly selection verification, and uses its buff near combat. Remove unused
gate-suppression recipe. Isolated build and817 tests passed, including recipe
capacity, confirmed replacement reservations and unchanged Stage2 composition.
Intent before watched98: existing saved pause is the observed campaign limit,
not FULL STOP. Verified no pending admission and stage7 attempts3. Apply the
existing Resume accounting (clear run/stage attempt counters and losses) after
this reviewed correction, preserve26 matches/11 wins/mask63/caps0, and deploy
expected marker20260925-98. No new composition victory is claimed.
98 build passed, loaded05:48:40/dispatched05:48:44. Match25 queue saved05:48:52.
98 first Succubus attempt: offensive buffs executed05:49:49/05:50:15/05:50:57.
At05:52:06 enemy B/C are destroyed and all three friendly stones remain above
3800HP; final stone A remains5000HP. No result yet. A native control flaw was
observed05:50:48: reinforcement logic redirected the newly ordered final opening
group while it was still near Gate C. Source-only99 correction remembers the
actual movement-click time for nearby selected units and excludes them from idle
reinforcement candidates for30seconds. Also captures Stage9 minimap node geometry
for later camera/bomb research. Isolated build passed. Hold for99 until match25
settles; do not interrupt this useful composition test without fresh reason.
Stage7 match25 WON05:53:06 (3:41), explicit You Win and credit once27/12;
all friendly stones still positive (2180/1440/4045). Closure05:53:10, Stage8 setup
05:53:15. This verifies the Succubus counter composition and unlock progression.
Intent before watched99: deploy the compiled movement-grace/minimap observation
changes during Stage8 transition, preserving any saved queue/admission identity.
No settings/pause/counter edits. Expected verminion-control-20260925-99. Stage8
uses the existing Wuk composition; Stage9 remains a bounded control capture.
99 build passed, loaded05:53:50/resumed05:53:54. Existing Stage8 opening roster
survived reload (six Wuks ready at B), and first stone order dispatched05:54:00.
Stage8 WON05:56:25, credit once28/13; closure05:56:29. Stage9 match27 saved05:56:42,
battlefield05:56:49. Control capture stopped05:57:10, abandoning27 with no credit.
Observed invincible Slime Puddle430HP, total boss3870HP, extra EventObj2005108 at
(8,0,-6), and normal Execute Action82/Trigger Trap81 controls. Six Alphinauds at B.
Intent before100: reload this research battlefield without inventing a pending
identity; use existing group movement to the one observed lever candidate and
capture carrier status/UI on arrival (65second bound). No placement/detonation
claim or automatic result credit. Expected verminion-control-20260925-100.
100 loaded05:58:41/resumed05:58:44; group reached lever05:59:17. Exactly one
Alphinaud received987 Trapper,param515. This confirms2005108 as the pickup area.
Slime now10000HP/status981; last friendly C5000HP and B760HP. Service captured
then stopped; no admission identity was recreated and no credit was recorded.
Intent before101: select the exact carrier (skip same-type second click), move
it to the live slime, dispatch Execute Action82 only when that carrier selection
is verified and within5yalms, and capture placement readback3seconds later.
If the research battle already ended, close its uncredited result and stop.
Expected verminion-control-20260925-101. Detonation remains unimplemented.
101 loaded06:02:12/dispatched06:02:15 outside duty: earlier research battle had
already ended; no old credit was recorded. Normal campaign flow admitted match28
06:02:28. Carrier pickup06:03:24; exact single selection/movement succeeded, and
action82 dispatched06:03:34 near the slime. Readback06:03:37:987 disappeared,
image and game chat say "Trap set!", boss still10000HP/981. Capture then abandoned
28 without credit. This positively verifies single-carrier selection and placement.
Source minimap camera proof uses observed60010/60009/60015 markers for friendly
A/B gates and B stone, deriving scale from native bounds/live world positions;
real mouse input only, then existing projection readback. Isolated build passed.
Intent before102: keep research battle's no-credit state, trigger the existing
armed bomb only when native81 is enabled, capture slime phase five seconds later.
Fresh placements wait up to15seconds for native readiness. Include the Stage9-only
minimap camera proof if framing is needed. Expected20260925-102; no pause/count
or purchase changes. Detonation and camera movement still require live evidence.
102 loaded06:05:44/resumed06:05:47. Native81 trigger accepted06:05:53; phase
readback06:05:58 still one invincible10000HP slime at(16.6,4.5). The bomb was
placed near B at(0.6,1.1) more than2minutes earlier; research pauses let the boss
leave its blast area. This proves trigger input, not a successful bomb hit.
Intent before103: chain the now-observed pickup/single-carrier/placement/armed
trigger controls without intervening research stops. Wait five seconds after
detonation, capture phase state, repeat only while invulnerable slimes exist,
and use the ordinary boss army after invulnerability ends. Bound to6 bomb cycles
and existing match timeout/three-attempt campaign limit. Queue Alphinaud reserves
while bombs arm. Include Stage9-only minimap framing proof. Preserve abandoned
match28 and any later positive saved admission; no counter/pause edits. Expected
verminion-control-20260925-103. Bomb damage, splits and winning remain unverified.
103 build passed, loaded06:07:50/dispatched06:07:53. Earlier research match28 had
ended outside duty, still uncredited. Fresh match29 saved06:08:06; this is the
third Stage9 admission, so failure must reach the normal campaign limit.
103 first bomb positively hit: pickup06:09:00, placement06:09:10, trigger06:09:21,
readback06:09:26 changed one data562 slime into three data3965 slimes. All stones
still positive(4385/4360/5000). Minimap click executed06:08:36 with derived3.5px
peryalm; detailed projection/camera verification remains to be reviewed.
Next pickup tried the prior carrier under the split slimes; exact selection failed
06:09:30, abandoning29 with no weekly credit. Intent before104: select a reserve
near the gate for pickup and accept any actually clicked friendly Alphinaud for
that purpose, while requiring exact identity after Trapper pickup. Resume this
same physical battlefield if still present; never recreate29. If already outside,
the three-attempt gate must pause until a separately reviewed Resume. Expected104.
104 loaded06:11:26/resumed06:11:30. Abandoned29 lost06:11:38, credited=False;
closure06:11:42 ended the service. Saved facts remain28/13/mask255, pending0,
Stage9 attempts3, pausefalse. Before105, correct Fail to persist the third-attempt
pause immediately (the old code only paused at the next admission gate); already
verified clears are excluded. Regression added to existing lifecycle coverage.
Minimap camera proof reviewed:06:08:36 click collided with LovmMiniMap/node16;
the same unit projection moved from(316.8,118.0) to(339.6,345.9) within0.51seconds,
with no intervening keyboard camera command. Selection then succeeded. This is
positive minimap camera evidence; normal keyboard framing remains available.
Intent before105: after tests, apply the reviewed Stage9 development Resume
accounting while idle, requiring pending0/attempts3/pausefalse (so a new FULL STOP
cannot be overwritten). Preserve28/13/mask255 and zero caps. Fresh continuous
two-phase bomb strategy uses reserves for later pickups. Expected marker105;
this is a justified retest after the observed selection correction.
105 tests817/build passed, loaded06:14:41/dispatched06:14:44, fresh match30 saved
06:14:58 and battlefield06:15:04. First placement06:16:07/trigger06:16:18 did
not split the boss at06:16:23. The5yalm placement gate can execute before the
carrier reaches effective blast range. Second reserve pickup succeeded06:16:50,
so the selection correction has positive evidence. Intent before106: narrow the
placement gate to2yalms (the prior successful placement was about1.4yalms from
the slime), preserving pending30 and its counts. No settings or pause changes.
Expected verminion-control-20260925-106; continue the same live attempt.
106 built, loaded06:17:46/resumed06:17:49. Stage9 match30 lost after its earlier
delays; normal loop saved31 at06:19:03, battlefield06:19:11. This clean attempt
uses the2yalm placement gate from the start. Await its first and second bomb
phase evidence before further strategy changes. No source changes after106.
Match30 loss reconciled:06:18:46 You Lose(3:16), credited29/13, closure06:18:50.
Fresh31 under106 completed both bomb phases: final normal slimes(data472) were
damageable by06:22:05, aggregateHP2632/3870, with two friendly stones surviving.
Stage9 WON06:23:26, credited exactly once30/14. Last friendly C1270HP remained.
Closure06:23:30, Stage10 match32 saved06:23:43 and battlefield06:23:50.
Source-only107 candidate: Stage12 uses live boss facing/radius for rear movement,
responds to turns, selects an enabled gate in B/A/C order, and reuses four owned
Haurchefant stone guards with shared capacity reservations. Minimap proof extends
to that stage only. Isolated build passed; no deployment/runtime claim yet. Hold
until current ordinary battles settle, preferably the existing Stage12 admission
block, then record107 intent and reload. Only status-text cleanup followed compile.
Stage10 WON06:26:36, credited31/15; closure06:26:40. Stage11 match33 saved06:26:52,
battlefield06:27:00. At06:29:38 only enemy A605HP remains; all three friendly
stones positive. Hold the watched build until its result is reconciled. Source
Stage12 logs actual rear destination/facing/radius to validate its geometry.
Stage11 WON06:29:46(2:20), credited exactly once32/16; closure06:29:50.
The old build stopped at the Stage12 strategy gate06:30:00 before admission.
Final isolated Stage12 source build passed. Intent before watched107: enable
the prepared rear-facing/gate-selection/defender strategy from this idle menu.
Preserve pending0, mask2047 (stages1–11), totals32/16, caps0 and all attempt
accounting. No Resume/reset is needed for an unattempted stage. Expected marker
verminion-control-20260925-107, same0.5.0.3. Native rear geometry, gate availability,
guard deployment and Stage12 clear remain unverified until this live attempt.
107 build passed, loaded06:31:23/dispatched06:31:26. Stage12 match34 saved06:31:34,
battlefield06:31:41. No extra admission/counter reset. Source matches the watched
build; no pending source-only changes or running tool sessions at this checkpoint.
109 loaded06:42:53/resumed06:42:56.35 lost06:43:01, credited34/16. Third Stage12
match36 saved06:43:18, battlefield06:43:25. Gulool546 correctly prepared/spawned;
four guards living, but boss11376/12000 at06:45:58. Damage units remained around
C/Z0 while the boss was C/Z9: elevated-model clicks do not reliably pursue its
feet. Intent before110: rear ground coordinates0.8y, retain verified selection
for three-second pursuit refreshes, quarter-second selection steps, and explicit
defender interceptions rather than stone-center ground orders. Include the prepared
Stage15 Gulool final25%-HP burst. Preserve pending36/attempts3/caps0; no Resume or
count reset. Isolated compile passed before the final ground/refresh changes.
