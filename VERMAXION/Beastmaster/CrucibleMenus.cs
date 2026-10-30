// Adapted from erdelf/AutoDuty, commit 7e0ede4be688980d217faf6889ffdac53e18e30f. Private local port.
using ECommons.GameHelpers;

using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace VERMAXION.Beastmaster
{
    using Dalamud.Game.ClientState.Objects.Enums;
    using Dalamud.Game.ClientState.Objects.Types;
    using ECommons.GameFunctions;
    using ECommons.Throttlers;
    using ECommons.UIHelpers.AddonMasterImplementations;
    using VERMAXION.Beastmaster.Readers;
    
    
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Numerics;
    using Screens = CrucibleUi.Screens;

    internal sealed unsafe class CrucibleMenus
    {
        private const int   FightPicks = CrucibleTeam.FightSize;
        private const int   RestPicks  = 2;
        private const float RestBelow  = 0.6f;
        private const int   ItemCap    = 10;
        private const int   GearCap    = 10;
        private const float FightLow   = 0.4f;
        private const float BoardLow   = 0.6f;

        private static readonly TimeSpan Retry         = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan PickInterval  = TimeSpan.FromMilliseconds(400);
        private static readonly TimeSpan CommenceRetry = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan ShopStep      = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan FeedRetry     = TimeSpan.FromMilliseconds(1500);
        private static readonly TimeSpan FeedTimeout   = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan ItemGap       = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan ItemMenuWait  = TimeSpan.FromMilliseconds(1500);

        private DateTime confirmFrom  = DateTime.MinValue;
        private string?  confirmationWindow;
        private nint     confirmationAddress;
        private DateTime nextConfirmation;

        private DateTime fightNext;

        private List<int>? restPicks;
        private int        restStep;

        private readonly HashSet<uint>                         shopTried = [];
        private          DateTime                              shopNext;
        private          DateTime                              feedFrom = DateTime.MinValue;
        private          List<ReaderXBMPetParty.MonsterEntry>? feedOrder;
        private          int                                   feedTry;
        private          bool                                  fedThisVisit;

        private DateTime itemNext;
        private DateTime itemLastUse = DateTime.MinValue;
        private (uint Id, int Count, string Name)? requestedItem;
        private DateTime itemMenuFrom = DateTime.MinValue;

        private readonly Dictionary<string, BeastmasterReadRecovery> readRecovery = [];
        private readonly Dictionary<string, BeastmasterMenuSubmission> submittedWindows = [];
        private nint rejectedLootAddress;
        private nint acknowledgedLootNotice;
        private DateTime nextLootNotice;
        private nint resultAddress;
        private bool resultsRecorded;
        private bool resultExitPending;
        private DateTime nextResultExit;

        public string Status { get; private set; } = "";
        internal bool LootRejectionPending => this.rejectedLootAddress != 0;

        private static BeastmasterSettings Config => BeastmasterRuntime.Settings;
        private bool TryRequiredClearGear(out uint[] requirements)
        {
            requirements = [];
            if (!BeastmasterSelection.RequiresClearTeam(Config)) return true;
            if (!BeastmasterCombat.TryReadSetup(Config.Board, 0, out var setup, out var error))
            {
                this.Status = $"Waiting for native clear gear requirements: {error}";
                return false;
            }
            requirements = setup.RequiredGear;
            return true;
        }

        public void Reset()
        {
            this.confirmFrom  = DateTime.MinValue;
            this.confirmationWindow = null;
            this.confirmationAddress = 0;
            this.nextConfirmation = default;
            this.restPicks    = null;
            this.feedFrom     = DateTime.MinValue;
            this.itemMenuFrom = DateTime.MinValue;
            this.requestedItem = null;
            this.ResetShopVisit();
            this.readRecovery.Clear();
            this.submittedWindows.Clear();
            this.rejectedLootAddress = 0;
            this.acknowledgedLootNotice = 0;
            this.nextLootNotice = default;
            this.resultAddress = 0;
            this.resultsRecorded = false;
            this.resultExitPending = false;
            this.nextResultExit = default;
            this.Status = "";
        }

        internal void ResumeAfterPause(TimeSpan elapsed)
        {
            // A paused window must not consume a pending feed or confirmation deadline.
            Shift(ref this.confirmFrom); Shift(ref this.fightNext); Shift(ref this.shopNext);
            Shift(ref this.feedFrom); Shift(ref this.itemNext); Shift(ref this.itemLastUse);
            Shift(ref this.itemMenuFrom); Shift(ref this.nextResultExit);
            Shift(ref this.nextConfirmation);
            Shift(ref this.nextLootNotice);
            foreach (var window in this.submittedWindows.Keys.ToArray())
                this.submittedWindows[window] = this.submittedWindows[window].ResumeAfterPause(elapsed);
            foreach (var recovery in this.readRecovery.Values) recovery.ResumeAfterPause(elapsed);
            void Shift(ref DateTime value) { if (value != DateTime.MinValue) value += elapsed; }
        }

        public void Update()
        {
            DateTime now = DateTime.UtcNow;

            if (!EzThrottler.Throttle("CrucibleMenus", 250))
                return;

            this.ObserveWindowChanges();

            if ((this.LootRejectionPending || CrucibleUi.IsVisible(CrucibleUi.LootWindow)) &&
                this.HandleLootRejection(now, CrucibleUi.LootRejectionText)) return;

            // Only a result exit submitted by this run owns this confirmation.
            if (this.resultExitPending && CrucibleUi.TryReady(CrucibleUi.YesNo, out AtkUnitBase* leaveConfirm))
            {
                var yes = ((FFXIVClientStructs.FFXIV.Client.UI.AddonSelectYesno*)leaveConfirm)->YesButton;
                if (now >= this.nextResultExit && yes != null && yes->IsEnabled)
                {
                    this.nextResultExit = now + BeastmasterReadRecovery.Delay;
                    new AddonMaster.SelectYesno(leaveConfirm).Yes();
                }
                return;
            }

            if (this.ConfirmOwnedMenu(now)) return;

            // The live loot addon can expose fewer AtkValues than the copied
            // reader expects. Use its own Take All control instead of those offsets.
            if (CrucibleUi.TryReady(CrucibleUi.LootWindow, out AtkUnitBase* loot))
            {
                this.Status = "Loot";
                if (this.WasSubmitted(CrucibleUi.LootWindow, loot)) return;
                if (!Config.Loot)
                {
                    Screens.Booty.Close(loot);
                    this.Submitted(CrucibleUi.LootWindow, loot);
                    this.confirmFrom = now;
                }
                else if (Screens.Booty.TryReadTakeAll(loot, out var enabled))
                {
                    if (!enabled)
                    {
                        Screens.Booty.Close(loot);
                        this.Submitted(CrucibleUi.LootWindow, loot);
                        this.confirmFrom = now;
                    }
                    else if (Screens.Booty.TakeAll(loot))
                    {
                        this.Submitted(CrucibleUi.LootWindow, loot);
                        this.confirmFrom = now;
                    }
                }
                else this.RecoverUnreadable(new(CrucibleUi.LootWindow, "Take All control is unavailable"));
                return;
            }

            // Completed results must not be starved by a lingering team/shop window.
            if (CrucibleUi.TryReady(CrucibleUi.ResultWindow, out AtkUnitBase* completedResult))
            {
                this.UpdateResults(completedResult, now);
                return;
            }

            try { this.UpdateItems(now); }
            catch (BeastmasterReadUnavailableException ex) { this.RecoverUnreadable(ex); }

            try { if (this.StartFight(now)) return; }
            catch (BeastmasterReadUnavailableException ex)
            {
                this.RecoverUnreadable(ex);
                if (CrucibleUi.IsOpen(CrucibleUi.BoardLayout)) return;
            }

            try { this.UpdateShop(now); }
            catch (BeastmasterReadUnavailableException ex) { this.RecoverUnreadable(ex); }



            if (!this.FeedPending(now) && !CrucibleUi.IsOpen(CrucibleUi.ShopWindow) && !CrucibleUi.IsOpen(CrucibleUi.BoardLayout) &&
                CrucibleUi.TryReady(CrucibleUi.TeamWindow, out AtkUnitBase* party))
            {
                try { this.Rest(party, now); }
                catch (BeastmasterReadUnavailableException ex) { this.RecoverUnreadable(ex); }
                return;
            }

            this.restPicks = null;

            if (CrucibleUi.TryReady(CrucibleUi.TreasureWindow, out AtkUnitBase* treasure))
            {
                try { this.PickTreasure(treasure, now); }
                catch (BeastmasterReadUnavailableException ex) { this.RecoverUnreadable(ex); }
                return;
            }

        }

        internal bool HandleLootRejection(DateTime now, string expected)
        {
            if (!BeastmasterService.IsCrucibleTerritory(Svc.ClientState.TerritoryType))
            {
                this.rejectedLootAddress = 0;
                this.acknowledgedLootNotice = 0;
                this.nextLootNotice = default;
                return false;
            }

            var loot = (AtkUnitBase*)Svc.GameGui.GetAddonByName(CrucibleUi.LootWindow).Address;
            if (loot != null && !loot->IsVisible) loot = null;
            if (loot == null && !this.LootRejectionPending) return false;
            if (loot != null && this.rejectedLootAddress != (nint)loot)
            {
                this.rejectedLootAddress = 0;
                this.acknowledgedLootNotice = 0;
                this.nextLootNotice = default;
            }

            var notice = CrucibleUi.LootRejectionNotice(expected, out var anyVisibleOk);
            if (notice != null)
            {
                if (!this.LootRejectionPending && loot != null)
                {
                    this.rejectedLootAddress = (nint)loot;
                    // The rejection settles the failed Take All attempt. Its old
                    // submission delay must not defer closing this loot visit.
                    this.submittedWindows.Remove(CrucibleUi.LootWindow);
                    if (this.confirmationWindow == CrucibleUi.LootWindow)
                    {
                        this.confirmationWindow = null;
                        this.confirmationAddress = 0;
                    }
                    Svc.Log.Warning("[Crewcible] Take All was rejected; clearing its notices and closing this loot window without further takes.");
                }
                this.Status = "Loot";
                if (this.acknowledgedLootNotice == (nint)notice && now < this.nextLootNotice) return true;
                var button = ((FFXIVClientStructs.FFXIV.Client.UI.AddonSelectOk*)notice)->OkButton;
                if (button == null || !button->IsEnabled) return true;
                this.acknowledgedLootNotice = (nint)notice;
                this.nextLootNotice = now + BeastmasterReadRecovery.Delay;
                new AddonMaster.SelectOk(notice).Ok();
                return true;
            }

            // Do not retry the parent beneath an unrelated or unsettled modal.
            if (anyVisibleOk) return true;
            if (!this.LootRejectionPending) return false;
            if (this.ConfirmOwnedMenu(now)) return true;
            if (loot == null)
            {
                if (now < this.nextLootNotice) return true;
                // Closing can hide the parent before its abandonment question
                // arrives. Use the existing submission window to let it settle.
                if (this.confirmationWindow == CrucibleUi.LootWindow &&
                    this.confirmationAddress == this.rejectedLootAddress &&
                    now - this.confirmFrom < BeastmasterReadRecovery.Delay) return true;
                this.rejectedLootAddress = 0;
                this.acknowledgedLootNotice = 0;
                this.nextLootNotice = default;
                return false;
            }
            this.acknowledgedLootNotice = 0;
            this.nextLootNotice = default;
            if (!loot->IsReady || CrucibleUi.IsVisible(CrucibleUi.YesNo) ||
                this.WasSubmitted(CrucibleUi.LootWindow, loot)) return true;
            Screens.Booty.Close(loot);
            this.Submitted(CrucibleUi.LootWindow, loot);
            return true;
        }

        private void ObserveWindowChanges()
        {
            foreach (var window in this.readRecovery.Keys.ToArray())
                if (!CrucibleUi.IsVisible(window)) this.readRecovery.Remove(window);

            foreach (var (window, submission) in this.submittedWindows.ToArray())
            {
                var current = CrucibleUi.Ready(window);
                if (!CrucibleUi.IsVisible(window) || current != null && (nint)current != submission.Address)
                    this.submittedWindows.Remove(window);
            }

            var result = CrucibleUi.Ready(CrucibleUi.ResultWindow);
            if (!CrucibleUi.IsVisible(CrucibleUi.ResultWindow) || result != null && (nint)result != this.resultAddress)
            {
                this.resultAddress = (nint)result;
                if (!this.resultExitPending) this.resultsRecorded = false;
            }
        }

        private bool WasSubmitted(string window, AtkUnitBase* addon) =>
            this.submittedWindows.TryGetValue(window, out var submission) && submission.IsPending((nint)addon, DateTime.UtcNow);

        private void Submitted(string window, AtkUnitBase* addon)
        {
            var now = DateTime.UtcNow;
            this.submittedWindows[window] = new((nint)addon, now);
            this.ExpectConfirmation(window, addon, now);
        }

        private void ExpectConfirmation(string window, AtkUnitBase* addon, DateTime now)
        {
            this.confirmationWindow = window;
            this.confirmationAddress = (nint)addon;
            this.confirmFrom = now;
            this.nextConfirmation = default;
        }

        private bool ConfirmOwnedMenu(DateTime now)
        {
            if (this.confirmationWindow == null) return false;
            var source = CrucibleUi.Ready(this.confirmationWindow);
            var rejectedLootClose = this.LootRejectionPending && this.confirmationWindow == CrucibleUi.LootWindow &&
                this.confirmationAddress == this.rejectedLootAddress;
            if (!rejectedLootClose && (!CrucibleUi.IsVisible(this.confirmationWindow) ||
                source != null && (nint)source != this.confirmationAddress))
            {
                this.confirmationWindow = null;
                this.confirmationAddress = 0;
                return false;
            }
            if ((source == null && !rejectedLootClose) || !CrucibleUi.TryReady(CrucibleUi.YesNo, out AtkUnitBase* confirm)) return false;
            var yes = ((FFXIVClientStructs.FFXIV.Client.UI.AddonSelectYesno*)confirm)->YesButton;
            if (now >= this.nextConfirmation && yes != null && yes->IsEnabled)
            {
                new AddonMaster.SelectYesno(confirm).Yes();
                this.nextConfirmation = now + BeastmasterReadRecovery.Delay;
                this.submittedWindows[this.confirmationWindow] = new(this.confirmationAddress, now);
                if (rejectedLootClose) this.confirmFrom = now;
                if (this.FeedPending(now))
                {
                    this.feedFrom = DateTime.MinValue;
                    this.shopNext = now + ShopStep;
                }
            }
            // An owned modal is handled before rereading or retrying its parent.
            return true;
        }

        private static bool TryButton(string window, AtkUnitBase* addon, uint nodeId)
        {
            if (addon == null || CrucibleUi.Ready(window) != addon) return false;
            var node = addon->UldManager.SearchNodeById(nodeId);
            if (node == null || !node->IsVisible() || (ushort)node->Type < 1000) return false;
            var component = node->GetAsAtkComponentNode()->Component;
            return component != null && component->GetComponentType() == ComponentType.Button &&
                ((AtkComponentButton*)component)->IsEnabled &&
                Services.GameHelpers.TryClickNativeButton(window, label: null, observedNodeId: nodeId);
        }

        // Each screen owns its wait, so unread optional HUD data cannot starve results or a shop.
        internal bool RecoverUnreadable(BeastmasterReadUnavailableException exception)
        {
            var window = exception.Window;
            if (window == CrucibleUi.LootWindow && this.LootRejectionPending) return false;
            var addon = CrucibleUi.Ready(window);
            if (addon == null || this.WasSubmitted(window, addon)) return false;
            if (!this.readRecovery.TryGetValue(window, out var recovery))
                this.readRecovery[window] = recovery = new();
            var now = DateTime.UtcNow;
            this.Status = $"Waiting for {window} data.";
            if (!recovery.Ready(window, (nint)addon, now)) return false;
            this.Status = $"Rechecking {window}; waiting for a valid native control.";

            // Resolve the native window again after the delay; never act on a stale reader.
            var observedAddress = (nint)addon;
            addon = CrucibleUi.Ready(window);
            if (addon == null) return false;
            if ((nint)addon != observedAddress)
            {
                recovery.Ready(window, (nint)addon, now);
                return false;
            }
            if (this.WasSubmitted(window, addon) ||
                CrucibleUi.IsOpen(CrucibleUi.YesNo) || CrucibleUi.IsOpen(CrucibleUi.ContextMenu) ||
                now - this.confirmFrom < BeastmasterReadRecovery.Delay) return false;

            bool dispatched = false;
            switch (window)
            {
                case CrucibleUi.ResultWindow:
                    return this.UpdateResults(addon, now, acceptUnconfirmed: true);
                case CrucibleUi.LootWindow:
                    if (Screens.Booty.TryReadTakeAll(addon, out var enabled))
                    {
                        if (Config.Loot && enabled)
                        {
                            if (Screens.Booty.TakeAll(addon))
                            {
                                this.Submitted(window, addon);
                                this.confirmFrom = now;
                                return true;
                            }
                        }
                        else { Screens.Booty.Close(addon); dispatched = true; }
                    }
                    break;
                case CrucibleUi.TreasureWindow:
                    // The established Close path never selects or purchases an unknown reward.
                    Screens.Treasure.Close(addon);
                    dispatched = true;
                    break;
                case CrucibleUi.ShopWindow:
                    if (!CrucibleUi.IsOpen(CrucibleUi.TeamWindow) && !this.FeedPending(now))
                        dispatched = TryButton(window, addon, 40);
                    break;
                case CrucibleUi.TeamWindow:
                    if (!CrucibleUi.IsOpen(CrucibleUi.BoardLayout) &&
                        (!CrucibleUi.IsOpen(CrucibleUi.ShopWindow) || this.FeedPending(now)))
                    {
                        dispatched = TryButton(window, addon, 21);
                        if (dispatched) this.feedFrom = DateTime.MinValue;
                    }
                    break;
                // Unknown inventory or party data never authorizes item use, picks, or combat.
            }

            if (dispatched)
            {
                this.Submitted(window, addon);
                this.confirmFrom = now;
            }
            return dispatched;
        }

        private bool UpdateResults(AtkUnitBase* result, DateTime now, bool acceptUnconfirmed = false)
        {
            if (this.resultAddress != (nint)result)
            {
                this.resultAddress = (nint)result;
                if (!this.resultExitPending) this.resultsRecorded = false;
            }
            if (!this.resultsRecorded)
            {
                List<(uint Number, uint PrevRank, uint NewRank, uint PrevXP, uint NewXP)>? beasts = null;
                try
                {
                    ReaderXBMResult reader = new(result);
                    // Native entries defer their reads. Materialize every required value before saving any row.
                    beasts = reader.BeastEntries.Select(entry =>
                        (entry.Number, entry.PrevRank, entry.NewRank, entry.PrevXP, entry.NewXP)).ToList();
                    if (beasts.Count == 0 || beasts.Any(entry => entry.PrevRank is < 1 or > 25 ||
                            entry.NewRank is < 1 or > 25 || !CrucibleTeam.FamiliarSheetDatas.ContainsKey(entry.Number)) ||
                        !BeastmasterRuntime.Service.ObserveBoardResults(beasts.Select(entry => (entry.Number, entry.NewRank, entry.NewXP)).ToArray()))
                        throw new BeastmasterReadUnavailableException(CrucibleUi.ResultWindow, "Participating beast results are incomplete");
                }
                catch (BeastmasterReadUnavailableException ex)
                {
                    if (!acceptUnconfirmed) return this.RecoverUnreadable(ex);
                    beasts = null;
                    BeastmasterRuntime.Service.AcceptUnconfirmedBoardResults();
                    this.resultsRecorded = true;
                }

                if (beasts != null)
                {
                    foreach (var entry in beasts)
                        CrucibleTeam.UpdateFamiliar(entry.Number, entry.NewRank, entry.NewXP);
                    BeastmasterRuntime.Save();
                    this.resultsRecorded = true;
                    this.LogResults(result, beasts);
                }
            }


            this.Status = "Finishing the board";
            if (now < this.nextResultExit) return false;
            this.nextResultExit = now + BeastmasterReadRecovery.Delay;
            if (!TryButton(CrucibleUi.ResultWindow, result, 61)) return false;
            this.resultExitPending = true;
            // Dispatch is not departure. Reacquire the control and retry after
            // five seconds if the game has kept this completed result open.
            return true;
        }

        private void LogResults(AtkUnitBase* result, List<(uint Number, uint PrevRank, uint NewRank, uint PrevXP, uint NewXP)> beasts)
        {
            try
            {
                ReaderXBMResult reader = new(result);
                Svc.Log.Info($"Crucible Results: Total Score: {reader.TotalScore}, Rank: {reader.Rank}" +
                    "\nBeasts:\n\t" + string.Join("\n\t", beasts.Select(entry =>
                        $"{CrucibleTeam.FamiliarSheetDatas[entry.Number].Name} ({entry.Number}) - Rank: {entry.PrevRank} -> {entry.NewRank}, XP: {entry.PrevXP} -> {entry.NewXP}")) +
                    $"\nScores: Performance: {reader.Performance} | MovesMade: {reader.MovesMadeScore} | Enemy: {reader.EnemyScore} | Elite: {reader.EliteScore} | Boss: {reader.BossScore} | HP: {reader.RemainingHP} -> {reader.RemainingHPScore}\n" +
                    "Bonus Boost: " + reader.BonusBoostScore + "\n\t" +
                    string.Join("\n\t", reader.BonusBoostEntries.Select(entry => $"{entry.Name}: {entry.Score}")) +
                    "\n Loot:\n\t" + string.Join("\n\t", reader.LootEntries.Select(entry => $"{entry.Name} ({entry.ItemId}) - Count: {entry.Count}")));
            }
            catch (BeastmasterReadUnavailableException)
            {
                // Score, bonus, and loot details are optional and cannot block a completed board.
                Svc.Log.Debug("[Crucible] Optional detailed result data is unavailable");
            }
        }

        private BeastmasterFightContext? ReadPendingFight()
        {
            if (CrucibleUi.Ready(CrucibleUi.BoardLayout) == null || CrucibleUi.Ready(CrucibleUi.TeamWindow) == null)
                return null;
            return CrucibleBoard.Current()?.StopPlayerIsOn()?.ResolveFight(fightPanelReady: true);
        }

        private bool StartFight(DateTime now)
        {
            AtkUnitBase* layout = CrucibleUi.Ready(CrucibleUi.BoardLayout);
            AtkUnitBase* party  = CrucibleUi.Ready(CrucibleUi.TeamWindow);
            if (layout == null || party == null)
                return false;
            if (this.WasSubmitted(CrucibleUi.BoardLayout, layout)) return true;

            var requiresClearTeam = BeastmasterSelection.RequiresClearTeam(Config);
            var fight = requiresClearTeam ? this.ReadPendingFight() : null;
            if (requiresClearTeam && fight == null)
            {
                this.Status = "Waiting for a verified native battle identity before selecting the fight setup.";
                return true;
            }
            BeastmasterCombat.Setup setup = default;
            if (requiresClearTeam && !BeastmasterCombat.TryReadSetup(Config.Board, fight!.Value.BattleId, out setup, out var setupReason))
            {
                this.Status = setupReason;
                return true;
            }

            if (CrucibleUi.IsOpen(CrucibleUi.YesNo) || now < this.fightNext || now - this.itemLastUse < ItemGap)
                return true;

            if (BeastmasterRuntime.Service.TryPrepareFood(betweenFights: true)) return true;

            if (!Config.FightPicks && !BeastmasterSelection.RequiresClearTeam(Config))
                return false;

            List<ReaderXBMPetParty.MonsterEntry>? team = CrucibleUi.Team();
            if (team == null || team.Count == 0)
                return true;

            List<ReaderXBMPetParty.MonsterEntry> alive = requiresClearTeam
                ? setup.Trio.Select(number => team.FirstOrDefault(row => row.Number == number && !row.Disabled &&
                    row.HP > 0 && row.MaxHP > 0 && row.HP <= row.MaxHP && row.Rank >= BeastmasterSelection.TrainingTarget(Config.Board)))
                    .OfType<ReaderXBMPetParty.MonsterEntry>().ToList()
                : CrucibleTeam.FightOrder(team).Take(FightPicks).ToList();
            if (requiresClearTeam && (!BeastmasterSelection.ClearFightReady(alive.Select(row => row.Number)) ||
                !BeastmasterSelection.TeamMatches(team.Select(row => row.Number).ToArray(), setup.Roster)))
            {
                this.Status = "The exact native encounter trio and registered roster must be present and trained. A missing or knocked-out required familiar cannot be silently replaced.";
                return true;
            }
            if (BeastmasterSelection.RequiresClearTeam(Config) &&
                (alive.Any(row => row.MaxHP == 0 || row.HP == 0 || row.HP > row.MaxHP) ||
                 Svc.Objects.LocalPlayer is not { IsDead: false, MaxHp: > 0 } player ||
                 !BeastmasterSelection.PlayerReadyForFight(player.CurrentHp, player.MaxHp)))
            {
                this.Status = "Fight preparation is waiting for readable, living familiars and player HP.";
                return true;
            }
            if (alive.Count == 0)
            {
                this.Status = "Every familiar is knocked out";
                return true;
            }

            List<ReaderXBMPetParty.MonsterEntry> picked = team.OrderBy(me => me.SelectionIndex).Where(me => me.SelectionIndex < 3).Take(FightPicks).ToList();

            if (picked.Count > 0)
            {
                for (int i = picked.Count - 1; i >= 0; i--)
                {
                    ReaderXBMPetParty.MonsterEntry pickedEntry = picked[i];
                    if (i >= alive.Count || pickedEntry.Number != alive[i].Number)
                    {
                        Svc.Log.Debug($"Crucible: Unpicking familiar ({pickedEntry.Name})");
                        Screens.PetParty.Pick(party, pickedEntry.index);
                        this.fightNext = now + PickInterval;
                        return true;
                    }
                }
            }



            if (picked.Count < Math.Min(FightPicks, alive.Count))
            {
                ReaderXBMPetParty.MonsterEntry entry = alive[picked.Count];

                Svc.Log.Debug($"Crucible: Picking familiar ({entry.Name})");
                Screens.PetParty.Pick(party, entry.index);
                this.fightNext = now + PickInterval;
                return true;
            }

            if (requiresClearTeam && setup.RequiredGear is { Length: > 0 } requiredGear)
            {
                var hud = CrucibleUi.Ready(CrucibleUi.MainHud);
                if (hud == null)
                {
                    this.Status = "Waiting for readable native Beast Gear before commencing this fight.";
                    return true;
                }
                var ownedGear = new ReaderXBMContentsMainHUD(hud).OwnedEntriesOwned.Select(entry => entry.Id).ToHashSet();
                var missingGear = requiredGear.Where(id => !ownedGear.Contains(id)).ToArray();
                if (missingGear.Length > 0)
                {
                    this.Status = "Fight preparation requires owned Beast Gear: " + string.Join(", ", missingGear.Select(CrucibleItemData.NameOf)) + ".";
                    return true;
                }
            }

            if (!BeastmasterRuntime.Service.CombatReady(out var combatReason))
            {
                BeastmasterRuntime.Service.Recover(combatReason);
                this.Status = combatReason;
                return true;
            }
            if (requiresClearTeam) BeastmasterRuntime.Service.RecordFightContext(fight!.Value);
            Screens.StageDetail.Confirm(layout);
            this.fightNext   = now + CommenceRetry;
            this.ExpectConfirmation(CrucibleUi.BoardLayout, layout, now);
            Svc.Log.Debug("Crucible: Commencing the battle");
            this.Status = "Commencing the battle";
            return true;
        }

        private void PickTreasure(AtkUnitBase* treasure, DateTime now)
        {
            if (this.WasSubmitted(CrucibleUi.TreasureWindow, treasure)) return;
            ReaderXBMContentsTreasure xbmTreasure = new(treasure);

            if (!Config.Treasure)
            {
                Screens.Treasure.Close(treasure);
                this.Submitted(CrucibleUi.TreasureWindow, treasure);
                this.confirmFrom = now;
                return;
            }
            
            uint[] items = xbmTreasure.ItemEntriesValid.Select(ie => ie.Id).ToArray();
            HashSet<uint> gear  = xbmTreasure.OwnedEntriesOwned.Select(ie => ie.Id).ToHashSet();
            if (!this.TryRequiredClearGear(out var requiredGear)) return;
            var missingGear = requiredGear.Count(row => !gear.Contains(row));

            List<ReaderXBMContentsTreasure.TreasureChoice> treasureChoices = xbmTreasure.TreasureChoices;

            if (treasureChoices.Count == 0)
                throw new BeastmasterReadUnavailableException(CrucibleUi.TreasureWindow, "Treasure choices are unavailable");

            List<ReaderXBMContentsTreasure.TreasureChoice> choices = [];

            foreach (ReaderXBMContentsTreasure.TreasureChoice choice in treasureChoices)
            {
                if (choice.Bought)
                    continue;
                if (CrucibleItemData.ShopOrder.Any(e => e.ID == choice.Item && e.Blocked))
                    continue;

                if(CrucibleItemData.ShopGear.Contains(choice.Item))
                {
                    if (gear.Count < GearCap && !gear.Contains(choice.Item) && !CrucibleItemData.BlockedGear(choice.Item, gear) &&
                        !(missingGear > 0 && gear.Count >= GearCap - missingGear && !requiredGear.Contains(choice.Item)))
                        choices.Add(choice);

                    continue;
                }

                if(items.Length < ItemCap)
                    choices.Add(choice);
            }

            if (choices.Count == 0)
            {
                Screens.Treasure.Close(treasure);
                this.Submitted(CrucibleUi.TreasureWindow, treasure);
                this.confirmFrom = now;
                return;
            }

            var clearPriorities = BeastmasterSelection.RequiresClearTeam(Config)
                ? CrucibleItemData.ClearShopPriority(Config.Board, items, gear, requiredGear).ToList() : [];
            ReaderXBMContentsTreasure.TreasureChoice best = choices.OrderBy(x =>
            {
                var rank = clearPriorities.IndexOf(x.Item);
                return rank >= 0 ? rank : clearPriorities.Count + (long)CrucibleItemData.TreasureRank(x.Item);
            }).ThenBy(x => x.treasureIndex).First();

            Svc.Log.Info($"[Crucible] Treasure: taking {Describe(best)} from {string.Join(" / ", choices.Select(Describe))}");

            Screens.Treasure.Take(treasure, (uint)best.treasureIndex);
            this.Submitted(CrucibleUi.TreasureWindow, treasure);

            this.confirmFrom = now;
            this.Status      = $"Taking {(best.Item != 0 ? CrucibleItemData.NameOf(best.Item) : best.treasureIndex)}";

            return;

            static string Describe(ReaderXBMContentsTreasure.TreasureChoice choice) =>
                $"{CrucibleItemData.NameOf(choice.Item)} ({choice.treasureIndex})";
        }

        private void Rest(AtkUnitBase* party, DateTime now)
        {
            if (this.WasSubmitted(CrucibleUi.TeamWindow, party)) return;
            if (!Config.Rest)
            {
                Screens.PetParty.RestReturn(party);
                this.Submitted(CrucibleUi.TeamWindow, party);
                this.confirmFrom = now;
                return;
            }

            if (this.restPicks == null)
            {
                if (CrucibleUi.Team() is not { Count: > 0 } rows)
                    throw new BeastmasterReadUnavailableException(CrucibleUi.TeamWindow, "Campsite familiar rows are unavailable");

                // Picking nobody gives a 90% heal
                var picks = rows.Where(x => x is { MaxHP: > 0, HP: > 0 } && (float)x.HP / x.MaxHP < RestBelow)
                                .OrderBy(x => (float)x.HP / x.MaxHP)
                                .Take(RestPicks)
                                .Select(x => x.index)
                                .ToList();
                if (BeastmasterSelection.RequiresClearTeam(Config) && Config.Board == 3 && CrucibleBoard.Current() is { } board &&
                    board.StopPlayerIsOn() is { Kind: CrucibleStopKind.Campsite } camp &&
                    Svc.Objects.LocalPlayer is { MaxHp: > 0 } player)
                {
                    var hud = CrucibleUi.Ready(CrucibleUi.MainHud);
                    // Missing optional inventory cannot establish two spare AoE
                    // items; retain the Behemoth recovery in that case.
                    var damageItems = hud == null ? 0 : new ReaderXBMContentsMainHUD(hud).ItemEntriesValid
                        .Count(item => CrucibleItemData.CombatDamageItems.Contains(item.Id));
                    var familiar = CrucibleItemData.Board3RestFamiliar(CrucibleItemData.BattleReachable(board, camp, 19),
                        (float)player.CurrentHp / player.MaxHp, damageItems);
                    picks = rows.Where(row => row.Number == familiar && row is { HP: > 0, MaxHP: > 0 } && row.HP < row.MaxHP)
                        .Select(row => row.index).Take(1).ToList();
                }
                string names = string.Join(", ", picks.Select(i => $"{rows[i].Name} {rows[i].HP}/{rows[i].MaxHP}"));
                this.restPicks = picks;
                this.restStep = 0;
                Svc.Log.Info($"[Crucible] Campsite: resting with {(names.Length > 0 ? names : "no familiars (90% self heal)")}");
            }

            if (this.restStep < this.restPicks.Count)
            {
                Screens.PetParty.Pick(party, this.restPicks[this.restStep]);
                this.restStep++;
                EzThrottler.Throttle("CrucibleMenus", 400);
                return;
            }

            if (TryButton(CrucibleUi.TeamWindow, party, 21))
            {
                this.Submitted(CrucibleUi.TeamWindow, party);
                this.confirmFrom = now;
                this.Status      = "Resting at the campsite";
            }
        }

        private bool FeedPending(DateTime now) =>
            now - this.feedFrom <= FeedTimeout;

        private void UpdateShop(DateTime now)
        {
            if (now < this.shopNext)
                return;

            this.shopNext = now + TimeSpan.FromMilliseconds(250);

            if (!Config.Shop)
            {
                this.ResetShopVisit();
                AtkUnitBase* shopWindow = CrucibleUi.Ready(CrucibleUi.ShopWindow);
                if (shopWindow != null)
                {
                    if (this.WasSubmitted(CrucibleUi.ShopWindow, shopWindow)) return;
                    if (TryButton(CrucibleUi.ShopWindow, shopWindow, 40))
                    {
                        this.Submitted(CrucibleUi.ShopWindow, shopWindow);
                        this.confirmFrom = now;
                    }
                }

                return;
            }

            AtkUnitBase* shop = CrucibleUi.Ready(CrucibleUi.ShopWindow);
            if (shop == null)
            {
                this.ResetShopVisit();
                return;
            }
            if (this.WasSubmitted(CrucibleUi.ShopWindow, shop)) return;


            if (this.FeedPending(now))
            {
                this.Feed(now);
                return;
            }
            else
            {
                AtkUnitBase* party = CrucibleUi.Ready(CrucibleUi.TeamWindow);
                if (party != null)
                {
                    if (!this.WasSubmitted(CrucibleUi.TeamWindow, party) &&
                        TryButton(CrucibleUi.TeamWindow, party, 21))
                        this.Submitted(CrucibleUi.TeamWindow, party);
                    return;
                }
            }


            if (CrucibleUi.IsOpen(CrucibleUi.YesNo) || CrucibleUi.IsOpen(CrucibleUi.TeamWindow))
                return;

            int coins = CrucibleUi.ShopCoins(shop);

            List<ReaderXBMContentsItemShop.StockEntry> affordable = CrucibleUi.ShopStock(shop).Where(x => !x.Bought && x.Price <= coins && !this.shopTried.Contains(x.Item)).ToList();

            if (!this.TryRequiredClearGear(out var requiredGear)) return;
            if (this.ChooseBuy(affordable, CrucibleUi.ShopHeldItems(shop), CrucibleUi.ShopOwnedGear(shop), requiredGear) is not { } buy)
            {
                if (TryButton(CrucibleUi.ShopWindow, shop, 40))
                {
                    this.Submitted(CrucibleUi.ShopWindow, shop);
                    this.Status          = $"Shop done, {coins} coins left";
                    Svc.Log.Info($"[Crucible] Shop: done with {coins} coins left");
                    this.confirmFrom = now;
                }

                return;
            }

            this.Status = $"Buying {CrucibleItemData.NameOf(buy.Item)} for {buy.Price}";
            Svc.Log.Info($"[Crucible] Shop: buying {CrucibleItemData.NameOf(buy.Item)} for {buy.Price} of {coins} coins");

            this.shopTried.Add(buy.Item);
            Screens.ItemShop.Buy(shop, buy.purchaseIndex);
            this.ExpectConfirmation(CrucibleUi.ShopWindow, shop, now);
            this.shopNext    = now + ShopStep;

            if (CrucibleItemData.ShopFeed.Contains(buy.Item))
            {
                this.fedThisVisit = true;
                this.feedFrom     = now;
                this.feedOrder    = null;
                this.feedTry      = 0;
            }
        }

        private ReaderXBMContentsItemShop.StockEntry? ChooseBuy(List<ReaderXBMContentsItemShop.StockEntry> stock, uint[] held, HashSet<uint> ownedGear,
            uint[] requiredGear)
        {
            bool itemRoom = held.Length    < ItemCap;
            bool gearRoom = ownedGear.Count < GearCap;
            var missingGear = requiredGear.Count(row => !ownedGear.Contains(row));

            IEnumerable<ReaderXBMContentsItemShop.StockEntry> wanted = stock.Where(x =>
                                                                                   {
                                                                                       CrucibleItemData.CrucibleItemCategory category = CrucibleItemData.GetCategoryOf(x.Item);
                                                                                       return category switch
                                                                                       { 
                                                                                           CrucibleItemData.CrucibleItemCategory.Gear => gearRoom && !ownedGear.Contains(x.Item) && !CrucibleItemData.BlockedGear(x.Item, ownedGear)
                                                                                               && !(missingGear > 0 && ownedGear.Count >= GearCap - missingGear && !requiredGear.Contains(x.Item)),
                                                                                           CrucibleItemData.CrucibleItemCategory.Feed => !this.fedThisVisit,
                                                                                           _ when CrucibleItemData.CrucibleItemCategory.Item.HasFlag(category) => itemRoom,
                                                                                           _ => false
                                                                                       };
                                                                                   });

            if (BeastmasterSelection.RequiresClearTeam(Config))
            {
                var clearWanted = wanted.ToList();
                var allowed = CrucibleItemData.ShopOrderActive.Select(row => row.ID).ToHashSet();
                foreach (var row in CrucibleItemData.ClearShopPriority(Config.Board, held, ownedGear, requiredGear))
                    if (allowed.Contains(row) && (row == 140 || !held.Contains(row)) && clearWanted.FirstOrDefault(entry => entry.Item == row) is { } supply)
                        return supply;
                return FirstInStock(clearWanted, CrucibleItemData.ShopOrderActive, held);
            }
            return FirstInStock(wanted, CrucibleItemData.ShopOrderActive, held);
        }

        private static ReaderXBMContentsItemShop.StockEntry? FirstInStock(IEnumerable<ReaderXBMContentsItemShop.StockEntry> stock, List<BeastmasterShopList.Entry> priority, uint[] owned)
        {
            Dictionary<uint, ReaderXBMContentsItemShop.StockEntry> byRow = stock.GroupBy(x => x.Item).ToDictionary(g => g.Key, g => g.First());
            foreach (BeastmasterShopList.Entry row in priority)
                if (!owned.Contains(row.ID) && byRow.TryGetValue(row.ID, out ReaderXBMContentsItemShop.StockEntry? entry))
                    return entry;
            return null;
        }

        private void Feed(DateTime now)
        {
            AtkUnitBase* party = CrucibleUi.Ready(CrucibleUi.TeamWindow);
            if (party == null || CrucibleUi.IsOpen(CrucibleUi.YesNo))
                return;
            if (this.WasSubmitted(CrucibleUi.TeamWindow, party)) return;

            ReaderXBMPetParty petParty = new(party);

            if (this.feedOrder == null)
            {
                if (CrucibleUi.Team(petParty) is not { Count: > 0 } team)
                    throw new BeastmasterReadUnavailableException(CrucibleUi.TeamWindow, "Feeding familiar rows are unavailable");

                team = team.Where(x => !x.Disabled && x.FedCurrent < x.FedMax && x.FedItems.All(item => item.ItemId != petParty.FeedItem.ItemId)).ToList();
                this.feedOrder = CrucibleTeam.FightOrder(team);
            }

            if (this.feedTry >= this.feedOrder.Count)
            {
                Svc.Log.Info("[Crucible] Shop: no familiar would take the feed");
                if (TryButton(CrucibleUi.TeamWindow, party, 21))
                {
                    this.Submitted(CrucibleUi.TeamWindow, party);
                    this.feedFrom = DateTime.MinValue;
                }
                return;
            }

            Screens.PetParty.Pick(party, this.feedOrder[this.feedTry].index);
            this.feedTry++;
            this.ExpectConfirmation(CrucibleUi.TeamWindow, party, now);
            this.shopNext    = now + FeedRetry;
        }

        private void ResetShopVisit()
        {
            this.shopTried.Clear();
            this.fedThisVisit    = false;
        }

        private void UpdateItems(DateTime now)
        {
            if (now < this.itemNext)
                return;

            this.itemNext = now + TimeSpan.FromMilliseconds(250);

            if (!Config.Items ||
                Svc.Objects.LocalPlayer is not { IsDead: false, MaxHp: > 0 } me || BeastmasterPlayer.HasStatus(418))
                return;

            AtkUnitBase* hud = CrucibleUi.Ready(CrucibleUi.MainHud);
            if (hud == null)
                return;

            ReaderXBMContentsMainHUD reader = new(hud);

            var blocked = CrucibleItemData.ShopOrder.Where(e => e.Blocked).Select(e => e.ID).ToHashSet();
            List<ReaderXBMContentsItemShop.ItemEntry> items = reader.ItemEntries.Where(e => !blocked.Contains(e.Id)).ToList();
            if (this.requestedItem is { } requested && InstanceContentCrucible.ItemCount(requested.Id) is { } remaining && remaining < requested.Count)
            {
                Svc.Log.Info($"[Crucible] Items: native inventory confirms consumption of {requested.Name}");
                this.requestedItem = null;
            }
            if (now - this.itemLastUse < ItemGap) return;

            CrucibleBoard?     board = CrucibleBoard.Current();
            CrucibleBoardStop? currentStop   = board?.StopPlayerIsOn();

            bool confirmingFight = CrucibleUi.IsOpen(CrucibleUi.BoardLayout);
            bool  InArena  = currentStop == null && !confirmingFight;
            bool  fighting = Svc.Condition[ConditionFlag.InCombat];
            float hp       = (float)me.CurrentHp / me.MaxHp;

            var ownedTarget = InArena && fighting ? BeastmasterRuntime.Service.ReadCombatTarget() : default;
            if (ownedTarget.ProtectionItem == 137 && !BeastmasterPlayer.HasStatus(4849) &&
                Pick(PickFromItems([137u])))
                return;

            IEnumerable<CrucibleBoardStop> boardStops = [];
            // An open fight confirmation commits to this fight, not to a possible camp
            // after it. Let the existing item cooldown and fresh HP observation finish first.
            if (hp <= (InArena ? FightLow : BoardLow) && (InArena && fighting ||
                !InArena && (confirmingFight || !board!.HasCampBeforeNextFight(currentStop!, ref boardStops))))
                if (Pick(PickFromItems(InArena ? CrucibleItemData.FightHealingItems : CrucibleItemData.BoardHealingItems)))
                    return;

            foreach (uint[] statusItem in CrucibleItemData.StatusItems)
                if (!BeastmasterPlayer.HasStatus(statusItem[1]) && (statusItem[0] is not (100 or 101) || fighting))
                    if (Pick(items.FirstOrDefault(x => x.Id == statusItem[0])))
                        return;

            if (!InArena || !BeastmasterRuntime.Service.AllowCombatDamageItems)
                return;

            List<IBattleNpc> enemies = Svc.Objects.Where(igo => igo is { ObjectKind: ObjectKind.BattleNpc, IsTargetable: true } && igo.IsHostile() && BeastmasterPlayer.BelowDistanceToPlayer(igo.Position, 40f, 20f / 2f)).Cast<IBattleNpc>().Where(ibn => ibn.Health > 0).ToList();
            var clearTeam = BeastmasterSelection.RequiresClearTeam(Config);
            // Encounter identity survives a boss temporarily becoming untargetable;
            // the actual item target still comes from the owned native controller.
            bool Encounter(uint id) => Svc.Objects.Any(actor => actor is IBattleNpc npc && npc.BaseId == id &&
                npc.Health > 0 && BeastmasterPlayer.BelowDistanceToPlayer(npc.Position, 40f, 10f));
            var siren = Config.Board == 3 && Encounter(0x4CA1);
            var nativeSandWindow = ownedTarget.DamageItem == 139;
            ReaderXBMContentsItemShop.ItemEntry? dmgItem = PickFromItems(CrucibleItemData.CombatDamageItems.Where(item =>
                !clearTeam || !CrucibleItemData.ReserveDamageItem(Config.Board, item, siren, nativeSandWindow)));
            if (dmgItem == null)
                return;

            var requestedSand = dmgItem.Id == 139 && nativeSandWindow;
            if (enemies.Count < (requestedSand ? 2 : 4)) return;
            IBattleNpc   bossObject = enemies.MaxBy(igo => igo.MaxHp)!;
            IBattleNpc[] battleNpcs = requestedSand ? enemies.ToArray()
                : enemies.Except([bossObject]).ToArray();

            IBattleNpc? target = ownedTarget.CanPull && ownedTarget.InstanceId != 0
                ? enemies.FirstOrDefault(enemy => enemy.EntityId == ownedTarget.InstanceId) : null;

            var radius = dmgItem.Id == 139 ? 18f : 11f;
            if (target != null && (dmgItem.Id != 139 || Vector2.DistanceSquared(
                    new(target.Position.X, target.Position.Z), new(me.Position.X, me.Position.Z)) <= MathF.Pow(25f + target.HitboxRadius, 2)) &&
                battleNpcs.Count(enemy => Vector2.DistanceSquared(
                    new(target.Position.X, target.Position.Z), new(enemy.Position.X, enemy.Position.Z)) < radius * radius) >= (requestedSand ? 2 : 3) &&
                BeastmasterRuntime.Service.AllowCombatDamageItems)
            {
                Svc.Targets.Target = target;
                Pick(dmgItem);
            }

            return;

            ReaderXBMContentsItemShop.ItemEntry? PickFromItems(IEnumerable<uint> ids)
            {
                foreach (uint id in ids)
                {
                    foreach (ReaderXBMContentsItemShop.ItemEntry entry in items)
                        if (entry.Id == id && entry.Available)
                            return entry;
                }
                return null;
            }

            bool Pick(ReaderXBMContentsItemShop.ItemEntry? pick)
            {
                if (pick == null)
                    return false;
                if (pick.Id == 0)
                    return false;
                if (!pick.Available)
                    return false;

                int slot = reader.GetItemIndex(pick);

                if (!InstanceContentCrucible.TryUseItem((uint)slot, pick.Id, 0, out var count))
                    return false;

                this.itemLastUse = now;
                this.requestedItem = (pick.Id, count, pick.Name);

                this.Status       = $"Requesting {pick.Name} at {hp:P0}";
                Svc.Log.Info($"[Crucible] Items: requesting {pick.Name} (slot {slot}) at {hp:P0} {(InArena ? "in a fight" : "on the board")}");

                return true;
            }
        }
    }
}
