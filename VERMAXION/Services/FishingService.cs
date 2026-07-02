using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using VERMAXION.IPC;
using VERMAXION.Models;

namespace VERMAXION.Services;

public sealed class FishingService
{
    private const uint VersatileLureItemId = 29717;
    private static readonly TimeSpan CastInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan NoFishingContextTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan RepairTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ResultCloseDelay = TimeSpan.FromSeconds(15);

    private readonly ICommandManager commandManager;
    private readonly IPluginLog log;
    private readonly Configuration configuration;
    private readonly ConfigManager configManager;
    private readonly XADatabaseIPCClient xaDatabase;
    private readonly VendorStockService vendorStockService;
    private readonly AdsIpcClient adsIpcClient;

    private FishingState state = FishingState.Idle;
    private DateTime stateEnteredAt = DateTime.MinValue;
    private DateTime lastCastAt = DateTime.MinValue;
    private DateTime repairStartedAt = DateTime.MinValue;
    private DateTime lastResultCloseAttemptAt = DateTime.MinValue;
    private bool sawFishingContext;
    private string lastError = string.Empty;

    public enum FishingState
    {
        Idle,
        CheckingRepair,
        WaitingForRepair,
        CheckingLures,
        RestockingLures,
        SettingBait,
        Fishing,
        Returning,
        Complete,
        Failed,
    }

    public FishingState State => state;
    public bool IsActive => state != FishingState.Idle && state != FishingState.Complete && state != FishingState.Failed;
    public bool IsComplete => state == FishingState.Complete;
    public bool IsFailed => state == FishingState.Failed;
    public string StatusText => state == FishingState.Failed && !string.IsNullOrWhiteSpace(lastError)
        ? lastError
        : state.ToString();

    public FishingService(
        ICommandManager commandManager,
        IPluginLog log,
        Configuration configuration,
        ConfigManager configManager,
        XADatabaseIPCClient xaDatabase,
        VendorStockService vendorStockService,
        AdsIpcClient adsIpcClient)
    {
        this.commandManager = commandManager;
        this.log = log;
        this.configuration = configuration;
        this.configManager = configManager;
        this.xaDatabase = xaDatabase;
        this.vendorStockService = vendorStockService;
        this.adsIpcClient = adsIpcClient;
    }

    public void Start()
    {
        if (IsActive)
            return;

        lastError = string.Empty;
        sawFishingContext = IsFishingContextActive();
        lastCastAt = DateTime.MinValue;
        repairStartedAt = DateTime.MinValue;
        lastResultCloseAttemptAt = DateTime.MinValue;
        SetState(FishingState.CheckingRepair);
    }

    public void RunTask()
    {
        log.Information("[VERMAXION] Manual Fishing triggered");
        Start();
    }

    public void Reset()
    {
        vendorStockService.Reset();
        state = FishingState.Idle;
        stateEnteredAt = DateTime.MinValue;
        lastCastAt = DateTime.MinValue;
        repairStartedAt = DateTime.MinValue;
        lastResultCloseAttemptAt = DateTime.MinValue;
        sawFishingContext = false;
        lastError = string.Empty;
    }

    public FishingSelectionResult SelectFishingTarget(bool fishingWindowActive)
    {
        var account = configManager.GetCurrentAccount();
        if (account == null)
            return FishingSelectionResult.None("No current account config.");

        var fisherLevels = xaDatabase.GetFisherLevelsByCharacterKey();
        var candidates = new List<FishingCharacterCandidate>();
        foreach (var pair in account.Characters)
        {
            var level = fisherLevels.TryGetValue(pair.Key, out var fisherLevel)
                ? fisherLevel
                : 0;
            candidates.Add(new FishingCharacterCandidate(
                pair.Key,
                level,
                pair.Value.EnableFishing,
                pair.Value.AlwaysFishOnThisCharacterIfWindowOpen,
                string.Equals(pair.Key, configManager.CurrentCharacterKey, StringComparison.OrdinalIgnoreCase)));
        }

        return FishingSelectionPolicy.Select(
            candidates,
            configuration.FishingMaxFisherLevel,
            configuration.FishingExecutionMode,
            configManager.CurrentCharacterKey,
            fishingWindowActive);
    }

    public static bool IsFishingContextActive()
        => Plugin.Condition[ConditionFlag.BoundByDuty]
           || Plugin.Condition[ConditionFlag.BoundByDuty56]
           || GameHelpers.IsAddonVisible("IKDResult");

