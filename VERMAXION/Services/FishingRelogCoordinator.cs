using System;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using VERMAXION.IPC;
using VERMAXION.Models;

namespace VERMAXION.Services;

public sealed class FishingRelogCoordinator
{
    private static readonly TimeSpan MultiModeCommandSettleDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan WaitLogInterval = TimeSpan.FromSeconds(15);

    private readonly IPluginLog log;
    private readonly ARPostProcessService arPostProcessService;
    private readonly AutoRetainerIPC autoRetainerIPC;
    private readonly Configuration configuration;
    private readonly ConfigManager configManager;

    private FishingRelogState state = FishingRelogState.Idle;
    private string targetCharacterKey = string.Empty;
    private string sourceCharacterKey = string.Empty;
    private DateTimeOffset startedAtUtc;
    private DateTimeOffset? lastRelogCommandAtUtc;
    private DateTimeOffset? multiModeCommandSentAtUtc;
    private DateTimeOffset lastWaitLogAtUtc;
    private string lastWaitReason = string.Empty;
    private int relogAttempts;
    private bool observableProgress;
    private string failureReason = string.Empty;

    public bool IsActive => state is not FishingRelogState.Idle and not FishingRelogState.Failed;
    public bool IsFailed => state == FishingRelogState.Failed;
    public string FailureReason => failureReason;
    public string StatusText { get; private set; } = "Idle";

    public FishingRelogCoordinator(
        IPluginLog log,
        ARPostProcessService arPostProcessService,
        AutoRetainerIPC autoRetainerIPC,
        Configuration configuration,
        ConfigManager configManager)
    {
        this.log = log;
        this.arPostProcessService = arPostProcessService;
        this.autoRetainerIPC = autoRetainerIPC;
        this.configuration = configuration;
        this.configManager = configManager;
    }

    public bool RequestRelog(string characterKey)
    {
        if (IsActive)
            return false;

        targetCharacterKey = characterKey.Trim();
        if (string.IsNullOrWhiteSpace(targetCharacterKey))
            return false;

        sourceCharacterKey = GetCurrentCharacterKey();
        startedAtUtc = DateTimeOffset.UtcNow;
        lastRelogCommandAtUtc = null;
        multiModeCommandSentAtUtc = null;
        lastWaitLogAtUtc = default;
        lastWaitReason = string.Empty;
        relogAttempts = 0;
        observableProgress = false;
        failureReason = string.Empty;
        state = FishingRelogState.FinishingPostprocess;
        StatusText = $"Preparing relog to {targetCharacterKey}";
        log.Information($"[Fishing][Relog] Starting observed relog sequence for {targetCharacterKey}; source={sourceCharacterKey}");
        return true;
    }

    public void NotifyCharacterChanged(string newCharacterKey)
    {
        if (!IsActive)
            return;

        if (!string.IsNullOrWhiteSpace(newCharacterKey) &&
            !string.Equals(newCharacterKey, sourceCharacterKey, StringComparison.OrdinalIgnoreCase))
        {
            observableProgress = true;
            log.Information($"[Fishing][Relog] Character transition observed: source={sourceCharacterKey}, current={newCharacterKey}, target={targetCharacterKey}");
        }
    }

    public void Reset()
    {
        state = FishingRelogState.Idle;
        targetCharacterKey = string.Empty;
        sourceCharacterKey = string.Empty;
        startedAtUtc = default;
        lastRelogCommandAtUtc = null;
        multiModeCommandSentAtUtc = null;
        lastWaitLogAtUtc = default;
        lastWaitReason = string.Empty;
        relogAttempts = 0;
        observableProgress = false;
        failureReason = string.Empty;
        StatusText = "Idle";
    }

