using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using VERMAXION.Beastmaster.Readers;

namespace VERMAXION.Beastmaster;

internal sealed unsafe partial class BeastmasterService
{
    private BeastmasterRefreshTraversal? refreshTraversal;
    private uint refreshTerritory;
    private uint refreshOriginalPage;
    private uint refreshOriginalSelection;
    private (uint Page, int Slot)? refreshOriginalSelectionLocation;
    private bool refreshCapturedState;
    private bool refreshOpenedNotebook;
    private bool refreshTouchedNotebook;
    private bool refreshClosing;
    private bool refreshSelectionSubmitted;
    private bool refreshCleanupPending;
    private bool refreshComplete;
    private bool refreshWholeRosterObserved;
    private bool refreshScoped;
    private string refreshOutcomeReason = string.Empty;
    private uint? refreshCleanupPage;
    private uint[] refreshCleanupPreviousRows = [];
    private readonly HashSet<uint> refreshCleanupPageRequests = [];
    private readonly HashSet<uint> refreshAdditionalUnread = [];
    private DateTime refreshStepStarted;
    private string lastRefreshCleanupFailure = string.Empty;
    private string refreshStatus = "No full beast refresh has been requested.";

    public bool IsRefreshing => State == RunState.Refreshing;
    public string RefreshStatus => IsRefreshing || refreshCleanupPending ? status : refreshStatus;

    private static bool RegistrationVisible() => CrucibleUi.IsVisible(CrucibleUi.TeamWindow) ||
        CrucibleUi.IsVisible(CrucibleUi.BoardList) || CrucibleUi.IsVisible(CrucibleUi.BoardLayout);

    private bool RefreshContextReady(out string reason)
    {
        if (!CanObserveCharacter || !BeastmasterPlayer.IsValid || Svc.Objects.LocalPlayer is not { IsDead: false })
        { reason = "Wait until the current character is ready."; return false; }
        if (Svc.Condition[ConditionFlag.BoundByDuty] || IsCrucibleTerritory(Svc.ClientState.TerritoryType))
        { reason = "Leave the current duty before refreshing beasts."; return false; }
        if (Svc.Condition[ConditionFlag.InCombat] || Svc.Condition[ConditionFlag.OccupiedInQuestEvent] ||
            Svc.Condition[ConditionFlag.OccupiedInCutSceneEvent])
        { reason = "Wait until the current character is idle before refreshing beasts."; return false; }
        if (!NativeQueueIsIdle() || RegistrationVisible())
        { reason = "Close Crucible registration and wait for the duty queue to settle before refreshing beasts."; return false; }
        if (CrucibleUi.IsVisible(CrucibleUi.YesNo))
        { reason = "Close the current confirmation before refreshing beasts."; return false; }
        if (!CrucibleTeam.OwnershipKnown)
        { reason = "Native beast ownership data is unavailable; recorded observations were retained."; return false; }
        reason = string.Empty;
        return true;
    }

    public bool CanRefresh(out string reason)
    {
        if (disposed || IsActive || IsPaused)
        { reason = "A Beastmaster run, refresh or its cleanup is already active."; return false; }
        return RefreshContextReady(out reason);
    }

    public bool TryRefreshOwnership()
    {
        if (!CanRefresh(out var reason)) { refreshStatus = reason; return false; }
        var character = Svc.PlayerState.ContentId;
        var data = getCharacterData();
        if (!CrucibleTeam.TryObserveOwnership(data, out _) || Svc.PlayerState.ContentId != character ||
            !ReferenceEquals(data, getCharacterData()))
        { refreshStatus = "Native beast ownership data is unavailable; recorded observations were retained."; return false; }
        CrucibleTeam.UpdateCache();
        save();
        refreshStatus = "Ownership checked. Recorded ranks may need a full refresh.";
        return true;
    }

    public bool RequestRefresh()
    {
        if (!CanRefresh(out var reason)) { refreshStatus = reason; return false; }
        hasRunDisplay = false;
        owner = Svc.PlayerState.ContentId;
        runData = getCharacterData();
        runSettings = null;
        BeginFullRefresh();
        return IsRefreshing || refreshCleanupPending;
    }

