# VERMAXION Changelog

## Unreleased - Jumbo Cactpot purchase fixes

- Route overdue ticket purchases to the broker when no payouts remain, including during the payout window, so partial purchases can resume instead of being marked not due.
- Recognize purchase receipts at any MGP price, preserving four-digit numbers and requiring system-message metadata. Advance only after a matching receipt and handle the native next-ticket prompt.
- Read current-drawing numbers from the ready purchase input and buy only missing tickets. Random mode excludes existing and newly purchased numbers, keeping each choice stable until its receipt; Fixed mode still permits repeated numbers.
- Leave unreadable ownership and reward-list-only results incomplete with a clear reason. Existing payout counts and closed confirmation windows cannot establish current-cycle completion. Live verification remains pending.

## Unreleased - Verminion CPU bot (in progress)

- Add a Check planned minions button to /vmx v. It reads the current character's ownership for every minion in the 24-stage plan, shows the registered total, and gives each missing minion's short manual acquisition source. Checking starts no purchases or quests and adds no saved setting.
- Keep pursuing Odin until the main army reaches melee range. A nearby selected attacker or attackers within five metres could previously suppress movement while most of the army remained behind the boss. Read actual army arrival, react to smaller boss moves and avoid an extra selection at the final-phase transition. Keep the guide-directed special and existing three-attempt stop. Added one focused regression; the current character's Stage 15 replay remains pending after its limit stop.
- Return separated Stage 15 attackers individually after the main party reaches its healing gate. The local attempt left seven fully healed Gentlemen at the gate and one injured unit near Odin, then correctly timed out without result credit. Keep the existing recovery and campaign attempt limits. Added one focused regression test; native verification is pending.
- Keep a selected minion or group through camera framing by using camera keys for its pending order. After two defeats with the prior code, the local Stage 12 run cleared on its first attempt with this fix while the game was out of focus, then advanced automatically to Stage 13. Result exit now allows 60 seconds for the return transition and checks an observed return before applying its timeout; the prior exit took about 45 seconds. The new run's exit succeeded in six seconds; a fresh exit longer than 30 seconds remains unverified.
- Inspect and register inventory minions when Run or Resume begins with Verminion's setup or challenge menu already open. Close the owned Return/Cancel menus first, then use the ordinary inventory and roster checks before selecting the next stage. This lets a newly acquired quest reward satisfy the roster without a separate manual registration step. Queue and battle reconciliation keep their existing paths; native replay remains pending.
- Reject Questionable minion acquisition when an unfinished route quest is blacklisted. After reserving priorities and the reward stop, wait for the provider to select the requested quest before starting it; an unrelated selection releases the reservation without starting quest work. Reloaded preparations require a new manual handoff. Native verification confirmed blacklist rejection, the correct accepted-quest start, reload observation without duplicate submission, and cleanup after the provider stopped an incomplete trial.
- Check DAD's local duty ownership directly before Questionable minion acquisition. Its network coordinator readiness does not describe the local duty bridge; keep an absent or busy bridge blocked without requiring a network coordinator for single-character questing.
- Verify stages 2–11 on another local character, all on their first fresh attempts. Its stages 1–2 were already unlocked; Stage 1 replay remains pending. The Stage 2 weekly run reused nine existing wins, credited one victory and stopped at exactly 10/10. Stage 12 correctly blocked before admission for the missing Wind-up Gentleman.
- Show the missing DAD requirement beside Questionable minion acquisition and disable that action until its existing IPC reports ready. Recheck readiness when invoked, so an absent provider produces a clear prerequisite message before any quest handoff instead of a raw IPC error. The disabled action and prerequisite text are verified on a client without DAD.
- Finish leaving Verminion's challenge and parent menus before handing a missing minion to ADS. A local campaign run exposed a gap where ADS started navigation before the parent menu appeared, then correctly rejected the unexpected UI without spending. Native replay verified both menus closing before ADS dispatch, one Zu Hatchling purchase for 10,000 MGP, its exact receipt and registration. Foreign interactions remain blocked and every vendor action stays in ADS.
- Keep Verminion status, Run, Resume and FULL STOP above a separate scrolling area for settings and guides. Group weekly settings and purchase limits under an expandable heading, and keep the stage-preview controls usable at narrower widths. Reduce the new window's initial height for smaller displays. Native layout and purchase-limit editing are verified on the newly selected local test client.
- Bind Recordkeeper registration to the observed tournament title. A changed period returns to the Recordkeeper for its own entry and reward check; old registration cannot authorize the new allowance or mark it exhausted. Reject a registration response naming a different tournament. Period-transition behavior has regression coverage and awaits live verification.
- Handle the Recordkeeper's known prize dialogue and owned rankings window, keeping the rewarded tournament separate from the current tournament notice. Save claim intent before accepting the exact prize prompt; require a saved acknowledgement and exact MGP increase before recording the receipt. Interrupted claims are never resubmitted, block competing purchases/matches, and survive FULL STOP, reload and weekly reset. Show pending and verified claims in Verminion/settings. Prize handling has regression coverage; live reward verification remains pending.
- Preserve the native CPU duty ID through queue ownership, reload, cancellation and result observation, including Master Tournament. Reject mixed queues and unrelated ready pops, and require a successful admission save before Commence. A tournament result cannot mark a campaign stage clear. New tournament entry and battle controls remain disabled pending verification.
- Continue remaining weekly participation after the Recordkeeper explicitly closes entries or fresh registered-tournament counters show all 15 matches used. Keep unknown allowance and prerequisite rejections blocked; later runs still inspect tournament and reward opportunities. These additional branches have regression coverage and await native verification.
- Keep the last readable tournament notice and visible counters per character across reloads, with its observation time shown in Verminion and settings. Saved observations never authorize entry or credit results; each CPU rewards run reads the game again. Correct the mode description to explain entry preparation and closed-period participation, while tournament battles and reward claims remain under development.
- After the Recordkeeper positively reports a closed tournament, finish only the remaining weekly participation through the existing ordinary CPU loss routine. Wait for the owned menu to close first; future runs still inspect tournament and reward availability independently of weekly completion. Native verification completed an already-met weekly goal without admitting another match; partial-week accounting has regression coverage.
- Apply the existing battle-roster requirement before pre-travel purchases, avoiding a needless Mammet request for an unlocked participation or CPU-information run. Entry minion requirements and purchase limits remain enforced by their existing paths.
- Handle the Recordkeeper's known registration menus and exact confirmation, require a positive registered/already-registered response, then refresh visible tournament allowance before preparing the CPU-only Master Tournament entry. Reject unrelated prompts and mixed duty selections. Entry preparation stops before Join; open-period registration still needs live verification.
- Verify Home World return, the native Master Tournament screen and list selection without queueing a match. The ordinary closed-period flow recognizes the upcoming tournament, selects Nothing and reports its next start time. Checked entry and open-period registration remain unverified.
- Read the Gold Saucer Verminion tournament notice before checking registration eligibility in CPU rewards mode. Display only visible valid counters; hidden values remain unknown and never credit matches, victories or rewards. A visitor replay read the current notice and stopped at the Home World gate without an admission or credit. Registration, NPC battles and rewards remain under development.
- Wait for visible "Receiving data" notices in the parent or Verminion panel to disappear before accepting tournament text, even if old text and counters remain populated.
- Verify Stage 24 with Wind-up Gentlemen and all 24 campaign clears on the current test character. The game reported Victory and the full completion mask after the final stage; repeated-win reliability is not established.
- Enable Stage 24 in ordinary campaign runs with the existing roster and three-attempt limit. Correct the earlier inferred six-minute limit: the verified victory's native match length was 7:51. Retain the bounded control window and positive-result requirement.
- Reassign a Stage 24 tower after an interrupted movement order leaves its candidate wounded outside the tower. Keep a living occupant already inside or still travelling on its verified order. Native evidence showed the old candidate blocking healthy replacements; replay remains pending.
- Exclude every observed circle when choosing a Stage 24 dodge destination, and reassess a saved destination if another warning covers it. Preserve a pending escape through unrelated warning changes while that destination remains safe. Native evidence exposed one destination inside a second circle and repeated command interruptions. Missing safe endpoints stop without credit; native replay remains pending.
- Finish a pending Stage 24 tower order when a new remote circle leaves its entire route safe. Repeated circle changes had cancelled the same order before movement, leaving the tower unoccupied. Safe pending-order preservation has native evidence; expired towers and unsafe routes still interrupt the order.
- Keep Stage 24's tower occupant assigned while sending other attackers back to combat individually after circles clear. A defeat after six minutes exposed lost attack time from holding the whole army through every tower. Individual attack orders and protected tower arrivals have native evidence.
- Verify ADS certificate purchases end to end: Wind-up Odin and Wind-up Cursor, two certificates each, one receipt per item, registration and standalone completion pause. The four-certificate cap was fully accounted for; battle use remains pending.
- Reuse the bounded selection candidates for Stage 24 tower orders when an overlapping weak minion receives the click. Only move a verified healthy individual; exhausted candidates preserve the battle and reassess at the existing strategy cadence. Alternate selection followed by tower movement has native evidence.
- Add a separate per-character Achievement Certificate cap and exact purchase accounting. The Verminion window can request Wind-up Odin or Wind-up Cursor through ADS, prefer owned inventory items, register the result and stop before further battles. Saved requests and receipts prevent duplicate purchases across reloads. Native acquisitions are verified; optional support compositions remain pending.
- Verify ADS travel to Jonathas and claiming available Achievement Certificates. Show the verified acquisitions and still-pending battle compositions in the Verminion window.

