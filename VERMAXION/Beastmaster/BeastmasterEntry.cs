using System;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Hooking;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Enums;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace VERMAXION.Beastmaster;

internal sealed unsafe partial class BeastmasterService
{
    private BeastmasterEntryAttempt? entryAttempt;
    private Hook<ContentsFinderQueueInfo.Delegates.OnQueueWithdrawn>? entryWithdrawalHook;
    private bool entryPromptListener;
    private bool EntryPending => entryAttempt?.Pending == true;

    private static bool NativeQueueIsIdle()
    {
        var finder = ContentsFinder.Instance();
        return finder != null && finder->QueueInfo.QueueState == ContentsFinderQueueState.None &&
            !Svc.Condition[ConditionFlag.InDutyQueue] && !Svc.Condition[ConditionFlag.WaitingForDutyFinder] &&
            !Svc.Condition[ConditionFlag.WaitingForDuty] && !CrucibleUi.IsVisible("ContentsFinderConfirm");
    }

    private static bool QueueMatches(BeastmasterEntryAttempt attempt, bool requirePop = false)
    {
        var finder = ContentsFinder.Instance();
        if (finder == null || finder->QueueInfo.QueueState is ContentsFinderQueueState.None or ContentsFinderQueueState.InContent)
            return false;
        var found = false;
        foreach (var queued in finder->QueueInfo.QueuedEntries)
        {
            if (queued.Id == 0) continue;
            if (queued.ContentType != ContentsType.Regular || queued.Id != attempt.Duty) return false;
            found = true;
        }
        var popped = finder->QueueInfo.QueueState is ContentsFinderQueueState.Ready or ContentsFinderQueueState.Accepted ||
            CrucibleUi.IsVisible("ContentsFinderConfirm");
        if (popped)
        {
            var entry = finder->QueueInfo.PoppedQueueEntry;
            if (entry.ContentType != ContentsType.Regular || entry.Id != attempt.Duty) return false;
            found = true;
        }
        return found && (!requirePop || popped);
    }

    private void ObserveQueueWithdrawal(ContentsFinderQueueInfo* queue, uint message, ulong content)
    {
        var attempt = entryAttempt;
        var matching = false;
        var sameCharacter = false;
        try
        {
            var finder = ContentsFinder.Instance();
            matching = attempt != null && finder != null && queue == &finder->QueueInfo &&
                (QueueMatches(attempt) || attempt.MatchingQueueObserved && !attempt.ConflictingQueueObserved && NativeQueueIsIdle());
            sameCharacter = attempt != null && Svc.ClientState.IsLoggedIn && Svc.PlayerState.IsLoaded &&
                Svc.PlayerState.ContentId == attempt.Character;
        }
        catch { /* Observation must never prevent the game's receive handler. */ }
        entryWithdrawalHook!.Original(queue, message, content);
        if (!matching && sameCharacter && attempt is { MatchingQueueObserved: true, ConflictingQueueObserved: false })
        {
            try
            {
                var finder = ContentsFinder.Instance();
                matching = finder != null && queue == &finder->QueueInfo && NativeQueueIsIdle();
            }
            catch { /* Retain ownership if receive-side identity cannot be verified. */ }
        }
        attempt?.ObserveWithdrawal(matching, sameCharacter);
    }

