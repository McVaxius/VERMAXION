using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using VERMAXION.IPC;
using VERMAXION.Models;

namespace VERMAXION.Services;

public sealed class FishingService
{
    private const int FisherJobId = 18;
    private const ushort LimsaTerritoryType = 129;
    private const uint DryskthotaDataId = 1005421;
    private const uint VersatileLureItemId = 29717;
    private const float RegistrarInteractDistance = 4.5f;
    private const float BoatFishingPositionTolerance = 1.5f;
    private static readonly Vector3 DryskthotaPosition = new(-409.42f, 4.00f, 74.48f);
    private static readonly Vector3 BoatFishingPosition = new(7.451f, 6.750f, -2.0f);
    private static readonly TimeSpan CastInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan FishingLoopPollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan JobSwitchTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan LimsaTravelTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan RegistrarNavigationTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan QueueConfirmationTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan DepartureTimeout = TimeSpan.FromMinutes(35);
    private static readonly TimeSpan DutyCompletionTimeout = TimeSpan.FromHours(3);
    private static readonly TimeSpan RepairTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ResultCloseDelay = TimeSpan.FromSeconds(15);

    private readonly ICommandManager commandManager;
    private readonly IPluginLog log;
    private readonly Configuration configuration;
    private readonly ConfigManager configManager;
    private readonly XADatabaseIPCClient xaDatabase;
    private readonly VendorStockService vendorStockService;
    private readonly AdsIpcClient adsIpcClient;
    private readonly VNavmeshIPC vnavmesh;
    private readonly LifestreamIPC lifestream;

    private FishingState state = FishingState.Idle;
    private DateTime stateEnteredAt = DateTime.MinValue;
    private DateTime lastCastAt = DateTime.MinValue;
    private DateTime repairStartedAt = DateTime.MinValue;
    private DateTime lastResultCloseAttemptAt = DateTime.MinValue;
    private DateTime lastFishingLoopPollAt = DateTime.MinValue;
    private DateTime lastJobSwitchAttemptAt = DateTime.MinValue;
    private DateTime lastTravelCommandAt = DateTime.MinValue;
    private DateTime travelStartedAt = DateTime.MinValue;
    private DateTime lastNavigationCommandAt = DateTime.MinValue;
    private DateTime lastInteractionAttemptAt = DateTime.MinValue;
    private DateTime registrationAttemptStartedAt = DateTime.MinValue;
    private DateTime departureWaitStartedAt = DateTime.MinValue;
    private DateTime dutyStartedAt = DateTime.MinValue;
    private bool sawFishingContext;
    private string lastError = string.Empty;
    private string statusDetail = string.Empty;

    public enum FishingState
    {
        Idle,
        SwitchingToFisher,
        CheckingRepair,
        WaitingForRepair,
        CheckingLures,
        RestockingLures,
        SettingBait,
        TravelingToLimsa,
        NavigatingToRegistrar,
        InteractingRegistrar,
        ConfirmingRegistration,
        WaitingForDeparture,
        MovingToFishingSpot,
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
        : !string.IsNullOrWhiteSpace(statusDetail)
            ? statusDetail
        : state.ToString();

    public FishingService(
        ICommandManager commandManager,
        IPluginLog log,
        Configuration configuration,
        ConfigManager configManager,
        XADatabaseIPCClient xaDatabase,
        VendorStockService vendorStockService,
        AdsIpcClient adsIpcClient,
        VNavmeshIPC vnavmesh,
        LifestreamIPC lifestream)
    {
        this.commandManager = commandManager;
        this.log = log;
        this.configuration = configuration;
        this.configManager = configManager;
        this.xaDatabase = xaDatabase;
        this.vendorStockService = vendorStockService;
        this.adsIpcClient = adsIpcClient;
        this.vnavmesh = vnavmesh;
        this.lifestream = lifestream;
    }