- Accept a different healthy Gentleman when the native tower click selects its overlapping model. Verify that it is one living friendly minion and track that actual unit for movement and arrival; exact identity is still required for other individual-unit mechanics.
- Update Stage 24's admission message and preview to distinguish implemented responses from the still-unverified full clear, and show the current development time limit. Correct Stage 20's preview to include its observed fresh mixed-roster clear.
- Keep the circle escape point near the current attack target to reduce travel after the warning clears. Tower movement now has another observed arrival without repeated selection. Allow nine minutes of bounded Stage 24 development control within the existing ten-minute result timeout; the six-minute probe ended before the add phase could finish. Final clear remains pending.
- Preserve an existing Verminion result screen during its selected debug-reload cleanup so the service can read the outcome before closing it. Explicit FULL STOP retains its normal cleanup. Native replay of this reload boundary remains pending.
- Verify one Stage 24 tower resolution: the assigned Gentleman took the 358 HP hit while the other seven avoided that damage. A later individual-selection failure left its match uncredited; passive reload recovery preserves that distinction. Full-stage completion remains open.
- Keep a stable Stage 24 escape point for each circle and prioritize the exposed selection anchor. Live movement showed that repeatedly choosing the nearest side for different stragglers could reverse the party through the warning. After a verified tower movement command, let the individual finish travelling instead of repeatedly selecting it. One tower arrival is observed; these corrections still need replay.
- Add development Stage 24 responses to the visually identified ground warnings: move exposed groups clear of red circles, send one healthy Gentleman into a tower and verify its arrival. Hold normal movement through active warnings while allowing bounded replacement summons. Interrupt stale orders on warning transitions and remove diagnostic image delays. Live damage avoidance, tower resolution and the final clear remain unverified.
- Verify native Stage 24 ground-omen path, position and lifetime readback. Preserve its paired diagnostic image from unrelated selection-retry captures so the effect can be identified visually before changing battle behavior.
- Observe the director's ground-effect slots through the native ownership and transform layout established by the code inspection. Bound changed-state samples and image requests, validate native class identities, and retain no effect pointers. Effect identities and coordinates still need runtime verification before driving movement.
- Follow the captured director's effect-creation and transform calls with bounded code reads tied to the observed executable layout. Keep this diagnostic-only; no unknown native method is called and no warning behavior is assumed.
- Extend the one-time native director code inspection to its nearby update/effect routines after the initial window exposed a separate 30-slot VfxData collection. No warning semantics or gameplay rule is inferred from that collection.
- Identify the native Verminion director by its content type and inspect one bounded executable-code window through its verified vtable in the existing diagnostic log. Its event entry ID changes between battles. This research locates battlefield state without adding a signature, tool or runtime memory dump. Also clear observation state if probe initialization fails.
- Give Stage 24 regrouping its own battle timer so frequent replacement waves cannot postpone it indefinitely. Rejoin separated attackers beyond six yalms while preserving an army still travelling together; Stage 12 keeps its existing distances and timing. Native effectiveness remains under test.
- Include bounded Bahamut animation-state changes and native director identity in Stage 24 diagnostics. The director-update hook initialized but exposed no updates during the first attack cycle; warning detection is still unverified.
- Observe at most 64 native director updates during the existing Stage 24 development window through ECommons' existing hook. Release it with the other owned battle diagnostics. Complete scene coverage still showed only permanent arena models and effects; no director category is yet treated as a mechanic.
- Reuse the tested pursuit regrouping policy for isolated Stage 24 attackers. Include object-table draw roots and scene model paths in the existing bounded image diagnostics: effect callbacks alone have not identified tower or circle warnings. Native verification remains pending.
- Remove the experimental Stage 24 effect-based retreat: native HP readback shows damage already occurred when its supposed warning arrived, and the inferred end event could leave attackers held at a gate. Preserve bounded event timing/HP and individual selection-effect observations to resolve the mechanics; ordinary admission remains blocked.
- Test a Stage 24 gate retreat during the native effect interval that was paired with a visible red ground warning and party damage. Reuse phase interruption, healing-gate movement and pending-result preservation; hold new attack waves until the ending effect is observed. This development tactic and tower handling remain unverified, and ordinary admission stays blocked.
- Hide visible Verminion battle panels by their actual loaded addon names during the diagnostic image, preserving results and confirmations. Move the single effect image later in the observed lead-up to party damage while ordinary battle controls continue until the short capture preparation.
- Request one diagnostic image after the first observed boss-attached effect that preceded party losses. Match the image with a native snapshot and temporarily hide the battle status panel; restore it through existing cleanup. This identifies the effect visually and does not treat its filename as a movement trigger.
- Reuse Stage 12's faster pursuit and verified group selection for Stage 24. Native snapshots showed the moving add outrunning repeated five-second ground orders while the army trailed behind. Preserve a party already in melee; native validation remains pending.
- Align Stage 23's prerequisite text with its observed clear. Change the existing static-effect probe from updates to creation after repeated zero-callback observations; capture path/source only, since constructor transforms do not prove final placement.
- Verify background window capture renders the game without taking focus. Reuse the existing boss camera zoom for Stage 24 and let hidden panels render before its single add-phase image; the first image was obscured by the small window's UI. Tower and circle detection remain unverified.
- Include declared scene decals and their texture paths in the existing bounded Stage 24 diagnostic, since ground markings need not be VFX objects. No warning identity is inferred from a texture filename.
- Request the existing diagnostic image from the client's window surface when it is in the background, and capture Stage 24's two-add phase once. Window-surface rendering remains unverified; a saved image alone is not proof that it contains the battlefield.
- Verify actor-effect callbacks arrive on the game thread in Stage 24. Report only positions resolved from the object table: the actor-effect return value did not expose the static-effect transform layout in the native probe. Attack and tower identities remain under investigation.
- Allow up to six minutes of Stage 24 development control within the existing ten-minute result timeout: the two-minute probes ended above half boss health, leaving later phases unobserved. Bound static and actor-effect diagnostics to that window and 64 distinct paths through ECommons' existing native events. Report callback delivery/thread and release subscriptions on stop, result, timeout and unload. Ground-warning identities remain unverified; no movement is inferred from effect names.
- Include AreaObject entries, native object state and scene-effect paths/positions in the existing bounded Stage 24 snapshots. The first probe exposed neither cast IDs nor distinct tower objects, so mechanics remain unidentified rather than inferred from names. Scene reads use the existing ClientStructs declarations and do not modify effects.
- Resume an already abandoned battle only for passive result observation and cleanup. Do not issue battlefield commands, replace its admission or credit its eventual result. FULL STOP still prevents reload dispatch.
- Verify fresh first-attempt clears of Stages 21–23 on the current test character, including the published Gentleman strategy for Twintania. Record Stage 20's fresh mixed opening, defender summons and special. Fresh campaign evidence now covers 1–23; Stage 24 mechanics and repeatability remain unverified.
- Preserve Stage 24's admitted match for passive result observation and cleanup after its bounded development control ends. Stop after a confirmed defeat for mechanics review instead of automatically repeating the observation. Unknown results and the existing battle timeout remain unsuccessful.
- Verify Stage 16 on the first mixed-roster attempt after three Zu-only defeats: use the guide's Mammet stone attackers and reserve four Zu Hatchlings for defense through the existing mixed-roster control. Nasty Peck and automatic Stage 17 admission were observed. Repeated-win reliability remains unverified; Odin support remains a guide option.
- Review Jonathas's certificate minions and show relevant guide alternatives in stage previews: Odin support for 16/19/22, Cursor's DEF debuff for 23, and White Mage Minion of Light healing for 24. Show Odin/Cursor ownership without changing the required roster or buying anything. Certificate claims and purchasing remain an ADS acquisition gap.
- Verify a fresh Stage 15 clear with Wind-up Gentleman on the second attempt, followed by Stage 16 admission. The later prompt-replacement change still needs runtime validation.
- Send lone Stage 15 replacements promptly instead of waiting for four units or 60 seconds; retain the opening assembly and final-phase movement guards. Reuse the existing bounded post-special readback for Odin and Twintania. Runtime validation remains pending.
- Include native hitbox radius and facing in the existing bounded battle snapshots to diagnose pursuit distance without inferring melee range from model size.
- Verify one Stage 12 clear on the final strategy's third attempt after two defeats, followed by automatic Stage 13 admission. Require Wind-up Gentleman, keep startup targeting on Demon Brick, widen the native camera, pursue from behind and rejoin separated attackers. Preserve the army by disabling its withdrawing special. Repeated-win reliability remains unverified.
- Keep Stage 12's rear destination stable across decisions and cover both approach sides and four boss orientations in a focused regression. Shorter pursuit checks still require native press/release completion and verified selection; the contact diagnostic runs once even when an unfocused screenshot is unavailable.
- Verify Stage 7 with the Zu roster on its third attempt after two defeats. Keep the losses separate from victories and describe the limited reliability in stage guidance.
- Verify Stage 6 with Wind-up Airship attackers and Zu Hatchling defenders, including Imp phase handling and automatic transition to Stage 7. Verify return travel from the completed vendor purchase through Lifestream's active-aetheryte readiness signal.
- Approach a loaded Gold Saucer aetheryte before requesting Minion Square through Lifestream; its local aethernet API requires shard range.
- Verify ADS's capped purchase of one Zu Hatchling for exactly 10,000 MGP, once-only receipt accounting and inventory registration after reload. No duplicate purchase or FATE was performed.
- Remove Baby Bat from every campaign requirement. Use permanent Gold Saucer stock Zu Hatchling for Stage 6 defense and Stages 7/16; use the published Wind-up Gentleman strategy for Odin, saving its withdrawing special for the final 25% with eight attackers. Update ownership checks, cost accounting tests and stage guidance. These roster changes still need live clears; no FATE execution is added.
- Require ADS for FC Buff Refill and Fishing. Show it unconditionally in task dependencies, block FC scheduling/manual starts and fishing startup before work begins, and recheck Fishing after relog. Vendor Stock also lists ADS as its purchase provider.
- Verify ten consecutive Stage 2 victories with the Mammet roster on the current test character, stopping exactly at the configured weekly victory target while preserving the existing four wins.
- Show the planned farming stage during initial preparation instead of a previously selected campaign stage.
- Delegate fishing food and repair vendor travel, plus NPC selling, to ADS. Preserve fishing's stock goals and AutoRetainer sell settings. Generic cleanup defers to ADS-owned vendor UI instead of closing its shops or confirmations. Native fishing validation remains pending.
- Move FC buff purchases to ADS's correlated company-action endpoint. Retain stock goals, inventory reconciliation and buff activation in Vermaxion; remove its vendor travel, quartermaster interaction, purchase confirmations and shop cleanup. Native validation remains pending.
- Explain ADS stock routing and Baby Bat's FATE-dependent vendor in settings. Distinguish earlier minion purchases from the new ADS handoff's pending live verification.
- Delegate Gysahl Greens, Dark Matter and Versatile Lure stocking to ADS's existing purchase runner. Vermaxion only requests missing quantities, observes the matching result and cancels its owned request; remove its vendor navigation, NPC interaction, shop callbacks and UI cleanup.
- Delegate minion vendor acquisition exclusively to ADS. Remove direct vendor travel, targeting, shop callbacks and confirmations from Verminion; retain its character budget, receipt accounting and inventory registration. The correlated guarded handoff is under validation.
- Request required vendor minions through ADS after inventory registration, before unnecessary return travel to the Saucer. Add Baby Bat to capped acquisition; ADS discovers and visits Junkmonger Nonoroon in Upper La Noscea, where nearby FATEs can temporarily remove him. Travel and purchase remain pending live verification.
- Verify fresh uninterrupted clears of challenges 1-5 on the current test character and remove the temporary first-three replay path. Challenge 6 correctly stops before admission when Baby Bat is missing.
- Select a registered minion from an occupied palette slot for tutorial briefing summons. Empty slots and stale entries for unowned minions cannot progress the tutorial. Reloading an active tutorial reads its current instruction instead of waiting for a new one before acting. Runtime revalidation is in progress.
- Skip dead or obscured hatchlings when selecting an individual tutorial unit, so a candidate hidden by the top UI does not prevent selecting a visible one for its special action.
- Reuse the existing clear-then-select battle control for the tutorial special and bring an isolated hatchling back to its action party before checking Execute Action. Verify the clicked friendly unit and let movement settle before trying the special. Live testing confirmed regrouping enabled the special and advanced the tutorial to healing.
- Accept the tutorial's explicit Victory result only against its matching saved admission, recording its permanent clear separately from weekly wins. Refresh participation from the native Challenge Log. Abandoned, unknown and duplicate tutorial results cannot advance the campaign. A fresh uninterrupted tutorial replay and automatic Stage 2 transition are now verified.

- Read native constant strings in the existing bounded addon diagnostics, so tournament information text is captured rather than reported as a non-numeric value.

- Include the observed duty, result and saved counters in the existing paused reload diagnostic without starting a run or recording a result. Live FULL STOP is verified at the owned queue and after battlefield selection/movement: queue withdrawal, cleared admission, paused reload without restarting, and no credit for the cancelled battle's later defeat. Weekly counts and campaign progress stay unchanged. Remove all temporary lifecycle test hooks; retain permanent required-minion acquisition through Questionable.

