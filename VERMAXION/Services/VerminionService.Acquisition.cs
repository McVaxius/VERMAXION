using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using VERMAXION.Models;

namespace VERMAXION.Services;

public sealed partial class VerminionService
{
    private DateTime nextAcquisitionObservation;
    private bool acquisitionStopSent;
    private ulong acquisitionObservedOwner;
    private VerminionQuestAcquisition? preparedAcquisition;
    public bool HasQuestAcquisition => configManager.GetActiveConfig().VerminionProgress.QuestAcquisition != null;

    // Called only from the idle manual handoff. Questionable owns all quest
    // steps and duties. This service remains inactive, so DAD can claim work.
    internal void AcquireMinion(ushort minionId)
    {
        if (IsActive || HasQuestAcquisition) return;
        var config = configManager.GetActiveConfig();
        var progress = config.VerminionProgress;
        var route = VerminionRoster.AcquisitionQuests(minionId);
        try
        {
            if (route.Length == 0 || VerminionGameInteraction.OwnsMinion(minionId) != false)
                throw new InvalidOperationException("No missing minion with a supported quest acquisition route was selected.");
            if (Plugin.PlayerState.ContentId == 0 || string.IsNullOrEmpty(configManager.CurrentCharacterKey) ||
                !GameHelpers.IsPlayerAvailable() || condition[ConditionFlag.InCombat] ||
                condition[ConditionFlag.BoundByDuty] || condition[ConditionFlag.BoundByDuty56] ||
                condition[ConditionFlag.InDutyQueue] || condition[ConditionFlag.WaitingForDutyFinder] ||
                progress.PendingMatch != 0 || progress.PendingPurchase != null ||
                !lifestream.TryReadBusy(out var busy) || busy)
                throw new InvalidOperationException("Character, duty, purchase or travel work is unresolved.");
            ReconcileLegacyAcquisitionMetadata();
            if (progress.UnlockPriorityInserted)
                throw new InvalidOperationException("Release the previous unlock priority in Questionable before starting this handoff.");
            if (new ushort[] { 434, 435, 1431 }.Any(id => !QuestManager.IsQuestComplete(id)))
                throw new InvalidOperationException("Finish Verminion's Gold Saucer prerequisites first.");

            // The installed WigglyQuest provider exposes the verified native
            // priority/stop controls. Do not invent those APIs for stock Q.
            const string provider = "WigglyQuest";
            if (ChocoboRaceService.QuestIpcPrefix != provider)
                throw new InvalidOperationException("This acquisition needs WigglyQuest's native priority and quest-stop controls.");
            if (QuestCall<bool>(provider, "IsRunning") ||
                QuestCall<List<string>>(provider, "GetPriorityQuests").Count != 0)
                throw new InvalidOperationException("Questionable already has a run or priority list. Finish or clear it before handing over minion acquisition.");
            if (!QuestCall<bool>(provider, "GetStopConditionsEnabled") ||
                QuestCall<List<string>>(provider, "GetStopQuestList").Contains(route[^1].ToString()))
                throw new InvalidOperationException("Enable Questionable stop conditions and remove any existing stop for this reward quest before handing over acquisition.");

            var remaining = route.Where(id => !QuestManager.IsQuestComplete(id)).ToArray();
            if (remaining.Length == 0 || QuestManager.IsQuestComplete(route[^1]))
                throw new InvalidOperationException("The reward quest is already complete. Register its minion item, then Resume Verminion.");
            var blacklisted = QuestCall<List<string>>(provider, "GetBlacklistedQuests");
            if (remaining.FirstOrDefault(id => blacklisted.Contains(id.ToString())) is var blockedQuest && blockedQuest != 0)
                throw new InvalidOperationException($"{AcquisitionQuestName(blockedQuest)} is blacklisted in Questionable. Remove that quest from its blacklist before starting acquisition.");
            var first = remaining[0];
            if (!Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Quest>().TryGetRow((uint)first + 65536, out var quest) ||
                VerminionGameInteraction.ReadUnsyncedJobLevel() is not { } level || level < quest.ClassJobLevel[0])
                throw new InvalidOperationException("The current job does not meet the acquisition quest's level requirement, or its level is unavailable.");
            if (Plugin.PluginInterface.GetIpcSubscriber<string, bool>($"{provider}.IsQuestLocked").InvokeFunc(first.ToString()))
                throw new InvalidOperationException($"{quest.Name} is locked. Complete its existing level/MSQ prerequisites first.");
            if (!VerminionGameInteraction.TryReadMinionInventory(VerminionRoster.MammetOffer.ItemId, out var gil, out _, out _) ||
                gil < config.VerminionGilReserve + 1000UL)
                throw new InvalidOperationException("Quest travel needs at least 1,000 gil above the configured minimum balance.");

            // Reserve only changes proven absent above, before any native write.
            // An uncertain submission can be observed or cancelled, never replayed.
            config.VerminionPaused = true;
            progress.QuestAcquisition = new(Plugin.PlayerState.ContentId, minionId, provider, remaining);
            SaveAcquisition();
            SetState(VerminionState.Idle, "Handing minion acquisition to Questionable");
            if (!Plugin.PluginInterface.GetIpcSubscriber<string, bool>($"{provider}.AddStopQuest").InvokeFunc(route[^1].ToString()))
                throw new InvalidOperationException("Questionable rejected the reward quest stop.");
            foreach (var id in remaining)
                if (!Plugin.PluginInterface.GetIpcSubscriber<string, bool>($"{provider}.AddQuestPriority").InvokeFunc(id.ToString()))
                    throw new InvalidOperationException("Questionable rejected an acquisition priority.");
            if (!QuestCall<List<string>>(provider, "GetPriorityQuests").SequenceEqual(remaining.Select(id => id.ToString())) ||
                !QuestCall<List<string>>(provider, "GetStopQuestList").Contains(route[^1].ToString()) ||
                !QuestCall<bool>(provider, "GetStopConditionsEnabled"))
                throw new InvalidOperationException("Questionable priority/stop readback disagreed; no quest submitted.");
            acquisitionStopSent = false;
            preparedAcquisition = progress.QuestAcquisition;
            nextAcquisitionObservation = DateTime.UtcNow.AddSeconds(3);
            // The provider resolves newly inserted priorities on its own update.
            // Starting an accepted quest in this same frame can run its old selection.
            reason = $"Waiting for Questionable to select {AcquisitionQuestName(first)} before starting acquisition. Verminion is paused.";
        }
        catch (Exception ex)
        {
            reason = $"Minion acquisition blocked: {ex.Message}";
            log.Warning($"[Verminion] {reason}");
        }
    }