    public void Start()
    {
        if (IsActive)
            return;

        lastError = string.Empty;
        sawFishingContext = IsFishingContextActive();
        statusDetail = string.Empty;
        lastCastAt = DateTime.MinValue;
        repairStartedAt = DateTime.MinValue;
        lastResultCloseAttemptAt = DateTime.MinValue;
        lastFishingLoopPollAt = DateTime.MinValue;
        lastJobSwitchAttemptAt = DateTime.MinValue;
        lastTravelCommandAt = DateTime.MinValue;
        travelStartedAt = DateTime.MinValue;
        lastNavigationCommandAt = DateTime.MinValue;
        lastInteractionAttemptAt = DateTime.MinValue;
        registrationAttemptStartedAt = DateTime.MinValue;
        departureWaitStartedAt = DateTime.MinValue;
        dutyStartedAt = DateTime.MinValue;
        SetState(FishingState.SwitchingToFisher);
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
        lastFishingLoopPollAt = DateTime.MinValue;
        lastJobSwitchAttemptAt = DateTime.MinValue;
        lastTravelCommandAt = DateTime.MinValue;
        travelStartedAt = DateTime.MinValue;
        lastNavigationCommandAt = DateTime.MinValue;
        lastInteractionAttemptAt = DateTime.MinValue;
        registrationAttemptStartedAt = DateTime.MinValue;
        departureWaitStartedAt = DateTime.MinValue;
        dutyStartedAt = DateTime.MinValue;
        sawFishingContext = false;
        lastError = string.Empty;
        statusDetail = string.Empty;
        vnavmesh.Stop();
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

    public bool IsOceanFishingStartupWindowActive(DateTimeOffset nowUtc)
        => OceanFishingSchedulePolicy.IsStartupWindowActive(
            nowUtc,
            configuration.OceanFishingPreWindowOffsetMinutes);

    public FishingSelectionResult SelectFishingStartupTarget(DateTimeOffset nowUtc)
    {
        if (!IsOceanFishingStartupWindowActive(nowUtc))
            return FishingSelectionResult.None("No VERMAXION Ocean Fishing startup window is active.");

        return SelectFishingTarget(fishingWindowActive: true);
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
            case FishingState.SwitchingToFisher:
                if (IsOceanFishingDutyActive())
                {
                    SetState(FishingState.MovingToFishingSpot);
                    break;
                }

                if (EnsureFisherJob(elapsed))
                    SetState(FishingState.CheckingRepair);
                break;

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
                SetState(FishingState.TravelingToLimsa);
                break;

            case FishingState.TravelingToLimsa:
                TickTravelToLimsa(elapsed);
                break;

            case FishingState.NavigatingToRegistrar:
                TickNavigateToRegistrar(elapsed);
                break;

            case FishingState.InteractingRegistrar:
                TickInteractWithRegistrar(elapsed);
                break;

            case FishingState.ConfirmingRegistration:
                TickConfirmRegistration(elapsed);
                break;

            case FishingState.WaitingForDeparture:
                TickWaitForDeparture(elapsed);
                break;

            case FishingState.MovingToFishingSpot:
                TickMoveToFishingSpot(elapsed);
                break;

            case FishingState.Fishing:
                TickFishingLoop(elapsed);
                break;

            case FishingState.Returning:
                SetState(FishingState.Complete);
                break;
        }
    }

    private bool EnsureFisherJob(TimeSpan elapsed)
    {
        if (GetCurrentJobId() == FisherJobId)
            return true;

        if (elapsed > JobSwitchTimeout)
        {
            Fail($"Timed out switching to Fisher; current job id is {GetCurrentJobId()}.");
            return false;
        }

        if (lastJobSwitchAttemptAt == DateTime.MinValue ||
            DateTime.UtcNow - lastJobSwitchAttemptAt >= TimeSpan.FromSeconds(5))
        {
            lastJobSwitchAttemptAt = DateTime.UtcNow;
            log.Information("[Fishing] Equipping Fisher with /gearset change FSH");
            CommandHelper.SendCommand("/gearset change FSH");
        }

        return false;
    }

    private static int GetCurrentJobId()
        => (int)Plugin.PlayerState.ClassJob.RowId;

    private void TickTravelToLimsa(TimeSpan elapsed)
    {
        if (IsOceanFishingDutyActive())
        {
            SetState(FishingState.MovingToFishingSpot);
            return;
        }

        if (IsInLimsaAndReady())
        {
            SetState(FishingState.NavigatingToRegistrar);
            return;
        }

        if (travelStartedAt == DateTime.MinValue)
            travelStartedAt = DateTime.UtcNow;

        if (elapsed > LimsaTravelTimeout)
        {
            Fail("Timed out traveling to Limsa for Ocean Fishing registration.");
            return;
        }

        if (lastTravelCommandAt == DateTime.MinValue ||
            DateTime.UtcNow - lastTravelCommandAt >= TimeSpan.FromSeconds(10))
        {
            lastTravelCommandAt = DateTime.UtcNow;
            log.Information("[Fishing] Traveling to Limsa for Ocean Fishing: /li limsa");
            lifestream.ExecuteCommand("/li limsa");
        }
    }