- Explain the Hildibrand handoff's existing duty-provider setup in the acquisition panel: solo unsynced trial settings, FrenRider's eight-player ADS handoff, and ADS maturity eligibility. Keep these settings under the providers' control and distinguish route setup from a verified trial clear.
- Release the unstarted Before-AR login gate as soon as registration identifies this character's saved minion acquisition, before reload actions and ownership readback. Preserve truthful ownership instead of hiding an active hold from DAD.
- Explain the actual minion-acquisition admission blocker, including existing Vermaxion work, DAD ownership, AutoRetainer state, registration or a pending reload action.
- Keep FULL STOP's message explicit while Questionable acquisition cancellation or cleanup is pending, and report a failed cancellation save instead of implying reload persistence was verified.
- Make the acquisition guard safe during plugin construction; Before-AR startup runs before VerminionService exists and rechecks the saved handoff after character registration.
- Make required-minion acquisition a permanent action in the standalone Verminion window. The Wind-up Gentleman route delegates unfinished Hildibrand quests to WigglyQuest's native priority list and reward-quest stop. Questionable owns quest steps and the configured DAD/FrenRider/ADS duty execution; Verminion remains paused and blocks its own new work without blocking DAD. Preserve handoff ownership across reload, never replay an uncertain dispatch, cancel owned acquisition through FULL STOP, and release only inserted priorities and the reward stop. Resume registers the reward through ordinary setup. Live start, active reload without replay, FULL STOP, owned priority/stop cleanup and cancelled reload are verified. Reward completion and registration remain pending. Remove the temporary character-specific launcher, debug-only quest-by-quest acquisition and lifecycle test hooks.
- Add Stage 24 targeting for Bahamut and one Clockwork Twintania: keep the chosen add while it lives, return to the boss after its observed death, and retain the choice across temporary missing-object snapshots. A fresh battle observer seeing one remaining add stays on Bahamut. This guide-based behavior still needs live verification; tower and circle handling remain unavailable.
- Align Stage 24's required roster with the published Gentleman-only guide, use its 30-point summon cost and prompt reinforcement orders, and preserve tower units by disabling its party-withdrawing special. Show the missing minion and guide in the Verminion window. Ordinary admission remains blocked until tower, circle and add handling is implemented and verified; this change has no live battle evidence yet.
- Verify the selected client's DAD bridge accepts WigglyQuest's checked Meta settings and queues the exact checked regular duty. Battle on the Big Bridge has an observed unsynced clear through DAD, FrenRider and ADS; the remaining acquisition trials are unverified. Remove temporary reload/readback/start actions after the proof.
- Include ADS, DAD and FrenRider in the existing acquisition-provider diagnostic so the selected duty stack is visible during setup.
- Cover a pending match crossing weekly reset and reload: refreshed participation is not counted twice, stale/unknown/duplicate results add no victories, and cancellation preserves permanent clears, spending and campaign attempt limits.
- Verify FULL STOP during the owned Hildibrand setup handoff and refusal to restart after reload; remove the temporary one-shot stop test after observation. Quest progress and campaign attempt limits remain intact.
- Add a bounded Debug setup snapshot of ARR Hildibrand quest progress and available duty providers for the authorized test-character acquisition of Wind-up Gentleman. Quest and trial execution remain separate from this readback.
- Use the current job's unsynced level for Verminion quest prerequisites, so a temporary FATE level sync does not incorrectly block a previously eligible character after reload.
- Check Home World eligibility before CPU tournament setup or NPC interaction, show the requirement in Verminion settings, and report an absent tournament dialog accurately.
- Check a completed weekly target before resuming an open challenge menu and again before admission. Close the owned challenge menu on completion. Starting a weekly run from a pause now preserves the separate campaign attempt count; a campaign Resume still resets that count.
- Keep the unfinished CPU rewards mode blocked when resuming an open menu, so it cannot silently enter an ordinary challenge.
- Allow the selected Debug CPU rewards scenario to inspect the Verminion Tournament Recordkeeper's dialog once, bounded to 60 seconds. Capture its native menu through existing diagnostics and close the owned selection menu without registration, reward claims or battle admission. Tournament execution remains unavailable pending that control evidence.
- Use the existing NPC interaction distance for the Tournament Recordkeeper approach and retain a bounded diagnostic capture on timeout.
- Require the published Wind-up Gentleman roster for Stage 23 after verified Airship, Nero and Zu failures. Explain the missing Her Last Vow quest reward before admission; do not buy another unproven substitute. Save its party-withdrawing special for the final 20% with eight survivors. Live verification remains blocked by the test character's missing minion.
- Check nearby action parties for charged specials during queue waits, subject to the stage's ability conditions, and keep two Stage 23 replacement summons in flight before pursuit commands.
- Verify one Zu Hatchling purchase for 10,000 MGP and one Wind-up Nero tol Scaeva purchase for 30,000 MGP, including registration, no gil deduction and exactly-once cumulative receipts. Preserve their purchase reconciliation despite replacing the unsuccessful battle compositions.
- Enable the existing geometry-checked minimap framing for Stage 24's bounded development observation and include its map geometry in those captures. Ordinary Stage 24 admission remains blocked pending actual mechanic handling.
- Keep Stage 23's main army pursuit separate from reserve-wave orders. Sending a replacement no longer makes the older army appear to have received a fresh destination; react to smaller boss movement and verify melee contact instead of accepting a six-yalm gap.
- Match the observed signed MGP currency-icon field; retain exact native balance and offer validation. Capture bounded exchange rows on a rejected quote without submitting a purchase.
- Validate the MGP confirmation's separate item ID/name, one-item quantity and exchange-price prompt before clicking its observed Yes button. Keep unresolved reservations across reload and never repeat a submitted checkout.
- Send Stage 23 replacement waves after 15 seconds or at full capacity; let even one surviving attacker follow Twintania and refresh pursuit after a one-yalm move. The new guide roster still needs live verification.
- Verify Stage 20 with Mammet stone attackers and Wayward Hatchling defenders: explicit victory, exactly one result credit, and 1,360 HP remaining on the defended center stone. This battle adopted hatchlings from the previous opening; the complete mixed opening still needs a fresh-start test. Show this limitation and both verified vendor purchases in the standalone window.
- Verify Stage 21 with the Mammet baseline and corrected minimap framing. One explicit victory and exactly one result credit advance the campaign to Stage 22; repeated-win acceptance remains pending.
- Verify Stage 22 with Mammets on the first attempt, with all three friendly stones surviving. This accessible composition clears the stage without needing the guide's suggested extra minion; repeated-win acceptance remains pending.
- Keep Stage 24 blocked in ordinary runs. In Debug builds only, the existing character-bound Verminion reload selection can run a two-minute phase observation. Capture new native casts and field-object types as well as bounded phase snapshots; missing mechanics remain unverified and the existing timeout abandons result eligibility.
- Extend verified minimap camera control to ordinary stone stages, retaining native structure geometry checks and projection readback. Stage 20 reserves capacity for four Wayward Hatchlings across both summon queues and uses their special near enemy poppets; its complete mixed opening still needs a fresh test.
- Add a per-character minimum gil balance for minion purchases. Enforce it for the complete entry plan, reservation, submission after saving, and native confirmation; preserve it through reload and settings copies. A large cumulative cap cannot spend the reserved balance.
- Leave the challenge list through Return and the table menu through its observed Cancel entry before vendor navigation. Report rejected navigation immediately and capture one bounded shop/menu snapshot when setup stalls, keeping purchase requests behind item, price and budget verification.
- Use the existing NPC interaction distance for the Minion Trader approach; navigation can stop outside the former 2.5-yalm threshold while already within the valid four-yalm range.
- Read basic gil-shop offers from the Shop window's item-ID and price arrays, matching the existing ECommons reader. Require the expected minion name, ID and price before reserving any spending.
- Use the observed OK/Cancel purchase buttons. After reload, allow only an already-visible confirmation matching the saved one-item reservation, vendor, offer and unchanged inventory/currencies; never resubmit the shop request.
- Verify one Mammet #001 purchase for 2,400 gil, with exactly-once spending and no MGP change. Wait for the game's item-use readiness before registration so a closing shop does not immediately fail setup; retain the existing registration timeout.
- Register minions through the inventory agent using the verified bag slot. Count registration only when the existing unlock reader confirms it; dispatching an item-use request is not completion. A Wayward Hatchling purchase and subsequent registration now have positive live readback.
- Verify Stage 19 with the accessible Airship roster: one explicit victory, one result credit, and transition to Stage 20. Observe Cargo's ATK buff and background controls. Stage 20 correctly stops before admission for missing Mammet #001 with zero purchase caps; include the required cumulative cap in that blocker. Show attempts for the next uncleared stage instead of the previous cleared stage.
- Update the expected native debug path for the selected `R:\parasite\vmx` deployment (client path `A:\ff14\parasite\vmx`). Verify copying marker 196 triggers automatic reload and preserves campaign progress and zero purchase caps. All 820 tests pass; version remains 0.5.0.3.
- Let initial setup fill the three-minion entry requirement with only the missing basic Minion Trader minions, preferring owned minions and Mammet #001. Require the complete entry plan to fit remaining gil caps and funds before purchasing one item at a time through the existing receipt checks. Explain entry costs and distinguish missing minions from palette-space failures. Native execution remains unverified.
- Refresh the compiled reload marker through 195 for Debug/x64 rebuilds through `Z:\vmx.bat`, including the reviewed Stage 19 resume after manual reload; keep release version 0.5.0.3.
- Add a capped Mammet #001 purchase path at the Minion Square trader. Verify vendor, item and price, persist one reservation before submission, and require acquisition plus the exact currency deduction before recording spending. Unresolved purchases survive stop/reload/reset and cannot be resubmitted. One live Mammet transaction is verified; other vendor routes remain unavailable. Correct Baby Bat's acquisition hint to the FATE-dependent vendor at Poor Maid's Mill.
- Add a standalone Verminion window through `/vmx v`, the main window and settings, reusing existing manual actions and FULL STOP. Explain weekly modes, progress, attempt limits, purchase accounting, and each stage's minions, acquisition and verification status.
- Replace collection-specific baseline minions with city-vendor Mammet #001, vendor Baby Bat and the level-15 MSQ Wind-up Airship. Check owned requirements after registration and before admission; report missing minions or palette space without purchases. These guide-informed replacements require fresh battle verification. Block Stage 24 before admission until tower and dangerous-attack handling is implemented.
- Use a single Airship group for Stage 19's center defense and buff-phase retreat; intercept adds when the boss leaves. Account for Airships' 25-point cost and nine-unit capacity instead of treating every army as 24 cheap minions.

- Remove Verminion's OS cursor movement, global mouse injection and window activation, including diagnostic image requests. Correct the installed cursor structure's release/repeat field mismatch using observed native input behavior. Background selection, group movement, attack buffs and a complete Stage 16 victory now have runtime evidence; remaining campaign and lifecycle acceptance is pending.

- Replace the existing Verminion task with Participation, Win X, and CPU rewards choices, plus a separate Clear all challenges action. Existing configurations keep participation mode. CPU tournament execution remains unavailable pending implementation and live verification.
- Keep weekly participation, weekly victories, permanent challenge clears, run attempts, and purchase accounting separate for each character. Preserve facts across reload and weekly reset; configuration copies do not copy another character's progress.
- Credit results only after an owned admission and an explicit native result. Cancellation, timeouts, unknown exits, and duplicate result observations never create victories. Read existing participation and sequential challenge unlocks from the game.
- Withdraw a cancelled, positively owned Verminion queue and reject reload adoption of a queue without its saved admission. Persist abandonment before native withdrawal so a lingering queue cannot recreate result eligibility.
- Persist FULL STOP pauses and use the existing scheduler, task identity, ownership, cleanup and debug reload workflow. Stop winning runs at their bounded loss/attempt limit and campaign stages after three unsuccessful attempts; Resume follows strategy review.
- Route required Gold Saucer and Challenge Log unlocks through the existing single-quest integrations, reporting missing level/MSQ eligibility. Register owned inventory minions and prepare available palette slots. First-time unlock execution remains unverified on the already-unlocked test character.
- Add cumulative per-character gil and MGP purchase caps, both defaulting to zero. Prefer owned minions. Mammet and Wayward Hatchling gil purchases have verified receipts; other vendor routes remain pending. Resetting character settings preserves Verminion progress, spending, reservations and pause.
- Sample routine battlefield movement and selection snapshots at a bounded cadence, retaining paired positions plus independent opening, phase and result evidence. Capture Stage 19 native state one second after a special request.
- Observe native battlefield units, HP, statuses, structures, summoning capacity, queue state, controls and results. Verify friendly selections, issue native field orders and specials, restore temporary rendering/input changes, and reconcile a draining summon queue after reload without duplicating requests.
- Require native friendly selection before ordinary movement orders. Preserve existing attacks and result observation when late reinforcement units are obscured, then reassess at the existing strategy cadence.
- Split ordinary attackers between Arcana Stones, redirect survivors from destroyed objectives, and replace casualties with bounded capacity reservations. Baby Bats replace the earlier Succubus counter composition in critter stages; fresh clears with that replacement remain pending.
- Verify ten consecutive Stage 2 farming victories with Mammet #001 and exact stopping at a target ten above the existing weekly victory count. The final battle also confirms background controls with the game out of focus. Earlier participation testing verified no new admission when that weekly requirement was already complete.
- Record challenge clears through Stage 22. Earlier clears verified Stage 6's invulnerability adds, Stage 9's two bomb phases, Stage 12's stone defense, and Stage 15's army recovery/final burst using the earlier roster. Those mechanics remain, but the accessible replacement rosters still need fresh clears on Stages 2-18.
- Select individual members of nearby action parties using native readiness without moving them; restore the full group before movement and preserve engaged troops while probing specials. Avoid redundant orders during Stage 15's stationary final cast.
- Keep the client's frame-level inactive flag consistent with a virtual mouse sample, restoring it after the frame without changing OS window focus. Background selection, movement, Cargo and Stage 19 completion now have runtime evidence.
- Wait for native background click completion before advancing selection or movement. Preserve its hit readback through later hover frames, and use that bounded sample for the same-type selection check.
- Reframe a Stage 19 group after its first failed selection, retaining native friendly-hit verification. Verify all nine Airships reach Gate B at full HP during the first buff window and return to defend the center afterward.
- Read the existing Stage 19 camera distance before zooming so reloads do not apply six extra wheel events.
- Verify Stage 19 camera zoom through background native wheel input; use the existing minimap control when a unit or destination needs framing.
- Rally idle Stage 19 Airships beside the stone, avoiding both stone models. Refresh local targets before movement and reject enemies that leave the defended area. Observe Gilgamesh's buffs before selection/camera work and prioritize gate recovery over stale attacks.

## Unreleased - Choke-abo progression V3 (runtime acceptance pending)

