using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using VERMAXION.IPC;
using VERMAXION.Models;
using static VERMAXION.Services.GameHelpers;

namespace VERMAXION.Services;

public class VermaxionEngine
{
    public Func<string?> StartBlocker { get; set; } = static () => null;
    private static readonly TimeSpan HandoffQuietPeriod = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan HandoffBlockerLogThrottle = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan HandoffBlockerWarningThrottle = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan NagYourMomLostStatusGrace = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan NagYourMomLostStatusLogThrottle = TimeSpan.FromSeconds(30);
    private static readonly string[] NagYourMomRouteOrder =
    [
        MomRunRoutes.CasualCc,
        MomRunRoutes.Frontline,
        MomRunRoutes.RivalWings,
    ];

    private static readonly string[] StartupMiscCommands =
    [
        "/rotation Cancel",
        "/at enable",
        "/vbmai off",
        "/bmrai off",
        "/wrath auto off",
		//"/vnavmesh stop",
		"/visland stop",
        "/ad stop",
        "/sice stop",
        "/ochillegal off",
        "/fr off",
		"/rotation Settings StartOnCountdown False",
    ];

    private static readonly string[] TaskOwnedAddonNames =
    [
        "SelectString",
        "SelectIconString",
        "SelectYesno",
        "Talk",
        "JournalAccept",
        "Request",
        "FreeCompany",
        "FreeCompanyAction",
        "FreeCompanyExchange",
        "ContentsFinder",
        "ContentsFinderConfirm",
        "GoldSaucerInfo",
        "RaceChocoboResult",
        "ChocoboResult",
        "LovmResult",
        "LotteryDaily",
        "LotteryWeeklyInput",
        "LotteryWeeklyRewardList",
        "FashionCheck",
        "FashionCheckScoreGauge",
        "Shop",
        "RetainerList",
        "RetainerSellList",
        "RetainerSell",
        "RetainerItemTransferList",
        "InventoryRetainerLarge",
        "InventoryRetainer",
        "RetainerGrid0",
        "RetainerGrid1",
        "RetainerGrid2",
        "RetainerGrid3",
        "RetainerGrid4",
        "RetainerCrystalGrid",
        "RetainerTaskAsk",
        "RetainerTaskResult",
        "ContextMenu",
        "RecommendEquip",
    ];

    private static readonly Dictionary<string, EngineState> TaskStateById = new()
    {
        [PostProcessTaskOrder.RefillListings] = EngineState.RunningRetainerListingRefill,
        [PostProcessTaskOrder.RetainerEquipping] = EngineState.RunningRetainerEquipping,
        [PostProcessTaskOrder.FCBuffRefill] = EngineState.RunningFCBuff,
        [PostProcessTaskOrder.VendorStock] = EngineState.RunningVendorStock,
        [PostProcessTaskOrder.RegisterRegistrables] = EngineState.RunningRegisterRegistrables,
        [PostProcessTaskOrder.GearUpdater] = EngineState.RunningGearUpdater,
        [PostProcessTaskOrder.HighestCombatJob] = EngineState.RunningHighestCombatJob,
        [PostProcessTaskOrder.CurrentJobEquipment] = EngineState.RunningCurrentJobEquipment,
        [PostProcessTaskOrder.AlliedSociety] = EngineState.RunningAlliedSociety,
        [PostProcessTaskOrder.SeasonalGear] = EngineState.RunningSeasonalGear,
        [PostProcessTaskOrder.AfterArPark] = EngineState.RunningAfterArPark,
        [PostProcessTaskOrder.MinionRoulette] = EngineState.RunningMinionRoulette,
        [PostProcessTaskOrder.VerminionQueue] = EngineState.RunningVerminion,
        [PostProcessTaskOrder.MiniCactpot] = EngineState.RunningMiniCactpot,
        [PostProcessTaskOrder.JumboCactpot] = EngineState.RunningJumboCactpot,
        [PostProcessTaskOrder.FashionReport] = EngineState.RunningFashionReport,
        [PostProcessTaskOrder.ChocoboRacing] = EngineState.RunningChocoboRacing,
        [PostProcessTaskOrder.LootGoblinMapGather] = EngineState.RunningLootGoblinMapGather,
        [PostProcessTaskOrder.NagYourMom] = EngineState.RunningNagYourMom,
        [PostProcessTaskOrder.NagYourDad] = EngineState.RunningNagYourDad,
    };

    private static readonly Dictionary<EngineState, string> TaskIdByState =
        TaskStateById.ToDictionary(pair => pair.Value, pair => pair.Key);

    private readonly IPluginLog log;
    private readonly Configuration configuration;
    private readonly ConfigManager configManager;
    private readonly ResetDetectionService resetService;
    private readonly FCBuffService fcBuffService;
    private readonly FCBuffInventoryService fcBuffInventoryService;
    private readonly VerminionService verminionService;
    private readonly CactpotService cactpotService;
    private readonly ChocoboRaceService chocoboRaceService;
    private readonly FashionReportService fashionReportService;
    private readonly VendorStockService vendorStockService;
    private readonly FishingService fishingService;
    private readonly RegisterRegistrablesService registerRegistrablesService;
    private readonly GearUpdaterService gearUpdaterService;
    private readonly HighestCombatJobService highestCombatJobService;
    private readonly CurrentJobEquipmentService currentJobEquipmentService;
    private readonly SeasonalGearService seasonalGearService;
    private readonly AlliedSocietyService alliedSocietyService;
    private readonly AfterArParkService afterArParkService;
    private readonly MinionRouletteService minionRouletteService;
    private readonly IEquipmentAutomationRuntime equipmentRuntime;
    private readonly RetainerListingRefillService retainerListingRefillService;
    private readonly RetainerEquippingService retainerEquippingService;
    private readonly WorkshopBellService workshopBellService;
    private readonly ARPostProcessService arService;
    private readonly YesAlreadyIPC yesAlreadyIPC;
    private readonly IClientState clientState;
    private readonly MomIPCClient momIPCClient;
    private readonly DadIPCClient dadIPCClient;
    private readonly LootGoblinMapGatherService lootGoblinMapGatherService;
    private readonly AutoRetainerIPC autoRetainerIPC;
    private readonly VNavmeshIPC vNavmeshIPC;
    private readonly LifestreamIPC lifestreamIPC;
    private readonly VermaxionIncidentWriter incidentWriter;

    private EngineState state = EngineState.Idle;
    private RunTaskPhaseFilter activePhaseFilter = RunTaskPhaseFilter.All;
    private DateTime stateEnteredAt = DateTime.MinValue;
    private CharacterConfig? activeConfig = null;
    private bool weeklyResetDetected = false;
    private bool dailyResetDetected = false;
    private bool nagYourMomRequestIssued = false;
    private string nagYourMomActiveRequestId = string.Empty;
    private string nagYourMomActiveRoute = MomRunRoutes.CasualCc;
    private int nagYourMomRequestedRuns = 0;
    private int nagYourMomLastCompletedRuns = 0;
    private int nagYourMomRouteCursor = 0;
    private DateTime nagYourMomLostStatusSince = DateTime.MinValue;
    private DateTime nagYourMomLostStatusLastLoggedAt = DateTime.MinValue;
    private DadSelectionExecution? activeDadExecution;
    private bool taskStartHoldLogged = false;
    private EngineState taskStartHoldState = EngineState.Idle;
    private JumboCactpotRouteDecision? activeJumboCactpotRoute = null;
    private DateTime handoffQuietSince = DateTime.MinValue;
    private DateTime handoffBlockerLastLoggedAt = DateTime.MinValue;
    private DateTime handoffMovementStopLastIssuedAt = DateTime.MinValue;
    private string handoffBlockerReason = string.Empty;
    private DateTime handoffBlockerSince = DateTime.MinValue;
    private DateTime handoffBlockerLastWarningAt = DateTime.MinValue;
    private DateTime orphanResultSettlingSince = DateTime.MinValue;
    private DateTime orphanResultLastPollAt = DateTime.MinValue;
    private DateTime orphanResultLastCallbackAt = DateTime.MinValue;
    private OceanFishingResultAddonSnapshot orphanResultAddonSnapshot = OceanFishingResultAddonSnapshot.NotPolled;
    private bool orphanResultCallbackDispatched;
    private bool orphanResultClosureLogged;
    private bool orphanResultTimeoutLogged;
    private readonly List<EngineState> runQueue = [];
    private int runQueueIndex = -1;
    private bool currentTaskOwnedWorkStarted;
    private bool runOwnedWorkStarted;
    private bool runHadFailure;
    private RunOutcome pendingRunOutcome = RunOutcome.None;
    private string pendingRunSummary = string.Empty;
    private bool requireEnabledConfig;
    private AutomationRunScope activeRunScope = AutomationRunScope.Full;
    private DateTime watchdogLastProgressAt = DateTime.MinValue;
    private string watchdogLastSignature = string.Empty;
    private string watchdogPauseReason = string.Empty;
    private readonly IReadOnlyDictionary<string, EngineTaskBinding> taskBindings;
    private bool miscHookRan;

    private sealed record EngineTaskBinding(
        string Id,
        EngineState State,
        Func<CharacterConfig, TaskEligibility> Eligibility,
        Action Tick,
        Action Reset,
        Action Cancel,
        Action Cleanup,
        Func<string> Status,
        Func<string> WatchdogProgress);

    public enum EngineState
    {
        Idle,
        Starting,
        CheckingResets,
        RunningFCBuff,
        RunningVendorStock,
        RunningRegisterRegistrables,
        RunningGearUpdater,
        RunningHighestCombatJob,
        RunningCurrentJobEquipment,
        RunningAlliedSociety,
        RunningSeasonalGear,
        RunningAfterArPark,
        RunningMinionRoulette,
        RunningRetainerListingRefill,
        RunningRetainerEquipping,
        RunningVerminion,
        RunningMiniCactpot,
        RunningJumboCactpot,
        RunningFashionReport,
        RunningChocoboRacing,
        RunningLootGoblinMapGather,
        RunningNagYourMom,
        RunningNagYourDad,
        SettlingTask,
        SettlingFinalHandoff,
        SignalingARDone,
        Complete,
        Error,
    }

    private enum RunTaskPhaseFilter
    {
        All,
        BeforeAR,
        AfterAR,
    }

    private sealed record NagYourMomRoutePlan(
        string Route,
        string Label,
        int RemainingRuns,
        bool StopAtSeriesRank25);

    public EngineState State => state;
    public bool IsRunning => state != EngineState.Idle;
    public string StatusText { get; private set; } = "Idle";
    public string NagYourMomStatusText { get; private set; } = "Idle";
    public string NagYourDadStatusText { get; private set; } = "Idle";
    public RunOutcome LastRunOutcome { get; private set; } = RunOutcome.None;
    public string LastRunSummary { get; private set; } = "No run recorded";
    public DateTime? LastRunCompletedAtUtc { get; private set; }
    public string ActiveHandoffBlocker => handoffBlockerReason;
    public bool OwnsActiveWork => IsRunning || runOwnedWorkStarted || arService.IsProcessing;
    public bool OwnsLiveWork => OwnsActiveWork || autoRetainerIPC.SuppressionOwnedByVermaxion;
    public AutomationRegistryValidation RegistryValidation { get; }
    public bool RegistryReady => RegistryValidation.IsValid;
    public string RegistryDiagnostic => RegistryValidation.Message;
    public IReadOnlyList<AutomationPlanEntry> LastPlan { get; private set; } = [];
    public IReadOnlyCollection<string> RegisteredTaskIds => taskBindings.Keys.ToArray();
    
    public bool IsRunningDebug => IsRunning; // For debugging