    private void TickNavigateToRegistrar(TimeSpan elapsed)
    {
        if (IsOceanFishingDutyActive())
        {
            SetState(FishingState.MovingToFishingSpot);
            return;
        }

        if (!IsInLimsaAndReady())
        {
            SetState(FishingState.TravelingToLimsa);
            return;
        }

        var distance = DistanceTo(DryskthotaPosition);
        if (distance <= RegistrarInteractDistance)
        {
            vnavmesh.Stop();
            SetState(FishingState.InteractingRegistrar);
            return;
        }

        if (elapsed > RegistrarNavigationTimeout)
        {
            Fail($"Timed out navigating to Dryskthota; distance={distance:F1}y.");
            return;
        }

        if (lastNavigationCommandAt == DateTime.MinValue ||
            DateTime.UtcNow - lastNavigationCommandAt >= TimeSpan.FromSeconds(2))
        {
            lastNavigationCommandAt = DateTime.UtcNow;
            log.Information($"[Fishing] Navigating to Dryskthota ({distance:F1}y)");
            vnavmesh.PathfindAndMoveTo(DryskthotaPosition);
        }
    }

    private void TickInteractWithRegistrar(TimeSpan elapsed)
    {
        if (IsOceanFishingDutyActive())
        {
            SetState(FishingState.MovingToFishingSpot);
            return;
        }

        if (IsQueueConfirmed())
        {
            SetState(FishingState.WaitingForDeparture);
            return;
        }

        if (!IsRegistrationWindowOpen(out var window))
        {
            if (OceanFishingSchedulePolicy.TryGetActiveStartupWindow(
                    DateTimeOffset.UtcNow,
                    configuration.OceanFishingPreWindowOffsetMinutes,
                    out window) &&
                DateTimeOffset.UtcNow < window.RegistrationStartUtc)
            {
                statusDetail = $"Waiting for Ocean Fishing registration at {window.RegistrationStartUtc:u}";
                return;
            }

            Fail("Ocean Fishing registration is not open before Dryskthota interaction.");
            return;
        }

        if (registrationAttemptStartedAt == DateTime.MinValue)
            registrationAttemptStartedAt = DateTime.UtcNow;

        if (elapsed > QueueConfirmationTimeout)
        {
            SetState(FishingState.ConfirmingRegistration);
            return;
        }

        TryHandleOceanFishingYesNo();

        if (lastInteractionAttemptAt == DateTime.MinValue ||
            DateTime.UtcNow - lastInteractionAttemptAt >= TimeSpan.FromSeconds(5))
        {
            lastInteractionAttemptAt = DateTime.UtcNow;
            log.Information($"[Fishing] Interacting with Dryskthota dataId={DryskthotaDataId} at registration window {window.RegistrationStartUtc:u}");
            GameHelpers.TargetAndInteractByDataId(DryskthotaDataId, "Dryskthota");
        }
    }

    private void TickConfirmRegistration(TimeSpan elapsed)
    {
        if (IsOceanFishingDutyActive())
        {
            SetState(FishingState.MovingToFishingSpot);
            return;
        }

        if (IsQueueConfirmed())
        {
            SetState(FishingState.WaitingForDeparture);
            return;
        }

        TryHandleOceanFishingYesNo();

        if (!IsRegistrationWindowOpen(out _))
        {
            Fail("Ocean Fishing registration closed before queue confirmation was observed.");
            return;
        }

        if (registrationAttemptStartedAt != DateTime.MinValue &&
            DateTime.UtcNow - registrationAttemptStartedAt > QueueConfirmationTimeout &&
            lastInteractionAttemptAt != DateTime.MinValue &&
            DateTime.UtcNow - lastInteractionAttemptAt >= TimeSpan.FromSeconds(5))
        {
            lastInteractionAttemptAt = DateTime.UtcNow;
            log.Information("[Fishing] Queue confirmation not observed yet; retrying Dryskthota interaction");
            GameHelpers.TargetAndInteractByDataId(DryskthotaDataId, "Dryskthota");
        }

        statusDetail = "Waiting for Ocean Fishing queue confirmation";
    }

    private void TickWaitForDeparture(TimeSpan elapsed)
    {
        if (departureWaitStartedAt == DateTime.MinValue)
            departureWaitStartedAt = DateTime.UtcNow;

        if (IsOceanFishingDutyActive())
        {
            SetState(FishingState.MovingToFishingSpot);
            return;
        }

        TryHandleOceanFishingYesNo();

        if (elapsed > DepartureTimeout)
        {
            Fail("Timed out waiting for Ocean Fishing departure after queue confirmation.");
            return;
        }

        statusDetail = "Registered for Ocean Fishing; waiting for departure";
    }