- Detect a Choke-abo V3 endpoint disappearing while progression is deferred, then reassess through the normal readiness/Stop gates when it returns, even when no Ensure call occurred during the reload.
- Require Choke-abo V3 for target progression and expose owned parents/NPC permits, Grade 1-3 feed, gil/MGP reserves, and Fall back/Skip/Stop. Keep legacy Always Race behavior and existing V1/V2 protocol parsing available.
- Recognize the V3 required-supplies phase separately from optional feeding while Choke-abo owns the action.
- Track three hours of queue/racing activity per character at the 09:00 UTC reset, retain consumption across reload, and split activity across the reset boundary. Exhaustion stops new race admissions while breeding remains eligible; a race already underway may finish.
- Add explicit progression Pause/Resume/Stop, saved stop state, and continuation through the existing readiness/ownership checks and manual action path. Report current pedigree, racing rank, allowance and the next-action reason. Target completion requires current rank-50 evidence from V3.
- Route missing Gold Saucer/racing/breeding unlocks through existing single-quest paths. Recognize the installed WigglyQuest IPC names, retain only the selected unlock in its priority list while owned, and clean up that entry on completion or Stop. Ordinary level/MSQ prerequisites remain explicit blockers.
- Bind new reload selections to the selected character and wait for owned UI cleanup before dispatch; FULL STOP cancels that pending dispatch.
- Reconcile the registrar's tutorial queue into the existing racing loop. Send current race-admission allowance through V3 and identify owned queues by actual racing territories; the shared Gold Saucer content type alone is insufficient.
- Resume confirmed racing/tutorial activity even when an earlier handoff lost its pending flag. Keep saved Stop gating and release tutorial movement keys on Stop, state exit and unload.
- Carry an explicit Resume through an existing race to the next Choke-abo handoff. Handle tutorial instructions using current keyboard bindings and native menu events, and exclude messages from earlier courses on a new admission.
- Queue pedigree progression from the current location after feeding; avoid the manual batch's unnecessary return-home logout transition.
- Select the observed Gold Saucer/Sagolii Road controls, verify native roulette 18 and reject mixed or unrelated selections before Join/Commence. Include this exact queue in allowance accounting and cancellation.
- Confirm the selected row's checkbox before Join, recognize the initial InDutyQueue state, and leave race results through the native LeaveButton with closure verification.
- Persist allowance activity only after native queue admission, clear its sample at activity end, and exclude result display and travel from reload reconciliation. Do not overwrite an unchanged allowance on unload.
- Reassess through the existing continuation path when Choke-abo's V3 endpoints return after a reload; keep saved Stop and ownership gates in effect.
- Recover an owned covering confirmation through normal Resume using V3 evidence that verifies its selected stock, parent window and fee. Permit character-settings initialization during that exact owned interaction while retaining Stop and competing-automation gates.
- Preserve version 0.5.0.2 and unrelated work. Isolated Debug builds and focused decision checks do not establish live queue cancellation, reload/resume, breeding or gameplay acceptance.

## Unreleased - Register Registrables queue creation

- Read registration state through Dalamud's unlock service and managed item rows so unloaded native EXD rows no longer abort queue creation. Use the same reader before item use and during registration verification; require loaded player data, available UI state, and a valid unlockable item, with explicit failures for missing data or exceptions.
- Preserve inventory order, personal-list selection, duplicate handling, seven-second verification, and three item-use attempts. Remove the obsolete native-result decoder and its tests; keep version 0.5.0.1.

## Unreleased - Deep Duck FULL STOP in Misc Commands

- Send `/dduck stop` first in the Misc Commands startup and Send now bundle to stop Deep Duck automation, and show it in the configuration command list. Preserve the existing Misc Cmd toggle and applicable run-start timing.

## Unreleased - I393 character setting filter

- Add a character-list filter for enabled settings such as Fishing and nag your mom. Keep Default Config and the editing selection intact; reset to All characters when the configuration window closes.

## Unreleased - Refill Listings retainer dialogue cleanup

- Advance greeting and farewell dialogue only while Refill Listings owns a retainer session. Wait for dialogue readiness and observed UI closure before continuing or releasing AutoRetainer, preserving the retainer list between targets and closing it at final cleanup.
- Retain dialogue ownership through failure, cancellation, and suppression recovery; clear it after closure, character changes, or Full Stop. Preserve confirmation safeguards, cleanup pacing, handoff settlement, saved settings, and version 0.4.0.14.

## Unreleased - I388 FC Buff Refill stock count

- Count only current FC action list entries so stale row text cannot inflate Seal Sweetener II stock or shorten a refill. Accept an empty list as zero stock, fail unreadable current entries, and preserve activation row indices.

## Unreleased - I417 Stylist replacement-equipment confirmation

- Accept the ready OK/Cancel prompt while polling a VERMAXION-started Stylist update so Stylist can finish saving the gearset; preserve native equipment's busy protection and existing update timing.

## Unreleased - I384 Current Job Equipment after Stylist

- Complete Current Job Equipment as soon as an accepted Stylist update reports idle, without an extra save-verification wait or duplicate native equipment pass. Fail on Stylist polling errors or busy timeout; preserve native fallback when Stylist cannot start, its overlap protection, and native save verification.

## Unreleased - Refill Listings gil withdrawal

- Add a default-on Refill Listings suboption that enables AutoRetainer gil withdrawal for the current character's retainers on login/reload and at refill start. Set withdrawal mode while preserving each retainer's percentage and unrelated settings; save through AutoRetainer only when a setting changes. Disabling the suboption stops applying it without undoing AR settings.

## 0.4.0.11 - Registrable inventory default upgrade

- On the first successful load after upgrading, set every saved account's default profile and character profiles with Register Registrables disabled to `All unregistered registrables discovered in inventory`. Preserve enabled character profiles' source choices, all enablement checkboxes, personal lists, and unrelated settings.
- Save the upgrade and its completion flag together through the existing atomic account writer. Failed writes leave the upgrade incomplete; new accounts are marked complete on their first save. Later source changes remain saved, including changes on disabled profiles, and the upgrade remains available when skipping directly to a later version.
- Advance the project, plugin/repository manifests, versioned download URLs, and release workflow fallback to `0.4.0.11`.
- Verification: all 66 targeted registrable and account-persistence tests pass, including four migration cases and new-account save coverage. The isolated Debug x64 plugin build succeeds with only the existing PInvoke.User32 NU1601 warning; version metadata and download URLs validate.

## Unreleased - Fishing display and task handoff fixes

- Start Global Fishing collapsed while retaining ImGui expansion memory.
- Restore VERMAXION's YesAlready pause entry if it disappears during work or cleanup, preserve other plugins' entries and Ocean Fishing's bait-shopping exception, and cover the remaining manual task buttons with the existing pause wrapper.
- Reuse the last task's two-second settlement for final handoff, retaining the final blocker check and normal settlement if a new blocker appears. Stylist timing is unchanged.

## Unreleased - Stable Dalamud builds

- Download stable Dalamud latest.zip and remove the obsolete pinned-version message. Keep the plugin-only build, plugin version 0.4.0.8, action versions, and release behavior unchanged.
- Verified workflow YAML and run-block syntax, the existing Release x64 restore/build commands against Dalamud 15.0.3.5, and nonempty ZIP/JSON packages with matching versions. The build passes with the existing PInvoke.User32 NU1601 warning. GitHub verification awaits publication.

## Unreleased - Feature sync and release recovery

- Keep one Register Registrables sync control beside the scheduling checkbox. It copies scheduling enablement, source choice, and an independent personal list to each character; character default matching and Use default cover the same three settings. Remove the separate source/list sync controls and source explanation.
- Apply row and all-character defaults, fishing-stock row/catalog defaults, and setup-wizard bulk sync immediately without confirmation. Preserve the existing save paths, completion-history behavior, and target-automation pause handling. Individual character reset/delete, task reset, and direct PvP enable warnings retain their existing confirmations.
- Verification: 110 existing registrable, persistence, UI/wizard, fishing-stock, and Choke-abo policy checks pass. A focused check compiles the production copy/default-matching callbacks in memory and passes 12 scenarios covering each feature field, independent character lists, preserved unrelated state, and no registrable Use default warning; production syntax confirms one control and immediate bulk sync. The Debug x64 build through `Z:\vmx.bat` succeeds with matching Dalamud 15.0.3.5 references and only the existing PInvoke.User32 dependency warning. Live UI interaction was not performed. Tests, workflow, and version `0.4.0.7` remain unchanged.
- Recover release `v0.4.0.7` using only GitHub's release-job rerun operation. The first retry failed with `Error creating asset temp dir`; after inspecting that failure, the next retry succeeded without a workflow change. Attempt 3 of run `35266275538` reused the original build artifact and published against commit `2729f131e498ed6813ee819b3b7f932d53310f86`, with `latest.zip` (1,170,204 bytes) and `VERMAXION.json` (887 bytes), both uploaded. This recovered release contains the existing commit, not these uncommitted sync edits; GitHub Actions did not run tests.

## Unreleased - Registrable unlock detection and default source

- Decode native registration result 2 as unregistered and registrable, and result 1 as registered. Other results, unavailable native components, and exceptions fail closed with the actual result or failure detail in existing logging.
- Default new configurations and missing saved source fields to inventory discovery. Preserve explicit saved source choices and personal lists, manual starts, filtering, seven-second verification, the three-attempt limit, and FULL STOP behavior.
- Add native-result regression coverage, extend source/default persistence coverage, and advance the startup attempt marker without changing release versions.
- Verified with 191 targeted registrable, recovery, catalog, and persistence checks and a Debug x64 build through `Z:\vmx.bat` using Dalamud 15.0.3.5 references. The build retains the existing PInvoke.User32 dependency warning.
- Free to Play 4 loaded marker `registrables-native-unlock-20260917-02` at 15:28:09 on 2026-09-17 and dispatched once after character registration. All eight queued items, including Ramuh Crystal, verified successfully by 15:29:34 with no retries, skips, or exhausted items. Native UseAction returned false for these requests, producing existing warning messages, but the subsequent unlock checks confirmed every registration.

## Unreleased - Native command string lifetime

- Use the native UTF-8 string constructor for command text and free the allocation in a finally block after chat dispatch, including failures. This fixes missing termination and cleanup found during the patch 7.56h compatibility review; command routing and task logic remain unchanged.

## Unreleased - Register Registrables manual runs and source settings

- Group inventory discovery and the personal list as two source choices under the existing scheduling checkbox, with Configure list beside the personal-list option. Preserve saved selections, lists, defaults, and character overrides; source controls remain usable with scheduling off.
- Route dashboard Run and saved debug reload attempts through the same manual start, bypassing only scheduled enablement. Share source checks so inventory discovery accepts an empty list while personal-list mode explains its empty-list blocker.
- Show the selected source and service progress or failure. Report verified registrations, skipped or exhausted items, and no eligible items separately. Preserve filtering, unlock checks, seven-second verification, the three-attempt limit, and FULL STOP cleanup; emit a distinct attempt marker in existing startup logging without changing versions.

## Unreleased - Refill Listings native row selection

- Resolve each withdrawal from the current sell-list row's native inventory slot, validating row bounds, numeric values, unique occupied slots, and the intended item's quantity and quality. Wait for addon readiness within the existing timeout; invalid mappings use existing failure cleanup.
- Report the native row in existing withdrawal diagnostics without an inventory-order fallback. Preserve callbacks, selection rules, pacing, withdrawal verification, inventory cutoff, and AutoRetainer handoff. I353 closed after Account 1 verification: all remaining selected listings withdrawn, retainer UI closed, and AutoRetainer suppression released.

## Unreleased - Refill Listings withdrawal diagnostics

- When the saved debug reload task is Refill Listings, log withdrawal dispatch and outcomes with the planned slot/item/quantity/quality, native sell-list row, context-menu readiness/ownership, selected entry and callback arguments. Compare listing snapshots by item/quantity/quality counts, including changes to other listings, and distinguish unreadable inventory from a readable empty inventory.
- Capture failure evidence before progress resets; diagnostic read failures preserve the existing cleanup path. Withdrawal callbacks, selection, pacing, inventory cutoff, and acknowledgement checks are unchanged.

## Unreleased - Refill Listings failure handoff

- Reset the owned bell helper when Refill Listings fails. A failed bell-opening task can now finish the existing UI cleanup and handoff checks instead of leaving AutoRetainer held indefinitely by an abandoned active helper.

## Unreleased - PvP route enable warnings

- Show Frontline and Rival Wings checkboxes in red in account-default and character settings. Require a Yes/No warning before enabling either route; disabling remains immediate.
- Include route warnings in default-copy confirmations, including character reset, row/all-character sync, and setup-wizard bulk apply. Cancel pending actions on No, dismissal, window closure, or account/profile changes. Keep acceptance in memory with no saved setting or scheduled-run popup.

## Unreleased - GitHub Actions plugin build

### Fixed

- Build only the VERMAXION plugin project in GitHub Actions so releases do not require the sibling mom repository used by offline tests. Keep the solution and tests available locally.

## Unreleased - nag your mom window cutoff

### Fixed

- Pass each batch its local window's closing UTC timestamp, including overnight windows. Block starts at the cutoff and visibly require mom queue-deadline support; never fall back to legacy starts for scheduled work.
- After mom confirms expiry and queue/match cleanup, complete the mom task for that cycle and skip remaining routes. Credit only observed completed matches, once, without filling daily caps or changing character settings. Keep withdrawal blockers, task settling, and AutoRetainer handoff ownership intact.

## Unreleased - I333/I334/I338 equipment, Fashion Report, and return travel

### Fixed

- Use Stylist for Current Job Equipment with exact gearset-save verification and the existing native fallback. Block native equipment while Stylist is busy or a dispatched update cannot be confirmed idle.
- Complete Fashion Report after one judging, retaining result-window closure and weekly completion handling.
- Add shared per-character return settings for mom/dad, enabled by default with `/li fc`. Send one single-line slash command before each new request, require observed travel and two stationary seconds with Lifestream and vnav idle, and fail through existing cleanup after three minutes without retrying.

