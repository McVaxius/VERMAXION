using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.DutyState;
using Dalamud.Plugin.Services;
using ECommons.GameHelpers;
using ECommons.ExcelServices;
using ECommons.ExcelServices.Sheets;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using VERMAXION.IPC;
using VERMAXION.Models;

namespace VERMAXION.Services;

public sealed class FishingService
{
    private const ushort LimsaTerritoryType = 129;
    private const uint ArcanistsGuildAethernetId = 43;
    private const uint OceanFishingUnlockQuestId = 69379;
    private const uint DryskthotaDataId = 1005421;
    private const uint VersatileLureItemId = 29717;
    private const string OceanFishingResultAddonName = "IKDResult";
    private const float BoatFishingPositionTolerance = 1.5f;
    private const ConditionFlag GatheringCondition = (ConditionFlag)6;
    private const ConditionFlag FishingCondition = (ConditionFlag)43;
    private static readonly Vector3 DryskthotaPosition = new(-409.42f, 4.00f, 74.48f);
    private static readonly Vector3[] BoatFishingPositions =
    [
        new(7.125f, 6.711f, -2.0f),
        new(7.125f, 6.711f, -8.0f),
        new(7.125f, 6.711f, -14.0f),
        new(-7.125f, 6.711f, -10.0f),
        new(-7.125f, 6.711f, -2.0f),
    ];
    private static readonly TimeSpan FishingLoopPollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan LimsaTravelTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan RegistrarNavigationTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan DepartureTimeout = TimeSpan.FromMinutes(35);
    private static readonly TimeSpan DutyCompletionTimeout = TimeSpan.FromHours(3);
    private static readonly TimeSpan RepairTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ResultCloseDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ResultSettlementTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ResultSettlementWaitLogInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ReturnSettlementTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ZoneTransitionTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan CleanupReadyTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan CleanupWorkTimeout = TimeSpan.FromMinutes(2);

    private readonly IPluginLog log;
    private readonly Configuration configuration;
    private readonly ConfigManager configManager;
    private readonly XADatabaseIPCClient xaDatabase;
    private readonly VendorStockService vendorStockService;
    private readonly AdsIpcClient adsIpcClient;
    private readonly VNavmeshIPC vnavmesh;
    private readonly LifestreamIPC lifestream;
    private readonly AutoRetainerIPC autoRetainer;
    private readonly FishingRunLifecycle runLifecycle;
    private readonly IFisherGearsetRuntime fisherGearsetRuntime;
    private readonly IDutyState dutyState;

    private FishingState state = FishingState.Idle;
    private DateTime stateEnteredAt = DateTime.MinValue;
    private DateTime lastCastAt = DateTime.MinValue;
    private DateTime repairStartedAt = DateTime.MinValue;
    private DateTime lastResultCloseAttemptAt = DateTime.MinValue;
    private DateTime lastResultSettlementWaitLogAt = DateTime.MinValue;
    private string lastResultAddonState = string.Empty;
    private DateTime lastFishingLoopPollAt = DateTime.MinValue;
    private DateTime lastTravelCommandAt = DateTime.MinValue;
    private DateTime travelStartedAt = DateTime.MinValue;
    private DateTime lastNavigationCommandAt = DateTime.MinValue;
    private DateTime lastInteractionAttemptAt = DateTime.MinValue;
    private DateTime departureWaitStartedAt = DateTime.MinValue;
    private DateTime dutyStartedAt = DateTime.MinValue;
    private bool sawFishingContext;
    private bool registrationEntrySelected;
    private bool routeSelectionHandled;
    private bool queueRegistrationObserved;
    private int lureCountBeforeRestock;
    private bool dutyReadyAccepted;
    private bool dutyCompletionObserved;
    private bool resultFallbackLogged;
    private bool aethernetAttempted;
    private bool aethernetAttunementAttempted;
    private DateTime aethernetAttunementStartedAt = DateTime.MinValue;
    private DateTime aethernetAttunementNavigationStartedAt = DateTime.MinValue;
    private bool waitingRegistrationLogged;
    private bool returnCommandSent;
    private bool returnTransitionObserved;
    private int returnCommandsSent;
    private DateTime dutyContextLostAt = DateTime.MinValue;
    private DateTime zoneTransitionStartedAt = DateTime.MinValue;
    private IReadOnlyList<FishingCleanupCommand> cleanupCommands = Array.Empty<FishingCleanupCommand>();
    private int cleanupCommandIndex;
    private bool cleanupCommandSent;
    private bool cleanupBusyObserved;
    private int railPositionIndex;
    private DateTime railPositionReachedAt = DateTime.MinValue;
    private DateTime canFishUnavailableSince = DateTime.MinValue;
    private int castAttemptCount;
    private bool castAcknowledged;
    private string lastCastGate = string.Empty;
    private DateTime returnStartedAt = DateTime.MinValue;
    private uint returnStartedTerritory;
    private FishingRunMode activeRunMode;
    private string lastError = string.Empty;
    private string statusDetail = string.Empty;
    private FisherGearsetEquipOperation? fisherGearsetOperation;
    private FishingAttemptFailureKind failureKind = FishingAttemptFailureKind.Stop;
    private bool failureReported;

    public enum FishingState
    {
        Idle,
        SwitchingToFisher,
        ValidatingUnlock,
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
        HandlingResult,
        WaitingForCleanupReady,
        NavigatingToCleanupVendor,
        RunningInventoryCleanup,
        WaitingForDutyEntry,
        Returning,
        CleaningUpLifecycle,
        Complete,
        Failed,
    }

    public FishingState State => state;
    public bool IsActive => state != FishingState.Idle && state != FishingState.Complete && state != FishingState.Failed;
    public bool IsComplete => state == FishingState.Complete;
    public bool IsFailed => state == FishingState.Failed;
    public bool QueueRegistrationObserved => queueRegistrationObserved;
    public FishingAttemptFailureKind FailureKind => failureKind;
    public bool FailureReported => failureReported;
    public string StatusText => (activeRunMode == FishingRunMode.Test ? "Test: " : string.Empty) +
        (state == FishingState.Failed && !string.IsNullOrWhiteSpace(lastError)
            ? lastError
            : !string.IsNullOrWhiteSpace(statusDetail)
                ? statusDetail
                : state.ToString());

    public FishingService(
        IPluginLog log,
        Configuration configuration,
        ConfigManager configManager,
        XADatabaseIPCClient xaDatabase,
        VendorStockService vendorStockService,
        AdsIpcClient adsIpcClient,
        VNavmeshIPC vnavmesh,
        LifestreamIPC lifestream,
        AutoRetainerIPC autoRetainer,
        FishingRunLifecycle runLifecycle,
        IFisherGearsetRuntime fisherGearsetRuntime,
        IDutyState dutyState)
    {
        this.log = log;
        this.configuration = configuration;
        this.configManager = configManager;
        this.xaDatabase = xaDatabase;
        this.vendorStockService = vendorStockService;
        this.adsIpcClient = adsIpcClient;
        this.vnavmesh = vnavmesh;
        this.lifestream = lifestream;
        this.autoRetainer = autoRetainer;
        this.runLifecycle = runLifecycle;
        this.fisherGearsetRuntime = fisherGearsetRuntime;
        this.dutyState = dutyState;
        dutyState.DutyCompleted += OnDutyCompleted;
    }