    public VermaxionEngine(
        IPluginLog log,
        Configuration configuration,
        ConfigManager configManager,
        ResetDetectionService resetService,
        FCBuffService fcBuffService,
        FCBuffInventoryService fcBuffInventoryService,
        VerminionService verminionService,
        CactpotService cactpotService,
        ChocoboRaceService chocoboRaceService,
        FashionReportService fashionReportService,
        VendorStockService vendorStockService,
        FishingService fishingService,
        RegisterRegistrablesService registerRegistrablesService,
        GearUpdaterService gearUpdaterService,
        HighestCombatJobService highestCombatJobService,
        CurrentJobEquipmentService currentJobEquipmentService,
        SeasonalGearService seasonalGearService,
        AlliedSocietyService alliedSocietyService,
        AfterArParkService afterArParkService,
        MinionRouletteService minionRouletteService,
        IEquipmentAutomationRuntime equipmentRuntime,
        RetainerListingRefillService retainerListingRefillService,
        RetainerEquippingService retainerEquippingService,
        WorkshopBellService workshopBellService,
        ARPostProcessService arService,
        YesAlreadyIPC yesAlreadyIPC,
        IClientState clientState,
        MomIPCClient momIPCClient,
        DadIPCClient dadIPCClient,
        LootGoblinMapGatherService lootGoblinMapGatherService,
        AutoRetainerIPC autoRetainerIPC,
        VNavmeshIPC vNavmeshIPC,
        LifestreamIPC lifestreamIPC,
        VermaxionIncidentWriter incidentWriter)
    {
        this.log = log;
        this.configuration = configuration;
        this.configManager = configManager;
        this.resetService = resetService;
        this.fcBuffService = fcBuffService;
        this.fcBuffInventoryService = fcBuffInventoryService;
        this.verminionService = verminionService;
        this.cactpotService = cactpotService;
        this.chocoboRaceService = chocoboRaceService;
        this.fashionReportService = fashionReportService;
        this.vendorStockService = vendorStockService;
        this.fishingService = fishingService;
        this.registerRegistrablesService = registerRegistrablesService;
        this.gearUpdaterService = gearUpdaterService;
        this.highestCombatJobService = highestCombatJobService;
        this.currentJobEquipmentService = currentJobEquipmentService;
        this.seasonalGearService = seasonalGearService;
        this.alliedSocietyService = alliedSocietyService;
        this.afterArParkService = afterArParkService;
        this.minionRouletteService = minionRouletteService;
        this.equipmentRuntime = equipmentRuntime;
        this.retainerListingRefillService = retainerListingRefillService;
        this.retainerEquippingService = retainerEquippingService;
        this.workshopBellService = workshopBellService;
        this.arService = arService;
        this.yesAlreadyIPC = yesAlreadyIPC;
        this.clientState = clientState;
        this.momIPCClient = momIPCClient;
        this.dadIPCClient = dadIPCClient;
        this.lootGoblinMapGatherService = lootGoblinMapGatherService;
        this.autoRetainerIPC = autoRetainerIPC;
        this.vNavmeshIPC = vNavmeshIPC;
        this.lifestreamIPC = lifestreamIPC;
        this.incidentWriter = incidentWriter;

        var registrations = CreateTaskBindings();
        RegistryValidation = AutomationCatalog.ValidateRuntimeRegistry(
            registrations.Select(binding => binding.Id),
            PostProcessTaskOrder.Definitions.Select(definition => definition.Id));
        taskBindings = registrations
            .GroupBy(binding => binding.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        if (!RegistryValidation.IsValid)
            log.Error($"[Engine][Registry] {RegistryValidation.Message}");

        // Subscribe to territory change events to close menus after teleporting
        clientState.TerritoryChanged += OnTerritoryChanged;
    }

    private IReadOnlyList<EngineTaskBinding> CreateTaskBindings()
    {
        EngineTaskBinding Bind(
            string id,
            Func<CharacterConfig, TaskEligibility> eligibility,
            Action tick,
            Action reset,
            Func<string> status,
            Action? cancel = null,
            Action? cleanup = null)
            => new(
                id,
                TaskStateById[id],
                eligibility,
                tick,
                reset,
                cancel ?? reset,
                cleanup ?? reset,
                status,
                status);

        var bindings = new[]
        {
            Bind(PostProcessTaskOrder.RefillListings, EvaluateRefillListings, retainerListingRefillService.Update,
                () => { retainerListingRefillService.Reset(); workshopBellService.Reset(); },
                () => retainerListingRefillService.StatusText),
            Bind(PostProcessTaskOrder.RetainerEquipping, EvaluateRetainerEquipping, retainerEquippingService.Update,
                retainerEquippingService.Cancel, () => retainerEquippingService.StatusText,
                retainerEquippingService.Cancel, retainerEquippingService.CleanupAfterDispatch),
            Bind(PostProcessTaskOrder.FCBuffRefill, EvaluateFcBuff, fcBuffService.Update, fcBuffService.Reset,
                () => fcBuffService.StatusText),
            Bind(PostProcessTaskOrder.VendorStock, EvaluateVendorStock, vendorStockService.Update, vendorStockService.Reset,
                () => vendorStockService.StatusText),
            Bind(PostProcessTaskOrder.RegisterRegistrables, EvaluateRegisterRegistrables, registerRegistrablesService.Update,
                registerRegistrablesService.Reset, () => registerRegistrablesService.State.ToString()),
            Bind(PostProcessTaskOrder.GearUpdater, EvaluateGearUpdater, gearUpdaterService.Update, gearUpdaterService.Reset,
                () => gearUpdaterService.StatusText, () => gearUpdaterService.Cancel()),
            Bind(PostProcessTaskOrder.HighestCombatJob, EvaluateHighestCombatJob, highestCombatJobService.Update,
                highestCombatJobService.Reset, () => highestCombatJobService.StatusText, () => highestCombatJobService.Cancel()),
            Bind(PostProcessTaskOrder.CurrentJobEquipment, EvaluateCurrentJobEquipment, currentJobEquipmentService.Update,
                currentJobEquipmentService.Reset, () => currentJobEquipmentService.StatusText, () => currentJobEquipmentService.Cancel()),
            Bind(PostProcessTaskOrder.AlliedSociety, EvaluateAlliedSociety, alliedSocietyService.Update,
                alliedSocietyService.Reset, () => alliedSocietyService.StatusText, () => alliedSocietyService.Cancel()),
            Bind(PostProcessTaskOrder.SeasonalGear, EvaluateSeasonalGear, seasonalGearService.Update, seasonalGearService.Reset,
                () => seasonalGearService.StatusText, () => seasonalGearService.Cancel()),
            Bind(PostProcessTaskOrder.AfterArPark, EvaluateAfterArPark, afterArParkService.Update,
                afterArParkService.Reset, () => afterArParkService.StatusText, () => afterArParkService.Cancel()),
            Bind(PostProcessTaskOrder.MinionRoulette, EvaluateMinionRoulette, minionRouletteService.Update,
                minionRouletteService.Reset, () => minionRouletteService.StatusText),
            Bind(PostProcessTaskOrder.VerminionQueue, EvaluateVerminion, verminionService.Update, verminionService.Reset,
                () => verminionService.StatusText),
            Bind(PostProcessTaskOrder.MiniCactpot, EvaluateMiniCactpot, cactpotService.Update, cactpotService.Reset,
                () => cactpotService.StatusText),
            Bind(PostProcessTaskOrder.JumboCactpot, EvaluateJumboCactpot, cactpotService.Update, cactpotService.Reset,
                () => cactpotService.StatusText),
            Bind(PostProcessTaskOrder.FashionReport, EvaluateFashionReport, fashionReportService.Update,
                fashionReportService.Reset, () => fashionReportService.State.ToString()),
            Bind(PostProcessTaskOrder.ChocoboRacing, EvaluateChocoboRacing, chocoboRaceService.Update,
                chocoboRaceService.Reset, () => chocoboRaceService.StatusText),
            Bind(PostProcessTaskOrder.LootGoblinMapGather, EvaluateLootGoblin, lootGoblinMapGatherService.Update,
                lootGoblinMapGatherService.Reset, () => lootGoblinMapGatherService.StatusText, lootGoblinMapGatherService.Cancel),
            Bind(PostProcessTaskOrder.NagYourMom, EvaluateNagYourMom, () => { }, () => { },
                () => NagYourMomStatusText, () => { momIPCClient.CancelActiveRun(); }),
            Bind(PostProcessTaskOrder.NagYourDad, EvaluateNagYourDad, () => { }, () => { },
                () => NagYourDadStatusText,
                () => { if (activeDadExecution != null) dadIPCClient.CancelSelection(activeDadExecution); }),
        };

        return bindings;
    }

    public TaskEligibility GetTaskEligibility(string id)
    {
        var config = configManager.GetActiveConfig();
        if (config == null)
            return TaskEligibility.Blocked("No active character configuration is available.");
        return taskBindings.TryGetValue(id, out var binding)
            ? binding.Eligibility(config)
            : TaskEligibility.Unsupported($"No runtime binding is registered for '{id}'.");
    }

    private static TaskEligibility Enabled(bool enabled, string label)
        => enabled ? TaskEligibility.Runnable() : TaskEligibility.Disabled($"{label} is disabled for this character.");

    private TaskEligibility EvaluateRefillListings(CharacterConfig config)
        => !config.EnableRefillFromListings
            ? TaskEligibility.Disabled("Refill Listings is disabled for this character.")
            : ShouldRunRefillFromListings(config)
                ? TaskEligibility.Runnable()
                : TaskEligibility.NotDue($"Refill Listings is not due for its {config.RefillFromListingsFrequency} cadence.");

    private TaskEligibility EvaluateFcBuff(CharacterConfig config)
        => !config.EnableFCBuffRefill
            ? TaskEligibility.Disabled("FC Buff Refill is disabled for this character.")
            : ShouldRunFCBuff(config)
                ? TaskEligibility.Runnable()
                : TaskEligibility.NotDue($"FC Buff Refill is not due for its {config.FCBuffFrequency} cadence.");

    private static TaskEligibility EvaluateVendorStock(CharacterConfig config)
        => !config.EnableVendorStock
            ? TaskEligibility.Disabled("Vendor Stock is disabled for this character.")
            : config.VendorStockGysahlGreensTarget <= 0 && config.VendorStockGrade8DarkMatterTarget <= 0
                ? TaskEligibility.Blocked("Vendor Stock has no positive item target configured.")
                : TaskEligibility.Runnable();

    private static TaskEligibility EvaluateRegisterRegistrables(CharacterConfig config)
        => !config.EnableRegisterRegistrables
            ? TaskEligibility.Disabled("Register Registrables is disabled for this character.")
            : !RegistrableRegistrationPolicy.CanStart(
                featureEnabled: true,
                config.RegisterUnregisteredItemsFromInventory,
                config.PersonalRegistrableItems.Count)
                ? TaskEligibility.Blocked("Register Registrables has no personal items configured.")
                : TaskEligibility.Runnable();

    private TaskEligibility EvaluateGearUpdater(CharacterConfig config)
    {
        if (!config.EnableGearUpdater)
            return TaskEligibility.Disabled("Gear Updater is disabled for this character.");
        return equipmentRuntime.GetValidGearsets().Count > 0 ||
               equipmentRuntime.CharacterContentId != 0 && equipmentRuntime.CurrentJobId != 0
            ? TaskEligibility.Runnable()
            : TaskEligibility.Blocked("Gear Updater cannot bootstrap without stable current-character and job data.");
    }

    private TaskEligibility EvaluateHighestCombatJob(CharacterConfig config)
    {
        if (!config.EnableHighestCombatJob)
            return TaskEligibility.Disabled("Highest Combat Job is disabled for this character.");
        return EquipmentAutomationPolicy.SelectHighestCombatJob(
                   equipmentRuntime.GetValidGearsets(),
                   equipmentRuntime.CurrentJobId) != null ||
               equipmentRuntime.CharacterContentId != 0 && equipmentRuntime.CurrentJobId != 0
            ? TaskEligibility.Runnable()
            : TaskEligibility.Blocked("Highest Combat Job cannot bootstrap without stable current-character and job data.");
    }

    private TaskEligibility EvaluateCurrentJobEquipment(CharacterConfig config)
        => !config.EnableCurrentJobEquipment
            ? TaskEligibility.Disabled("Current Job Equipment is disabled for this character.")
            : EquipmentAutomationPolicy.SelectCurrentGearset(
                equipmentRuntime.GetValidGearsets(), equipmentRuntime.CurrentGearsetId, equipmentRuntime.CurrentJobId) == null
                ? TaskEligibility.Blocked("Current Job Equipment requires the active job to match a valid saved gearset.")
                : TaskEligibility.Runnable();

    private TaskEligibility EvaluateAlliedSociety(CharacterConfig config)
    {
        if (!config.EnableAlliedSociety)
            return TaskEligibility.Disabled("Allied Society is disabled for this character.");
        if (!ResetDetectionService.TaskNeedsRun(config.AlliedSocietyLastCompleted, config.AlliedSocietyNextReset))
            return TaskEligibility.NotDue($"Allied Society is not due until {config.AlliedSocietyNextReset:u}.");

        var gearsets = equipmentRuntime.GetValidGearsets();
        var selected = config.AlliedSocietyGearsetSelection switch
        {
            AlliedSocietyGearsetSelection.CurrentJob => EquipmentAutomationPolicy.SelectCurrentGearset(
                gearsets,
                equipmentRuntime.CurrentGearsetId,
                equipmentRuntime.CurrentJobId),
            AlliedSocietyGearsetSelection.SavedGearset => gearsets.FirstOrDefault(
                gearset => gearset.GearsetId == config.AlliedSocietyGearsetId),
            _ => null,
        };
        return selected == null
            ? TaskEligibility.Blocked(config.AlliedSocietyGearsetSelection == AlliedSocietyGearsetSelection.SavedGearset
                ? "Allied Society requires a valid selected saved gearset."
                : "Allied Society Current Job requires a valid active saved gearset.")
            : TaskEligibility.Runnable();
    }

    private TaskEligibility EvaluateSeasonalGear(CharacterConfig config)
        => !config.EnableSeasonalGearRoulette
            ? TaskEligibility.Disabled("Seasonal Gear is disabled for this character.")
            : equipmentRuntime.FindSeasonalInventoryItems(
                EquipmentAutomationPolicy.DeduplicateCuratedItemIds(SeasonalGearService.CuratedItemIds)).Count == 0
                ? TaskEligibility.Blocked("Seasonal Gear found no curated equippable items in inventory or the Armoury Chest.")
                : TaskEligibility.Runnable();

    private static TaskEligibility EvaluateAfterArPark(CharacterConfig config)
    {
        if (!config.EnableAfterArPark)
            return TaskEligibility.Disabled("After-AR Park is disabled for this character.");
        return AfterArParkService.TryResolveCommand(
            config.AfterArParkDestination,
            config.AfterArParkCustomCommand,
            out _,
            out var error)
            ? TaskEligibility.Runnable()
            : TaskEligibility.Blocked(error);
    }

    private static TaskEligibility EvaluateMinionRoulette(CharacterConfig config)
        => Enabled(config.EnableMinionRoulette, "Minion Roulette");

    private static TaskEligibility EvaluateRetainerEquipping(CharacterConfig config)
        => EvaluateRetainerEquipping(config, ignoreSchedulingFlag: false);

    private static TaskEligibility EvaluateRetainerEquipping(
        CharacterConfig config,
        bool ignoreSchedulingFlag)
        => !ignoreSchedulingFlag && !config.EnableRetainerEquipping
            ? TaskEligibility.Disabled("Retainer Equipping is disabled for this character.")
            : config.RetainerCombatItemLevelTarget <= 0 &&
              config.RetainerGatheringPerceptionTarget <= 0
                ? TaskEligibility.Blocked("Retainer Equipping requires a combat item-level or gathering Perception target above zero.")
                : TaskEligibility.Runnable();

    private static TaskEligibility Due(bool enabled, string label, DateTime completed, DateTime next)
        => !enabled
            ? TaskEligibility.Disabled($"{label} is disabled for this character.")
            : ResetDetectionService.TaskNeedsRun(completed, next)
                ? TaskEligibility.Runnable()
                : TaskEligibility.NotDue($"{label} is not due until {next:u}.");

    private static TaskEligibility EvaluateVerminion(CharacterConfig config)
        => Due(config.EnableVerminionQueue, "Verminion Queue", config.VerminionLastCompleted, config.VerminionNextReset);

    private static TaskEligibility EvaluateMiniCactpot(CharacterConfig config)
        => Due(config.EnableMiniCactpot, "Mini Cactpot", config.MiniCactpotLastCompleted, config.MiniCactpotNextReset);

    private TaskEligibility EvaluateJumboCactpot(CharacterConfig config)
    {
        if (!config.EnableJumboCactpot)
            return TaskEligibility.Disabled("Jumbo Cactpot is disabled for this character.");
        var now = DateTime.UtcNow;
        var decision = JumboCactpotRoutingPolicy.Decide(
            now,
            ResetDetectionService.IsJumboCactpotPayoutAvailable(now),
            config.JumboCactpotUnclaimedTickets,
            config.JumboCactpotPayoutAvailableAt,
            ResetDetectionService.TaskNeedsRun(config.JumboCactpotLastCompleted, config.JumboCactpotNextReset));
        return decision.Route == JumboCactpotRoute.Wait
            ? TaskEligibility.NotDue("Jumbo Cactpot has no payout or ticket-purchase route due now.")
            : TaskEligibility.Runnable($"Route: {decision.Route}.");
    }

    private static TaskEligibility EvaluateFashionReport(CharacterConfig config)
    {
        if (!config.EnableFashionReport)
            return TaskEligibility.Disabled("Fashion Report is disabled for this character.");
        if (!ResetDetectionService.IsFashionReportAvailable(DateTime.UtcNow))
            return TaskEligibility.Blocked("Fashion Report is outside its Friday-to-reset availability window.");
        return ResetDetectionService.TaskNeedsRun(config.FashionReportLastCompleted, config.FashionReportNextReset)
            ? TaskEligibility.Runnable()
            : TaskEligibility.NotDue($"Fashion Report is not due until {config.FashionReportNextReset:u}.");
    }

    private static TaskEligibility EvaluateChocoboRacing(CharacterConfig config)
        => Due(config.EnableChocoboRacing, "Chocobo Racing", config.ChocoboRacingLastCompleted, config.ChocoboRacingNextReset);

    private static TaskEligibility EvaluateLootGoblin(CharacterConfig config)
        => Due(config.EnableLootGoblinMapGather, "LootGoblin Map Gather", config.LootGoblinMapGatherLastCompleted, config.LootGoblinMapGatherNextReset);

    private TaskEligibility EvaluateNagYourMom(CharacterConfig config)
    {
        if (!config.EnableNagYourMom)
            return TaskEligibility.Disabled("nag your mom is disabled for this character.");
        if (string.IsNullOrWhiteSpace(config.NagYourMomJob))
            return TaskEligibility.Blocked("Set a mom job.");
        if (!TryParseLocalTime(config.NagYourMomWindowStartLocal, out var start) ||
            !TryParseLocalTime(config.NagYourMomWindowEndLocal, out var end))
            return TaskEligibility.Blocked("The mom local-time window is invalid.");
        if (!IsWithinLocalWindow(DateTime.Now.TimeOfDay, start, end))
            return TaskEligibility.NotDue($"Outside mom window ({config.NagYourMomWindowStartLocal}-{config.NagYourMomWindowEndLocal}).");
        RollNagYourMomLocalDay(config);
        if (!NagYourMomRouteOrder.Any(route => IsNagYourMomRouteDue(config, route)))
            return TaskEligibility.NotDue("All enabled mom routes reached their local-day caps.");
        var readiness = momIPCClient.GetReadiness();
        return readiness.CanStart
            ? TaskEligibility.Runnable()
            : TaskEligibility.Blocked(string.IsNullOrWhiteSpace(readiness.BlockReason) ? readiness.Summary : readiness.BlockReason);
    }

    private TaskEligibility EvaluateNagYourDad(CharacterConfig config)
    {
        if (!ShouldRunNagYourDadNow(config, out var reason))
            return !config.EnableNagYourDad ? TaskEligibility.Disabled(reason) : TaskEligibility.Blocked(reason);
        return dadIPCClient.IsReady()
            ? TaskEligibility.Runnable()
            : TaskEligibility.Blocked("DAD IPC is unavailable or not ready.");
    }

    public bool StartPostProcess()
    {
        return TryBeginRun(RunTaskPhaseFilter.AfterAR, requireEnabled: true, requireWorldReady: false, automatedRun: true, "post-processing");
    }

    public bool StartBeforeAutoRetainer()
    {
        return TryBeginRun(RunTaskPhaseFilter.BeforeAR, requireEnabled: true, requireWorldReady: true, automatedRun: true, "before-AR");
    }

    private bool TryGetBeforeArWorldReady(out string reason)
    {
        var localPlayer = Plugin.ObjectTable.LocalPlayer;
        var charName = localPlayer?.Name.ToString() ?? "";
        var worldName = localPlayer?.HomeWorld.Value.Name.ToString() ?? "";
        var contentId = Plugin.PlayerState.ContentId;
        var loggedIn = clientState.IsLoggedIn;
        var hasLocalPlayer = localPlayer != null;
        var betweenAreas = Plugin.Condition[ConditionFlag.BetweenAreas];
        var betweenAreas51 = Plugin.Condition[ConditionFlag.BetweenAreas51];
        var playerAvailable = IsPlayerAvailable();

        if (loggedIn &&
            hasLocalPlayer &&
            !string.IsNullOrEmpty(charName) &&
            !string.IsNullOrEmpty(worldName) &&
            contentId != 0 &&
            !betweenAreas &&
            !betweenAreas51 &&
            playerAvailable)
        {
            reason = $"character={charName}@{worldName}, contentId={contentId:X16}";
            return true;
        }

        reason = $"loggedIn={loggedIn}, hasLocalPlayer={hasLocalPlayer}, character='{charName}', world='{worldName}', contentId={contentId:X16}, BetweenAreas={betweenAreas}, BetweenAreas51={betweenAreas51}, playerAvailable={playerAvailable}";
        return false;
    }

    public bool ManualStart()
    {
        return TryBeginRun(RunTaskPhaseFilter.All, requireEnabled: false, requireWorldReady: false, automatedRun: false, "manual");
    }

    public bool ManualStartRetainerEquipping()
    {
        return TryBeginRun(
            RunTaskPhaseFilter.All,
            requireEnabled: false,
            requireWorldReady: false,
            automatedRun: false,
            "manual Retainer Equipping",
            AutomationRunScope.SingleTask(PostProcessTaskOrder.RetainerEquipping));
    }

    public void RecordSkippedOpportunity(string summary)
    {
        if (!IsRunning)
            RecordRunCompletion(RunOutcome.Skipped, summary);
    }

    private bool TryBeginRun(
        RunTaskPhaseFilter phaseFilter,
        bool requireEnabled,
        bool requireWorldReady,
        bool automatedRun,
        string source,
        AutomationRunScope? runScope = null)
    {
        if (!RegistryValidation.IsValid)
        {
            log.Error($"[Engine][Registry] Rejecting {source}: {RegistryValidation.Message}");
            if (arService.IsProcessing)
                arService.FinishPostProcess(force: true);
            autoRetainerIPC.ReleaseSuppressionIfOwned(force: true);
            RecordRunCompletion(RunOutcome.Failed, RegistryValidation.Message);
            SetState(EngineState.Idle);
            return false;
        }

        var startBlocker = StartBlocker();
        if (!string.IsNullOrWhiteSpace(startBlocker))
        {
            log.Information($"[Engine] Rejected {source} start: {startBlocker}");
            return false;
        }

        if (!LifecyclePolicy.CanStart(IsRunning))
        {
            log.Warning($"[Engine] Rejected overlapping {source} start while state={state}");
            return false;
        }

        runScope ??= AutomationRunScope.Full;
        if (runScope.SingleTaskId != null &&
            !taskBindings.ContainsKey(runScope.SingleTaskId))
        {
            log.Warning($"[Engine] Rejected {source}: no engine binding exists for '{runScope.SingleTaskId}'.");
            return false;
        }

        ResetRunTracking();
        activePhaseFilter = phaseFilter;
        activeRunScope = runScope;
        requireEnabledConfig = requireEnabled;
        activeConfig = configManager.GetActiveConfig();
        NagYourMomStatusText = "Idle";
        NagYourDadStatusText = "Idle";

        if (requireWorldReady && !TryGetBeforeArWorldReady(out var worldReadyReason))
        {
            log.Warning($"[Engine] Skipping before-AR start because world is not ready: {worldReadyReason}");
            BeginImmediateFinalization(RunOutcome.Skipped, "Before-AR world was not ready");
            return true;
        }

        if (activeConfig == null || (requireEnabled && !activeConfig.Enabled))
        {
            log.Information($"[Engine] Skipping {source}: config missing or disabled");
            BeginImmediateFinalization(RunOutcome.Skipped, $"{source} config missing or disabled");
            return true;
        }

        log.Information($"[Engine] === Starting Vermaxion {source} run ===");
        SetState(EngineState.CheckingResets);
        return true;
    }

    public void Cancel()
    {
        log.Warning("[Engine] Cancelled by user");
        CancelForSettling("Cancelled");
    }

    public void Stop()
    {
        log.Information("[Engine] Stopped by user");
        CancelForSettling("Stopped");
    }

    public void ForceStop()
    {
        log.Warning("[Engine] Full Stop force-releasing ownership");
        momIPCClient.CancelActiveRun();
        dadIPCClient.CancelActiveRun();
        lootGoblinMapGatherService.Cancel();
        CancelTaskServices();
        vNavmeshIPC.Stop();
        TryCloseOwnedUiBestEffort();
        if (arService.IsProcessing)
            arService.FinishPostProcess(force: true);
        yesAlreadyIPC.Unpause();
        autoRetainerIPC.ReleaseSuppressionIfOwned(force: true);
        ResetHandoffTracking();
        RecordRunCompletion(RunOutcome.ForceStopped, "Full Stop force-released ownership");
        ResetRunTracking();
        SetState(EngineState.Idle);
    }

    public void SendRunShutdownCommandBundle()
    {
        foreach (var command in StartupMiscCommands)
            CommandHelper.SendCommand(command);

        log.Information("[Engine] Sent Misc Cmd startup bundle");
    }

    private void SendStartupMiscCommandBundleIfEnabled()
    {
        if (activeConfig?.EnableMiscCmd != true)
        {
            log.Information("[Engine] Misc Cmd startup bundle disabled for this character");
            return;
        }

        SendRunShutdownCommandBundle();
    }

    public int GetPendingTaskCount()
    {
        return Math.Max(0, runQueue.Count - Math.Max(0, runQueueIndex + 1));
    }

    public void Update()
    {
        if (state == EngineState.Idle)
            return;

        try
        {
            if (TickTaskWatchdog())
                return;
            UpdateCore();
        }
        catch (Exception ex)
        {
            log.Error($"[Engine] Unhandled engine exception while state={state}: {ex}");
            WriteIncident("engine-exception", ex.Message);
            CleanupFaultingWork(state, "engine exception");
            runHadFailure = true;
            pendingRunOutcome = RunOutcome.Failed;
            pendingRunSummary = $"Unhandled exception in {state}: {ex.Message}";
            if (LifecyclePolicy.RequiresSettling(runOwnedWorkStarted))
                BeginFinalHandoffSettling();
            else
                SetState(EngineState.SignalingARDone);
        }
    }

    private void UpdateCore()
    {
        var elapsed = (DateTime.UtcNow - stateEnteredAt).TotalSeconds;

        switch (state)
        {
            case EngineState.Starting:
                if (elapsed < 1.5) return; // AR settle delay
                RevalidatePlannedQueue();
                var miscHookRunnable = AutomationRunScopePolicy.ShouldRunMiscHook(
                    activeRunScope,
                    activeConfig?.EnableMiscCmd == true,
                    activePhaseFilter == RunTaskPhaseFilter.BeforeAR);
                if (!AutomationRunHookPolicy.HasApplicableWork(runQueue.Count > 0, miscHookRunnable))
                {
                    BeginImmediateFinalization(RunOutcome.Skipped, "Planned tasks were no longer runnable before dispatch");
                    break;
                }

                if (miscHookRunnable && !miscHookRan)
                {
                    SendRunShutdownCommandBundle();
                    miscHookRan = true;
                    runOwnedWorkStarted = true;
                }

                if (runQueue.Count == 0)
                {
                    BeginImmediateFinalization(RunOutcome.Succeeded, "Misc Commands run-start hook completed");
                    break;
                }

                yesAlreadyIPC.Pause();
                DispatchNextQueuedTask();
                break;

            case EngineState.CheckingResets:
                activeConfig = GetLiveActiveConfig();
                if (activeConfig == null || (requireEnabledConfig && !activeConfig.Enabled))
                {
                    BeginImmediateFinalization(RunOutcome.Skipped, "Config became unavailable or disabled before planning");
                    break;
                }

                weeklyResetDetected = resetService.CheckWeeklyReset(activeConfig!);
                dailyResetDetected = resetService.CheckDailyReset(activeConfig!);
                
                // NEW: Migrate from legacy flags if needed
                resetService.MigrateFromLegacyFlags(activeConfig!);
                
                configManager.SaveCurrentAccount();

                log.Information($"[Engine] Weekly reset: {weeklyResetDetected}, Daily reset: {dailyResetDetected}, Saturday: {resetService.IsSaturday()}");
                BuildRunQueue(activeConfig);
                var miscOnlyApplicable = AutomationRunScopePolicy.ShouldRunMiscHook(
                    activeRunScope,
                    activeConfig.EnableMiscCmd,
                    activePhaseFilter == RunTaskPhaseFilter.BeforeAR);
                if (!AutomationRunHookPolicy.HasApplicableWork(runQueue.Count > 0, miscOnlyApplicable))
                {
                    BeginImmediateFinalization(RunOutcome.Skipped, BuildNoRunnableWorkSummary());
                    break;
                }

                log.Information($"[Engine] Runnable queue: [{string.Join(", ", runQueue.Select(taskState => TaskIdByState[taskState]))}]");
                SetState(EngineState.Starting);
                break;

            case EngineState.RunningFCBuff:
                if (activeConfig!.EnableFCBuffRefill)
                {
                    if (!fcBuffService.IsActive && !fcBuffService.IsComplete && !fcBuffService.IsFailed)
                    {
                        // Clean slate before starting FC Buff
                        log.Information("[Engine] Clean slate: clearing open UI before FC Buff");
                        ResetInteractionState();

                        MarkCurrentTaskWorkStarted();
                        fcBuffService.Start(activeConfig.FCBuffPurchaseAttempts);
                        return;
                    }

                    taskBindings[PostProcessTaskOrder.FCBuffRefill].Tick();

                    if (fcBuffService.IsComplete || fcBuffService.IsFailed)
                    {
                        if (fcBuffService.IsFailed)
                        {
                            log.Warning("[Engine] FC buff refill failed - continuing");
                            runHadFailure = true;
                        }
                        else
                        {
                            PersistFCBuffCompletion(
                                GetLiveActiveConfig().FCBuffFrequency,
                                fcBuffService.CompletedViaRankOneToSevenShortcut);
                        }
                        fcBuffService.Reset();
                        AdvanceToNextTask(EngineState.RunningFCBuff);
                    }
                }
                else
                {
                    AdvanceToNextTask(EngineState.RunningFCBuff);
                }
                break;

            case EngineState.RunningVendorStock:
                if (activeConfig!.EnableVendorStock)
                {
                    if (!vendorStockService.IsActive && !vendorStockService.IsComplete && !vendorStockService.IsFailed)
                    {
                        log.Information("[Engine] Starting Vendor Stock");
                        ResetInteractionState();
                        MarkCurrentTaskWorkStarted();
                        vendorStockService.Start();
                        return;
                    }

                    taskBindings[PostProcessTaskOrder.VendorStock].Tick();

                    if (vendorStockService.IsComplete)
                    {
                        log.Information("[Engine] Vendor Stock completed");
                        vendorStockService.Reset();
                        AdvanceToNextTask(EngineState.RunningVendorStock);
                    }
                    else if (vendorStockService.IsFailed)
                    {
                        log.Warning("[Engine] Vendor Stock failed - continuing");
                        runHadFailure = true;
                        vendorStockService.Reset();
                        AdvanceToNextTask(EngineState.RunningVendorStock);
                    }
                }
                else
                {
                    AdvanceToNextTask(EngineState.RunningVendorStock);
                }
                break;

            case EngineState.RunningRegisterRegistrables:
                if (activeConfig!.EnableRegisterRegistrables)
                {
                    if (!registerRegistrablesService.IsActive && !registerRegistrablesService.IsComplete && !registerRegistrablesService.IsFailed)
                    {
                        log.Information("[Engine] Starting Register Registrables");
                        MarkCurrentTaskWorkStarted();
                        registerRegistrablesService.Start();
                        return;
                    }

                    taskBindings[PostProcessTaskOrder.RegisterRegistrables].Tick();

                    if (registerRegistrablesService.IsComplete)
                    {
                        log.Information("[Engine] Register Registrables completed");
                        registerRegistrablesService.Reset();
                        AdvanceToNextTask(EngineState.RunningRegisterRegistrables);
                    }
                    else if (registerRegistrablesService.IsFailed)
                    {
                        log.Warning("[Engine] Register Registrables failed - continuing");
                        runHadFailure = true;
                        registerRegistrablesService.Reset();
                        AdvanceToNextTask(EngineState.RunningRegisterRegistrables);
                    }
                }
                else
                {
                    AdvanceToNextTask(EngineState.RunningRegisterRegistrables);
                }
                break;

            case EngineState.RunningGearUpdater:
                TickSimpleRegisteredTask(
                    EngineState.RunningGearUpdater,
                    activeConfig!.EnableGearUpdater,
                    () => gearUpdaterService.IsActive,
                    () => gearUpdaterService.IsComplete,
                    () => gearUpdaterService.IsFailed,
                    gearUpdaterService.Start);
                break;

            case EngineState.RunningHighestCombatJob:
                TickSimpleRegisteredTask(
                    EngineState.RunningHighestCombatJob,
                    activeConfig!.EnableHighestCombatJob,
                    () => highestCombatJobService.IsActive,
                    () => highestCombatJobService.IsComplete,
                    () => highestCombatJobService.IsFailed,
                    highestCombatJobService.Start);
                break;

            case EngineState.RunningCurrentJobEquipment:
                TickSimpleRegisteredTask(
                    EngineState.RunningCurrentJobEquipment,
                    activeConfig!.EnableCurrentJobEquipment,
                    () => currentJobEquipmentService.IsActive,
                    () => currentJobEquipmentService.IsComplete,
                    () => currentJobEquipmentService.IsFailed,
                    currentJobEquipmentService.Start);
                break;

            case EngineState.RunningAlliedSociety:
                if (!activeConfig!.EnableAlliedSociety)
                {
                    alliedSocietyService.Reset();
                    AdvanceToNextTask(EngineState.RunningAlliedSociety);
                    break;
                }

                if (!alliedSocietyService.IsActive && !alliedSocietyService.IsComplete && !alliedSocietyService.IsFailed)
                {
                    log.Information("[Engine] Starting Allied Society");
                    MarkCurrentTaskWorkStarted();
                    alliedSocietyService.Start(activeConfig);
                    return;
                }

                taskBindings[PostProcessTaskOrder.AlliedSociety].Tick();
                if (alliedSocietyService.IsComplete)
                {
                    var completedAt = DateTime.UtcNow;
                    PersistCurrentCharacterConfig(config =>
                    {
                        config.AlliedSocietyLastCompleted = completedAt;
                        config.AlliedSocietyNextReset = ResetDetectionService.GetNextDailyReset(completedAt);
                    }, "Allied Society completion");
                    log.Information($"[Engine] Allied Society completed: {alliedSocietyService.StatusText}");
                    alliedSocietyService.Reset();
                    AdvanceToNextTask(EngineState.RunningAlliedSociety);
                }
                else if (alliedSocietyService.IsFailed)
                {
                    runHadFailure = true;
                    log.Warning($"[Engine] Allied Society failed and remains unstamped: {alliedSocietyService.StatusText}");
                    alliedSocietyService.Reset();
                    AdvanceToNextTask(EngineState.RunningAlliedSociety);
                }
                break;

            case EngineState.RunningSeasonalGear:
                TickSimpleRegisteredTask(
                    EngineState.RunningSeasonalGear,
                    activeConfig!.EnableSeasonalGearRoulette,
                    () => seasonalGearService.IsActive,
                    () => seasonalGearService.IsComplete,
                    () => seasonalGearService.IsFailed,
                    seasonalGearService.Start);
                break;

            case EngineState.RunningAfterArPark:
                TickSimpleRegisteredTask(
                    EngineState.RunningAfterArPark,
                    activeConfig!.EnableAfterArPark,
                    () => afterArParkService.IsActive,
                    () => afterArParkService.IsComplete,
                    () => afterArParkService.IsFailed,
                    () => afterArParkService.Start(activeConfig));
                break;

            case EngineState.RunningMinionRoulette:
                TickSimpleRegisteredTask(
                    EngineState.RunningMinionRoulette,
                    activeConfig!.EnableMinionRoulette,
                    () => minionRouletteService.IsActive,
                    () => minionRouletteService.IsComplete,
                    () => minionRouletteService.IsFailed,
                    minionRouletteService.Start);
                break;

            case EngineState.RunningRetainerEquipping:
                TickSimpleRegisteredTask(
                    EngineState.RunningRetainerEquipping,
                    AutomationRunScopePolicy.IsTaskSchedulingEnabled(
                        activeRunScope,
                        PostProcessTaskOrder.RetainerEquipping,
                        activeConfig!.EnableRetainerEquipping),
                    () => retainerEquippingService.IsActive,
                    () => retainerEquippingService.IsComplete,
                    () => retainerEquippingService.IsFailed,
                    () => retainerEquippingService.Start(activeConfig!));
                break;

            case EngineState.RunningRetainerListingRefill:
                activeConfig = GetLiveActiveConfig();
                if (ShouldRunRefillFromListings(activeConfig!))
                {
                    if (!retainerListingRefillService.IsActive && !retainerListingRefillService.IsComplete && !retainerListingRefillService.IsFailed)
                    {
                        log.Information("[Engine] Starting Retainer Listing Refill");
                        ResetInteractionState();
                        MarkCurrentTaskWorkStarted();
                        retainerListingRefillService.Start(activeConfig!);
                        return;
                    }

                    taskBindings[PostProcessTaskOrder.RefillListings].Tick();

                    if (retainerListingRefillService.IsComplete)
                    {
                        log.Information("[Engine] Retainer Listing Refill completed");
                        PersistRefillFromListingsCompletion(activeConfig!.RefillFromListingsFrequency);
                        retainerListingRefillService.Reset();
                        AdvanceToNextTask(EngineState.RunningRetainerListingRefill);
                    }
                    else if (retainerListingRefillService.IsFailed)
                    {
                        log.Warning($"[Engine] Retainer Listing Refill failed - continuing: {retainerListingRefillService.LastError}");
                        runHadFailure = true;
                        retainerListingRefillService.Reset();
                        AdvanceToNextTask(EngineState.RunningRetainerListingRefill);
                    }
                }
                else
                {
                    AdvanceToNextTask(EngineState.RunningRetainerListingRefill);
                }
                break;

            case EngineState.RunningVerminion:
                activeConfig = GetLiveActiveConfig();
                if (activeConfig!.EnableVerminionQueue &&
                    ResetDetectionService.TaskNeedsRun(activeConfig.VerminionLastCompleted, activeConfig.VerminionNextReset))
                {
                    if (!verminionService.IsActive && !verminionService.IsComplete && !verminionService.IsFailed)
                    {
                        // Clean slate before starting Verminion
                        log.Information("[Engine] Clean slate: clearing open UI before Verminion");
                        ResetInteractionState();

                        MarkCurrentTaskWorkStarted();
                        verminionService.Start();
                        return;
                    }

                    taskBindings[PostProcessTaskOrder.VerminionQueue].Tick();

                    if (verminionService.IsComplete)
                    {
                        var completedAt = DateTime.UtcNow;
                        PersistCurrentCharacterConfig(config =>
                        {
                            config.VerminionLastCompleted = completedAt;
                            config.VerminionNextReset = ResetDetectionService.GetNextWeeklyReset(completedAt);
                            config.VerminionCompletedThisWeek = true;
                        }, "Verminion completion");
                        verminionService.Reset();
                        AdvanceToNextTask(EngineState.RunningVerminion);
                    }
                    else if (verminionService.IsFailed)
                    {
                        log.Warning("[Engine] Verminion failed - continuing");
                        runHadFailure = true;
                        MarkWeeklyTaskFailed(
                            taskName: "Verminion",
                            clearLegacyFlag: config => config.VerminionCompletedThisWeek = false);
                        verminionService.Reset();
                        AdvanceToNextTask(EngineState.RunningVerminion);
                    }
                }
                else
                {
                    AdvanceToNextTask(EngineState.RunningVerminion);
                }
                break;

            case EngineState.RunningMiniCactpot:
                activeConfig = GetLiveActiveConfig();
                if (activeConfig!.EnableMiniCactpot &&
                    ResetDetectionService.TaskNeedsRun(activeConfig.MiniCactpotLastCompleted, activeConfig.MiniCactpotNextReset))
                {
                    if (!cactpotService.IsActive && !cactpotService.IsComplete && !cactpotService.IsFailed)
                    {
                        if (ShouldHoldNonDutyTaskStart("Mini Cactpot"))
                            return;

                        // Clean slate before starting Mini Cactpot
                        log.Information("[Engine] Clean slate: clearing open UI before Mini Cactpot");
                        ResetInteractionState();

                        MarkCurrentTaskWorkStarted();
                        cactpotService.StartMiniCactpot();
                        return;
                    }

                    taskBindings[PostProcessTaskOrder.JumboCactpot].Tick();

                    if (cactpotService.IsComplete)
                    {
                        var completedAt = DateTime.UtcNow;
                        PersistCurrentCharacterConfig(config =>
                        {
                            config.MiniCactpotLastCompleted = completedAt;
                            config.MiniCactpotNextReset = ResetDetectionService.GetNextDailyReset(completedAt);
                            config.MiniCactpotCompletedToday = true;
                            config.MiniCactpotTicketsToday = Math.Max(config.MiniCactpotTicketsToday, 3);
                        }, "Mini Cactpot completion");
                        cactpotService.Reset();
                        AdvanceToNextTask(EngineState.RunningMiniCactpot);
                    }
                    else if (cactpotService.IsFailed)
                    {
                        log.Warning("[Engine] Mini Cactpot failed - continuing");
                        runHadFailure = true;
                        PersistCurrentCharacterConfig(config =>
                        {
                            config.MiniCactpotCompletedToday = false;
                            config.MiniCactpotTicketsToday = Math.Clamp(config.MiniCactpotTicketsToday, 0, 3);
                        }, "Mini Cactpot partial failure");
                        cactpotService.Reset();
                        AdvanceToNextTask(EngineState.RunningMiniCactpot);
                    }
                }
                else
                {
                    AdvanceToNextTask(EngineState.RunningMiniCactpot);
                }
                break;

            case EngineState.RunningJumboCactpot:
                activeConfig = GetLiveActiveConfig();
                if (activeConfig!.EnableJumboCactpot)
                {
                    var now = DateTime.UtcNow;
                    var purchaseDue = ResetDetectionService.TaskNeedsRun(
                        activeConfig.JumboCactpotLastCompleted,
                        activeConfig.JumboCactpotNextReset);
                    var route = activeJumboCactpotRoute ?? JumboCactpotRoutingPolicy.Decide(
                        now,
                        ResetDetectionService.IsJumboCactpotPayoutAvailable(now),
                        activeConfig.JumboCactpotUnclaimedTickets,
                        activeConfig.JumboCactpotPayoutAvailableAt,
                        purchaseDue);

                    if (!cactpotService.IsActive && !cactpotService.IsComplete && !cactpotService.IsFailed)
                    {
                        if (route.Route == JumboCactpotRoute.Wait)
                        {
                            log.Information("[Engine] Jumbo Cactpot routing decision is wait: tickets={Tickets}, payoutAt={PayoutAt:u}, purchaseDue={PurchaseDue}",
                                activeConfig.JumboCactpotUnclaimedTickets?.ToString() ?? "unknown",
                                activeConfig.JumboCactpotPayoutAvailableAt,
                                purchaseDue);
                            AdvanceToNextTask(EngineState.RunningJumboCactpot);
                            break;
                        }

                        if (ShouldHoldNonDutyTaskStart("Jumbo Cactpot"))
                            return;

                        activeJumboCactpotRoute = route;
                        log.Information("[Engine] Jumbo Cactpot routing decision: now={Now:u}, dc={DataCenter}, route={Route}, expectedClaims={ExpectedClaims}, purchaseDue={PurchaseDue}",
                            now,
                            ResetDetectionService.GetCurrentCharacterJumboDataCenterName(),
                            route.Route,
                            route.ExpectedClaims?.ToString() ?? "discovery",
                            route.PurchaseDue);

                        // Clean slate before starting Jumbo Cactpot
                        log.Information("[Engine] Clean slate: clearing open UI before Jumbo Cactpot");
                        ResetInteractionState();
                        MarkCurrentTaskWorkStarted();

                        if (route.UsesCashier)
                        {
                            log.Information("[Engine] Starting Jumbo Cactpot cashier route {Route}", route.Route);
                            cactpotService.StartJumboCactpotCheck(route);
                        }
                        else
                        {
                            log.Information("[Engine] Starting Jumbo Cactpot ticket purchase");
                            cactpotService.StartJumboCactpot();
                        }
                        return;
                    }

                    taskBindings[PostProcessTaskOrder.MiniCactpot].Tick();

                    if (cactpotService.IsComplete)
                    {
                        var completedAt = DateTime.UtcNow;
                        PersistCurrentCharacterConfig(config =>
                        {
                            switch (cactpotService.JumboCompletionKind)
                            {
                                case JumboCactpotCompletionKind.PurchaseBatchEstablished:
                                    config.JumboCactpotLastCompleted = completedAt;
                                    config.JumboCactpotNextReset = ResetDetectionService.GetNextJumboCactpotPayoutAvailability(completedAt);
                                    config.JumboCactpotCompletedThisWeek = false;
                                    config.JumboCactpotUnclaimedTickets = 3;
                                    config.JumboCactpotPayoutAvailableAt = config.JumboCactpotNextReset;
                                    break;
                                case JumboCactpotCompletionKind.ScheduledPayoutComplete:
                                    config.JumboCactpotLastCompleted = completedAt;
                                    config.JumboCactpotNextReset = ResetDetectionService.GetNextWeeklyReset(completedAt);
                                    config.JumboCactpotCompletedThisWeek = true;
                                    config.JumboCactpotUnclaimedTickets = 0;
                                    config.JumboCactpotPayoutAvailableAt = DateTime.MinValue;
                                    break;
                                case JumboCactpotCompletionKind.PreservedExistingCompletion:
                                    config.JumboCactpotUnclaimedTickets = 0;
                                    config.JumboCactpotPayoutAvailableAt = DateTime.MinValue;
                                    break;
                                default:
                                    throw new InvalidOperationException("Jumbo Cactpot completed without a verified completion kind.");
                            }
                        }, "Jumbo Cactpot completion");
                        cactpotService.Reset();
                        activeJumboCactpotRoute = null;
                        AdvanceToNextTask(EngineState.RunningJumboCactpot);
                    }
                    else if (cactpotService.IsFailed)
                    {
                        log.Warning("[Engine] Jumbo Cactpot failed - continuing");
                        runHadFailure = true;
                        MarkJumboCactpotFailed(route.Route);
                        cactpotService.Reset();
                        activeJumboCactpotRoute = null;
                        AdvanceToNextTask(EngineState.RunningJumboCactpot);
                    }
                }
                else
                {
                    AdvanceToNextTask(EngineState.RunningJumboCactpot);
                }
                break;

            case EngineState.RunningFashionReport:
                activeConfig = GetLiveActiveConfig();
                if (activeConfig!.EnableFashionReport &&
                    ResetDetectionService.IsFashionReportAvailable(DateTime.UtcNow) &&
                    ResetDetectionService.TaskNeedsRun(activeConfig.FashionReportLastCompleted, activeConfig.FashionReportNextReset))
                {
                    if (!fashionReportService.IsActive && !fashionReportService.IsComplete && !fashionReportService.IsFailed)
                    {
                        // Clean slate before starting Fashion Report
                        log.Information("[Engine] Clean slate: clearing open UI before Fashion Report");
                        ResetInteractionState();

                        log.Information("[Engine] Starting Fashion Report (Friday 09:00 UTC through weekly reset)");
                        MarkCurrentTaskWorkStarted();
                        fashionReportService.Start();
                        return;
                    }

                    taskBindings[PostProcessTaskOrder.FashionReport].Tick();

                    if (fashionReportService.IsComplete)
                    {
                        var completedAt = DateTime.UtcNow;
                        PersistCurrentCharacterConfig(config =>
                        {
                            config.FashionReportLastCompleted = completedAt;
                            config.FashionReportNextReset = ResetDetectionService.GetNextWeeklyReset(completedAt);
                            config.FashionReportCompletedThisWeek = true;
                        }, "Fashion Report completion");
                        fashionReportService.Reset();
                        AdvanceToNextTask(EngineState.RunningFashionReport);
                    }
                    else if (fashionReportService.IsFailed)
                    {
                        log.Warning("[Engine] Fashion Report failed - continuing");
                        runHadFailure = true;
                        MarkWeeklyTaskFailed(
                            taskName: "Fashion Report",
                            clearLegacyFlag: config => config.FashionReportCompletedThisWeek = false);
                        fashionReportService.Reset();
                        AdvanceToNextTask(EngineState.RunningFashionReport);
                    }
                }
                else
                {
                    AdvanceToNextTask(EngineState.RunningFashionReport);
                }
                break;

            case EngineState.RunningChocoboRacing:
                activeConfig = GetLiveActiveConfig();
                if (activeConfig!.EnableChocoboRacing &&
                    ResetDetectionService.TaskNeedsRun(activeConfig.ChocoboRacingLastCompleted, activeConfig.ChocoboRacingNextReset))
                {
                    if (!chocoboRaceService.IsActive && !chocoboRaceService.IsComplete && !chocoboRaceService.IsFailed)
                    {
                        // Clean slate before starting Chocobo Racing
                        log.Information("[Engine] Clean slate: clearing open UI before Chocobo Racing");
                        ResetInteractionState();

                        MarkCurrentTaskWorkStarted();
                        chocoboRaceService.Start();
                        return;
                    }

                    taskBindings[PostProcessTaskOrder.ChocoboRacing].Tick();

                    if (chocoboRaceService.IsComplete)
                    {
                        var completedAt = DateTime.UtcNow;
                        PersistCurrentCharacterConfig(config =>
                        {
                            config.ChocoboRacingLastCompleted = completedAt;
                            config.ChocoboRacingNextReset = ResetDetectionService.GetNextDailyReset(completedAt);
                            config.ChocoboRacingCompletedToday = true;
                        }, "Chocobo Racing completion");
                        chocoboRaceService.Reset();
                        AdvanceToNextTask(EngineState.RunningChocoboRacing);
                    }
                    else if (chocoboRaceService.IsFailed)
                    {
                        log.Warning("[Engine] Chocobo Racing failed - continuing");
                        runHadFailure = true;
                        MarkDailyTaskFailed(
                            taskName: "Chocobo Racing",
                            clearLegacyFlag: config => config.ChocoboRacingCompletedToday = false);
                        chocoboRaceService.Reset();
                        AdvanceToNextTask(EngineState.RunningChocoboRacing);
                    }
                }
                else
                {
                    AdvanceToNextTask(EngineState.RunningChocoboRacing);
                }
                break;

            case EngineState.RunningLootGoblinMapGather:
                activeConfig = GetLiveActiveConfig();
                if (activeConfig!.EnableLootGoblinMapGather &&
                    ResetDetectionService.TaskNeedsRun(activeConfig.LootGoblinMapGatherLastCompleted, activeConfig.LootGoblinMapGatherNextReset))
                {
                    if (!lootGoblinMapGatherService.IsActive && !lootGoblinMapGatherService.IsComplete && !lootGoblinMapGatherService.IsFailed)
                    {
                        log.Information("[Engine] Starting LootGoblin Map Gather");
                        ResetInteractionState();
                        MarkCurrentTaskWorkStarted();
                        lootGoblinMapGatherService.Start(activeConfig);
                        return;
                    }

                    taskBindings[PostProcessTaskOrder.LootGoblinMapGather].Tick();

                    if (lootGoblinMapGatherService.IsComplete)
                    {
                        var completedAt = DateTime.UtcNow;
                        PersistCurrentCharacterConfig(config =>
                        {
                            config.LootGoblinMapGatherLastCompleted = completedAt;
                            config.LootGoblinMapGatherNextReset = ResetDetectionService.GetNextDailyReset(completedAt);
                        }, "LootGoblin Map Gather completion");
                        lootGoblinMapGatherService.Reset();
                        AdvanceToNextTask(EngineState.RunningLootGoblinMapGather);
                    }
                    else if (lootGoblinMapGatherService.IsFailed)
                    {
                        log.Warning($"[Engine] LootGoblin Map Gather failed - will retry later: {lootGoblinMapGatherService.StatusText}");
                        runHadFailure = true;
                        lootGoblinMapGatherService.Reset();
                        AdvanceToNextTask(EngineState.RunningLootGoblinMapGather);
                    }
                }
                else
                {
                    AdvanceToNextTask(EngineState.RunningLootGoblinMapGather);
                }
                break;

            case EngineState.RunningNagYourMom:
                activeConfig = GetLiveActiveConfig();
                RollNagYourMomLocalDay(activeConfig!);
                var nagRoutePlan = new NagYourMomRoutePlan(MomRunRoutes.CasualCc, "Casual CC", 0, false);
                if (!nagYourMomRequestIssued && !TryGetNextNagYourMomRoute(activeConfig!, out nagRoutePlan, out var nagSkipReason))
                {
                    NagYourMomStatusText = nagSkipReason;
                    AdvanceToNextTask(EngineState.RunningNagYourMom);
                    break;
                }

                if (!nagYourMomRequestIssued)
                {
                    if (nagRoutePlan.Route == MomRunRoutes.RivalWings)
                    {
                        var gate = momIPCClient.GetRivalWingsAchievementGate();
                        if (gate.DisableRouteRecommended || gate.BothComplete)
                        {
                            DisableNagYourMomRoute(MomRunRoutes.RivalWings, string.IsNullOrWhiteSpace(gate.DisableRouteReason) ? gate.Summary : gate.DisableRouteReason);
                            nagYourMomRouteCursor++;
                            break;
                        }
                    }

                    var momReadiness = momIPCClient.GetReadiness(useCache: false);
                    if (!momReadiness.IpcRegistered)
                    {
                        NagYourMomStatusText = momReadiness.Summary;
                        log.Warning($"[Engine] nag your mom unavailable - mom IPC missing: {momReadiness.Summary}");
                        AdvanceToNextTask(EngineState.RunningNagYourMom);
                        break;
                    }

                    var stopAtSeriesRank25 = nagRoutePlan.StopAtSeriesRank25;
                    var startResult = momIPCClient.StartRun(nagRoutePlan.RemainingRuns, activeConfig!.NagYourMomJob, stopAtSeriesRank25, nagRoutePlan.Route);
                    NagYourMomStatusText = startResult.Summary;

                    if (ApplyMomDisableRouteRecommendation(startResult))
                    {
                        nagYourMomRouteCursor++;
                        break;
                    }

                    if (startResult.Status is not (MomRunStatus.Queued or MomRunStatus.Running or MomRunStatus.Completed))
                    {
                        var rejectionReadiness = momIPCClient.GetReadiness(useCache: false);
                        log.Warning(
                            $"[Engine] nag your mom start rejected: status={startResult.Status}, summary={startResult.Summary}, route={startResult.Route}, pluginEnabled={rejectionReadiness.PluginEnabled}, ipcReady={rejectionReadiness.IpcReady}, canStart={rejectionReadiness.CanStart}, blockReason={rejectionReadiness.BlockReason}, startupSummary={rejectionReadiness.StartupSummary}");
                        nagYourMomRouteCursor++;
                        break;
                    }

                    TrackAcceptedNagYourMomRequest(startResult, nagRoutePlan);
                    MarkCurrentTaskWorkStarted();
                    log.Information($"[Engine] nag your mom accepted: route={startResult.Route}, job={activeConfig!.NagYourMomJob}, requestedRuns={nagYourMomRequestedRuns}, stopAtSeriesRank25={stopAtSeriesRank25}, status={startResult.Status}");

                    if (startResult.Status == MomRunStatus.Completed)
                    {
                        CreditNagYourMomTerminalResult(startResult);
                        ClearNagYourMomTracking();
                        nagYourMomRouteCursor++;
                        log.Information("[Engine] nag your mom completed immediately");
                        break;
                    }

                    nagYourMomRequestIssued = true;
                    return;
                }

                if (!momIPCClient.TryGetStatus(out var currentMomStatus))
                {
                    NagYourMomStatusText = currentMomStatus.Summary;
                    if (ShouldWaitForLostNagYourMomStatus(currentMomStatus.Summary))
                        return;

                    var creditedAfterLostStatus = CreditNagYourMomRunCount(nagYourMomActiveRoute, nagYourMomLastCompletedRuns, currentMomStatus.Summary);
                    runHadFailure = true;
                    log.Warning($"[Engine] nag your mom status lost after active request; advancing as failed after grace. reason={currentMomStatus.FailureReason}, creditedRuns={creditedAfterLostStatus}, requestedRuns={nagYourMomRequestedRuns}");
                    ClearNagYourMomTracking();
                    nagYourMomRouteCursor++;
                    break;
                }

                NagYourMomStatusText = currentMomStatus.Summary;
                ApplyMomDisableRouteRecommendation(currentMomStatus);
                nagYourMomLostStatusSince = DateTime.MinValue;
                nagYourMomLastCompletedRuns = Math.Max(nagYourMomLastCompletedRuns, currentMomStatus.CompletedRunCount);
                if (currentMomStatus.Status is MomRunStatus.Queued or MomRunStatus.Running)
                    return;

                if (currentMomStatus.Status == MomRunStatus.Idle)
                {
                    if (ShouldWaitForLostNagYourMomStatus("mom returned Idle during an active Nag Mom request."))
                        return;

                    var creditedAfterIdle = CreditNagYourMomRunCount(nagYourMomActiveRoute, nagYourMomLastCompletedRuns, currentMomStatus.Summary);
                    runHadFailure = true;
                    log.Warning($"[Engine] nag your mom returned Idle during active request; advancing as failed after grace. creditedRuns={creditedAfterIdle}, requestedRuns={nagYourMomRequestedRuns}");
                    ClearNagYourMomTracking();
                    nagYourMomRouteCursor++;
                    break;
                }

                if (currentMomStatus.Status == MomRunStatus.Completed)
                {
                    var creditedRuns = CreditNagYourMomTerminalResult(currentMomStatus);
                    ClearNagYourMomTracking();
                    nagYourMomRouteCursor++;
                    log.Information($"[Engine] nag your mom completed successfully: route={currentMomStatus.Route}, creditedRuns={creditedRuns}, completedRuns={currentMomStatus.CompletedRunCount}, requestedRuns={currentMomStatus.RequestedRunCount}");
                }
                else
                {
                    var creditedRuns = CreditNagYourMomTerminalResult(currentMomStatus);
                    runHadFailure = true;
                    ClearNagYourMomTracking();
                    nagYourMomRouteCursor++;
                    log.Warning($"[Engine] nag your mom ended with status {currentMomStatus.Status}: {currentMomStatus.Summary}; creditedRuns={creditedRuns}, completedRuns={currentMomStatus.CompletedRunCount}, requestedRuns={currentMomStatus.RequestedRunCount}");
                }

                break;

            case EngineState.RunningNagYourDad:
                activeConfig = GetLiveActiveConfig();
                if (!ShouldRunNagYourDadNow(activeConfig!, out var dadSkipReason))
                {
                    NagYourDadStatusText = dadSkipReason;
                    AdvanceToNextTask(EngineState.RunningNagYourDad);
                    break;
                }

                if (activeDadExecution == null)
                {
                    if (!dadIPCClient.IsReady())
                    {
                        NagYourDadStatusText = "dad IPC is not ready.";
                        log.Warning("[Engine] dad IPC is not ready - deferring until the next AR opportunity");
                        AdvanceToNextTask(EngineState.RunningNagYourDad);
                        break;
                    }

                    activeDadExecution = dadIPCClient.StartSelection(
                        activeConfig!.NagYourDadSelectionKind,
                        activeConfig.NagYourDadSelectionId,
                        activeConfig.NagYourDadSelectionDisplayName);
                    NagYourDadStatusText = activeDadExecution.StatusText;

                    if (activeDadExecution.IsTerminal)
                    {
                        if (activeDadExecution.Success)
                        {
                            MarkCurrentTaskWorkStarted();
                            log.Information($"[Engine] nag your dad completed immediately: {activeDadExecution.StatusText}");
                        }
                        else
                        {
                            runHadFailure = true;
                            log.Warning($"[Engine] nag your dad start failed: {activeDadExecution.StatusText}");
                        }
                        activeDadExecution = null;
                        AdvanceToNextTask(EngineState.RunningNagYourDad);
                        break;
                    }

                    MarkCurrentTaskWorkStarted();
                    return;
                }

                activeDadExecution = dadIPCClient.PollSelection(activeDadExecution);
                NagYourDadStatusText = activeDadExecution.StatusText;
                if (!activeDadExecution.IsTerminal)
                    return;

                if (activeDadExecution.Success)
                    log.Information($"[Engine] nag your dad completed successfully: {activeDadExecution.StatusText}");
                else
                {
                    runHadFailure = true;
                    log.Warning($"[Engine] nag your dad ended with status {activeDadExecution.StatusText}");
                }

                activeDadExecution = null;
                AdvanceToNextTask(EngineState.RunningNagYourDad);
                break;

            case EngineState.SettlingTask:
                if (!TickHandoffSettling("task handoff"))
                    break;

                ResetHandoffTracking();
                DispatchNextQueuedTask();
                break;

            case EngineState.SettlingFinalHandoff:
                if (!TickHandoffSettling("final handoff", finalHandoff: true))
                    break;

                ResetHandoffTracking();
                SetState(EngineState.SignalingARDone);
                break;

            case EngineState.SignalingARDone:
                var finalBlocker = GetFinalHandoffBlocker();
                if (finalBlocker != null)
                {
                    log.Information($"[Engine] Final handoff blocker appeared after quiet period: {finalBlocker}");
                    BeginFinalHandoffSettling();
                    break;
                }

                if (arService.IsProcessing)
                {
                    if (!arService.FinishPostProcess())
                    {
                        StatusText = "Waiting to signal AutoRetainer";
                        break;
                    }

                    log.Information("[Engine] Signaled AR to continue");
                }
                if (activePhaseFilter == RunTaskPhaseFilter.BeforeAR)
                {
                    if (!autoRetainerIPC.ReleaseSuppressionIfOwned())
                    {
                        StatusText = "Waiting to release AutoRetainer suppression";
                        break;
                    }
                }

                yesAlreadyIPC.Unpause();
                CompleteRun();
                break;
        }
    }

    private void TickSimpleRegisteredTask(
        EngineState expectedState,
        bool enabled,
        Func<bool> isActive,
        Func<bool> isComplete,
        Func<bool> isFailed,
        Action start)
    {
        var taskId = TaskIdByState[expectedState];
        var binding = taskBindings[taskId];
        if (!enabled)
        {
            binding.Cleanup();
            AdvanceToNextTask(expectedState);
            return;
        }

        if (!isActive() && !isComplete() && !isFailed())
        {
            log.Information($"[Engine] Starting {AutomationCatalog.Get(taskId).Label}");
            MarkCurrentTaskWorkStarted();
            start();
            return;
        }

        binding.Tick();
        if (!isComplete() && !isFailed())
            return;

        if (isFailed())
        {
            runHadFailure = true;
            log.Warning($"[Engine] {AutomationCatalog.Get(taskId).Label} failed: {binding.Status()}");
        }
        else
        {
            log.Information($"[Engine] {AutomationCatalog.Get(taskId).Label} completed: {binding.Status()}");
        }

        binding.Cleanup();
        AdvanceToNextTask(expectedState);
    }

    private void MarkWeeklyTaskFailed(string taskName, Action<CharacterConfig> clearLegacyFlag)
    {
        PersistCurrentCharacterConfig(config =>
        {
            clearLegacyFlag(config);
        }, $"{taskName} failure");
        log.Warning($"[Engine] {taskName} failed and remains unstamped for a future retry.");
    }

    private void MarkDailyTaskFailed(string taskName, Action<CharacterConfig> clearLegacyFlag)
    {
        PersistCurrentCharacterConfig(config =>
        {
            clearLegacyFlag(config);
        }, $"{taskName} failure");
        log.Warning($"[Engine] {taskName} failed and remains unstamped for a future retry.");
    }

    private void MarkJumboCactpotFailed(JumboCactpotRoute route)
    {
        PersistCurrentCharacterConfig(config =>
        {
            config.JumboCactpotCompletedThisWeek = false;
        }, "Jumbo Cactpot failure");
        log.Warning("[Engine] Jumbo Cactpot route {Route} failed and remains unstamped for a future retry.", route);
    }

    private void PersistRefillFromListingsCompletion(RefillFromListingsFrequency frequency)
    {
        if (frequency == RefillFromListingsFrequency.EveryAR)
            return;

        var completedAt = DateTime.UtcNow;
        PersistCurrentCharacterConfig(config =>
        {
            config.RefillFromListingsLastCompleted = completedAt;
            config.RefillFromListingsNextReset = GetNextRefillFromListingsReset(frequency, completedAt);
        }, "Retainer Listing Refill completion");
    }

    private void PersistFCBuffCompletion(FCBuffFrequency frequency, bool usedRankOneToSevenShortcut)
    {
        var completedAt = DateTime.UtcNow;
        var completion = FCBuffRecoveryPolicy.GetCompletionTimestamps(
            frequency,
            usedRankOneToSevenShortcut,
            completedAt);
        if (completion == null)
            return;

        PersistCurrentCharacterConfig(config =>
        {
            config.FCBuffLastCompleted = completion.Value.LastCompleted;
            config.FCBuffNextReset = completion.Value.NextReset;
        }, "FC Buff Refill completion");
    }

    private bool ShouldRunFCBuff(CharacterConfig config)
        => FCBuffRecoveryPolicy.ShouldRun(
            config.FCBuffFrequency,
            fcBuffService.GetCurrentFreeCompanyRank(),
            config.FCBuffLastCompleted,
            config.FCBuffNextReset,
            DateTime.UtcNow);

    private static bool ShouldRunRefillFromListings(CharacterConfig config)
    {
        if (!config.EnableRefillFromListings)
            return false;

        var now = DateTime.UtcNow;
        return config.RefillFromListingsFrequency switch
        {
            RefillFromListingsFrequency.EveryAR => true,
            RefillFromListingsFrequency.Daily => ResetDetectionService.TaskNeedsRun(config.RefillFromListingsLastCompleted, config.RefillFromListingsNextReset),
            RefillFromListingsFrequency.Weekly => ResetDetectionService.TaskNeedsRun(config.RefillFromListingsLastCompleted, config.RefillFromListingsNextReset),
            RefillFromListingsFrequency.Monthly => ShouldRunMonthlyRefill(config, now),
            _ => ResetDetectionService.TaskNeedsRun(config.RefillFromListingsLastCompleted, config.RefillFromListingsNextReset),
        };
    }

    private static bool ShouldRunMonthlyRefill(CharacterConfig config, DateTime now)
    {
        if (config.RefillFromListingsLastCompleted == DateTime.MinValue)
            return true;

        var lastCompleted = config.RefillFromListingsLastCompleted.ToUniversalTime();
        if (lastCompleted.Year != now.Year || lastCompleted.Month != now.Month)
            return true;

        return config.RefillFromListingsNextReset != DateTime.MinValue && now >= config.RefillFromListingsNextReset;
    }

    private static DateTime GetNextRefillFromListingsReset(RefillFromListingsFrequency frequency, DateTime now)
    {
        return frequency switch
        {
            RefillFromListingsFrequency.Daily => ResetDetectionService.GetNextDailyReset(now),
            RefillFromListingsFrequency.Weekly => ResetDetectionService.GetNextWeeklyReset(now),
            RefillFromListingsFrequency.Monthly => GetFirstDayOfNextUtcMonth(now),
            _ => DateTime.MinValue,
        };
    }

    private static DateTime GetFirstDayOfNextUtcMonth(DateTime now)
    {
        var utc = now.ToUniversalTime();
        var nextMonth = utc.Month == 12
            ? new DateTime(utc.Year + 1, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            : new DateTime(utc.Year, utc.Month + 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return nextMonth;
    }

    private bool ShouldHoldNonDutyTaskStart(string taskName)
    {
        var blockReason = GetNonDutyTaskStartBlockReason();
        if (blockReason == null)
        {
            taskStartHoldLogged = false;
            taskStartHoldState = EngineState.Idle;
            return false;
        }

        StatusText = $"Waiting for duty/queue before {taskName}";
        if (!taskStartHoldLogged || taskStartHoldState != state)
        {
            log.Warning($"[Engine] Holding {taskName} start until duty/queue clears: {blockReason}");
            taskStartHoldLogged = true;
            taskStartHoldState = state;
        }

        return true;
    }

    private static string? GetNonDutyTaskStartBlockReason()
    {
        if (Plugin.Condition[ConditionFlag.BoundByDuty])
            return "BoundByDuty condition is active";
        if (Plugin.Condition[ConditionFlag.InDutyQueue])
            return "InDutyQueue condition is active";
        if (Plugin.Condition[ConditionFlag.WaitingForDuty])
            return "WaitingForDuty condition is active";
        if (Plugin.Condition[ConditionFlag.WaitingForDutyFinder])
            return "WaitingForDutyFinder condition is active";
        if (IsAddonVisible("ContentsFinderConfirm"))
            return "ContentsFinderConfirm is visible";
        if (IsAddonVisible("ContentsFinder"))
            return "ContentsFinder is visible";
        if (!IsPlayerAvailable())
            return "player is not available";

        return null;
    }

    private void AdvanceToNextTask(EngineState currentTask)
    {
        if (!currentTaskOwnedWorkStarted)
        {
            log.Information($"[Engine] {currentTask} did not start owned work; advancing without settling");
            DispatchNextQueuedTask();
            return;
        }

        currentTaskOwnedWorkStarted = false;
        SetState(EngineState.SettlingTask);
    }

    private void BeginFinalHandoffSettling()
    {
        var externalBlockerPresent = arService.IsProcessing && GetExternalHandoffBlocker() != null;
        if (LifecyclePolicy.RequiresFinalSettling(
                runOwnedWorkStarted,
                arService.IsProcessing,
                externalBlockerPresent))
            SetState(EngineState.SettlingFinalHandoff);
        else
            SetState(EngineState.SignalingARDone);
    }

    private void BeginImmediateFinalization(RunOutcome outcome, string summary)
    {
        pendingRunOutcome = outcome;
        pendingRunSummary = summary;
        BeginFinalHandoffSettling();
    }

    private void CancelForSettling(string status)
    {
        momIPCClient.CancelActiveRun();
        if (activeDadExecution != null)
            dadIPCClient.CancelSelection(activeDadExecution);
        lootGoblinMapGatherService.Cancel();
        CancelTaskServices();
        vNavmeshIPC.Stop();
        TryCloseOwnedUiBestEffort();
        NagYourMomStatusText = status;
        NagYourDadStatusText = status;
        pendingRunOutcome = RunOutcome.Cancelled;
        pendingRunSummary = status;
        BeginFinalHandoffSettling();
    }

    private void MarkCurrentTaskWorkStarted()
    {
        currentTaskOwnedWorkStarted = true;
        runOwnedWorkStarted = true;
        RecordWatchdogProgress("owned work started");
    }

    private void ResetTaskServices()
    {
        foreach (var reset in taskBindings.Values.Select(binding => binding.Reset).Distinct())
            reset();
        fcBuffInventoryService.Reset();
        workshopBellService.Reset();
    }

    private void CancelTaskServices()
    {
        foreach (var cancel in taskBindings.Values.Select(binding => binding.Cancel).Distinct())
            cancel();
        fcBuffInventoryService.Reset();
        workshopBellService.Reset();
    }

    private bool TickTaskWatchdog()
    {
        if (!TaskIdByState.ContainsKey(state))
        {
            ResetWatchdog();
            return false;
        }

        var now = DateTime.UtcNow;
        var pauseReason = GetTaskWatchdogPauseReason();
        if (pauseReason != null)
        {
            if (!string.Equals(watchdogPauseReason, pauseReason, StringComparison.Ordinal))
                log.Information($"[Engine][Watchdog] Suspended in {state}: {pauseReason}");
            watchdogPauseReason = pauseReason;
            watchdogLastProgressAt = now;
            watchdogLastSignature = BuildWatchdogSignature();
            return false;
        }

        if (!string.IsNullOrEmpty(watchdogPauseReason))
        {
            log.Information($"[Engine][Watchdog] Re-armed in {state} after {watchdogPauseReason}");
            watchdogPauseReason = string.Empty;
            watchdogLastProgressAt = now;
            watchdogLastSignature = BuildWatchdogSignature();
            return false;
        }

        var signature = BuildWatchdogSignature();
        if (!string.Equals(signature, watchdogLastSignature, StringComparison.Ordinal))
        {
            watchdogLastSignature = signature;
            watchdogLastProgressAt = now;
            return false;
        }

        if (watchdogLastProgressAt == DateTime.MinValue)
        {
            watchdogLastProgressAt = now;
            return false;
        }

        if (!TaskWatchdogPolicy.ShouldTimeout(now, watchdogLastProgressAt, paused: false))
            return false;

        var timedOutState = state;
        var taskId = TaskIdByState[timedOutState];
        var stalledFor = now - watchdogLastProgressAt;
        var summary = $"{taskId} stalled for {stalledFor.TotalMinutes:F1} minutes without observable progress";
        log.Error($"[Engine][Watchdog] {summary}; cleaning task and continuing queue");
        WriteIncident("task-watchdog-timeout", summary);
        CleanupFaultingWork(timedOutState, "watchdog timeout");
        runHadFailure = true;
        AdvanceToNextTask(timedOutState);
        return true;
    }

    private string? GetTaskWatchdogPauseReason()
    {
        if (!clientState.IsLoggedIn)
            return "client logged out";
        if (Plugin.ObjectTable.LocalPlayer == null)
            return "local player unavailable";
        if (Plugin.Condition[ConditionFlag.BoundByDuty] || Plugin.Condition[ConditionFlag.BoundByDuty56])
            return "duty-bound";
        if (Plugin.Condition[ConditionFlag.InDutyQueue]
            || Plugin.Condition[ConditionFlag.WaitingForDuty]
            || Plugin.Condition[ConditionFlag.WaitingForDutyFinder])
            return "duty queue/wait";
        if (Plugin.Condition[ConditionFlag.InCombat] || Plugin.Condition[ConditionFlag.Casting])
            return "combat/casting";
        if (Plugin.Condition[ConditionFlag.BetweenAreas] || Plugin.Condition[ConditionFlag.BetweenAreas51])
            return "zoning";
        if (Plugin.Condition[ConditionFlag.Occupied]
            || Plugin.Condition[ConditionFlag.OccupiedInQuestEvent]
            || Plugin.Condition[ConditionFlag.OccupiedInCutSceneEvent]
            || Plugin.Condition[ConditionFlag.WatchingCutscene])
            return "occupied/cutscene";
        if (!IsPlayerAvailable())
            return "player unavailable";
        return null;
    }

    private string BuildWatchdogSignature()
        => string.Join(
            '|',
            state,
            runQueueIndex,
            TaskIdByState.TryGetValue(state, out var id) && taskBindings.TryGetValue(id, out var binding)
                ? binding.WatchdogProgress()
                : StatusText,
            workshopBellService.StatusText);

    private void RecordWatchdogProgress(string reason)
    {
        watchdogLastProgressAt = DateTime.UtcNow;
        watchdogLastSignature = BuildWatchdogSignature();
        watchdogPauseReason = string.Empty;
        log.Debug($"[Engine][Watchdog] Progress: {reason}");
    }

    private void ResetWatchdog()
    {
        watchdogLastProgressAt = DateTime.MinValue;
        watchdogLastSignature = string.Empty;
        watchdogPauseReason = string.Empty;
    }

    private void CleanupFaultingWork(EngineState faultingState, string reason)
    {
        if (TaskIdByState.TryGetValue(faultingState, out var taskId) &&
            taskBindings.TryGetValue(taskId, out var binding))
        {
            binding.Cancel();
        }
        else
        {
            CancelTaskServices();
        }

        vNavmeshIPC.Stop();
        TryCloseOwnedUiBestEffort();
        log.Warning($"[Engine] Cleaned faulting work in {faultingState} after {reason}");
    }

    private void WriteIncident(string type, string summary)
    {
        try
        {
            var taskId = TaskIdByState.TryGetValue(state, out var id) ? id : string.Empty;
            incidentWriter.Write(new VermaxionIncident(
                DateTime.UtcNow,
                type,
                state.ToString(),
                taskId,
                summary,
                $"queueIndex={runQueueIndex}; queue=[{string.Join(',', runQueue)}]; ownedTask={currentTaskOwnedWorkStarted}; ownedRun={runOwnedWorkStarted}; blocker={GetHandoffBlocker() ?? "none"}; signature={BuildWatchdogSignature()}"));
        }
        catch (Exception ex)
        {
            log.Error($"[Engine] Failed to write Vermaxion incident: {ex.Message}");
        }
    }

    private bool TickHandoffSettling(string phase, bool finalHandoff = false)
    {
        StopMovementForHandoff();
        yesAlreadyIPC.Pause();

        if (finalHandoff && arService.IsProcessing && !fishingService.IsActive)
            TickOrphanOceanFishingResult();

        var blocker = finalHandoff ? GetFinalHandoffBlocker() : GetHandoffBlocker();
        var now = DateTime.UtcNow;
        if (blocker != null)
        {
            var blockerChanged = !string.Equals(handoffBlockerReason, blocker, StringComparison.Ordinal);
            handoffQuietSince = DateTime.MinValue;
            handoffBlockerReason = blocker;
            StatusText = $"Settling: {blocker}";

            if (blockerChanged)
            {
                handoffBlockerSince = now;
                handoffBlockerLastWarningAt = DateTime.MinValue;
                log.Information($"[Engine] Waiting for {phase}: {blocker}");
                handoffBlockerLastLoggedAt = now;
            }
            else if (handoffBlockerLastLoggedAt == DateTime.MinValue ||
                     now - handoffBlockerLastLoggedAt >= HandoffBlockerLogThrottle)
            {
                log.Information($"[Engine] Waiting for {phase}: {blocker}");
                handoffBlockerLastLoggedAt = now;
            }

            if (handoffBlockerSince != DateTime.MinValue &&
                now - handoffBlockerSince >= HandoffBlockerWarningThrottle &&
                (handoffBlockerLastWarningAt == DateTime.MinValue ||
                 now - handoffBlockerLastWarningAt >= HandoffBlockerWarningThrottle))
            {
                log.Warning($"[Engine] Handoff remains blocked for {(now - handoffBlockerSince).TotalSeconds:F0}s: {blocker}. Ownership retained; Full Stop is the only forced release.");
                handoffBlockerLastWarningAt = now;
            }

            return false;
        }

        if (handoffQuietSince == DateTime.MinValue)
        {
            handoffQuietSince = now;
            handoffBlockerReason = string.Empty;
            handoffBlockerLastLoggedAt = DateTime.MinValue;
            log.Information($"[Engine] {phase} is quiet; holding ownership for {HandoffQuietPeriod.TotalSeconds:F0}s");
        }

        var quietFor = now - handoffQuietSince;
        StatusText = $"Settling: quiet {quietFor.TotalSeconds:F1}/{HandoffQuietPeriod.TotalSeconds:F1}s";
        if (quietFor < HandoffQuietPeriod)
            return false;

        log.Information($"[Engine] {phase} quiet-period complete");
        return true;
    }

    private string? GetHandoffBlocker()
        => GetServiceOwnedHandoffBlocker() ?? GetExternalHandoffBlocker();

    private string? GetFinalHandoffBlocker()
    {
        if (runOwnedWorkStarted)
            return GetHandoffBlocker();

        return arService.IsProcessing ? GetExternalHandoffBlocker() : null;
    }

    private string? GetServiceOwnedHandoffBlocker()
    {
        if (fcBuffService.IsActive)
            return $"FC Buff service active ({fcBuffService.StatusText})";
        if (fcBuffInventoryService.IsActive)
            return "FC Buff Inventory service active";
        if (vendorStockService.IsActive)
            return "Vendor Stock service active";
        if (fishingService.IsActive)
            return $"Fishing service active ({fishingService.StatusText})";
        if (registerRegistrablesService.IsActive)
            return "Register Registrables service active";
        if (retainerListingRefillService.IsActive)
            return "Retainer Listing Refill service active";
        if (workshopBellService.IsActive)
            return "Workshop Bell service active";
        if (verminionService.IsActive)
            return $"Verminion service active ({verminionService.StatusText})";
        if (cactpotService.IsActive)
            return $"Cactpot service active ({cactpotService.StatusText})";
        if (fashionReportService.IsActive)
            return $"Fashion Report service active ({fashionReportService.State})";
        if (chocoboRaceService.IsActive)
            return $"Chocobo Racing service active ({chocoboRaceService.StatusText})";
        if (gearUpdaterService.IsActive)
            return $"Gear Updater active ({gearUpdaterService.StatusText})";
        if (highestCombatJobService.IsActive)
            return $"Highest Combat Job active ({highestCombatJobService.StatusText})";
        if (currentJobEquipmentService.IsActive)
            return $"Current Job Equipment active ({currentJobEquipmentService.StatusText})";
        if (alliedSocietyService.IsActive || alliedSocietyService.OwnsRotation)
            return $"Allied Society active ({alliedSocietyService.StatusText})";
        if (seasonalGearService.IsActive)
            return $"Seasonal Gear active ({seasonalGearService.StatusText})";
        if (afterArParkService.IsActive)
            return $"After-AR Park active ({afterArParkService.StatusText})";
        if (minionRouletteService.IsActive)
            return $"Minion Roulette active ({minionRouletteService.StatusText})";

        var visibleAddon = TaskOwnedAddonNames.FirstOrDefault(IsAddonVisible);
        if (visibleAddon != null)
            return $"{visibleAddon} addon is visible";

        return null;
    }

    private string? GetExternalHandoffBlocker()
    {
        var resultAddon = GameHelpers.GetIKDResultAddonSnapshot();
        return ExternalHandoffPolicy.GetBlocker(new ExternalHandoffSnapshot(
            clientState.TerritoryType,
            resultAddon.Visible,
            resultAddon.Ready,
            lifestreamIPC.IsBusy(),
            Plugin.Condition[ConditionFlag.BoundByDuty] || Plugin.Condition[ConditionFlag.BoundByDuty56],
            Plugin.Condition[ConditionFlag.InDutyQueue] ||
            Plugin.Condition[ConditionFlag.WaitingForDuty] ||
            Plugin.Condition[ConditionFlag.WaitingForDutyFinder],
            Plugin.Condition[ConditionFlag.BetweenAreas] || Plugin.Condition[ConditionFlag.BetweenAreas51],
            Plugin.Condition[ConditionFlag.Occupied] ||
            Plugin.Condition[ConditionFlag.OccupiedInQuestEvent] ||
            Plugin.Condition[ConditionFlag.OccupiedInCutSceneEvent] ||
            Plugin.Condition[ConditionFlag.WatchingCutscene],
            Plugin.Condition[ConditionFlag.InCombat] || Plugin.Condition[ConditionFlag.Casting],
            clientState.IsLoggedIn,
            IsPlayerAvailable()));
    }

    private void TickOrphanOceanFishingResult()
    {
        var now = DateTime.UtcNow;
        if (orphanResultSettlingSince == DateTime.MinValue)
            orphanResultSettlingSince = now;

        var elapsed = now - orphanResultSettlingSince;
        var sinceLastPoll = orphanResultLastPollAt == DateTime.MinValue
            ? TimeSpan.MaxValue
            : now - orphanResultLastPollAt;
        if (elapsed >= OceanFishingResultClosePolicy.InitialDelay &&
            sinceLastPoll >= OceanFishingResultClosePolicy.PollInterval)
        {
            orphanResultAddonSnapshot = GameHelpers.GetIKDResultAddonSnapshot();
            orphanResultLastPollAt = now;
        }

        if (elapsed >= OceanFishingResultClosePolicy.Timeout &&
            orphanResultAddonSnapshot.Visible &&
            !orphanResultTimeoutLogged)
        {
            orphanResultTimeoutLogged = true;
            log.Warning(
                $"[Engine][IKDResult] Orphan result remained visible after {OceanFishingResultClosePolicy.Timeout.TotalSeconds:F0}s; " +
                $"continuing close attempts and retaining AR ownership. {orphanResultAddonSnapshot.Detail}");
        }

        var decision = OceanFishingResultClosePolicy.Decide(new OceanFishingResultCloseSnapshot(
            elapsed,
            sinceLastPoll,
            orphanResultLastCallbackAt == DateTime.MinValue
                ? TimeSpan.MaxValue
                : now - orphanResultLastCallbackAt,
            orphanResultAddonSnapshot.Found,
            orphanResultAddonSnapshot.Visible,
            orphanResultAddonSnapshot.Ready,
            orphanResultCallbackDispatched,
            ResultClosed: false,
            PostVoyageTransitionObserved: false,
            PostVoyageSettled: false));

        if (decision.Action == OceanFishingResultCloseAction.FireCallback)
        {
            if (GameHelpers.TryCloseReadyIKDResult(out var firedSnapshot, out var closeError))
            {
                orphanResultAddonSnapshot = firedSnapshot;
                orphanResultCallbackDispatched = true;
                orphanResultLastCallbackAt = now;
                log.Information($"[Engine][IKDResult] Fired close callback: IKDResult true 0; {firedSnapshot.Detail}");
            }
            else
            {
                orphanResultAddonSnapshot = firedSnapshot;
                if (!string.IsNullOrWhiteSpace(closeError))
                    log.Warning($"[Engine][IKDResult] Ready close callback failed; retaining AR ownership: {closeError}");
            }
        }
        else if (decision.ResultClosed && orphanResultCallbackDispatched && !orphanResultClosureLogged)
        {
            orphanResultClosureLogged = true;
            log.Information($"[Engine][IKDResult] Orphan result window closed; {orphanResultAddonSnapshot.Detail}");
        }
    }

    private void TryCloseOwnedUiBestEffort(UiCloseFallbackMode fallbackMode = UiCloseFallbackMode.Always)
    {
        var knownAddonWasVisible = TaskOwnedAddonNames.Any(IsAddonVisible);

        foreach (var addonName in TaskOwnedAddonNames)
            TryCloseAddonByCallback(addonName);

        if (UiCloseFallbackPolicy.ShouldPressFallbackEscape(fallbackMode, knownAddonWasVisible))
            ResetInteractionState();
    }

    private void ResetHandoffTracking()
    {
        handoffQuietSince = DateTime.MinValue;
        handoffBlockerLastLoggedAt = DateTime.MinValue;
        handoffMovementStopLastIssuedAt = DateTime.MinValue;
        handoffBlockerReason = string.Empty;
        handoffBlockerSince = DateTime.MinValue;
        handoffBlockerLastWarningAt = DateTime.MinValue;
    }

    private void StopMovementForHandoff()
    {
        var now = DateTime.UtcNow;
        if (handoffMovementStopLastIssuedAt != DateTime.MinValue &&
            now - handoffMovementStopLastIssuedAt < TimeSpan.FromSeconds(1))
        {
            return;
        }

        handoffMovementStopLastIssuedAt = now;
        vNavmeshIPC.Stop();
    }

    private void BuildRunQueue(CharacterConfig config)
    {
        runQueue.Clear();
        runQueueIndex = -1;
        LastPlan = BuildAutomationPlan(config);
        foreach (var entry in LastPlan)
            log.Information($"[Engine][Plan] {entry}");

        var eligibility = taskBindings.ToDictionary(
            pair => pair.Key,
            pair => GetActiveRunEligibility(pair.Key, config),
            StringComparer.Ordinal);
        var scopedOrder = AutomationRunScopePolicy.FilterOrderedIds(
            GetNormalizedTaskOrder(),
            activeRunScope);
        var runnableIds = AutomationDispatchPlanner.BuildRunnableQueue(scopedOrder, eligibility);
        runQueue.AddRange(runnableIds.Select(id => TaskStateById[id]));
    }

    private IReadOnlyList<AutomationPlanEntry> BuildAutomationPlan(CharacterConfig config)
    {
        var entries = new List<AutomationPlanEntry>(AutomationCatalog.Features.Count);
        foreach (var feature in AutomationCatalog.Features)
        {
            var phase = feature.DefaultPhase.ToString();
            TaskEligibility eligibility;
            if (feature.Owner == AutomationOwner.EngineTask)
            {
                var configuredPhase = configuration.PostProcessTaskPlacement.TryGetValue(feature.Id, out var placement)
                    ? placement
                    : feature.DefaultPhase;
                phase = configuredPhase.ToString();
                if (!AutomationRunScopePolicy.IncludesTask(activeRunScope, feature.Id))
                {
                    eligibility = TaskEligibility.Blocked("Excluded from this manual single-task run.");
                }
                else
                {
                    var phaseMatches = activePhaseFilter == RunTaskPhaseFilter.All ||
                                       activePhaseFilter == RunTaskPhaseFilter.BeforeAR && configuredPhase == PostProcessTaskPhase.BeforeAR ||
                                       activePhaseFilter == RunTaskPhaseFilter.AfterAR && configuredPhase == PostProcessTaskPhase.AfterAR;
                    eligibility = phaseMatches
                        ? GetActiveRunEligibility(feature.Id, config)
                        : TaskEligibility.Blocked($"Assigned to {configuredPhase}, outside this {activePhaseFilter} run.");
                }
            }
            else if (feature.Owner == AutomationOwner.RunHook)
            {
                eligibility = !activeRunScope.AllowRunHooks
                    ? TaskEligibility.Blocked("Suppressed for this manual single-task run.")
                    : !config.EnableMiscCmd
                    ? TaskEligibility.Disabled("Misc Commands is disabled for this character.")
                    : activePhaseFilter == RunTaskPhaseFilter.BeforeAR
                        ? TaskEligibility.Blocked("Misc Commands does not arm or run a Before-AR pass by itself.")
                        : TaskEligibility.Runnable("Runs once at the start of this applicable engine run.");
            }
            else if (feature.Owner == AutomationOwner.PreemptiveCoordinator)
            {
                eligibility = config.EnableFishing
                    ? TaskEligibility.Unsupported("Owned by FishingStartupCoordinator before engine dispatch; it is not reorderable.")
                    : TaskEligibility.Disabled("Fishing is disabled for this character.");
            }
            else if (feature.Owner == AutomationOwner.ConfigOnlyWip)
            {
                eligibility = TaskEligibility.Unsupported("Configuration-only WIP; no runtime dispatch is advertised.");
            }
            else
            {
                var property = typeof(CharacterConfig).GetProperty(feature.FlagProperty);
                var enabled = property?.GetValue(config) as bool? == true;
                eligibility = enabled
                    ? TaskEligibility.Unsupported("Child route option; dispatched only through nag your mom.")
                    : TaskEligibility.Disabled("Child route option is disabled.");
            }

            entries.Add(new AutomationPlanEntry(
                feature.Id,
                feature.Label,
                feature.OwnershipLabel,
                phase,
                eligibility.Status,
                eligibility.Reason));
        }

        return entries;
    }

    private string BuildNoRunnableWorkSummary()
    {
        var reasons = LastPlan
            .Where(entry => entry.Status is TaskEligibilityStatus.Blocked or TaskEligibilityStatus.Unsupported)
            .Select(entry => $"{entry.Label}: {entry.Reason}")
            .Take(4)
            .ToList();
        return reasons.Count == 0
            ? "All configured engine tasks are disabled or not due"
            : $"No runnable work. {string.Join(" | ", reasons)}";
    }

    private void DispatchNextQueuedTask()
    {
        currentTaskOwnedWorkStarted = false;
        while (++runQueueIndex < runQueue.Count)
        {
            var nextState = runQueue[runQueueIndex];
            var taskId = TaskIdByState[nextState];
            var liveConfig = GetLiveActiveConfig();
            var eligibility = GetActiveRunEligibility(taskId, liveConfig);
            if (!eligibility.IsRunnable)
            {
                log.Information($"[Engine] Skipping queued task after dispatch revalidation: {taskId} ({eligibility.Status}: {eligibility.Reason})");
                continue;
            }

            SetState(nextState);
            return;
        }

        BeginFinalHandoffSettling();
    }

    private void RevalidatePlannedQueue()
    {
        var liveConfig = GetLiveActiveConfig();
        runQueue.RemoveAll(taskState => !GetActiveRunEligibility(TaskIdByState[taskState], liveConfig).IsRunnable);
        runQueueIndex = -1;
    }

    private TaskEligibility GetActiveRunEligibility(string taskId, CharacterConfig config)
    {
        if (activeRunScope.BypassSelectedScheduling &&
            string.Equals(
                activeRunScope.SingleTaskId,
                PostProcessTaskOrder.RetainerEquipping,
                StringComparison.Ordinal) &&
            string.Equals(taskId, PostProcessTaskOrder.RetainerEquipping, StringComparison.Ordinal))
        {
            return EvaluateRetainerEquipping(config, ignoreSchedulingFlag: true);
        }

        return taskBindings[taskId].Eligibility(config);
    }

    private List<string> GetNormalizedTaskOrder()
    {
        if (PostProcessTaskOrder.Normalize(configuration))
            configuration.Save();

        if (activePhaseFilter == RunTaskPhaseFilter.All)
            return configuration.PostProcessTaskOrder;

        var phase = activePhaseFilter == RunTaskPhaseFilter.BeforeAR
            ? PostProcessTaskPhase.BeforeAR
            : PostProcessTaskPhase.AfterAR;

        return configuration.PostProcessTaskOrder
            .Where(id => configuration.PostProcessTaskPlacement.TryGetValue(id, out var placement) && placement == phase)
            .ToList();
    }

    private bool HasRunnableTaskForPhase(CharacterConfig config, PostProcessTaskPhase phase)
    {
        return GetRunnableTaskIdsForPhase(config, phase).Any();
    }

    public IReadOnlyList<string> GetRunnableTaskIdsForPhase(PostProcessTaskPhase phase)
    {
        return GetRunnableTaskIdsForPhase(configManager.GetActiveConfig(), phase);
    }

    private List<string> GetRunnableTaskIdsForPhase(CharacterConfig config, PostProcessTaskPhase phase)
    {
        if (PostProcessTaskOrder.Normalize(configuration))
            configuration.Save();

        return LifecyclePolicy.BuildRunnableQueue(
            configuration.PostProcessTaskOrder,
            id => configuration.PostProcessTaskPlacement.TryGetValue(id, out var placement) && placement == phase,
            id => taskBindings.TryGetValue(id, out var binding) && binding.Eligibility(config).IsRunnable);
    }

    private bool ShouldRunTask(string id, CharacterConfig config)
        => taskBindings.TryGetValue(id, out var binding) && binding.Eligibility(config).IsRunnable;

    private void CompleteRun()
    {
        var outcome = pendingRunOutcome != RunOutcome.None
            ? pendingRunOutcome
            : runHadFailure
                ? RunOutcome.PartialFailure
                : runOwnedWorkStarted
                    ? RunOutcome.Succeeded
                    : RunOutcome.Skipped;
        var summary = !string.IsNullOrWhiteSpace(pendingRunSummary)
            ? pendingRunSummary
            : outcome switch
            {
                RunOutcome.Succeeded => "All planned work completed",
                RunOutcome.PartialFailure => "Run completed with one or more task failures",
                RunOutcome.Skipped => "No runnable work",
                _ => outcome.ToString(),
            };

        RecordRunCompletion(outcome, summary);
        log.Information($"[Engine] === Vermaxion run complete: outcome={outcome}, summary={summary} ===");
        ResetRunTracking();
        SetState(EngineState.Idle);
    }

    private void RecordRunCompletion(RunOutcome outcome, string summary)
    {
        LastRunOutcome = outcome;
        LastRunSummary = summary;
        LastRunCompletedAtUtc = DateTime.UtcNow;
    }

    private void ResetRunTracking()
    {
        runQueue.Clear();
        runQueueIndex = -1;
        currentTaskOwnedWorkStarted = false;
        runOwnedWorkStarted = false;
        runHadFailure = false;
        pendingRunOutcome = RunOutcome.None;
        pendingRunSummary = string.Empty;
        requireEnabledConfig = false;
        miscHookRan = false;
        activeConfig = null;
        activePhaseFilter = RunTaskPhaseFilter.All;
        activeRunScope = AutomationRunScope.Full;
        ResetOrphanResultTracking();
        ResetWatchdog();
    }

    private void ResetOrphanResultTracking()
    {
        orphanResultSettlingSince = DateTime.MinValue;
        orphanResultLastPollAt = DateTime.MinValue;
        orphanResultLastCallbackAt = DateTime.MinValue;
        orphanResultAddonSnapshot = OceanFishingResultAddonSnapshot.NotPolled;
        orphanResultCallbackDispatched = false;
        orphanResultClosureLogged = false;
        orphanResultTimeoutLogged = false;
    }

    private void SetState(EngineState newState)
    {
        log.Debug($"[Engine] State: {state} -> {newState}");
        state = newState;
        stateEnteredAt = DateTime.UtcNow;
        RecordWatchdogProgress($"state entered {newState}");

        StatusText = newState switch
        {
            EngineState.Idle => "Idle",
            EngineState.Starting => "Starting...",
            EngineState.CheckingResets => "Checking resets",
            EngineState.RunningFCBuff => "FC Buff Refill",
            EngineState.RunningVendorStock => "Vendor Stock",
            EngineState.RunningRegisterRegistrables => "Register Registrables",
            EngineState.RunningGearUpdater => "Gear Updater",
            EngineState.RunningHighestCombatJob => "Highest Combat Job",
            EngineState.RunningCurrentJobEquipment => "Current Job Equipment",
            EngineState.RunningAlliedSociety => "Allied Society",
            EngineState.RunningSeasonalGear => "Seasonal Gear",
            EngineState.RunningAfterArPark => "After-AR Park",
            EngineState.RunningMinionRoulette => "Minion Roulette",
            EngineState.RunningRetainerListingRefill => "Retainer Listing Refill",
            EngineState.RunningRetainerEquipping => "Retainer Equipping",
            EngineState.RunningVerminion => "Verminion Queue",
            EngineState.RunningMiniCactpot => "Mini Cactpot",
            EngineState.RunningJumboCactpot => "Jumbo Cactpot",
            EngineState.RunningFashionReport => "Fashion Report",
            EngineState.RunningChocoboRacing => "Chocobo Racing",
            EngineState.RunningLootGoblinMapGather => "LootGoblin Map Gather",
            EngineState.RunningNagYourMom => "nag your mom",
            EngineState.RunningNagYourDad => "nag your dad",
            EngineState.SettlingTask => "Settling task handoff",
            EngineState.SettlingFinalHandoff => "Settling final handoff",
            EngineState.SignalingARDone => "Signaling AR",
            EngineState.Complete => "Complete",
            EngineState.Error => "Error",
            _ => "Unknown",
        };

        if (newState != EngineState.RunningNagYourMom)
        {
            ClearNagYourMomTracking();
            nagYourMomRouteCursor = 0;
        }
        if (newState != EngineState.RunningNagYourDad)
            activeDadExecution = null;
        if (newState != EngineState.RunningLootGoblinMapGather && lootGoblinMapGatherService.IsComplete)
            lootGoblinMapGatherService.Reset();
        if (newState != EngineState.RunningJumboCactpot)
            activeJumboCactpotRoute = null;
        if (newState != EngineState.RunningMiniCactpot && newState != EngineState.RunningJumboCactpot)
        {
            taskStartHoldLogged = false;
            taskStartHoldState = EngineState.Idle;
        }

        if (newState is EngineState.SettlingTask or EngineState.SettlingFinalHandoff)
        {
            ResetHandoffTracking();
            StopMovementForHandoff();
            yesAlreadyIPC.Pause();
        }
    }

    private CharacterConfig GetLiveActiveConfig()
    {
        activeConfig = configManager.GetActiveConfig();
        return activeConfig;
    }

    private void PersistCurrentCharacterConfig(Action<CharacterConfig> update, string reason)
    {
        var liveConfig = GetLiveActiveConfig();
        update(liveConfig);
        configManager.SaveCurrentAccount();
        log.Information($"[Engine] Persisted {reason} for {configManager.CurrentCharacterKey}");
    }

    private static bool TryParseLocalTime(string value, out TimeSpan result)
    {
        return TimeSpan.TryParse(value, out result);
    }

    private static bool IsWithinLocalWindow(TimeSpan now, TimeSpan start, TimeSpan end)
    {
        return start <= end
            ? now >= start && now <= end
            : now >= start || now <= end;
    }

    private void RollNagYourMomLocalDay(CharacterConfig config)
    {
        var localToday = DateTime.Now.Date;
        var ccCurrent = config.NagYourMomLastLocalDate.Date == localToday;
        var frontlineCurrent = config.NagYourMomFrontlineLastLocalDate.Date == localToday;
        var rivalWingsCurrent = config.NagYourMomRivalWingsLastLocalDate.Date == localToday;
        if (ccCurrent && frontlineCurrent && rivalWingsCurrent)
            return;

        PersistCurrentCharacterConfig(current =>
        {
            if (!ccCurrent)
            {
                current.NagYourMomAttemptsToday = 0;
                current.NagYourMomLastLocalDate = localToday;
            }

            if (!frontlineCurrent)
            {
                current.NagYourMomFrontlineAttemptsToday = 0;
                current.NagYourMomFrontlineLastLocalDate = localToday;
            }

            if (!rivalWingsCurrent)
            {
                current.NagYourMomRivalWingsAttemptsToday = 0;
                current.NagYourMomRivalWingsLastLocalDate = localToday;
            }
        }, "nag your mom local-day rollover");
    }

    private bool ShouldCountNagYourMom(CharacterConfig config)
    {
        if (!config.EnableNagYourMom || string.IsNullOrWhiteSpace(config.NagYourMomJob))
            return false;

        RollNagYourMomLocalDay(config);
        return NagYourMomRouteOrder.Any(route => IsNagYourMomRouteDue(config, route));
    }

    private bool ShouldRunNagYourMomNow(CharacterConfig config)
    {
        if (!ShouldCountNagYourMom(config))
            return false;
        if (!TryParseLocalTime(config.NagYourMomWindowStartLocal, out var start) ||
            !TryParseLocalTime(config.NagYourMomWindowEndLocal, out var end))
        {
            return false;
        }

        return IsWithinLocalWindow(DateTime.Now.TimeOfDay, start, end);
    }

    private bool TryGetNextNagYourMomRoute(CharacterConfig config, out NagYourMomRoutePlan plan, out string reason)
    {
        plan = new NagYourMomRoutePlan(MomRunRoutes.CasualCc, "Casual CC", 0, false);
        reason = "nag your mom disabled";

        if (!config.EnableNagYourMom)
            return false;

        if (string.IsNullOrWhiteSpace(config.NagYourMomJob))
        {
            reason = "Set a mom job";
            return false;
        }

        if (!TryParseLocalTime(config.NagYourMomWindowStartLocal, out var start) || !TryParseLocalTime(config.NagYourMomWindowEndLocal, out var end))
        {
            reason = "Invalid mom local-time window";
            return false;
        }

        if (!IsWithinLocalWindow(DateTime.Now.TimeOfDay, start, end))
        {
            reason = $"Outside mom window ({config.NagYourMomWindowStartLocal}-{config.NagYourMomWindowEndLocal})";
            return false;
        }

        for (var index = nagYourMomRouteCursor; index < NagYourMomRouteOrder.Length; index++)
        {
            var route = NagYourMomRouteOrder[index];
            if (!IsNagYourMomRouteDue(config, route))
                continue;

            nagYourMomRouteCursor = index;
            plan = new NagYourMomRoutePlan(
                route,
                FormatNagYourMomRouteLabel(route),
                GetRemainingNagYourMomRuns(config, route),
                route == MomRunRoutes.CasualCc && config.NagYourMomStopAtSeriesRank25);
            reason = "Ready";
            return true;
        }

        reason = "mom route daily caps hit or routes disabled";
        return false;
    }

    private static bool IsNagYourMomRouteDue(CharacterConfig config, string route)
        => IsNagYourMomRouteEnabled(config, route)
           && GetNagYourMomRouteCap(config, route) > 0
           && GetNagYourMomRouteAttempts(config, route) < GetNagYourMomRouteCap(config, route);

    private static bool IsNagYourMomRouteEnabled(CharacterConfig config, string route)
        => route switch
        {
            MomRunRoutes.Frontline => config.EnableNagYourMomFrontline,
            MomRunRoutes.RivalWings => config.EnableNagYourMomRivalWings,
            _ => config.EnableNagYourMomCasualCc,
        };

    private static int GetNagYourMomRouteCap(CharacterConfig config, string route)
        => route switch
        {
            MomRunRoutes.Frontline => config.NagYourMomFrontlineRunsPerDay,
            MomRunRoutes.RivalWings => config.NagYourMomRivalWingsRunsPerDay,
            _ => config.NagYourMomRunsPerDay,
        };

    private static int GetNagYourMomRouteAttempts(CharacterConfig config, string route)
        => route switch
        {
            MomRunRoutes.Frontline => config.NagYourMomFrontlineAttemptsToday,
            MomRunRoutes.RivalWings => config.NagYourMomRivalWingsAttemptsToday,
            _ => config.NagYourMomAttemptsToday,
        };

    private static int GetRemainingNagYourMomRuns(CharacterConfig config, string route)
        => Math.Max(0, GetNagYourMomRouteCap(config, route) - GetNagYourMomRouteAttempts(config, route));

    private static string FormatNagYourMomRouteLabel(string route)
        => route switch
        {
            MomRunRoutes.Frontline => "Frontline",
            MomRunRoutes.RivalWings => "Rival Wings",
            _ => "Casual CC",
        };

    private void TrackAcceptedNagYourMomRequest(MomRunResult result, NagYourMomRoutePlan plan)
    {
        nagYourMomActiveRequestId = result.RequestId ?? string.Empty;
        nagYourMomActiveRoute = string.IsNullOrWhiteSpace(result.Route) ? plan.Route : result.Route;
        nagYourMomRequestedRuns = result.RequestedRunCount > 0 ? result.RequestedRunCount : plan.RemainingRuns;
        nagYourMomLastCompletedRuns = Math.Max(0, result.CompletedRunCount);
        nagYourMomLostStatusSince = DateTime.MinValue;
        nagYourMomLostStatusLastLoggedAt = DateTime.MinValue;
    }

    private void ClearNagYourMomTracking()
    {
        nagYourMomRequestIssued = false;
        nagYourMomActiveRequestId = string.Empty;
        nagYourMomActiveRoute = MomRunRoutes.CasualCc;
        nagYourMomRequestedRuns = 0;
        nagYourMomLastCompletedRuns = 0;
        nagYourMomLostStatusSince = DateTime.MinValue;
        nagYourMomLostStatusLastLoggedAt = DateTime.MinValue;
    }

    private int CreditNagYourMomTerminalResult(MomRunResult result)
    {
        var route = string.IsNullOrWhiteSpace(result.Route) ? nagYourMomActiveRoute : result.Route;
        var observedCompletedRuns = Math.Max(nagYourMomLastCompletedRuns, result.CompletedRunCount);
        var runsToCredit = result.Status == MomRunStatus.Completed
            ? Math.Max(result.RequestedRunCount > 0 ? result.RequestedRunCount : nagYourMomRequestedRuns, observedCompletedRuns)
            : observedCompletedRuns;

        return CreditNagYourMomRunCount(route, runsToCredit, result.Summary);
    }

    private int CreditNagYourMomRunCount(string route, int runCount, string statusText)
    {
        var creditableRuns = Math.Max(0, runCount);
        if (creditableRuns == 0)
        {
            NagYourMomStatusText = statusText;
            return 0;
        }

        var liveConfig = GetLiveActiveConfig();
        var runsToCredit = Math.Min(creditableRuns, GetRemainingNagYourMomRuns(liveConfig, route));
        if (runsToCredit <= 0)
        {
            NagYourMomStatusText = statusText;
            return 0;
        }

        PersistCurrentCharacterConfig(current =>
        {
            CreditNagYourMomRoute(current, route, runsToCredit);
        }, $"nag your mom {FormatNagYourMomRouteLabel(route)} run credit ({runsToCredit})");

        NagYourMomStatusText = statusText;
        return runsToCredit;
    }

    private static void CreditNagYourMomRoute(CharacterConfig config, string route, int runsToCredit)
    {
        var localDate = DateTime.Now.Date;
        switch (route)
        {
            case MomRunRoutes.Frontline:
                config.NagYourMomFrontlineAttemptsToday = Math.Min(
                    config.NagYourMomFrontlineRunsPerDay,
                    config.NagYourMomFrontlineAttemptsToday + runsToCredit);
                config.NagYourMomFrontlineLastLocalDate = localDate;
                break;

            case MomRunRoutes.RivalWings:
                config.NagYourMomRivalWingsAttemptsToday = Math.Min(
                    config.NagYourMomRivalWingsRunsPerDay,
                    config.NagYourMomRivalWingsAttemptsToday + runsToCredit);
                config.NagYourMomRivalWingsLastLocalDate = localDate;
                break;

            default:
                config.NagYourMomAttemptsToday = Math.Min(
                    config.NagYourMomRunsPerDay,
                    config.NagYourMomAttemptsToday + runsToCredit);
                config.NagYourMomLastLocalDate = localDate;
                break;
        }
    }

    private bool ApplyMomDisableRouteRecommendation(MomRunResult result)
    {
        if (!result.DisableRouteRecommended)
            return false;

        return DisableNagYourMomRoute(result.Route, string.IsNullOrWhiteSpace(result.DisableRouteReason) ? result.Summary : result.DisableRouteReason);
    }

    private bool DisableNagYourMomRoute(string route, string reason)
    {
        if (route != MomRunRoutes.RivalWings)
            return false;

        NagYourMomStatusText = string.IsNullOrWhiteSpace(reason)
            ? "Rival Wings skipped because mom recommends disabling this route; the checkbox was preserved."
            : $"Rival Wings skipped: {reason} The checkbox was preserved.";
        log.Warning($"[Engine] {NagYourMomStatusText}");
        return true;
    }

    private bool ShouldWaitForLostNagYourMomStatus(string reason)
    {
        var now = DateTime.UtcNow;
        var dutyOrQueueActive = IsNagYourMomDutyOrQueueActive();

        if (dutyOrQueueActive)
        {
            nagYourMomLostStatusSince = DateTime.MinValue;
            NagYourMomStatusText = $"{reason} Waiting for active duty/queue to clear.";
            LogNagYourMomLostStatusWait(reason, dutyOrQueueActive, now);
            return true;
        }

        if (nagYourMomLostStatusSince == DateTime.MinValue)
        {
            nagYourMomLostStatusSince = now;
            LogNagYourMomLostStatusWait(reason, dutyOrQueueActive, now);
        }

        if (now - nagYourMomLostStatusSince < NagYourMomLostStatusGrace)
        {
            NagYourMomStatusText = $"{reason} Waiting for mom status recovery.";
            return true;
        }

        return false;
    }

    private void LogNagYourMomLostStatusWait(string reason, bool dutyOrQueueActive, DateTime now)
    {
        if (nagYourMomLostStatusLastLoggedAt != DateTime.MinValue &&
            now - nagYourMomLostStatusLastLoggedAt < NagYourMomLostStatusLogThrottle)
        {
            return;
        }

        log.Warning($"[Engine] nag your mom status unavailable during active request; waiting. reason={reason}, dutyOrQueueActive={dutyOrQueueActive}, requestId={nagYourMomActiveRequestId}, completedRuns={nagYourMomLastCompletedRuns}, requestedRuns={nagYourMomRequestedRuns}");
        nagYourMomLostStatusLastLoggedAt = now;
    }

    private static bool IsNagYourMomDutyOrQueueActive()
    {
        if (Plugin.Condition[ConditionFlag.BoundByDuty] || Plugin.Condition[ConditionFlag.BoundByDuty56])
            return true;
        if (Plugin.Condition[ConditionFlag.InDutyQueue])
            return true;
        if (Plugin.Condition[ConditionFlag.WaitingForDuty])
            return true;
        if (Plugin.Condition[ConditionFlag.WaitingForDutyFinder])
            return true;
        if (Plugin.Condition[ConditionFlag.BetweenAreas] || Plugin.Condition[ConditionFlag.BetweenAreas51])
            return true;
        if (IsAddonVisible("ContentsFinderConfirm") || IsAddonVisible("ContentsFinder"))
            return true;

        return false;
    }

    private bool ShouldCountNagYourDad(CharacterConfig config)
    {
        if (!config.EnableNagYourDad)
            return false;

        return HasNagYourDadConfiguredWork(config);
    }

    private bool ShouldRunNagYourDadNow(CharacterConfig config, out string reason)
    {
        reason = "nag your dad disabled";

        if (!config.EnableNagYourDad)
            return false;

        if (!HasNagYourDadConfiguredWork(config))
        {
            reason = "Select a DAD preset or schedule";
            return false;
        }

        reason = "Ready";
        return true;
    }

    private static bool HasNagYourDadConfiguredWork(CharacterConfig config)
        => config.NagYourDadSelectionKind is DadSelectionKind.Preset or DadSelectionKind.Schedule &&
           !string.IsNullOrWhiteSpace(config.NagYourDadSelectionId);

    /// <summary>
    /// Handle territory changes to close menus that might be stuck after teleporting.
    /// This prevents pathing issues when services try to navigate after area changes.
    /// </summary>
    private void OnTerritoryChanged(ushort territoryType)
        => OnTerritoryChanged((uint)territoryType);

    private void OnTerritoryChanged(uint territoryType)
    {
        try
        {
            log.Information($"[Engine] Territory changed to {territoryType} - clearing known task UI");
            TryCloseOwnedUiBestEffort(UiCloseFallbackMode.OnlyWhenKnownAddonVisible);
        }
        catch (Exception ex)
        {
            log.Error($"[Engine] Error handling territory change: {ex.Message}");
        }
    }
}