## Unreleased - Ocean Fishing registration acknowledgement

### Fixed

- Wait for a fully ready Dryskthota menu and observed dialog progress before completing boarding or route selection. Compare menu contents across sparse updates, preserve localized boarding/embark checks and route preferences, and retry an unchanged recognized menu up to eight times with five seconds between attempts and after the final attempt. Retain queue recognition, the registration deadline, and existing failure cleanup.
- Increase Ocean Fishing startup travel to eight total attempts within the existing travel timeouts and registration deadline.

## Unreleased - nag your mom rank checking

### Fixed

- Let Test Series Rank finish through framework updates while idle or with windows closed. Reuse a pending request and poll mom's read-only status every two seconds; keep loading distinct from IPC errors and cancel pending tests on Stop, logout, character change, or disposal.
- Let mom own the Casual CC rank-25 gate and suspend the task watchdog during its rank wait. Preserve the selected reload task and normal run dispatch.

## Unreleased - I332 reload debug task

### Added

- Add `/vmx debug` with one saved task selection for the next plugin reload. After character registration, consume one attempt, run existing FULL STOP cleanup, refresh dashboard availability, and invoke the existing manual action through its normal wrapper. Keep configuration-only stubs unavailable and preserve task prerequisites and scheduling.
- Show pending, dispatched, or blocked status in the debug window and existing logs, with no debug retry. Keep the checkbox selected after an attempt; unchecking or replacing it cancels pending startup, while FULL STOP cancels this reload's pending attempt without clearing the saved selection.

## Unreleased - I329/I330/I331 travel, listings, and task settings

### Fixed

- Retry failed Ocean Fishing startup travel up to three total attempts, resetting the Limsa-to-dock route and rechecking preparation within the existing registration deadline. Bound stalled aethernet waits and cancel only owned travel before retrying.
- Let Refill Listings wait for its context menu within the existing twelve-second timeout after dispatching the item-row callback once; retain detailed diagnostics if the menu never opens.
- Show Settings beside every configurable task in both dashboard views, opening its configuration section for the current or last loaded character in this session and selecting that character's account. Disable these links until a character is known.

## Unreleased - Retainer ownership and refill recovery

### Fixed

- Maintain verified AutoRetainer suppression throughout Before-AR work and retainer cleanup, retaining the two-second recovery throttle. Wait for queued AutoRetainer work to drain before retainer actions, recheck after Lifestream routing, and preserve the formal After-AR completion handoff.
- Run manual Refill Listings through the single-task engine path. Keep suppression through verified bell closure and the handoff quiet period, releasing only VERMAXION's current lease before the next-login arm callback.
- Resume interrupted refills by returning to the retainer list, reselecting the current retainer, and rescanning live rows. Preserve original Random selections and remaining counts, reconcile pending withdrawals against matching item/quantity/quality counts, and retain interrupted step time limits. Clear transient recovery state on completion, Full Stop, or character change.
- Recognize the localized buyback prompt from Addon row 215 during retainer cleanup, confirm Yes once per visible dialog, and wait for its UI transition. Use the localized Quit entry; leave unrelated or unreadable confirmations untouched with a blocked status. Intermediate exits return to the retainer list; final and inventory-limit exits close the entire bell session.

## Unreleased - Ocean Fishing recovery and inn parking

### Fixed

- Add global Fixed locations, Continuous rail, and Spacing mode radio controls beside the Ocean Fishing provider. Fixed locations defaults to an independent random choice among 32 built-in positions; Continuous rail samples the existing ranges. Neither random mode uses passenger assignment, reserved slices, or nearby-player clearance. Spacing mode retains passenger assignment and spacing explicitly.
- Persist the new positioning choice, default missing or invalid values to Fixed locations regardless of legacy rail settings, and lock controls during active Fishing runs.
- Pause placement during the boarding lobby and transitions, retain arrival/facing/path-stop verification, and allow at least two minutes before exhausting positioning retries. Only Spacing mode retains the post-arrival player-clearance check.
- Recover from the English full-inventory game error by attempting an onboard sell and returning to the saved fishing position; hand off to result handling if the voyage ends. Require the ErrorMessage channel, no player sender, an active Ocean Fishing duty, and VERMAXION provider ownership; ignore identical player chat and duplicate recovery triggers.
- Allow character-select recovery to finish during fishing relogs while retaining the registration deadline.
- Release the final AutoRetainer handoff while logged out when fishing is inactive, without bypassing other safety blockers.
- Recognize the full inn catalog, yield idle parking to AutoRetainer, and leave already-parked characters in their inn.
- Omit character identity from the roster-assignment diagnostic.

### Local correction verification

- Focused fishing/lifecycle checks passed 311/311; the full native suite passed 706/706. Coverage includes production configuration save/reload, all three positioning choices, missing/invalid/legacy settings, random-mode independence from spacing state, occupied-destination regression, inventory-message origin and provider ownership, and relog/handoff boundaries.
- The x64 Release build succeeded with zero errors and the existing PInvoke.User32 version-resolution warning. Git whitespace checks passed. Vendor return, voyage completion during recovery, and inn-parking ownership were reviewed in source; no new live-client testing or deployment was performed.

### Contributor verification (PR #6, before these corrections)

- The full native suite passed 671/671, including an independent rerun. Regressions check roster-order-independent assignment, unclaimed fallbacks, geometry-based clearance, the minimum retry interval, inventory-recovery decisions and relog/handoff policies; source-wiring checks cover chat delivery, inn-parking guards and diagnostic privacy.
- An isolated x64 Release build succeeded with zero errors using cached packages. NuGet source/vulnerability checks were unavailable, and the existing PInvoke.User32 version-resolution warning remains.
- These results verify the tested policies, wiring and compilation, not full in-game acceptance.

### Observed live behavior (2026-09-03)

The PR contributor checked existing deployment logs after the build, without starting new runs. The contributor reported that the inspected deployed build's runtime source matched the original PR except for the removed identity diagnostic, and that loaded module identity matched the inspected disk build across all sampled clients. These observations predate the positioning-mode and inventory-message corrections above.

- Historical validation: the contributor reports that these features have worked live in previous versions. Paths absent from the current sample are not newly re-exercised here; this does not mean they have never worked. That earlier-version confirmation is separate from the directly inspected observations below.
- Two retained fishing sequences latched settled passenger rosters, reached their assigned built-in spots, acknowledged fishing after one paired start attempt, completed the duty, and settled the result screen. One explicitly held positioning for 28.244 seconds during the lobby with no cast sends or destination selections; later route transitions retained the movement lock.
- Retained logs contain 10 idle-inn entry requests, 23 in-inn/AutoRetainer-enable confirmations, 1,265 normal AutoRetainer finish signals and 1,265 engine continuation messages. These are aggregate event counts, not individually paired events, distinct characters or proof that the logged-out handoff edge case ran.
- Limits: the two positioning samples had only two and four passengers. Contested fallback, simultaneous multi-client assignment, retry exhaustion, full-inventory recovery, character-select timeout extension and logged-out final handoff were not revalidated by this sample. Log rotation and size caps limit coverage; silence is not a failed run. No new deployment or forced failure test was performed for the anonymised candidate.

## 2026-08-31 - I280/I281 Dashboard pause and dialog readiness

### Fixed

- Dashboard Run actions now hold the VERMAXION YesAlready pause for their manual-service lifetime, while policy-guarded Yes/No handling waits for a ready dialog before reading its prompt.

## 2026-08-30 - Ocean Fishing hold commitment and FC buff stock/travel

### Added

- Added the default-off per-character `Maintain configured Seal Sweetener II stock target` setting to FC Buff controls, account-default propagation, and the existing setup wizard. When enabled, the configured purchase quantity becomes the final stock target and only the live shortfall is bought, including one replacement before a VERMAXION activation.

### Fixed

- Scheduled Ocean Fishing logout now overrides every configured return after cleanup and lifecycle restoration. Once its hold is persisted, later checkbox changes no longer cancel the automatic wake, login, and fishing handoff; the global master and Main Window `FULL STOP` remain cancellation paths.
- Maelstrom FC buff travel now recognizes canonical `/li gc` completion in territory 128, while preserving the continuous eight-second settlement before Quartermaster pathing. Ocean Fishing retains its separate territory-129 Limsa route.
- Manual FC Buff refill now owns a YesAlready pause, caps stock targets to the Free Company rank's inactive-action capacity, and fires each purchase callback only once before waiting for VERMAXION's validated confirmation.

## 2026-08-30 - I259/I260 Jumbo Cactpot recovery and Mini requirements

### Fixed

- Jumbo broker and cashier travel now uses `/li Cactpot` without a forced-jump loop, reconciles authoritative cashier exhaustion against stale payout counts, and continues into the three-ticket broker purchase when due; Mini Cactpot now reports its actual Lifestream `/li saucer` and vnavmesh requirements without Teleporter.

## 2026-08-29 - I256 Chocobo repeat queue selection

### Fixed

- Chocobo Racing now reselects row `10` before Join on every race attempt, preventing later attempts from joining the CFC anchor duty, The Whorleater.

## 2026-08-29 - I255 Gold Saucer route resets

### Fixed

- Mini Cactpot, Jumbo broker and cashier, and Fashion Report starts now reset through `/li saucer`; during Jumbo broker and cashier travel, VERMAXION again jumps every 500 ms until within 10 yalms of the destination.

## 2026-08-29 - I253/I254 Jumbo travel settlement and unlock blockers

### Fixed

- Jumbo Cactpot broker and cashier routes now wait for Lifestream to be idle and for territory 144 and player availability to remain continuously settled for eight seconds before starting ground navigation.
- Enabled Mini Cactpot, Jumbo Cactpot, and Fashion Report tasks now show the existing red Blocked prerequisite state when `Scratch It Rich`, `Hitting the Cactpot`, or `Passion for Fashion` is incomplete, before cadence, route, or availability checks.

## 2026-08-29 - I250 offline-hold human test controls

### Added

- Global Fishing settings now open by default and state the scheduled Home/Inn prerequisite; Advanced test controls adds bounded main-menu logout and full next-gate Ocean Fishing hold actions using the existing coordinator paths.

## 2026-08-28 - Shared ground-navigation recovery

### Changed

- Centralized every VERMAXION ground movement and stop through VNavmeshIPC, which now suppresses healthy duplicate destinations and performs one jump plus one reissue only after a 12-second stall without 0.5 yalms of progress; Fashion Report and Cactpot no longer run separate recovery loops.

## 2026-08-28 - I243 progress-gated Fashion Report navigation

### Changed

- Fashion Report now keeps a healthy Masked Rose vnavmesh path instead of replacing it every five seconds; a jump and fixed-route retry occur only after player movement remains below 0.5 yalms for 12 seconds.

## 2026-08-28 - I242 default-off FC Buff activation

### Added

- Added the per-character `Allow VERMAXION to activate Seal Sweetener II` checkbox to the existing FC Buff controls and setup wizard. It defaults off for new and legacy configurations and follows character cloning and account-default synchronization.

### Changed

- FC Buff Refill still reconciles live Seal Sweetener II stock and uses the existing purchase flow when stock is empty. With activation disabled, positive stock completes without opening the activation menu, consuming a buff, or decrementing the VERMAXION stock ledger; explicit opt-in retains the verified activation path.

### Verification

- Added one focused regression for default-off configuration propagation, forced reconciliation, zero-stock purchasing, positive-stock no-activation, and preserved opt-in activation. The full suite, packaging, publication, clients, and live testing remain outside I242 scope.

## 2026-08-28 - I237 Choke-abo breeding-aware racing

### Added

- Added per-character Chocobo automation mode and Target Pedigree settings: Always Race by default, target pedigree G9, retirement rank 40, and preferred feed Grade 3. Defaults, character cloning, and account-default copying preserve all four values without silently normalizing corrupt saved ranges.
- Added strict built-in-JSON clients for `ChokeAbo.Breeding.EnsureTargetCycle.V2`, `GetTargetCycleStatus.V2`, and `PauseTargetCycle.V2`. Version, unsigned Content ID, request ranges, required response fields, UTC eligibility, and known phases fail closed; V2 never falls back to V1.
- Added current-character Choke-abo phase, racing block, readiness, game-action, reason, and covering-eligibility status to the existing Chocobo configuration area.

### Changed

- Always Race retains the fail-open `ChokeAbo.Breeding.ShouldBlockRacing.V1` behavior and makes no V2 calls.
- Target Pedigree invokes Ensure at the real start boundary and after each completed race. VERMAXION retains the active task while Choke-abo owns immediate navigation/UI/purchase/feed work, resumes racing when yielded, and continues the ordinary race batch without further breeding calls once the target racer is ready.
- Stable Target Pedigree waits and blocks use a distinct deferred terminal result, allow unrelated queued tasks to continue, and write neither completion nor failure timestamps. Only a completed configured daily race batch uses the existing daily completion fields.
- Disabling active target racing, changing it to Always Race, global stop, and plugin disposal request a best-effort Choke-abo pause without blocking Full Stop or Always Race.
- Exact retirement, covering, fledgling-selector, and adoption behavior remains capture-blocked in Choke-abo Stage 1; no live target-cycle acceptance is claimed.