    public void Update()
    {
        if (!IsActive)
            return;

        switch (state)
        {
            case FishingRelogState.FinishingPostprocess:
                StatusText = "Finishing Vermaxion AR postprocess";
                if (arPostProcessService.IsProcessing &&
                    !arPostProcessService.FinishPostProcess(mode: ARPostProcessFinishMode.ReleaseOnly))
                {
                    LogWait("Waiting for AutoRetainer postprocess ownership to release.");
                    return;
                }

                SetState(FishingRelogState.ReleasingSuppression);
                break;

            case FishingRelogState.ReleasingSuppression:
                StatusText = "Releasing Vermaxion AutoRetainer suppression";
                if (autoRetainerIPC.SuppressionOwnedByVermaxion &&
                    !autoRetainerIPC.ReleaseSuppressionIfOwned())
                {
                    LogWait("Waiting for VMX-owned AutoRetainer suppression to release.");
                    return;
                }

                SetState(FishingRelogState.DisablingAutoRetainerMultiMode);
                break;

            case FishingRelogState.DisablingAutoRetainerMultiMode:
                if (!multiModeCommandSentAtUtc.HasValue)
                {
                    const string command = "/ays m d";
                    log.Information(FishingRelogDiagnostics.FormatCommand(new FishingRelogPrepStep(FishingRelogPrepAction.SendCommand, command)));
                    CommandHelper.SendCommand(command);
                    multiModeCommandSentAtUtc = DateTimeOffset.UtcNow;
                    StatusText = "Waiting for AutoRetainer multi-mode disable to settle";
                    return;
                }

                if (DateTimeOffset.UtcNow - multiModeCommandSentAtUtc.Value < MultiModeCommandSettleDelay)
                    return;

                SetState(FishingRelogState.WaitingForRelogReadiness);
                break;

            case FishingRelogState.WaitingForRelogReadiness:
            case FishingRelogState.WaitingForRelogProgress:
                ProcessRelogCommandPolicy();
                break;
        }
    }

    private void ProcessRelogCommandPolicy()
    {
        var now = DateTimeOffset.UtcNow;
        UpdateObservableProgress();

        var ready = IsReadyForRelog(out var blockedReason);
        var targetReached = IsTargetReached();
        var wrongCharacterArrived = IsWrongCharacterArrival();
        var registrationOpen = OceanFishingSchedulePolicy.IsStartupWindowActive(
            now,
            configuration.OceanFishingPreWindowOffsetMinutes);

        var decision = FishingRelogCommandPolicy.Evaluate(
            now,
            startedAtUtc,
            lastRelogCommandAtUtc,
            registrationOpen,
            ready,
            blockedReason,
            targetReached,
            observableProgress,
            wrongCharacterArrived);

        switch (decision.Action)
        {
            case FishingRelogRuntimeAction.Complete:
                log.Information($"[Fishing][Relog] Completed observed relog to {targetCharacterKey}: {decision.Reason}");
                Reset();
                break;
            case FishingRelogRuntimeAction.Fail:
                Fail(decision.Reason);
                break;
            case FishingRelogRuntimeAction.SendRelog:
                SendRelog(decision.Reason);
                break;
            case FishingRelogRuntimeAction.Wait:
                StatusText = decision.Reason;
                LogWait(decision.Reason);
                break;
        }
    }

    private void SendRelog(string reason)
    {
        var command = $"/ays relog {targetCharacterKey}";
        relogAttempts++;
        lastRelogCommandAtUtc = DateTimeOffset.UtcNow;
        SetState(FishingRelogState.WaitingForRelogProgress);
        StatusText = $"Sent relog attempt {relogAttempts} to {targetCharacterKey}";
        log.Information($"[Fishing][Relog] {reason} Sending attempt {relogAttempts}: {command}");
        CommandHelper.SendCommand(command);
    }

