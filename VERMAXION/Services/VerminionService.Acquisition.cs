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
            if (new ushort[] { 434, 435, 1431 }.Any(id => !QuestManager.IsQuestComplete(id)))
                throw new InvalidOperationException("Finish Verminion's Gold Saucer prerequisites first.");

            // The installed WigglyQuest provider exposes the verified native
            // priority/stop controls. Do not invent those APIs for stock Q.
            const string provider = "WigglyQuest";
            if (ChocoboRaceService.QuestIpcPrefix != provider)
                throw new InvalidOperationException("This acquisition needs WigglyQuest's native priority and quest-stop controls.");
            RequireQuestAcquisitionIpc(provider);
            ReconcileLegacyAcquisitionMetadata();
            if (progress.UnlockPriorityInserted)
                throw new InvalidOperationException("Release the previous unlock priority in Questionable before starting this handoff.");
            if (QuestCall<bool>(provider, "IsRunning") ||
                AcquisitionQuestList(provider, "Priority.Get").Count != 0)
                throw new InvalidOperationException("Questionable already has a run or priority list. Finish or clear it before handing over minion acquisition.");
            var stops = AcquisitionStopSettings(provider);
            if (!stops.Enabled || AcquisitionRewardStop(stops, route[^1]) != null)
                throw new InvalidOperationException("Enable Questionable stop conditions and remove any existing stop for this reward quest before handing over acquisition.");

            var remaining = route.Where(id => !QuestManager.IsQuestComplete(id)).ToArray();
            if (remaining.Length == 0 || QuestManager.IsQuestComplete(route[^1]))
                throw new InvalidOperationException("The reward quest is already complete. Register its minion item, then Resume Verminion.");
            var blacklisted = AcquisitionQuestList(provider, "Blacklist.Get");
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
            SetAcquisitionRewardStop(provider, route[^1], 1);
            foreach (var id in remaining)
                if (!Plugin.PluginInterface.GetIpcSubscriber<string, bool>($"{provider}.AddQuestPriority").InvokeFunc(id.ToString()))
                    throw new InvalidOperationException("Questionable rejected an acquisition priority.");
            if (!AcquisitionQuestList(provider, "Priority.Get").SequenceEqual(remaining.Select(id => id.ToString())) ||
                !HasAcquisitionRewardStop(provider, route[^1]))
                throw new InvalidOperationException("Questionable priority/stop readback disagreed; no quest submitted.");
            acquisitionStopSent = false;
            preparedAcquisition = progress.QuestAcquisition;
            nextAcquisitionObservation = DateTime.UtcNow.AddSeconds(3);
            // Recheck native selection/readiness before the one-shot targeted dispatch.
            // Idle Questionable need not resolve priorities while its quest window is closed.
            reason = $"Preparing targeted Questionable dispatch for {AcquisitionQuestName(first)}. Verminion is paused.";
            log.Information($"[Verminion] Native minion acquisition prepared; minion={minionId}; first={first}; reward={route[^1]}; priorities={remaining.Length}; reward stop=Pause after quest completion; IPC=3");
        }
        catch (Exception ex)
        {
            reason = $"Minion acquisition blocked: {ex.Message}";
            log.Warning($"[Verminion] {reason}");
        }
    }

    private static T QuestCall<T>(string provider, string method) =>
        Plugin.PluginInterface.GetIpcSubscriber<T>($"{provider}.{method}").InvokeFunc();

    private static void RequireQuestAcquisitionIpc(string provider)
    {
        var version = QuestCall<(int, int)>(provider, "ApiVersion");
        if (version.Item1 != 3)
            throw new InvalidOperationException($"Minion acquisition requires Questionable IPC 3.x; detected {version.Item1}.{version.Item2}.");
    }

    private static List<string> AcquisitionQuestList(string provider, string method) =>
        ValidateAcquisitionQuestList(QuestCall<List<string>>(provider, method), method);

    private static List<string> ValidateAcquisitionQuestList(List<string>? quests, string method)
    {
        if (quests == null || quests.Any(string.IsNullOrWhiteSpace) || quests.Distinct(StringComparer.Ordinal).Count() != quests.Count)
            throw new InvalidOperationException($"Questionable {method} returned an unavailable or ambiguous quest list.");
        return quests;
    }

    // IPC 3.x DTO fields match the provider's public wire contract.
    private sealed class AcquisitionStopData
    {
        public AcquisitionStopData() { }
        [Newtonsoft.Json.JsonProperty(Required = Newtonsoft.Json.Required.Always)]
        [System.Text.Json.Serialization.JsonRequired]
        public bool Enabled { get; set; }
        [Newtonsoft.Json.JsonProperty(Required = Newtonsoft.Json.Required.Always)]
        [System.Text.Json.Serialization.JsonRequired]
        public List<AcquisitionStopEntry>? Conditions { get; set; }
    }

    private sealed class AcquisitionStopEntry
    {
        public AcquisitionStopEntry() { }
        [Newtonsoft.Json.JsonProperty(Required = Newtonsoft.Json.Required.Always)]
        [System.Text.Json.Serialization.JsonRequired]
        public string Type { get; set; } = string.Empty;
        [Newtonsoft.Json.JsonProperty(Required = Newtonsoft.Json.Required.Always)]
        [System.Text.Json.Serialization.JsonRequired]
        public int Mode { get; set; } = -1;
        public int? Value { get; set; }
        public string? QuestId { get; set; }
        public int? Sequence { get; set; }
    }

    private sealed class AcquisitionStepData
    {
        public AcquisitionStepData() { }
        [Newtonsoft.Json.JsonProperty(Required = Newtonsoft.Json.Required.Always)]
        [System.Text.Json.Serialization.JsonRequired]
        public string QuestId { get; set; } = string.Empty;
        [Newtonsoft.Json.JsonProperty(Required = Newtonsoft.Json.Required.Always)]
        [System.Text.Json.Serialization.JsonRequired]
        public byte Sequence { get; set; }
    }

    private static bool AcquisitionSelectionMatches(string first, string? current, bool firstAccepted,
        bool firstReadyToAccept, AcquisitionStepData? step, byte nativeSequence)
    {
        if (string.IsNullOrEmpty(first)) return false;
        if (current != first)
            return current == null && !firstAccepted && firstReadyToAccept;
        return firstAccepted
            ? step != null && step.QuestId == first && step.Sequence == nativeSequence
            : firstReadyToAccept;
    }

    private static AcquisitionStopData AcquisitionStopSettings(string provider) =>
        ValidateAcquisitionStopSettings(QuestCall<AcquisitionStopData>(provider, "StopConditions.Get"));

    private static AcquisitionStopData ValidateAcquisitionStopSettings(AcquisitionStopData? settings)
    {
        if (settings?.Conditions == null || settings.Conditions.Any(entry => entry == null ||
                string.IsNullOrWhiteSpace(entry.Type) || entry.Mode is < 0 or > 2 ||
                (entry.Type is "QuestComplete" or "QuestAccept") && string.IsNullOrWhiteSpace(entry.QuestId)) ||
            settings.Conditions.GroupBy(entry => (entry.Type,
                entry.Type is "QuestComplete" or "QuestAccept" ? entry.QuestId : null)).Any(group => group.Count() > 1))
            throw new InvalidOperationException("Questionable stop conditions are unavailable; no stop settings changed.");
        return settings;
    }

    private static AcquisitionStopEntry? AcquisitionRewardStop(AcquisitionStopData settings, ushort reward)
    {
        var entries = settings.Conditions!.Where(entry => entry.Type == "QuestComplete" && entry.QuestId == reward.ToString()).ToArray();
        if (entries.Length > 1)
            throw new InvalidOperationException("Questionable has duplicate reward quest stops; no stop settings changed.");
        return entries.SingleOrDefault();
    }

    private static bool IsAcquisitionRewardStop(AcquisitionStopEntry? entry) =>
        entry is { Mode: 1, Sequence: null, Value: null };

    private static bool HasAcquisitionRewardStop(string provider, ushort reward)
    {
        var settings = AcquisitionStopSettings(provider);
        return settings.Enabled && IsAcquisitionRewardStop(AcquisitionRewardStop(settings, reward));
    }

    private static void SetAcquisitionRewardStop(string provider, ushort reward, int mode)
    {
        // Pause stops native automation without invoking the user's CommandAfterStop.
        // A null Sequence means completion of the whole reward quest; Off removes only this key.
        var result = Plugin.PluginInterface.GetIpcSubscriber<AcquisitionStopEntry, (bool, string?)>($"{provider}.StopConditions.Set")
            .InvokeFunc(new() { Type = "QuestComplete", QuestId = reward.ToString(), Mode = mode });
        if (!result.Item1)
            throw new InvalidOperationException($"Questionable rejected the reward quest stop change: {result.Item2 ?? "no error detail"}.");
    }

    private void ReconcileLegacyAcquisitionMetadata()
    {
        var progress = configManager.GetActiveConfig().VerminionProgress;
        if (!VerminionRoster.GentlemanQuests.Contains(progress.UnlockQuestId)) return;
        RequireQuestAcquisitionIpc("WigglyQuest");
        if (progress.UnlockPriorityInserted &&
            (progress.UnlockQuestProvider != "WigglyQuest" ||
             AcquisitionQuestList("WigglyQuest", "Priority.Get").Contains(progress.UnlockQuestId.ToString())))
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
            RequireQuestAcquisitionIpc(handoff.Provider);
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
                else
                {
                    var prioritiesMatch = AcquisitionQuestList(handoff.Provider, "Priority.Get").SequenceEqual(handoff.Quests.Select(id => id.ToString()));
                    var rewardStopMatches = HasAcquisitionRewardStop(handoff.Provider, handoff.RewardQuest);
                    var blacklisted = AcquisitionQuestList(handoff.Provider, "Blacklist.Get").Any(id => handoff.Quests.Any(quest => quest.ToString() == id));
                    var firstAccepted = Plugin.PluginInterface.GetIpcSubscriber<string, bool>($"{handoff.Provider}.IsQuestAccepted").InvokeFunc(first);
                    var firstReady = Plugin.PluginInterface.GetIpcSubscriber<string, bool>($"{handoff.Provider}.IsReadyToAcceptQuest").InvokeFunc(first);
                    var step = firstAccepted && current == first
                        ? QuestCall<AcquisitionStepData?>(handoff.Provider, "GetCurrentStepData") : null;
                    var nativeSequence = firstAccepted ? QuestManager.GetQuestSequence(handoff.Quests[0]) : (byte)0;
                    var selectionMatches = AcquisitionSelectionMatches(first, current, firstAccepted, firstReady, step, nativeSequence);
                    log.Information($"[Verminion] Acquisition dispatch preflight; first={first}; current={current ?? "none"}; firstAccepted={firstAccepted}; readyToAccept={firstReady}; selectionMatches={selectionMatches}; reportedSequence={step?.Sequence}; nativeSequence={nativeSequence}; prioritiesExact={prioritiesMatch}; rewardStopExact={rewardStopMatches}; routeBlacklisted={blacklisted}");
                    if (!selectionMatches || !prioritiesMatch || !rewardStopMatches || blacklisted)
                        dispatchBlocker = "Questionable acquisition preflight disagreed. No quest started; review its quest readiness, selection, priorities, blacklist and reward stop before trying again.";
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
            }

            // Never resume battle work just because Q stopped. Native reward
            // completion proves the quest only; registration is checked on Resume.
            foreach (var id in handoff.Quests)
            {
                var key = id.ToString();
                if (AcquisitionQuestList(handoff.Provider, "Priority.Get").Contains(key))
                {
                    var removed = Plugin.PluginInterface.GetIpcSubscriber<string, (bool, string?)>($"{handoff.Provider}.Priority.Remove").InvokeFunc(key);
                    if (!removed.Item1)
                        throw new InvalidOperationException($"Questionable could not release an owned priority ({removed.Item2}); cleanup remains pending.");
                }
            }
            var rewardStop = AcquisitionRewardStop(AcquisitionStopSettings(handoff.Provider), handoff.RewardQuest);
            if (rewardStop != null)
            {
                if (!IsAcquisitionRewardStop(rewardStop))
                    throw new InvalidOperationException("Questionable's owned reward stop was changed; cleanup remains pending without modifying it.");
                SetAcquisitionRewardStop(handoff.Provider, handoff.RewardQuest, 0);
            }
            if (AcquisitionQuestList(handoff.Provider, "Priority.Get").Any(id => handoff.Quests.Any(quest => quest.ToString() == id)) ||
                AcquisitionRewardStop(AcquisitionStopSettings(handoff.Provider), handoff.RewardQuest) != null)
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