    private void BeginFullRefresh(IReadOnlyCollection<uint>? scope = null)
    {
        refreshTerritory = Svc.ClientState.TerritoryType;
        if (!RefreshContextReady(out var reason) || !CrucibleTeam.TryObserveOwnership(CharacterData, out var owned))
        {
            refreshStatus = reason.Length > 0 ? reason : "Native beast ownership data is unavailable; recorded observations were retained.";
            Transition(RunState.Complete, refreshStatus);
            owner = 0; runData = null;
            return;
        }
        refreshTraversal = new(scope ?? owned, DateTime.UtcNow);
        refreshScoped = scope != null;
        refreshWholeRosterObserved = false;
        refreshOutcomeReason = string.Empty;
        refreshStepStarted = DateTime.UtcNow;
        refreshOriginalPage = refreshOriginalSelection = 0;
        refreshOriginalSelectionLocation = null;
        refreshCapturedState = refreshOpenedNotebook = refreshTouchedNotebook = refreshClosing = refreshSelectionSubmitted = false;
        refreshCleanupPending = refreshComplete = false;
        refreshCleanupPage = null;
        refreshCleanupPageRequests.Clear();
        refreshAdditionalUnread.Clear();
        Save();
        Transition(RunState.Refreshing, "Opening the standalone bestiary to refresh beast ranks and EXP.");
        refreshStatus = status;
        try
        {
            var agent = AgentXBMMonsterNotebook.Instance();
            if (agent == null) { FailRefresh("The standalone bestiary is unavailable."); return; }
            if (CrucibleUi.IsVisible(CrucibleUi.BestiaryWindow))
            {
                if (agent->Source != 0 || agent->UsesContentPetData)
                { FailRefresh("Close the team bestiary before refreshing beasts."); return; }
                // Snapshot the preexisting page/selection only after real page
                // data is ready; zero-valued initialization is not user state.
            }
            else
            {
                // SDK source 0 is the standalone notebook. Never open through
                // PetParty, change filters, or enter the registration scan path.
                refreshOpenedNotebook = true;
                agent->OpenAddon(0);
            }
        }
        catch (Exception ex) { Svc.Log.Error(ex, "[Beastmaster] Beast refresh unavailable"); FailRefresh(ex.Message); }
    }