    private void TickMoveToFishingSpot(TimeSpan elapsed)
    {
        if (!IsOceanFishingDutyActive())
        {
            if (elapsed > TimeSpan.FromSeconds(30))
            {
                Fail("Ocean Fishing duty context was not active after departure.");
                return;
            }

            statusDetail = "Waiting for Ocean Fishing duty context";
            return;
        }

        var distance = DistanceTo(BoatFishingPosition);
        if (distance <= BoatFishingPositionTolerance)
        {
            vnavmesh.Stop();
            SetState(FishingState.Fishing);
            return;
        }

        if (lastNavigationCommandAt == DateTime.MinValue ||
            DateTime.UtcNow - lastNavigationCommandAt >= TimeSpan.FromSeconds(2))
        {
            lastNavigationCommandAt = DateTime.UtcNow;
            log.Information($"[Fishing] Moving to Ocean Fishing rail position ({distance:F1}y)");
            vnavmesh.PathfindAndMoveTo(BoatFishingPosition);
        }
    }

    private static bool IsInLimsaAndReady()
        => Plugin.ClientState.TerritoryType == LimsaTerritoryType &&
           !Plugin.Condition[ConditionFlag.BetweenAreas] &&
           !Plugin.Condition[ConditionFlag.BetweenAreas51] &&
           GameHelpers.IsPlayerAvailable();

    private static bool IsQueueConfirmed()
        => Plugin.Condition[ConditionFlag.WaitingForDuty] ||
           Plugin.Condition[ConditionFlag.WaitingForDutyFinder] ||
           GameHelpers.IsAddonVisible("ContentsFinderConfirm") ||
           GameHelpers.IsAddonVisible("ContentsFinderReady");

    private static bool IsOceanFishingDutyActive()
        => IsFishingContextActive();

    private static double DistanceTo(Vector3 position)
    {
        var player = Plugin.ObjectTable.LocalPlayer;
        return player == null
            ? double.MaxValue
            : Vector3.Distance(player.Position, position);
    }

    private bool IsRegistrationWindowOpen(out OceanFishingStartupWindow window)
    {
        var now = DateTimeOffset.UtcNow;
        if (!OceanFishingSchedulePolicy.TryGetActiveStartupWindow(
                now,
                configuration.OceanFishingPreWindowOffsetMinutes,
                out window))
        {
            return false;
        }

        return now >= window.RegistrationStartUtc && now < window.EndUtc;
    }

    private static void TryHandleOceanFishingYesNo()
    {
        GameHelpers.TryClickYesIfPromptAllowed(
            prompt => prompt.Contains("Register to board", StringComparison.OrdinalIgnoreCase) ||
                      prompt.Contains("Embark to the", StringComparison.OrdinalIgnoreCase) ||
                      prompt.Contains("board the Endeavor", StringComparison.OrdinalIgnoreCase) ||
                      prompt.Contains("ocean fishing", StringComparison.OrdinalIgnoreCase),
            "Ocean Fishing registration/embark",
            allowUnreadable: false,
            out _);
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
        var now = DateTime.UtcNow;
        if (lastFishingLoopPollAt != DateTime.MinValue &&
            now - lastFishingLoopPollAt < FishingLoopPollInterval)
        {
            return;
        }

        lastFishingLoopPollAt = now;
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

            if (elapsed >= DutyCompletionTimeout)
            {
                Fail("Timed out waiting for Ocean Fishing duty completion.");
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
        statusDetail = string.Empty;

        if (newState is FishingState.NavigatingToRegistrar or FishingState.MovingToFishingSpot)
            lastNavigationCommandAt = DateTime.MinValue;

        if (newState == FishingState.InteractingRegistrar)
            lastInteractionAttemptAt = DateTime.MinValue;

        if (newState == FishingState.ConfirmingRegistration && registrationAttemptStartedAt == DateTime.MinValue)
            registrationAttemptStartedAt = DateTime.UtcNow;

        if (newState == FishingState.WaitingForDeparture)
            departureWaitStartedAt = DateTime.UtcNow;

        if (newState == FishingState.Fishing)
        {
            lastFishingLoopPollAt = DateTime.MinValue;
            dutyStartedAt = DateTime.UtcNow;
            sawFishingContext = true;
        }
    }
}