    private bool IsReadyForRelog(out string reason)
    {
        if (arPostProcessService.IsProcessing)
        {
            reason = "Waiting for AutoRetainer postprocess to finish.";
            return false;
        }

        if (autoRetainerIPC.SuppressionOwnedByVermaxion)
        {
            reason = "Waiting for VMX-owned AutoRetainer suppression to release.";
            return false;
        }

        if (autoRetainerIPC.IsBusy())
        {
            reason = "Waiting for AutoRetainer to become idle before relog.";
            return false;
        }

        if (!Plugin.ClientState.IsLoggedIn)
        {
            reason = "Waiting for the current character to be logged in before sending relog.";
            return false;
        }

        if (Plugin.Condition[ConditionFlag.BetweenAreas] || Plugin.Condition[ConditionFlag.BetweenAreas51])
        {
            reason = "Waiting for area transition to clear before relog.";
            return false;
        }

        if (Plugin.Condition[ConditionFlag.Occupied] ||
            Plugin.Condition[ConditionFlag.Occupied33] ||
            Plugin.Condition[ConditionFlag.Occupied39] ||
            Plugin.Condition[ConditionFlag.OccupiedSummoningBell] ||
            Plugin.Condition[ConditionFlag.OccupiedInQuestEvent] ||
            Plugin.Condition[ConditionFlag.OccupiedInCutSceneEvent] ||
            Plugin.Condition[ConditionFlag.WatchingCutscene])
        {
            reason = "Waiting for occupied/bell/cutscene state to clear before relog.";
            return false;
        }

        if (GameHelpers.IsAddonVisible("RetainerList") ||
            GameHelpers.IsAddonVisible("RetainerSellList") ||
            GameHelpers.IsAddonVisible("RetainerTaskAsk") ||
            GameHelpers.IsAddonVisible("RetainerTaskResult"))
        {
            reason = "Waiting for retainer/bell UI to close before relog.";
            return false;
        }

        if (!GameHelpers.IsPlayerAvailable())
        {
            reason = "Waiting for player availability before relog.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private void UpdateObservableProgress()
    {
        if (observableProgress)
            return;

        if (!Plugin.ClientState.IsLoggedIn ||
            Plugin.Condition[ConditionFlag.BetweenAreas] ||
            Plugin.Condition[ConditionFlag.BetweenAreas51])
        {
            observableProgress = true;
            return;
        }

        var currentKey = GetCurrentCharacterKey();
        if (!string.IsNullOrWhiteSpace(currentKey) &&
            !string.IsNullOrWhiteSpace(sourceCharacterKey) &&
            !string.Equals(currentKey, sourceCharacterKey, StringComparison.OrdinalIgnoreCase))
        {
            observableProgress = true;
        }
    }

    private bool IsTargetReached()
        => Plugin.ClientState.IsLoggedIn &&
           string.Equals(GetCurrentCharacterKey(), targetCharacterKey, StringComparison.OrdinalIgnoreCase);

    private bool IsWrongCharacterArrival()
    {
        if (!observableProgress ||
            !Plugin.ClientState.IsLoggedIn ||
            Plugin.Condition[ConditionFlag.BetweenAreas] ||
            Plugin.Condition[ConditionFlag.BetweenAreas51] ||
            !GameHelpers.IsPlayerAvailable())
        {
            return false;
        }

        var currentKey = GetCurrentCharacterKey();
        return !string.IsNullOrWhiteSpace(currentKey) &&
               !string.Equals(currentKey, sourceCharacterKey, StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(currentKey, targetCharacterKey, StringComparison.OrdinalIgnoreCase);
    }

    private string GetCurrentCharacterKey()
    {
        if (!string.IsNullOrWhiteSpace(configManager.CurrentCharacterKey))
            return configManager.CurrentCharacterKey;

        var player = Plugin.ObjectTable.LocalPlayer;
        var characterName = player?.Name.ToString() ?? string.Empty;
        var worldName = player?.HomeWorld.Value.Name.ToString() ?? string.Empty;
        return string.IsNullOrWhiteSpace(characterName) || string.IsNullOrWhiteSpace(worldName)
            ? string.Empty
            : $"{characterName}@{worldName}";
    }

    private void SetState(FishingRelogState newState)
    {
        if (state != newState)
            log.Information($"[Fishing][Relog] {state} -> {newState}");

        state = newState;
    }

    private void LogWait(string reason)
    {
        var now = DateTimeOffset.UtcNow;
        if (string.Equals(lastWaitReason, reason, StringComparison.Ordinal) &&
            lastWaitLogAtUtc != default &&
            now - lastWaitLogAtUtc < WaitLogInterval)
        {
            return;
        }

        lastWaitReason = reason;
        lastWaitLogAtUtc = now;
        log.Information($"[Fishing][Relog] {reason}");
    }

    private void Fail(string reason)
    {
        failureReason = reason;
        state = FishingRelogState.Failed;
        StatusText = reason;
        log.Warning($"[Fishing][Relog] {reason}");
    }

    private enum FishingRelogState
    {
        Idle,
        FinishingPostprocess,
        ReleasingSuppression,
        DisablingAutoRetainerMultiMode,
        WaitingForRelogReadiness,
        WaitingForRelogProgress,
        Failed,
    }
}