    private void UpdateFullRefresh()
    {
        if (!IsRefreshing || refreshTraversal == null) return;
        try
        {
            if (!CanObserveCharacter || Svc.ClientState.TerritoryType != refreshTerritory || !RefreshContextReady(out _))
            { FailRefresh("Beast refresh stopped after character, territory or native context changed."); return; }
            if (IsPaused) return;
            var now = DateTime.UtcNow;
            var agent = AgentXBMMonsterNotebook.Instance();
            if (agent == null || agent->Source != 0 || agent->UsesContentPetData)
            { FailRefresh("Beast refresh stopped because the bestiary changed to team data."); return; }
            var page = new BeastmasterNotebookPage(false, 0, 0, []);
            ReaderXBMMonsterNotebook? reader = null;
            AtkUnitBase* notebook = null;
            if (CrucibleUi.TryReady(CrucibleUi.BestiaryWindow, out notebook))
            {
                try
                {
                    reader = new(notebook);
                    if (reader.Filtered)
                    { FailRefresh("Clear the native bestiary filters before refreshing all owned beasts."); return; }
                    page = reader.ReadPage(agent->PageIndex);
                    if (page.Ready && !refreshOpenedNotebook && !refreshCapturedState)
                    {
                        refreshCapturedState = true;
                        refreshOriginalPage = page.Page;
                        refreshOriginalSelection = agent->SelectedPetId;
                        // No verified operation clears a standalone selection. Keep
                        // an unselected preexisting notebook untouched instead.
                        if (refreshOriginalSelection == 0)
                        { FailRefresh("Close the existing bestiary before refreshing beasts."); return; }
                    }
                    if (page.Ready && refreshCapturedState)
                    {
                        for (var slot = 0; slot < page.Numbers.Count; slot++)
                            if (page.Numbers[slot] == refreshOriginalSelection)
                                refreshOriginalSelectionLocation = (page.Page, slot);
                    }
                }
                catch (BeastmasterReadUnavailableException)
                {
                    reader = null;
                    page = new(false, 0, 0, []);
                }
            }

            CrucibleFamiliar? selected = null;
            try
            {
                if (reader != null && reader.TryReadCurrentExperience(out _) && CrucibleUi.BestiarySelected(notebook) is { } seen &&
                    seen.Number == agent->SelectedPetId && seen.Rank is >= 1 and <= 25)
                    selected = seen;
            }
            catch (BeastmasterReadUnavailableException) { /* The traversal retains this beast as unread. */ }
            if (selected != null && selected.Number == refreshTraversal.Selected &&
                CharacterData.Familiars.TryGetValue(selected.Number, out var previous) && previous.Rank > selected.Rank)
            {
                Svc.Log.Warning($"[Beastmaster] Refresh retained No. {selected.Number}: observed rank {selected.Rank} is below recorded rank {previous.Rank}.");
                refreshTraversal.RejectSelected(BeastmasterRefreshProblem.LowerRank, now);
                selected = null;
            }
            var priorSelection = refreshTraversal.Selected;
            var decision = refreshTraversal.Next(page, selected?.Number ?? 0, now);
            if (priorSelection != 0 && refreshTraversal.UnreadDetails.TryGetValue(priorSelection, out var problem))
                Svc.Log.Warning($"[Beastmaster] Refresh unread No. {priorSelection} ({CrucibleTeam.NameOf(priorSelection)}): {problem}; page={page.Page}/{page.PageCount}, nativePage={agent->PageIndex}, ready={page.Ready}, detail={selected?.Number}, entries=[{string.Join(",", page.Numbers)}].");
            status = refreshStatus = $"Refreshing beast ranks and EXP ({refreshTraversal.Read.Count}/{refreshTraversal.Owned.Count}); page {(page.PageCount == 0 ? 0 : page.Page + 1)}/{page.PageCount}.";
            switch (decision.Action)
            {
                case BeastmasterRefreshAction.ShowPage:
                    refreshTouchedNotebook = true;
                    Svc.Log.Debug($"[Beastmaster] Refresh requests page {decision.Page}; observed page={page.Page}/{page.PageCount}, entries=[{string.Join(",", page.Numbers)}].");
                    CrucibleUi.Screens.Notebook.ShowPage(notebook, decision.Page);
                    break;
                case BeastmasterRefreshAction.Select:
                    refreshTouchedNotebook = true;
                    if (!CrucibleUi.Screens.Notebook.PickEntry(notebook, (uint)decision.Slot))
                    {
                        Svc.Log.Warning($"[Beastmaster] Refresh selection unconfirmed for No. {decision.Number} ({CrucibleTeam.NameOf(decision.Number)}), page={page.Page}/{page.PageCount}, slot={decision.Slot}.");
                        refreshTraversal.RejectSelected(BeastmasterRefreshProblem.DetailUnavailable, now);
                    }
                    break;
                case BeastmasterRefreshAction.Record:
                    selected!.ObservedAtUtc = now;
                    CharacterData.Familiars[selected.Number] = selected;
                    Save();
                    break;
                case BeastmasterRefreshAction.Complete:
                    FinishRefreshObservation(string.Empty, complete: true);
                    break;
                case BeastmasterRefreshAction.Partial:
                    Svc.Log.Warning($"[Beastmaster] Refresh partial: {decision.Problem}; page={page.Page}/{page.PageCount}, requestedPage={refreshTraversal.RequestedPage}, nativePage={agent->PageIndex}, ready={page.Ready}, visited={refreshTraversal.VisitedPages}, entries=[{string.Join(",", page.Numbers)}], unread=[{string.Join(",", refreshTraversal.Missing)}].");
                    var reason = decision.Problem switch
                    {
                        BeastmasterRefreshProblem.InitialPageUnavailable => "The standalone bestiary did not become ready.",
                        BeastmasterRefreshProblem.PageUnconfirmed or BeastmasterRefreshProblem.PageChanged => "The bestiary page did not settle after refresh.",
                        BeastmasterRefreshProblem.TimeLimit => "The bestiary refresh did not finish within five minutes; partial observations were retained.",
                        _ when refreshTraversal.UnreadDetails.Values.Contains(BeastmasterRefreshProblem.LowerRank) => "The bestiary returned a lower rank than recorded; refresh stopped without replacing that observation.",
                        _ when refreshTraversal.UnreadDetails.Count > 0 => "The bestiary did not show the requested beast's rank and EXP; partial observations were retained.",
                        _ => "An owned beast was not available in the standalone bestiary; partial observations were retained.",
                    };
                    FinishRefreshObservation(reason, complete: false);
                    break;
            }
        }
        catch (Exception ex) { Svc.Log.Error(ex, "[Beastmaster] Beast refresh unavailable"); FailRefresh(ex.Message); }
    }