### Verification

- Focused target-cycle and existing lifecycle policy coverage passes 60 tests. The Debug x64 VERMAXION project builds with zero errors and only the existing `PInvoke.User32` NU1601 warning. Packaging, publication, client control, and live-game testing were not performed.

## 2026-08-28 - I236 scheduled Ocean Fishing offline hold

### Added

- Added the default-off global `Log out between scheduled Ocean Fishing voyages` setting and a persisted hold record containing its phase, completed/next registration times, snapshotted startup gate and wake time, logout/wake attempt times, and AutoRetainer multi-mode restoration ownership.
- After an automatically scheduled voyage completes a verified Home or Inn return and `FishingRunLifecycle` finishes restoring AutoHook, AutoRetainer multi-mode, and YesAlready, the hold disables AutoRetainer multi-mode, sends `/logout`, confirms the ready Yes/No dialog, retries every five seconds, and waits offline until the snapshotted configured startup gate.
- Wake restores AutoRetainer, requests one guarded first-character bootstrap login, waits for stable world readiness and character registration, then invokes the existing fishing startup coordinator with a dedicated scheduled-wake trigger. The existing candidate queue and relog coordinator still choose the eligible fisher.

### Changed

- Intentional holds suppress ordinary character-select recovery, window watching, fake-ready nudges, idle inn parking, AutoRetainer postprocess requests, and Before-AR startup. Disabling the feature or global master, and Full Stop, cancel the hold and restore AutoRetainer without forcing a login.
- A logout that does not complete within 45 seconds is treated as a successful voyage with a warning: AutoRetainer is restored, the hold is cleared, and the character remains logged in. Manual/test runs and returns other than Home or Inn never create a hold.
- Global Settings now retains its existing controls inside three collapsed groups: Display & DTR, Automation & Recovery, and Fishing. The account/character settings and Main Window window-watcher control are unchanged.

### Verification

- The focused Debug x64 offline-hold and protected Ocean Fishing regression command passes 14/14 tests, including default/legacy configuration, scheduled/manual and Home/Inn boundaries, future snapshotted wake calculation, persisted-phase resume, ordinary-automation suppression, one wake, cancellation, logout timeout restoration, return settlement, paired start cadence, and permanent movement lock.
- The Debug x64 plugin build with `--no-restore` succeeds with zero errors and only the existing `PInvoke.User32` NU1601 resolution warning. The full suite, packaging, release flows, and live clients were not run; title-screen logout/wake/bootstrap acceptance remains explicitly not tested.

## 2026-08-27 - Ocean Fishing providers, route families, and dependency readiness

### Added

- Added an enabled-by-default persisted global automation master, controlled by the Main Window `Enabled` checkbox and `/vmx on|off`. It blocks new automatic entry points while preserving per-character automation gates, manual controls, and already-owned cleanup/recovery.
- Added one global Ocean Fishing provider choice. `VerMAXION + AutoHook` remains the compatibility default and retains the existing placement, bait, facing, `/ahstart`, `/ac cast`, and recovery path. `AutoHook AutoOceanFish` gives AutoHook all in-duty fishing while VERMAXION retains preparation, registration, result handling, cleanup, and return.

### Fixed

- The task dashboard continues to display `Teleporter` while resolving readiness through the verified `TeleporterPlugin` internal name.
- Ruby-route registration now accepts the completed Thavnair embark question when it is followed by the Endwalker eligibility warning, while still rejecting route text without `?`.
- I212: FC Buff refill now recognizes active Seal Sweetener II/III by live status strength and uses the enabled localized context-menu action, completing only after activation is verified.
- I227: The FC Buff rank 1-7 shortcut now records daily, weekly, and monthly completions at the configured cadence. Every AR uses the next daily reset only while the FC remains rank 1-7; rank 8+ and unknown ranks retain ordinary Every AR behavior.
- I232: Restored the configured Casual CC series-rank-25 gate before mom dispatch and added a read-only dashboard/chat series-rank test.
- I234: Low-inventory Refill Listings completion now closes child retainer surfaces while preserving the retainer list.

### Changed

- Provider changes now persist AutoHook's `AutoOceanFish` setting immediately, and every Fishing run verifies the same alignment before taking lifecycle ownership. The AutoHook-owned provider also enables AutoHook before any relog or duty entry. An unavailable Boolean setting or static `Save()` surface blocks startup with an actionable status; the aligned `AutoOceanFish` value is not restored after the run.
- Ocean Fishing route selection now uses the game's two dialog families instead of localized route-name matching: Indigo selects entry 0 and Ruby selects entry 1 for Ruby Sea, Thavnair, and Unknown Island destinations. Legacy serialized Thavnair preferences behave as Ruby and are hidden from both selectors; unavailable requested entries fall back to entry 0.
- The task dashboard now reports `Ready`, `Missing`, or `Needs setup`. Fishing reports provider drift; Mini Cactpot, Jumbo Cactpot, and Fashion Report accept either enabled TextAdvance or XA Slave Skip Dialogue; required Saucy checks configuration accessibility. Reporting remains informational except that Fishing refuses to start when provider synchronization fails.

### Verification

- The focused I227 FC-buff recovery matrix passes 28/28 tests across ranks 1, 7, 8, 30, unknown rank, and all four cadences. A fresh Debug x64 `--no-restore` plugin build succeeds with zero errors and only the existing `PInvoke.User32` NU1601 warning.
- A fresh Debug x64 `--no-restore` plugin build for the global-master and dependency-name fixes succeeds with zero errors and only the existing `PInvoke.User32` NU1601 warning. The existing Ruby/Thavnair matcher and regression remain unchanged; no tests or live-client checks were run for these narrow fixes.
- The focused provider, reflection, route, ownership, and dependency regression set passes 30/30 tests.
- The focused `FishingPolicyTests.RegistrationEmbarkPromptAcceptsRouteSpecificEnglishPrompt` regression passes 1/1. A fresh Debug x64 plugin project build succeeds with zero errors and only the existing `PInvoke.User32` NU1601 dependency-resolution warning.
- The full suite, live clients, packaging, deployment, and release workflows were not run.

## 2026-08-26 - Ocean Fishing route and aethernet ownership

### Changed

- Ocean Fishing now honors global or per-character Indigo, Ruby, and Thavnair route preferences with a safe first-route fallback, while Arcanists' Guild aethernet travel remains movement-exclusive and falls back directly after a stuck window.

## 2026-08-25 - FC rank guard, Fishing conflict warning, and task dependencies

### Changed

- FC Buff refill now completes without purchase work for Free Company ranks 1â€“7, while unknown ranks preserve the existing flow.
- Enabling Fishing now warns when AutoHook's AutoOceanFish setting is active, with direct settings access and a persistent opt-out.
- The task dashboard now shows informational loaded-plugin dependency readiness without changing task eligibility.

## 2026-08-24 - Scheduled FC Buff refill and settled GC arrival

### Added

- Added per-character Every AR, daily, weekly, and monthly FC Buff cadence, including configuration and setup-wizard controls plus an explicit cadence-state reset. Existing and new configurations remain on Every AR by default.

### Fixed

- FC Buff completion now stamps scheduled cadence only after success, including when Seal Sweetener II is already active; failed runs remain due and manual runs continue to bypass cadence.
- FC Buff GC travel now waits for the expected territory, a completed zone transition, an available player, and idle Lifestream before Quartermaster navigation or a teleport retry.

## 2026-08-22 - Arcanists' Guild aethernet stall recovery

### Fixed

- Ocean Fishing now waits on its own aethernet window and, if it remains visible for 10 seconds, closes it, cancels Lifestream, stops vnavmesh, and retries within the existing navigation timeout.

## 2026-08-22 - Opt-in Ocean Fishing window watcher

### Added

- Added the disabled-by-default `Actively check for Ocean Fishing windows without AR pre/post process` checkbox to the fixed top of the Main Window. It starts the existing Ocean Fishing coordinator during an open startup window without waiting for an AutoRetainer pre/post process.

### Changed

- Window-watcher starts use the existing candidate order, startup guards, relog and recovery paths, configured return destination, and voyage behavior. Manual and AutoRetainer post-process starts remain unchanged and share the same per-window deduplication.

### Verification

- Reviewed the complete source diff and exact-scope searches. Automated tests, builds, packaging, deployment, and live-client verification were not run by request.

## 2026-08-20 - Complete UI/UX review implementation

### Added

- Added a saved global `Auto width the columns` setting, enabled by default, with immediate save and guidance for automatic versus manual task-table sizing.
- Added a readiness-first automation dashboard with `Due now`, `Blocked`, `Scheduled later`, and `Complete` sections, written state labels, local next-eligible times, owner/cadence context, direct blocker recovery where configuration can help, and collapsed advanced diagnostics/test controls.
- Added persistent configuration-scope context, row-level account-default comparisons, differing-character counts, immediate single-character `Use default`, and named-scope confirmations for propagation, reset, and delete actions.
- Added explicit Before AR and After AR task-order lanes with lane-local movement, explicit phase changes, and inline cadence, ownership, and blocker details.
- Added setup-wizard field-impact previews and separate account-default versus confirmed all-character apply actions. Fishing previews include every changed stock row.
- Added personal registrable-list search and validated import previews with accepted, duplicate, unknown, invalid, added, and removed counts. Import, Clear All, and default-list replacement now mutate only after confirmation.

### Changed

- Corrected the Main Window to keep its identity, readiness, recovery, and primary actions fixed above exactly one scrolling body. The task table no longer owns a nested scrollbar or forces an empty minimum height, so short Favorites views do not reserve a blank table area.
- Replaced the multiline six-column task table with a single-line `â˜… | Task | When | Type | Actions` layout. Compact local timing and owner/cadence codes carry full legends in header tooltips, while each task tooltip retains its complete status, blocker, maturity, schedule, and disabled-action context without increasing row height.
- Automatic task-column sizing now fits the compact columns to their contents, assigns the remaining width to Task, and prevents divider dragging. Disabling it restores the shared, natively persisted manual layout for All Tasks and Favorites.
- Configuration, task-order, wizard, and registrable-editor layouts retain their stretch/scroll tables, wrapped explanations, stable action columns, and explicit empty/error states at their existing minimum sizes.
- Configuration recovery selects the active character, opens the correct tab and section, and scrolls it into view. Runtime-only blockers remain informational.

### Verification

- The focused `UiUxPolicyTests` class passes 18/18 tests, including fresh and legacy automatic-width defaults. The complete Debug x64 suite passes 538/538 tests.
- The Debug x64 solution build succeeds with zero errors and only the existing `PInvoke.User32` NU1601 dependency-resolution warning. Manual checklist verification remains David-operated and pending.

## 2026-08-20 - Favorites and independent Refill Listings pacing

### Added

- Added global saved Favorites for catalog automations. The main window now defaults to `All Tasks`, provides a flat `Favorites` tab using the same task status and run actions, and keeps manual utilities and test controls in `All Tasks` only.
- Added an independent Refill Listings inter-item delay, defaulting to 250 ms and clamped to 0â€“2000 ms. It is used only after a listing withdrawal is verified and before the next listing is selected.

### Changed

- Refill Listings snapshots both pacing settings when a run starts. The existing action delay remains exclusive to ordinary menu and click pacing; navigation, verification polling, retries, timeouts, settlement, and closing are unchanged.
- Expanded the UI/UX guide with implementation status while retaining every P0/P1/P2 recommendation for the complete follow-up pass.

### Verification

- Focused favorites and Refill Listings pacing policy coverage passes 8/8 tests, including null, duplicate, unknown, and retired favorite IDs, independent clamping, ordinary click pacing, successful verification, and failed-verification polling.
- Focused catalog, equipment-timing, favorites, and Refill Listings pacing coverage passes 36/36 tests. The complete Debug x64 suite passes 520/520 tests.
- The Debug x64 solution build succeeds with zero errors and only the existing `PInvoke.User32` NU1601 dependency-resolution warning. Live-client verification was not performed.

## 2026-08-18 - Gearset bootstrap and narrow post-process utilities

### Added

- Added a bounded native bootstrap for missing unlocked class/job gearsets, including exact current-job anchoring, owned-mainhand selection, recommended-equipment timing, exact save verification, and manual controls. Gear Updater also uses optional Stylist IPC with its existing native path as the fallback.
- Added disabled-by-default current-character Allied Society automation through Questionable Companion and one-shot After-AR parking with Home, Limsa, Free Company, Inn, Workshop, and validated custom `/li ...` destinations.

### Changed

- Added one absolute Refill Listings action delay, defaulting to 250 ms and clamped to 0â€“2000 ms, for ordinary listing action pacing.
- Existing Current Job Equipment, Seasonal Gear, Ocean Fishing, and W40 behavior remain on their prior paths.