    public void Start()
    {
        if (IsActive)
            return;

        if (runLifecycle.Current == null)
        {
            lastError = "Ocean Fishing cannot start without an owned run context.";
            state = FishingState.Failed;
            return;
        }

        activeRunMode = runLifecycle.Current.Mode;
        lastError = string.Empty;
        sawFishingContext = IsFishingContextActive();
        statusDetail = string.Empty;
        lastCastAt = DateTime.MinValue;
        repairStartedAt = DateTime.MinValue;
        lastResultCloseAttemptAt = DateTime.MinValue;
        lastResultSettlementWaitLogAt = DateTime.MinValue;
        lastResultAddonState = string.Empty;
        lastFishingLoopPollAt = DateTime.MinValue;
        fisherGearsetOperation = null;
        lastTravelCommandAt = DateTime.MinValue;
        travelStartedAt = DateTime.MinValue;
        lastNavigationCommandAt = DateTime.MinValue;
        lastInteractionAttemptAt = DateTime.MinValue;
        departureWaitStartedAt = DateTime.MinValue;
        dutyStartedAt = DateTime.MinValue;
        registrationEntrySelected = false;
        routeSelectionHandled = false;
        queueRegistrationObserved = false;
        lureCountBeforeRestock = 0;
        dutyReadyAccepted = false;
        dutyCompletionObserved = false;
        resultFallbackLogged = false;
        aethernetAttempted = false;
        aethernetAttunementAttempted = false;
        aethernetAttunementStartedAt = DateTime.MinValue;
        aethernetAttunementNavigationStartedAt = DateTime.MinValue;
        waitingRegistrationLogged = false;
        returnCommandSent = false;
        returnTransitionObserved = false;
        returnCommandsSent = 0;
        dutyContextLostAt = DateTime.MinValue;
        zoneTransitionStartedAt = DateTime.MinValue;
        cleanupCommands = Array.Empty<FishingCleanupCommand>();
        cleanupCommandIndex = 0;
        cleanupCommandSent = false;
        cleanupBusyObserved = false;
        failureKind = FishingAttemptFailureKind.Stop;
        failureReported = false;
        railPositionIndex = 0;
        railPositionReachedAt = DateTime.MinValue;
        canFishUnavailableSince = DateTime.MinValue;
        ResetCastAttemptState();
        returnStartedAt = DateTime.MinValue;
        returnStartedTerritory = 0;
        SetState(FishingState.SwitchingToFisher);
    }

    public void RunTask()
    {
        log.Information("[VERMAXION] Manual Fishing triggered");
        Start();
    }

    public void Reset(bool releaseRun = true)
    {
        vendorStockService.Reset();
        state = FishingState.Idle;
        stateEnteredAt = DateTime.MinValue;
        lastCastAt = DateTime.MinValue;
        repairStartedAt = DateTime.MinValue;
        lastResultCloseAttemptAt = DateTime.MinValue;
        lastResultSettlementWaitLogAt = DateTime.MinValue;
        lastResultAddonState = string.Empty;
        lastFishingLoopPollAt = DateTime.MinValue;
        fisherGearsetOperation = null;
        lastTravelCommandAt = DateTime.MinValue;
        travelStartedAt = DateTime.MinValue;
        lastNavigationCommandAt = DateTime.MinValue;
        lastInteractionAttemptAt = DateTime.MinValue;
        departureWaitStartedAt = DateTime.MinValue;
        dutyStartedAt = DateTime.MinValue;
        sawFishingContext = false;
        resultFallbackLogged = false;
        lastError = string.Empty;
        statusDetail = string.Empty;
        railPositionIndex = 0;
        railPositionReachedAt = DateTime.MinValue;
        canFishUnavailableSince = DateTime.MinValue;
        zoneTransitionStartedAt = DateTime.MinValue;
        ResetCastAttemptState();
        vnavmesh.Stop();
        if (releaseRun)
        {
            runLifecycle.Cleanup("fishing service reset");
            activeRunMode = FishingRunMode.Scheduled;
        }
    }

    public void Dispose()
    {
        dutyState.DutyCompleted -= OnDutyCompleted;
        Reset();
    }

    public void MarkFailureReported()
        => failureReported = true;

    public IReadOnlyList<FishingSelectionResult> BuildFishingCandidateQueue(bool fishingWindowActive)
    {
        var account = configManager.GetCurrentAccount();
        if (account == null)
            return Array.Empty<FishingSelectionResult>();

        var roster = xaDatabase.GetFishingRoster();
        log.Information(
            $"[Fishing][Selection] XADB status={roster.Status}, rows={roster.Characters.Count}, " +
            $"generated={FormatSnapshotTimestamp(roster.GeneratedAtUtc)}, detail={roster.Detail}");
        if (!roster.IsUsable)
        {
            log.Warning(
                $"[Fishing][Selection] Selection failed closed because the XADB full roster is unavailable: " +
                $"status={roster.Status}, detail={roster.Detail}");
            return Array.Empty<FishingSelectionResult>();
        }

        var rosterByCharacterKey = roster.Characters.ToDictionary(
            entry => entry.CharacterKey,
            StringComparer.OrdinalIgnoreCase);
        var configuredCandidates = new List<FishingCharacterCandidate>();
        foreach (var pair in account.Characters)
        {
            configuredCandidates.Add(new FishingCharacterCandidate(
                pair.Key,
                null,
                pair.Value.EnableFishing,
                pair.Value.AlwaysFishOnThisCharacterIfWindowOpen,
                string.Equals(pair.Key, configManager.CurrentCharacterKey, StringComparison.OrdinalIgnoreCase)));
        }

        var candidates = FishingXadbCandidatePolicy.ApplyAuthoritativeLevels(
            configuredCandidates,
            roster);
        foreach (var candidate in candidates.Where(candidate => candidate.FishingEnabled))
        {
            rosterByCharacterKey.TryGetValue(candidate.CharacterKey, out var rosterEntry);
            log.Information(
                $"[Fishing][Selection] Enabled XADB candidate: key={candidate.CharacterKey}, " +
                $"fisher={FormatFisherLevel(candidate.FisherLevel)}, source={FormatRosterSource(rosterEntry)}, " +
                $"snapshot={FormatSnapshotTimestamp(rosterEntry?.SnapshotTimestamp)}, " +
                $"alwaysFish={candidate.AlwaysFishIfWindowOpen}, current={candidate.IsCurrentCharacter}");
        }

        var ordered = FishingSelectionPolicy.BuildOrderedCandidates(
            candidates,
            configuration.FishingMaxFisherLevel,
            configuration.FishingExecutionMode,
            configManager.CurrentCharacterKey,
            fishingWindowActive);
        var orderedDescription = ordered.Count == 0
            ? "<empty>"
            : string.Join(
                " -> ",
                ordered.Select((candidate, index) =>
                {
                    rosterByCharacterKey.TryGetValue(candidate.CharacterKey, out var entry);
                    return $"{index + 1}:{candidate.CharacterKey} " +
                           $"(Fisher {FormatFisherLevel(candidate.FisherLevel)}, " +
                           $"source={FormatRosterSource(entry)}, " +
                           $"snapshot={FormatSnapshotTimestamp(entry?.SnapshotTimestamp)})";
                }));
        log.Information($"[Fishing][Selection] Ordered XADB candidates: {orderedDescription}");
        log.Information(
            ordered.Count == 0
                ? "[Fishing][Selection] Selected character: <none>"
                : $"[Fishing][Selection] Selected character: {ordered[0].CharacterKey} " +
                  $"(Fisher {FormatFisherLevel(ordered[0].FisherLevel)})");
        return ordered;
    }

    private static string FormatFisherLevel(int? level)
        => level?.ToString(CultureInfo.InvariantCulture) ?? "unknown";