    private void FailRefresh(string reason)
    {
        if (IsRefreshing && refreshTraversal != null) FinishRefreshObservation(reason, complete: false);
    }

    private void FinishRefreshObservation(string reason, bool complete)
    {
        if (refreshTraversal == null) return;
        if (complete)
        {
            var ownershipReady = CrucibleTeam.TryObserveOwnership(CharacterData, out var currentOwned);
            refreshWholeRosterObserved = ownershipReady && currentOwned.SetEquals(refreshTraversal.Read);
            if (ownershipReady && !refreshScoped)
                refreshAdditionalUnread.UnionWith(currentOwned.Except(refreshTraversal.Read));
            if (!ownershipReady || !(refreshScoped ? refreshTraversal.Read.All(currentOwned.Contains) : refreshWholeRosterObserved))
            {
                complete = false;
                reason = "Beast ownership changed during refresh; partial observations were retained. Refresh again when the roster is settled.";
            }
        }
        refreshComplete = complete;
        if (complete) refreshStatus = $"Refreshed ranks and EXP for {refreshTraversal.Read.Count} owned beasts.";
        else MarkRefreshIncomplete(reason);
        Svc.Log.Information($"[Beastmaster] {refreshStatus}");
        refreshCleanupPending = true;
        if (!refreshOpenedNotebook && refreshTraversal.RequestedPage is { } unsettledPage)
        {
            // A timed-out page request may still arrive. Observe that result
            // before restoring; one sample of the old page is not settlement.
            refreshCleanupPage = unsettledPage;
            refreshCleanupPreviousRows = refreshTraversal.RequestedPagePreviousRows.ToArray();
        }
        refreshStepStarted = DateTime.UtcNow;
        IsPaused = false;
        pausedAt = default;
        Transition(RunState.Complete, complete ? "Restoring the bestiary after refresh." : refreshStatus);
        UpdateRefreshCleanup();
    }

    private void CancelFullRefresh()
    {
        if (IsRefreshing) { FailRefresh("Beast refresh cancelled; partial observations were retained."); return; }
        if (refreshCleanupPending)
        {
            MarkRefreshIncomplete("Beast refresh cancelled; partial observations were retained.");
        }
    }