### Verification

- Static acceptance review covered default compatibility, native timing, job/content and save drift, unsafe/full/missing-equipment failures, Stylist fallback, and invalid parking/Allied selections.
- The single permitted Debug x64 plugin build reached compilation and reported one `uint`-to-`int` argument error in unlocked-job discovery. The direct cast was applied afterward, but the final source was not rebuilt under the one-build limit. Automated tests and live-client actions were not run; runtime behavior remains untested.

## 2026-08-08 - Character-select stall recovery

### Added

- Added the enabled-by-default global `EnableCharacterSelectStallRecovery` setting and its configuration control.
- Whenever `CharaSelect` remains visible, a five-minute timer now makes one automatic attempt to load character-list entry 0. The Main Window also exposes a `Load first character now` control that queues the same framework-thread attempt.
- Both triggers require only a visible `CharaSelect` addon, then invoke `_CharaSelectListMenu` callbacks `29, 0` and `21, 0` before accepting the resulting OK confirmation. They never open, navigate, or back out of character select; a fired callback accepts one new login confirmation only.
- The Main Window reports the setting, timer/stall state, and precise blocked reason.

### Changed

- The automatic timer now uses `CharaSelect` visibility as its sole gate, resets when that addon is hidden, and displays `Automatic recovery in m:ss` while armed or `Automatic attempt used` after its one automatic attempt.

### Verification

- Focused Debug x64 character-select recovery coverage passes 5/5, including visible-`CharaSelect` timer arm, `5:00` and `4:32` countdown formatting, one-shot expiry, hidden-addon reset, and re-arm behavior.
- The complete Debug x64 test project passes 502/502. The Debug x64 plugin build succeeds with 0 errors and the existing `PInvoke.User32` NU1601 dependency-resolution warning (reported during restore and build).
- Live-client verification was not performed.
- The focused legacy-null account-persistence regression passes 1/1. The fresh Debug x64 plugin build succeeds with 0 errors and the existing `PInvoke.User32` NU1601 dependency-resolution warning. Live-client verification remains pending.

### Fixed

- Treat a visible `CharaSelect` addon as the complete character-select gate. The generic ECommons addon-ready predicate and `AgentLobby`/entry checks do not apply to this recovery path, which invokes the callback through the visible-addon path.
- Legacy account JSON with null or missing `CharacterCreatedAtUtc` metadata now loads for the existing timestamp backfill without dropping character configurations.
- Every confirmed login now independently registers the ready `Name@World` character against its selected account, including plugin reload while already logged in. An unknown character remains with its selected readable account even when another account file is unreadable; unreadable-only account sets still fail closed. Before-AR automation waits for that registration but keeps its existing task, AutoRetainer, and DAD gates unchanged.

## 2026-08-01 - Fishing handoff waits for Lifestream

### Fixed
- AutoRetainer post-process fishing startup now waits for Lifestream to become idle, retaining the existing post-process hold and before-AR gate until the existing fishing handoff runs.

## 2026-08-01 - Ocean Fishing 1.5-yalm clearance

### Changed
- Ocean Fishing now uses one shared 1.5-yalm clearance policy for continuous rail candidates, the initial start gate, and recovery-point separation from the prior destination.
- The 32-sample cap, stopped-path and facing gates, paired `/ahstart` then `/ac cast` cadence, bounded recovery, and permanent post-acknowledgement movement lock are unchanged. W40 remains a separate active workflow.

### Verification
- Focused Debug x64 fishing policy tests pass 143/143, including the 1.499-yalm rejection and exact 1.5-yalm acceptance boundary. The full Debug x64 suite passes 497/497 tests.
- The Debug x64 plugin build succeeds with zero errors and the existing `PInvoke.User32` NU1601 dependency-resolution warning. No package, deployment, version bump, commit, push, remote-client access, or live-game verification was performed.

## 2026-07-30 - Automatic Register Registrables inventory mode

### Added
- Added the opt-in per-character `RegisterUnregisteredItemsFromInventory` setting, defaulting to `false` for new and existing JSON. Clone and default-to-character copies preserve the value.
- Automatic mode takes one ordered snapshot of readable, loaded `Inventory1` through `Inventory4`, deduplicates item IDs by first bag/slot occurrence, and ignores the personal list for that run. It selects only still-locked direct mounts, minions, fashion accessories, facewear, orchestrion rolls, emotes/hairstyles, bardings, and Triple Triad cards using the ADS action-ID classification. Faded orchestrion materials and unrelated/indirect items are excluded.

### Changed
- Manual mode continues to use only the configured personal list and retains the empty-list blocker. Automatic mode is eligible with an empty personal list.
- The fixed queue skips already registered items, rechecks native registration immediately before use, then waits the existing seven seconds after every use request. A verified unlock advances even if duplicate copies remain; only a still-present locked item retries, with the existing three-attempt limit and warning. Unreadable bag, slot, item-registration, or verification state fails closed, and exhausted items do not trigger a rescan.

### Verification
- Focused Register Registrables, recovery, and automation-catalog tests pass 64/64, including all eight ADS action IDs, faded/unrelated rejection, inventory ordering/deduplication, locked filtering, unreadable-state failure, automatic/manual source selection, empty-list eligibility, exact seven-second verification, duplicate-copy advancement, and three-attempt exhaustion.
- The complete Debug x64 solution suite passes 497/497 tests. The isolated Debug x64 plugin build succeeds with zero errors and only the existing `PInvoke.User32` NU1601 resolution warning.
- No ADS edits, version/manifest/dependency changes, packaging, release, commit, push, client control, or live-game testing was performed. Live acceptance was out of scope and no deferred work was created.

## 2026-07-29 - Jumbo follow-up and shared configuration safety

### Fixed
- Jumbo Cactpot now restores the state-owned Yes click only for the second/third-ticket follow-up prompt. The guarded first-ticket confirmation, purchase system-message verification, payout recovery, ticket cadence, Mini Cactpot, and Saucy behavior are unchanged.
- Per-account saves no longer write a process's entire stale in-memory snapshot over a shared configuration file. Each local process tracks its loaded baseline, locks the account across processes, reads the newest valid disk copy, and merges only locally changed account, default, and character records. Remote character additions/deletions are retained, deliberate local additions/deletions propagate, and a same-character conflict uses the current saver's value.
- Account JSON remains schema-compatible. Saves validate a same-directory temporary file before atomic replacement and retain one last-known-good `.bak`; a malformed primary loads from that backup. If neither copy is valid, loading and saving fail closed without overwriting either file or treating the unreadable account as absent.

### Verification
- Focused Jumbo, persistence, and account-selection tests pass 65/65, including two stale clients editing different characters, remote additions/deletions, intentional bulk changes, same-character conflict precedence, backup recovery, malformed-file refusal, and valid atomic replacement.
- The full Debug suite passes 470/470 tests. The Debug x64 plugin build succeeds with zero errors and only the existing `PInvoke.User32` NU1601 resolution warning.
- No version, manifest, dependency, package, commit, push, deployment, client configuration, or live-game action was performed. Live Jumbo and multi-client verification were not run.

## 2026-07-26 - Retainer Equipping live defect repair

### Fixed
- Saved gearsets are now protected by counted exact fingerprints containing the encoded HQ item ID, glamour, both stains, and all five materia IDs and grades. `Ignore Gearset` preserves the exact saved copies while allowing physical surplus duplicates; `Ignore Armory`, `All Gear`, player-equipped exclusion, compatibility, and allocation behavior are unchanged.
- Retainer equipment-window ownership is now bound to the selected retainer. Multiple upgrades remain in one retainer's window, cross-retainer transitions return to the list exactly once, and the final equipment window closes before completion.
- Native equipment moves now wait 500 ms after opening the window and between items or attempts, dispatch one request at a time, and poll the exact destination for up to two seconds. Nonzero native returns can still settle successfully; unresolved moves retry only while the exact source remains, stop after three total attempts, emit one terminal warning, and continue without discarding earlier upgrades.
- Completion, failure, cancellation, and Full Stop clear all pending move state while retaining the existing bell ownership and collect-only restoration paths.

### Verification
- Focused Retainer Equipment tests pass 29/29, including HQ/customization fingerprints, counted saved copies and surplus duplicates, same-class combat retainers, gatherers, same-retainer batching, final-window closure, asynchronous nonzero-return success, exact verification, bounded retry, source loss, and single terminal-signal behavior.
- The full Debug suite passes 460/460 tests. The Debug x64 plugin build succeeds with zero errors and only the existing `PInvoke.User32` NU1601 resolution warning.
- Packaging, publication, version changes, plugin commits/pushes, DLL copying, client mutation, remote testing, and live-game actions were not performed. Live acceptance remains operator-run.

## 2026-07-25 - Ocean Fishing AutoHook start with direct-cast fallback

### Fixed
- Every eligible Ocean Fishing start now sends exact `/ahstart` first and exact `/ac cast` second in the same tick. AutoHook remains the primary start path, while the direct cast provides a fallback when preset-driven startup does not cast.
- The paired dispatch remains one attempt: one three-second retry cadence, one attempt-counter increment, and one slot toward the existing five-attempt placement-recovery threshold.
- Both commands remain behind the existing continuous-rail placement and Ocean Fishing duty gates. Fishing/Gathering acknowledgement, recovery, voyage-long movement lock, AutoHook preset ownership, and AutoHook lifecycle cleanup are unchanged.

### Verification
- Focused fishing policy tests pass 143/143, including exact command strings, paired-attempt cadence, placement gates, recovery, movement locking, and outside-duty suppression.
- The full Debug suite passes 452/452 tests. The Debug x64 plugin build succeeds with zero errors and only the existing `PInvoke.User32` NU1601 resolution warning.
- Packaging, publication, client configuration, and live-game actions were not performed. Multi-client live acceptance remains pending separately authorized observation.

## 2026-07-25 - Ocean Fishing live Fisher-cap revalidation

### Fixed
- Ocean Fishing now re-reads the current character's Fisher level from native `PlayerState` immediately before acquiring a new run or starting `FishingService`. An unavailable live level fails closed and retries without switching job, traveling, queueing, or casting.
- A non-override candidate whose live Fisher level is at or above the configured cap is rejected even when the cached XADB roster reports a lower level. Current-character rejection stops before run ownership; post-relog rejection releases the owned lifecycle and advances to the next cached candidate.
- The explicit `AlwaysFish` selection now carries override provenance through the startup coordinator, preserving its deliberate cap bypass while normal candidates remain protected.
- Added current-character, unavailable-native-state, post-relog mismatch, next-candidate recovery, and explicit-override regression coverage. W40 voyage positioning and casting behavior is unchanged.

## 2026-07-24 - Ocean Fishing continuous rail placement and settled cast gate

### Fixed
- Replaced the six static boat destinations with continuous Henchman-proven rail sampling. Starboard preserves the middle obstruction gap, port uses its full proven span, and both retain their outward character rotations.
- Each sampling pass rejects candidates within three yalms of another player and rejects recovery points within three yalms of the previous destination. An exhausted 32-candidate pass stops navigation, blocks `/ahstart`, and retries after one second.
- Reaching a destination within 0.5 yalms now stops vnavmesh and applies character rotation. The camera is not rotated. The first `/ahstart` and Fishing/Gathering acknowledgement remain locked until live player clearance is at least three yalms, `vnavmesh.Path.IsRunning` has remained false for one continuous second, and facing readback is within 0.05 radians.
- Clearance loss before acknowledgement resamples as soon as movement is safe; facing verification resamples after ten active seconds, and unavailable path-status IPC fails closed after ten active seconds. Existing navigation stall, timeout, false-`CanFish`, and five-attempt recovery now resample instead of cycling fixed indices.
- The first valid acknowledgement still permanently locks voyage movement. Later route changes and fishing interruptions retry in place without respreading. The existing Ocean Fishing context gate still rejects stale Fishing/Gathering state and `/ahstart` outside the duty.

### Verification
- Focused fishing policy tests pass 143/143, including rail ranges, obstruction-gap preservation, exact three-yalm boundary, bounded sampling, stopped-path resets, facing/path-status timeouts, pre-readiness recovery pauses, movement locking, and outside-duty stale-condition coverage.
- The full Debug suite passes 448/448 tests. The Debug x64 plugin build succeeds with zero errors and only the existing `PInvoke.User32` NU1601 resolution warning.
- Packaging, publication, client configuration, and live-game actions were not performed. Multi-client live acceptance remains pending separately authorized observation.

## 2026-07-24 - Retainer Equipping main-window ordered run

### Added
- Added Retainer Equipping immediately after Refill Listings in the Main Window. The row shows the current character's scheduling checkbox state, cached AutoRetainer readiness or live execution status, a functional Run button, and yellow `WIP` maturity.
- Added exact manual readiness for login, DAD ownership, another engine run, an existing retainer bell session, positive targets, readable/idle AutoRetainer state, selected retainers, target completion, unknown stats, and targeted active ventures. UI probes are cached for five seconds, while a click always forces a fresh AutoRetainer read.
- Added a scoped engine manual-run path that queues only Retainer Equipping, bypasses only its own scheduling checkbox, and suppresses Misc Commands and every unrelated configured task.

