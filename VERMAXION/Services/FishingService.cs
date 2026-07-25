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
    private const ushort LimsaTerritoryType = OceanFishingDockPreparationPolicy.LimsaTerritoryType;
    private const uint ArcanistsGuildAethernetId = OceanFishingDockPreparationPolicy.ArcanistsGuildAethernetId;
    private const uint OceanFishingUnlockQuestId = 69379;
    private const uint MerchantAndMenderDataId = OceanFishingDockPreparationPolicy.MerchantAndMenderDataId;
    private const uint DryskthotaDataId = OceanFishingDockPreparationPolicy.DryskthotaDataId;
    private const uint VersatileLureItemId = OceanFishingDockPreparationPolicy.VersatileLureItemId;
    private const string OceanFishingResultAddonName = "IKDResult";
    private const float BoatFishingPositionTolerance = 0.5f;
    private const ConditionFlag GatheringCondition = (ConditionFlag)6;
    private const ConditionFlag FishingCondition = (ConditionFlag)43;
    private static readonly Vector3 MerchantAndMenderPosition = OceanFishingDockPreparationPolicy.MerchantAndMenderPosition;
    private static readonly Vector3 DryskthotaPosition = OceanFishingDockPreparationPolicy.DryskthotaPosition;
    private static readonly TimeSpan FishingLoopPollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan LimsaTravelTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan DockNavigationTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan RegistrarNavigationTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan DepartureTimeout = TimeSpan.FromMinutes(35);
    private static readonly TimeSpan DutyCompletionTimeout = TimeSpan.FromHours(3);
    private static readonly TimeSpan RepairTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ShopPurchaseTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ResultSettlementTimeout = OceanFishingResultClosePolicy.Timeout;
    private static readonly TimeSpan ReturnSettlementTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ZoneTransitionTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan CleanupReadyTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan CleanupWorkTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RailSampleRetryInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan NavigationStopRetryInterval = TimeSpan.FromSeconds(1);

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
    private readonly FisherFallbackService fisherFallbackService;
    private readonly IDutyState dutyState;

    private FishingState state = FishingState.Idle;
    private DateTime stateEnteredAt = DateTime.MinValue;
    private DateTime repairStartedAt = DateTime.MinValue;
    private DateTime lastResultPollAt = DateTime.MinValue;
    private DateTime lastResultCallbackAt = DateTime.MinValue;
    private OceanFishingResultAddonSnapshot lastResultAddonSnapshot = OceanFishingResultAddonSnapshot.NotPolled;
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
    private bool embarkConfirmationAccepted;
    private bool queueRegistrationObserved;
    private bool queueRecognitionGraceEntered;
    private bool lateQueueRecognitionLogged;
    private bool dutyReadyAccepted;
    private bool dutyCompletionObserved;
    private bool resultFallbackLogged;
    private bool resultCallbackDispatched;
    private bool resultWindowClosed;
    private bool resultPostVoyageTransitionObserved;
    private bool resultDetectionLogged;
    private bool resultTransitionLogged;
    private bool resultCallbackLogged;
    private bool resultClosureLogged;
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
    private OceanFishingRailDestination? currentRailDestination;
    private OceanFishingRailDestination? railSampleExclusionDestination;
    private DateTime nextRailSampleAt = DateTime.MinValue;
    private readonly OceanFishingVoyageState voyageState = new();
    private string lastCastGate = string.Empty;
    private DateTime returnStartedAt = DateTime.MinValue;
    private uint returnStartedTerritory;
    private FishingRunMode activeRunMode;
    private string lastError = string.Empty;
    private string statusDetail = string.Empty;
    private FisherGearsetEquipOperation? fisherGearsetOperation;
    private bool fisherFallbackStarted;
    private IReadOnlyList<FishingStockRequirement> fishingStockRequirements =
        Array.Empty<FishingStockRequirement>();
    private readonly List<FishingStockPurchaseOutcome> fishingStockPartialFailures = [];
    private int fishingStockRequirementIndex;
    private FishingStockRequirement? activeFishingStockRequirement;
    private DateTime fishingStockPurchaseStartedAt = DateTime.MinValue;
    private bool fishingStockPurchaseOwned;
    private FishingAttemptFailureKind failureKind = FishingAttemptFailureKind.Stop;
    private bool failureReported;

    public enum FishingState
    {
        Idle,
        SwitchingToFisher,
        ValidatingUnlock,
        TravelingToLimsa,
        CheckingPreparation,
        NavigatingToPreparationDock,
        CheckingRepair,
        WaitingForRepair,
        CheckingLures,
        RestockingLures,
        SettingBait,
        NavigatingToRegistrar,
        InteractingRegistrar,
        ConfirmingRegistration,
        WaitingForQueueRecognitionGrace,
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
        fisherFallbackService = new FisherFallbackService(
            Plugin.DataManager,
            Plugin.Framework,
            Plugin.PlayerState,
            adsIpcClient,
            log);
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
        repairStartedAt = DateTime.MinValue;
        ResetResultHandlingState();
        lastFishingLoopPollAt = DateTime.MinValue;
        fisherGearsetOperation = null;
        fisherFallbackStarted = false;
        ResetFishingStockPurchase(cancelOwned: true);
        lastTravelCommandAt = DateTime.MinValue;
        travelStartedAt = DateTime.MinValue;
        lastNavigationCommandAt = DateTime.MinValue;
        lastInteractionAttemptAt = DateTime.MinValue;
        departureWaitStartedAt = DateTime.MinValue;
        dutyStartedAt = DateTime.MinValue;
        registrationEntrySelected = false;
        routeSelectionHandled = false;
        embarkConfirmationAccepted = false;
        queueRegistrationObserved = false;
        queueRecognitionGraceEntered = false;
        lateQueueRecognitionLogged = false;
        dutyReadyAccepted = false;
        dutyCompletionObserved = false;
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
        currentRailDestination = null;
        railSampleExclusionDestination = null;
        nextRailSampleAt = DateTime.MinValue;
        voyageState.Reset();
        lastCastGate = string.Empty;
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
        if (CurrentRailDestination.HasValue && IsOceanFishingDutyActive())
            StopFishingNavigationAndFaceOutward("fishing service reset");
        else
            vnavmesh.Stop();

        state = FishingState.Idle;
        stateEnteredAt = DateTime.MinValue;
        repairStartedAt = DateTime.MinValue;
        ResetResultHandlingState();
        lastFishingLoopPollAt = DateTime.MinValue;
        fisherGearsetOperation = null;
        fisherFallbackService.Reset();
        fisherFallbackStarted = false;
        ResetFishingStockPurchase(cancelOwned: true);
        lastTravelCommandAt = DateTime.MinValue;
        travelStartedAt = DateTime.MinValue;
        lastNavigationCommandAt = DateTime.MinValue;
        lastInteractionAttemptAt = DateTime.MinValue;
        departureWaitStartedAt = DateTime.MinValue;
        dutyStartedAt = DateTime.MinValue;
        sawFishingContext = false;
        registrationEntrySelected = false;
        routeSelectionHandled = false;
        embarkConfirmationAccepted = false;
        queueRegistrationObserved = false;
        queueRecognitionGraceEntered = false;
        lateQueueRecognitionLogged = false;
        dutyReadyAccepted = false;
        lastError = string.Empty;
        statusDetail = string.Empty;
        currentRailDestination = null;
        railSampleExclusionDestination = null;
        nextRailSampleAt = DateTime.MinValue;
        voyageState.Reset();
        lastCastGate = string.Empty;
        zoneTransitionStartedAt = DateTime.MinValue;
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
            var reason =
                "XADB 0.0.0.39+ contract v6 roster IPC is required via XA.Database.GetAccountCharacterListJson; " +
                $"status={roster.Status}, detail={roster.Detail}";
            log.Warning(
                $"[Fishing][Selection] Selection failed closed because the XADB full roster is unavailable: {reason}");
            return [FishingSelectionResult.None(reason)];
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
                    BeginVoyageFishing("voyage entry");
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
                SetState(FishingState.TravelingToLimsa);
                break;

            case FishingState.TravelingToLimsa:
                TickTravelToLimsa(elapsed);
                break;

            case FishingState.CheckingPreparation:
                TickCheckDockPreparation();
                break;

            case FishingState.NavigatingToPreparationDock:
                TickNavigateToPreparationDock(elapsed);
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
                fishingStockRequirements = FishingStockCatalogPolicy.BuildRequirements(
                    configuration.FishingStockCatalog,
                    configManager.GetActiveConfig().FishingStockItems,
                    itemId => (int)GameHelpers.GetInventoryItemCount(itemId));
                fishingStockRequirementIndex = 0;
                fishingStockPartialFailures.Clear();
                activeFishingStockRequirement = null;
                SetState(FishingState.RestockingLures);
                break;

            case FishingState.RestockingLures:
                TickFishingStockPurchases();
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
                SetState(FishingState.NavigatingToRegistrar);
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

            case FishingState.WaitingForQueueRecognitionGrace:
                TickWaitForQueueRecognitionGrace();
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
        if (fisherFallbackStarted)
        {
            fisherFallbackService.Update();
            statusDetail = fisherFallbackService.StatusText;
            if (fisherFallbackService.Succeeded)
                return true;
            if (fisherFallbackService.IsComplete)
                Fail(fisherFallbackService.Failure, FishingAttemptFailureKind.SharedTransient);
            return false;
        }

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

            if (entry.Kind is FisherGearsetEventKind.TerminalFailure or FisherGearsetEventKind.FallbackRequested)
                terminalMessage = entry.Message;
        }

        if (fisherGearsetOperation.Succeeded)
            return true;

        if (fisherGearsetOperation.IsComplete)
        {
            if (fisherGearsetOperation.State == FisherGearsetEquipState.FallbackRequired)
            {
                var context = runLifecycle.Current;
                if (context == null)
                {
                    Fail("Fisher fallback lost its owned registration deadline.");
                    return false;
                }
                fisherFallbackStarted = true;
                fisherFallbackService.Start(context.RegistrationDeadlineUtc);
                statusDetail = terminalMessage;
                return false;
            }

            Fail(string.IsNullOrWhiteSpace(terminalMessage)
                ? $"Fisher gearset activation failed: {fisherGearsetOperation.State}."
                : terminalMessage,
                FishingAttemptFailureKind.SharedTransient);
        }

        return false;
    }

    private void TickTravelToLimsa(TimeSpan elapsed)
    {
        if (IsOceanFishingDutyActive())
        {
            ObserveQueueRegistration("duty already active");
            BeginVoyageFishing("voyage entry");
            return;
        }

        if (IsInLimsaAndReady())
        {
            log.Information(
                $"[Fishing][DockPrep] Limsa settlement confirmed in territory {LimsaTerritoryType}; evaluating repair and lure requirements");
            SetState(FishingState.CheckingPreparation);
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

    private void TickCheckDockPreparation()
    {
        if (!IsInLimsaAndReady())
        {
            SetState(FishingState.TravelingToLimsa);
            return;
        }

        var settings = GetActiveOperationSettings();
        var durabilityKnown = GameHelpers.TryGetLowestEquippedGearConditionPercent(out var lowestDurability);
        var repairDecision = FishingOperationPolicy.EvaluateRepair(
            settings,
            durabilityKnown,
            lowestDurability);
        var requirements = FishingStockCatalogPolicy.BuildRequirements(
            configuration.FishingStockCatalog,
            configManager.GetActiveConfig().FishingStockItems,
            itemId => (int)GameHelpers.GetInventoryItemCount(itemId));
        var missingItems = requirements.Count(item => item.MissingQuantity > 0);

        log.Information(
            $"[Fishing][DockPrep] Requirements evaluated after Limsa arrival: " +
            $"repair={repairDecision.ShouldRepair} ({repairDecision.Reason}), " +
            $"enabledStock={requirements.Count}, missingStock={missingItems}");
        SetState(FishingState.CheckingRepair);
    }

    private void TickFishingStockPurchases()
    {
        if (activeFishingStockRequirement == null)
        {
            while (fishingStockRequirementIndex < fishingStockRequirements.Count)
            {
                var configured = fishingStockRequirements[fishingStockRequirementIndex];
                var current = (int)GameHelpers.GetInventoryItemCount(configured.ItemId);
                var missing = Math.Max(0, configured.Target - current);
                if (missing == 0)
                {
                    fishingStockRequirementIndex++;
                    continue;
                }

                activeFishingStockRequirement = configured with
                {
                    InventoryCount = current,
                    MissingQuantity = missing,
                };
                if (!adsIpcClient.StartShopPurchase(configured.ItemId, missing, out var startFailure))
                {
                    CompleteFishingStockRequirement(
                        adsSucceeded: false,
                        acquiredQuantity: 0,
                        startFailure);
                    return;
                }

                fishingStockPurchaseOwned = true;
                fishingStockPurchaseStartedAt = DateTime.UtcNow;
                statusDetail = $"ADS purchasing {missing} of item {configured.ItemId}";
                log.Information(
                    $"[Fishing][Stock] ADS accepted exact missing quantity item={configured.ItemId}, quantity={missing}, target={configured.Target}");
                return;
            }

            FinishFishingStockPreparation();
            return;
        }

        if (DateTime.UtcNow - fishingStockPurchaseStartedAt > ShopPurchaseTimeout)
        {
            if (fishingStockPurchaseOwned)
                adsIpcClient.CancelUtility(out _);
            fishingStockPurchaseOwned = false;
            CompleteFishingStockRequirement(false, 0, "ADS shop purchase timed out.");
            return;
        }

        var status = adsIpcClient.RefreshShopPurchase();
        if (!status.StatusReadable ||
            status.ItemId != activeFishingStockRequirement.Value.ItemId ||
            !status.IsTerminal)
            return;

        fishingStockPurchaseOwned = false;
        var failure = status.Succeeded == true
            ? string.Empty
            : !string.IsNullOrWhiteSpace(status.FailureMessage)
                ? status.FailureMessage
                : status.StatusMessage;
        CompleteFishingStockRequirement(
            status.Succeeded == true,
            Math.Max(0, status.AcquiredQuantity),
            failure);
    }

    private void CompleteFishingStockRequirement(
        bool adsSucceeded,
        int acquiredQuantity,
        string failure)
    {
        if (activeFishingStockRequirement is not { } requirement)
            return;

        var after = (int)GameHelpers.GetInventoryItemCount(requirement.ItemId);
        var outcome = new FishingStockPurchaseOutcome(
            requirement.ItemId,
            requirement.MissingQuantity,
            acquiredQuantity,
            after,
            requirement.Target,
            adsSucceeded,
            failure);
        if (outcome.IsPartialFailure)
        {
            fishingStockPartialFailures.Add(outcome);
            log.Warning(
                $"[Fishing][Stock] Partial failure item={outcome.ItemId}, requested={outcome.RequestedQuantity}, " +
                $"acquired={outcome.AcquiredQuantity}, inventory={outcome.InventoryAfter}/{outcome.Target}, failure={outcome.Failure}");
        }
        else
        {
            log.Information(
                $"[Fishing][Stock] Verified item={outcome.ItemId} at {outcome.InventoryAfter}/{outcome.Target} after ADS terminal result");
        }

        if (!outcome.CanContinueFishing)
        {
            Fail(
                "Versatile Lure restocking ended with zero usable lures.",
                FishingAttemptFailureKind.CharacterPermanent);
            return;
        }

        activeFishingStockRequirement = null;
        fishingStockRequirementIndex++;
        fishingStockPurchaseStartedAt = DateTime.MinValue;
    }

    private void FinishFishingStockPreparation()
    {
        var versatileLures = (int)GameHelpers.GetInventoryItemCount(VersatileLureItemId);
        if (versatileLures <= 0)
        {
            Fail(
                "No usable Versatile Lure remains after ordered fishing-stock preparation.",
                FishingAttemptFailureKind.CharacterPermanent);
            return;
        }

        if (fishingStockPartialFailures.Count > 0)
        {
            var report = string.Join(
                "; ",
                fishingStockPartialFailures.Select(outcome =>
                    $"{outcome.ItemId} {outcome.InventoryAfter}/{outcome.Target}"));
            log.Warning($"[Fishing][Stock] Optional stock partial-failure report: {report}. Fishing will continue.");
        }

        SetState(FishingState.SettingBait);
    }

    private void ResetFishingStockPurchase(bool cancelOwned)
    {
        if (cancelOwned && fishingStockPurchaseOwned)
        {
            var status = adsIpcClient.RefreshShopPurchase(force: true);
            if (status.Running)
                adsIpcClient.CancelUtility(out _);
        }

        fishingStockPurchaseOwned = false;
        fishingStockRequirements = Array.Empty<FishingStockRequirement>();
        fishingStockPartialFailures.Clear();
        fishingStockRequirementIndex = 0;
        activeFishingStockRequirement = null;
        fishingStockPurchaseStartedAt = DateTime.MinValue;
    }

    private void TickNavigateToPreparationDock(TimeSpan elapsed)
    {
        if (!IsInLimsaAndReady())
        {
            SetState(FishingState.TravelingToLimsa);
            return;
        }

        var dataIdVendor = GameHelpers.FindObjectByDataId(MerchantAndMenderDataId);
        var nameFallbackVendor = GameHelpers.FindObjectByName("Merchant & Mender");
        var vendor = dataIdVendor ?? nameFallbackVendor;
        var approachPosition = OceanFishingDockPreparationPolicy.ResolveMerchantApproachPosition(
            MerchantAndMenderPosition,
            dataIdVendor?.Position,
            nameFallbackVendor?.Position);
        var distance = DistanceTo(approachPosition);

        if (TryRouteViaArcanistsGuild(distance, "Merchant & Mender"))
            return;

        if (vendor != null && distance <= OceanFishingDockPreparationPolicy.InteractDistance)
        {
            vnavmesh.Stop();
            var source = dataIdVendor != null ? "data ID" : "name fallback";
            log.Information(
                $"[Fishing][DockPrep] Vendor acquisition: Merchant & Mender dataId={MerchantAndMenderDataId} resolved by {source} at {vendor.Position}");
            SetState(FishingState.CheckingRepair);
            return;
        }

        if (vendor == null && distance <= OceanFishingDockPreparationPolicy.InteractDistance)
        {
            vnavmesh.Stop();
            statusDetail = "Waiting for Merchant & Mender to load at the Limsa dock";
        }

        if (elapsed > DockNavigationTimeout)
        {
            Fail(
                $"Bounded dock navigation timed out waiting for Merchant & Mender dataId={MerchantAndMenderDataId}; distance={distance:F1}y.",
                FishingAttemptFailureKind.SharedTransient);
            return;
        }

        if (vendor == null && distance <= OceanFishingDockPreparationPolicy.InteractDistance)
            return;

        if (lastNavigationCommandAt == DateTime.MinValue ||
            DateTime.UtcNow - lastNavigationCommandAt >= TimeSpan.FromSeconds(2))
        {
            lastNavigationCommandAt = DateTime.UtcNow;
            var source = vendor == null ? "fixed fallback" : dataIdVendor != null ? "data ID" : "name fallback";
            log.Information(
                $"[Fishing][DockPrep] Dock navigation to Merchant & Mender ({distance:F1}y, source={source}, dataId={MerchantAndMenderDataId})");
            vnavmesh.PathfindAndMoveTo(approachPosition);
        }
    }

    private bool TryRouteViaArcanistsGuild(double distance, string directDestinationName)
    {
        if (distance <= 100)
            return false;

        if (!IsArcanistsGuildAethernetUnlocked() && !aethernetAttunementAttempted)
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
                        return true;
                    }
                }

                aethernetAttunementAttempted = true;
                log.Warning(
                    $"[Fishing][DockRoute] Arcanists' Guild shard {ArcanistsGuildAethernetId} was locked but could not be found for one attunement attempt; navigating directly to {directDestinationName}");
            }
            else
            {
                var shardDistance = DistanceTo(shard.Position);
                if (shardDistance > 3.0)
                {
                    statusDetail = $"Moving to locked Arcanists' Guild shard ({shardDistance:F1}y)";
                    vnavmesh.PathfindAndMoveTo(shard.Position);
                    return true;
                }

                vnavmesh.Stop();
                aethernetAttunementAttempted = true;
                aethernetAttunementStartedAt = DateTime.UtcNow;
                Plugin.TargetManager.Target = shard;
                GameHelpers.InteractWithObject(shard);
                log.Information(
                    $"[Fishing][DockRoute] Attempted attunement of locked Arcanists' Guild shard {ArcanistsGuildAethernetId}");
                return true;
            }
        }

        if (aethernetAttunementStartedAt != DateTime.MinValue && !IsArcanistsGuildAethernetUnlocked())
        {
            if (DateTime.UtcNow - aethernetAttunementStartedAt < OceanFishingAttunementPolicy.VerificationWait)
            {
                statusDetail = "Verifying Arcanists' Guild shard attunement";
                return true;
            }

            aethernetAttunementStartedAt = DateTime.MinValue;
            log.Warning(
                $"[Fishing][DockRoute] Arcanists' Guild shard {ArcanistsGuildAethernetId} did not unlock after the attunement attempt; navigating directly to {directDestinationName}");
        }

        if (!aethernetAttempted && IsArcanistsGuildAethernetUnlocked())
        {
            aethernetAttempted = true;
            if (lifestream.AethernetTeleportById(ArcanistsGuildAethernetId))
            {
                log.Information(
                    $"[Fishing][DockRoute] Traveling toward {directDestinationName} via Arcanists' Guild aethernet id {ArcanistsGuildAethernetId}");
                statusDetail = "Traveling to Arcanists' Guild";
                return true;
            }

            log.Warning(
                $"[Fishing][DockRoute] Arcanists' Guild aethernet request failed; navigating directly to {directDestinationName}");
        }

        return false;
    }

    private void TickNavigateToRegistrar(TimeSpan elapsed)
    {
        if (IsOceanFishingDutyActive())
        {
            BeginVoyageFishing("voyage entry");
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

        if (TryRouteViaArcanistsGuild(distance, "Dryskthota"))
            return;

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
        if (TryObserveQueueRegistration())
        {
            return;
        }

        var context = runLifecycle.Current;
        if (context == null)
        {
            Fail("Ocean Fishing registration lost its owned run context.", FishingAttemptFailureKind.Stop);
            return;
        }

        var registrationDecision = OceanFishingRegistrationPolicy.Decide(
            queueConfirmed: false,
            embarkAccepted: embarkConfirmationAccepted,
            nowUtc: DateTimeOffset.UtcNow,
            registrationDeadlineUtc: context.RegistrationDeadlineUtc,
            genuineFailure: false);
        if (registrationDecision == OceanFishingRegistrationDecision.WaitForQueueRecognitionGrace)
        {
            EnterQueueRecognitionGrace(context);
            return;
        }

        if (registrationDecision == OceanFishingRegistrationDecision.RegistrationExpired)
        {
            Fail(
                embarkConfirmationAccepted
                    ? "Ocean Fishing queue recognition grace expired with no queue evidence."
                    : "Ocean Fishing registration closed before queue confirmation was observed.",
                FishingAttemptFailureKind.Stop);
            return;
        }

        if (TryHandleOceanFishingYesNo())
        {
            embarkConfirmationAccepted = true;
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

        var window = new OceanFishingStartupWindow(
            context.RegistrationStartUtc,
            context.RegistrationStartUtc,
            context.RegistrationDeadlineUtc);

        statusDetail = registrationEntrySelected
            ? $"Waiting for route/embark confirmation (deadline {window.EndUtc:u})"
            : "Waiting for registration selection";
    }

    private void TickWaitForQueueRecognitionGrace()
    {
        if (TryObserveQueueRegistration(duringGrace: true))
            return;

        var context = runLifecycle.Current;
        if (context == null)
        {
            Fail("Ocean Fishing queue recognition grace lost its owned run context.", FishingAttemptFailureKind.Stop);
            return;
        }

        var registrationDecision = OceanFishingRegistrationPolicy.Decide(
            queueConfirmed: false,
            embarkAccepted: embarkConfirmationAccepted,
            nowUtc: DateTimeOffset.UtcNow,
            registrationDeadlineUtc: context.RegistrationDeadlineUtc,
            genuineFailure: false);
        if (registrationDecision == OceanFishingRegistrationDecision.RegistrationExpired)
        {
            Fail(
                "Ocean Fishing queue recognition grace expired with no queue evidence.",
                FishingAttemptFailureKind.Stop);
            return;
        }

        var graceDeadlineUtc =
            context.RegistrationDeadlineUtc + OceanFishingRegistrationPolicy.QueueRecognitionGracePeriod;
        statusDetail =
            $"Embark accepted; retaining lifecycle locks while queue recognition settles (until {graceDeadlineUtc:u})";
    }

    private void EnterQueueRecognitionGrace(FishingRunContext context)
    {
        if (!queueRecognitionGraceEntered)
        {
            queueRecognitionGraceEntered = true;
            var graceDeadlineUtc =
                context.RegistrationDeadlineUtc + OceanFishingRegistrationPolicy.QueueRecognitionGracePeriod;
            log.Information(
                $"[Fishing] Embark was accepted but queue evidence was not visible at registration close; " +
                $"retaining lifecycle locks through {graceDeadlineUtc:u}");
        }

        vnavmesh.Stop();
        SetState(FishingState.WaitingForQueueRecognitionGrace);
    }

    private void TickWaitForDeparture(TimeSpan elapsed)
    {
        if (departureWaitStartedAt == DateTime.MinValue)
            departureWaitStartedAt = DateTime.UtcNow;

        if (IsOceanFishingDutyActive())
        {
            BeginVoyageFishing("voyage entry");
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
            BeginVoyageFishing("voyage entry");
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
        var now = DateTime.UtcNow;
        if (TickVoyageRouteTransition(now))
            return;

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

        if (voyageState.SessionNumber == 0 && !BeginFishingSession("initial voyage session"))
            return;

        if (voyageState.MovementLocked)
        {
            StopFishingNavigationAndFaceOutward("movement lock already active");
            SetState(FishingState.Fishing);
            return;
        }

        if (CurrentRailDestination is not { } destination)
        {
            TrySelectRailDestination(
                now,
                previousDestination: null,
                "waiting for an open continuous rail point");
            statusDetail = $"Waiting for an open rail point with " +
                           $"{OceanFishingContinuousRailPolicy.MinimumPlayerClearance:F1}-yalm player clearance";
            return;
        }

        var position = destination.Position;
        var distance = (float)DistanceTo(position);
        var atDestination = distance <= BoatFishingPositionTolerance;
        var nowUtc = new DateTimeOffset(now, TimeSpan.Zero);
        var timersPaused = AreFishingRecoveryTimersPaused();
        var placement = EvaluateInitialPlacementReadiness(
            now,
            destination,
            atDestination,
            timersPaused);
        if (HandlePlacementFailure(placement, nowUtc))
            return;

        if (TickFishingStartAttempt(
                now,
                resultWindowVisible: false,
                atDestination,
                placement.Ready,
                placement.Gate))
            return;

        if (voyageState.MovementLocked)
            return;

        var recovery = voyageState.EvaluateRecovery(
            nowUtc,
            distance,
            atDestination,
            CanFish(),
            timersPaused || (atDestination && !placement.Ready));
        if (TryAdvanceFishingDestination(recovery, nowUtc))
            return;

        if (atDestination)
        {
            if (!placement.Ready)
            {
                TryReapplyRailFacing(nowUtc);
                statusDetail = placement.Gate;
                return;
            }

            SetState(FishingState.Fishing);
            return;
        }

        if (timersPaused)
        {
            statusDetail = $"Recovery timers paused while moving to destination attempt {voyageState.DestinationAttemptNumber}";
            return;
        }

        if (lastNavigationCommandAt == DateTime.MinValue ||
            now - lastNavigationCommandAt >= TimeSpan.FromSeconds(2))
        {
            lastNavigationCommandAt = now;
            log.Information(
                $"[Fishing][Position] Moving to continuous rail destination attempt " +
                $"{voyageState.DestinationAttemptNumber} ({distance:F1}y); " +
                $"{FishingCastPolicy.CastCommand} then {FishingCastPolicy.DirectCastFallbackCommand}, plus premature " +
                "Fishing/Gathering acknowledgement, remain gated until " +
                $"distance, {OceanFishingContinuousRailPolicy.MinimumPlayerClearance:F1}y clearance, stopped-path settlement, " +
                "and outward character facing are verified");
            vnavmesh.PathfindAndMoveTo(position);
        }
    }

    private static bool IsInLimsaAndReady()
        => OceanFishingDockPreparationPolicy.IsLimsaSettlementReady(
            Plugin.ClientState.TerritoryType,
            Plugin.Condition[ConditionFlag.BetweenAreas] ||
            Plugin.Condition[ConditionFlag.BetweenAreas51],
            GameHelpers.IsPlayerAvailable());

    private static bool IsQueueRegistered()
        => OceanFishingQueueEvidencePolicy.Detect(
               Plugin.Condition[ConditionFlag.InDutyQueue],
               Plugin.Condition[ConditionFlag.WaitingForDuty],
               Plugin.Condition[ConditionFlag.WaitingForDutyFinder],
               oceanFishingDutyActive: false,
               contentsFinderConfirmVisible: false) != OceanFishingQueueEvidence.None;

    private bool TryObserveQueueRegistration(bool duringGrace = false)
    {
        var evidence = OceanFishingQueueEvidencePolicy.Detect(
            Plugin.Condition[ConditionFlag.InDutyQueue],
            Plugin.Condition[ConditionFlag.WaitingForDuty],
            Plugin.Condition[ConditionFlag.WaitingForDutyFinder],
            IsOceanFishingDutyActive(),
            GameHelpers.IsAddonVisible("ContentsFinderConfirm"));
        if (evidence == OceanFishingQueueEvidence.None)
            return false;

        var reason = evidence switch
        {
            OceanFishingQueueEvidence.InDutyQueue => "InDutyQueue condition",
            OceanFishingQueueEvidence.WaitingForDuty => "WaitingForDuty condition",
            OceanFishingQueueEvidence.WaitingForDutyFinder => "WaitingForDutyFinder condition",
            OceanFishingQueueEvidence.OceanFishingDutyEntry => "Ocean Fishing duty entry",
            OceanFishingQueueEvidence.ContentsFinderConfirm => "ContentsFinderConfirm ready prompt",
            _ => "Ocean Fishing queue evidence",
        };
        ObserveQueueRegistration(reason);

        if (duringGrace && !lateQueueRecognitionLogged)
        {
            lateQueueRecognitionLogged = true;
            log.Information($"[Fishing] Ocean Fishing queue recognized during post-registration grace: {reason}");
        }

        if (evidence == OceanFishingQueueEvidence.OceanFishingDutyEntry)
        {
            log.Information("[Fishing] Ocean Fishing duty entry observed");
            BeginVoyageFishing("voyage entry");
        }
        else
        {
            SetState(FishingState.WaitingForDeparture);
        }

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
            embarkAccepted: false,
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
            StopFishingNavigationAndFaceOutward("voyage result/completion");
            SetState(FishingState.HandlingResult);
            return;
        }

        if (TickVoyageRouteTransition(now))
            return;

        if (!inFishingContext)
        {
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
        var canFish = CanFish();
        var destination = CurrentRailDestination;
        var distance = destination.HasValue
            ? (float)DistanceTo(destination.Value.Position)
            : float.PositiveInfinity;
        var atDestination = destination.HasValue && distance <= BoatFishingPositionTolerance;
        var nowUtc = new DateTimeOffset(now, TimeSpan.Zero);
        var timersPaused = AreFishingRecoveryTimersPaused();
        var placement = voyageState.MovementLocked
            ? new OceanFishingPlacementEvaluation(
                Ready: true,
                ShouldResample: false,
                ShouldAbort: false,
                Gate: string.Empty)
            : destination.HasValue
                ? EvaluateInitialPlacementReadiness(
                    now,
                    destination.Value,
                    atDestination,
                    timersPaused)
                : new OceanFishingPlacementEvaluation(
                    Ready: false,
                    ShouldResample: false,
                    ShouldAbort: false,
                    Gate: "waiting for an open continuous rail point");

        if (!voyageState.MovementLocked && HandlePlacementFailure(placement, nowUtc))
            return;

        if (TickFishingStartAttempt(
                now,
                resultVisible,
                atDestination,
                placement.Ready,
                placement.Gate))
            return;

        if (voyageState.MovementLocked)
        {
            statusDetail = $"Fishing active; movement locked for voyage session {voyageState.SessionNumber}";
            return;
        }

        var recovery = voyageState.EvaluateRecovery(
            nowUtc,
            distance,
            atDestination,
            canFish,
            timersPaused || (atDestination && !placement.Ready));
        if (TryAdvanceFishingDestination(recovery, nowUtc))
            return;

        if (!atDestination || !destination.HasValue)
        {
            SetState(FishingState.MovingToFishingSpot);
            return;
        }

        if (!placement.Ready)
            TryReapplyRailFacing(nowUtc);
        statusDetail = timersPaused
            ? $"Recovery timers paused at destination attempt {voyageState.DestinationAttemptNumber}"
            : !placement.Ready
                ? placement.Gate
                : $"Retrying {FishingCastPolicy.CastCommand} then {FishingCastPolicy.DirectCastFallbackCommand} " +
                  $"in place for voyage session {voyageState.SessionNumber}; " +
                  $"post-arrival attempts={voyageState.PostArrivalStartAttemptCount}/{OceanFishingVoyageState.PostArrivalAttemptLimit}";
    }

    private static bool IsVoyageRouteTransitionActive()
        => (TryGetOceanFishingStatus(out var oceanStatus) &&
            oceanStatus == InstanceContentOceanFishing.OceanFishingStatus.NewZone) ||
           Plugin.Condition[ConditionFlag.BetweenAreas] ||
           Plugin.Condition[ConditionFlag.BetweenAreas51] ||
           Plugin.Condition[ConditionFlag.OccupiedInCutSceneEvent] ||
           Plugin.Condition[ConditionFlag.WatchingCutscene];

    private void BeginVoyageFishing(string reason)
    {
        var localPlayer = Plugin.ObjectTable.LocalPlayer;
        if (localPlayer == null)
        {
            Fail("Ocean Fishing voyage entry had no local player position.", FishingAttemptFailureKind.Stop);
            return;
        }

        voyageState.Reset();
        lastNavigationCommandAt = DateTime.MinValue;
        lastCastGate = string.Empty;
        zoneTransitionStartedAt = DateTime.MinValue;
        currentRailDestination = null;
        railSampleExclusionDestination = null;
        nextRailSampleAt = DateTime.MinValue;
        var nowUtc = new DateTimeOffset(DateTime.UtcNow, TimeSpan.Zero);
        voyageState.BeginPositioning(nowUtc);
        TrySelectRailDestination(
            DateTime.UtcNow,
            previousDestination: null,
            $"voyage entry for {reason}");

        if (IsVoyageRouteTransitionActive())
        {
            zoneTransitionStartedAt = DateTime.UtcNow;
            StopFishingNavigationAndFaceOutward("voyage entry transition");
        }
        else if (!BeginFishingSession(reason))
        {
            return;
        }

        SetState(FishingState.MovingToFishingSpot);
    }

    private bool TrySelectRailDestination(
        DateTime now,
        OceanFishingRailDestination? previousDestination,
        string reason)
    {
        if (now < nextRailSampleAt)
            return false;

        var localPlayer = Plugin.ObjectTable.LocalPlayer;
        if (localPlayer == null || localPlayer.Address == nint.Zero)
        {
            vnavmesh.Stop();
            currentRailDestination = null;
            nextRailSampleAt = now + RailSampleRetryInterval;
            statusDetail = "Waiting for local player before sampling an Ocean Fishing rail point";
            return false;
        }

        var otherPlayers = SnapshotOtherPlayerPositions(localPlayer.Address);
        var excludedDestination = previousDestination ?? railSampleExclusionDestination;
        if (!OceanFishingContinuousRailPolicy.TrySample(
                Random.Shared,
                otherPlayers,
                excludedDestination,
                out var destination))
        {
            vnavmesh.Stop();
            currentRailDestination = null;
            railSampleExclusionDestination = excludedDestination;
            nextRailSampleAt = now + RailSampleRetryInterval;
            statusDetail = $"No open continuous rail sample currently satisfies " +
                           $"{OceanFishingContinuousRailPolicy.MinimumPlayerClearance:F1}-yalm clearance";
            log.Debug(
                $"[Fishing][Position] Sampling pass exhausted " +
                $"{OceanFishingContinuousRailPolicy.MaxSampleAttempts} candidates for {reason}; " +
                $"otherPlayers={otherPlayers.Length}, retry={RailSampleRetryInterval.TotalSeconds:F0}s");
            return false;
        }

        currentRailDestination = destination;
        railSampleExclusionDestination = null;
        nextRailSampleAt = DateTime.MinValue;
        lastNavigationCommandAt = DateTime.MinValue;
        lastCastGate = string.Empty;
        log.Information(
            $"[Fishing][Position] Sampled continuous rail destination attempt " +
            $"{voyageState.DestinationAttemptNumber} for {reason}: " +
            $"({destination.Position.X:F3}, {destination.Position.Y:F3}, {destination.Position.Z:F3}), " +
            $"outwardRotation={destination.Rotation:F3}, otherPlayers={otherPlayers.Length}, " +
            $"clearance={OceanFishingContinuousRailPolicy.MinimumPlayerClearance:F1}y");
        return true;
    }

    private bool BeginFishingSession(string reason)
    {
        if (!runLifecycle.EnsureAutoHookEnabled(out var hookError))
        {
            Fail($"Could not enable AutoHook: {hookError}", FishingAttemptFailureKind.Stop);
            return false;
        }

        voyageState.BeginSession();
        lastCastGate = string.Empty;
        if (voyageState.TryApplySessionBait())
            CommandHelper.SendCommand("/bait Versatile Lure");

        log.Information(
            $"[Fishing][Session] Session {voyageState.SessionNumber} started for {reason}; " +
            $"Versatile Lure set once; the first voyage start is distance-gated, while post-acknowledgement " +
            $"{FishingCastPolicy.CastCommand} then {FishingCastPolicy.DirectCastFallbackCommand} retries remain in place");
        return true;
    }

    private bool TickVoyageRouteTransition(DateTime now)
    {
        if (IsVoyageRouteTransitionActive())
        {
            voyageState.PauseRecovery(new DateTimeOffset(now, TimeSpan.Zero));
            if (zoneTransitionStartedAt == DateTime.MinValue)
            {
                zoneTransitionStartedAt = now;
                StopFishingNavigationAndFaceOutward("route transition");
                log.Information(
                    "[Fishing][Session] Route transition detected; start retries are paused and the stored destination will not change");
            }

            if (now - zoneTransitionStartedAt >= ZoneTransitionTimeout)
            {
                Fail("Ocean Fishing zone transition remained stalled for 90 seconds.", FishingAttemptFailureKind.Stop);
                return true;
            }

            statusDetail = "Waiting for Ocean Fishing route transition without repositioning";
            return true;
        }

        if (zoneTransitionStartedAt == DateTime.MinValue)
            return false;

        zoneTransitionStartedAt = DateTime.MinValue;
        if (!IsOceanFishingDutyActive())
            return false;

        if (!BeginFishingSession("route transition completion"))
            return true;

        log.Information(
            $"[Fishing][Session] Route transition completed; session {voyageState.SessionNumber} will retry " +
            $"{FishingCastPolicy.CastCommand} then {FishingCastPolicy.DirectCastFallbackCommand} in place " +
            $"with movementLocked={voyageState.MovementLocked}");
        return false;
    }

    private bool TickFishingStartAttempt(
        DateTime now,
        bool resultWindowVisible,
        bool atDestination,
        bool initialPlacementReady,
        string initialPlacementGate)
    {
        var gathering = Plugin.Condition[GatheringCondition];
        var fishing = Plugin.Condition[FishingCondition];
        var evaluation = voyageState.EvaluateFishingStart(
            new DateTimeOffset(now, TimeSpan.Zero),
            enabled: true,
            inFishingContext: IsOceanFishingDutyActive(),
            zoneTransitionActive: IsVoyageRouteTransitionActive(),
            playerAvailable: GameHelpers.IsPlayerAvailable(),
            gatheringConditionActive: gathering,
            fishingConditionActive: fishing,
            resultWindowVisible,
            atDestination,
            initialPlacementReady,
            initialPlacementGate);

        if (evaluation.Decision == FishingCastDecision.Acknowledged)
        {
            if (!evaluation.StopNavigation)
                return false;

            StopFishingNavigationAndFaceOutward("Fishing/Gathering acknowledgement");
            var acknowledgement = fishing ? "Fishing" : "Gathering";
            log.Information(
                $"[Fishing][Cast] {acknowledgement} acknowledged after " +
                $"{voyageState.SessionStartAttemptCount} paired start attempt(s); " +
                "navigation stopped immediately and movement is locked for the remainder of the voyage");
            if (state == FishingState.MovingToFishingSpot)
                SetState(FishingState.Fishing);
            return true;
        }

        if (evaluation.Decision == FishingCastDecision.Suppressed)
        {
            LogCastGate(evaluation.Gate);
            return false;
        }

        CommandHelper.SendCommand(FishingCastPolicy.CastCommand);
        CommandHelper.SendCommand(FishingCastPolicy.DirectCastFallbackCommand);
        lastCastGate = string.Empty;
        log.Information(
            $"[Fishing][Cast] Session {voyageState.SessionNumber} attempt " +
            $"{voyageState.SessionStartAttemptCount}: sent {FishingCastPolicy.CastCommand} then " +
            $"{FishingCastPolicy.DirectCastFallbackCommand} " +
            $"at continuous rail destination attempt {voyageState.DestinationAttemptNumber}; " +
            "awaiting Fishing/Gathering acknowledgement");
        return false;
    }

    private bool TryAdvanceFishingDestination(
        OceanFishingAdvanceReason reason,
        DateTimeOffset nowUtc)
    {
        if (reason == OceanFishingAdvanceReason.None)
            return false;

        var previousDestination = CurrentRailDestination;
        vnavmesh.Stop();
        if (!voyageState.AdvanceDestination(nowUtc))
            return false;

        currentRailDestination = null;
        railSampleExclusionDestination = previousDestination;
        nextRailSampleAt = DateTime.MinValue;
        lastNavigationCommandAt = DateTime.MinValue;
        lastCastGate = string.Empty;
        var sampled = TrySelectRailDestination(
            nowUtc.UtcDateTime,
            previousDestination,
            DescribeAdvanceReason(reason));
        if (sampled && CurrentRailDestination is { } destination)
        {
            log.Warning(
                $"[Fishing][Position] Resampled after {DescribeAdvanceReason(reason)}; " +
                $"destination attempt {voyageState.DestinationAttemptNumber} is " +
                $"({destination.Position.X:F3}, {destination.Position.Y:F3}, {destination.Position.Z:F3}) " +
                $"with outward character rotation {destination.Rotation:F3}. " +
                "Start retry cadence and session bait state are preserved.");
        }
        else
        {
            log.Warning(
                $"[Fishing][Position] Resampling after {DescribeAdvanceReason(reason)} found no open point; " +
                $"{FishingCastPolicy.CastCommand} then {FishingCastPolicy.DirectCastFallbackCommand} remain blocked, " +
                "and sampling will retry in " +
                $"{RailSampleRetryInterval.TotalSeconds:F0}s");
        }

        SetState(FishingState.MovingToFishingSpot);
        return true;
    }

    private OceanFishingPlacementEvaluation EvaluateInitialPlacementReadiness(
        DateTime now,
        OceanFishingRailDestination destination,
        bool atDestination,
        bool timersPaused)
    {
        var nowUtc = new DateTimeOffset(now, TimeSpan.Zero);
        var player = Plugin.ObjectTable.LocalPlayer;
        var playerAvailable = GameHelpers.IsPlayerAvailable() &&
                              player != null &&
                              player.Address != nint.Zero;
        var playerClear = !atDestination ||
                          (playerAvailable &&
                           OceanFishingContinuousRailPolicy.HasPlayerClearance(
                               player!.Position,
                               SnapshotOtherPlayerPositions(player.Address)));

        if (atDestination &&
            playerClear &&
            !timersPaused &&
            !voyageState.DestinationArrived)
        {
            vnavmesh.Stop();
            lastNavigationCommandAt = now;
            GameHelpers.TrySetLocalPlayerRotation(destination.Rotation);
            voyageState.MarkArrived(nowUtc);
            log.Information(
                $"[Fishing][Position] Continuous rail destination attempt " +
                $"{voyageState.DestinationAttemptNumber} reached; distance<={BoatFishingPositionTolerance:F1}y, " +
                $"clearance>={OceanFishingContinuousRailPolicy.MinimumPlayerClearance:F1}y, " +
                $"rotation={destination.Rotation:F3}. Navigation stop issued; " +
                $"{FishingCastPolicy.CastCommand} then {FishingCastPolicy.DirectCastFallbackCommand} remain gated until " +
                "Path.IsRunning is false for " +
                $"{OceanFishingVoyageState.StoppedPathSettlementDelay.TotalSeconds:F0}s and character facing verifies.");
        }

        var pathStatusAvailable = false;
        var pathRunning = false;
        if (atDestination && voyageState.DestinationArrived)
        {
            pathStatusAvailable = vnavmesh.TryGetPathIsRunning(out pathRunning);
            if (pathStatusAvailable &&
                pathRunning &&
                (lastNavigationCommandAt == DateTime.MinValue ||
                 now - lastNavigationCommandAt >= NavigationStopRetryInterval))
            {
                lastNavigationCommandAt = now;
                vnavmesh.Stop();
            }
        }

        var facingVerified = playerAvailable &&
                             OceanFishingContinuousRailPolicy.IsFacingOutward(
                                 player!.Rotation,
                                 destination.Rotation);
        return voyageState.EvaluatePlacementReadiness(
            nowUtc,
            inFishingContext: IsOceanFishingDutyActive(),
            zoneTransitionActive: IsVoyageRouteTransitionActive(),
            playerAvailable,
            timersPaused,
            atDestination,
            playerClear,
            pathStatusAvailable,
            pathRunning,
            facingVerified);
    }

    private bool HandlePlacementFailure(
        OceanFishingPlacementEvaluation placement,
        DateTimeOffset nowUtc)
    {
        if (placement.ShouldAbort)
        {
            Fail(
                $"Ocean Fishing initial placement aborted: {placement.Gate}.",
                FishingAttemptFailureKind.Stop);
            return true;
        }

        if (!placement.ShouldResample)
            return false;

        vnavmesh.Stop();
        if (Plugin.Condition[GatheringCondition] ||
            Plugin.Condition[FishingCondition] ||
            AreFishingRecoveryTimersPaused())
        {
            voyageState.PauseRecovery(nowUtc);
            statusDetail = $"{placement.Gate}; waiting for a movement-safe state before resampling";
            return true;
        }

        var reason = placement.Gate.StartsWith("outward character facing", StringComparison.Ordinal)
            ? OceanFishingAdvanceReason.FacingUnverified
            : OceanFishingAdvanceReason.PlayerClearanceLost;
        TryAdvanceFishingDestination(reason, nowUtc);
        return true;
    }

    private static Vector3[] SnapshotOtherPlayerPositions(nint localPlayerAddress)
        => Plugin.ObjectTable
            .Where(gameObject =>
                gameObject.Address != localPlayerAddress &&
                gameObject.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Pc)
            .Select(gameObject => gameObject.Position)
            .ToArray();

    private void StopFishingNavigationAndFaceOutward(string reason)
    {
        vnavmesh.Stop();
        if (CurrentRailDestination is { } destination)
            GameHelpers.TrySetLocalPlayerRotation(destination.Rotation);

        log.Debug(
            $"[Fishing][Position] Navigation stop for {reason}; " +
            $"stored outward rotation={(CurrentRailDestination?.Rotation.ToString("F3", CultureInfo.InvariantCulture) ?? "unavailable")}");
    }

    private void TryReapplyRailFacing(DateTimeOffset nowUtc)
    {
        if (CurrentRailDestination is not { } destination || !voyageState.ShouldReapplyFacing(nowUtc))
            return;

        GameHelpers.TrySetLocalPlayerRotation(destination.Rotation);
        log.Debug(
            $"[Fishing][Position] Reapplied outward character rotation {destination.Rotation:F3} after arrival settlement; " +
            $"next reapply is limited to {OceanFishingVoyageState.FacingRetryInterval.TotalSeconds:F0}s");
    }

    private static bool AreFishingRecoveryTimersPaused()
    {
        var player = Plugin.ObjectTable.LocalPlayer;
        return !GameHelpers.IsPlayerAvailable() ||
               player?.IsCasting == true ||
               Plugin.Condition[ConditionFlag.Casting] ||
               Plugin.Condition[ConditionFlag.Occupied] ||
               Plugin.Condition[ConditionFlag.OccupiedInQuestEvent] ||
               Plugin.Condition[ConditionFlag.OccupiedInCutSceneEvent] ||
               Plugin.Condition[ConditionFlag.WatchingCutscene] ||
               Plugin.Condition[ConditionFlag.InCombat] ||
               Plugin.Condition[ConditionFlag.BetweenAreas] ||
               Plugin.Condition[ConditionFlag.BetweenAreas51] ||
               IsVoyageRouteTransitionActive();
    }

    private static string DescribeAdvanceReason(OceanFishingAdvanceReason reason)
        => reason switch
        {
            OceanFishingAdvanceReason.NavigationStalled => "less than 0.25y navigation progress for 10 active seconds",
            OceanFishingAdvanceReason.NavigationTimeout => "30 active navigation seconds on one destination",
            OceanFishingAdvanceReason.CannotFish => "CanFish false for 10 available/non-busy seconds after arrival",
            OceanFishingAdvanceReason.StartUnacknowledged => "five post-arrival paired /ahstart then /ac cast attempts without acknowledgement",
            OceanFishingAdvanceReason.PlayerClearanceLost => "another player entered the 3-yalm first-cast clearance",
            OceanFishingAdvanceReason.FacingUnverified => "outward character facing failed to verify for 10 active seconds",
            _ => "an unknown recovery condition",
        };

    private OceanFishingRailDestination? CurrentRailDestination
        => currentRailDestination;

    private void ResetResultHandlingState()
    {
        lastResultPollAt = DateTime.MinValue;
        lastResultCallbackAt = DateTime.MinValue;
        lastResultAddonSnapshot = OceanFishingResultAddonSnapshot.NotPolled;
        resultFallbackLogged = false;
        resultCallbackDispatched = false;
        resultWindowClosed = false;
        resultPostVoyageTransitionObserved = false;
        resultDetectionLogged = false;
        resultTransitionLogged = false;
        resultCallbackLogged = false;
        resultClosureLogged = false;
    }

    private void LogCastGate(string gate)
    {
        if (string.Equals(lastCastGate, gate, StringComparison.Ordinal))
            return;

        lastCastGate = gate;
        log.Debug(
            $"[Fishing][Cast] Session {voyageState.SessionNumber} attempt " +
            $"{voyageState.SessionStartAttemptCount + 1} suppressed: {gate}; " +
            $"destinationAttempt={voyageState.DestinationAttemptNumber}");
    }

    private static bool IsOceanFishingResultAddonAvailable()
        => GameHelpers.GetIKDResultAddonSnapshot().Visible;

    private void TickHandleResult(TimeSpan elapsed)
    {
        var now = DateTime.UtcNow;
        LogResultDetection();

        var areaTransitioning = Plugin.Condition[ConditionFlag.BetweenAreas] ||
                                Plugin.Condition[ConditionFlag.BetweenAreas51];
        var playerAvailable = GameHelpers.IsPlayerAvailable();
        var stillInOceanFishingTerritory = Plugin.ClientState.TerritoryType is 900 or 1163;
        var postVoyageTransitionStarted = areaTransitioning || !stillInOceanFishingTerritory;
        if (postVoyageTransitionStarted)
        {
            LogResultTransition(areaTransitioning, stillInOceanFishingTerritory, playerAvailable);
            resultPostVoyageTransitionObserved = true;
        }

        var postVoyageSettled = resultPostVoyageTransitionObserved &&
                                playerAvailable &&
                                !areaTransitioning &&
                                !stillInOceanFishingTerritory;
        var sinceLastPoll = lastResultPollAt == DateTime.MinValue
            ? TimeSpan.MaxValue
            : now - lastResultPollAt;
        if (elapsed >= OceanFishingResultClosePolicy.InitialDelay &&
            sinceLastPoll >= OceanFishingResultClosePolicy.PollInterval)
        {
            lastResultAddonSnapshot = GameHelpers.GetIKDResultAddonSnapshot();
            lastResultPollAt = now;
        }

        if (elapsed >= ResultSettlementTimeout && lastResultAddonSnapshot.Visible)
        {
            LogResultFallback(
                $"IKDResult remained visible after {ResultSettlementTimeout.TotalSeconds:F0}s; " +
                $"{lastResultAddonSnapshot.Detail}");
        }

        var decision = OceanFishingResultClosePolicy.Decide(new OceanFishingResultCloseSnapshot(
            elapsed,
            sinceLastPoll,
            lastResultCallbackAt == DateTime.MinValue
                ? TimeSpan.MaxValue
                : now - lastResultCallbackAt,
            lastResultAddonSnapshot.Found,
            lastResultAddonSnapshot.Visible,
            lastResultAddonSnapshot.Ready,
            resultCallbackDispatched,
            resultWindowClosed,
            resultPostVoyageTransitionObserved,
            postVoyageSettled));

        if (decision.ResultClosed && !resultWindowClosed)
        {
            resultWindowClosed = true;
            LogResultClosure(decision.Reason);
        }

        switch (decision.Action)
        {
            case OceanFishingResultCloseAction.WaitInitialDelay:
                statusDetail = "Waiting for Ocean Fishing result";
                return;

            case OceanFishingResultCloseAction.WaitForPollInterval:
                statusDetail = "Closing Ocean Fishing result";
                return;

            case OceanFishingResultCloseAction.WaitForReadyAddon:
                statusDetail = "Waiting for Ocean Fishing result addon readiness";
                return;

            case OceanFishingResultCloseAction.WaitCallbackSettlement:
                statusDetail = "Waiting for Ocean Fishing result close";
                return;

            case OceanFishingResultCloseAction.FireCallback:
                statusDetail = "Closing Ocean Fishing result";
                if (GameHelpers.TryCloseReadyIKDResult(out var firedSnapshot, out var closeError))
                {
                    lastResultAddonSnapshot = firedSnapshot;
                    resultCallbackDispatched = true;
                    lastResultCallbackAt = now;
                    LogResultCallbackDispatch();
                }
                else
                {
                    lastResultAddonSnapshot = firedSnapshot;
                }
                return;

            case OceanFishingResultCloseAction.WaitPostVoyageTransition:
                statusDetail = "Waiting for post-voyage transition";
                return;

            case OceanFishingResultCloseAction.WaitPlayerSettlement:
                statusDetail = "Waiting for post-voyage player settlement";
                return;

            case OceanFishingResultCloseAction.Complete:
                log.Information("[Fishing][IKDResult] Result handling settled; beginning post-voyage cleanup flow");
                BeginInventoryCleanup();
                return;
        }
    }

    private void LogResultFallback(string reason)
    {
        if (resultFallbackLogged)
            return;

        resultFallbackLogged = true;
        log.Warning($"[Fishing][IKDResult] Result handling timeout reached: {reason}; continuing close attempts and retaining ownership");
    }

    private void LogResultDetection()
    {
        if (resultDetectionLogged)
            return;

        resultDetectionLogged = true;
        log.Information(
            $"[Fishing][IKDResult] Result handling detected; dutyCompleted={dutyCompletionObserved}, " +
            $"territory={Plugin.ClientState.TerritoryType}");
    }

    private void LogResultCallbackDispatch()
    {
        if (resultCallbackLogged)
            return;

        resultCallbackLogged = true;
        log.Information($"[Fishing][IKDResult] Fired close callback: {OceanFishingResultAddonName} true 0; {lastResultAddonSnapshot.Detail}");
    }

    private void LogResultTransition(bool areaTransitioning, bool stillInOceanFishingTerritory, bool playerAvailable)
    {
        if (resultTransitionLogged)
            return;

        resultTransitionLogged = true;
        log.Information(
            $"[Fishing][IKDResult] Real post-voyage transition signal detected; " +
            $"BetweenAreas={Plugin.Condition[ConditionFlag.BetweenAreas]}, " +
            $"BetweenAreas51={Plugin.Condition[ConditionFlag.BetweenAreas51]}, " +
            $"areaTransitioning={areaTransitioning}, " +
            $"territory={Plugin.ClientState.TerritoryType}, " +
            $"stillInOceanFishingTerritory={stillInOceanFishingTerritory}, " +
            $"playerAvailable={playerAvailable}");
    }

    private void LogResultClosure(string reason)
    {
        if (resultClosureLogged)
            return;

        resultClosureLogged = true;
        log.Information($"[Fishing][IKDResult] Result window closed: {reason}; {lastResultAddonSnapshot.Detail}");
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
        if (TryReenterResultHandlingBeforeReturn("starting the configured return"))
            return;

        var command = ResolveReturnCommand();
        if (!string.IsNullOrWhiteSpace(command))
        {
            log.Information($"[Fishing] Fishing context ended; returning with {command}");
            if (!SendReturnCommand(command))
                return;
            returnCommandSent = true;
        }

        returnStartedAt = DateTime.UtcNow;
        returnStartedTerritory = Plugin.ClientState.TerritoryType;
        SetState(FishingState.Returning);
    }

    private void TickReturnSettlement(TimeSpan elapsed)
    {
        if (TryReenterResultHandlingBeforeReturn("waiting for the configured return"))
            return;

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

    private bool SendReturnCommand(string command)
    {
        if (TryReenterResultHandlingBeforeReturn($"sending return command {command}"))
            return false;

        returnCommandsSent++;
        if (command.StartsWith("/li ", StringComparison.OrdinalIgnoreCase))
            lifestream.ExecuteCommand(command);
        else
            CommandHelper.SendCommand(command);

        return true;
    }

    private bool TryReenterResultHandlingBeforeReturn(string phase)
    {
        var snapshot = GameHelpers.GetIKDResultAddonSnapshot();
        if (!FishingReturnPolicy.ShouldSuppressCommand(snapshot.Visible))
            return false;

        log.Warning(
            $"[Fishing][IKDResult] Suppressing {phase} because IKDResult reappeared; " +
            $"returning to result handling. {snapshot.Detail}");
        SetState(FishingState.HandlingResult);
        return true;
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
        fisherFallbackService.Reset();
        ResetFishingStockPurchase(cancelOwned: true);
        if (CurrentRailDestination.HasValue && IsOceanFishingDutyActive())
            StopFishingNavigationAndFaceOutward("terminal voyage failure");
        else
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

        if (newState is FishingState.NavigatingToPreparationDock or
            FishingState.NavigatingToRegistrar or
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
            ResetResultHandlingState();
    }
}