    private static T QuestCall<T>(string provider, string method) =>
        Plugin.PluginInterface.GetIpcSubscriber<T>($"{provider}.{method}").InvokeFunc();

    private void ReconcileLegacyAcquisitionMetadata()
    {
        var progress = configManager.GetActiveConfig().VerminionProgress;
        if (!VerminionRoster.GentlemanQuests.Contains(progress.UnlockQuestId)) return;
        if (progress.UnlockPriorityInserted &&
            (progress.UnlockQuestProvider != "WigglyQuest" ||
             Plugin.PluginInterface.GetIpcSubscriber<string, bool>("WigglyQuest.IsQuestInPriority")
                 .InvokeFunc(progress.UnlockQuestId.ToString())))
            throw new InvalidOperationException("Release the previous Hildibrand priority in Questionable before starting Verminion again.");
        // Only discard the old claim after its priority is absent. Do not stop,
        // adopt or modify the separately launched native Questionable chain.
        progress.UnlockPriorityInserted = false;
        progress.UnlockQuestId = 0;
        progress.UnlockQuestProvider = string.Empty;
        SaveAcquisition();
    }

    private static string AcquisitionQuestName(ushort id) =>
        Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Quest>().TryGetRow((uint)id + 65536, out var quest)
            ? quest.Name.ToString() : "the acquisition quest";