    private bool PrepareEntryObservation(out string reason)
    {
        reason = "Crucible entry observation is unavailable.";
        if (EntryPending || !NativeQueueIsIdle()) return false;
        if (entryWithdrawalHook != null) { reason = string.Empty; return true; }
        var address = (nint)ContentsFinderQueueInfo.MemberFunctionPointers.OnQueueWithdrawn;
        if (address == 0) return false;
        try
        {
            // SDK receive-side binding: OnQueueWithdrawn runs on a withdrawal packet.
            entryWithdrawalHook = Plugin.GameInterop.HookFromAddress<ContentsFinderQueueInfo.Delegates.OnQueueWithdrawn>(
                address, ObserveQueueWithdrawal);
            entryWithdrawalHook.Enable();
            Svc.AddonLifecycle.RegisterListener(AddonEvent.PreReceiveEvent, CrucibleUi.YesNo, ObserveEntryPromptEvent);
            entryPromptListener = true;
            Svc.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, CrucibleUi.YesNo, ObserveEntryPromptSetup);
            reason = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            if (entryPromptListener)
            {
                Svc.AddonLifecycle.UnregisterListener(AddonEvent.PreReceiveEvent, CrucibleUi.YesNo, ObserveEntryPromptEvent);
                Svc.AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, CrucibleUi.YesNo, ObserveEntryPromptSetup);
                entryPromptListener = false;
            }
            entryWithdrawalHook?.Dispose(); entryWithdrawalHook = null;
            Svc.Log.Error(ex, "[Beastmaster] Entry observation could not be prepared");
            return false;
        }
    }

    private void ObserveEntryPromptSetup(AddonEvent kind, AddonArgs args)
    {
        if (entryAttempt is not { Pending: true } attempt || !CanObserveCharacter || Svc.PlayerState.ContentId != attempt.Character) return;
        // A native setup is a new modal generation even when a chained warning
        // reuses both the same addon address and the same localized text.
        attempt.ObservePrompt(false);
        attempt.ObservePrompt(true);
    }

    private void ObserveEntryPromptEvent(AddonEvent kind, AddonArgs args)
    {
        try
        {
            if (entryAttempt is not { Pending: true } attempt || args is not AddonReceiveEventArgs receive ||
                !CanObserveCharacter || Svc.PlayerState.ContentId != attempt.Character ||
                !CrucibleUi.TryPrompt(17787, out var prompt, finalLineOnly: true) || args.Addon.Address != (nint)prompt)
                return;
            var no = ((AddonSelectYesno*)prompt)->NoButton;
            if (no == null || !no->IsEnabled || no->AtkComponentBase.OwnerNode == null) return;
            // Identify the negative action through that button's registered native
            // event, without guessing a callback number or interpreting idle as No.
            var count = 0;
            var receivedEvent = (AtkEvent*)receive.AtkEvent;
            for (var evt = no->AtkComponentBase.OwnerNode->AtkResNode.AtkEventManager.Event; evt != null && count++ < 32; evt = evt->NextEvent)
            {
                if (evt->State.EventType == AtkEventType.ButtonClick && evt->Listener == (AtkEventListener*)prompt &&
                    (int)receive.AtkEventType == (int)evt->State.EventType && receive.EventParam == (int)evt->Param &&
                    (receivedEvent == evt || receivedEvent != null && evt->Target != null && receivedEvent->Target == evt->Target))
                {
                    attempt.ObserveUserDecline(matchingPrompt: true, negativeButton: true);
                    break;
                }
            }
        }
        catch (Exception ex) { Svc.Log.Error(ex, "[Beastmaster] Entry prompt observation failed; entry remains owned"); }
    }

    private void ReconcileEntry()
    {
        if (entryAttempt is not { } attempt) return;
        attempt.ObserveSession(Svc.ClientState.IsLoggedIn, Svc.PlayerState.IsLoaded, Svc.PlayerState.ContentId);
        if (Svc.PlayerState.IsLoaded && Svc.PlayerState.ContentId == attempt.Character && BeastmasterPlayer.IsValid)
            attempt.ObserveEntry(Svc.ClientState.TerritoryType);
        attempt.ObservePrompt(CrucibleUi.IsVisible(CrucibleUi.YesNo));
        if (attempt.Pending && CanObserveCharacter && Svc.PlayerState.ContentId == attempt.Character)
        {
            var matches = QueueMatches(attempt);
            attempt.ObserveQueue(matches, otherQueue: !matches && !NativeQueueIsIdle());
            attempt.ObserveDeclinedPrompt(CrucibleUi.IsVisible(CrucibleUi.YesNo), NativeQueueIsIdle());
            attempt.ObserveIdle(BeastmasterPlayer.IsValid && !IsCrucibleTerritory(Svc.ClientState.TerritoryType) &&
                !Svc.Condition[ConditionFlag.BoundByDuty] && !CrucibleUi.IsVisible(CrucibleUi.YesNo) && NativeQueueIsIdle(), DateTime.UtcNow);
        }
        if (attempt.Pending && attempt.StopRequested && BeastmasterPlayer.IsValid && Svc.ClientState.IsLoggedIn && Svc.PlayerState.IsLoaded &&
            Svc.PlayerState.ContentId == attempt.Character)
        {
            attempt.ObserveDeclinedPrompt(CrucibleUi.IsVisible(CrucibleUi.YesNo), NativeQueueIsIdle());
            if (CrucibleUi.TryPrompt(17787, out var prompt, finalLineOnly: true) &&
                ((AddonSelectYesno*)prompt)->NoButton != null && ((AddonSelectYesno*)prompt)->NoButton->IsEnabled &&
                attempt.TryDeclineBoardPrompt(CrucibleUi.PromptText(prompt)))
                CrucibleUi.Screens.Prompt.No(prompt);
        }
        if (attempt.Pending && attempt.StopRequested && BeastmasterPlayer.IsValid && Svc.ClientState.IsLoggedIn && Svc.PlayerState.IsLoaded &&
            Svc.PlayerState.ContentId == attempt.Character && attempt.TryCancel(QueueMatches(attempt)))
        {
            try { ContentsFinder.Instance()->QueueInfo.CancelQueue(); }
            catch (Exception ex) { Svc.Log.Error(ex, "[Beastmaster] Entry cancellation will be rechecked in five seconds"); }
        }
        if (attempt.Pending) return;
        if (attempt.Outcome == BeastmasterEntryOutcome.Withdrawn && State == RunState.WaitingDuty)
        {
            entryAttempt = null;
            if (attempt.StopRequested) Transition(RunState.Idle, "Stopped");
            else BeginRegistration();
            return;
        }
        entryAttempt = null;
        if ((State is RunState.Idle or RunState.Complete or RunState.Failed) && !combat.OwnsState && !refreshCleanupPending)
        { owner = 0; runData = null; runSettings = null; }
    }
}