    public void Update()
    {
        if (state is FishingState.Idle or FishingState.Complete or FishingState.Failed)
            return;

        var elapsed = DateTime.UtcNow - stateEnteredAt;
        switch (state)
        {
            case FishingState.CheckingRepair:
                if (TryStartRepairIfNeeded())
                    break;
                SetState(FishingState.CheckingLures);
                break;

            case FishingState.WaitingForRepair:
                if (DateTime.UtcNow - repairStartedAt > RepairTimeout)
                {
                    Fail("ADS repair timed out.");
                    break;
                }

                var status = adsIpcClient.Refresh();
                if (status.UtilityRunning)
                    break;

                if (elapsed.TotalSeconds < 3)
                    break;

                log.Information("[Fishing] ADS repair finished or no longer running; continuing fishing prep");
                SetState(FishingState.CheckingLures);
                break;

            case FishingState.CheckingLures:
                var lureTarget = Math.Max(0, GetActiveOperationSettings().LureRestockTarget);
                var lureCount = (int)GameHelpers.GetInventoryItemCount(VersatileLureItemId);
                if (lureTarget > 0 && lureCount < lureTarget)
                {
                    log.Information($"[Fishing] Versatile Lures below target ({lureCount}/{lureTarget}); starting restock");
                    vendorStockService.StartVersatileLureRestock(lureTarget);
                    SetState(FishingState.RestockingLures);
                    break;
                }

                SetState(FishingState.SettingBait);
                break;

            case FishingState.RestockingLures:
                vendorStockService.Update();
                if (vendorStockService.IsComplete)
                {
                    vendorStockService.Reset();
                    SetState(FishingState.SettingBait);
                }
                else if (vendorStockService.IsFailed)
                {
                    vendorStockService.Reset();
                    Fail("Versatile Lure restock failed.");
                }
                break;

            case FishingState.SettingBait:
                CommandHelper.SendCommand("/bait Versatile Lure");
                SetState(FishingState.Fishing);
                break;

            case FishingState.Fishing:
                TickFishingLoop(elapsed);
                break;

            case FishingState.Returning:
                SetState(FishingState.Complete);
                break;
        }
    }

    private bool TryStartRepairIfNeeded()
    {
        var settings = GetActiveOperationSettings();
        var durabilityKnown = GameHelpers.TryGetLowestEquippedGearConditionPercent(out var lowestDurability);
        var decision = FishingOperationPolicy.EvaluateRepair(
            settings,
            durabilityKnown,
            lowestDurability);

        if (!decision.ShouldRepair)
        {
            log.Information($"[Fishing] Repair skipped: {decision.Reason}");
            return false;
        }

        if (!adsIpcClient.StartRepair(decision.AdsMode, out var failure))
        {
            Fail($"ADS repair failed to start: {failure}");
            return true;
        }

        repairStartedAt = DateTime.UtcNow;
        log.Information($"[Fishing] ADS repair requested: {decision.AdsMode}. {decision.Reason}");
        SetState(FishingState.WaitingForRepair);
        return true;
    }

    private void TickFishingLoop(TimeSpan elapsed)
    {
        var inFishingContext = IsFishingContextActive();
        if (inFishingContext)
            sawFishingContext = true;

        if (GameHelpers.IsAddonVisible("IKDResult"))
            TryCloseResultWindow();

        if (!inFishingContext)
        {
            if (sawFishingContext)
            {
                ReturnAfterFishing();
                return;
            }

            if (elapsed >= NoFishingContextTimeout)
            {
                log.Information("[Fishing] No active fishing duty/window found after prep; completing without cast spam.");
                SetState(FishingState.Complete);
            }

            return;
        }

        var player = Plugin.ObjectTable.LocalPlayer;
        var busy = player?.IsCasting == true ||
                   Plugin.Condition[ConditionFlag.Casting] ||
                   Plugin.Condition[ConditionFlag.Occupied] ||
                   Plugin.Condition[ConditionFlag.OccupiedInQuestEvent] ||
                   Plugin.Condition[ConditionFlag.OccupiedInCutSceneEvent] ||
                   Plugin.Condition[ConditionFlag.WatchingCutscene] ||
                   Plugin.Condition[ConditionFlag.InCombat] ||
                   Plugin.Condition[ConditionFlag.BetweenAreas] ||
                   Plugin.Condition[ConditionFlag.BetweenAreas51];

        if (!FishingCastPolicy.ShouldCast(
                enabled: true,
                inFishingContext,
                playerAvailable: GameHelpers.IsPlayerAvailable(),
                busy,
                resultWindowVisible: GameHelpers.IsAddonVisible("IKDResult")))
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (lastCastAt != DateTime.MinValue && now - lastCastAt < CastInterval)
            return;

        lastCastAt = now;
        commandManager.ProcessCommand("/ac cast");
        log.Debug("[Fishing] Sent /ac cast");
    }

    private void TryCloseResultWindow()
    {
        var now = DateTime.UtcNow;
        if (lastResultCloseAttemptAt != DateTime.MinValue && now - lastResultCloseAttemptAt < ResultCloseDelay)
            return;

        lastResultCloseAttemptAt = now;
        GameHelpers.TryCloseAddonByCallback("IKDResult");
    }

    private void ReturnAfterFishing()
    {
        var command = ResolveReturnCommand();
        if (!string.IsNullOrWhiteSpace(command))
        {
            log.Information($"[Fishing] Fishing context ended; returning with {command}");
            CommandHelper.SendCommand(command);
        }

        SetState(FishingState.Returning);
    }

    private string ResolveReturnCommand()
        => FishingOperationPolicy.ResolveReturnCommand(GetActiveOperationSettings());

    private FishingOperationSettings GetActiveOperationSettings()
    {
        var activeConfig = configManager.GetActiveConfig();
        return new FishingOperationSettings(
            activeConfig.FishingLureRestockTarget,
            activeConfig.FishingReturnDestination,
            activeConfig.FishingReturnCommand,
            activeConfig.FishingRepairMode,
            activeConfig.FishingRepairThresholdPercent);
    }

    private void Fail(string message)
    {
        lastError = message;
        log.Warning($"[Fishing] {message}");
        vendorStockService.Reset();
        SetState(FishingState.Failed);
    }

    private void SetState(FishingState newState)
    {
        log.Information($"[Fishing] {state} -> {newState}");
        state = newState;
        stateEnteredAt = DateTime.UtcNow;
    }
}