    private void SaveAcquisition()
    {
        if (!configManager.TrySaveAccount(configManager.CurrentAccountId))
            throw new InvalidOperationException("Acquisition ownership could not be saved; no further submission allowed.");
    }

    internal void CancelQuestAcquisition()
    {
        var config = configManager.GetActiveConfig();
        if (config.VerminionProgress.QuestAcquisition is not { } handoff || handoff.Owner != Plugin.PlayerState.ContentId) return;
        config.VerminionPaused = true;
        config.VerminionProgress.QuestAcquisition = handoff with { CancellationRequested = true };
        if (!configManager.TrySaveAccount(configManager.CurrentAccountId))
            log.Warning("[Verminion] Acquisition cancellation is active in memory but could not be saved; reload persistence is unverified.");
        nextAcquisitionObservation = DateTime.MinValue;
        UpdateQuestAcquisition();
    }

    private void UpdateQuestAcquisition()
    {
        var config = configManager.GetActiveConfig();
        var progress = config.VerminionProgress;
        if (progress.QuestAcquisition is not { } handoff || Plugin.PlayerState.ContentId == 0 ||
            handoff.Owner != Plugin.PlayerState.ContentId || DateTime.UtcNow < nextAcquisitionObservation) return;
        nextAcquisitionObservation = DateTime.UtcNow.AddSeconds(2);
        if (acquisitionObservedOwner != handoff.Owner)
        { acquisitionObservedOwner = handoff.Owner; acquisitionStopSent = false; }
        try
        {
            if (handoff.Provider != "WigglyQuest" || handoff.Quests.Length == 0 ||
                handoff.RewardQuest != VerminionRoster.AcquisitionQuests(handoff.MinionId).LastOrDefault() ||
                handoff.Quests.Any(id => !VerminionRoster.AcquisitionQuests(handoff.MinionId).Contains(id)))
                throw new InvalidOperationException("Saved acquisition does not match a supported route; no provider action taken.");
            var running = QuestCall<bool>(handoff.Provider, "IsRunning");
            var current = QuestCall<string?>(handoff.Provider, "GetCurrentQuestId");
            var rewardComplete = QuestManager.IsQuestComplete(handoff.RewardQuest);
            var owned = handoff.OwnsQuest(Plugin.PlayerState.ContentId, current);
            if (running)
            {
                if (!owned && !string.IsNullOrEmpty(current) && !handoff.OwnershipReleased)
                {
                    // Once another quest takes over, later matching IDs do not
                    // restore our right to stop that independently started run.
                    progress.QuestAcquisition = handoff with { OwnershipReleased = true };
                    SaveAcquisition();
                }
                if (owned && (handoff.CancellationRequested || rewardComplete))
                {
                    if (!acquisitionStopSent)
                    {
                        acquisitionStopSent = true;
                        commandManager.ProcessCommand("/wqst stop");
                    }
                    reason = "Waiting for Questionable to stop the owned acquisition; Verminion stays paused.";
                }
                else
                    reason = owned
                        ? $"Questionable acquisition: {AcquisitionQuestName(ushort.Parse(current!))}; {handoff.Quests.Count(id => QuestManager.IsQuestComplete(id))}/{handoff.Quests.Length} route quests complete. Verminion is paused."
                        : "Questionable is running outside the saved acquisition. Waiting for it to stop before releasing owned settings.";
                return;
            }

            string? dispatchBlocker = null;
            if (!handoff.DispatchAttempted && !handoff.CancellationRequested)
            {
                var first = handoff.Quests[0].ToString();
                if (!ReferenceEquals(preparedAcquisition, handoff))
                    dispatchBlocker = "Minion acquisition preparation was interrupted. No quest started; use Acquire to start a new handoff.";
                else if (current != first ||
                    !QuestCall<List<string>>(handoff.Provider, "GetPriorityQuests").SequenceEqual(handoff.Quests.Select(id => id.ToString())) ||
                    !QuestCall<bool>(handoff.Provider, "GetStopConditionsEnabled") ||
                    !QuestCall<List<string>>(handoff.Provider, "GetStopQuestList").Contains(handoff.RewardQuest.ToString()) ||
                    QuestCall<List<string>>(handoff.Provider, "GetBlacklistedQuests").Any(id => handoff.Quests.Any(quest => quest.ToString() == id)))
                    dispatchBlocker = "Questionable did not select the requested acquisition quest with its priority and reward stop intact. No quest started; review its selection and settings before trying again.";
                else
                {
                    preparedAcquisition = null;
                    progress.QuestAcquisition = handoff with { DispatchAttempted = true };
                    SaveAcquisition();
                    var accepted = Plugin.PluginInterface.GetIpcSubscriber<string, bool>($"{handoff.Provider}.StartQuest").InvokeFunc(first);
                    reason = accepted
                        ? $"Questionable is acquiring the required minion; native stop after {AcquisitionQuestName(handoff.RewardQuest)}. Verminion is paused."
                        : "Questionable rejected acquisition; no automatic retry. Reconciling owned settings.";
                    log.Information($"[Verminion] Native minion acquisition submitted; minion={handoff.MinionId}; first={first}; reward={handoff.RewardQuest}; accepted={accepted}");
                    return;
                }
            }

            // Never resume battle work just because Q stopped. Native reward
            // completion proves the quest only; registration is checked on Resume.
            foreach (var id in handoff.Quests)
            {
                var key = id.ToString();
                if (QuestCall<List<string>>(handoff.Provider, "GetPriorityQuests").Contains(key) &&
                    !Plugin.PluginInterface.GetIpcSubscriber<string, bool>($"{handoff.Provider}.RemovePriorityQuest").InvokeFunc(key))
                    throw new InvalidOperationException("Questionable could not release an owned priority; cleanup remains pending.");
            }
            var rewardKey = handoff.RewardQuest.ToString();
            if (QuestCall<List<string>>(handoff.Provider, "GetStopQuestList").Contains(rewardKey) &&
                !Plugin.PluginInterface.GetIpcSubscriber<string, bool>($"{handoff.Provider}.RemoveStopQuest").InvokeFunc(rewardKey))
                throw new InvalidOperationException("Questionable could not release the owned reward stop; cleanup remains pending.");
            if (QuestCall<List<string>>(handoff.Provider, "GetPriorityQuests").Any(id => handoff.Quests.Any(quest => quest.ToString() == id)) ||
                QuestCall<List<string>>(handoff.Provider, "GetStopQuestList").Contains(rewardKey))
                throw new InvalidOperationException("Questionable cleanup readback disagreed; the handoff remains reserved.");
            config.VerminionPaused = true;
            preparedAcquisition = null;
            progress.QuestAcquisition = null;
            if (!configManager.TrySaveAccount(configManager.CurrentAccountId))
            {
                progress.QuestAcquisition = handoff;
                throw new InvalidOperationException("Acquisition cleanup could not be saved.");
            }
            reason = dispatchBlocker ?? (handoff.CancellationRequested ? "Minion acquisition cancelled. Verminion remains paused." :
                rewardComplete ? "Minion reward quest completion verified. Resume Verminion to register the reward and continue." :
                "Questionable stopped before the reward quest completed. Acquisition is unresolved; use Acquire to continue after reviewing its progress.");
            log.Information($"[Verminion] {reason}");
        }
        catch (Exception ex)
        {
            // Provider absence/errors never trigger a replacement run or credit.
            reason = $"Minion acquisition observation/cleanup pending: {ex.Message}";
        }
    }
}