    private static string FormatRosterSource(XaFishingRosterEntry? entry)
        => entry == null || string.IsNullOrWhiteSpace(entry.Source) ? "missing" : entry.Source;

    private static string FormatSnapshotTimestamp(DateTimeOffset? timestamp)
        => timestamp?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? "unknown";

    public FishingSelectionResult SelectFishingTarget(bool fishingWindowActive)
    {
        var candidates = BuildFishingCandidateQueue(fishingWindowActive);
        return candidates.Count > 0
            ? candidates[0]
            : FishingSelectionResult.None("No eligible Ocean Fishing character is known.");
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
    {
        var territory = Plugin.ClientState.TerritoryType;
        var oceanTerritory = territory is 900 or 1163;
        return (oceanTerritory &&
                (Plugin.Condition[ConditionFlag.BoundByDuty] || Plugin.Condition[ConditionFlag.BoundByDuty56]) &&
                TryGetOceanFishingStatus(out _)) ||
               IsOceanFishingResultAddonAvailable();
    }

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
                    ObserveQueueRegistration("duty already active");
                    SetState(FishingState.MovingToFishingSpot);
                    break;
                }

                if (EnsureFisherJob())
                    SetState(FishingState.ValidatingUnlock);
                break;

            case FishingState.ValidatingUnlock:
                if (!IsOceanFishingUnlocked())
                {
                    Fail(
                        $"{configManager.CurrentCharacterKey} has not unlocked Ocean Fishing (quest {OceanFishingUnlockQuestId}).",
                        FishingAttemptFailureKind.CharacterPermanent);
                    break;
                }