    private void UpdateRefreshCleanup()
    {
        if (!refreshCleanupPending) return;
        try
        {
            // Session/territory replacement ends ownership of the previous context.
            // Never close or select an addon belonging to its replacement.
            if (!Svc.ClientState.IsLoggedIn || Svc.PlayerState.IsLoaded && (Svc.PlayerState.ContentId != owner || !ReferenceEquals(runData, getCharacterData())) ||
                Svc.ClientState.TerritoryType != refreshTerritory || Svc.Condition[ConditionFlag.BoundByDuty] || RegistrationVisible())
            { MarkRefreshIncomplete("Beast refresh stopped after character, territory or native context changed."); CompleteRefreshCleanup(); return; }
            if (!CrucibleUi.IsVisible(CrucibleUi.BestiaryWindow)) { CompleteRefreshCleanup(); return; }
            if (!CanObserveCharacter || !BeastmasterPlayer.IsValid || CrucibleUi.IsVisible(CrucibleUi.YesNo) ||
                !CrucibleUi.TryReady(CrucibleUi.BestiaryWindow, out var notebook)) return;
            var agent = AgentXBMMonsterNotebook.Instance();
            if (agent == null || agent->Source != 0 || agent->UsesContentPetData)
            { MarkRefreshIncomplete("Beast refresh stopped because the bestiary changed to team data."); CompleteRefreshCleanup(); return; }
            if (refreshOpenedNotebook)
            {
                if (!refreshClosing)
                {
                    refreshClosing = true; refreshStepStarted = DateTime.UtcNow;
                    CrucibleUi.Screens.Notebook.Close(notebook);
                }
                else if (DateTime.UtcNow - refreshStepStarted > TimeSpan.FromSeconds(3))
                    status = "Stopped; waiting for the standalone bestiary to close. Close it manually if necessary.";
                return;
            }
            if (!refreshTouchedNotebook) { CompleteRefreshCleanup(); return; }
            var reader = new ReaderXBMMonsterNotebook(notebook);
            var page = reader.ReadPage(agent->PageIndex);
            if (!page.Ready) return;
            if (refreshCleanupPage is { } requested)
            {
                if (page.Page != requested || page.Numbers.SequenceEqual(refreshCleanupPreviousRows))
                { ShowRefreshRestoreWait(); return; }
                refreshCleanupPage = null;
            }
            if (agent->SelectedPetId != refreshOriginalSelection || reader.CurrentNumber != refreshOriginalSelection || !reader.Selected)
            {
                if (refreshSelectionSubmitted) { ShowRefreshRestoreWait(); return; }
                if (refreshOriginalSelectionLocation is not { } location) { ShowRefreshRestoreWait(); return; }
                if (!RestoreRefreshPage(notebook, page, location.Page)) return;
                var slot = page.Numbers.ToList().FindIndex(number => number == refreshOriginalSelection);
                if (slot < 0) { ShowRefreshRestoreWait(); return; }
                refreshSelectionSubmitted = true; refreshStepStarted = DateTime.UtcNow;
                CrucibleUi.Screens.Notebook.PickEntry(notebook, (uint)slot);
                return;
            }
            if (!RestoreRefreshPage(notebook, page, refreshOriginalPage)) return;
            CompleteRefreshCleanup();
        }
        catch (Exception ex)
        {
            MarkRefreshIncomplete("The original bestiary selection could not be restored.");
            if (lastRefreshCleanupFailure != ex.Message)
            {
                lastRefreshCleanupFailure = ex.Message;
                Svc.Log.Error(ex, "[Beastmaster] Refresh cleanup is unconfirmed; no native input was repeated");
            }
            ShowRefreshRestoreWait();
        }
    }

    private bool RestoreRefreshPage(AtkUnitBase* notebook, BeastmasterNotebookPage observed, uint desired)
    {
        if (observed.Page == desired) return true;
        if (!refreshCleanupPageRequests.Add(desired)) { ShowRefreshRestoreWait(); return false; }
        refreshCleanupPage = desired;
        refreshCleanupPreviousRows = observed.Numbers.ToArray();
        refreshStepStarted = DateTime.UtcNow;
        CrucibleUi.Screens.Notebook.ShowPage(notebook, desired);
        return false;
    }

    private void ShowRefreshRestoreWait()
    {
        if (DateTime.UtcNow - refreshStepStarted <= TimeSpan.FromSeconds(3)) return;
        MarkRefreshIncomplete("The original bestiary selection could not be restored.");
        status = refreshStatus;
    }

    private void MarkRefreshIncomplete(string reason)
    {
        if (!refreshComplete && refreshOutcomeReason == reason) return;
        refreshComplete = false;
        refreshOutcomeReason = reason;
        var missing = (refreshTraversal?.Missing ?? []).Concat(refreshAdditionalUnread).Distinct().Order().ToArray();
        var unread = missing.Select(number => $"{CrucibleTeam.NameOf(number)} (#{number})");
        var total = (refreshTraversal?.Owned ?? []).Concat(refreshAdditionalUnread).Distinct().Count();
        refreshStatus = $"Beast refresh incomplete: {refreshTraversal?.Read.Count ?? 0}/{total} observed; unread: {string.Join(", ", unread)}. {reason}";
    }

    private void CompleteRefreshCleanup()
    {
        if (refreshComplete && refreshWholeRosterObserved && CanObserveCharacter)
        {
            CharacterData.RanksObservedAtUtc = DateTime.UtcNow;
            Save();
        }
        refreshCleanupPending = false; lastRefreshCleanupFailure = string.Empty;
        refreshTraversal = null;
        if (State == RunState.Complete) status = refreshStatus;
        if (!EntryPending && !combat.OwnsState) { owner = 0; runData = null; runSettings = null; }
    }

    private void ResumeRefreshAfterPause(TimeSpan elapsed)
    {
        if (IsRefreshing) refreshTraversal?.ResumeAfterPause(elapsed);
    }
}
