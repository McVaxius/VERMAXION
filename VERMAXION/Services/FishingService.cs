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
    private const string TelepotTownAddonName = "TelepotTown";
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
    private static readonly TimeSpan TelepotTownRecoveryDelay = TimeSpan.FromSeconds(10);

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
    private readonly ScheduledOfflineHoldCoordinator scheduledOfflineHold;
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
    private bool aethernetTeleportOwned;
    private DateTime ownedTelepotTownVisibleAt = DateTime.MinValue;
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
    private bool scheduledOfflineHoldPending;
    private OceanFishingRailDestination? currentRailDestination;
    private OceanFishingRailDestination? railSampleExclusionDestination;
    private DateTime nextRailSampleAt = DateTime.MinValue;
    private readonly OceanFishingVoyageState voyageState = new();
    private string lastCastGate = string.Empty;
    private DateTime returnStartedAt = DateTime.MinValue;
    private uint returnStartedTerritory;
    private FishingRunMode activeRunMode;
    private FishingStartupTrigger activeStartupTrigger;
    private DateTimeOffset activeRegistrationStartUtc;
    private OceanFishingProvider activeProvider;
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
    private bool fishingStockFoodWalkDone;
    private DateTime fishingStockFoodWalkStartedAt = DateTime.MinValue;
    private DateTime fishingStockFoodWalkLastNavAt = DateTime.MinValue;
    private bool fishingStockNavReadyConfirmed;
    private DateTime fishingStockNavReadyWaitStartedAt = DateTime.MinValue;
    private DateTime fishingStockSettleStartedAt = DateTime.MinValue;
    private DateTime fishingStockSettleClearAt = DateTime.MinValue;
    private bool fishingStockSettleSawClear;
    private DateTime fishingStockSettleLastWarnAt = DateTime.MinValue;
    // Shop chaining. "Requested" is what ADS was last told; "HeldOpen" is whether a shop is actually
    // standing open right now (only a SUCCEEDED purchase leaves one — ADS tears the UI down on failure
    // even while holding). They are separate because the release path has to know both: stop holding, and
    // close what is still up.
    private bool fishingStockKeepOpenRequested;
    private bool fishingStockShopHeldOpen;
    private int fishingStockReuseRetryIndex = -1;
    private static readonly TimeSpan ShopSessionSettleTimeout = TimeSpan.FromSeconds(12);
    // Absolute cap the seen-clear latch cannot suppress. Without it, a character that goes busy and STAYS
    // busy after one clear tick (a stuck OccupiedInQuestEvent, an orphaned dialog) would hold RestockingLures
    // forever: nothing else bounds this state, and a wedged fishing run keeps AR MultiMode disabled, so the
    // client would lose every later window too. A bounded bad purchase is always better than a wedged client.
    private static readonly TimeSpan ShopSessionSettleHardCap = TimeSpan.FromSeconds(36);
    private static readonly TimeSpan ShopSessionSettleWarnInterval = TimeSpan.FromSeconds(5);
    // 90s: generous enough to absorb a still-building navmesh right after the fake-ready login — ADS's own
    // tighter candidate timeout is exactly what turns nav-not-ready into cross-zone vendor teleports. The hard
    // cap bounds the extra wait when a pathfind is still PENDING at 90s (handing ADS control with a pending
    // task both blocks its own movetos and later yanks the toon mid-purchase).
    private static readonly TimeSpan FoodVendorWalkTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan FoodVendorWalkHardCap = TimeSpan.FromSeconds(150);
    private static readonly TimeSpan NavmeshReadyTimeout = TimeSpan.FromSeconds(60);
    private const uint GerulfDataId = 1003253;                       // Gerulf, Limsa Lower Decks grocer (sells 4674)
    private static readonly Vector3 GerulfPosition = new(-149.95f, 18.17f, 36.94f);
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
        AbandoningStuckVoyage,
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
        ScheduledOfflineHoldCoordinator scheduledOfflineHold,
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
        this.scheduledOfflineHold = scheduledOfflineHold;
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
        activeStartupTrigger = runLifecycle.Current.StartupTrigger;
        activeRegistrationStartUtc = runLifecycle.Current.RegistrationStartUtc;
        activeProvider = runLifecycle.Current.Provider;
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
        aethernetTeleportOwned = false;
        ownedTelepotTownVisibleAt = DateTime.MinValue;
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
        scheduledOfflineHoldPending = false;
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
        if (OceanFishingProviderPolicy.VermaxionOwnsInDutyFishing(activeProvider) &&
            CurrentRailDestination.HasValue &&
            IsOceanFishingDutyActive())
            StopFishingNavigationAndFaceOutward("fishing service reset");
        else if (!IsOceanFishingDutyActive())
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
        aethernetTeleportOwned = false;
        ownedTelepotTownVisibleAt = DateTime.MinValue;
        lastError = string.Empty;
        statusDetail = string.Empty;
        scheduledOfflineHoldPending = false;
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
            activeStartupTrigger = FishingStartupTrigger.Clock;
            activeRegistrationStartUtc = default;
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
        TryEatFishingFood();
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
                log.Information("[Fishing] Ocean Fishing unlock validation skipped");
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
                // Re-entry (e.g. a TravelingToLimsa bounce mid-prep) relocates the toon, so the food walk
                // and mesh gate must re-run per pass — a stale walkDone would dispatch food from the
                // aetheryte plaza.
                fishingStockFoodWalkDone = false;
                fishingStockFoodWalkStartedAt = DateTime.MinValue;
                fishingStockFoodWalkLastNavAt = DateTime.MinValue;
                fishingStockNavReadyConfirmed = false;
                fishingStockNavReadyWaitStartedAt = DateTime.MinValue;
                // ARM (not clear) the settle gate for the first dispatch: an at-dock ADS repair is an NPC
                // event at the same Merchant & Mender with the same terminal-before-teardown gap, and
                // WaitingForRepair exits the tick ADS reports done. Cost when nothing needs settling is
                // one randomized sub-2s pause.
                fishingStockSettleStartedAt = DateTime.UtcNow;
                fishingStockSettleClearAt = DateTime.MinValue;
                fishingStockSettleSawClear = false;
                fishingStockSettleLastWarnAt = DateTime.UtcNow;
                // Start every restock with shop-holding OFF in ADS. ADS keeps that flag across a VERMAXION
                // reload, so a run that died mid-chain would otherwise leave holding on for the FOOD
                // purchase and drag Gerulf's open shop along the 250y walk to the dock.
                ReleaseHeldShop();
                // The shop purchase confirmations need YesAlready live; the run-wide pause is restored
                // the moment restocking ends.
                runLifecycle.SuspendYesAlreadyPauseForShopping("vendor bait restock");
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

            case FishingState.AbandoningStuckVoyage:
                TickAbandonStuckVoyage(elapsed);
                break;

            case FishingState.CleaningUpLifecycle:
                runLifecycle.Update();
                if (!runLifecycle.IsActive)
                {
                    if (scheduledOfflineHoldPending)
                    {
                        scheduledOfflineHoldPending = false;
                        if (!scheduledOfflineHold.IsEligibleAfterSuccessfulRun(activeRunMode, activeStartupTrigger))
                        {
                            ReturnAfterFishing();
                            break;
                        }

                        scheduledOfflineHold.BeginAfterSuccessfulRun(
                            activeRunMode,
                            activeStartupTrigger,
                            activeRegistrationStartUtc,
                            DateTimeOffset.UtcNow,
                            configuration.OceanFishingPreWindowOffsetMinutes);
                    }
                    SetState(FishingState.Complete);
                }
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

        // Reissue /li limsa ONLY while Lifestream is IDLE. Lifestream runs its travel as a task chain whose
        // TeleportToRootAetheryte alone times out at 30s; re-firing the command while it is mid-teleport
        // RESETS its task queue and the teleport never lands. An unconditional /li limsa
        // every 10s against a 30s teleport leaves Lifestream perpetually reset, so every attempt hits
        // "timed out traveling to Limsa". Gate on !IsBusy so Lifestream gets to finish; only re-fire if it
        // went idle without reaching Limsa. ESCAPE HATCH: a wedged Lifestream can hold
        // IsBusy true forever (infinite-timeout task chains) and the busy-gate alone would then never issue
        // anything — so the gate defers the reissue at most 75s (a healthy teleport chain finishes well
        // under that), restoring the old accidental unwedge at a cadence that cannot reset a live teleport.
        var sinceLastTravelCommand = lastTravelCommandAt == DateTime.MinValue
            ? TimeSpan.MaxValue
            : DateTime.UtcNow - lastTravelCommandAt;
        if ((!lifestream.IsBusy() && sinceLastTravelCommand >= TimeSpan.FromSeconds(10)) ||
            sinceLastTravelCommand >= TimeSpan.FromSeconds(75))
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
        // When repair is due, go THROUGH the dock walk first: the Merchant & Mender the stock machinery
        // already navigates to IS a mender, so repairing there needs no teleport at all. The old direct
        // transition ran CheckingRepair from wherever the character stood (usually the summoning bell),
        // which forced ADS onto its teleporting repair routes — those can loop
        // repair-start -> 5-minute wait -> fail through entire registration windows. Arrival at the
        // vendor transitions to CheckingRepair (TickNavigateToPreparationDock), same as before.
        SetState(repairDecision.ShouldRepair
            ? FishingState.NavigatingToPreparationDock
            : FishingState.CheckingRepair);
    }

    private void TickFishingStockPurchases()
    {
        if (activeFishingStockRequirement == null)
        {
            if (!TryEnsureShopSessionSettled())
                return;

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

                // EVERY dispatch waits for the navmesh: ADS navigates for every purchase, and a dispatch
                // onto a still-building mesh (right after the fake-ready login) is what turns nav
                // timeouts into cross-zone teleport fallbacks — for ANY vendor, not just food.
                // Bounded, fail-open.
                if (!TryEnsureNavmeshReady())
                    return;

                // FOOD: additionally walk the toon INTO GERULF'S RANGE before dispatching (the same
                // walk-first pattern as the dock preparation). ADS always resolves
                // 4674 to Gerulf; with the vendor already in interact range its walk is a no-op and the
                // buy lands in seconds.
                if (configured.ItemId == FishingStockItemIds.LentilsAndChestnuts && !TryWalkToFoodVendor())
                    return;

                // Hold the dock vendor's shop open across EVERY dock purchase, so the whole stop costs one
                // interact; without it the third consecutive interact with that NPC is swallowed by the
                // stale NPC event the previous close left behind (see IsDockVendorItem). Holding stays on
                // through the last one too — predicting which purchase is last would close the shop exactly
                // when the final buy needs it. The chain is closed by ReleaseHeldShop when restocking ends,
                // and switching to the food vendor turns it off here, before the walk to Gerulf.
                RequestShopKeepOpen(FishingStockItemIds.IsDockVendorItem(configured.ItemId));

                activeFishingStockRequirement = configured with
                {
                    InventoryCount = current,
                    MissingQuantity = missing,
                };
                if (!adsIpcClient.StartShopPurchase(configured.ItemId, missing, out var startFailure))
                {
                    // A start refused while a shop is held open is almost always THAT shop being in the way
                    // ("a different shop is already open" — a stock entry the dock vendor does not sell).
                    // Drop the held shop and let the next tick dispatch this same item cleanly; once only,
                    // so a genuinely broken start still reports rather than looping.
                    if (fishingStockShopHeldOpen && fishingStockReuseRetryIndex != fishingStockRequirementIndex)
                    {
                        fishingStockReuseRetryIndex = fishingStockRequirementIndex;
                        activeFishingStockRequirement = null;
                        log.Warning(
                            $"[Fishing][Stock] Start refused while reusing the held-open shop item={configured.ItemId} " +
                            $"({startFailure}) — closing it and retrying once without reuse");
                        ReleaseHeldShop();
                        return;
                    }

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

    /// <summary>Names the occupancy flags currently set, so a stuck session says WHICH state it is stuck in
    /// rather than just "occupied". The aggregate alone can't distinguish an unfinished NPC event from an
    /// unrelated flag, and that distinction decides whether waiting can ever help.</summary>
    private static string DescribeOccupiedFlags()
    {
        var flags = new List<string>(2);
        if (Plugin.Condition[ConditionFlag.Occupied])
            flags.Add("Occupied");
        if (Plugin.Condition[ConditionFlag.OccupiedInEvent])
            flags.Add("InEvent");
        if (Plugin.Condition[ConditionFlag.OccupiedInQuestEvent])
            flags.Add("InQuestEvent");
        if (Plugin.Condition[ConditionFlag.OccupiedInCutSceneEvent])
            flags.Add("InCutScene");
        if (Plugin.Condition[ConditionFlag.Occupied33])
            flags.Add("Occupied33");
        if (Plugin.Condition[ConditionFlag.Occupied38])
            flags.Add("Occupied38");
        if (Plugin.Condition[ConditionFlag.Occupied39])
            flags.Add("Occupied39");
        return string.Join("+", flags);
    }

    /// <summary>Tells ADS whether to leave the next successful purchase's shop open. Tracks what ADS
    /// confirmed, not what was asked: an ADS build without the endpoint answers false, and the run then
    /// silently keeps the old close-and-re-interact behavior (correct for up to two dock purchases) instead
    /// of believing in a chain that is not happening.</summary>
    private void RequestShopKeepOpen(bool enabled)
    {
        if (fishingStockKeepOpenRequested == enabled)
            return;
        if (!enabled)
        {
            adsIpcClient.SetShopKeepOpen(false);
            fishingStockKeepOpenRequested = false;
            return;
        }

        fishingStockKeepOpenRequested = adsIpcClient.SetShopKeepOpen(true);
        if (!fishingStockKeepOpenRequested)
            log.Warning("[Fishing][Stock] ADS did not accept shop keep-open — falling back to one interact per purchase");
    }

    /// <summary>Ends the chain: stops ADS holding shops and closes the one it left open. MUST run on every
    /// exit from restocking — a shop addon left standing blocks the walk to the boat, the registration
    /// dialogs, and AR alike, and while holding is on ADS will not close it for us.</summary>
    private void ReleaseHeldShop()
    {
        var wasHeld = fishingStockShopHeldOpen;
        fishingStockShopHeldOpen = false;
        // Unconditional rather than through RequestShopKeepOpen's change-tracking: this also runs as the
        // per-restock reset, where VERMAXION's tracked state can be false while ADS's own flag is still on.
        // Turning holding off is also what CLOSES a shop it left standing (ADS 0.6.1.5+) — CancelUtility
        // cannot, because ADS's cancel paths early-return unless a purchase is still running, and a held
        // shop only exists once the purchase is terminal.
        fishingStockKeepOpenRequested = false;
        adsIpcClient.SetShopKeepOpen(false);
        if (wasHeld)
            log.Information("[Fishing][Stock] Released the held-open vendor shop");
    }

    /// <summary>Holds the next stock dispatch until the previous shop session has fully wound down. ADS
    /// reports its terminal result while the Shop addon — and the NPC event behind it — is still closing;
    /// a re-interact fired into that gap is silently swallowed by the game and the purchase dies with
    /// "did not open a supported shop menu" (reproduced on a later consecutive dock purchase after earlier
    /// purchases succeeded). Requires the shop UI gone and the player unoccupied, then a short
    /// randomized settle for the event teardown the addon close doesn't cover. Always fails open by the
    /// hard cap, so this can never hold the state machine.</summary>
    private bool TryEnsureShopSessionSettled()
    {
        if (fishingStockSettleStartedAt == DateTime.MinValue)
            return true;

        var shopUiUp = GameHelpers.IsAddonVisible("Shop") ||
            GameHelpers.IsAddonVisible("ShopExchangeItem") ||
            GameHelpers.IsAddonVisible("ShopExchangeCurrency") ||
            GameHelpers.IsAddonVisible("Repair") ||
            GameHelpers.IsAddonVisible("SelectYesno");
        // The gate acts on the event flags only; the description also names the neighbouring Occupied3x
        // states purely so the log can tell us what a stuck session is actually stuck in.
        var occupied = Plugin.Condition[ConditionFlag.Occupied] ||
            Plugin.Condition[ConditionFlag.OccupiedInEvent] ||
            Plugin.Condition[ConditionFlag.OccupiedInQuestEvent] ||
            Plugin.Condition[ConditionFlag.OccupiedInCutSceneEvent];
        var occupiedFlags = DescribeOccupiedFlags();
        if (shopUiUp || occupied)
        {
            fishingStockSettleClearAt = DateTime.MinValue;
            var waited = DateTime.UtcNow - fishingStockSettleStartedAt;
            // Two deadlines. Until the hard cap, the soft one is skipped once the session has been seen
            // clear, so a busy flicker can't preempt an almost-finished settle into a near-zero clearance —
            // the very gap this gate closes. The hard cap always fires, so a character that never comes
            // back can't hold the state machine; past it, a short clearance is the accepted trade.
            if (waited > ShopSessionSettleHardCap ||
                (!fishingStockSettleSawClear && waited > ShopSessionSettleTimeout))
            {
                log.Warning(
                    $"[Fishing][Stock] Previous shop session did not settle within bounds after {waited.TotalSeconds:F0}s " +
                    $"(shopUi={shopUiUp}, occupied=[{occupiedFlags}]) — dispatching anyway");
                fishingStockSettleStartedAt = DateTime.MinValue;
                fishingStockSettleClearAt = DateTime.MinValue;
                fishingStockSettleSawClear = false;
                fishingStockSettleLastWarnAt = DateTime.MinValue;
                return true;
            }

            // A silent gate is untriageable: without this the wedge case logs nothing at all. The stamp is
            // armed with the wait, so a normal sub-second teardown stays quiet and a logged line always
            // means the wait is real.
            if (DateTime.UtcNow - fishingStockSettleLastWarnAt > ShopSessionSettleWarnInterval)
            {
                fishingStockSettleLastWarnAt = DateTime.UtcNow;
                log.Information(
                    $"[Fishing][Stock] Waiting for the previous shop session to settle " +
                    $"({waited.TotalSeconds:F0}s, shopUi={shopUiUp}, occupied=[{occupiedFlags}])");
            }
            statusDetail = $"Waiting for the previous shop session to settle ({waited.TotalSeconds:F0}s)";
            return false;
        }

        fishingStockSettleSawClear = true;
        if (fishingStockSettleClearAt == DateTime.MinValue)
        {
            // The 900ms floor is load-bearing: occupancy tails on flags this gate does not check
            // (e.g. Occupied33/39) are absorbed only by this settle. Do not trim it.
            fishingStockSettleClearAt = DateTime.UtcNow +
                TimeSpan.FromMilliseconds(Random.Shared.Next(900, 1700));
            return false;
        }

        if (DateTime.UtcNow < fishingStockSettleClearAt)
            return false;

        fishingStockSettleStartedAt = DateTime.MinValue;
        fishingStockSettleClearAt = DateTime.MinValue;
        return true;
    }

    /// <summary>Waits (bounded, once per restock) for the zone navmesh to be BUILT before the first ADS
    /// dispatch. Fail-open on timeout — a broken mesh should degrade to old behavior, not block the run.</summary>
    private bool TryEnsureNavmeshReady()
    {
        if (fishingStockNavReadyConfirmed)
            return true;
        if (vnavmesh.TryGetNavReady(out var ready) && ready)
        {
            fishingStockNavReadyConfirmed = true;
            return true;
        }
        if (fishingStockNavReadyWaitStartedAt == DateTime.MinValue)
        {
            fishingStockNavReadyWaitStartedAt = DateTime.UtcNow;
            log.Information("[Fishing][Stock] Waiting for the navmesh build before restock purchases");
        }
        if (DateTime.UtcNow - fishingStockNavReadyWaitStartedAt > NavmeshReadyTimeout)
        {
            fishingStockNavReadyConfirmed = true;
            log.Warning("[Fishing][Stock] Navmesh not ready after bounded wait — dispatching anyway");
            return true;
        }
        statusDetail = "Waiting for the navmesh build before restock purchases";
        return false;
    }

    /// <summary>Walks the toon into Gerulf's interact range before the FOOD purchase dispatch (same
    /// vnavmesh re-issue pattern as the Merchant &amp; Mender dock walk). Returns true when dispatching may
    /// proceed: vendor in range (vnav stopped), or the bounded walk gave up — in which case it NEVER
    /// fails open while a pathfind is still pending (a pending task blocks ADS's own movetos and starts
    /// moving the toon when the mesh finishes), waiting up to the hard cap first.</summary>
    private bool TryWalkToFoodVendor()
    {
        if (fishingStockFoodWalkDone)
            return true;

        var vendor = GameHelpers.FindObjectByDataId(GerulfDataId) ?? GameHelpers.FindObjectByName("Gerulf");
        var position = vendor?.Position ?? GerulfPosition;
        var distance = DistanceTo(position);
        if (vendor != null && distance <= OceanFishingDockPreparationPolicy.InteractDistance)
        {
            vnavmesh.Stop();
            fishingStockFoodWalkDone = true;
            log.Information($"[Fishing][Stock] Food vendor in range ({distance:F1}y) — dispatching the food purchase");
            return true;
        }

        if (fishingStockFoodWalkStartedAt == DateTime.MinValue)
            fishingStockFoodWalkStartedAt = DateTime.UtcNow;
        var elapsed = DateTime.UtcNow - fishingStockFoodWalkStartedAt;

        // At destination but the vendor object hasn't loaded: stand and wait (the dock walk's own
        // pattern) instead of re-issuing movetos to our own position for the rest of the budget.
        if (vendor == null && distance <= OceanFishingDockPreparationPolicy.InteractDistance)
        {
            vnavmesh.Stop();
            statusDetail = "Waiting for Gerulf to load at the food stop";
            if (elapsed <= FoodVendorWalkTimeout)
                return false;
        }

        if (elapsed > FoodVendorWalkTimeout)
        {
            var pending = vnavmesh.TryGetPathfindInProgress(out var p) && p;
            if (pending && elapsed <= FoodVendorWalkHardCap)
            {
                statusDetail = "Food-vendor walk over budget; waiting out the pending pathfind";
                return false;
            }
            vnavmesh.Stop();
            fishingStockFoodWalkDone = true;
            log.Warning($"[Fishing][Stock] Food-vendor walk gave up at {distance:F1}y (pendingPathfind={pending}) — falling back to ADS vendor resolution");
            return true;
        }

        if (fishingStockFoodWalkLastNavAt == DateTime.MinValue ||
            DateTime.UtcNow - fishingStockFoodWalkLastNavAt >= TimeSpan.FromSeconds(2))
        {
            fishingStockFoodWalkLastNavAt = DateTime.UtcNow;
            statusDetail = $"Walking to Gerulf for the food purchase ({distance:F1}y)";
            vnavmesh.PathfindAndMoveTo(position);
        }
        return false;
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

        // ADS leaves the shop standing only when it was asked to hold AND the purchase succeeded; a failed
        // run always tears the UI down, holding or not.
        fishingStockShopHeldOpen = fishingStockKeepOpenRequested && adsSucceeded;

        activeFishingStockRequirement = null;
        fishingStockRequirementIndex++;
        fishingStockPurchaseStartedAt = DateTime.MinValue;
        // A held-open shop has no session to settle: the NPC event was never ended and the next purchase
        // reuses the open addon instead of re-interacting. Arming the gate there would burn its full soft
        // cap on every chained item (shop UI visible, InEvent set, never clearing) for no benefit.
        fishingStockSettleStartedAt = fishingStockShopHeldOpen ? DateTime.MinValue : DateTime.UtcNow;
        fishingStockSettleClearAt = DateTime.MinValue;
        fishingStockSettleSawClear = false;
        fishingStockSettleLastWarnAt = DateTime.UtcNow;
    }

    private void FinishFishingStockPreparation()
    {
        // Dock purchases deliberately keep holding enabled through the final buy. End the chain here so
        // nothing after this state inherits a shop addon or NPC event left open by ADS.
        ReleaseHeldShop();
        if (!runLifecycle.ResumeYesAlreadyPauseAfterShopping("vendor bait restock"))
        {
            // Same policy as TryBegin: no YesAlready lease, no run. Registration and duty dialogs must
            // not be reached with YesAlready live.
            Fail(
                "Could not re-acquire the YesAlready pause lease after vendor restocking.",
                FishingAttemptFailureKind.SharedTransient);
            return;
        }

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

        ReleaseHeldShop();
        fishingStockReuseRetryIndex = -1;
        fishingStockPurchaseOwned = false;
        fishingStockRequirements = Array.Empty<FishingStockRequirement>();
        fishingStockPartialFailures.Clear();
        fishingStockRequirementIndex = 0;
        activeFishingStockRequirement = null;
        fishingStockPurchaseStartedAt = DateTime.MinValue;
        fishingStockFoodWalkDone = false;
        fishingStockFoodWalkStartedAt = DateTime.MinValue;
        fishingStockFoodWalkLastNavAt = DateTime.MinValue;
        fishingStockNavReadyConfirmed = false;
        fishingStockNavReadyWaitStartedAt = DateTime.MinValue;
        fishingStockSettleStartedAt = DateTime.MinValue;
        fishingStockSettleClearAt = DateTime.MinValue;
        fishingStockSettleSawClear = false;
        fishingStockSettleLastWarnAt = DateTime.MinValue;
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
        if (TryRecoverOwnedTelepotTown(distance, directDestinationName))
            return true;

        if (distance <= 100)
        {
            aethernetTeleportOwned = false;
            ownedTelepotTownVisibleAt = DateTime.MinValue;
            return false;
        }

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
                vnavmesh.Stop();
                aethernetTeleportOwned = true;
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

    private bool TryRecoverOwnedTelepotTown(double distance, string directDestinationName)
    {
        if (!aethernetTeleportOwned)
            return false;

        if (!GameHelpers.IsAddonVisible(TelepotTownAddonName))
        {
            if (ownedTelepotTownVisibleAt != DateTime.MinValue)
                ownedTelepotTownVisibleAt = DateTime.MinValue;

            if (lifestream.IsBusy() || distance > 100)
            {
                statusDetail = "Traveling to Arcanists' Guild";
                return true;
            }

            aethernetTeleportOwned = false;
            return false;
        }

        var now = DateTime.UtcNow;
        if (ownedTelepotTownVisibleAt == DateTime.MinValue)
        {
            ownedTelepotTownVisibleAt = now;
            log.Information("[Fishing][DockRoute] Waiting for the owned Arcanists' Guild aethernet window to close");
        }

        if (now - ownedTelepotTownVisibleAt < TelepotTownRecoveryDelay)
        {
            statusDetail = "Waiting for Arcanists' Guild aethernet travel";
            return true;
        }

        GameHelpers.TryCloseAddonByCallback(TelepotTownAddonName);
        CommandHelper.SendCommand("/lifestream cancel");
        vnavmesh.Stop();
        aethernetTeleportOwned = false;
        ownedTelepotTownVisibleAt = DateTime.MinValue;
        lastNavigationCommandAt = DateTime.MinValue;
        log.Warning(
            $"[Fishing][DockRoute] Recovered a stuck Arcanists' Guild aethernet window; continuing directly to {directDestinationName}");
        return true;
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
            routeSelectionHandled = TrySelectOceanFishingRoute();
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
            // No statusDetail here, deliberately: every TrySelectRailDestination failure path writes its
            // own, more specific message ("no rail point clears the fallback floor", "waiting for local
            // player", ...), and the sample throttle intentionally leaves the previous one standing.
            // A generic line at this site overwrote those on every tick and hid the actual reason.
            TrySelectRailDestination(
                now,
                previousDestination: null,
                "waiting for an open continuous rail point");
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

        // (B rule 1) Game-timing first: pause the not-catching timers until the fishing phase is open.
        var phaseGate = OceanFishingDiscreteSpotPolicy.Enabled &&
                        voyageState.DestinationArrived &&
                        IsOceanFishingPhaseNotYetOpen();
        var recovery = voyageState.EvaluateRecovery(
            nowUtc,
            distance,
            atDestination,
            CanFish(),
            timersPaused || phaseGate || (atDestination && !placement.Ready));
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
                $"distance, {destination.ArrivalClearance:F1}y clearance, stopped-path settlement, " +
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

    private bool TrySelectOceanFishingRoute()
    {
        var configuredPreference = configManager.GetActiveConfig().OceanFishingRouteOverride ??
                                   configuration.OceanFishingRoutePreference;
        var preference = OceanFishingRoutePolicy.Normalize(configuredPreference);
        var requestedIndex = OceanFishingRoutePolicy.GetDialogEntryIndex(preference);
        if (!GameHelpers.TrySelectStringEntry(requestedIndex, out var selectedIndex, out var entryCount))
            return false;

        log.Information(
            selectedIndex == requestedIndex
                ? $"[Fishing] Selected {preference} Ocean Fishing route at dialog entry {selectedIndex}"
                : $"[Fishing] Requested {preference} Ocean Fishing route entry {requestedIndex}, but only {entryCount} entries were available; selected safe fallback entry 0");
        return true;
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

    /// <summary>True while a spectral current is active (InstanceContentOceanFishing.SpectralCurrentActive,
    /// the field AutoHook also reads). Fail-open: a read failure returns false — spectral deferral is an
    /// optimization, never a blocker.</summary>
    private static unsafe bool IsSpectralCurrentActive()
    {
        try
        {
            var framework = EventFramework.Instance();
            if (framework == null)
                return false;
            var instance = framework->GetInstanceContentOceanFishing();
            return instance != null && instance->SpectralCurrentActive;
        }
        catch
        {
            return false;
        }
    }

    // (B rule 1) Confirmed game-timing gate: true only when the status read SUCCEEDS and reports a non-Fishing
    // phase. A transient read failure returns false (do NOT wait), matching TryReapplyRailFacing's "only act on
    // a confirmed non-Fishing status" philosophy.
    private bool IsOceanFishingPhaseNotYetOpen()
        => TryGetOceanFishingStatus(out var s) &&
           s != InstanceContentOceanFishing.OceanFishingStatus.Fishing;

    // --- Ocean-fishing food: eat in the pre-fishing lobby so Well-Fed covers the whole voyage. ---
    private const uint WellFedStatusId = 48;
    private const float FoodRefreshThresholdSeconds = 20f * 60f;   // lobby: refresh if <20min Well-Fed remains
    private static readonly TimeSpan FoodAttemptInterval = TimeSpan.FromSeconds(5);
    private DateTime lastFoodAttemptAt = DateTime.MinValue;
    private Vector3 lastFoodFramePosition;
    private bool lastFoodFramePositionValid;
    private bool foodWarnedMissingThisLobby;
    private uint pendingFoodConfirmId;   // food just eaten, awaiting a Well-Fed confirmation log
    private float pendingFoodPriorRemaining;   // Well-Fed seconds at the instant the eat fired: a voyage
                                               // re-eat starts with the OLD buff still ticking (<90s), so
                                               // "buff present" is NOT proof — only an INCREASE past this is.

    /// <summary>Eats the per-character configured food (FishingFoodItemId) in the ocean-fishing pre-fishing
    /// lobby (OceanFishingStatus.WaitingForPlayers) AND mid-voyage (Status.Fishing) whenever the Well-Fed
    /// buff is ABSENT (the 30-min buff covers the ~23-min voyage after a lobby eat, so no
    /// pre-emptive refresh — only a genuinely missing buff triggers a voyage eat). Mid-voyage it prefers
    /// FREE slots (rod stowed: duty pop, zone entries, waiting phases) and otherwise the between-casts
    /// CanFish gap, never during a spectral current, never while moving or casting. Default-off (item id 0).
    /// Runs from Update() every frame; the status gate makes it a cheap no-op outside these phases.</summary>
    private void TryEatFishingFood()
    {
        var gotStatus = TryGetOceanFishingStatus(out var status);
        if (!gotStatus ||
            (status != InstanceContentOceanFishing.OceanFishingStatus.WaitingForPlayers &&
             status != InstanceContentOceanFishing.OceanFishingStatus.Fishing))
        {
            lastFoodFramePositionValid = false;
            // Warn-once semantics are PER BOAT: the latch resets only when the status read fails (we left
            // ocean content), NOT on SwitchingZone/NewZone — else a foodless toon re-warns every zone
            // (3-4 warnings per voyage instead of 1).
            if (!gotStatus)
                foodWarnedMissingThisLobby = false;
            return;
        }
        var inVoyage = status == InstanceContentOceanFishing.OceanFishingStatus.Fishing;

        var player = Plugin.ObjectTable.LocalPlayer;
        if (player == null)
        {
            lastFoodFramePositionValid = false;
            return;
        }

        // Confirm a prior eat actually landed. UseAction's own return is false-on-success for items (that
        // false-negative reads as a failed eat even when Well-Fed lands), so we confirm by watching
        // for the Well-Fed status to appear instead of trusting the call's return value.
        var wellFed = GameHelpers.GetStatusTimeRemaining(WellFedStatusId);
        // Confirm ONLY on an INCREASE past the pre-eat remnant: a voyage re-eat fires while the old buff
        // still ticks (<90s), so "buff present" would false-confirm one frame after UseItem. A real eat
        // lands ~30min, always far above remnant+60s.
        if (pendingFoodConfirmId != 0 && wellFed > pendingFoodPriorRemaining + 60f)
        {
            log.Information($"[Fishing][Food] Well-Fed active ({wellFed / 60f:F0}m) after eating {GameHelpers.GetItemName(pendingFoodConfirmId)} — voyage covered.");
            pendingFoodConfirmId = 0;
        }

        // Per-frame stationary gate: only eat once the toon has settled at its spot. Using an item mid-move
        // would break the cast and could disturb placement, so we require two consecutive frames in place.
        var position = player.Position;
        var stationary = lastFoodFramePositionValid &&
                         Vector3.Distance(position, lastFoodFramePosition) <= 0.02f;
        lastFoodFramePosition = position;
        lastFoodFramePositionValid = true;
        if (!stationary)
            return;

        var config = configManager.GetActiveConfig();
        if (config.FishingFoodItemId == 0 && !config.FishingEatAnyFood)
            return;   // feature off: no specific food id and "eat any food" disabled.

        // Phase threshold (30-min buff vs ~23-min voyage = 3x7min zones +
        // cutscenes): the lobby/duty-pop eat tops up anything under 20min (free time, covers the voyage
        // in the normal case); mid-voyage we eat only when the buff is ABSENT — no pre-emptive refresh.
        if (inVoyage ? wellFed > 0f : wellFed >= FoodRefreshThresholdSeconds)
            return;

        if (!GameHelpers.IsPlayerAvailable())
            return;

        if (inVoyage)
        {
            if (player.IsCasting || Plugin.Condition[ConditionFlag.Casting] || IsSpectralCurrentActive())
                return;
            // Preferred slot: FREE time with the rod stowed (duty pop before the first cast, zone entries,
            // waiting phases) — ConditionFlag.Fishing false means no stance/line, so eating costs zero
            // fishing time and cannot race AutoHook. Fallback: in-stance between casts, where CanFish
            // (the game's own "rod may be cast now" bit — false while the line is out, a fish is hooked,
            // or a cast/reel animation runs) marks the only safe gap.
            var rodOut = Plugin.Condition[ConditionFlag.Fishing];
            if (rodOut && !CanFish())
                return;
        }

        var now = DateTime.UtcNow;
        if (now - lastFoodAttemptAt < FoodAttemptInterval)
            return;
        lastFoodAttemptAt = now;

        // Resolve candidate foods: the configured id if set, else the ranked "eat any food" list (GP food
        // first). Try them in order and eat the FIRST one that is usable now — the best food may
        // be 583-blocked for a given toon (per-item level/usability), so fall through to the next usable food
        // instead of giving up (which would leave the toon unfed).
        var candidates = config.FishingFoodItemId != 0
            ? new List<uint> { config.FishingFoodItemId }
            : GameHelpers.GetFishingFoodCandidates();

        if (candidates.Count == 0)
        {
            if (!foodWarnedMissingThisLobby)
            {
                foodWarnedMissingThisLobby = true;
                log.Warning("[Fishing][Food] No edible food is in inventory to eat on the boat.");
            }
            return;
        }

        uint blockedExample = 0;
        uint blockedStatus = 0;
        foreach (var foodId in candidates)
        {
            // NQ and HQ are SEPARATE use-ids (HQ = +1,000,000) with separate usability checks — checking
            // only the NQ id leaves HQ-only holders 583-blocked and unfed. Try every
            // variant actually held.
            foreach (var useId in GameHelpers.GetHeldFoodVariants(foodId))
            {
                var actionStatus = GameHelpers.GetItemActionStatus(useId);
                if (actionStatus != 0)
                {
                    // Not usable now (e.g. 583). Try the next variant/candidate; a transient block clears later.
                    if (blockedExample == 0) { blockedExample = useId; blockedStatus = actionStatus; }
                    continue;
                }
                GameHelpers.UseItem(useId);   // fire it; success is confirmed by Well-Fed INCREASING (see top), not the return
                pendingFoodConfirmId = useId;
                pendingFoodPriorRemaining = wellFed;
                log.Information($"[Fishing][Food] Eating {GameHelpers.GetItemName(useId)} ({useId}) {(inVoyage ? "between casts mid-voyage" : "in the pre-fishing lobby")} to refresh Well-Fed.");
                return;
            }
        }

        // Have food but none is usable right now -> log the blocking status once so the cause is visible.
        if (!foodWarnedMissingThisLobby)
        {
            foodWarnedMissingThisLobby = true;
            if (blockedExample != 0)
                log.Warning($"[Fishing][Food] Have food but none usable now (e.g. {GameHelpers.GetItemName(blockedExample)} ({blockedExample}) GetActionStatus={blockedStatus}); retrying this boat.");
            else
                log.Warning("[Fishing][Food] Available food is not in inventory; cannot eat on the boat.");
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

        // Standing at the dock's Merchant & Mender (which CheckingPreparation now routes through when
        // repair is due), the no-teleport mode is strictly better than whatever is configured: the mender
        // is right here, and every teleporting mode risks the multi-minute field/inn odyssey that has
        // missed boats. Configured mode still applies when repair triggers away from the dock.
        var adsMode = decision.AdsMode;
        if (DistanceTo(MerchantAndMenderPosition) <= OceanFishingDockPreparationPolicy.InteractDistance * 2f)
        {
            adsMode = FishingRepairPolicy.ToAdsMode(FishingRepairMode.NpcNoTeleportNoInn);
            log.Information("[Fishing][DockPrep] At the Merchant & Mender; using no-teleport repair mode.");
        }

        if (!adsIpcClient.StartRepair(adsMode, out var failure))
        {
            // Self-heal the stale-state case: ADS can answer "Cannot start NPC repair while NPC repair is
            // active" from a PREVIOUS repair whose utility state never cleared (client reload / interrupted
            // route). The stale flag never clears itself, so without this the character fails every
            // subsequent window's DockPrep and misses every boat until ADS.CancelUtility is called by hand.
            // Cancel-and-retry once; any second failure is reported as before.
            if (failure.Contains("repair is active", StringComparison.OrdinalIgnoreCase))
            {
                log.Warning("[Fishing][DockPrep] ADS reports a repair already active; cancelling the stale " +
                            "utility and retrying once.");
                adsIpcClient.CancelUtility(out _);
                // Retry with the SAME resolved mode as the first attempt — the at-mender no-teleport
                // override must survive the retry, or the stale-state self-heal teleports the character
                // away from the dock in exactly the state both protections exist for.
                if (adsIpcClient.StartRepair(adsMode, out failure))
                {
                    repairStartedAt = DateTime.UtcNow;
                    log.Information($"[Fishing] ADS repair requested after stale-state cancel: {adsMode}.");
                    SetState(FishingState.WaitingForRepair);
                    return true;
                }
            }

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
            if (OceanFishingProviderPolicy.VermaxionOwnsInDutyFishing(activeProvider))
                StopFishingNavigationAndFaceOutward("voyage result/completion");
            SetState(FishingState.HandlingResult);
            return;
        }

        if (!OceanFishingProviderPolicy.VermaxionOwnsInDutyFishing(activeProvider))
        {
            TickAutoHookOwnedFishingLoop(now, elapsed, inFishingContext);
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

        // While the fence-push is physically walking the body to the edge it can travel well past the 0.5y
        // arrival tolerance (especially at bow/stern, where "outward" is along z, so the walk is several
        // yalms). Drive it to completion HERE instead of letting the recovery / return-to-band logic below
        // see !atDestination and fight it back to the inset band.
        if (fencePushPhase == FencePushPhase.Pushing)
        {
            TryImproveRailPlacement(nowUtc, canFish);
            return;
        }

        // (B rule 1) Game-timing first: while the voyage's fishing phase is not open (status != Fishing),
        // CanFish is false for a reason unrelated to this spot, so WAIT -- do not let the CannotFish /
        // StartUnacknowledged timers accumulate and relocate a placed mode-2 toon.
        var phaseGate = OceanFishingDiscreteSpotPolicy.Enabled &&
                        voyageState.DestinationArrived &&
                        IsOceanFishingPhaseNotYetOpen();
        var recovery = voyageState.EvaluateRecovery(
            nowUtc,
            distance,
            atDestination,
            canFish,
            timersPaused || phaseGate || (atDestination && !placement.Ready));
        if (TryAdvanceFishingDestination(recovery, nowUtc))
            return;

        if (!atDestination || !destination.HasValue)
        {
            SetState(FishingState.MovingToFishingSpot);
            return;
        }

        // CanFish-false recovery chain (in order):
        //   1. PUSH FORWARD toward the water until coordinates stop changing (the rail fence stops the
        //      body — the physical guarantee of "close enough to cast", like a player walking to the rail);
        //   2. CAST from the fence (destination position is updated to the settled spot, so the normal
        //      cast machinery fires there);
        //   3. only if STILL not gathering, rotate (the 15-degree facing sweep);
        //   4. resample remains the last resort via the existing CanFish-false advance.
        // Fence-push to the edge on EVERY arrival until it has settled (Done), even when CanFish is already
        // true at the inset band -- so every toon ends standing at the fence, not 1-1.5y inboard.
        if (!placement.Ready || !canFish || fencePushPhase != FencePushPhase.Done)
            TryImproveRailPlacement(nowUtc, canFish);
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

    private void TickAutoHookOwnedFishingLoop(DateTime now, TimeSpan elapsed, bool inFishingContext)
    {
        if (IsVoyageRouteTransitionActive())
        {
            if (zoneTransitionStartedAt == DateTime.MinValue)
                zoneTransitionStartedAt = now;

            if (now - zoneTransitionStartedAt >= ZoneTransitionTimeout)
            {
                Fail("Ocean Fishing zone transition remained stalled for 90 seconds.", FishingAttemptFailureKind.Stop);
                return;
            }

            statusDetail = "AutoHook AutoOceanFish owns the in-duty route transition";
            return;
        }

        zoneTransitionStartedAt = DateTime.MinValue;
        if (!inFishingContext)
        {
            if (OceanFishingCompletionPolicy.ShouldInferFromDutyContextLoss(
                    sawFishingContext,
                    Plugin.ClientState.TerritoryType is 900 or 1163,
                    GameHelpers.IsPlayerAvailable(),
                    Plugin.Condition[ConditionFlag.BetweenAreas] ||
                    Plugin.Condition[ConditionFlag.BetweenAreas51]))
            {
                log.Information("[Fishing] AutoHook-owned voyage context disappeared; inferring voyage completion");
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
                Fail("Timed out waiting for Ocean Fishing duty completion.");
            return;
        }

        sawFishingContext = true;
        dutyContextLostAt = DateTime.MinValue;
        statusDetail = "AutoHook AutoOceanFish owns in-duty fishing; VERMAXION is monitoring completion";
    }

    private void BeginVoyageFishing(string reason)
    {
        if (!OceanFishingProviderPolicy.VermaxionOwnsInDutyFishing(activeProvider))
        {
            voyageState.Reset();
            currentRailDestination = null;
            railSampleExclusionDestination = null;
            zoneTransitionStartedAt = DateTime.MinValue;
            log.Information(
                $"[Fishing][Provider] AutoHook AutoOceanFish owns all in-duty fishing for {reason}; " +
                "VERMAXION retains result, cleanup, and return handling");
            SetState(FishingState.Fishing);
            return;
        }

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
        fencePushPhase = FencePushPhase.NotStarted;
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
        // Positioning: the discrete fixed-spot policy (default) resolves against the configured spot list;
        // the continuous sweep sampler backs it up. Both produce the same OceanFishingRailDestination
        // shape, so arrival/facing/cast logic downstream is unchanged.
        bool sampled;
        OceanFishingRailDestination destination;
        if (OceanFishingDiscreteSpotPolicy.Enabled)
        {
            sampled = OceanFishingDiscreteSpotPolicy.TrySample(
                localPlayer.Position,
                otherPlayers,
                excludedDestination,
                out destination);
            // The fixed list is finite: when no listed spot is usable (empty list, every spot dead or
            // contested by foreign players), retrying a set that cannot change this leg is a livelock —
            // the continuous sampler is the safety net for deck-geometry drift and crowded public boats.
            if (!sampled)
            {
                log.Debug("[Fishing][Position] No usable discrete spot — falling back to the continuous rail sampler");
                sampled = OceanFishingContinuousRailPolicy.TrySample(
                    Random.Shared,
                    otherPlayers,
                    excludedDestination,
                    out destination);
            }
        }
        else
        {
            sampled = OceanFishingContinuousRailPolicy.TrySample(
                Random.Shared,
                otherPlayers,
                excludedDestination,
                out destination);
        }
        if (!sampled)
        {
            vnavmesh.Stop();
            currentRailDestination = null;
            railSampleExclusionDestination = excludedDestination;
            nextRailSampleAt = now + RailSampleRetryInterval;
            statusDetail = $"No rail point anywhere clears even the " +
                           $"{OceanFishingContinuousRailPolicy.FallbackPlayerClearance:F1}-yalm fallback floor";
            log.Debug(
                $"[Fishing][Position] Full-rail sweep found no point clearing the " +
                $"{OceanFishingContinuousRailPolicy.FallbackPlayerClearance:F1}y fallback floor for {reason}; " +
                $"otherPlayers={otherPlayers.Length}, retry={RailSampleRetryInterval.TotalSeconds:F0}s");
            return false;
        }

        currentRailDestination = destination;
        railSampleExclusionDestination = null;
        nextRailSampleAt = DateTime.MinValue;
        lastNavigationCommandAt = DateTime.MinValue;
        lastCastGate = string.Empty;
        railFacingSweepSteps = 0;
        lastFacingSweepAt = DateTime.MinValue;
        railFacingSweepBaseRotation = float.NaN;
        railRebaitApplied = false;
        railRebaitAppliedAt = DateTime.MinValue;
        fencePushPhase = FencePushPhase.NotStarted;
        fencePushStableTicks = 0;
        // The caller no longer writes a generic status after this returns (it hid the failure-path
        // messages), so the success path owns its own.
        statusDetail = $"Moving to a sampled rail point ({destination.ArrivalClearance:F1}y clearance tier)";
        log.Information(
            $"[Fishing][Position] Sampled continuous rail destination attempt " +
            $"{voyageState.DestinationAttemptNumber} for {reason}: " +
            $"({destination.Position.X:F3}, {destination.Position.Y:F3}, {destination.Position.Z:F3}), " +
            $"outwardRotation={destination.Rotation:F3}, otherPlayers={otherPlayers.Length}, " +
            $"clearance={destination.ArrivalClearance:F1}y" +
            (destination.ArrivalClearance < OceanFishingContinuousRailPolicy.MinimumPlayerClearance
                ? " (fallback tier)"
                : string.Empty));
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

        // (B rule 5) Mode-2 placed fisher: reposition is the LAST resort. Return FALSE here (not handled) so the
        // tick continues to the in-place ladder -- the facing re-assert/sweep runs at the bottom of the loop
        // (TryImproveRailPlacement) EVERY tick, which it would NOT if we returned true. PlayerClearanceLost /
        // FacingUnverified / StartUnacknowledged never move a fixed spot; only CannotFish moves, and only after
        // the facing sweep completed a full circle AND a one-shot re-bait had its grace to work. Navigation
        // stall/timeout fire while still MOVING (DestinationArrived == false), so they bypass this gate.
        if (OceanFishingDiscreteSpotPolicy.Enabled &&
            voyageState.DestinationArrived &&
            reason is OceanFishingAdvanceReason.PlayerClearanceLost
                   or OceanFishingAdvanceReason.FacingUnverified
                   or OceanFishingAdvanceReason.StartUnacknowledged
                   or OceanFishingAdvanceReason.CannotFish)
        {
            if (!railRebaitApplied)
            {
                railRebaitApplied = true;
                railRebaitAppliedAt = nowUtc.UtcDateTime;
                log.Warning(
                    $"[Fishing][Position] Placed mode-2 spot not catching ({DescribeAdvanceReason(reason)}); " +
                    "in-place recovery (facing + bait) before any relocate. Reposition is the last resort.");
                CommandHelper.SendCommand("/bait Versatile Lure");
            }

            // Exhaustion differs by reason. CannotFish (CanFish==false): the facing sweep runs each tick, so
            // require a FULL circle AND the rebait grace before relocating. StartUnacknowledged (CanFish==TRUE,
            // casts won't acknowledge -- the facing sweep never runs, so railFacingSweepSteps stays 0 and a
            // facing-circle gate would never trip): require only the rebait grace, then relocate. This keeps the
            // MaxDestinationAttempts logout backstop for BOTH. FacingUnverified / PlayerClearanceLost never
            // relocate a fixed spot (facing is fixed in place; a passer-by is ignored).
            bool exhausted;
            if (reason == OceanFishingAdvanceReason.CannotFish)
                exhausted = railFacingSweepSteps >= RailFacingSweepMaxSteps &&
                            nowUtc.UtcDateTime - railRebaitAppliedAt >= RebaitGrace;
            else if (reason == OceanFishingAdvanceReason.StartUnacknowledged)
                exhausted = nowUtc.UtcDateTime - railRebaitAppliedAt >= RebaitGrace;
            else
                exhausted = false;
            if (!exhausted)
                return false; // not handled -> tick continues to the facing sweep; do NOT reposition
            // else: in-place ladder exhausted for this reason -> fall through to relocate (last resort).
        }

        var previousDestination = CurrentRailDestination;
        vnavmesh.Stop();
        if (!voyageState.AdvanceDestination(nowUtc))
        {
            // Give up rather than keep resampling forever (endless resampling is visible deck-running on a
            // public boat). Only the attempt-budget exhaustion routes to a quiet logout; the movement-locked
            // / already-fishing false returns are normal and leave the loop untouched.
            if (voyageState.DestinationAttemptsExhausted)
            {
                log.Warning(
                    $"[Fishing][Position] Exhausted {OceanFishingVoyageState.MaxDestinationAttempts} destination " +
                    $"attempts without fishing ({DescribeAdvanceReason(reason)}); abandoning this voyage to a logout.");
                BeginAbandonStuckVoyage();
                return true;
            }
            return false;
        }

        currentRailDestination = null;
        fencePushPhase = FencePushPhase.NotStarted;
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
        // Arrival is gated at the tier the sampler accepted THIS point under: full minimum for
        // preferred-tier points, the fallback floor for busy-vessel fallback points. One fixed value
        // here is wrong in both directions — the full minimum livelocks fallback destinations, and the
        // fallback floor would silently weaken the first-cast guard at fully-clear ones. Clamped at
        // zero, never escalated: every producer stamps a tier, and zeroing the fallback
        // knob makes the sampler accept at 0y — escalating that stamp to the full minimum here would
        // recreate the walk-fail-resample livelock the tier exists to prevent.
        var requiredClearance = MathF.Max(0f, destination.ArrivalClearance);
        // (A) Mode-2 fixed spots: clearance is a START-ONLY gate. Once this toon has MARKED ARRIVED at its
        // assigned spot, a foreign player wandering into the first-cast radius must NOT relocate it -- "don't
        // move once placed, just fish". Pinning playerClear post-arrival stops !playerClear from re-firing
        // ShouldResample/PlayerClearanceLost AND keeps placement Ready so it keeps casting. The pre-arrival
        // gate is unchanged (DestinationArrived is still false at selection/arrival), so we still refuse to
        // settle ONTO an occupied spot at the start.
        var placedNoRelocate = OceanFishingDiscreteSpotPolicy.Enabled && voyageState.DestinationArrived;
        var playerClear = placedNoRelocate ||
                          !atDestination ||
                          (playerAvailable &&
                           OceanFishingContinuousRailPolicy.HasPlayerClearance(
                               player!.Position,
                               SnapshotOtherPlayerPositions(player.Address),
                               requiredClearance));

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
                $"clearance>={requiredClearance:F1}y, " +
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
        // Return the advance RESULT, not an unconditional true: for a placed mode-2 fisher the B2 gate keeps it
        // in place (returns false) so this tick can fall through to the in-place facing sweep
        // (TryImproveRailPlacement). Returning true here short-circuited that -> a FacingUnverified stall never
        // got the sweep that fixes it and idled the whole voyage. Modes 0/1 and the reposition path still
        // return true (advance handled), preserving their behavior.
        return TryAdvanceFishingDestination(reason, nowUtc);
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

    private enum FencePushPhase { NotStarted, Pushing, Done }
    private FencePushPhase fencePushPhase = FencePushPhase.NotStarted;
    private Vector3 fencePushLastPosition;
    private Vector3 fencePushStartPosition;
    private int fencePushStableTicks;
    private DateTime fencePushIssuedAt = DateTime.MinValue;
    private DateTime fencePushDoneAt = DateTime.MinValue;
    private static readonly TimeSpan FencePushCastGrace = TimeSpan.FromSeconds(4); // >= one 3s cast cadence
    private static readonly TimeSpan FencePushTimeout = TimeSpan.FromSeconds(8);

    /// <summary>The CanFish-false recovery chain: fence-push, then cast, then rotate.</summary>
    private void TryImproveRailPlacement(DateTimeOffset nowUtc, bool canFish)
    {
        if (CurrentRailDestination is not { } destination)
            return;

        // The fence-push runs ONCE per placement regardless of CanFish, so every toon ends at the physical
        // fence (closest castable stand) rather than fishing from the inset band. Only after it has SETTLED
        // (Done) do we defer to normal facing/cast behavior. (The phase is reset to NotStarted whenever a
        // new placement begins -- voyage entry and every resample.)
        if (fencePushPhase == FencePushPhase.Done)
        {
            if (canFish)
            {
                TryReapplyRailFacing(nowUtc, canFish: true);
                return;
            }
            // Pushed to the fence but still can't fish: give the cast a grace window, then rotate-sweep.
            if (nowUtc.UtcDateTime - fencePushDoneAt < FencePushCastGrace)
                return;
            TryReapplyRailFacing(nowUtc, canFish: false);
            return;
        }

        var player = Plugin.ObjectTable.LocalPlayer;
        if (player == null)
            return;

        switch (fencePushPhase)
        {
            case FencePushPhase.NotStarted:
            {
                // Aim ~4y along the outward facing; vnav clamps to the walkable mesh and the rail fence
                // stops the body, so the settle position IS the closest castable stand.
                var dir = new Vector3(MathF.Sin(destination.Rotation), 0f, MathF.Cos(destination.Rotation));
                var target = destination.Position + dir * 4f;
                vnavmesh.PathfindAndMoveTo(target);
                fencePushPhase = FencePushPhase.Pushing;
                fencePushIssuedAt = nowUtc.UtcDateTime;
                fencePushLastPosition = player.Position;
                fencePushStartPosition = player.Position;
                fencePushStableTicks = 0;
                log.Information(
                    $"[Fishing][Position] Pushing to the fence from ({destination.Position.X:F2}, {destination.Position.Z:F2}) " +
                    $"(canFish={canFish}); target ({target.X:F2}, {target.Z:F2})");
                return;
            }
            case FencePushPhase.Pushing:
            {
                var moved = Vector3.Distance(player.Position, fencePushLastPosition);
                fencePushLastPosition = player.Position;
                if (moved < 0.05f)
                    fencePushStableTicks++;
                else
                    fencePushStableTicks = 0;
                var timedOut = nowUtc.UtcDateTime - fencePushIssuedAt > FencePushTimeout;
                // Don't treat "async pathfind hasn't started moving the body yet" on a loaded system as a
                // settle: only honor stable ticks once we've actually left the band.
                var movedFromStart = Vector3.Distance(player.Position, fencePushStartPosition) >= 0.2f;
                if ((fencePushStableTicks >= 2 && movedFromStart) || timedOut)
                {
                    vnavmesh.Stop();
                    // The settled spot becomes the destination, so arrival/facing gates pass and the normal
                    // cast machinery fires from the fence. Navmesh clamps ~1.5y inboard of the true deck edge
                    // and can't walk past the mesh, so optionally register the last stretch out with a direct
                    // SetPosition (default off; live-tunable per client; if the online instance corrects it we
                    // simply stay at the walkable edge, no worse than before).
                    var settledPos = player.Position;
                    var edgeOffset = OceanFishingContinuousRailPolicy.EdgeSetPositionOffsetYalms;
                    if (edgeOffset > 0f)
                    {
                        var outward = new Vector3(MathF.Sin(destination.Rotation), 0f, MathF.Cos(destination.Rotation));
                        settledPos = player.Position + outward * edgeOffset;
                        GameHelpers.TrySetLocalPlayerPosition(settledPos);
                    }
                    currentRailDestination = destination with { Position = settledPos };
                    GameHelpers.TrySetLocalPlayerRotation(destination.Rotation);
                    fencePushPhase = FencePushPhase.Done;
                    fencePushDoneAt = nowUtc.UtcDateTime;
                    // Re-stamp the recovery baseline: the Pushing guard skipped EvaluateRecovery for the whole
                    // walk, so without this the next tick would charge the entire push-transit duration to the
                    // CanFish-false budget and truncate/skip the facing-sweep salvage at the fence.
                    voyageState.PauseRecovery(nowUtc);
                    log.Information(
                        $"[Fishing][Position] Fence push settled at ({player.Position.X:F2}, {player.Position.Z:F2})" +
                        (timedOut ? " (timeout)" : string.Empty) + "; attempting casts before any rotation");
                }
                return;
            }
            case FencePushPhase.Done:
            {
                // Give the cast machinery at least one full cadence at the fence before rotating.
                if (nowUtc.UtcDateTime - fencePushDoneAt < FencePushCastGrace)
                    return;
                TryReapplyRailFacing(nowUtc, canFish: false);
                return;
            }
        }
    }

    private int railFacingSweepSteps;
    private DateTime lastFacingSweepAt = DateTime.MinValue;
    private float railFacingSweepBaseRotation = float.NaN;
    // (B rule 3-4) One in-place re-bait attempt per placement, with a grace window for it to take effect,
    // before position is ever considered for a mode-2 fixed spot.
    private bool railRebaitApplied;
    private DateTime railRebaitAppliedAt = DateTime.MinValue;
    private static readonly TimeSpan RebaitGrace = TimeSpan.FromSeconds(15);
    private const float RailFacingSweepStepDegrees = 15f;
    private const float RailFacingSweepStepRadians = RailFacingSweepStepDegrees * MathF.PI / 180f;
    private const int RailFacingSweepMaxSteps = 24; // one full 360-degree circle at 15-degree steps

    private void TryReapplyRailFacing(DateTimeOffset nowUtc, bool canFish = true)
    {
        if (CurrentRailDestination is not { } destination)
            return;

        // With a castable facing, keep re-asserting the destination rotation (rate-limited) and clear any
        // in-progress sweep. When a sweep found this facing, destination.Rotation already IS the swept
        // angle (the sweep writes it back), so the successful facing is kept, not the failed base.
        if (canFish)
        {
            railFacingSweepSteps = 0;
            railFacingSweepBaseRotation = float.NaN;
            if (!voyageState.ShouldReapplyFacing(nowUtc))
                return;
            GameHelpers.TrySetLocalPlayerRotation(destination.Rotation);
            log.Debug(
                $"[Fishing][Position] Reapplied outward character rotation {destination.Rotation:F3} after arrival settlement; " +
                $"next reapply is limited to {OceanFishingVoyageState.FacingRetryInterval.TotalSeconds:F0}s");
            return;
        }

        // Zone-start phase-lock guard: at voyage/zone entry the game reports CanFish=false because fishing is
        // not yet ENABLED (ocean status WaitingForPlayers/SwitchingZone/NewZone; the timer has not begun), NOT
        // because the outward facing is wrong. Sweeping here spins a correctly-placed fisher off the rail line
        // into the deck for a fault that does not exist. Only sweep while fishing is live (status == Fishing);
        // otherwise snap back to the outward normal and reset any in-progress sweep so it restarts clean once
        // fishing opens.
        // Only act on a CONFIRMED non-Fishing status: if the status read transiently fails, fall through and
        // let the normal sweep run rather than wiping an in-progress legitimate bad-angle sweep.
        if (TryGetOceanFishingStatus(out var oceanStatus) &&
            oceanStatus != InstanceContentOceanFishing.OceanFishingStatus.Fishing)
        {
            if (!float.IsNaN(railFacingSweepBaseRotation))
            {
                currentRailDestination = destination with { Rotation = railFacingSweepBaseRotation };
                GameHelpers.TrySetLocalPlayerRotation(railFacingSweepBaseRotation);
            }
            railFacingSweepSteps = 0;
            railFacingSweepBaseRotation = float.NaN;
            return;
        }

        // Fallback: the precomputed perpendicular did not yield CanFish at this spot (e.g. a bow/stern angle
        // that is slightly off). Rotate in 15-degree increments, sampling CanFish each step, until the game
        // accepts a facing or a full circle is swept. Each step WRITES the swept rotation back into the
        // current destination, so the placement facing-gate verifies against the swept angle and a cast can
        // actually fire from it (sweeping only the character rotation would leave every swept
        // facing uncastable because placement would still gate on the stale base rotation). The sweep progresses
        // from the ORIGINAL base captured at sweep start, so the base does not drift as steps are written.
        var stepIntervalMs = RailFacingSweepStepDegrees / Math.Max(30f, OceanFishingContinuousRailPolicy.FacingSweepDegreesPerSecond) * 1000f;
        var now = nowUtc.UtcDateTime;
        // Throttle steps; and a full circle (step max = base + 360deg = the outward normal, since SetRotation
        // normalises) with no castable facing ends here -- the last written rotation is already the outward
        // normal and steps stays at max, so the fisher rests facing outward and re-entry returns immediately.
        // The 10s CannotFish resample is the escape hatch for a genuinely dead spot.
        if (now - lastFacingSweepAt < TimeSpan.FromMilliseconds(stepIntervalMs) ||
            railFacingSweepSteps >= RailFacingSweepMaxSteps)
        {
            return;
        }
        if (float.IsNaN(railFacingSweepBaseRotation))
            railFacingSweepBaseRotation = destination.Rotation;
        lastFacingSweepAt = now;
        railFacingSweepSteps++;
        var rotation = railFacingSweepBaseRotation + railFacingSweepSteps * RailFacingSweepStepRadians;
        currentRailDestination = destination with { Rotation = rotation };
        GameHelpers.TrySetLocalPlayerRotation(rotation);
        log.Debug(
            $"[Fishing][Position] CanFish false; facing sweep step {railFacingSweepSteps}/{RailFacingSweepMaxSteps} " +
            $"to rotation {rotation:F3} (base {railFacingSweepBaseRotation:F3})");
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
            OceanFishingAdvanceReason.PlayerClearanceLost => "another player entered the destination's first-cast clearance",
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
            // Same idle-gate + 75s wedge escape as boarding travel (see TickTravelToLimsa).
            var sinceLastCleanupTravel = lastTravelCommandAt == DateTime.MinValue
                ? TimeSpan.MaxValue
                : DateTime.UtcNow - lastTravelCommandAt;
            if ((!lifestream.IsBusy() && sinceLastCleanupTravel >= TimeSpan.FromSeconds(10)) ||
                sinceLastCleanupTravel >= TimeSpan.FromSeconds(75))
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

        // Reuse the repair path's vendor acquisition (DataId 1005422 + resolved approach position + 3y
        // interact), NOT name-only + 12y. /ays itemsell only engages AR's sell task when a GilShop vendor is
        // within 7y (NpcSaleManager.GetValidNPC); a 12y arrival gate declares "arrived" ~5y short, so
        // AR never goes busy and the sell silently no-ops. The repair
        // path reaches this same NPC reliably at InteractDistance, so selling does too.
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
            SetState(FishingState.RunningInventoryCleanup);
            return;
        }

        if (vendor != null)
        {
            statusDetail = $"Moving near Merchant & Mender for /ays itemsell ({distance:F1}y)";
            vnavmesh.PathfindAndMoveTo(approachPosition);
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

        if (scheduledOfflineHold.IsEligibleAfterSuccessfulRun(activeRunMode, activeStartupTrigger))
        {
            log.Information("[Fishing][OfflineHold] Scheduled logout overrides the configured return; restoring lifecycle state before creating the hold");
            scheduledOfflineHoldPending = true;
            runLifecycle.Cleanup("Ocean Fishing completed before scheduled offline hold");
            SetState(FishingState.CleaningUpLifecycle);
            return;
        }

        var operationSettings = GetActiveOperationSettings();
        var command = FishingOperationPolicy.ResolveReturnCommand(operationSettings);
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
        if (FishingReturnPolicy.ShouldRetry(returnCommandsSent, returnElapsed, transitioning))
        {
            var command = ResolveReturnCommand();
            log.Warning($"[Fishing] Return command produced no observable activity after 30 seconds; retrying once: {command}");
            SendReturnCommand(command);
            return;
        }

        if (returnElapsed >= FishingReturnPolicy.FailAfter || elapsed > ReturnSettlementTimeout)
            Fail("Timed out waiting for configured Ocean Fishing return to settle.");
    }

    private DateTime abandonStartedAt = DateTime.MinValue;
    private DateTime lastAbandonLogoutAt = DateTime.MinValue;
    private static readonly TimeSpan AbandonLogoutTimeout = TimeSpan.FromSeconds(45);

    private void BeginAbandonStuckVoyage()
    {
        vnavmesh.Stop();
        // Stand still, facing the sea, before anything else — the failure mode of the whole abandon path is
        // a stationary rail-facing character, never a running one. That alone ends the "sussy" behaviour.
        StopFishingNavigationAndFaceOutward("abandoning stuck voyage");
        abandonStartedAt = DateTime.MinValue;
        lastAbandonLogoutAt = DateTime.MinValue;
        SetState(FishingState.AbandoningStuckVoyage);
    }

    /// <summary>Quietly logs the character out after positioning gave up (log out rather than
    /// keep visibly performing on a public boat). Sends /logout and
    /// confirms the SelectYesno; if it cannot log out within the timeout it simply stays standing (still
    /// non-sussy) and lets the run fail normally.</summary>
    private void TickAbandonStuckVoyage(TimeSpan elapsed)
    {
        var now = DateTime.UtcNow;
        if (abandonStartedAt == DateTime.MinValue)
            abandonStartedAt = now;

        // Logged out (title / character select) — the abandon succeeded; end the run so lifecycle restores
        // AutoHook/AR/YesAlready and the supervisor takes it from title.
        if (!GameHelpers.IsPlayerAvailable())
        {
            log.Information("[Fishing] Abandoned stuck voyage via logout; player no longer in world.");
            Fail("Abandoned an unfishable voyage via logout after exhausting positioning attempts.",
                FishingAttemptFailureKind.Stop);
            return;
        }

        // Confirm the logout dialog if it is up (yes = 0).
        if (GameHelpers.IsAddonVisible("SelectYesno"))
        {
            GameHelpers.TryFireReadyAddonCallback("SelectYesno", true, 0);
            statusDetail = "Confirming logout after abandoning an unfishable voyage";
        }
        else if (lastAbandonLogoutAt == DateTime.MinValue ||
                 now - lastAbandonLogoutAt >= TimeSpan.FromSeconds(5))
        {
            // (Re)issue /logout, keeping the character stationary the whole time.
            lastAbandonLogoutAt = now;
            GameHelpers.TrySetLocalPlayerRotation(CurrentRailDestination?.Rotation ?? 0f);
            CommandHelper.SendCommand("/logout");
            statusDetail = "Logging out after abandoning an unfishable voyage";
        }

        if (now - abandonStartedAt >= AbandonLogoutTimeout)
        {
            // Could not log out (e.g. logout blocked in an instance) — stop trying and fail quietly. The
            // character remains standing at the rail facing the sea, which is not a sussy state.
            log.Warning("[Fishing] Logout after abandoning a stuck voyage did not complete within the timeout; " +
                        "leaving the character stationary and failing the run.");
            Fail("Could not log out after abandoning an unfishable voyage; left the character stationary.",
                FishingAttemptFailureKind.Stop);
        }
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
        if (OceanFishingProviderPolicy.VermaxionOwnsInDutyFishing(activeProvider) &&
            CurrentRailDestination.HasValue &&
            IsOceanFishingDutyActive())
            StopFishingNavigationAndFaceOutward("terminal voyage failure");
        else if (!IsOceanFishingDutyActive())
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