### Changed
- Retainer Equipping is now catalogued as `Wip` without changing its `EngineTask` ownership, Before-AR default placement, or normal automatic dispatch.
- Manual execution uses the existing engine state, bell ownership, watchdog, handoff settling, cancellation, cleanup, and collect-only restoration paths. Targeted retainers with active ventures wait for AutoRetainer collection, while already-complete retainers do not block.

### Verification
- Added deterministic WIP-dispatch, isolated-run-scope, scheduling-bypass, hook suppression, readiness matrix, forced-cache-refresh, blocker-reason, and collect-only restoration regressions.
- The complete Debug suite passes 433 tests, and the Debug x64 solution build succeeds with zero errors and only the existing `PInvoke.User32` NU1601 resolution warning.
- Packaging, release, X: copy, and live-game validation were not performed.

## 2026-07-23 - Native gearset confirmation and Ocean Fishing distance gate

### Fixed
- Gear Updater target changes and restoration, Highest Combat Job, and Seasonal Gear restoration now own a three-second confirmation window after every applicable native `EquipGearset` request. Each window polls every framework tick and accepts the first ready `SelectYesno` without reading prompt text or metadata; duplicate gearset requests are suppressed while the window is open, including when the native call returns an error.
- Closing or expiring the gearset confirmation window restarts normal active-gearset verification without consuming another attempt. Current Job Equipment remains unchanged and does not use the confirmation path.
- Ocean Fishing now keeps the initial `/ahstart` and Fishing/Gathering acknowledgement gated until the character is currently within the existing 0.5-yalm fixed-rail threshold. Premature conditions do not increment attempts, stop navigation, mark fishing started, or lock movement.
- Reaching the fixed rail permits the first `/ahstart` immediately without waiting for the 500 ms facing-settlement timer. Arrival no longer stops vnavmesh; the first valid at-destination Fishing/Gathering acknowledgement owns the stop and permanent movement lock.
- Existing fixed-rail stall/timeout cycling, false-`CanFish` fallback, five post-arrival attempts, six coordinates, route behavior, and in-place post-start retries are unchanged.

### Verification
- Added prompt-ready/not-ready, three-second boundary, native-error, duplicate-suppression, final-attempt, and post-window activation-verification regressions across native equipment paths.
- Added pre-arrival cast/acknowledgement suppression, counter/lock preservation, immediate-at-threshold start, acknowledgement ownership, stall recovery, and post-start in-place retry regressions. The full Debug suite contains 420 tests; native/live-game acceptance remains pending.

## 2026-07-23 - P27 six-item recovery and retainer equipping

### Added
- Added a persistent Seal Sweetener II ledger keyed by Free Company ID. Already-active actions leave stock unchanged, confirmed activations decrement exactly once, and successful zero reads remain distinguishable from unreadable FC UI state.
- Added a global ordered fishing-stock catalog and per-character enabled/target settings. Defaults are Versatile Lure at enabled/22 and Plump Worm, Ragworm, and Krill at disabled/99. Catalog removal purges all stored values; later default propagation is explicit.
- Added typed ADS shop purchase start/status/cancel operations. Ocean Fishing requests exact missing quantities in catalog order, verifies final inventory, reports optional partial failures, and blocks only when no Versatile Lure remains.
- Added bounded Fisher fallback after a missing saved gearset or ten unverified equip requests. It reuses an inventory/Armoury Weathered Fishing Rod or asks ADS for exactly one, then uses a verified native inventory move without saving a gearset.
- Added Retainer Equipping for AutoRetainer-enabled retainers. Combat uses AutoRetainer-compatible weighted average item level; gathering uses Perception only. Allocation respects job, level, physical-item uniqueness, both ring slots, source mode, saved-gearset membership, and the independent non-unique filter.
- Added replayable Default & Sync, FC Buff, Fishing, and Retainer Equipping wizards. Apply changes only the current account Default Config and never launches automation.

### Changed
- Retainer source selection now has inventory-only, inventory/Armoury excluding saved gearsets (default), and unrestricted inventory/Armoury modes. Player-equipped containers remain excluded.
- AutoRetainer collect-only state is checkpointed while returned ventures are collected, survives character rotation, and is restored to the original value on success, failure, cancellation, logout, disposal, and recovery.
- The automation catalog now owns 24 per-character enable flags and 18 ordered engine tasks. Retainer Equipping defaults to Before AR. Existing Gysahl Greens and Grade 8 Dark Matter vendor-stock paths are unchanged.
- I68 is resolved as the duplicate umbrella for the I75 FC-action recovery and I72 Fisher fallback work; no additional runtime feature was introduced.

### Verification
- Added deterministic migration, catalog add/remove/sync, FC reconciliation/persistence, ADS partial-outcome, Fisher boundary, source-mode, Perception-only, weighted item-level, strongest-allocation, ring-uniqueness, non-unique, target-zero, retry-signature, and collect-only restoration coverage.
- The full Debug suite passes 412 tests, and the fresh Debug x64 solution build succeeds with zero errors and only the existing `PInvoke.User32` NU1601 resolution warning.
- Native/live-game behavior was not executed and remains pending separately authorized validation.

## 2026-07-22 - Ocean Fishing fixed-rail recovery

### Fixed
- Replaced the live-unverified dynamic vnavmesh edge scan and player/entry-position fallbacks with six proven Henchman/FUTA rail coordinates. Destinations keep canonical starboard/port rotations and are ranked by two-yalm player clearance, greatest clearance, and stable canonical order; player positions are never used as navigation targets.
- Tightened rail arrival to 0.5 yalms. Navigation now stops and applies the canonical facing at arrival, waits 500 ms before a bounded facing reapply, and retries facing at most once per second until Fishing/Gathering acknowledges startup.
- Before the first acknowledgement, Ocean Fishing now advances through and wraps the fixed list on ten seconds without 0.25 yalms of progress, 30 active navigation seconds, ten available/non-busy seconds with `CanFish` false after arrival, or five unacknowledged post-arrival `/ahstart` attempts. Recovery clocks pause during route transitions, unavailable-player states, combat, casting, and occupied states.
- `/ahstart` remains immediately eligible and retries every three seconds while moving or settled. Versatile Lure remains once per seven-minute session, advances preserve session state, and the first Fishing/Gathering acknowledgement still stops navigation immediately and permanently locks movement for the voyage.

### Verification
- Added deterministic fixed-coordinate, canonical-rotation, crowd-ranking, wraparound, stall, timeout, false-`CanFish`, unacknowledged-start, paused-timer, facing-settlement, and permanent-lock regressions while retaining startup, route, lifecycle, result, cleanup, and lure coverage.
- The full Debug suite passes 389/389 tests, and the Debug x64 solution build succeeds with zero errors and only the existing `PInvoke.User32` NU1601 resolution warning.

## 2026-07-22 - P27 holistic automation dispatch and native equipment hardening

### Added
- Added one internal automation catalog that assigns each of the 23 per-character `Enable*` feature flags exactly one stable ID, cadence, maturity, default phase, and runtime owner. The 17 ordered engine tasks now have matching catalog, order, and runtime registrations that fail closed with a visible diagnostic if they diverge.
- Added ordered dispatch for Gear Updater, Highest Combat Job, Current Job Equipment, Seasonal Gear, and Minion Roulette. Existing custom order and phase choices are preserved while the five IDs are inserted deterministically after Register Registrables.
- Added structured runnable, disabled, not-due, blocked, and unsupported planning results. Every AutoRetainer/manual run logs the complete plan, and the configuration/main windows expose registry failures and concrete prerequisite blockers.

### Changed
- Misc Commands is a run-start hook and can now be the only After-AR/manual work. It still does not arm or run a Before-AR pass by itself. Ocean Fishing remains owned by its preemptive startup coordinator and is no longer presented as reorderable.
- Gear Updater, Highest Combat Job, Current Job Equipment, and Seasonal Gear now use bounded native gearset/recommended-equipment/inventory adapters. SimpleTweaks commands, hardcoded job targets, blocking delays, delayed continuations, and temporary framework subscriptions were removed.
- Gear Updater enumerates all 100 saved-gearset slots, chooses one stable gearset per unlocked class/job, verifies native updates, and restores the starting gearset. Highest Combat Job uses saved combat gearsets plus Lumina metadata and actual levels. Current Job Equipment saves only the captured active gearset. Seasonal Gear derives slots from Lumina data, verifies moves and saves, and restores the starting gearset on failure without applying recommended gear.
- Minion Roulette sends exactly one command per run and updates its informational attempt counter without using it as an eligibility gate.

### Fixed
- Empty Register Registrables lists and Rival Wings completion/disable recommendations no longer change character enablement. They now produce visible blocked/skip reasons while preserving their checkboxes.
- Cancellation, character changes, watchdog failures, and Full Stop now clean up the new equipment state machines and recommended-equipment operation state.

### Verification
- Expanded the baseline from 366 to 388 passing tests with catalog/reflection, registry contract, migration, dispatch, misc-hook, native equipment policy, timeout, partial-failure, restoration, and cleanup coverage. The isolated Debug x64 plugin build succeeds with only the existing `PInvoke.User32` NU1601 resolution warning.

## 2026-07-21 - I61/I57 narrow Ocean Fishing positioning and startup fix

### Fixed
- Ocean Fishing now derives its boat position from one read-only voyage-entry vnavmesh scan across 32 directions at 0.5-yalm intervals up to 20 yalms. It chooses the nearest edge at least 2 yalms from other players, otherwise the greatest-clearance edge, faces outward, and uses the specified player/entry fallback when no mesh edge is available.
- Each seven-minute session sets Versatile Lure once and retries `/ahstart` every three seconds, including during initial movement and after fishing is interrupted. Fishing/Gathering acknowledgement stops navigation immediately and permanently locks voyage movement.
- Before fishing has ever started, the first destination may use one scanned alternative only after remaining unfishable for 10 seconds. Route changes, crowd changes, and later failures never trigger repositioning. AutoHook preset ownership and unrelated fishing lifecycle behavior are unchanged.

## 2026-07-20 - I62 recommended-equipment fix

### Fixed
- Equipment updaters now use the native recommended-equipment module instead of `/equiprecommended`, while retaining their existing `/updategearset` save flow and timing.

## 2026-07-17 - P1195 DAD terminal-reservation reacquisition

### Fixed
- A valid DAD v2 `Reserve` received after the retained reservation reached terminal `Released` now initializes a
  fresh `Pending`/`Granting` attempt with new timestamps and a new 15-second lease, including for the same operation
  token. The new attempt can grant normally after VERMAXION and AutoRetainer reach the existing safe boundary.
- Active same-token renewals remain idempotent and extend only the current lease. Conflicting active tokens remain
  rejected without replacing the owner.
- Added regressions for explicit release and lease-expiry reacquisition while retaining renewal and conflict coverage.
  No IPC channel, JSON shape, DTO, enum, configuration, manifest, or plugin version changed.

## v0.0.0.1 - Initial Scaffold

### Added
- Full plugin scaffold with account-based per-character configuration (FrenRider pattern)
- ConfigManager with account/character system, JSON persistence, KrangleService
- ARPostProcessService: Two-phase IPC integration with AutoRetainer
  - Subscribe to OnCharacterAdditionalTask â†’ RequestCharacterPostprocess
  - Subscribe to OnCharacterReadyForPostprocess â†’ run tasks â†’ FinishCharacterPostprocessRequest
- VermaxionEngine: State machine orchestrator that sequences all tasks
- ResetDetectionService: Weekly (Tue 8:00 UTC), daily (15:00 UTC), Saturday detection
- HenchmanService: Stop/start via /henchman off and /henchman on slash commands
- FCBuffService: Seal Sweetener check and purchase flow (stub - needs addon research)
- VerminionService: Lord of Verminion 5x queue (stub - needs ContentsFinder research)
- CactpotService: Mini Cactpot via Saucy, Jumbo Cactpot (stub - needs addon research)
- ChocoboRaceService: legacy external-command racing stub (later replaced by VERMAXION's native observable queue loop)
- MainWindow: Status overview with task table, reset timers, manual run button
- ConfigWindow: Left panel character list, right panel settings (FrenRider-style layout)
- DTR bar entry with status display
- Commands: /vermaxion (main UI), /vmx [on|off|run|cancel|config]

### Known Stubs (Need In-Game Research)
- VerminionService: Duty queue interaction not implemented
- FCBuffService: FreeCompanyAction addon interaction not implemented
- CactpotService: Saucy command syntax needs verification
- CactpotService: Jumbo Cactpot addon interaction not implemented
- ChocoboRaceService: legacy external-command path was still a stub (later superseded by native VERMAXION racing)