                log.Information($"[Fishing] Ocean Fishing unlock quest {OceanFishingUnlockQuestId} verified complete");
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
                    Fail("ADS repair timed out.", FishingAttemptFailureKind.SharedTransient);
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
                    lureCountBeforeRestock = lureCount;
                    log.Information($"[Fishing] Versatile Lures below target ({lureCount}/{lureTarget}); starting restock");
                    vendorStockService.StartVersatileLureRestock(lureTarget);
                    SetState(FishingState.RestockingLures);
                    break;
                }

                if (lureCount <= 0)
                {
                    Fail(
                        "No usable Versatile Lure is available and lure restocking is disabled.",
                        FishingAttemptFailureKind.CharacterPermanent);
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
                    var availableLures = (int)GameHelpers.GetInventoryItemCount(VersatileLureItemId);
                    if (Math.Max(availableLures, lureCountBeforeRestock) > 0)
                    {
                        log.Warning($"[Fishing] Versatile Lure restock failed, but {availableLures} lure(s) are available; continuing with existing stock.");
                        SetState(FishingState.SettingBait);
                    }
                    else
                    {
                        Fail(
                            "Versatile Lure restock failed and no Versatile Lures are available.",
                            FishingAttemptFailureKind.CharacterPermanent);
                    }
                }
                break;

            case FishingState.SettingBait:
                if (GameHelpers.GetInventoryItemCount(VersatileLureItemId) == 0)
                {
                    Fail(
                        "No usable Versatile Lure remains after restocking.",
                        FishingAttemptFailureKind.CharacterPermanent);
                    break;
                }

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

            case FishingState.HandlingResult:
                TickHandleResult(elapsed);
                break;

            case FishingState.WaitingForCleanupReady:
                TickWaitForCleanupReady(elapsed);
                break;

            case FishingState.NavigatingToCleanupVendor:
                TickNavigateToCleanupVendor(elapsed);
                break;

            case FishingState.RunningInventoryCleanup:
                TickInventoryCleanup(elapsed);
                break;

            case FishingState.WaitingForDutyEntry:
                TickWaitForDutyEntry(elapsed);
                break;

            case FishingState.Returning:
                TickReturnSettlement(elapsed);
                break;

            case FishingState.CleaningUpLifecycle:
                runLifecycle.Update();
                if (!runLifecycle.IsActive)
                    SetState(FishingState.Complete);
                else
                    statusDetail = "Restoring AutoHook, AutoRetainer, and YesAlready";
                break;
        }
    }

    private bool EnsureFisherJob()
    {
        if (fisherGearsetOperation == null)
        {
            var context = runLifecycle.Current;
            if (context == null)
            {
                Fail("Cannot equip Fisher without an owned registration deadline.");
                return false;
            }

            var now = DateTimeOffset.UtcNow;
            fisherGearsetOperation = new FisherGearsetEquipOperation(
                fisherGearsetRuntime,
                now,
                context.RegistrationDeadlineUtc);
            log.Information(
                $"[Fishing][Gearset] Starting class-job ID: {fisherGearsetOperation.StartingClassJobId}; " +
                $"will retry until registration closes at {context.RegistrationDeadlineUtc:u}");
        }

        var terminalMessage = string.Empty;
        foreach (var entry in fisherGearsetOperation.Tick(DateTimeOffset.UtcNow))
        {
            if (entry.Kind == FisherGearsetEventKind.TransientFailure)
                log.Warning($"[Fishing][Gearset] {entry.Message}");
            else
                log.Information($"[Fishing][Gearset] {entry.Message}");

            if (entry.Kind == FisherGearsetEventKind.TerminalFailure)
                terminalMessage = entry.Message;
        }

        if (fisherGearsetOperation.Succeeded)
            return true;

        if (fisherGearsetOperation.IsComplete)
        {
            Fail(string.IsNullOrWhiteSpace(terminalMessage)
                ? $"Fisher gearset activation failed: {fisherGearsetOperation.State}."
                : terminalMessage,
                fisherGearsetOperation.State == FisherGearsetEquipState.MissingGearset
                    ? FishingAttemptFailureKind.CharacterPermanent
                    : FishingAttemptFailureKind.SharedTransient);
        }

        return false;
    }

    private void TickTravelToLimsa(TimeSpan elapsed)
    {
        if (IsOceanFishingDutyActive())
        {
            ObserveQueueRegistration("duty already active");
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
            Fail("Timed out traveling to Limsa for Ocean Fishing registration.", FishingAttemptFailureKind.SharedTransient);
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

        var registrar = GameHelpers.FindObjectByDataId(DryskthotaDataId) ??
                        GameHelpers.FindObjectByName("Dryskthota");
        var approachPosition = OceanFishingRegistrarPolicy.ResolveApproachPosition(
            DryskthotaPosition,
            registrar?.Position);
        var distance = DistanceTo(approachPosition);

        if (distance > 100 && !IsArcanistsGuildAethernetUnlocked() && !aethernetAttunementAttempted)
        {
            var shardName = GetArcanistsGuildAethernetName();
            var shard = string.IsNullOrWhiteSpace(shardName)
                ? null
                : GameHelpers.FindObjectByName(shardName);
            if (shard == null)
            {
                if (TryGetAethernetPosition(ArcanistsGuildAethernetId, out var shardPosition))
                {
                    if (aethernetAttunementNavigationStartedAt == DateTime.MinValue)
                        aethernetAttunementNavigationStartedAt = DateTime.UtcNow;

                    if (DateTime.UtcNow - aethernetAttunementNavigationStartedAt < TimeSpan.FromSeconds(60))
                    {
                        statusDetail = "Moving to locked Arcanists' Guild shard for attunement";
                        vnavmesh.PathfindAndMoveTo(shardPosition);
                        return;
                    }
                }

                aethernetAttunementAttempted = true;
                log.Warning("[Fishing] Arcanists' Guild shard 43 was locked but could not be found for one attunement attempt; navigating directly to Dryskthota");
            }
            else
            {
                var shardDistance = DistanceTo(shard.Position);
                if (shardDistance > 3.0)
                {
                    statusDetail = $"Moving to locked Arcanists' Guild shard ({shardDistance:F1}y)";
                    vnavmesh.PathfindAndMoveTo(shard.Position);
                    return;
                }

                vnavmesh.Stop();
                aethernetAttunementAttempted = true;
                aethernetAttunementStartedAt = DateTime.UtcNow;
                Plugin.TargetManager.Target = shard;
                GameHelpers.InteractWithObject(shard);
                log.Information("[Fishing] Attempted attunement of locked Arcanists' Guild shard 43");
                return;
            }
        }

        if (aethernetAttunementStartedAt != DateTime.MinValue && !IsArcanistsGuildAethernetUnlocked())
        {
            if (DateTime.UtcNow - aethernetAttunementStartedAt < OceanFishingAttunementPolicy.VerificationWait)
            {
                statusDetail = "Verifying Arcanists' Guild shard attunement";
                return;
            }

            aethernetAttunementStartedAt = DateTime.MinValue;
            log.Warning("[Fishing] Arcanists' Guild shard 43 did not unlock after the attunement attempt; navigating directly to Dryskthota");
        }

        if (!aethernetAttempted && distance > 100 && IsArcanistsGuildAethernetUnlocked())
        {
            aethernetAttempted = true;
            if (lifestream.AethernetTeleportById(ArcanistsGuildAethernetId))
            {
                log.Information($"[Fishing] Traveling to Arcanists' Guild via aethernet id {ArcanistsGuildAethernetId}");
                statusDetail = "Traveling to Arcanists' Guild";
                return;
            }

            log.Warning("[Fishing] Arcanists' Guild aethernet request failed; navigating directly");
        }

        if (registrar != null && OceanFishingRegistrarPolicy.IsWithinInteractionRange(distance))
        {
            vnavmesh.Stop();
            SetState(FishingState.InteractingRegistrar);
            return;
        }

        if (registrar == null && OceanFishingRegistrarPolicy.IsWithinInteractionRange(distance))
        {
            vnavmesh.Stop();
            statusDetail = "Waiting for Dryskthota to load";
            return;
        }

        if (elapsed > RegistrarNavigationTimeout)
        {
            Fail($"Timed out navigating to Dryskthota; distance={distance:F1}y.", FishingAttemptFailureKind.SharedTransient);
            return;
        }

        if (lastNavigationCommandAt == DateTime.MinValue ||
            DateTime.UtcNow - lastNavigationCommandAt >= TimeSpan.FromSeconds(2))
        {
            lastNavigationCommandAt = DateTime.UtcNow;
            log.Information($"[Fishing] Navigating to Dryskthota ({distance:F1}y, source={(registrar == null ? "fallback" : "live")})");
            vnavmesh.PathfindAndMoveTo(approachPosition);
        }
    }

    private void TickInteractWithRegistrar(TimeSpan elapsed)
    {
        if (IsOceanFishingDutyActive())
        {
            SetState(FishingState.MovingToFishingSpot);
            return;
        }

        if (TryObserveQueueRegistration())
        {
            return;
        }

        var registrar = GameHelpers.FindObjectByDataId(DryskthotaDataId) ??
                        GameHelpers.FindObjectByName("Dryskthota");
        if (registrar == null ||
            !OceanFishingRegistrarPolicy.IsWithinInteractionRange(DistanceTo(registrar.Position)))
        {
            log.Information("[Fishing] Dryskthota is not within the safe interaction range; resuming close approach");
            SetState(FishingState.NavigatingToRegistrar);
            return;
        }

        if (!IsRegistrationWindowOpen(out var window))
        {
            if (DateTimeOffset.UtcNow < window.RegistrationStartUtc)
            {
                statusDetail = $"Waiting for registration at {window.RegistrationStartUtc:u}";
                if (!waitingRegistrationLogged)
                {
                    waitingRegistrationLogged = true;
                    log.Information($"[Fishing] Waiting for registration at Dryskthota; opens={window.RegistrationStartUtc:u}, deadline={window.EndUtc:u}");
                }
                return;
            }

            Fail("Ocean Fishing registration closed before queue confirmation.", FishingAttemptFailureKind.Stop);
            return;
        }

        var boardingText = GetOceanFishingDialogueText(OceanFishingDialoguePolicy.BoardingRow);
        var entries = string.Empty;
        if (!string.IsNullOrWhiteSpace(boardingText) &&
            GameHelpers.TrySelectStringExact(boardingText, out entries))
        {
            registrationEntrySelected = true;
            log.Information($"[Fishing] Selected localized registration entry from CtsIkdEntrance_00663 row 4: '{boardingText}'");
            SetState(FishingState.ConfirmingRegistration);
            return;
        }

        if (GameHelpers.IsAddonVisible("SelectString") && !string.IsNullOrWhiteSpace(entries))
            statusDetail = $"Waiting for Register to board entry ({entries})";

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

        if (TryObserveQueueRegistration())
        {
            return;
        }

        if (TryHandleOceanFishingYesNo())
        {
            log.Information("[Fishing] Confirmed Ocean Fishing embark prompt");
            statusDetail = "Waiting for queue registration";
            return;
        }

        if (GameHelpers.IsAddonVisible("SelectString") && !routeSelectionHandled)
        {
            routeSelectionHandled = GameHelpers.TrySelectFirstStringEntry();
            if (routeSelectionHandled)
                log.Information("[Fishing] Selected Ocean Fishing route entry 0");
            return;
        }

        if (!IsRegistrationWindowOpen(out var window))
        {
            Fail("Ocean Fishing registration closed before queue confirmation was observed.", FishingAttemptFailureKind.Stop);
            return;
        }

        statusDetail = registrationEntrySelected
            ? $"Waiting for route/embark confirmation (deadline {window.EndUtc:u})"
            : "Waiting for registration selection";
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

        if (!queueRegistrationObserved && IsQueueRegistered())
            ObserveQueueRegistration("duty queue condition");

        if (GameHelpers.IsAddonVisible("ContentsFinderConfirm"))
        {
            if (GameHelpers.TryCommenceDuty())
            {
                dutyReadyAccepted = true;
                log.Information("[Fishing] Clicked ContentsFinderConfirm Commence");
                SetState(FishingState.WaitingForDutyEntry);
            }
            return;
        }

        if (elapsed > DepartureTimeout)
        {
            Fail("Timed out waiting for Ocean Fishing departure after queue confirmation.", FishingAttemptFailureKind.Stop);
            return;
        }

        statusDetail = "Registered for Ocean Fishing; waiting for duty-ready prompt";
    }

    private void TickWaitForDutyEntry(TimeSpan elapsed)
    {
        if (IsOceanFishingDutyActive())
        {
            ObserveQueueRegistration("duty entry");
            log.Information("[Fishing] Ocean Fishing duty entry observed");
            SetState(FishingState.MovingToFishingSpot);
            return;
        }

        if (GameHelpers.IsAddonVisible("ContentsFinderConfirm") &&
            DateTime.UtcNow - lastInteractionAttemptAt >= TimeSpan.FromSeconds(2))
        {
            lastInteractionAttemptAt = DateTime.UtcNow;
            GameHelpers.TryCommenceDuty();
        }

        if (elapsed > TimeSpan.FromMinutes(3))
        {
            Fail("Timed out waiting for actual Ocean Fishing duty entry after Commence.", FishingAttemptFailureKind.Stop);
            return;
        }

        statusDetail = dutyReadyAccepted
            ? "Commence accepted; waiting for duty entry"
            : "Waiting for duty entry";
    }

    private void TickMoveToFishingSpot(TimeSpan elapsed)
    {
        if (IsVoyageRouteTransitionActive())
        {
            statusDetail = "Waiting for Ocean Fishing route transition";
            return;
        }

        if (!IsOceanFishingDutyActive())
        {
            if (elapsed > TimeSpan.FromSeconds(30))
            {
            Fail("Ocean Fishing duty context was not active after departure.", FishingAttemptFailureKind.Stop);
                return;
            }

            statusDetail = "Waiting for Ocean Fishing duty context";
            return;
        }

        if (railPositionIndex >= BoatFishingPositions.Length)
        {
            Fail("Could not find a fishable Ocean Fishing rail position.", FishingAttemptFailureKind.Stop);
            return;
        }

        var position = BoatFishingPositions[railPositionIndex];
        var distance = DistanceTo(position);
        if (distance <= BoatFishingPositionTolerance)
        {
            vnavmesh.Stop();
            var rotation = position.X > 0 ? 1.5f : -1.5f;
            GameHelpers.TrySetLocalPlayerRotation(rotation);
            if (!runLifecycle.EnsureAutoHookEnabled(out var hookError))
            {
                Fail($"Could not enable AutoHook: {hookError}", FishingAttemptFailureKind.Stop);
                return;
            }

            CommandHelper.SendCommand("/bait Versatile Lure");
            railPositionReachedAt = DateTime.UtcNow;
            canFishUnavailableSince = DateTime.MinValue;
            ResetCastAttemptState();
            log.Information(
                $"[Fishing][Cast] Rail position {railPositionIndex + 1}/{BoatFishingPositions.Length} reached; " +
                $"rotation={rotation:F1}, waiting {FishingCastPolicy.FirstAttemptDelay.TotalSeconds:F0}s for movement/bait settlement");
            SetState(FishingState.Fishing);
            return;
        }

        railPositionReachedAt = DateTime.MinValue;

        if (lastNavigationCommandAt == DateTime.MinValue ||
            DateTime.UtcNow - lastNavigationCommandAt >= TimeSpan.FromSeconds(2))
        {
            lastNavigationCommandAt = DateTime.UtcNow;
            log.Information($"[Fishing] Moving to Ocean Fishing rail position {railPositionIndex + 1}/{BoatFishingPositions.Length} ({distance:F1}y)");
            vnavmesh.PathfindAndMoveTo(position);
        }
    }

    private static bool IsInLimsaAndReady()
        => Plugin.ClientState.TerritoryType == LimsaTerritoryType &&
           !Plugin.Condition[ConditionFlag.BetweenAreas] &&
           !Plugin.Condition[ConditionFlag.BetweenAreas51] &&
           GameHelpers.IsPlayerAvailable();

    private static bool IsQueueRegistered()
        => Plugin.Condition[ConditionFlag.WaitingForDuty] ||
           Plugin.Condition[ConditionFlag.WaitingForDutyFinder];

    private bool TryObserveQueueRegistration()
    {
        if (!IsQueueRegistered())
            return false;

        ObserveQueueRegistration("duty queue condition");
        SetState(FishingState.WaitingForDeparture);
        return true;
    }

    private void ObserveQueueRegistration(string reason)
    {
        if (!queueRegistrationObserved)
            log.Information("[Fishing] Ocean Fishing queue registration observed");

        queueRegistrationObserved = true;
        runLifecycle.MarkQueueRegistrationConfirmed(reason);
    }

    private bool IsOceanFishingDutyActive()
    {
        var territory = Plugin.ClientState.TerritoryType;
        if (territory is not 900 and not 1163)
            return false;

        var dutyKnown = dutyState.IsDutyStarted ||
                        Plugin.Condition[ConditionFlag.BoundByDuty] ||
                        Plugin.Condition[ConditionFlag.BoundByDuty56];
        return dutyKnown && TryGetOceanFishingStatus(out _);
    }

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
        var context = runLifecycle.Current;
        if (context == null)
        {
            window = default;
            return false;
        }

        window = new OceanFishingStartupWindow(
            context.RegistrationStartUtc,
            context.RegistrationStartUtc,
            context.RegistrationDeadlineUtc);
        var registrationDecision = OceanFishingRegistrationPolicy.Decide(
            queueConfirmed: false,
            nowUtc: now,
            registrationDeadlineUtc: context.RegistrationDeadlineUtc,
            genuineFailure: false);
        return now >= window.RegistrationStartUtc &&
               registrationDecision == OceanFishingRegistrationDecision.ContinueDialogs;
    }

    private static bool TryHandleOceanFishingYesNo()
    {
        var embarkText = GetOceanFishingDialogueText(OceanFishingDialoguePolicy.EmbarkRow);
        return GameHelpers.TryClickYesIfPromptAllowed(
            prompt => OceanFishingDialoguePolicy.MatchesEmbarkPrompt(prompt, embarkText),
            "Ocean Fishing registration/embark",
            allowUnreadable: false,
            out _,
            OceanFishingDialoguePolicy.DescribeEmbarkExpectation(embarkText));
    }

    private static string GetOceanFishingDialogueText(uint rowId)
    {
        try
        {
            var sheet = Plugin.DataManager.GetExcelSheet<QuestDialogueText>(
                name: OceanFishingDialoguePolicy.SheetName);
            return sheet.TryGetRow(rowId, out var row)
                ? row.Value.ToString().Trim()
                : string.Empty;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning($"[Fishing] Could not read localized CtsIkdEntrance_00663 row {rowId}: {ex.Message}");
            return string.Empty;
        }
    }

    private static string GetArcanistsGuildAethernetName()
    {
        try
        {
            var sheet = Plugin.DataManager.GetExcelSheet<Aetheryte>();
            return sheet.TryGetRow(ArcanistsGuildAethernetId, out var row)
                ? row.AethernetName.Value.Name.ToString().Trim()
                : string.Empty;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning($"[Fishing] Could not resolve localized aethernet name for row {ArcanistsGuildAethernetId}: {ex.Message}");
            return string.Empty;
        }
    }

    private static bool TryGetAethernetPosition(uint aetheryteId, out Vector3 position)
    {
        position = default;
        try
        {
            var aetherytes = Plugin.DataManager.GetExcelSheet<Aetheryte>();
            if (!aetherytes.TryGetRow(aetheryteId, out var aetheryte))
                return false;

            var map = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Map>()
                .FirstOrDefault(candidate => candidate.TerritoryType.RowId == aetheryte.Territory.RowId);
            if (map.RowId == 0)
                return false;

            var marker = Plugin.DataManager.GetSubrowExcelSheet<MapMarker>()
                .SelectMany(row => row)
                .FirstOrDefault(candidate =>
                    candidate.DataType == (aetheryte.IsAetheryte ? 3 : 4) &&
                    candidate.DataKey.RowId == (aetheryte.IsAetheryte
                        ? aetheryte.RowId
                        : aetheryte.AethernetName.RowId));
            if (marker.RowId == 0)
                return false;

            var scale = map.SizeFactor / 100f;
            var x = (marker.X - 1024f) / scale;
            var z = (marker.Y - 1024f) / scale;
            position = new Vector3(x, Plugin.ObjectTable.LocalPlayer?.Position.Y ?? 0f, z);
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning($"[Fishing] Could not resolve aethernet map position for row {aetheryteId}: {ex.Message}");
            return false;
        }
    }

    private static unsafe bool IsArcanistsGuildAethernetUnlocked()
    {
        try
        {
            return UIState.Instance()->IsAetheryteUnlocked(ArcanistsGuildAethernetId);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsOceanFishingUnlocked()
    {
        var quests = Plugin.DataManager.GetExcelSheet<Quest>();
        return quests.TryGetRow(OceanFishingUnlockQuestId, out var quest) &&
               Plugin.UnlockState.IsQuestCompleted(quest);
    }

    private static unsafe bool CanFish()
    {
        try
        {
            var framework = EventFramework.Instance();
            return framework != null && framework->EventHandlerModule.FishingEventHandler->CanFish;
        }
        catch
        {
            return false;
        }
    }

    private static unsafe bool TryGetOceanFishingStatus(out InstanceContentOceanFishing.OceanFishingStatus status)
    {
        status = default;
        try
        {
            var framework = EventFramework.Instance();
            if (framework == null)
                return false;

            var instance = framework->GetInstanceContentOceanFishing();
            if (instance == null)
                return false;

            status = instance->Status;
            return true;
        }
        catch
        {
            return false;
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
            Fail($"ADS repair failed to start: {failure}", FishingAttemptFailureKind.SharedTransient);
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
        var inFishingContext = IsOceanFishingDutyActive();
        if (inFishingContext)
            sawFishingContext = true;

        var resultVisible = IsOceanFishingResultAddonAvailable();
        if (dutyCompletionObserved || resultVisible)
        {
            CancelCastRetry("voyage result/completion");
            SetState(FishingState.HandlingResult);
            return;
        }

        var routeTransitionActive = IsVoyageRouteTransitionActive();
        if (routeTransitionActive)
        {
            if (zoneTransitionStartedAt == DateTime.MinValue)
            {
                zoneTransitionStartedAt = now;
                CancelCastRetry("route transition");
                vnavmesh.Stop();
                log.Information("[Fishing][Cast] Route transition detected; cast retries paused until the next route is ready");
            }

            if (now - zoneTransitionStartedAt >= ZoneTransitionTimeout)
            {
                Fail("Ocean Fishing zone transition remained stalled for 90 seconds.", FishingAttemptFailureKind.Stop);
                return;
            }

            statusDetail = "Waiting for Ocean Fishing route transition";
            return;
        }

        if (zoneTransitionStartedAt != DateTime.MinValue)
        {
            zoneTransitionStartedAt = DateTime.MinValue;
            if (inFishingContext)
            {
                log.Information("[Fishing][Cast] Route transition completed; reacquiring an Ocean Fishing rail position");
                railPositionIndex = 0;
                railPositionReachedAt = DateTime.MinValue;
                canFishUnavailableSince = DateTime.MinValue;
                SetState(FishingState.MovingToFishingSpot);
                return;
            }
        }

        if (!inFishingContext)
        {
            CancelCastRetry("Ocean Fishing duty context inactive");
            if (OceanFishingCompletionPolicy.ShouldInferFromDutyContextLoss(
                    sawFishingContext,
                    Plugin.ClientState.TerritoryType is 900 or 1163,
                    GameHelpers.IsPlayerAvailable(),
                    Plugin.Condition[ConditionFlag.BetweenAreas] ||
                    Plugin.Condition[ConditionFlag.BetweenAreas51]))
            {
                log.Information("[Fishing] Previously observed Ocean Fishing duty context disappeared; inferring voyage completion");
                dutyCompletionObserved = true;
                SetState(FishingState.HandlingResult);
                return;
            }

            if (dutyContextLostAt == DateTime.MinValue)
                dutyContextLostAt = now;

            if (sawFishingContext && now - dutyContextLostAt >= TimeSpan.FromSeconds(30))
            {
                Fail("Ocean Fishing duty context disappeared without a settled post-duty state.", FishingAttemptFailureKind.Stop);
                return;
            }

            if (elapsed >= DutyCompletionTimeout)
            {
                Fail("Timed out waiting for Ocean Fishing duty completion.");
            }

            return;
        }

        dutyContextLostAt = DateTime.MinValue;
        var gathering = Plugin.Condition[GatheringCondition];
        var fishing = Plugin.Condition[FishingCondition];
        var canFish = CanFish();

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

        var railPositionReady =
            railPositionReachedAt != DateTime.MinValue &&
            railPositionIndex >= 0 &&
            railPositionIndex < BoatFishingPositions.Length &&
            DistanceTo(BoatFishingPositions[railPositionIndex]) <= BoatFishingPositionTolerance;
        var evaluation = FishingCastPolicy.Evaluate(
                enabled: true,
                inFishingContext,
                zoneTransitionActive: false,
                railPositionReady,
                canFish,
                playerAvailable: GameHelpers.IsPlayerAvailable(),
                gatheringConditionActive: gathering,
                fishingConditionActive: fishing,
                busy,
                resultWindowVisible: resultVisible,
                railSettlementElapsed: railPositionReachedAt == DateTime.MinValue
                    ? TimeSpan.Zero
                    : now - railPositionReachedAt,
                sinceLastAttempt: lastCastAt == DateTime.MinValue
                    ? TimeSpan.MaxValue
                    : now - lastCastAt);

        if (evaluation.Decision == FishingCastDecision.Acknowledged)
        {
            canFishUnavailableSince = DateTime.MinValue;
            if (!castAcknowledged)
            {
                castAcknowledged = true;
                var acknowledgement = fishing ? "Fishing" : "Gathering";
                log.Information(
                    $"[Fishing][Cast] Acknowledged by {acknowledgement} condition after {castAttemptCount} attempt(s) " +
                    $"at rail position {railPositionIndex + 1}");
            }
            return;
        }

        if (evaluation.Decision == FishingCastDecision.Suppressed)
        {
            LogCastGate(evaluation.Gate);

            if (evaluation.Gate == "CanFish false")
            {
                if (canFishUnavailableSince == DateTime.MinValue)
                    canFishUnavailableSince = now;

                if (FishingCastPolicy.ShouldAdvanceRail(
                        canFish,
                        gathering,
                        fishing,
                        GameHelpers.IsPlayerAvailable(),
                        busy,
                        now - canFishUnavailableSince))
                    AdvanceToNextRail("CanFish remained false for 10 seconds");
            }
            else if (evaluation.Gate is "player unavailable" or "player occupied/casting")
            {
                // Busy time does not count toward abandoning a rail position.
                canFishUnavailableSince = DateTime.MinValue;
            }

            if (evaluation.Gate == "rail position invalid")
            {
                log.Warning($"[Fishing][Cast] Rail position {railPositionIndex + 1} is no longer valid; reacquiring it");
                railPositionReachedAt = DateTime.MinValue;
                canFishUnavailableSince = DateTime.MinValue;
                ResetCastAttemptState();
                SetState(FishingState.MovingToFishingSpot);
            }
            return;
        }

        canFishUnavailableSince = DateTime.MinValue;
        castAcknowledged = false;
        castAttemptCount++;
        lastCastAt = now;
        CommandHelper.SendCommand(FishingCastPolicy.CastCommand);
        lastCastGate = string.Empty;
        log.Information(
            $"[Fishing][Cast] Attempt {castAttemptCount}: sent {FishingCastPolicy.CastCommand} " +
            $"at rail position {railPositionIndex + 1}; awaiting Fishing/Gathering acknowledgement");
    }

    private static bool IsVoyageRouteTransitionActive()
        => (TryGetOceanFishingStatus(out var oceanStatus) &&
            oceanStatus == InstanceContentOceanFishing.OceanFishingStatus.NewZone) ||
           Plugin.Condition[ConditionFlag.BetweenAreas] ||
           Plugin.Condition[ConditionFlag.BetweenAreas51] ||
           Plugin.Condition[ConditionFlag.OccupiedInCutSceneEvent] ||
           Plugin.Condition[ConditionFlag.WatchingCutscene];

    private void AdvanceToNextRail(string reason)
    {
        var previousRail = railPositionIndex + 1;
        railPositionIndex++;
        railPositionReachedAt = DateTime.MinValue;
        canFishUnavailableSince = DateTime.MinValue;
        lastNavigationCommandAt = DateTime.MinValue;
        ResetCastAttemptState();
        log.Warning(
            $"[Fishing][Cast] {reason} at rail position {previousRail}; " +
            $"advancing to rail position {railPositionIndex + 1}/{BoatFishingPositions.Length}");
        SetState(FishingState.MovingToFishingSpot);
    }

    private void CancelCastRetry(string reason)
    {
        if (castAttemptCount > 0 || railPositionReachedAt != DateTime.MinValue)
            log.Debug($"[Fishing][Cast] Pending cast retry cancelled: {reason}");

        railPositionReachedAt = DateTime.MinValue;
        canFishUnavailableSince = DateTime.MinValue;
        ResetCastAttemptState();
    }

    private void ResetCastAttemptState()
    {
        lastCastAt = DateTime.MinValue;
        castAttemptCount = 0;
        castAcknowledged = false;
        lastCastGate = string.Empty;
    }

    private void LogCastGate(string gate)
    {
        if (string.Equals(lastCastGate, gate, StringComparison.Ordinal))
            return;

        lastCastGate = gate;
        log.Debug(
            $"[Fishing][Cast] Attempt {castAttemptCount + 1} suppressed: {gate}; " +
            $"rail={railPositionIndex + 1}/{BoatFishingPositions.Length}");
    }

    private static bool IsOceanFishingResultAddonAvailable()
        => GameHelpers.IsIKDResultAddonPresent(out _);

    private bool TryCloseResultWindow(out string addonState)
    {
        addonState = lastResultAddonState;
        var now = DateTime.UtcNow;
        if (lastResultCloseAttemptAt != DateTime.MinValue && now - lastResultCloseAttemptAt < ResultCloseDelay)
            return false;

        lastResultCloseAttemptAt = now;
        var closeFired = GameHelpers.TryCloseIKDResult(out _, out addonState);
        lastResultAddonState = addonState;
        return closeFired;
    }

    private void TickHandleResult(TimeSpan elapsed)
    {
        var closeFired = TryCloseResultWindow(out var closeAttemptState);
        var resultAddonAvailable = GameHelpers.IsIKDResultAddonPresent(out var resultAddonState);
        if (resultAddonAvailable)
        {
            statusDetail = "Closing Ocean Fishing result";
            var stateDetails = string.IsNullOrWhiteSpace(resultAddonState) ? closeAttemptState : resultAddonState;
            if (elapsed < ResultSettlementTimeout)
            {
                LogResultSettlementWait(closeFired
                    ? $"IKDResult close callback fired; waiting for result addon to settle. state={stateDetails}"
                    : $"IKDResult close is pending or the addon is not ready. state={stateDetails}");
                return;
            }

            LogResultFallback($"IKDResult callback failed; addon still present after {ResultSettlementTimeout.TotalSeconds:F0}s. state={stateDetails}");
        }

        var stillInOceanFishingTerritory = Plugin.ClientState.TerritoryType is 900 or 1163;
        if (!dutyCompletionObserved && stillInOceanFishingTerritory)
        {
            if (elapsed < ResultSettlementTimeout)
            {
                statusDetail = "Waiting for Ocean Fishing completion";
                LogResultSettlementWait("duty completion has not settled yet");
                return;
            }

            LogResultFallback("Ocean Fishing completion did not settle");
        }

        if (!GameHelpers.IsPlayerAvailable() ||
            Plugin.Condition[ConditionFlag.BetweenAreas] ||
            Plugin.Condition[ConditionFlag.BetweenAreas51])
        {
            statusDetail = "Waiting for post-voyage player settlement";
            LogResultSettlementWait("player is unavailable or between areas");
            return;
        }

        BeginInventoryCleanup();
    }

    private void LogResultSettlementWait(string reason)
    {
        var now = DateTime.UtcNow;
        if (lastResultSettlementWaitLogAt != DateTime.MinValue &&
            now - lastResultSettlementWaitLogAt < ResultSettlementWaitLogInterval)
            return;

        lastResultSettlementWaitLogAt = now;
        log.Information($"[Fishing] Result handling waiting: {reason}");
    }

    private void LogResultFallback(string reason)
    {
        if (resultFallbackLogged)
            return;

        resultFallbackLogged = true;
        log.Warning($"[Fishing] Two-minute result fallback reached: {reason}; continuing cleanup and return");
    }

    private void BeginInventoryCleanup()
    {
        var config = configManager.GetActiveConfig();
        cleanupCommands = FishingInventoryCleanupPolicy.Build(
            config.FishingDiscardAfterVoyage,
            config.FishingSellAfterVoyage);
        cleanupCommandIndex = 0;
        cleanupCommandSent = false;
        cleanupBusyObserved = false;

        if (cleanupCommands.Count == 0)
        {
            ReturnAfterFishing();
            return;
        }

        log.Information($"[Fishing][Cleanup] Starting post-voyage cleanup: {string.Join(", ", cleanupCommands)}");
        SetState(FishingState.WaitingForCleanupReady);
    }

    private void TickWaitForCleanupReady(TimeSpan elapsed)
    {
        var busy = autoRetainer.ReadBusyState();
        if (!busy.Success)
        {
            statusDetail = "Waiting for readable AutoRetainer cleanup state";
            if (elapsed < CleanupReadyTimeout)
                return;

            log.Warning($"[Fishing][Cleanup] AutoRetainer state stayed unreadable; skipping remaining cleanup: {busy.Error}");
            ReturnAfterFishing();
            return;
        }

        if (busy.Busy)
        {
            statusDetail = "Waiting for AutoRetainer to become idle";
            if (elapsed < CleanupReadyTimeout)
                return;

            log.Warning("[Fishing][Cleanup] AutoRetainer stayed busy; skipping remaining cleanup");
            ReturnAfterFishing();
            return;
        }

        if (cleanupCommandIndex >= cleanupCommands.Count)
        {
            ReturnAfterFishing();
            return;
        }

        if (cleanupCommands[cleanupCommandIndex] == FishingCleanupCommand.Sell)
            SetState(FishingState.NavigatingToCleanupVendor);
        else
            SetState(FishingState.RunningInventoryCleanup);
    }

    private void TickNavigateToCleanupVendor(TimeSpan elapsed)
    {
        if (!IsInLimsaAndReady())
        {
            if (lastTravelCommandAt == DateTime.MinValue ||
                DateTime.UtcNow - lastTravelCommandAt >= TimeSpan.FromSeconds(10))
            {
                lastTravelCommandAt = DateTime.UtcNow;
                lifestream.ExecuteCommand("/li limsa");
            }

            if (elapsed < LimsaTravelTimeout)
            {
                statusDetail = "Traveling to Limsa Merchant & Mender for item selling";
                return;
            }

            log.Warning("[Fishing][Cleanup] Could not reach Limsa for /ays itemsell; continuing to return");
            AdvanceInventoryCleanup();
            return;
        }

        var vendor = GameHelpers.FindObjectByName("Merchant & Mender");
        if (vendor != null && DistanceTo(vendor.Position) <= 12.0)
        {
            vnavmesh.Stop();
            SetState(FishingState.RunningInventoryCleanup);
            return;
        }

        if (vendor != null)
        {
            statusDetail = $"Moving near Merchant & Mender ({DistanceTo(vendor.Position):F1}y)";
            vnavmesh.PathfindAndMoveTo(vendor.Position);
        }
        else
        {
            statusDetail = "Waiting for Limsa Merchant & Mender to load";
        }

        if (elapsed >= RegistrarNavigationTimeout)
        {
            log.Warning("[Fishing][Cleanup] Could not navigate near Merchant & Mender; skipping /ays itemsell");
            AdvanceInventoryCleanup();
        }
    }

    private void TickInventoryCleanup(TimeSpan elapsed)
    {
        if (cleanupCommandIndex >= cleanupCommands.Count)
        {
            ReturnAfterFishing();
            return;
        }

        var command = cleanupCommands[cleanupCommandIndex] switch
        {
            FishingCleanupCommand.Discard => "/ays discard",
            FishingCleanupCommand.Sell => "/ays itemsell",
            _ => string.Empty,
        };

        if (!cleanupCommandSent)
        {
            cleanupCommandSent = true;
            log.Information($"[Fishing][Cleanup] Sending {command}");
            CommandHelper.SendCommand(command);
        }

        var busy = autoRetainer.ReadBusyState();
        if (!busy.Success)
        {
            if (elapsed < CleanupWorkTimeout)
                return;

            log.Warning($"[Fishing][Cleanup] Could not observe AutoRetainer work for {command}: {busy.Error}");
            AdvanceInventoryCleanup();
            return;
        }

        if (busy.Busy)
        {
            cleanupBusyObserved = true;
            statusDetail = $"AutoRetainer is processing {command}";
            if (elapsed < CleanupWorkTimeout)
                return;

            log.Warning($"[Fishing][Cleanup] AutoRetainer work timed out for {command}");
            AdvanceInventoryCleanup();
            return;
        }

        if (cleanupBusyObserved ||
            FishingInventoryCleanupPolicy.TreatAsNothingToProcess(cleanupBusyObserved, elapsed))
        {
            log.Information(
                cleanupBusyObserved
                    ? $"[Fishing][Cleanup] Observed AutoRetainer complete {command}"
                    : $"[Fishing][Cleanup] No busy transition for {command}; treating as nothing to process");
            AdvanceInventoryCleanup();
        }
    }

    private void AdvanceInventoryCleanup()
    {
        cleanupCommandIndex++;
        cleanupCommandSent = false;
        cleanupBusyObserved = false;
        if (cleanupCommandIndex >= cleanupCommands.Count)
        {
            ReturnAfterFishing();
            return;
        }

        SetState(FishingState.WaitingForCleanupReady);
    }

    private void ReturnAfterFishing()
    {
        var command = ResolveReturnCommand();
        if (!string.IsNullOrWhiteSpace(command))
        {
            log.Information($"[Fishing] Fishing context ended; returning with {command}");
            SendReturnCommand(command);
            returnCommandSent = true;
        }

        returnStartedAt = DateTime.UtcNow;
        returnStartedTerritory = Plugin.ClientState.TerritoryType;
        SetState(FishingState.Returning);
    }

    private void TickReturnSettlement(TimeSpan elapsed)
    {
        if (returnStartedAt == DateTime.MinValue)
            returnStartedAt = DateTime.UtcNow;

        var transitioning = lifestream.IsBusy() ||
                            Plugin.Condition[ConditionFlag.BetweenAreas] ||
                            Plugin.Condition[ConditionFlag.BetweenAreas51] ||
                            !GameHelpers.IsPlayerAvailable();
        if (transitioning)
            returnTransitionObserved = true;

        if (transitioning)
        {
            statusDetail = "Waiting for configured return transition";
        }

        var territoryChanged = Plugin.ClientState.TerritoryType != returnStartedTerritory;
        if (FishingReturnPolicy.IsVerified(
                commandRequired: returnCommandSent,
                activityObserved: returnTransitionObserved,
                territoryChanged,
                currentlyBusy: transitioning))
        {
            log.Information($"[Fishing] Return settled; territory={Plugin.ClientState.TerritoryType}, changed={territoryChanged}, transitionObserved={returnTransitionObserved}");
            runLifecycle.Cleanup("Ocean Fishing completed");
            SetState(FishingState.CleaningUpLifecycle);
            return;
        }

        var returnElapsed = DateTime.UtcNow - returnStartedAt;
        if (FishingReturnPolicy.ShouldRetry(returnCommandsSent, returnElapsed))
        {
            var command = ResolveReturnCommand();
            log.Warning($"[Fishing] Return command produced no observable activity after 30 seconds; retrying once: {command}");
            SendReturnCommand(command);
            return;
        }

        if (returnElapsed >= FishingReturnPolicy.FailAfter || elapsed > ReturnSettlementTimeout)
            Fail("Timed out waiting for configured Ocean Fishing return to settle.");
    }

    private void SendReturnCommand(string command)
    {
        returnCommandsSent++;
        if (command.StartsWith("/li ", StringComparison.OrdinalIgnoreCase))
            lifestream.ExecuteCommand(command);
        else
            CommandHelper.SendCommand(command);
    }

    private void OnDutyCompleted(IDutyStateEventArgs args)
    {
        if (args.TerritoryType.RowId is not 900 and not 1163)
            return;

        dutyCompletionObserved = true;
        log.Information($"[Fishing] Duty completion event observed for Ocean Fishing territory {args.TerritoryType.RowId}");
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

    private void Fail(string message, FishingAttemptFailureKind kind = FishingAttemptFailureKind.Stop)
    {
        lastError = message;
        failureKind = queueRegistrationObserved ? FishingAttemptFailureKind.Stop : kind;
        log.Warning($"[Fishing] {message}");
        if (failureKind == FishingAttemptFailureKind.Stop && !queueRegistrationObserved)
            runLifecycle.MarkTerminalFailureBeforeQueueConfirmation(message);
        vendorStockService.Reset();
        vnavmesh.Stop();
        SetState(FishingState.Failed);
        runLifecycle.Cleanup(message);
    }

    private void SetState(FishingState newState)
    {
        log.Information($"[Fishing] {state} -> {newState}");
        state = newState;
        stateEnteredAt = DateTime.UtcNow;
        statusDetail = string.Empty;

        if (newState is FishingState.NavigatingToRegistrar or
            FishingState.MovingToFishingSpot or
            FishingState.NavigatingToCleanupVendor)
            lastNavigationCommandAt = DateTime.MinValue;

        if (newState == FishingState.NavigatingToCleanupVendor)
            lastTravelCommandAt = DateTime.MinValue;

        if (newState == FishingState.InteractingRegistrar)
            lastInteractionAttemptAt = DateTime.MinValue;

        if (newState == FishingState.WaitingForDeparture)
            departureWaitStartedAt = DateTime.UtcNow;

        if (newState == FishingState.Fishing)
        {
            lastFishingLoopPollAt = DateTime.MinValue;
            dutyStartedAt = DateTime.UtcNow;
            sawFishingContext = true;
        }

        if (newState == FishingState.HandlingResult)
        {
            lastResultCloseAttemptAt = DateTime.MinValue;
            lastResultSettlementWaitLogAt = DateTime.MinValue;
            lastResultAddonState = string.Empty;
            resultFallbackLogged = false;
        }
    }
}
