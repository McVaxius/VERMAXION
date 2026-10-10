using AethertekUI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using System.Numerics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Dalamud.Game.Command;
using Dalamud.Game.Chat;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Plugin.Services;
using ECommons;
using ECommons.ExcelServices.TerritoryEnumeration;
using VERMAXION.IPC;
using VERMAXION.Services;
using VERMAXION.Models;
using VERMAXION.CustomDeliveries;
using VERMAXION.Windows;

namespace VERMAXION;

public sealed class Plugin : IDalamudPlugin, IFishingStartupRuntime, IScheduledOfflineHoldRuntime
{
    private readonly System.Collections.Generic.Dictionary<Dalamud.Interface.Windowing.IWindow, AethertekUI.MaterialWindowOpacity> windowOpacities = new();
    private readonly AethertekUI.MaterialWindowOpacity fontStatusOpacity = new();
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider GameInterop { get; private set; } = null!;
    [PluginService] internal static IDtrBar DtrBar { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static ITargetManager TargetManager { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IGameInventory GameInventory { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;

    internal Dalamud.Interface.Textures.TextureWraps.IDalamudTextureWrap OriginalIcon
        => TextureProvider.GetFromFile(System.IO.Path.Combine(
            PluginInterface.AssemblyLocation.DirectoryName ?? "", "icon.png")).GetWrapOrEmpty();
    [PluginService] internal static IUnlockState UnlockState { get; private set; } = null!;
    [PluginService] internal static IDutyState DutyState { get; private set; } = null!;
    [PluginService] internal static INotificationManager NotificationManager { get; private set; } = null!;

    private const string CommandName = "/vermaxion";
    private const string AliasCommandName = "/vmx";
    private const string DebugAttemptMarker = "fish-collection-client7-20261006-44-crystal-perch-cleanup";
#if DEBUG
    // Source-selected bounds for this isolated client7 reload test; no saved setting.
    private const string DebugCollectionStopMilestone = "Cleanup";
    private const uint DebugCollectionTargetItemId = 7682;
    private static readonly TimeSpan DebugCollectionRunLimit = TimeSpan.FromMinutes(15);
    private DateTime debugCollectionStopDeadline;
    private bool debugCollectionStopArmed, debugCollectionStopPending;
#endif
    private DateTime nextChocoboContinuationUtc;
    private VermaxionFonts uiFonts = null!;
    private UiText uiText = null!;
    private AethertekUI.Dalamud.MaterialTextHost? shapedText;
    private MaterialTheme uiTheme = null!;
    private readonly MaterialWindowFold fontStatusFold = new();
    private readonly MaterialWindowDecorations fontStatusDecorations = new();
    private MaterialOptions<string> languageOptions = null!;
    private string appliedLanguage = "";
    private uint appliedAccent;
    private Vector3 accentDraft;
    private int checkedFontGeneration = -1;
    private int checkedHindiGeneration = -1;
    private bool fontIssueLogged;

    private const string ExpectedDebugPluginPath = @"A:\ff14\xivlauncher7\FishCollectionClient7Verification\VERMAXION\VERMAXION.dll";

    public Configuration Configuration { get; init; }
    public ConfigManager ConfigManager { get; init; }
    public ResetDetectionService ResetDetectionService { get; init; }
    public FCBuffService FCBuffService { get; init; }
    public FCBuffInventoryService FCBuffInventoryService { get; init; }
    public VerminionService VerminionService { get; init; }
    public CactpotService CactpotService { get; init; }
    public ChocoboRaceService ChocoboRaceService { get; init; }
    public FashionReportService FashionReportService { get; init; }
    public CustomDeliveriesService CustomDeliveriesService { get; init; }
    public ChocoboStablesService ChocoboStablesService { get; init; }
    private DeliveryFishing DeliveryFishing { get; init; }
    public RegisterRegistrablesService RegisterRegistrablesService { get; init; }
    public VendorStockService VendorStockService { get; init; }
    public RetainerListingRefillService RetainerListingRefillService { get; init; }
    public RetainerEquippingService RetainerEquippingService { get; init; }
    public ARPostProcessService ARPostProcessService { get; init; }
    public AutoRetainerIPC AutoRetainerIPC { get; init; }
    internal AutoRetainerSelectionGuard AutoRetainerSelectionGuard { get; init; }
    public AutoHookIPC AutoHookIPC { get; init; }
    public XADatabaseIPCClient XADatabaseIPCClient { get; init; }
    public ChokeAboIpcClient ChokeAboIpcClient { get; init; }
    public AdsIpcClient AdsIpcClient { get; init; }
    public RegistrableConfigManager RegistrableConfigManager { get; init; }
    public MinionRouletteService MinionRouletteService { get; init; }
    public SeasonalGearService SeasonalGearService { get; init; }
    public GearUpdaterService GearUpdaterService { get; init; }
    public HighestCombatJobService HighestCombatJobService { get; init; }
    public CurrentJobEquipmentService CurrentJobEquipmentService { get; init; }
    internal IEquipmentAutomationRuntime EquipmentAutomationRuntime { get; init; }
    public YesAlreadyIPC YesAlreadyIPC { get; init; }
    public VNavmeshIPC VNavmeshIPC { get; init; }
    public LifestreamIPC LifestreamIPC { get; init; }
    public StylistIPC StylistIPC { get; init; }
    public AlliedSocietyService AlliedSocietyService { get; init; }
    public AfterArParkService AfterArParkService { get; init; }
    public MomIPCClient MomIPCClient { get; init; }
    public DadIPCClient DadIPCClient { get; init; }
    public LootGoblinIPCClient LootGoblinIPCClient { get; init; }
    public LootGoblinMapGatherService LootGoblinMapGatherService { get; init; }
    public LootGoblinMapGatherManualRunCoordinator LootGoblinMapGatherManualRunCoordinator { get; init; }
    public WorkshopBellService WorkshopBellService { get; init; }
    public FishingService FishingService { get; init; }
    internal FishCollectionService FishCollection { get; private set; } = null!;
    internal FishCollectionWindow FishCollectionWindow { get; private set; } = null!;
    public FishingRelogCoordinator FishingRelogCoordinator { get; init; }
    public CharacterSelectStallRecoveryService CharacterSelectStallRecovery { get; init; }
    public FishingStartupCoordinator FishingStartupCoordinator { get; init; }
    public ScheduledOfflineHoldCoordinator ScheduledOfflineHoldCoordinator { get; init; }
    public FishingRunLifecycle FishingRunLifecycle { get; init; }
    public IFisherGearsetRuntime FisherGearsetRuntime { get; init; }
    public FisherGearsetTestService FisherGearsetTestService { get; init; }
    public VermaxionEngine Engine { get; init; }
    public VermaxionIncidentWriter IncidentWriter { get; init; }
    public AutomationStatusIpcProvider AutomationStatusIpcProvider { get; init; }
    public DadHandoffIpcProvider DadHandoffIpcProvider { get; private set; } = null!;
    public bool DadHandoffBlocksNewWork => DadHandoffIpcProvider?.BlocksNewWork == true;

    public readonly WindowSystem WindowSystem = new("VERMAXION");
    public ConfigWindow ConfigWindow { get; init; }
    public MainWindow MainWindow { get; init; }
    internal DebugWindow DebugWindow { get; init; }
    internal VerminionWindow VerminionWindow { get; init; }
    public RegistrableConfigWindow RegistrableConfigWindow { get; init; }

    private IDtrBarEntry? dtrEntry;
    private bool wasLoggedIn;
    private bool pendingCharacterRegistration;
    private bool characterRegistrationCompletedThisLogin;
    private string? pendingDebugTaskId;
    private string? pendingDebugDispatchTaskId;
    private DateTime debugDispatchReadyDeadline;
    private ulong debugDispatchContentId;
    internal string DebugTaskStatus { get; private set; } = "No task selected.";
    internal string DebugBuildMarker => DebugAttemptMarker;
    internal bool IsCharacterRegistered => characterRegistrationCompletedThisLogin;
    private string characterRegistrationFailureReason = string.Empty;
    private DateTime characterRegistrationWorldReadySince = DateTime.MinValue;
    private bool pendingBeforeArLogin;
    private bool pendingFishingPostprocessHandoff;
    private bool beforeArStartedThisLogin;
    private bool beforeArArmedByPostprocess;
    private DateTime beforeArLoginPendingSince = DateTime.MinValue;
    private DateTime beforeArLoginLastDiagnosticAt = DateTime.MinValue;
    private DateTime beforeArWorldReadySince = DateTime.MinValue;
    private DateTime beforeArSuppressionRecoveryLastAttemptAt = DateTime.MinValue;
    private bool releaseOnlyPostprocessFinishPending;
    private string releaseOnlyPostprocessFinishReason = string.Empty;
    private string beforeArTimeoutReleaseReason = string.Empty;
    private DateTime fishingRelogContinuationLastCheckAt = DateTime.MinValue;
    private DateTime fishingRelogWorldReadySince = DateTime.MinValue;
    private DateTime fishingRelogLastDiagnosticAt = DateTime.MinValue;
    private bool retainerCollectOnlyObservedArProcessing;
    private bool dashboardRunYesAlreadyPauseOwned;
    private DateTime nextAutomaticDueCheckUtc;
    private DateTime? retainerlessTimerDueUtc;
    private bool retainerlessTimerWasEnabled;
    private bool retainerlessTimerRunPendingCompletion;
    private string retainerlessTimerAccountId = string.Empty;
    private ulong retainerlessTimerCharacterId;
    private int retainerlessTimerIntervalMinutes;
    private readonly Dictionary<(ulong Character, string Task), DateTime> automaticTaskNextAttemptUtc = new();
    private int loggedDadBeforeArYield;
    private const int BeforeArLoginTimeoutSeconds = 120;
    private const double BeforeArWorldReadyStableSeconds = 2.0;
    private static readonly TimeSpan FishingRelogContinuationPollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan BeforeArArmedStallTimeout = BeforeArArmedStallPolicy.DefaultTimeout;
    public BeforeArGateState BeforeArGate { get; private set; } = BeforeArGateState.Idle;
    public string BeforeArGateStatus { get; private set; } = "Idle";
    public DateTime BeforeArArmedAtUtc { get; private set; } = DateTime.MinValue;
    public string BeforeArArmedAccountId { get; private set; } = string.Empty;
    public string BeforeArArmedCharacterKey { get; private set; } = string.Empty;
    public string BeforeArStatusText => BuildBeforeArStatusText();
    private bool IsScheduledOfflineHoldActive => Configuration.ScheduledOfflineHold != null;
    private bool OfflineLogoutBlocksOrdinaryAutomation
        => IsScheduledOfflineHoldActive || ScheduledOfflineHoldCoordinator?.BlocksOrdinaryAutomation == true;
    public bool IsFishingRunActive => FishCollection?.IsActive == true || FishingRunLifecycle.IsActive ||
                                      FishingService.IsActive ||
                                      FishingRelogCoordinator.IsActive ||
                                      FishingStartupCoordinator.HasPendingRelogContinuation;
    public string FishingRunStatusText
    {
        get
        {
            if (FishCollection?.IsActive == true) return FishCollection.Status;
            var prefix = FishingRunLifecycle.StatusPrefix;
            if (FishingRelogCoordinator.IsActive)
                return prefix + FishingRelogCoordinator.StatusText;
            if (FishingStartupCoordinator.HasPendingRelogContinuation)
                return prefix + $"Relog pending {FishingStartupCoordinator.PendingRelogCharacterKey}";
            if (!string.IsNullOrWhiteSpace(FishingRunLifecycle.LastBeginError))
                return prefix + FishingRunLifecycle.LastBeginError;
            return FishingService.StatusText;
        }
    }

    public Plugin()
    {
        ECommonsMain.Init(PluginInterface, this);
        LogPluginAssemblyDetails();

        var storedConfiguration = PluginInterface.GetPluginConfig() as Configuration;
        var setupWizardDecision = SetupWizardMigrationPolicy.Decide(
            storedConfiguration != null,
            storedConfiguration?.SetupWizardStateMigrated ?? false,
            storedConfiguration?.SetupWizardCompleted ?? false);
        Configuration = storedConfiguration ?? new Configuration();
        var compactMigrated = Configuration.ApplyCompactDefaults();
        pendingDebugTaskId = string.IsNullOrWhiteSpace(Configuration.DebugTaskId) ? null : Configuration.DebugTaskId;
        if (pendingDebugTaskId != null)
            SetDebugTaskStatus("Pending: waiting for character registration.");
        if (compactMigrated || Configuration.SetupWizardCompleted != setupWizardDecision.Completed ||
            Configuration.SetupWizardStateMigrated != setupWizardDecision.Migrated)
        {
            Configuration.SetupWizardCompleted = setupWizardDecision.Completed;
            Configuration.SetupWizardStateMigrated = setupWizardDecision.Migrated;
            Configuration.Save();
        }
        ConfigManager = new ConfigManager(PluginInterface, Log);
        // Rail spot-selection knobs persist in config; apply before any fishing machinery constructs so a
        // bare client (no reflection tooling, no external scripts) starts correctly tuned.
        OceanFishingContinuousRailPolicy.ApplyConfiguration(Configuration);
        OceanFishingDiscreteSpotPolicy.ApplyConfiguration(Configuration);
        ApplyLegacyFishingOperationSettingsIfNeeded();
        ApplyFishingStockCatalogMigrationIfNeeded();
        RegistrableConfigManager = new RegistrableConfigManager(Log, DataManager, PluginInterface.ConfigDirectory.FullName);
        AutoRetainerIPC = new AutoRetainerIPC(PluginInterface, Log);
        AutoRetainerSelectionGuard = new AutoRetainerSelectionGuard(
            AutoRetainerIPC,
            message => Log.Information(message),
            message => Log.Warning(message));
        AutoHookIPC = new AutoHookIPC(PluginInterface, Log);
        XADatabaseIPCClient = new XADatabaseIPCClient(PluginInterface, Log);
        ChokeAboIpcClient = new ChokeAboIpcClient(PluginInterface);
        AdsIpcClient = new AdsIpcClient(PluginInterface, Log);

        if (!string.IsNullOrEmpty(Configuration.LastAccountId))
            ConfigManager.CurrentAccountId = Configuration.LastAccountId;

        if (ClientState.IsLoggedIn)
        {
            wasLoggedIn = true;
            QueueCharacterRegistration("plugin load while logged in");
            BeginBeforeArLoginPendingFromPluginLoad();
        }

        // Subscribe to character change events
        ConfigManager.OnCharacterChanged += OnCharacterChanged;

        // Initialize services
        VNavmeshIPC = new VNavmeshIPC(Log, CommandManager);
        LifestreamIPC = new LifestreamIPC(PluginInterface, Log, CommandManager);
        ResetDetectionService = new ResetDetectionService(Log);
        YesAlreadyIPC = new YesAlreadyIPC(Log);
        FCBuffService = new FCBuffService(CommandManager, Log, ClientState, Condition, ObjectTable, TargetManager, ConfigManager, YesAlreadyIPC, this);
        FCBuffInventoryService = new FCBuffInventoryService(CommandManager, Log, GameGui);
        VerminionService = new VerminionService(CommandManager, Condition, Log, ConfigManager, LifestreamIPC, VNavmeshIPC);
        CactpotService = new CactpotService(CommandManager, Log, ClientState, ConfigManager, new SaucyMiniCactpotService(Log), VNavmeshIPC, LifestreamIPC);
        ChocoboRaceService = new ChocoboRaceService(CommandManager, Log, ConfigManager, ChokeAboIpcClient,
            () => CanStartChocoboProgression(out var reason) ? null : reason);
        FashionReportService = new FashionReportService(CommandManager, ClientState, ObjectTable, Log, VNavmeshIPC);
        RegisterRegistrablesService = new RegisterRegistrablesService(Log, ConfigManager, DataManager);
        StylistIPC = new StylistIPC(PluginInterface, Log);
        EquipmentAutomationRuntime = new NativeEquipmentAutomationRuntime(
            DataManager,
            Framework,
            PlayerState,
            ClientState,
            Condition,
            ObjectTable,
            StylistIPC,
            Log);
        MinionRouletteService = new MinionRouletteService(Log, ConfigManager);
        SeasonalGearService = new SeasonalGearService(EquipmentAutomationRuntime, Log);
        GearUpdaterService = new GearUpdaterService(EquipmentAutomationRuntime, Log);
        HighestCombatJobService = new HighestCombatJobService(EquipmentAutomationRuntime, Log);
        CurrentJobEquipmentService = new CurrentJobEquipmentService(EquipmentAutomationRuntime, Log);
        FishingRunLifecycle = new FishingRunLifecycle(Log, YesAlreadyIPC, AutoRetainerIPC, AutoHookIPC);
        FisherGearsetRuntime = new FisherGearsetRuntime();
        FisherGearsetTestService = new FisherGearsetTestService(
            FisherGearsetRuntime,
            Log,
            message => ChatGui.Print(message));
        var alliedSocietyBridge = new QuestionableCompanionAlliedSocietyBridge();
        AlliedSocietyService = new AlliedSocietyService(
            EquipmentAutomationRuntime,
            ClientState,
            PlayerState,
            ObjectTable,
            Log,
            alliedSocietyBridge);
        AfterArParkService = new AfterArParkService(Log, ClientState, LifestreamIPC);
        MomIPCClient = new MomIPCClient(PluginInterface, Log);
        DadIPCClient = new DadIPCClient(PluginInterface, Log);
        LootGoblinIPCClient = new LootGoblinIPCClient(PluginInterface, Log);
        LootGoblinMapGatherService = new LootGoblinMapGatherService(Log, LootGoblinIPCClient);
        LootGoblinMapGatherManualRunCoordinator = new LootGoblinMapGatherManualRunCoordinator(Log, ConfigManager, LootGoblinMapGatherService);
        VendorStockService = new VendorStockService(CommandManager, Log, ConfigManager, VNavmeshIPC);
        WorkshopBellService = new WorkshopBellService(Log, LifestreamIPC, VNavmeshIPC);
        RetainerListingRefillService = new RetainerListingRefillService(
            Log,
            Configuration,
            VNavmeshIPC,
            WorkshopBellService,
            AutoRetainerIPC);
        RetainerEquippingService = new RetainerEquippingService(
            Log,
            ConfigManager,
            AutoRetainerIPC,
            WorkshopBellService,
            DataManager,
            Framework);
        IncidentWriter = new VermaxionIncidentWriter(PluginInterface.ConfigDirectory.FullName);

        // AR PostProcess - fires OnARCharacterReady when AR signals us
        ARPostProcessService = new ARPostProcessService(
            PluginInterface,
            Log,
            OnARCharacterReady,
            ArmBeforeArSuppressionFromPostprocess,
            () => Configuration.Enabled && !DadHandoffBlocksNewWork && !OfflineLogoutBlocksOrdinaryAutomation && !VerminionService.HasQuestAcquisition);
        FishingRelogCoordinator = new FishingRelogCoordinator(Log, ARPostProcessService, AutoRetainerIPC, ConfigManager);
        CharacterSelectStallRecovery = new CharacterSelectStallRecoveryService(Log);
        ScheduledOfflineHoldCoordinator = new ScheduledOfflineHoldCoordinator(
            this,
            message => Log.Information(message),
            message => Log.Warning(message));
        FishingService = new FishingService(Log, Configuration, ConfigManager, XADatabaseIPCClient, VendorStockService, AdsIpcClient, VNavmeshIPC, LifestreamIPC, AutoRetainerIPC, FishingRunLifecycle, ScheduledOfflineHoldCoordinator, FisherGearsetRuntime, DutyState);
        FishingStartupCoordinator = new FishingStartupCoordinator(this);
        FishCollection = new FishCollectionService(this);
        FishCollectionWindow = new FishCollectionWindow(this);
        DeliveryFishing = new DeliveryFishing(this);
        CustomDeliveriesService = new CustomDeliveriesService(this)
        {
            FishingHandler = DeliveryFishing.RunAsync,
            FishingCleanup = DeliveryFishing.Cancel,
            FishingCleanupPending = () => DeliveryFishing.IsCleanupPending,
            FishingCleanupStatus = () => DeliveryFishing.StatusText,
        };

        // Engine - orchestrates all tasks
        ChocoboStablesService = new ChocoboStablesService(this);
        Engine = new VermaxionEngine(
            Log, Configuration, ConfigManager, ResetDetectionService,
            FCBuffService, FCBuffInventoryService, VerminionService,
            CactpotService, ChocoboRaceService, ChokeAboIpcClient, FashionReportService, CustomDeliveriesService, ChocoboStablesService,
            VendorStockService, FishingService,
            RegisterRegistrablesService, GearUpdaterService, HighestCombatJobService,
            CurrentJobEquipmentService, SeasonalGearService, AlliedSocietyService, AfterArParkService,
            MinionRouletteService, EquipmentAutomationRuntime,
            RetainerListingRefillService, RetainerEquippingService, WorkshopBellService, ARPostProcessService, YesAlreadyIPC,
            ClientState, MomIPCClient, DadIPCClient, LootGoblinMapGatherService, AutoRetainerIPC, VNavmeshIPC, LifestreamIPC, IncidentWriter);
        Engine.StartBlocker = () => DadHandoffBlocksNewWork
            ? "A granted or pending DAD handoff reservation blocks new VERMAXION work."
            : VerminionService.HasQuestAcquisition ? "Questionable minion acquisition blocks new VERMAXION work."
            : DeliveryFishing.IsCleanupPending ? CustomDeliveriesService.StatusText
            : ChocoboStablesService.IsCleanupPending ? "Stable menu cleanup is still settling."
            : null;
        AutomationStatusIpcProvider = new AutomationStatusIpcProvider(PluginInterface, BuildAutomationStatus);
        DadHandoffIpcProvider = new DadHandoffIpcProvider(
            PluginInterface,
            Log,
            AutoRetainerIPC,
            BuildAutomationStatus,
            YieldUnstartedBeforeArGateToDad);

        // Windows
        ApplyAppearance();
        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this);
        DebugWindow = new DebugWindow(this);
        VerminionWindow = new VerminionWindow(this);
        RegistrableConfigWindow = new RegistrableConfigWindow(Log, RegistrableConfigManager, ConfigManager, DataManager, Configuration);
        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);
        WindowSystem.AddWindow(FishCollectionWindow);
        WindowSystem.AddWindow(DebugWindow);
        WindowSystem.AddWindow(VerminionWindow);
        WindowSystem.AddWindow(RegistrableConfigWindow);
        if (setupWizardDecision.ShouldAutoOpen && !Configuration.SetupWizardCompleted)
        {
            ConfigWindow.IsOpen = true;
            ConfigWindow.OpenWizard(SetupWizardKind.DefaultAndSync);
        }

        // Commands
        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the Vermaxion main window."
        });
        CommandManager.AddHandler(AliasCommandName, new CommandInfo(OnAliasCommand)
        {
            HelpMessage = "Vermaxion: /vmx [on|off|run|stop|config|debug|v] or /vmx to open UI."
        });

        // Events
        PluginInterface.UiBuilder.Draw += DrawUi;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        // DTR bar
        SetupDtrBar();

        // Login detection
        ClientState.Login += OnLoginEvent;
        Framework.Update += OnFrameworkUpdate;
        ChatGui.ChatMessage += OnChatMessage;

        Log.Information("===Vermaxion loaded!===");
    }

    private void DrawUi()
    {
        ApplyAppearance();
        if(!WindowSystem.Windows.Any(window => window.IsOpen)) return;
        using var text = uiText.Enter();
        shapedText ??= new(TextureProvider);
        using var shaped = shapedText.Push();
        if(!uiFonts.Ready)
        {
            if(!fontIssueLogged && uiFonts.LoadException is { } error) { Log.Error(error,"[VERMAXION] Required UI fonts failed to load.");fontIssueLogged=true; }
            // Do not silently present temporary host fonts as the finished UI.
            DrawFontStatus(uiFonts.LoadException is null);
            return;
        }
        if (checkedHindiGeneration != uiFonts.Generation)
        {
            var generation = uiFonts.Generation;
            var hindiAvailable = true;
            foreach (var size in VermaxionPresentation.FontSizes)
                hindiAvailable &= shapedText.Renderer.TryCheckGlyphs(["हिन्दी"], size * ImGuiHelpers.GlobalScale, out _);
            languageOptions.Replace(UiText.Languages.Select(l => new MaterialOption<string>(l.Code, l.Code,
                l.Code == "hi" && !hindiAvailable ? "Hindi (unavailable)" : l.Name, l.Code == "hi" && !hindiAvailable)).ToArray());
            checkedHindiGeneration = generation;
        }
        if(checkedFontGeneration!=uiFonts.Generation)
        {
            try
            {
                var generation=uiFonts.Generation;
                foreach (var size in VermaxionPresentation.FontSizes)
                    shapedText.Renderer.CheckGlyphs(uiText.RequiredText, size * ImGuiHelpers.GlobalScale);
                uiFonts.CheckGlyphs(uiText.RequiredText);
                checkedFontGeneration=generation;
            }
            catch(Exception ex) { if(!fontIssueLogged) { Log.Error(ex,"[VERMAXION] Required UI glyph coverage failed.");fontIssueLogged=true; } DrawFontStatus(false);return; }
        }
        using var theme = MaterialTheme.Push(uiTheme, ImGuiHelpers.GlobalScale, MaterialStyleMode.ColorsOnly);
        using var body = uiFonts.Push(UiFontRole.Body);
        WindowSystem.Draw();
        foreach (var window in WindowSystem.Windows)
        {
            if (!windowOpacities.TryGetValue(window, out var opacity))
                windowOpacities.Add(window, opacity = new());
            ApplyWindowOpacity(opacity, window.WindowName);
        }
    }

    private void DrawFontStatus(bool loading)
    {
        using var theme = MaterialTheme.Push(uiTheme, ImGuiHelpers.GlobalScale, MaterialStyleMode.ColorsOnly);
        using var chrome = MaterialWindowChrome.Push();
        Dalamud.Bindings.ImGui.ImGui.SetNextWindowSize(new System.Numerics.Vector2(460f*ImGuiHelpers.GlobalScale,0f));
        fontStatusFold.PreDraw("VERMAXION##FontStatus", null, null, false, fontStatusDecorations.Prepare);
        var visible = Dalamud.Bindings.ImGui.ImGui.Begin("VERMAXION##FontStatus",Dalamud.Bindings.ImGui.ImGuiWindowFlags.AlwaysAutoResize);
        try
        {
            if (visible)
            {
                fontStatusDecorations.Paint();
                if (appliedLanguage == "hi")
                {
                    ImGui.TextWrapped(loading ? "Loading Hindi UI fonts..." : "Hindi UI fonts are unavailable. See the plugin log.");
                    if (!loading && ImGui.Button("Use English")) { Configuration.UiLanguage = "en"; Configuration.Save(); }
                }
                else MaterialText.TextWrapped(UiText.T(loading?"Loading UI fonts...":"UI fonts failed to load. See the plugin log."));
            }
        }
        finally
        {
            Dalamud.Bindings.ImGui.ImGui.End();
            fontStatusDecorations.Paint();
            fontStatusFold.PostDraw();
            ApplyWindowOpacity(fontStatusOpacity, "VERMAXION##FontStatus");
        }
    }

    private void ApplyAppearance()
    {
        var language=UiText.Languages.Any(l=>l.Code==Configuration.UiLanguage)?Configuration.UiLanguage:"en";
        if(language!=appliedLanguage)
        {
            uiFonts?.Dispose();
            uiText?.Dispose();
            uiText=new(language,role=>uiFonts!.Push(role));
            uiFonts=new(PluginInterface.UiBuilder.FontAtlas,uiText.GlyphRanges(),language);
            languageOptions=new(UiText.Languages.Select(l=>new MaterialOption<string>(l.Code,l.Code,l.Name)).ToArray());
            appliedLanguage=language;
            checkedFontGeneration=-1;
            checkedHindiGeneration = -1;
            fontIssueLogged=false;
        }
        if(uiTheme is null || (Configuration.UiAccentRgb & 0xFFFFFF)!=appliedAccent)
        {
            appliedAccent=Configuration.UiAccentRgb & 0xFFFFFF;
            uiTheme=VermaxionPresentation.Theme(appliedAccent);
            var color=VermaxionPresentation.Rgb(appliedAccent);
            accentDraft=new(color.X,color.Y,color.Z);
        }
    }

    public void DrawAppearanceSelector()
    {
        var language=appliedLanguage;
        using var controls=MaterialControls.Push(VermaxionPresentation.Controls(28,18));
        var changed=MaterialAppearanceSelector.Draw("appearance",ref accentDraft,ref language,languageOptions,
            new(UiText.T("Color"),UiText.T("Language"),UiText.T("Teal"),UiText.T("Blue"),UiText.T("Pink"),UiText.T("Custom RGB")), languageWidth: 140);
        if(changed.AccentChanged) Configuration.UiAccentRgb=((uint)Math.Clamp((int)MathF.Round(accentDraft.X*255),0,255)<<16)
            |((uint)Math.Clamp((int)MathF.Round(accentDraft.Y*255),0,255)<<8)|(uint)Math.Clamp((int)MathF.Round(accentDraft.Z*255),0,255);
        if(changed.LanguageChanged) Configuration.UiLanguage=language;
        if(changed.AccentChanged || changed.LanguageChanged) Configuration.Save();
    }


    private static void LogPluginAssemblyDetails()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version?.ToString() ?? "unknown";
        var path = PluginInterface.AssemblyLocation.FullName;
        Log.Information($"[Plugin] VERMAXION assembly loaded from '{path}', version '{version}', expected debug path '{ExpectedDebugPluginPath}', attempt marker '{DebugAttemptMarker}'");
    }

    public void Dispose()
    {
        Engine.CancelNagYourMomSeriesRankTest();
        ChocoboRaceService.Dispose();
        ChokeAboIpcClient.SuspendTargetCycle(PlayerState.ContentId);
        DadHandoffIpcProvider.Dispose();
        AutomationStatusIpcProvider.Dispose();
        ChatGui.ChatMessage -= OnChatMessage;
        Framework.Update -= OnFrameworkUpdate;
        FishCollection.Dispose();
        CustomDeliveriesService.Dispose();
        ChocoboStablesService.Cancel();
        DeliveryFishing.Dispose();
        VerminionService.Dispose();
        ClientState.Login -= OnLoginEvent;
        ConfigManager.OnCharacterChanged -= OnCharacterChanged;

        PluginInterface.UiBuilder.Draw -= DrawUi;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

        WindowSystem.RemoveAllWindows();
        ConfigWindow.Dispose();
        MainWindow.Dispose();
        uiFonts.Dispose();
        uiText.Dispose();
        shapedText?.Dispose();

        ARPostProcessService.Dispose();
        FCBuffService.Dispose();
        FishingService.Dispose();
        FishingRunLifecycle.ForceCleanup("plugin disposal");
        RetainerEquippingService.Cancel();
        AutoRetainerIPC.ReleaseSuppressionIfOwned(force: true);
        YesAlreadyIPC.Dispose();
        VNavmeshIPC.Dispose();
        AlliedSocietyService.Cancel("Plugin disposed");
        GearUpdaterService.Dispose();
        HighestCombatJobService.Dispose();
        CurrentJobEquipmentService.Dispose();

        dtrEntry?.Remove();

        CommandManager.RemoveHandler(AliasCommandName);
        CommandManager.RemoveHandler(CommandName);

        ECommonsMain.Dispose();
    }

    private AutomationStatus BuildAutomationStatus()
    {
        var fishingCleanup = FishCollection.IsCleanupPending || FishingRunLifecycle.IsCleanupPending ||
                             FishingService.State is FishingService.FishingState.HandlingResult
                                 or FishingService.FishingState.WaitingForCleanupReady
                                 or FishingService.FishingState.NavigatingToCleanupVendor
                                 or FishingService.FishingState.RunningInventoryCleanup
                                 or FishingService.FishingState.Returning
                                 or FishingService.FishingState.CleaningUpLifecycle;
        var fishingRelog = FishingRelogCoordinator.IsActive ||
                           FishingStartupCoordinator.HasPendingRelogContinuation;
        var fishing = FishingRunLifecycle.IsActive || FishingService.IsActive;
        var beforeAutoRetainer = BeforeArGate is BeforeArGateState.Armed
            or BeforeArGateState.WaitingForWorldReady
            or BeforeArGateState.Running;
        var suppressionRecovery = BeforeArGate == BeforeArGateState.ReleasePending ||
                                  releaseOnlyPostprocessFinishPending ||
                                  (!beforeAutoRetainer && AutoRetainerIPC.SuppressionOwnedByVermaxion);
        var manual = GetActiveManualService();
        var postprocessPending = ARPostProcessService.IsRequested && !ARPostProcessService.IsProcessing;

        var state = fishingRelog
            ? FishingRelogCoordinator.IsActive
                ? FishingRelogCoordinator.StatusText
                : $"RelogPending:{FishingStartupCoordinator.PendingRelogCharacterKey}"
            : postprocessPending
                ? "Requested"
            : fishingCleanup || fishing
                ? FishingService.State.ToString()
                : beforeAutoRetainer
                    ? BeforeArGate.ToString()
                    : suppressionRecovery
                        ? "Recovery"
                        : Engine.OwnsLiveWork
                            ? Engine.State.ToString()
                            : manual.State;
        var summary = postprocessPending
            ? "VERMAXION requested AutoRetainer character postprocessing."
            : fishingRelog || fishingCleanup || fishing
            ? FishingRunStatusText
            : beforeAutoRetainer
                ? BeforeArStatusText
                : suppressionRecovery
                    ? GetDtrOperationalStatus() ?? "VERMAXION suppression or cleanup recovery is active."
                    : Engine.OwnsLiveWork
                        ? Engine.StatusText
                        : manual.Summary;

        return AutomationStatusPolicy.Evaluate(
            new AutomationOwnershipSnapshot
            {
                FishingRelogActive = FishingRelogCoordinator.IsActive,
                FishingRelogPending = FishingStartupCoordinator.HasPendingRelogContinuation,
                FishingActive = fishing,
                FishingCleanupActive = fishingCleanup,
                BeforeAutoRetainerActive = beforeAutoRetainer,
                SuppressionRecoveryActive = suppressionRecovery,
                EngineOwnsLiveWork = Engine.OwnsLiveWork,
                ManualServiceActive = manual.Active,
                CharacterPostprocessRequested = postprocessPending,
                State = state,
                Summary = summary,
            },
            DateTime.UtcNow);
    }

    private void YieldUnstartedBeforeArGateToDad()
    {
        var snapshot = BuildDadHandoffBeforeArYieldSnapshot();
        if (!DadHandoffBeforeArYieldPolicy.ShouldYield(snapshot))
            return;

        var previousGate = BeforeArGate;
        var hadPendingLogin = pendingBeforeArLogin;
        var suppressionOwnedBefore = AutoRetainerIPC.SuppressionOwnedByVermaxion;
        SkipBeforeArForLogin("Yielded unstarted Before-AR work to active DAD handoff reservation");
        if (System.Threading.Interlocked.Exchange(ref loggedDadBeforeArYield, 1) == 0)
        {
            Log.Information(
                "[DAD handoff] Yielded unstarted Before-AR gate to DAD: gate={Gate}, loginPending={LoginPending}, suppressionOwnedBefore={SuppressionOwnedBefore}, suppressionReleasePending={SuppressionReleasePending}; external suppression was not touched.",
                previousGate,
                hadPendingLogin,
                suppressionOwnedBefore,
                AutoRetainerIPC.SuppressionOwnedByVermaxion);
        }
    }

    private DadHandoffBeforeArYieldSnapshot BuildDadHandoffBeforeArYieldSnapshot()
    {
        var manual = GetActiveManualService();
        var fishingOwnsWork = FishingRunLifecycle.IsActive ||
                              FishingRunLifecycle.IsCleanupPending ||
                              FishingService.IsActive ||
                              FishingRelogCoordinator.IsActive ||
                              FishingStartupCoordinator.HasPendingRelogContinuation ||
                              FishingService.State is FishingService.FishingState.HandlingResult
                                  or FishingService.FishingState.WaitingForCleanupReady
                                  or FishingService.FishingState.NavigatingToCleanupVendor
                                  or FishingService.FishingState.RunningInventoryCleanup
                                  or FishingService.FishingState.Returning
                                  or FishingService.FishingState.CleaningUpLifecycle;
        return new DadHandoffBeforeArYieldSnapshot(
            DadHandoffBlocksNewWork,
            BeforeArGate,
            pendingBeforeArLogin,
            Engine.OwnsActiveWork,
            fishingOwnsWork,
            manual.Active,
            ARPostProcessService.IsRequested,
            releaseOnlyPostprocessFinishPending);
    }

    private (bool Active, string State, string Summary) GetActiveManualService()
    {
        if (FisherGearsetTestService.IsActive)
            return (true, "FisherGearsetTest", FisherGearsetTestService.StatusText);
        if (LootGoblinMapGatherManualRunCoordinator.IsActive)
            return (true, LootGoblinMapGatherService.State.ToString(), LootGoblinMapGatherService.StatusText);
        if (FCBuffService.IsActive)
            return (true, FCBuffService.State.ToString(), FCBuffService.StatusText);
        if (FCBuffInventoryService.IsActive)
            return (true, FCBuffInventoryService.State.ToString(), FCBuffInventoryService.StatusText);
        if (VendorStockService.IsActive)
            return (true, VendorStockService.State.ToString(), VendorStockService.StatusText);
        if (RegisterRegistrablesService.IsActive)
            return (true, RegisterRegistrablesService.State.ToString(), $"Register Registrables: {RegisterRegistrablesService.StatusText}");
        if (RetainerListingRefillService.IsActive)
            return (true, "RetainerListingRefill", RetainerListingRefillService.StatusText);
        if (WorkshopBellService.IsActive)
            return (true, "WorkshopBell", WorkshopBellService.StatusText);
        if (SeasonalGearService.IsActive)
            return (true, "SeasonalGear", SeasonalGearService.StatusText);
        if (MinionRouletteService.IsActive)
            return (true, "MinionRoulette", MinionRouletteService.StatusText);
        if (GearUpdaterService.IsActive)
            return (true, "GearUpdater", GearUpdaterService.StatusText);
        if (VerminionService.IsActive)
            return (true, VerminionService.State.ToString(), VerminionService.StatusText);
        if (CactpotService.IsActive)
            return (true, CactpotService.State.ToString(), CactpotService.StatusText);
        if (FashionReportService.IsActive)
            return (true, FashionReportService.State.ToString(), $"Fashion Report: {FashionReportService.State}");
        if (CustomDeliveriesService.IsActive)
            return (true, "CustomDeliveries", CustomDeliveriesService.StatusText);
        if (ChocoboStablesService.IsActive)
            return (true, "ChocoboStables", ChocoboStablesService.StatusText);
        if (ChocoboRaceService.IsActive)
            return (true, ChocoboRaceService.State.ToString(), ChocoboRaceService.StatusText);
        if (CurrentJobEquipmentService.IsActive)
            return (true, CurrentJobEquipmentService.State.ToString(), CurrentJobEquipmentService.StatusText);
        if (AlliedSocietyService.IsActive || AlliedSocietyService.OwnsRotation)
            return (true, AlliedSocietyService.State.ToString(), AlliedSocietyService.StatusText);
        if (AfterArParkService.IsActive)
            return (true, "AfterArPark", AfterArParkService.StatusText);
        if (HighestCombatJobService.IsActive)
            return (true, "HighestCombatJob", HighestCombatJobService.StatusText);

        return (false, "Idle", "VERMAXION is idle.");
    }

    internal bool RunDashboardAction(Action action)
    {
        if (VerminionService.HasQuestAcquisition)
        {
            ChatGui.Print("[Vermaxion] Questionable owns minion acquisition. Wait for it to finish or use FULL STOP.");
            return false;
        }
        var engineWasRunningBefore = Engine.IsRunning;
        var fishingLifecycleActiveBefore = FishingRunLifecycle.IsActive;

        var yesAlreadyWasPaused = YesAlreadyIPC.IsPaused;
        YesAlreadyIPC.Pause();
        if (!yesAlreadyWasPaused)
        {
            if (!YesAlreadyIPC.IsPaused)
            {
                Log.Warning("[Dashboard] Run action blocked because VERMAXION could not pause YesAlready.");
                return false;
            }

            dashboardRunYesAlreadyPauseOwned = true;
        }

        try
        {
            action();
        }
        finally
        {
            if (dashboardRunYesAlreadyPauseOwned &&
                ((!engineWasRunningBefore && Engine.IsRunning) ||
                 (!fishingLifecycleActiveBefore && FishingRunLifecycle.IsActive)))
            {
                dashboardRunYesAlreadyPauseOwned = false;
            }

            ReleaseDashboardRunYesAlreadyPauseIfIdle();
        }

        return true;
    }

    internal string? VerminionQuestHandoffBlocker()
    {
        // Local duty ownership is independent of DAD's network coordinator.
        try
        {
            return PluginInterface.GetIpcSubscriber<bool>("dad.Duty.IsStopped").InvokeFunc()
                ? null : "DAD already owns a duty.";
        }
        catch
        {
            return "Enable DAD before starting Questionable minion acquisition; its duty handoff is unavailable.";
        }
    }

    internal void AcquireVerminionMinion(ushort minionId)
    {
        // No parent engine/manual task or AR suppression may survive the handoff.
        // Do not make the ownership IPC lie to admit a child duty.
        var automation = BuildAutomationStatus();
        var blocker = !characterRegistrationCompletedThisLogin ? "Waiting for character registration." :
            automation.IsBusy ? automation.Summary :
            DadHandoffBlocksNewWork ? "DAD owns the character." :
            OfflineLogoutBlocksOrdinaryAutomation ? "An offline logout hold is active." :
            VerminionService.HasQuestAcquisition ? "A minion acquisition handoff is already reserved." :
            AutoRetainerIPC.IsBusy() ? "AutoRetainer is busy or its state is unavailable." :
            pendingDebugTaskId != null || pendingDebugDispatchTaskId != null ? "A reload action is pending." : null;
        if (blocker != null)
        {
            ChatGui.Print($"[Vermaxion] Minion acquisition unavailable: {blocker}");
            Log.Warning($"[Verminion] Minion acquisition unavailable: {blocker}");
            return;
        }
        try
        {
            var multi = AutoRetainerIPC.ReadMultiModeEnabled();
            if (!multi.Success || multi.Enabled)
                throw new InvalidOperationException("AutoRetainer multi mode must be confirmed off for the quest handoff.");
            if (VerminionRoster.AchievementOffers.Any(offer => offer.MinionId == minionId))
            {
                RunDashboardAction(() => VerminionService.AcquireAchievementMinion(minionId));
                return;
            }
            if (VerminionQuestHandoffBlocker() is { } dutyBlocker)
                throw new InvalidOperationException(dutyBlocker);
            VerminionService.AcquireMinion(minionId);
        }
        catch (Exception ex)
        {
            ChatGui.Print($"[Vermaxion] Minion acquisition unavailable: {ex.Message}");
        }
    }

    internal void SetDebugTaskSelection(string? taskId)
    {
        if (string.Equals(Configuration.DebugTaskId, taskId, StringComparison.Ordinal))
            return;

        Configuration.DebugTaskId = taskId;
        Configuration.DebugTaskCharacterKey = taskId == null ? null : ConfigManager.CurrentCharacterKey;
        // Edits arm only the next reload, and cancel any older pending selection.
        pendingDebugTaskId = null;
        pendingDebugDispatchTaskId = null;
        Configuration.Save();
        SetDebugTaskStatus(taskId == null ? "No task selected." : "Pending: next plugin reload.");
    }

    private void SetDebugTaskStatus(string status)
    {
        DebugTaskStatus = status;
        Log.Information($"[DebugReload] marker={DebugAttemptMarker}; task={Configuration.DebugTaskId ?? "none"}; {status}");
    }

    private void ProcessPendingDebugTask()
    {
#if DEBUG
        ProcessCollectionDebugStop();
#endif
        if (pendingDebugDispatchTaskId != null)
        {
            if (PlayerState.ContentId != debugDispatchContentId)
            {
                pendingDebugDispatchTaskId = null;
                SetDebugTaskStatus("Cancelled: the character changed during cleanup.");
                return;
            }
            var ready = !ChocoboStablesService.IsCleanupPending && (GameHelpers.IsPlayerAvailable() || pendingDebugDispatchTaskId == AutomationCatalog.ChocoboRacing && CanStartChocoboProgression(out _) ||
                pendingDebugDispatchTaskId == AutomationCatalog.VerminionQueue && CanObserveVerminionForReload());
            if (pendingDebugDispatchTaskId == "Run##FishCollection")
            {
                ready &= CanStartMainMenuTest(false, out _);
                ready &= !FishCollection.IsActive && !FishingRunLifecycle.IsActive &&
                    VNavmeshIPC.TryGetPathIsRunning(out var running) && !running &&
                    VNavmeshIPC.TryGetPathfindInProgress(out var pathfinding) && !pathfinding &&
                    LifestreamIPC.TryReadBusy(out var busy) && !busy;
            }
            if (!ready && DateTime.UtcNow < debugDispatchReadyDeadline) return;
            var dispatchId = pendingDebugDispatchTaskId;
            pendingDebugDispatchTaskId = null;
            if (!ready)
            { SetDebugTaskStatus("Blocked: cleanup did not release the character for dispatch."); return; }
            DispatchDebugTask(dispatchId);
            return;
        }
        if (pendingDebugTaskId == null)
            return;

        if (!characterRegistrationCompletedThisLogin)
        {
            if (!string.IsNullOrEmpty(characterRegistrationFailureReason))
            {
                pendingDebugTaskId = null;
                SetDebugTaskStatus("Blocked: character registration failed; see the [Config] log.");
            }
            else if (pendingDebugTaskId == "Run##FishCollection")
            {
                var waiting = $"Pending: character registration; loggedIn={ClientState.IsLoggedIn}; localPlayer={ObjectTable.LocalPlayer != null}; casting={ObjectTable.LocalPlayer?.IsCasting == true}; combat={Condition[ConditionFlag.InCombat]}; questEvent={Condition[ConditionFlag.OccupiedInQuestEvent]}; cutscene={Condition[ConditionFlag.OccupiedInCutSceneEvent]}; betweenAreas={Condition[ConditionFlag.BetweenAreas] || Condition[ConditionFlag.BetweenAreas51]}; dialogs={string.Join(",", new[] { "Talk", "SelectString", "SelectIconString", "SelectYesno", "Shop" }.Where(GameHelpers.IsAddonVisible))}";
                if (DebugTaskStatus != waiting) SetDebugTaskStatus(waiting);
            }
            return;
        }

        // Consume before cleanup or dispatch, including failures and reentrant callbacks.
        var taskId = pendingDebugTaskId;
        pendingDebugTaskId = null;
        if (!string.IsNullOrEmpty(Configuration.DebugTaskCharacterKey) &&
            Configuration.DebugTaskCharacterKey != ConfigManager.CurrentCharacterKey)
        { SetDebugTaskStatus("Blocked: this reload task belongs to a different character."); return; }
        if (VerminionService.HasQuestAcquisition)
        {

            SetDebugTaskStatus("Questionable owns minion acquisition; this reload only observes the saved handoff.");
            return;
        }
        if (taskId == AutomationCatalog.ChocoboRacing && ConfigManager.GetActiveConfig().ChocoboProgressionPaused)
        {
            SetDebugTaskStatus("Chocobo progression is paused; explicit Resume is required before reload continuation.");
            return;
        }
        if (taskId == AutomationCatalog.VerminionQueue && ConfigManager.GetActiveConfig().VerminionPaused)
        {
            var progress = ConfigManager.GetActiveConfig().VerminionProgress;
            Log.Information($"[Verminion] Paused reload observation: duty={VerminionGameInteraction.CurrentCpuDutyId()}; result={VerminionGameInteraction.ReadBattleOutcome()}; pending={progress.PendingMatch}; matches={progress.WeeklyMatches}; wins={progress.WeeklyWins}; no admission or credit.");
            SetDebugTaskStatus("Verminion is paused; explicit Run/Resume is required before reload continuation.");
            return;
        }
        try
        {
            if (taskId == AutomationCatalog.ChocoboRacing)
                CommandManager.ProcessCommand("/chokeabo inspect");
            FullStop(preparingDebugTask: true);
            if (taskId == AutomationCatalog.ChocoboStables)
                ChocoboStablesService.CleanupReloadMenu(ConfigManager.GetActiveConfig().ChocoboStablesSettings);
            pendingDebugDispatchTaskId = taskId;
            debugDispatchContentId = PlayerState.ContentId;
            debugDispatchReadyDeadline = DateTime.UtcNow.AddSeconds(30);
            SetDebugTaskStatus("Consumed: cleanup sent; waiting for character readiness before dispatch.");
        }
        catch (Exception ex)
        {
            SetDebugTaskStatus($"Blocked: reload cleanup failed. {ex.Message}");
        }
    }

    private void DispatchDebugTask(string taskId)
    {
        try
        {
            var row = MainWindow.GetDashboardTaskRows(forceRefresh: true)
                .FirstOrDefault(candidate => string.Equals(candidate.Id, taskId, StringComparison.Ordinal));
            if (row == null)
            {
                SetDebugTaskStatus("Blocked: the saved task is no longer in the dashboard.");
                return;
            }

            if (row.DebugBlockedReason is { } reason)
            {
                SetDebugTaskStatus($"Blocked: {row.Task}. {reason}");
                return;
            }

            Action selectedAction = taskId == AutomationCatalog.VerminionQueue ? VerminionService.ResumeTask : row.OnClick;
#if DEBUG
            if (taskId == "Run##FishCollection" &&
                string.Equals(PluginInterface.AssemblyLocation.FullName, ExpectedDebugPluginPath, StringComparison.OrdinalIgnoreCase))
                selectedAction = () =>
                {
                    if (string.Equals(DebugCollectionStopMilestone, "Cleanup", StringComparison.Ordinal))
                    {
                        SetDebugTaskStatus($"Skipped: cleanup-only reload for the requested pause; {FishCollection.DebugStopState}.");
                        return;
                    }
                    Configuration.FishCollection.PinnedItemId = DebugCollectionTargetItemId;
                    Configuration.Save();
                    FishCollection.Start(DebugCollectionTargetItemId);
                };
#endif
            var dispatched = RunDashboardAction(selectedAction);
            SetDebugTaskStatus(dispatched
                ? $"Dispatched: {row.Task}. Check its existing task status for progress."
                : $"Blocked: {row.Task}. VERMAXION could not pause YesAlready.");
#if DEBUG
            if (dispatched && taskId == "Run##FishCollection" && FishCollection.IsActive &&
                string.Equals(PluginInterface.AssemblyLocation.FullName, ExpectedDebugPluginPath, StringComparison.OrdinalIgnoreCase))
            {
                debugCollectionStopArmed = true;
                debugCollectionStopDeadline = DateTime.UtcNow + DebugCollectionRunLimit;
                SetDebugTaskStatus(DebugCollectionStopMilestone == "Caught"
                    ? $"Automatic Stop armed: native target caught; item={DebugCollectionTargetItemId}; ordinary casting continues until credit."
                    : $"Automatic Stop armed: {DebugCollectionStopMilestone}; bound={DebugCollectionRunLimit.TotalMinutes:g} minutes.");
            }
#endif
        }
        catch (Exception ex)
        {
            SetDebugTaskStatus($"Blocked: reload attempt failed. {ex.Message}");
        }
    }

#if DEBUG
    private void ProcessCollectionDebugStop()
    {
        if (debugCollectionStopPending)
        {
            if (FishCollection.IsActive || FishingRunLifecycle.IsActive || FishingService.HasPendingCollectionCleanup ||
                !GameHelpers.IsPlayerAvailable() || Condition[ConditionFlag.BetweenAreas] || Condition[ConditionFlag.BetweenAreas51] ||
                !VNavmeshIPC.TryGetPathIsRunning(out var running) || running ||
                !VNavmeshIPC.TryGetPathfindInProgress(out var pathfinding) || pathfinding ||
                !LifestreamIPC.TryReadBusy(out var busy) || busy) return;
            debugCollectionStopPending = false;
            SetDebugTaskStatus("Automatic Stop settled: collection, lifecycle, navigation and travel released; ready for DLL overwrite.");
            return;
        }
        if (!debugCollectionStopArmed) return;
        if (!FishCollection.IsActive || !FishCollection.Running)
        {
            debugCollectionStopArmed = false;
            debugCollectionStopPending = true;
            return;
        }
        var milestone = FishCollection.DebugStopReady(DebugCollectionStopMilestone);
        if (!milestone && (DebugCollectionStopMilestone == "Caught" || DateTime.UtcNow < debugCollectionStopDeadline)) return;
        debugCollectionStopArmed = false;
        SetDebugTaskStatus($"Automatic Stop requested: {(milestone ? DebugCollectionStopMilestone : "run limit")}; {FishCollection.DebugStopState}.");
        try { FullStop(); }
        finally { debugCollectionStopPending = true; }
    }
#endif

    private void ReleaseDashboardRunYesAlreadyPauseIfIdle()
    {
        if (!dashboardRunYesAlreadyPauseOwned || GetActiveManualService().Active)
            return;

        dashboardRunYesAlreadyPauseOwned = false;
        YesAlreadyIPC.Unpause();
    }

    private void OnChatMessage(IChatMessage message)
    {
        CactpotService.HandleChatMessage(
            message.LogKind.ToString(),
            message.Sender.TextValue,
            message.Message.TextValue);
        FishingService.HandleChatMessage(
            message.LogKind.ToString(),
            message.Sender.TextValue,
            message.Message.TextValue);
    }

    private void OnARCharacterReady(string pluginName)
    {
        Log.Information($"[Plugin] AR signaled character ready for postprocess");
        if (IsScheduledOfflineHoldActive)
        {
            const string reason = "Scheduled Ocean Fishing wake owns this bootstrap login";
            pendingFishingPostprocessHandoff = false;
            FinishReleaseOnlyPostprocess(reason);
            ReleaseOwnedSuppressionAfterSkippedPostprocess(reason);
            Log.Information($"[Fishing][OfflineHold] Suppressed ordinary AutoRetainer postprocess: {reason}");
            return;
        }

        pendingFishingPostprocessHandoff = true;
    }

    private bool ProcessPendingFishingPostprocessHandoff()
    {
        if (FishCollection.IsActive)
        {
            if (pendingFishingPostprocessHandoff)
            { pendingFishingPostprocessHandoff = false; FinishReleaseOnlyPostprocess("Fish collection owns this run"); }
            return true;
        }
        if (!pendingFishingPostprocessHandoff)
            return false;

        if (LifestreamIPC.IsBusy())
            return true;

        pendingFishingPostprocessHandoff = false;

        if (!Configuration.Enabled)
        {
            const string reason = "Global automation is disabled";
            Engine.RecordSkippedOpportunity($"Postprocess skipped: {reason}");
            FinishReleaseOnlyPostprocess(reason);
            ReleaseOwnedSuppressionAfterSkippedPostprocess(reason);
            return true;
        }

        var fishingStartup = RunFishingStartupTrigger(FishingStartupTrigger.AutoRetainerPostprocess);
        if (fishingStartup.ClaimsStartup)
        {
            Engine.RecordSkippedOpportunity($"Ocean Fishing startup: {fishingStartup.Reason}");

            // Relog startup owns the AR release steps itself. Current-character
            // startup has no relog sequence, so release the AR handoff directly.
            if (!FishingRelogCoordinator.IsActive)
            {
                FinishReleaseOnlyPostprocess(fishingStartup.Reason);
                ReleaseOwnedSuppressionAfterSkippedPostprocess(fishingStartup.Reason);
            }

            return true;
        }

        Engine.StartPostProcess();
        return true;
    }

    public void RunFishingStartupManual()
    {
        FishingStartupCoordinator.ResetCurrentWindow(DateTimeOffset.UtcNow, clearPendingRelogContinuation: false);
        var result = RunFishingStartupTrigger(FishingStartupTrigger.Manual);
        ChatGui.Print($"[Vermaxion] Fishing startup: {result.Reason}");
    }

    public void RunFishingStartupTest()
    {
        var result = FishingStartupCoordinator.StartTest(DateTimeOffset.UtcNow);
        if (result.Started)
            Log.Information(FishingStartupDiagnostics.FormatStarted(result));
        else
            Log.Information($"[Fishing][Startup] trigger={result.Trigger}, action={result.Action}, reason={result.Reason}");
        ChatGui.Print($"[Vermaxion] Test fishing startup: {result.Reason}");
    }

    public void RunFishingGearsetTest()
    {
        if (DadHandoffBlocksNewWork || Engine.IsRunning || IsFishingRunActive)
        {
            const string message = "Fisher gearset test is unavailable while DAD handoff, engine, fishing, or relog work is active.";
            Log.Warning($"[Fishing][GearsetTest] {message}");
            ChatGui.Print($"[Vermaxion] {message}");
            return;
        }

        if (!FisherGearsetTestService.Start())
            ChatGui.Print("[Vermaxion] Fisher gearset test is already active.");
    }

    internal bool CanStartMainMenuTest(bool waitForOceanFishing, out string reason)
    {
        if (VerminionService.HasQuestAcquisition)
        { reason = "Questionable owns minion acquisition."; return false; }
        if (!ClientState.IsLoggedIn)
        {
            reason = "A character must be logged in.";
            return false;
        }

        if (DadHandoffBlocksNewWork)
        {
            reason = "A granted or pending DAD handoff reservation blocks new work.";
            return false;
        }

        if (ScheduledOfflineHoldCoordinator.IsActive)
        {
            reason = "Offline-hold or main-menu test work is already active.";
            return false;
        }

        if (Engine.OwnsLiveWork ||
            ARPostProcessService.IsRequested ||
            pendingFishingPostprocessHandoff ||
            pendingBeforeArLogin ||
            BeforeArGate is BeforeArGateState.Armed
                or BeforeArGateState.WaitingForWorldReady
                or BeforeArGateState.Running
                or BeforeArGateState.ReleasePending ||
            releaseOnlyPostprocessFinishPending ||
            AutoRetainerIPC.IsBusy())
        {
            reason = "VERMAXION engine or AutoRetainer postprocess work is active.";
            return false;
        }

        if (IsFishingRunActive ||
            FishingStartupCoordinator.HasRecoveryPending ||
            FishingRelogCoordinator.IsFailed)
        {
            reason = "Fishing, relog, or fishing recovery work is active.";
            return false;
        }

        var manual = GetActiveManualService();
        if (manual.Active)
        {
            reason = $"Existing test or manual work is active: {manual.Summary}";
            return false;
        }

        if (waitForOceanFishing && !Configuration.Enabled)
        {
            reason = "Global automation must be enabled for the full-cycle action.";
            return false;
        }

        if (waitForOceanFishing && !Configuration.LogoutBetweenScheduledOceanFishingVoyages)
        {
            reason = "Enable 'Log out between scheduled Ocean Fishing voyages' in Config > Global Settings > Fishing.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    internal bool CanStartJumboRouteTest(out string reason)
    {
        if (!CanStartMainMenuTest(waitForOceanFishing: false, out reason))
            return false;

        if (!GameHelpers.IsPlayerAvailable() || Condition[ConditionFlag.BoundByDuty] ||
            Condition[ConditionFlag.BoundByDuty56] || Condition[ConditionFlag.BoundByDuty95])
        {
            reason = "A character must be available outside a duty.";
            return false;
        }

        if (!LifestreamIPC.TryReadBusy(out var travelBusy) || travelBusy ||
            !VNavmeshIPC.TryGetPathIsRunning(out var navigationRunning) || navigationRunning ||
            !VNavmeshIPC.TryGetPathfindInProgress(out var pathfinding) || pathfinding)
        {
            reason = "Travel owns the character.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    internal void RunJumboRouteTest(bool cashier)
    {
        if (!CanStartJumboRouteTest(out var reason))
        {
            ChatGui.Print($"[Vermaxion] {reason}");
            return;
        }

        RunDashboardAction(() => CactpotService.StartJumboRouteTest(cashier));
    }

    internal void GoToMainMenu()
    {
        if (!CanStartMainMenuTest(waitForOceanFishing: false, out var reason) ||
            !ScheduledOfflineHoldCoordinator.BeginHumanLogoutOnly(DateTimeOffset.UtcNow, out reason))
        {
            Log.Warning($"[Fishing][OfflineHold] Human main-menu action rejected: {reason}");
            ChatGui.Print($"[Vermaxion] Main-menu action unavailable: {reason}");
            return;
        }

        ChatGui.Print("[Vermaxion] Logging out to the main menu; no automatic wake will be requested.");
    }

    internal void GoToMainMenuAndWaitForOceanFishing()
    {
        if (!CanStartMainMenuTest(waitForOceanFishing: true, out var reason) ||
            !ScheduledOfflineHoldCoordinator.BeginHumanWaitForNextGate(
                DateTimeOffset.UtcNow,
                Configuration.OceanFishingPreWindowOffsetMinutes,
                out reason))
        {
            Log.Warning($"[Fishing][OfflineHold] Human full-cycle action rejected: {reason}");
            ChatGui.Print($"[Vermaxion] Full-cycle action unavailable: {reason}");
            return;
        }

        var hold = Configuration.ScheduledOfflineHold;
        ChatGui.Print(
            hold == null
                ? "[Vermaxion] Ocean Fishing offline hold started."
                : $"[Vermaxion] Logging out; scheduled wake is {hold.WakeAtUtc:u}.");
    }

    public void ResetFishingStartupGate()
    {
        FishingStartupCoordinator.ResetCurrentWindow(DateTimeOffset.UtcNow);
        Log.Information("[Fishing][Startup] Startup gate reset by user");
        ChatGui.Print("[Vermaxion] Fishing startup gate reset.");
    }

    private FishingStartupResult RunFishingStartupTrigger(
        FishingStartupTrigger trigger,
        int? preWindowOffsetMinutes = null)
    {
        if (FishCollection.IsActive)
            return new FishingStartupResult(trigger, FishingRunMode.Collection, FishingStartupAction.Waiting, false,
                null, FishingSelectionResult.None("Fish collection owns this run"), "Fish collection owns this run");
        var result = FishingStartupCoordinator.Poll(
            DateTimeOffset.UtcNow,
            trigger,
            preWindowOffsetMinutes);
        if (result.Started)
        {
            Log.Information(FishingStartupDiagnostics.FormatStarted(result));
        }
        else if (trigger is FishingStartupTrigger.Manual or FishingStartupTrigger.AutoRetainerPostprocess)
        {
            Log.Information($"[Fishing][Startup] trigger={trigger}, action={result.Action}, reason={result.Reason}");
        }

        if (trigger != FishingStartupTrigger.AutoRetainerPostprocess &&
            result.Action == FishingStartupAction.FishingStarted &&
            ARPostProcessService.IsProcessing)
        {
            FinishReleaseOnlyPostprocess(result.Reason);
            ReleaseOwnedSuppressionAfterSkippedPostprocess(result.Reason);
        }

        return result;
    }

    private void ApplyLegacyFishingOperationSettingsIfNeeded()
    {
        if (Configuration.FishingCharacterSettingsMigrated ||
            !Configuration.TryGetLegacyFishingOperationSettings(out var settings))
        {
            return;
        }

        var migratedCount = ConfigManager.ApplyFishingOperationSettingsToAllAccounts(settings);
        if (migratedCount == 0)
            return;

        Configuration.FishingCharacterSettingsMigrated = true;
        Configuration.ClearLegacyFishingOperationSettings();
        Configuration.Save();
        Log.Information($"[Fishing] Migrated legacy global fishing operation settings to {migratedCount} per-character config record(s)");
    }

    private void ApplyFishingStockCatalogMigrationIfNeeded()
    {
        Configuration.FishingStockCatalog ??= FishingStockCatalogPolicy.CreateDefaultCatalog();
        var changed = FishingStockCatalogPolicy.NormalizeCatalog(Configuration.FishingStockCatalog);
        if (!Configuration.FishingStockCatalogMigrated)
        {
            var migratedCount = ConfigManager.MigrateFishingStockCatalog(Configuration.FishingStockCatalog);
            Configuration.FishingStockCatalogMigrated = true;
            changed = true;
            Log.Information($"[Fishing] Migrated ordered stock settings to {migratedCount} account default/character record(s)");
        }

        // A saved catalog predates rows added to CreateDefaultCatalog later (e.g. Lentils 4674 for
        // eat-on-boat), so append any missing default rows — idempotent: AddFishingStock-
        // CatalogEntry refuses ids already present. Rows arrive with their catalog default enablement
        // (Lentils: disabled) and are switched on per profile via EnableFishingStockRow.
        foreach (var def in FishingStockCatalogPolicy.CreateDefaultCatalog())
        {
            if (ConfigManager.AddFishingStockCatalogEntry(
                    Configuration, def.ItemId, def.DefaultTarget, def.DefaultEnabled, def.DefaultMin))
            {
                changed = true;
                Log.Information($"[Fishing] Stock catalog: appended new default row item={def.ItemId} enabled={def.DefaultEnabled}");
            }
        }

        if (changed)
            Configuration.Save();
    }

    /// <summary>Bridge-friendly per-row stock enablement: flips ONE catalog row's default enablement and
    /// syncs just that row to the current account and all its characters (never the whole catalog — a
    /// full sync would clobber existing per-character bait enables back to catalog defaults). target/min &lt; 0 keep
    /// the row's existing values.</summary>
    public string EnableFishingStockRow(uint itemId, bool enabled, int target = -1, int min = -1)
    {
        // Refuse without a current account: the sync below would touch 0 records while the return still
        // read success-shaped, and nothing replays the sync on a later login.
        if (string.IsNullOrEmpty(ConfigManager.CurrentAccountId))
            return "refused: no current account selected — log a character in on this client first";
        var row = Configuration.FishingStockCatalog?.FirstOrDefault(r => r.ItemId == itemId);
        if (row == null)
            return $"no catalog row for item {itemId}";
        row.DefaultEnabled = enabled;
        if (target >= 0)
            row.DefaultTarget = target;
        if (min >= 0)
            row.DefaultMin = min;
        Configuration.Save();
        var synced = ConfigManager.SyncFishingStockRowToCurrentAccount(row);
        return $"item {itemId} enabled={enabled} target={row.DefaultTarget} min={row.DefaultMin} synced to {synced} record(s)";
    }

    private void FinishReleaseOnlyPostprocess(string reason)
    {
        if (ARPostProcessService.FinishPostProcess(mode: ARPostProcessFinishMode.ReleaseOnly))
        {
            releaseOnlyPostprocessFinishPending = false;
            releaseOnlyPostprocessFinishReason = string.Empty;
            return;
        }

        releaseOnlyPostprocessFinishPending = ARPostProcessService.IsProcessing;
        releaseOnlyPostprocessFinishReason = reason;
    }

    private void ProcessReleaseOnlyPostprocessFinishPending()
    {
        if (!releaseOnlyPostprocessFinishPending)
            return;

        if (!ARPostProcessService.IsProcessing)
        {
            releaseOnlyPostprocessFinishPending = false;
            releaseOnlyPostprocessFinishReason = string.Empty;
            return;
        }

        if (!ARPostProcessService.FinishPostProcess(mode: ARPostProcessFinishMode.ReleaseOnly))
            return;

        Log.Information($"[AR] Release-only postprocess finish confirmed after retry: {releaseOnlyPostprocessFinishReason}");
        releaseOnlyPostprocessFinishPending = false;
        releaseOnlyPostprocessFinishReason = string.Empty;
    }

    private void ReleaseOwnedSuppressionAfterSkippedPostprocess(string reason)
    {
        if (!AutoRetainerIPC.SuppressionOwnedByVermaxion)
            return;

        SetBeforeArGate(BeforeArGateState.ReleasePending, reason);
        ProcessBeforeArReleasePending();
    }

    private void OnCharacterChanged(string oldCharacterKey, string newCharacterKey)
    {
        Log.Information($"[Plugin] Character changed: '{oldCharacterKey}' -> '{newCharacterKey}'");
        
        // Reset all services to prevent state persistence between characters
        try
        {
            Log.Information("[Plugin] Resetting all services due to character change");

            LootGoblinMapGatherManualRunCoordinator.Cancel();
            FisherGearsetTestService.Cancel();
            
            FCBuffService.Reset();
            VerminionService.Reset();
            CactpotService.Reset();
            ChocoboRaceService.Reset();
            FashionReportService.Reset();
            VendorStockService.Reset();
            var preserveFishingRun = FishCollection.IsActive || FishingRelogCoordinator.IsActive || FishingStartupCoordinator.HasPendingRelogContinuation;
            FishingService.Reset(releaseRun: !preserveFishingRun);
            if (FishingRelogCoordinator.IsActive)
                FishingRelogCoordinator.NotifyCharacterChanged(newCharacterKey);
            else
                FishingRelogCoordinator.Reset();
            RetainerListingRefillService.Reset();
            WorkshopBellService.Reset();
            RegisterRegistrablesService.Reset();
            MinionRouletteService.Reset();
            SeasonalGearService.Reset();
            GearUpdaterService.Reset();
            HighestCombatJobService.Reset();
            CurrentJobEquipmentService.Reset();
            AlliedSocietyService.Reset();
            AfterArParkService.Reset();
            
            // Reset engine state if running
            if (Engine.IsRunning)
            {
                Log.Information("[Plugin] Stopping engine due to character change");
                Engine.Stop(userRequested: false);
            }
            else if (pendingBeforeArLogin)
            {
                Log.Information("[Plugin] Preserving AutoRetainer suppression during pending before-AR login resolution");
            }
            else
            {
                Log.Information("[Plugin] Character changed while engine idle; preserving any VMX-owned suppression until Full Stop or a settled handoff.");
            }
            
            Log.Information("[Plugin] All services reset successfully");
        }
        catch (Exception ex)
        {
            Log.Error($"[Plugin] Error resetting services on character change: {ex.Message}");
        }
    }

    private void OnCommand(string command, string args)
    {
        MainWindow.Toggle();
    }

    private void OnAliasCommand(string command, string args)
    {
        var arg = args.Trim().ToLowerInvariant();
        switch (arg)
        {
            case "on":
            case "off":
                if (arg == "off")
                    PauseCurrentTargetCycleBestEffort("VERMAXION global automation disabled");
                Configuration.Enabled = arg == "on";
                Configuration.Save();
                Log.Information($"Vermaxion {(Configuration.Enabled ? "enabled" : "disabled")} globally via /vmx {arg}");
                ChatGui.Print($"[Vermaxion] Globally {(Configuration.Enabled ? "enabled" : "disabled")}");
                break;

            case "run":
                if (Engine.ManualStart())
                {
                    ChatGui.Print("[Vermaxion] Manual run started");
                }
                else
                {
                    ChatGui.Print("[Vermaxion] Already running");
                }
                break;

            case "stop":
                FullStop();
                break;

            case "cancel":
                Engine.Cancel();
                ChatGui.Print("[Vermaxion] Cancelled");
                break;

            case "config":
                ConfigWindow.Toggle();
                break;

            case "chocobo resume":
                RunDashboardAction(() => ChocoboRaceService.ResumeProgression());
                break;

            case "chocobo settings":
                ConfigWindow.OpenAutomationSettings(ConfigurationSection.Daily, AutomationCatalog.ChocoboRacing);
                break;

            case "chocobo pause":
            case "chocobo stop":
                ChocoboRaceService.PauseProgression();
                break;

            case "v":
                VerminionWindow.IsOpen = true;
                break;

            case "debug":
                DebugWindow.Toggle();
                break;

            case "fcpoints":
                Log.Information("[FC POINTS] Testing FC points reading...");
                var fcPoints = GameHelpers.GetFCPointsNode();
                if (fcPoints.HasValue)
                {
                    Log.Information($"[FC POINTS] SUCCESS: FC points = {fcPoints.Value:N0}");
                }
                else
                {
                    Log.Information("[FC POINTS] FAILED: Could not read FC points");
                }
                break;

            default:
                MainWindow.Toggle();
                break;
        }
    }

    private void OnLoginEvent()
    {
        CharacterSelectStallRecovery.NotifyLoginConfirmation();
        beforeArStartedThisLogin = false;
        QueueCharacterRegistration("ClientState.Login");
        BeginBeforeArLoginPending("ClientState.Login");
    }

    private void QueueCharacterRegistration(string reason)
    {
        if (pendingCharacterRegistration || characterRegistrationCompletedThisLogin)
            return;

        pendingCharacterRegistration = true;
        characterRegistrationFailureReason = string.Empty;
        characterRegistrationWorldReadySince = DateTime.MinValue;
        Log.Information($"[Config] Character registration queued: reason={reason}");
    }

    private void ProcessPendingCharacterRegistration()
    {
        if (!pendingCharacterRegistration)
            return;

        if (!ClientState.IsLoggedIn)
        {
            ClearCharacterRegistrationForLogout();
            return;
        }

        if (!TryGetWorldReadyCharacterForRegistration(out var charName, out var worldName, out var contentId, out _))
            return;

        var characterKey = $"{charName}@{worldName}";
        try
        {
            Log.Information($"[Config] Registering logged-in character: character={characterKey}, contentId={contentId:X16}");
            ConfigManager.EnsureAccountSelected(contentId, null, characterKey);
            ConfigManager.EnsureCharacterExists(charName, worldName);
            ApplyLegacyFishingOperationSettingsIfNeeded();
            Configuration.LastAccountId = ConfigManager.CurrentAccountId;
            Configuration.Save();
            ConfigManager.LoadAllAccounts();

            if (!string.Equals(ConfigManager.CurrentCharacterKey, characterKey, StringComparison.OrdinalIgnoreCase))
            {
                characterRegistrationFailureReason = $"Could not persist {characterKey}; account configuration remains fail-closed.";
                pendingCharacterRegistration = false;
                characterRegistrationWorldReadySince = DateTime.MinValue;
                Log.Error($"[Config] Character registration failed: {characterRegistrationFailureReason}");
                return;
            }

            pendingCharacterRegistration = false;
            characterRegistrationCompletedThisLogin = true;
            characterRegistrationWorldReadySince = DateTime.MinValue;
            Log.Information($"[Config] Character registration completed: accountId={ConfigManager.CurrentAccountId}, characterKey='{ConfigManager.CurrentCharacterKey}'");
            if (Configuration.FishCollection.RemoveFishFromAutoRetainerLists)
                FishCollection.ProtectAutoRetainerFish();
            var activeConfig = ConfigManager.GetActiveConfig();
            if (activeConfig.VerminionProgress.QuestAcquisition?.Owner == contentId)
                SkipBeforeArForLogin("Questionable owns the saved minion acquisition; released the unstarted login gate");
            else if (Configuration.Enabled && activeConfig.Enabled)
                AutoRetainerIPC.ConfigureRetainerGilWithdrawal(activeConfig, contentId);
        }
        catch (Exception ex)
        {
            characterRegistrationFailureReason = ex.Message;
            pendingCharacterRegistration = false;
            characterRegistrationWorldReadySince = DateTime.MinValue;
            Log.Error($"[Config] Character registration failed: {ex.Message}");
        }
    }

    private void ClearCharacterRegistrationForLogout()
    {
        pendingCharacterRegistration = false;
        characterRegistrationCompletedThisLogin = false;
        characterRegistrationFailureReason = string.Empty;
        characterRegistrationWorldReadySince = DateTime.MinValue;
    }

    private void BeginBeforeArLoginPendingFromPluginLoad()
    {
        // This also runs before services are constructed; character registration
        // rechecks the saved handoff before any Before-AR work may start.
        if (VerminionService?.HasQuestAcquisition == true)
        { SkipBeforeArForLogin("Questionable owns minion acquisition"); return; }
        if (OfflineLogoutBlocksOrdinaryAutomation)
        {
            SkipBeforeArForLogin("Offline logout work suppresses Before-AR startup");
            return;
        }

        if (!Configuration.Enabled)
        {
            SkipBeforeArForLogin("Global automation is disabled");
            return;
        }

        var configuredCount = GetConfiguredBeforeAutoRetainerTaskCount();
        var suppression = AutoRetainerIPC.GetSuppressionSnapshot();
        var arBusy = AutoRetainerIPC.IsBusy();
        Log.Information($"[AR] Plugin-load before-AR check: configuredBeforeArCount={configuredCount}, suppression={suppression}, arBusy={arBusy}");

        if (configuredCount == 0)
        {
            SkipBeforeArForLogin("No Before-AR tasks configured");
            return;
        }

        if (arBusy || (suppression.RemoteKnown && suppression.RemoteSuppressed))
        {
            Log.Warning($"[AR] Late before-AR skip on plugin load: AutoRetainer already active/queued or suppressed; arBusy={arBusy}, suppression={suppression}. VMX will arm next login from character postprocess handoff.");
            beforeArStartedThisLogin = true;
            SetBeforeArGate(BeforeArGateState.Skipped, "Late plugin load; AutoRetainer already active or suppressed");
            return;
        }

        BeginBeforeArLoginPending("plugin load while logged in");
    }

    private void BeginBeforeArLoginPending(string reason)
    {
        if (VerminionService?.HasQuestAcquisition == true)
        { SkipBeforeArForLogin("Questionable owns minion acquisition"); return; }
        if (OfflineLogoutBlocksOrdinaryAutomation)
        {
            SkipBeforeArForLogin("Offline logout work suppresses Before-AR startup");
            return;
        }

        if (!Configuration.Enabled)
        {
            SkipBeforeArForLogin("Global automation is disabled");
            return;
        }

        if (beforeArStartedThisLogin)
        {
            Log.Information($"[AR] Ignoring before-AR login trigger after start this login: reason={reason}");
            return;
        }

        if (DadHandoffBlocksNewWork)
        {
            var handoff = BuildDadHandoffBeforeArYieldSnapshot();
            if (handoff.GateState == BeforeArGateState.Running ||
                handoff.EngineOwnsActiveWork ||
                handoff.FishingOwnsWork ||
                handoff.ManualServiceOwnsWork ||
                handoff.CharacterPostprocessRequested ||
                handoff.CleanupOwnsWork)
            {
                beforeArStartedThisLogin = true;
                ClearPendingBeforeArLogin("DAD handoff blocks login detection while active VERMAXION work drains");
                Log.Information(
                    "[DAD handoff] Prevented new Before-AR login work while active VERMAXION work drains; existing work and suppression ownership were preserved.");
                return;
            }

            SkipBeforeArForLogin("DAD handoff reservation blocks new Before-AR work on this login");
            return;
        }

        var configuredCount = GetConfiguredBeforeAutoRetainerTaskCount();
        Log.Information($"[AR] Before-AR login trigger: reason={reason}, configuredBeforeArCount={configuredCount}");
        if (configuredCount == 0)
        {
            SkipBeforeArForLogin("No Before-AR tasks configured");
            return;
        }

        if (!beforeArArmedByPostprocess && !AutoRetainerIPC.SuppressionOwnedByVermaxion && AutoRetainerIPC.IsBusy())
        {
            Log.Warning($"[AR] Late before-AR skip on login: AutoRetainer already active/queued; reason={reason}. VMX will arm next login from character postprocess handoff.");
            beforeArStartedThisLogin = true;
            SetBeforeArGate(BeforeArGateState.Skipped, "AutoRetainer already active or queued");
            return;
        }

        if (!pendingBeforeArLogin)
        {
            pendingBeforeArLogin = true;
            beforeArLoginPendingSince = DateTime.UtcNow;
            beforeArLoginLastDiagnosticAt = DateTime.MinValue;
        }

        beforeArTimeoutReleaseReason = string.Empty;
        SetBeforeArGate(BeforeArGateState.WaitingForWorldReady, reason);
        Log.Information($"[AR] Before-AR login pending: reason={reason}, suppression={AutoRetainerIPC.GetSuppressionSnapshot()}");
    }

    private void ArmBeforeArSuppressionFromPostprocess()
    {
        if (VerminionService.HasQuestAcquisition) return;
        if (OfflineLogoutBlocksOrdinaryAutomation)
        {
            beforeArArmedByPostprocess = false;
            ClearBeforeArArmedTracking();
            SetBeforeArGate(BeforeArGateState.Skipped, "Offline logout work suppresses Before-AR startup");
            Log.Information("[Fishing][OfflineHold] Suppressed Before-AR arming during offline logout work.");
            return;
        }

        if (!Configuration.Enabled)
        {
            const string reason = "Global automation is disabled";
            beforeArArmedByPostprocess = false;
            ClearBeforeArArmedTracking();
            if (AutoRetainerIPC.SuppressionOwnedByVermaxion)
            {
                SetBeforeArGate(BeforeArGateState.ReleasePending, reason);
                ProcessBeforeArReleasePending();
            }
            else
            {
                SetBeforeArGate(BeforeArGateState.Skipped, reason);
            }
            Log.Information("[AR] Skipped before-AR arming because global automation is disabled.");
            return;
        }

        if (DadHandoffBlocksNewWork)
        {
            beforeArArmedByPostprocess = false;
            ClearBeforeArArmedTracking();
            SetBeforeArGate(BeforeArGateState.Skipped, "DAD handoff reservation blocks new before-AR work");
            Log.Information("[AR] Skipped before-AR arming because DAD owns a handoff reservation.");
            return;
        }

        var configuredCount = GetConfiguredBeforeAutoRetainerTaskCount();
        var activeConfig = ConfigManager.GetActiveConfig();
        var dueTaskIds = Engine.GetRunnableTaskIdsForPhase(PostProcessTaskPhase.BeforeAR).ToList();
        var multiMode = AutoRetainerIPC.ReadMultiModeEnabled();
        var suppressionBefore = AutoRetainerIPC.GetSuppressionSnapshot();
        var armDecision = BeforeArArmPolicy.Evaluate(
            multiMode.Success,
            multiMode.Enabled,
            activeConfig.Enabled,
            dueTaskIds.Count);
        Log.Information($"[AR] Postprocess before-AR arm check: configuredBeforeArCount={configuredCount}, dueBeforeArTaskIds=[{string.Join(", ", dueTaskIds)}], multiModeRead={multiMode.Success}, multiModeEnabled={multiMode.Enabled}, configEnabled={activeConfig.Enabled}, suppression={suppressionBefore}");

        if (!armDecision.ShouldArm)
        {
            var reason = FormatBeforeArArmSkipReason(armDecision, multiMode);
            Log.Information($"[AR] Postprocess before-AR arm skipped: reason={armDecision.Reason}; detail={reason}");
            beforeArArmedByPostprocess = false;
            ClearBeforeArArmedTracking();
            if (AutoRetainerIPC.SuppressionOwnedByVermaxion)
            {
                SetBeforeArGate(BeforeArGateState.ReleasePending, reason);
                ProcessBeforeArReleasePending();
            }
            else
            {
                SetBeforeArGate(BeforeArGateState.Skipped, reason);
            }
            return;
        }

        var acquired = AutoRetainerIPC.TryAcquireSuppression();
        var suppressionAfter = AutoRetainerIPC.GetSuppressionSnapshot();
        beforeArArmedByPostprocess = AutoRetainerIPC.SuppressionOwnedByVermaxion;
        if (beforeArArmedByPostprocess)
            RecordBeforeArArmed();
        else
            ClearBeforeArArmedTracking();
        SetBeforeArGate(
            beforeArArmedByPostprocess ? BeforeArGateState.Armed : BeforeArGateState.Skipped,
            beforeArArmedByPostprocess ? "Suppression armed for next login" : "Could not acquire suppression");
        Log.Information($"[AR] Postprocess before-AR suppression arm: acquired={acquired}, armedByPostprocess={beforeArArmedByPostprocess}, suppression={suppressionAfter}");
    }

    private void ProcessPendingBeforeArLogin()
    {
        if (FishCollection.IsActive)
        { if (pendingBeforeArLogin) SkipBeforeArForLogin("Fish collection owns selected-character inspection or an opportunity"); return; }
        if (VerminionService.HasQuestAcquisition)
        { if (pendingBeforeArLogin) SkipBeforeArForLogin("Questionable owns minion acquisition"); return; }
        if (!pendingBeforeArLogin)
            return;

        if (OfflineLogoutBlocksOrdinaryAutomation)
        {
            SkipBeforeArForLogin("Offline logout work suppresses Before-AR startup");
            return;
        }

        if (!Configuration.Enabled)
        {
            SkipBeforeArForLogin("Global automation is disabled");
            return;
        }

        if (DadHandoffBlocksNewWork)
            return;

        try
        {
            if (!ClientState.IsLoggedIn)
            {
                ClearPendingBeforeArLogin("logged out");
                return;
            }

            if (beforeArStartedThisLogin)
            {
                ClearPendingBeforeArLogin("before-AR already started this login");
                return;
            }

            var elapsed = DateTime.UtcNow - beforeArLoginPendingSince;
            if (LifecyclePolicy.ShouldSkipBeforeArForTimeout(elapsed, workStarted: false, TimeSpan.FromSeconds(BeforeArLoginTimeoutSeconds)))
            {
                LogPendingBeforeArDiagnostic($"world-ready wait exceeded {BeforeArLoginTimeoutSeconds}s; skipping this login and releasing VMX suppression");
                SkipBeforeArForLogin($"World-ready timeout after {BeforeArLoginTimeoutSeconds}s");
                return;
            }

            if (!TryGetWorldReadyCharacter(out var charName, out var worldName, out var contentId, out var notReadyReason))
            {
                LogPendingBeforeArDiagnostic(notReadyReason);
                return;
            }

            if (!characterRegistrationCompletedThisLogin)
            {
                if (!string.IsNullOrWhiteSpace(characterRegistrationFailureReason))
                {
                    SkipBeforeArForLogin($"Character registration failed: {characterRegistrationFailureReason}");
                }
                else
                {
                    LogPendingBeforeArDiagnostic("waiting for independent character registration");
                }

                return;
            }

            Log.Information($"[AR] Login world-ready resolved: character={charName}@{worldName}, contentId={contentId:X16}");
            var activeConfig = ConfigManager.GetActiveConfig();
            var configuredCount = GetConfiguredBeforeAutoRetainerTaskCount();
            var dueTaskIds = Engine.GetRunnableTaskIdsForPhase(PostProcessTaskPhase.BeforeAR).ToList();
            var suppression = AutoRetainerIPC.GetSuppressionSnapshot();
            var multiMode = AutoRetainerIPC.ReadMultiModeEnabled();
            var armDecision = BeforeArArmPolicy.Evaluate(
                multiMode.Success,
                multiMode.Enabled,
                activeConfig.Enabled,
                dueTaskIds.Count);
            Log.Information($"[AR] Before-AR gate: accountId={ConfigManager.CurrentAccountId}, characterKey='{ConfigManager.CurrentCharacterKey}', configuredBeforeArCount={configuredCount}, dueBeforeArTaskIds=[{string.Join(", ", dueTaskIds)}], multiModeRead={multiMode.Success}, multiModeEnabled={multiMode.Enabled}, suppression={suppression}, configEnabled={activeConfig.Enabled}");

            if (!armDecision.ShouldArm)
            {
                var reason = FormatBeforeArArmSkipReason(armDecision, multiMode);
                Log.Information($"[AR] Before-AR gated off for this login: reason={armDecision.Reason}; detail={reason}");
                SkipBeforeArForLogin(reason);
                return;
            }

            var acquiredSuppression = AutoRetainerIPC.TryAcquireSuppression();
            suppression = AutoRetainerIPC.GetSuppressionSnapshot();
            Log.Information($"[AR] Before-AR world-ready suppression acquire: acquired={acquiredSuppression}, suppression={suppression}");
            if (!acquiredSuppression || !AutoRetainerIPC.SuppressionOwnedByVermaxion)
            {
                Log.Warning($"[AR] Skipping before-AR tasks because VMX could not acquire AutoRetainer suppression after world-ready gate; suppression={suppression}.");
                SkipBeforeArForLogin("Suppression not owned by VMX");
                return;
            }

            beforeArStartedThisLogin = true;
            beforeArArmedByPostprocess = false;
            ClearBeforeArArmedTracking();
            ClearPendingBeforeArLogin("starting before-AR engine");
            if (Engine.StartBeforeAutoRetainer())
                SetBeforeArGate(BeforeArGateState.Running, "Before-AR engine running");
            else
                SkipBeforeArForLogin("Before-AR engine start rejected");
        }
        catch (Exception ex)
        {
            Log.Error($"Error in pending before-AR login processing: {ex.Message}");
            SkipBeforeArForLogin($"Pending Before-AR exception: {ex.Message}");
        }
    }

    private bool ProcessPendingFishingRelogContinuation()
    {
        if (!FishingStartupCoordinator.HasPendingRelogContinuation)
        {
            ClearFishingRelogContinuationReadiness();
            return false;
        }

        if (FishingRelogCoordinator.IsFailed)
        {
            var reason = FishingRelogCoordinator.FailureReason;
            Log.Warning($"[Fishing][Startup] Pending relog continuation failed: {reason}");
            FishingRelogCoordinator.Reset();
            FishingStartupCoordinator.ReportAttemptFailure(
                DateTimeOffset.UtcNow,
                FishingAttemptFailureKind.SharedTransient,
                $"Fishing relog failed: {reason}",
                queueConfirmed: false);
            ClearFishingRelogContinuationReadiness();

            if (pendingBeforeArLogin)
                SkipBeforeArForLogin($"Ocean Fishing relog failed: {reason}");

            return false;
        }

        if (!ClientState.IsLoggedIn)
        {
            fishingRelogWorldReadySince = DateTime.MinValue;
            return true;
        }

        var now = DateTime.UtcNow;
        if (fishingRelogContinuationLastCheckAt != DateTime.MinValue &&
            now - fishingRelogContinuationLastCheckAt < FishingRelogContinuationPollInterval)
        {
            return true;
        }

        fishingRelogContinuationLastCheckAt = now;
        if (!TryGetWorldReadyCharacterForFishing(out var charName, out var worldName, out var contentId, out var notReadyReason))
        {
            LogFishingRelogContinuationDiagnostic(notReadyReason);
            return true;
        }

        var characterKey = $"{charName}@{worldName}";
        Log.Information($"[Fishing][Startup] Relog continuation world-ready resolved: character={characterKey}, contentId={contentId:X16}, target={FishingStartupCoordinator.PendingRelogCharacterKey}");
        ConfigManager.EnsureAccountSelected(contentId, null, characterKey);
        ConfigManager.EnsureCharacterExists(charName, worldName);
        ApplyLegacyFishingOperationSettingsIfNeeded();
        Configuration.LastAccountId = ConfigManager.CurrentAccountId;
        Configuration.Save();
        ConfigManager.LoadAllAccounts();

        var result = FishingStartupCoordinator.ContinuePendingRelog(DateTimeOffset.UtcNow, ConfigManager.CurrentCharacterKey);
        if (result.Started)
            Log.Information(FishingStartupDiagnostics.FormatStarted(result));
        else
            Log.Information($"[Fishing][Startup] trigger={result.Trigger}, action={result.Action}, reason={result.Reason}");

        if (result.Action is FishingStartupAction.FishingStarted or FishingStartupAction.AlreadyHandled)
        {
            if (pendingBeforeArLogin)
                SkipBeforeArForLogin($"Ocean Fishing relog continuation: {result.Reason}");

            ClearFishingRelogContinuationReadiness();
            return true;
        }

        if (!FishingStartupCoordinator.HasPendingRelogContinuation)
        {
            ClearFishingRelogContinuationReadiness();
            return false;
        }

        return true;
    }

    private void ClearPendingBeforeArLogin(string reason)
    {
        if (!pendingBeforeArLogin)
            return;

        Log.Information($"[AR] Clearing pending before-AR login: reason={reason}, suppression={AutoRetainerIPC.GetSuppressionSnapshot()}");
        pendingBeforeArLogin = false;
        beforeArLoginPendingSince = DateTime.MinValue;
        beforeArLoginLastDiagnosticAt = DateTime.MinValue;
        beforeArWorldReadySince = DateTime.MinValue;
        beforeArSuppressionRecoveryLastAttemptAt = DateTime.MinValue;
    }

    private void SkipBeforeArForLogin(string reason)
    {
        Log.Information($"[AR] Skipping Before-AR for this login: {reason}");
        Engine?.RecordSkippedOpportunity($"Before-AR skipped: {reason}");
        beforeArStartedThisLogin = true;
        beforeArArmedByPostprocess = false;
        ClearBeforeArArmedTracking();
        ClearPendingBeforeArLogin(reason);
        if (AutoRetainerIPC.SuppressionOwnedByVermaxion)
        {
            SetBeforeArGate(BeforeArGateState.ReleasePending, reason);
            ProcessBeforeArReleasePending();
        }
        else
        {
            SetBeforeArGate(BeforeArGateState.Skipped, reason);
        }
    }

    private void ProcessBeforeArReleasePending()
    {
        if (BeforeArGate != BeforeArGateState.ReleasePending)
            return;

        if (!AutoRetainerIPC.ReleaseSuppressionIfOwned())
            return;

        ClearBeforeArArmedTracking(preserveTimeoutReason: !string.IsNullOrWhiteSpace(beforeArTimeoutReleaseReason));
        SetBeforeArGate(BeforeArGateState.Skipped, $"{BeforeArGateStatus}; suppression release confirmed");
    }

    private void ProcessBeforeArArmedStallGuard()
    {
        if (BeforeArGate != BeforeArGateState.Armed ||
            BeforeArArmedAtUtc == DateTime.MinValue ||
            !AutoRetainerIPC.SuppressionOwnedByVermaxion)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var arBusy = AutoRetainerIPC.IsBusy();
        if (!BeforeArArmedStallPolicy.ShouldRelease(
                now,
                BeforeArArmedAtUtc,
                BeforeArGate,
                AutoRetainerIPC.SuppressionOwnedByVermaxion,
                Engine.OwnsActiveWork,
                pendingBeforeArLogin,
                arBusy,
                BeforeArArmedStallTimeout))
        {
            return;
        }

        var elapsed = now - BeforeArArmedAtUtc;
        var suppressionBefore = AutoRetainerIPC.GetSuppressionSnapshot();
        var reason = $"Before-AR armed timeout released after {elapsed.TotalMinutes:F1}m";
        beforeArTimeoutReleaseReason = reason;
        Log.Warning($"[AR] {reason}; account={BeforeArArmedAccountId}, character={BeforeArArmedCharacterKey}, arBusy={arBusy}, suppression={suppressionBefore}");

        var released = AutoRetainerIPC.ReleaseSuppressionIfOwned(force: true);
        var suppressionAfter = AutoRetainerIPC.GetSuppressionSnapshot();
        WriteBeforeArArmedStallIncident(elapsed, arBusy, suppressionBefore, suppressionAfter, released);

        beforeArArmedByPostprocess = false;
        ClearBeforeArArmedTracking(preserveTimeoutReason: true);
        SetBeforeArGate(
            !AutoRetainerIPC.SuppressionOwnedByVermaxion ? BeforeArGateState.Skipped : BeforeArGateState.ReleasePending,
            released ? reason : $"{reason}; release confirmation pending");
    }

    private void ProcessBeforeArSuppressionRecovery()
    {
        var activeRun = Engine.RequiresAutoRetainerSuppression;
        if (!Configuration.Enabled && !activeRun)
        {
            if (BeforeArGate is BeforeArGateState.Armed or BeforeArGateState.WaitingForWorldReady)
                SkipBeforeArForLogin("Global automation is disabled");
            return;
        }

        if (!activeRun && BeforeArGate is not (BeforeArGateState.Armed or BeforeArGateState.WaitingForWorldReady))
            return;
        if (!activeRun && BeforeArGate == BeforeArGateState.WaitingForWorldReady && !AutoRetainerIPC.SuppressionOwnedByVermaxion)
            return;

        var suppression = AutoRetainerIPC.GetSuppressionSnapshot();
        if (suppression.RemoteKnown && suppression.RemoteSuppressed && suppression.OwnedByVermaxion)
            return;
        if (activeRun)
            Engine.NotifyRetainerOwnershipLost("AutoRetainer suppression is lost or unreadable; waiting for ownership recovery");

        var now = DateTime.UtcNow;
        if (beforeArSuppressionRecoveryLastAttemptAt != DateTime.MinValue &&
            now - beforeArSuppressionRecoveryLastAttemptAt < TimeSpan.FromSeconds(2))
        {
            return;
        }

        beforeArSuppressionRecoveryLastAttemptAt = now;
        if (AutoRetainerIPC.TryAcquireSuppression())
            return;
        if (AutoRetainerIPC.SuppressionOwnedByVermaxion && BeforeArGate == BeforeArGateState.WaitingForWorldReady)
            LogPendingBeforeArDiagnostic("VMX suppression ownership recovery pending");
        else if (!AutoRetainerIPC.SuppressionOwnedByVermaxion && BeforeArGate == BeforeArGateState.Armed)
        {
            ClearBeforeArArmedTracking();
            SetBeforeArGate(BeforeArGateState.Skipped, "Could not recover armed suppression");
        }
    }

    private void UpdateBeforeArGateAfterEngine()
    {
        if (BeforeArGate != BeforeArGateState.Running || Engine.IsRunning)
            return;

        SetBeforeArGate(
            AutoRetainerIPC.SuppressionOwnedByVermaxion ? BeforeArGateState.ReleasePending : BeforeArGateState.Idle,
            AutoRetainerIPC.SuppressionOwnedByVermaxion ? "Engine idle; suppression release pending" : "Before-AR run complete");
    }

    private void SetBeforeArGate(BeforeArGateState state, string status)
    {
        if (BeforeArGate != state || !string.Equals(BeforeArGateStatus, status, StringComparison.Ordinal))
            Log.Information($"[AR] Before-AR gate: {BeforeArGate} -> {state}; {status}");

        BeforeArGate = state;
        BeforeArGateStatus = status;
    }

    private static string FormatBeforeArArmSkipReason(
        BeforeArArmDecision decision,
        AutoRetainerMultiModeReadResult multiMode)
    {
        return decision.Reason == BeforeArArmPolicy.MultiModeUnreadableReason && !string.IsNullOrWhiteSpace(multiMode.Error)
            ? $"{decision.Reason}: {multiMode.Error}"
            : decision.Reason;
    }

    private void RecordBeforeArArmed()
    {
        BeforeArArmedAtUtc = DateTime.UtcNow;
        BeforeArArmedAccountId = ConfigManager.CurrentAccountId;
        BeforeArArmedCharacterKey = ConfigManager.CurrentCharacterKey;
        beforeArTimeoutReleaseReason = string.Empty;
    }

    private void ClearBeforeArArmedTracking(bool preserveTimeoutReason = false)
    {
        BeforeArArmedAtUtc = DateTime.MinValue;
        BeforeArArmedAccountId = string.Empty;
        BeforeArArmedCharacterKey = string.Empty;
        if (!preserveTimeoutReason)
            beforeArTimeoutReleaseReason = string.Empty;
    }

    private string BuildBeforeArStatusText()
    {
        if (BeforeArGate == BeforeArGateState.Armed && BeforeArArmedAtUtc != DateTime.MinValue)
            return $"{BeforeArGateStatus}; armed {FormatBeforeArArmedAge()}";

        if (BeforeArGate == BeforeArGateState.Skipped && !string.IsNullOrWhiteSpace(beforeArTimeoutReleaseReason))
            return beforeArTimeoutReleaseReason;

        return BeforeArGateStatus;
    }

    private string FormatBeforeArArmedAge()
    {
        var elapsed = DateTime.UtcNow - BeforeArArmedAtUtc;
        return $"{Math.Max(0, elapsed.TotalMinutes):F1}/{BeforeArArmedStallTimeout.TotalMinutes:F1}m";
    }

    private void WriteBeforeArArmedStallIncident(
        TimeSpan elapsed,
        bool arBusy,
        SuppressionSnapshot suppressionBefore,
        SuppressionSnapshot suppressionAfter,
        bool released)
    {
        try
        {
            var summary = $"Before-AR armed suppression released after {elapsed.TotalMinutes:F1}m for {BeforeArArmedCharacterKey}";
            var diagnostics =
                $"account={BeforeArArmedAccountId}; character={BeforeArArmedCharacterKey}; elapsed={elapsed}; arBusy={arBusy}; suppressionBefore={suppressionBefore}; suppressionAfter={suppressionAfter}; releaseConfirmed={released}; lastRunOutcome={Engine.LastRunOutcome}; lastRunSummary={Engine.LastRunSummary}";
            IncidentWriter.Write(new VermaxionIncident(
                DateTime.UtcNow,
                "before-ar-armed-stall-release",
                BeforeArGate.ToString(),
                "BeforeAR",
                summary,
                diagnostics));
        }
        catch (Exception ex)
        {
            Log.Error($"[AR] Failed to write before-AR armed stall incident: {ex.Message}");
        }
    }

    private void LogPendingBeforeArDiagnostic(string reason)
    {
        var now = DateTime.UtcNow;
        if (beforeArLoginLastDiagnosticAt != DateTime.MinValue &&
            (now - beforeArLoginLastDiagnosticAt).TotalSeconds < 2)
        {
            return;
        }

        beforeArLoginLastDiagnosticAt = now;
        Log.Information($"[AR] Pending before-AR login: {reason}, elapsed={(now - beforeArLoginPendingSince).TotalSeconds:F1}s, suppression={AutoRetainerIPC.GetSuppressionSnapshot()}");
    }

    private bool TryGetWorldReadyCharacter(out string charName, out string worldName, out ulong contentId, out string reason)
    {
        charName = ObjectTable.LocalPlayer?.Name.ToString() ?? "";
        worldName = ObjectTable.LocalPlayer?.HomeWorld.Value.Name.ToString() ?? "";
        contentId = PlayerState.ContentId;

        var loggedIn = ClientState.IsLoggedIn;
        var hasLocalPlayer = ObjectTable.LocalPlayer != null;
        var betweenAreas = Condition[ConditionFlag.BetweenAreas];
        var betweenAreas51 = Condition[ConditionFlag.BetweenAreas51];
        var playerAvailable = GameHelpers.IsPlayerAvailable();

        if (!loggedIn ||
            !hasLocalPlayer ||
            string.IsNullOrEmpty(charName) ||
            string.IsNullOrEmpty(worldName) ||
            contentId == 0 ||
            betweenAreas ||
            betweenAreas51 ||
            !playerAvailable)
        {
            beforeArWorldReadySince = DateTime.MinValue;
            reason = $"waiting for world-ready: loggedIn={loggedIn}, hasLocalPlayer={hasLocalPlayer}, character='{charName}', world='{worldName}', contentId={contentId:X16}, BetweenAreas={betweenAreas}, BetweenAreas51={betweenAreas51}, playerAvailable={playerAvailable}";
            return false;
        }

        var now = DateTime.UtcNow;
        if (beforeArWorldReadySince == DateTime.MinValue)
            beforeArWorldReadySince = now;

        var stableSeconds = (now - beforeArWorldReadySince).TotalSeconds;
        if (stableSeconds < BeforeArWorldReadyStableSeconds)
        {
            reason = $"waiting for world-ready stability: stable={stableSeconds:F1}/{BeforeArWorldReadyStableSeconds:F1}s, character='{charName}', world='{worldName}', contentId={contentId:X16}, BetweenAreas={betweenAreas}, BetweenAreas51={betweenAreas51}, playerAvailable={playerAvailable}";
            return false;
        }

        reason = "world-ready";
        return true;
    }

    private bool TryGetWorldReadyCharacterForRegistration(out string charName, out string worldName, out ulong contentId, out string reason)
    {
        charName = ObjectTable.LocalPlayer?.Name.ToString() ?? "";
        worldName = ObjectTable.LocalPlayer?.HomeWorld.Value.Name.ToString() ?? "";
        contentId = PlayerState.ContentId;

        var loggedIn = ClientState.IsLoggedIn;
        var hasLocalPlayer = ObjectTable.LocalPlayer != null;
        var betweenAreas = Condition[ConditionFlag.BetweenAreas];
        var betweenAreas51 = Condition[ConditionFlag.BetweenAreas51];
        var playerAvailable = GameHelpers.IsPlayerAvailable() || loggedIn && hasLocalPlayer && !betweenAreas && !betweenAreas51 &&
            contentId != 0 && (ChokeAboIpcClient.GetTargetCycleStatus(contentId).Status?.CanResumeOwnedInteraction == true || CanObserveVerminionForReload());

        if (!loggedIn ||
            !hasLocalPlayer ||
            string.IsNullOrEmpty(charName) ||
            string.IsNullOrEmpty(worldName) ||
            contentId == 0 ||
            betweenAreas ||
            betweenAreas51 ||
            !playerAvailable)
        {
            characterRegistrationWorldReadySince = DateTime.MinValue;
            reason = "waiting for character-registration world-ready";
            return false;
        }

        var now = DateTime.UtcNow;
        if (characterRegistrationWorldReadySince == DateTime.MinValue)
            characterRegistrationWorldReadySince = now;

        if ((now - characterRegistrationWorldReadySince).TotalSeconds < BeforeArWorldReadyStableSeconds)
        {
            reason = "waiting for character-registration world-ready stability";
            return false;
        }

        reason = "world-ready";
        return true;
    }

    private bool CanObserveVerminionForReload()
    {
        var player = ObjectTable.LocalPlayer;
        return Configuration.DebugTaskId == AutomationCatalog.VerminionQueue && player != null &&
            Configuration.DebugTaskCharacterKey == $"{player.Name}@{player.HomeWorld.Value.Name}" &&
            (VerminionGameInteraction.IsSetupMenu || VerminionGameInteraction.CurrentCpuDutyId() != 0);
    }

    private bool TryGetWorldReadyCharacterForFishing(out string charName, out string worldName, out ulong contentId, out string reason)
    {
        charName = ObjectTable.LocalPlayer?.Name.ToString() ?? "";
        worldName = ObjectTable.LocalPlayer?.HomeWorld.Value.Name.ToString() ?? "";
        contentId = PlayerState.ContentId;

        var loggedIn = ClientState.IsLoggedIn;
        var hasLocalPlayer = ObjectTable.LocalPlayer != null;
        var betweenAreas = Condition[ConditionFlag.BetweenAreas];
        var betweenAreas51 = Condition[ConditionFlag.BetweenAreas51];
        var playerAvailable = GameHelpers.IsPlayerAvailable();

        if (!loggedIn ||
            !hasLocalPlayer ||
            string.IsNullOrEmpty(charName) ||
            string.IsNullOrEmpty(worldName) ||
            contentId == 0 ||
            betweenAreas ||
            betweenAreas51 ||
            !playerAvailable)
        {
            fishingRelogWorldReadySince = DateTime.MinValue;
            reason = $"waiting for fishing relog world-ready: loggedIn={loggedIn}, hasLocalPlayer={hasLocalPlayer}, character='{charName}', world='{worldName}', contentId={contentId:X16}, BetweenAreas={betweenAreas}, BetweenAreas51={betweenAreas51}, playerAvailable={playerAvailable}";
            return false;
        }

        var now = DateTime.UtcNow;
        if (fishingRelogWorldReadySince == DateTime.MinValue)
            fishingRelogWorldReadySince = now;

        var stableSeconds = (now - fishingRelogWorldReadySince).TotalSeconds;
        if (stableSeconds < BeforeArWorldReadyStableSeconds)
        {
            reason = $"waiting for fishing relog world-ready stability: stable={stableSeconds:F1}/{BeforeArWorldReadyStableSeconds:F1}s, character='{charName}', world='{worldName}', contentId={contentId:X16}, BetweenAreas={betweenAreas}, BetweenAreas51={betweenAreas51}, playerAvailable={playerAvailable}";
            return false;
        }

        reason = "world-ready";
        return true;
    }

    private void LogFishingRelogContinuationDiagnostic(string reason)
    {
        var now = DateTime.UtcNow;
        if (fishingRelogLastDiagnosticAt != DateTime.MinValue &&
            (now - fishingRelogLastDiagnosticAt).TotalSeconds < 2)
        {
            return;
        }

        fishingRelogLastDiagnosticAt = now;
        Log.Information($"[Fishing][Startup] Pending relog continuation: {reason}, target={FishingStartupCoordinator.PendingRelogCharacterKey}");
    }

    private void ClearFishingRelogContinuationReadiness()
    {
        fishingRelogContinuationLastCheckAt = DateTime.MinValue;
        fishingRelogWorldReadySince = DateTime.MinValue;
        fishingRelogLastDiagnosticAt = DateTime.MinValue;
    }

    private int GetConfiguredBeforeAutoRetainerTaskCount()
    {
        if (PostProcessTaskOrder.Normalize(Configuration))
            Configuration.Save();

        return Configuration.PostProcessTaskPlacement.Values.Count(phase => phase == PostProcessTaskPhase.BeforeAR);
    }

    private DateTime nextStuckDetectionSuppressionUtc = DateTime.MinValue;
    private DateTime nextStuckSuppressionWarnUtc = DateTime.MinValue;
    private DateTime nextFakeReadyUtc = DateTime.MinValue;

    /// <summary>Once past the configured offset into a registration window with no fishing run active,
    /// fake-ready a character so AutoRetainer logs one in (see AutoRetainerIPC.TryFakeReadyEnabledCharacter).
    /// Throttled to every 30s; skips entirely when a run or relog is already in progress.</summary>
    private void ProcessFishingFakeReady()
    {
        if (VerminionService.HasQuestAcquisition) return;
        if (!Configuration.Enabled ||
            !Configuration.OceanFishingFakeReadyEnabled ||
            OfflineLogoutBlocksOrdinaryAutomation)
            return;
        if (FishingService.IsActive || FishingRelogCoordinator.IsActive)
            return;
        var now = DateTime.UtcNow;
        if (now < nextFakeReadyUtc)
            return;
        if (!OceanFishingSchedulePolicy.TryGetActiveStartupWindow(
                DateTimeOffset.UtcNow,
                Configuration.OceanFishingPreWindowOffsetMinutes,
                out var window))
        {
            return;
        }
        var offset = Math.Clamp(Configuration.OceanFishingFakeReadyOffsetMinutes, 0, 12);
        if (DateTimeOffset.UtcNow < window.RegistrationStartUtc.AddMinutes(offset))
            return;
        // Per-window latch: once this client's queue registration is confirmed for the window, the nudge
        // has served its purpose — keep nudging only while registration has not happened, so a failed
        // registration still gets retries but a registered/completed one is not churned every 30s.
        if (FishingRunLifecycle.IsQueueRegistrationConfirmedForWindow(window.RegistrationStartUtc))
            return;
        nextFakeReadyUtc = now.AddSeconds(30);
        if (AutoRetainerIPC.TryFakeReadyEnabledCharacter(15, out var who, out var error))
            Log.Information($"[Fishing][FakeReady] Nudged AutoRetainer ({who}) to log a character in for the open window.");
        else
            Log.Debug($"[Fishing][FakeReady] Not applied: {error}");
    }

    private DateTime nextInnParkCheckUtc = DateTime.MinValue;
    private bool innParkEnableLogged;

    /// <summary>Idle inn-parking (checked every 60s): when the logged-in character is not already in an inn,
    /// during downtime — no fishing run/relog, outside any startup window with at least
    /// OceanIdleInnParkMinMinutesToWindow to the next even-UTC registration, and no venture due within the exit
    /// lead — send the character to an inn. Once confirmed inside an
    /// inn: optionally enable the character in AutoRetainer from a confirmed inn location.
    /// An already-parked character is left in the inn; this maintenance pass does not
    /// send it back to Limsa.</summary>
    private void ProcessIdleInnPark()
    {
        if (VerminionService.HasQuestAcquisition) return;
        if (!Configuration.Enabled ||
            !Configuration.OceanIdleInnParkEnabled ||
            OfflineLogoutBlocksOrdinaryAutomation)
            return;
        if (FishingService.IsActive || FishingRelogCoordinator.IsActive)
            return;
        if (ARPostProcessService.IsRequested)
            return;
        var arBusy = AutoRetainerIPC.ReadBusyState();
        if (!arBusy.Success || arBusy.Busy)
            return;
        var now = DateTime.UtcNow;
        if (now < nextInnParkCheckUtc)
            return;
        nextInnParkCheckUtc = now.AddSeconds(60);

        var contentId = PlayerState.ContentId;
        if (contentId == 0 || ObjectTable.LocalPlayer == null)
            return;

        var territory = ClientState.TerritoryType;
        var inInn = Inns.List.Any(inn => inn == territory);

        if (inInn)
        {
            // Enable at the confirmed inn location (log only on an actual state change, not per tick).
            if (Configuration.OceanIdleInnParkEnableAutoRetainer)
            {
                if (AutoRetainerIPC.TryEnableCharacter(contentId, out var who, out var enableError))
                {
                    if (!string.IsNullOrEmpty(who) && !innParkEnableLogged)
                    {
                        Log.Information($"[InnPark] {who} parked in inn (territory {territory}); AutoRetainer enable ensured.");
                        innParkEnableLogged = true;
                    }
                }
                else if (!string.IsNullOrEmpty(enableError))
                {
                    Log.Debug($"[InnPark] AR enable not applied: {enableError}");
                }
            }
            return;
        }
        innParkEnableLogged = false;

        // Earliest venture across the CURRENT char's retainers (unix seconds); unreadable -> be conservative
        // and treat as due-now so we never park a char AR is about to need.
        var earliest = 0L;
        if (!AutoRetainerIPC.TryReadEarliestVenture(contentId, out earliest, out var ventureError))
        {
            Log.Debug($"[InnPark] Venture read unavailable ({ventureError}); skipping this pass.");
            return;
        }
        var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var exitLeadSeconds = Math.Clamp(Configuration.OceanIdleInnParkExitLeadMinutes, 1, 60) * 60L;

        // Not in an inn: park only in genuine downtime.
        if (OceanFishingSchedulePolicy.TryGetActiveStartupWindow(
                DateTimeOffset.UtcNow,
                Configuration.OceanFishingPreWindowOffsetMinutes,
                out _))
        {
            return; // a window is active — VMX owns the char
        }
        // Next even-UTC registration must be comfortably far away.
        var utcNow = DateTimeOffset.UtcNow;
        var nextEven = new DateTimeOffset(utcNow.Year, utcNow.Month, utcNow.Day, utcNow.Hour, 0, 0, TimeSpan.Zero);
        if (nextEven.Hour % 2 != 0)
            nextEven = nextEven.AddHours(1);
        else if (utcNow >= nextEven.AddMinutes(15))
            nextEven = nextEven.AddHours(2);
        var minutesToWindow = (nextEven - utcNow).TotalMinutes;
        if (minutesToWindow < Math.Clamp(Configuration.OceanIdleInnParkMinMinutesToWindow, 1, 110))
            return;
        // Nothing due on this char within the lead either (else AR is about to want the bell).
        if (earliest != long.MaxValue && earliest - nowUnix <= exitLeadSeconds)
            return;
        if (LifestreamIPC.IsBusy())
            return;

        Log.Information($"[InnPark] Downtime ({minutesToWindow:F0}min to next window); sending character to the inn.");
        LifestreamIPC.ExecuteCommand("/li inn");
    }

    /// <summary>Re-arms the AutoRetainer stuck-detector suppression every 60s (AR reloads reset its
    /// throttler). Failures log at most hourly; AR simply not being loaded is a normal standalone state.</summary>
    private void ProcessStuckDetectionSuppression()
    {
        if (!Configuration.SuppressAutoRetainerStuckDetection)
            return;
        var now = DateTime.UtcNow;
        if (now < nextStuckDetectionSuppressionUtc)
            return;
        nextStuckDetectionSuppressionUtc = now.AddSeconds(60);
        if (!AutoRetainerIPC.TrySuppressStuckDetection(out var error) && now >= nextStuckSuppressionWarnUtc)
        {
            nextStuckSuppressionWarnUtc = now.AddHours(1);
            Log.Debug($"[AR] Stuck-detection suppression not applied: {error}");
        }
    }

    private void OnFrameworkUpdate(IFramework fw)
    {
        // Reassert our owned entry if the shared list changes. Shopping deliberately releases it.
        if (YesAlreadyIPC.IsPaused)
            YesAlreadyIPC.Pause();

        DadHandoffIpcProvider.Update();

        // Login detection
        if (ClientState.IsLoggedIn && !wasLoggedIn)
        {
            wasLoggedIn = true;
            beforeArStartedThisLogin = false;
            QueueCharacterRegistration("framework login transition");
            BeginBeforeArLoginPending("framework login transition");
        }
        else if (!ClientState.IsLoggedIn && wasLoggedIn)
        {
            RetainerEquippingService.RestoreCollectOnly(preserveCheckpoint: true);
            retainerCollectOnlyObservedArProcessing = false;
            wasLoggedIn = false;
            beforeArStartedThisLogin = false;
            if (ARPostProcessService.IsProcessing || ARPostProcessService.IsRequested)
            {
                const string reason = "Client disconnected during VERMAXION-owned postprocess";
                Log.Warning($"[AR] {reason}; cancelling interrupted work and releasing ownership.");
                pendingFishingPostprocessHandoff = false;
                FishingStartupCoordinator.CancelPendingRun();
                FishingRelogCoordinator.Reset();
                ClearFishingRelogContinuationReadiness();
                FishingService.Reset();
                // Release without arming next-login work before the existing force-stop cleanup.
                ARPostProcessService.FinishPostProcess(force: true, mode: ARPostProcessFinishMode.ReleaseOnly);
                Engine.ForceStop();
                releaseOnlyPostprocessFinishPending = false;
                releaseOnlyPostprocessFinishReason = string.Empty;
                beforeArArmedByPostprocess = false;
                ClearBeforeArArmedTracking();
                SetBeforeArGate(BeforeArGateState.Idle, reason);
            }
            ClearPendingBeforeArLogin("framework logout transition");
            ClearCharacterRegistrationForLogout();
        }

        ProcessPendingCharacterRegistration();
        FishCollection.Update();
        // Custom-delivery data and task progress must refresh with all windows closed.
        DeliveryFishing.Update();
        CustomDeliveriesService.Update();
        ChocoboStablesService.Update();
        if (characterRegistrationCompletedThisLogin)
            MainWindow.UpdateCustomDeliveryVisibility();
        ProcessPendingDebugTask();
        ProcessChocoboContinuation();

        AutoRetainerSelectionGuard.Update(
            Configuration.AutoRestoreRetainerCheckingAfterWork,
            ClientState.IsLoggedIn,
            PlayerState.ContentId,
            DateTime.UtcNow);

        CharacterSelectStallRecovery.Update(
            DateTime.UtcNow,
            Configuration.EnableCharacterSelectStallRecovery && !OfflineLogoutBlocksOrdinaryAutomation,
            Configuration.Enabled &&
            Configuration.EnableCharacterSelectStallRecovery &&
            !OfflineLogoutBlocksOrdinaryAutomation,
            GameHelpers.IsAddonVisible("CharaSelect"),
            ClientState.IsLoggedIn);

        ProcessBeforeArSuppressionRecovery();
        FishingRunLifecycle.Update();
        ScheduledOfflineHoldCoordinator.Update(DateTimeOffset.UtcNow);
        ProcessFishingRecovery();
        if (Configuration.Enabled &&
            Configuration.OceanFishingWindowWatchEnabled &&
            !OfflineLogoutBlocksOrdinaryAutomation)
            RunFishingStartupTrigger(FishingStartupTrigger.WindowWatch);
        ProcessStuckDetectionSuppression();
        ProcessFishingFakeReady();
        ProcessIdleInnPark();
        ProcessBeforeArArmedStallGuard();
        ProcessReleaseOnlyPostprocessFinishPending();
        ProcessBeforeArReleasePending();
        var fishingContinuationBlocksBeforeAr = ProcessPendingFishingRelogContinuation();
        var fishingPostprocessHandoffBlocksBeforeAr = ProcessPendingFishingPostprocessHandoff();
        if (!fishingContinuationBlocksBeforeAr && !fishingPostprocessHandoffBlocksBeforeAr)
            ProcessPendingBeforeArLogin();

        // Update engine (runs the state machine)
        Engine.Update();
        FisherGearsetTestService.Update();
        UpdateBeforeArGateAfterEngine();
        ProcessBeforeArReleasePending();
        ProcessRetainerCollectOnlyRecovery();
        ProcessRetainerlessTimer();
        ProcessAutomaticDueTasks();

        // Update DTR bar
        UpdateDtrBar();

        // Update individual services for manual testing (when not running through engine)
        if (!Engine.IsRunning)
        {
            FCBuffService.Update();
            FCBuffInventoryService.Update();
            VerminionService.Update();
            CactpotService.Update();
            ChocoboRaceService.Update();
            FashionReportService.Update();
            if (!FishingService.IsActive)
                VendorStockService.Update();
            FishingRelogCoordinator.Update();
            RetainerListingRefillService.Update();
            WorkshopBellService.Update();
            RegisterRegistrablesService.Update();
            LootGoblinMapGatherManualRunCoordinator.Update();
            MinionRouletteService.Update();
            SeasonalGearService.Update();
            GearUpdaterService.Update();
            HighestCombatJobService.Update();
            CurrentJobEquipmentService.Update();
            AlliedSocietyService.Update();
            AfterArParkService.Update();
            FishingService.Update();
        }

        ReleaseDashboardRunYesAlreadyPauseIfIdle();
    }

    private void ProcessRetainerCollectOnlyRecovery()
    {
        var active = ConfigManager.GetActiveConfig();
        if (active?.RetainerEquipmentCheckpointPending != true)
        {
            retainerCollectOnlyObservedArProcessing = false;
            return;
        }

        if (ARPostProcessService.IsProcessing)
        {
            retainerCollectOnlyObservedArProcessing = true;
            return;
        }

        if (!retainerCollectOnlyObservedArProcessing || Engine.IsRunning)
            return;

        RetainerEquippingService.RestoreCollectOnly();
        if (active.RetainerEquipmentCheckpointPending)
            return;

        retainerCollectOnlyObservedArProcessing = false;
        Log.Information("[RetainerEquip] Restored collect-only after AutoRetainer processing completed.");
    }

    public void SetupDtrBar()
    {
        try
        {
            dtrEntry = DtrBar.Get("Vermaxion");
            dtrEntry.Shown = Configuration.DtrBarEnabled;
            dtrEntry.Text = new SeString(new TextPayload("VMX: Idle"));
            dtrEntry.OnClick = (_) =>
            {
                MainWindow.Toggle();
            };
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to setup DTR bar: {ex.Message}");
        }
    }

    public void UpdateDtrBar()
    {
        if (dtrEntry == null) return;
        using var textScope = uiText.Enter();
        // DTR uses the game renderer; authored Hindi requires the ImGui shaping path.
        string DtrText(string value) => uiText.Language == "hi" ? value : UiText.T(value);
        string DtrFormat(string format, params object?[] values) => uiText.Language == "hi"
            ? string.Format(System.Globalization.CultureInfo.InvariantCulture, format, values) : UiText.F(format, values);

        dtrEntry.Shown = Configuration.DtrBarEnabled;
        if (!Configuration.DtrBarEnabled) return;

        var config = ConfigManager.GetActiveConfig();
        var isEnabled = Configuration.Enabled && (config?.Enabled ?? false);
        
        // DTR modes: 0=text-only, 1=icon+text, 2=icon-only
        var iconEnabled = string.IsNullOrEmpty(Configuration.DtrIconEnabled) ? "\uE03C" : Configuration.DtrIconEnabled;
        var iconDisabled = string.IsNullOrEmpty(Configuration.DtrIconDisabled) ? "\uE03D" : Configuration.DtrIconDisabled;
        var glyph = isEnabled ? iconEnabled : iconDisabled;
        var operationalStatus = GetDtrOperationalStatus() is { } status ? DtrText(status) : null;

        string statusText;
        string tooltipText;

        switch (Configuration.DtrBarMode)
        {
            case 1: // icon+text
                statusText = $"{glyph} VMX";
                tooltipText = operationalStatus != null
                    ? DtrFormat("Vermaxion: {0}", operationalStatus)
                    : isEnabled
                        ? DtrText("Vermaxion ready - waiting for AR postprocess")
                        : DtrText("Vermaxion disabled");
                break;
            case 2: // icon-only
                statusText = glyph;
                tooltipText = operationalStatus != null
                    ? DtrFormat("Vermaxion: {0}", operationalStatus)
                    : isEnabled
                        ? DtrText("Vermaxion ready")
                        : DtrText("Vermaxion disabled");
                break;
            default: // text-only
                if (operationalStatus != null)
                {
                    statusText = DtrFormat("VMX: {0}", operationalStatus);
                }
                else
                {
                    statusText = DtrText(isEnabled ? "VMX: Ready" : "VMX: Off");
                }
                tooltipText = operationalStatus != null
                    ? DtrFormat("Vermaxion: {0}", operationalStatus)
                    : isEnabled
                        ? DtrText("Vermaxion ready - waiting for AR postprocess")
                        : DtrText("Vermaxion disabled");
                break;
        }

        dtrEntry.Text = new SeString(new TextPayload(statusText));
        dtrEntry.Tooltip = new SeString(new TextPayload(tooltipText));
    }

    private string? GetDtrOperationalStatus()
    {
        if (Engine.IsRunning)
            return Engine.StatusText;
        if (BeforeArGate == BeforeArGateState.ReleasePending)
            return "AR suppression release pending";
        if (BeforeArGate == BeforeArGateState.Armed)
            return BeforeArArmedAtUtc != DateTime.MinValue
                ? $"Before-AR Armed {FormatBeforeArArmedAge()}"
                : "Before-AR Armed";
        if (BeforeArGate is BeforeArGateState.WaitingForWorldReady or BeforeArGateState.Running)
            return $"Before-AR {BeforeArGate}";
        if (BeforeArGate == BeforeArGateState.Skipped && !string.IsNullOrWhiteSpace(beforeArTimeoutReleaseReason))
            return beforeArTimeoutReleaseReason;
        if (FishingRelogCoordinator.IsActive)
            return FishingRunLifecycle.StatusPrefix + FishingRelogCoordinator.StatusText;
        if (FishingStartupCoordinator.HasPendingRelogContinuation)
            return FishingRunLifecycle.StatusPrefix + $"Fishing relog pending {FishingStartupCoordinator.PendingRelogCharacterKey}";
        if (FishingService.IsActive)
            return $"Fishing {FishingService.StatusText}";
        if (FishingRunLifecycle.IsCleanupPending)
            return "Fishing external-state cleanup pending";
        if (ARPostProcessService.IsProcessing)
            return "AR postprocess owned";
        if (AutoRetainerIPC.SuppressionOwnedByVermaxion)
            return "AR suppression recovery";

        return null;
    }

    private void ProcessFishingRecovery()
    {
        if (FishCollection.IsActive) return;
        if (FishingService.IsFailed && !FishingService.FailureReported)
        {
            FishingService.MarkFailureReported();
            FishingStartupCoordinator.ReportAttemptFailure(
                DateTimeOffset.UtcNow,
                FishingService.FailureKind,
                FishingService.StatusText,
                FishingService.QueueRegistrationObserved);
        }

        if (!FishingStartupCoordinator.HasRecoveryPending)
            return;

        var result = FishingStartupCoordinator.PollRecovery(
            DateTimeOffset.UtcNow,
            ConfigManager.CurrentCharacterKey);
        if (result.Started)
            Log.Information($"[Fishing][Recovery] {FishingStartupDiagnostics.FormatStarted(result)}");
        else if (result.Action == FishingStartupAction.AlreadyHandled)
            Log.Warning($"[Fishing][Recovery] {result.Reason}");
    }

    /// <summary>
    /// FULL STOP - Immediately halts ALL plugin operations, services, and navigation.
    /// </summary>
    public void FullStop(bool preparingDebugTask = false)
    {
        var timerAccount = ConfigManager.GetCurrentAccount();
        if (timerAccount?.RetainerlessTimerEnabled == true)
        {
            timerAccount.RetainerlessTimerEnabled = false;
            ConfigManager.SaveCurrentAccount();
        }
        retainerlessTimerDueUtc = null;
        retainerlessTimerRunPendingCompletion = false;
#if DEBUG
        debugCollectionStopArmed = false;
#endif
        VerminionService.CancelQuestAcquisition();
        pendingDebugDispatchTaskId = null;
        if (pendingDebugTaskId != null)
        {
            pendingDebugTaskId = null;
            SetDebugTaskStatus("Cancelled for this reload; selection is saved for the next reload.");
        }

        Log.Information("[FULL STOP] ========== STOPPING ALL OPERATIONS ==========");
        FishCollection.Stop("Full Stop");
        if (!preparingDebugTask && PlayerState.ContentId != 0 && !string.IsNullOrEmpty(ConfigManager.CurrentCharacterKey))
        {
            ConfigManager.GetActiveConfig().VerminionPaused = true;
            ConfigManager.SaveCurrentAccount();
        }
        PauseCurrentTargetCycleBestEffort("VERMAXION Full Stop");

        ScheduledOfflineHoldCoordinator.Cancel("Full Stop", DateTimeOffset.UtcNow);
        FisherGearsetTestService.Cancel();
        if (FishingRunLifecycle.Mode != FishingRunMode.Test)
            FishingStartupCoordinator.SuppressCurrentWindow(DateTimeOffset.UtcNow);
        FishingStartupCoordinator.CancelPendingRun();
        pendingFishingPostprocessHandoff = false;
        ClearFishingRelogContinuationReadiness();

        LootGoblinMapGatherManualRunCoordinator.Cancel();
        Log.Information("[FULL STOP] LootGoblin map gather cancel requested");

        if (!FishCollection.IsActive)
            Engine.ForceStop(preserveVerminionResult: preparingDebugTask &&
                Configuration.DebugTaskId == AutomationCatalog.VerminionQueue);
        Log.Information(FishCollection.IsActive ? "[FULL STOP] Engine cleanup retained by collection" : "[FULL STOP] Engine force-stopped");

        MomIPCClient.CancelActiveRun();
        Log.Information("[FULL STOP] mom IPC cancel requested");

        // Stop all services that have state machines
        FCBuffService.Reset();
        VerminionService.Reset();
        CactpotService.Reset();
        ChocoboRaceService.Reset();
        FashionReportService.Reset();
        CustomDeliveriesService.Reset();
        ChocoboStablesService.Reset();
        VendorStockService.Reset();
        if (!FishCollection.IsActive)
        {
            FishingService.Reset();
            FishingRelogCoordinator.Reset();
        }
        CharacterSelectStallRecovery.Reset();
        if (!FishCollection.IsActive) FishingRunLifecycle.ForceCleanup("Full Stop");
        RetainerListingRefillService.Reset();
        WorkshopBellService.Reset();
        RegisterRegistrablesService.Reset();
        LootGoblinMapGatherManualRunCoordinator.Reset();
        MinionRouletteService.Reset();
        SeasonalGearService.Reset();
        GearUpdaterService.Reset();
        HighestCombatJobService.Reset();
        CurrentJobEquipmentService.Reset();
        AlliedSocietyService.Reset();
        AfterArParkService.Reset();
        Log.Information(FishCollection.IsActive ? "[FULL STOP] Other services reset; collection cleanup retained" : "[FULL STOP] All services reset");

        // Stop VNavmesh navigation
        if (!FishCollection.IsActive) VNavmeshIPC.Stop();
        Log.Information(FishCollection.IsActive ? "[FULL STOP] Owned VNavmesh cancellation requested by collection" : "[FULL STOP] VNavmesh stopped");

        // Unpause YesAlready
        if (!FishCollection.IsActive) YesAlreadyIPC.Unpause();
        dashboardRunYesAlreadyPauseOwned = false;
        Log.Information(FishCollection.IsActive ? "[FULL STOP] YesAlready lease retained through collection cleanup" : "[FULL STOP] YesAlready unpaused");

        if (!FishCollection.IsActive) AutoRetainerIPC.ReleaseSuppressionIfOwned(force: true);
        releaseOnlyPostprocessFinishPending = false;
        releaseOnlyPostprocessFinishReason = string.Empty;
        beforeArArmedByPostprocess = false;
        ClearBeforeArArmedTracking();
        pendingBeforeArLogin = false;
        SetBeforeArGate(BeforeArGateState.Idle, "Full Stop");
        Log.Information(FishCollection.IsActive ? "[FULL STOP] AutoRetainer suppression retained through collection cleanup" : "[FULL STOP] AutoRetainer suppression released if owned");

        Log.Information(FishCollection.IsActive
            ? "[FULL STOP] Collection stopped; retaining ownership until external work and cleanup settle"
            : "[FULL STOP] ========== ALL OPERATIONS HALTED ==========");
        ChatGui.Print(FishCollection.IsActive
            ? "[Vermaxion] FULL STOP - Collection stopped; owned external work and cleanup remain pending."
            : VerminionService.HasQuestAcquisition
            ? "[Vermaxion] FULL STOP - Vermaxion halted. Questionable acquisition cancellation or cleanup remains pending."
            : "[Vermaxion] FULL STOP - All operations halted.");
    }

    public void PauseCurrentTargetCycleBestEffort(string reason, bool targetModeWasActive = false)
    {
        var activeConfig = ConfigManager.GetActiveConfig();
        if (!targetModeWasActive && activeConfig?.ChocoboAutomationMode != ChocoboAutomationMode.TargetPedigree)
            return;

        if (activeConfig != null)
        {
            activeConfig.ChocoboProgressionPaused = true;
            ConfigManager.SaveCurrentAccount();
        }

        var contentId = PlayerState.ContentId;
        if (contentId == 0)
            return;

        var result = ChokeAboIpcClient.PauseTargetCycle(contentId);
        if (result.Succeeded && result.Status != null)
        {
            Log.Information($"[ChocoboRace] Requested Choke-abo V2 pause for {reason}: {result.Status.Phase} - {result.Status.Reason}");
        }
        else
        {
            Log.Warning($"[ChocoboRace] Best-effort Choke-abo V2 pause failed for {reason}: {result.Error}");
        }
    }

    private bool CanStartChocoboProgression(out string reason)
    {
        if (!characterRegistrationCompletedThisLogin || !GameHelpers.IsPlayerAvailable() &&
            ChokeAboIpcClient.GetTargetCycleStatus(PlayerState.ContentId).Status?.CanResumeOwnedInteraction != true)
        { reason = "Waiting for the current character to be ready."; return false; }
        if (!CanStartMainMenuTest(waitForOceanFishing: false, out reason)) return false;
        if (Condition[ConditionFlag.BoundByDuty] && !ChocoboRaceService.CanReconcileRacingActivity())
        { reason = "Another duty owns the character."; return false; }
        if (LifestreamIPC.IsBusy()) { reason = "Travel owns the character."; return false; }
        if (PluginInterface.InstalledPlugins.Any(plugin => plugin.IsLoaded && plugin.InternalName is "Questionable" or "WigglyQuest"))
        {
            try
            {
                var prefix = PluginInterface.InstalledPlugins.Any(plugin => plugin.IsLoaded && plugin.InternalName == "WigglyQuest")
                    ? "WigglyQuest" : "Questionable";
                if (PluginInterface.GetIpcSubscriber<bool>($"{prefix}.IsRunning").InvokeFunc() && !ChocoboRaceService.OwnsCurrentUnlockQuest())
                { reason = "Questionable owns the character."; return false; }
            }
            catch { reason = "Questionable ownership could not be read."; return false; }
        }
        reason = string.Empty;
        return true;
    }

    private void ProcessRetainerlessTimer()
    {
        var now = DateTime.UtcNow;
        var account = ConfigManager.GetCurrentAccount();
        var enabled = account?.RetainerlessTimerEnabled == true;
        var interval = account?.RetainerlessTimerIntervalMinutes ?? 30;
        var accountId = ConfigManager.CurrentAccountId;
        var characterId = ClientState.IsLoggedIn ? PlayerState.ContentId : 0;
        var registeredSession = ClientState.IsLoggedIn && IsCharacterRegistered && PlayerState.IsLoaded &&
            characterId != 0 && account?.Characters.ContainsKey(ConfigManager.CurrentCharacterKey) == true;
        var sessionChanged = accountId != retainerlessTimerAccountId || characterId != retainerlessTimerCharacterId;
        var restartInterval = enabled && !retainerlessTimerWasEnabled || sessionChanged ||
            interval != retainerlessTimerIntervalMinutes;
        if (sessionChanged)
            retainerlessTimerRunPendingCompletion = false;
        DateTime? completedAtUtc = null;
        if (retainerlessTimerRunPendingCompletion && !Engine.IsRetainerlessTimerRun)
        {
            retainerlessTimerRunPendingCompletion = false;
            completedAtUtc = Engine.LastRetainerlessTimerRunCompletedAtUtc;
        }
        retainerlessTimerDueUtc = LifecyclePolicy.UpdateRetainerlessTimerDueUtc(
            now, retainerlessTimerDueUtc, interval, enabled, registeredSession,
            restartInterval, retainerlessTimerRunPendingCompletion && Engine.IsRetainerlessTimerRun, completedAtUtc);
        retainerlessTimerWasEnabled = enabled;
        retainerlessTimerAccountId = accountId;
        retainerlessTimerCharacterId = characterId;
        retainerlessTimerIntervalMinutes = interval;

        // Share the existing five-second admission cadence; retain a single expired deadline while blocked.
        if (retainerlessTimerDueUtc is not { } due || now < due || now < nextAutomaticDueCheckUtc ||
            !Engine.RegistryReady || !CanStartAutomaticDueWork() ||
            Condition[ConditionFlag.BoundByDuty95] ||
            Condition[ConditionFlag.Occupied] || Condition[ConditionFlag.Occupied33] ||
            Condition[ConditionFlag.Occupied39] || Condition[ConditionFlag.WatchingCutscene])
            return;

        var arInstalled = PluginInterface.InstalledPlugins.Any(plugin => plugin.InternalName == "AutoRetainer");
        var busy = arInstalled ? AutoRetainerIPC.ReadBusyState() : PluginBusyReadResult.Known(false);
        var suppression = arInstalled ? AutoRetainerIPC.GetSuppressionSnapshot() : default;
        if (!LifecyclePolicy.CanStartRetainerlessTimer(now, retainerlessTimerDueUtc,
                admissionReady: true, arInstalled, busy.Success, busy.Busy, suppression))
            return;
        if (Engine.StartRetainerlessTimerRun())
        {
            retainerlessTimerRunPendingCompletion = true;
            retainerlessTimerDueUtc = null;
        }
    }

    private bool CanStartAutomaticDueWork()
    {
        if (!Configuration.Enabled || !IsCharacterRegistered || !PlayerState.IsLoaded ||
            OfflineLogoutBlocksOrdinaryAutomation || !GameHelpers.IsPlayerAvailable() ||
            DadHandoffBlocksNewWork || VerminionService.HasQuestAcquisition ||
            Engine.OwnsLiveWork || IsFishingRunActive || FishingStartupCoordinator.HasRecoveryPending ||
            pendingBeforeArLogin || pendingFishingPostprocessHandoff ||
            ARPostProcessService.IsRequested || ARPostProcessService.IsProcessing ||
            releaseOnlyPostprocessFinishPending || DeliveryFishing.IsCleanupPending ||
            BeforeArGate is BeforeArGateState.Armed or BeforeArGateState.WaitingForWorldReady
                or BeforeArGateState.Running or BeforeArGateState.ReleasePending ||
            GetActiveManualService().Active || ChocoboStablesService.IsActive ||
            LifestreamIPC.IsBusy() ||
            Condition[ConditionFlag.BoundByDuty] || Condition[ConditionFlag.BoundByDuty56] ||
            Condition[ConditionFlag.InDutyQueue] || Condition[ConditionFlag.WaitingForDuty] ||
            Condition[ConditionFlag.WaitingForDutyFinder]) return false;
        if (!VNavmeshIPC.TryGetPathIsRunning(out var pathRunning) || pathRunning ||
            !VNavmeshIPC.TryGetPathfindInProgress(out var pathfinding) || pathfinding) return false;
        foreach (var addon in new[] { "Talk", "SelectString", "SelectIconString", "SelectYesno", "Shop", "RetainerList", "HousingChocoboList" })
            if (GameHelpers.IsAddonVisible(addon)) return false;
        return ConfigManager.GetActiveConfig().Enabled;
    }

    private void ProcessAutomaticDueTasks()
    {
        var now = DateTime.UtcNow;
        if (now < nextAutomaticDueCheckUtc) return;
        nextAutomaticDueCheckUtc = now.AddSeconds(5);
        if (!CanStartAutomaticDueWork()) return;
        var busy = AutoRetainerIPC.ReadBusyState();
        var suppression = AutoRetainerIPC.GetSuppressionSnapshot();
        if (!busy.Success || busy.Busy || !suppression.RemoteKnown || suppression.RemoteSuppressed) return;

        foreach (var task in Configuration.PostProcessTaskOrder)
        {
            if (task is not (PostProcessTaskOrder.MiniCactpot or PostProcessTaskOrder.ChocoboStables)) continue;
            var key = (PlayerState.ContentId, task);
            if (automaticTaskNextAttemptUtc.TryGetValue(key, out var nextAttempt) && now < nextAttempt) continue;
            if (!Engine.StartScheduledTask(task)) continue;
            // A failed Mini run must not loop immediately; an untrainable stable needs at most an hourly revisit.
            automaticTaskNextAttemptUtc[key] = task == PostProcessTaskOrder.MiniCactpot
                ? now.AddMinutes(5) : now.AddHours(1);
            return;
        }
    }

    private void ProcessChocoboContinuation()
    {
        if (DateTime.UtcNow < nextChocoboContinuationUtc) return;
        nextChocoboContinuationUtc = DateTime.UtcNow.AddSeconds(5);
        if (!Configuration.Enabled || !characterRegistrationCompletedThisLogin || ChocoboRaceService.IsActive ||
            DateTime.UtcNow < ChocoboRaceService.NextContinuationUtc) return;
        var config = ConfigManager.GetActiveConfig();
        if (!config.Enabled || !config.EnableChocoboRacing || config.ChocoboProgressionPaused ||
            config.ChocoboAutomationMode != ChocoboAutomationMode.TargetPedigree || !CanStartChocoboProgression(out _)) return;
        RunDashboardAction(() => ChocoboRaceService.Start());
    }

    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();

    public void QueueCharacterSelectRecoveryAttempt()
        => Framework.RunOnFrameworkThread(CharacterSelectStallRecovery.QueueManualAttempt);

    int IFishingStartupRuntime.PreWindowOffsetMinutes
        => Configuration.OceanFishingPreWindowOffsetMinutes;

    int IFishingStartupRuntime.MaxFisherLevel
        => Math.Clamp(Configuration.FishingMaxFisherLevel, 1, 100);

    bool IFishingStartupRuntime.CanInitiateStartup
        => ClientState.IsLoggedIn &&
           ObjectTable.LocalPlayer != null &&
           !Condition[ConditionFlag.BetweenAreas] &&
           !Condition[ConditionFlag.BetweenAreas51] &&
           !Engine.IsRunning &&
           !CustomDeliveriesService.IsActive &&
           !DadHandoffBlocksNewWork && !VerminionService.HasQuestAcquisition;

    bool IFishingStartupRuntime.IsFishingActive => FishingService.IsActive;
    bool IFishingStartupRuntime.IsRelogActive => FishingRelogCoordinator.IsActive;

    IReadOnlyList<FishingSelectionResult> IFishingStartupRuntime.BuildCandidateQueue()
        => FishingService.BuildFishingCandidateQueue(fishingWindowActive: true);

    FishingLiveFisherLevelSnapshot IFishingStartupRuntime.ReadCurrentFisherLevel()
        => ReadCurrentFisherLevel();

    bool IFishingStartupRuntime.IsFishingRunOwnedForWindow(DateTimeOffset registrationStartUtc)
        => FishingRunLifecycle.IsActiveForWindow(registrationStartUtc);

    bool IFishingStartupRuntime.IsQueueRegistrationConfirmedForWindow(DateTimeOffset registrationStartUtc)
        => FishingRunLifecycle.IsQueueRegistrationConfirmedForWindow(registrationStartUtc);

    bool IFishingStartupRuntime.IsTerminalFailureBeforeQueueConfirmationForWindow(DateTimeOffset registrationStartUtc)
        => FishingRunLifecycle.IsTerminalFailureBeforeQueueConfirmationForWindow(registrationStartUtc);

    void IFishingStartupRuntime.ClearFishingWindowOutcome(DateTimeOffset registrationStartUtc)
        => FishingRunLifecycle.ClearWindowOutcome(registrationStartUtc);

    bool IFishingStartupRuntime.BeginRun(
        FishingRunMode mode,
        FishingStartupTrigger startupTrigger,
        string targetCharacterKey,
        DateTimeOffset registrationStartUtc,
        DateTimeOffset registrationDeadlineUtc)
    {
        if (!PluginInterface.InstalledPlugins.Any(candidate => candidate.IsLoaded && candidate.InternalName == "ADS"))
        {
            const string adsRequired = "ADS is required for Fishing. Install and enable ADS before starting a fishing run.";
            FishingRunLifecycle.ReportBeginFailure(adsRequired);
            Log.Warning($"[Fishing][Startup] {adsRequired}");
            return false;
        }

        var provider = Configuration.OceanFishingProvider;
        if (!AutoHookIPC.TrySynchronizeAutoOceanFish(provider, out var synchronizationStatus))
        {
            var synchronizationError =
                $"Fishing provider setup failed: {synchronizationStatus} Open AutoHook settings and try again.";
            FishingRunLifecycle.ReportBeginFailure(synchronizationError);
            Log.Warning($"[Fishing][Startup] {synchronizationError}");
            return false;
        }

        if (FishingRunLifecycle.TryBegin(mode, startupTrigger, provider, targetCharacterKey, registrationStartUtc, registrationDeadlineUtc, out var error))
            return true;

        Log.Warning($"[Fishing][Startup] Could not begin run: {error}");
        return false;
    }

    void IFishingStartupRuntime.AbortRun(string reason)
        => FishingRunLifecycle.Cleanup(reason);

    bool IFishingStartupRuntime.RequestRelog(string characterKey, DateTimeOffset registrationDeadlineUtc)
        => FishingRelogCoordinator.RequestRelog(characterKey, registrationDeadlineUtc);

    bool IFishingStartupRuntime.StartFishing()
    {
        FishingService.Start();
        return FishingService.IsActive;
    }

    bool IScheduledOfflineHoldRuntime.MasterEnabled => Configuration.Enabled;
    bool IScheduledOfflineHoldRuntime.FeatureEnabled
        => Configuration.LogoutBetweenScheduledOceanFishingVoyages;
    bool IScheduledOfflineHoldRuntime.IsLoggedIn => ClientState.IsLoggedIn;
    ScheduledOfflineHoldState? IScheduledOfflineHoldRuntime.PersistedHold
        => Configuration.ScheduledOfflineHold;

    void IScheduledOfflineHoldRuntime.PersistHold(ScheduledOfflineHoldState? hold)
    {
        Configuration.ScheduledOfflineHold = hold;
        Configuration.Save();
    }

    AutoRetainerMultiModeReadResult IScheduledOfflineHoldRuntime.ReadAutoRetainerMultiMode()
        => AutoRetainerIPC.ReadMultiModeEnabled();

    bool IScheduledOfflineHoldRuntime.TrySetAutoRetainerMultiMode(bool enabled, out string error)
        => AutoRetainerIPC.TrySetMultiModeEnabled(enabled, out error);

    void IScheduledOfflineHoldRuntime.SendLogoutCommand()
        => CommandHelper.SendCommand("/logout");

    bool IScheduledOfflineHoldRuntime.TryConfirmLogout()
        => GameHelpers.IsAddonVisible("SelectYesno") &&
           GameHelpers.TryFireReadyAddonCallback("SelectYesno", true, 0);

    bool IScheduledOfflineHoldRuntime.IsIntentionalFishingWakeReady(out string reason)
    {
        var eligibility = CharacterSelectStallRecovery.GetIntentionalFishingWakeEligibility();
        reason = eligibility.Reason;
        return eligibility.CanAttempt;
    }

    bool IScheduledOfflineHoldRuntime.TryRequestIntentionalFishingWake(out string error)
        => CharacterSelectStallRecovery.TryRequestIntentionalFishingWake(out error);

    bool IScheduledOfflineHoldRuntime.IsScheduledWakeWorldReady(out string reason)
    {
        if (!TryGetWorldReadyCharacterForFishing(out _, out _, out _, out reason))
            return false;

        if (!characterRegistrationCompletedThisLogin)
        {
            reason = string.IsNullOrWhiteSpace(characterRegistrationFailureReason)
                ? "Waiting for bootstrap character registration."
                : $"Bootstrap character registration failed: {characterRegistrationFailureReason}";
            return false;
        }

        reason = "Scheduled wake world-ready.";
        return true;
    }

    FishingStartupResult IScheduledOfflineHoldRuntime.RunScheduledWakeStartup(int preWindowOffsetMinutes)
        => RunFishingStartupTrigger(
            FishingStartupTrigger.ScheduledWake,
            preWindowOffsetMinutes);

    void IScheduledOfflineHoldRuntime.CompleteIntentionalFishingWake()
        => CharacterSelectStallRecovery.CompleteIntentionalFishingWake();

    private static unsafe FishingLiveFisherLevelSnapshot ReadCurrentFisherLevel()
    {
        try
        {
            var playerState = FFXIVClientStructs.FFXIV.Client.Game.UI.PlayerState.Instance();
            if (playerState == null)
            {
                return FishingLiveFisherLevelSnapshot.Unavailable(
                    "Native PlayerState is unavailable.");
            }

            return FishingLiveFisherLevelSnapshot.Ready(
                playerState->GetClassJobLevel(OceanFishingQueuePolicy.FisherJobId, false));
        }
        catch (Exception ex)
        {
            Log.Warning($"[Fishing][Startup] Live Fisher level read failed: {ex.Message}");
            return FishingLiveFisherLevelSnapshot.Unavailable(
                "Native PlayerState Fisher-level read failed.");
        }
    }

    internal void ApplyWindowOpacity(AethertekUI.MaterialWindowOpacity opacity, string windowName)
    {
        var config = Configuration;
        opacity.Apply(windowName, config.UiWindowOpacityPercent / 100f, config.UiTransparencyEnabled,
            config.UiAutoFade, config.UiFadedOpacityPercent / 100f, config.UiUnfocusedDelaySeconds);
    }

    internal void DrawTransparencyToggle()
    {
        var enabled = Configuration.UiTransparencyEnabled;
        if (UiGui.Checkbox("Transparency" + "###window-transparency-main", ref enabled))
        { Configuration.UiTransparencyEnabled = enabled; Configuration.Save(); }
    }

    internal void DrawWindowSettings()
    {
        var config = Configuration;
        var changed = false;
        var compact = config.CompactUi;
        if (UiGui.Checkbox("Compact mode" + "###window-compact-settings", ref compact))
        { config.CompactUi = compact; changed = true; }
        var compactVisible = config.UiCompactVisibleOnMainWindow;
        if (UiGui.Checkbox("Compact visible on main window" + "###window-compact-visible", ref compactVisible))
        { config.UiCompactVisibleOnMainWindow = compactVisible; changed = true; }
        var transparencyVisible = config.UiTransparencyVisibleOnMainWindow;
        if (UiGui.Checkbox("Transparency visible on main window###UiTransparencyVisibleOnMainWindowSettings", ref transparencyVisible))
        { config.UiTransparencyVisibleOnMainWindow = transparencyVisible; changed = true; }
        var languageVisible = config.UiLanguageVisibleOnMainWindow;
        if (UiGui.Checkbox("Language visible on main window" + "###window-language-visible", ref languageVisible))
        { config.UiLanguageVisibleOnMainWindow = languageVisible; changed = true; }
        var enabled = config.UiTransparencyEnabled;
        if (UiGui.Checkbox("Transparency" + "###window-transparency", ref enabled))
        { config.UiTransparencyEnabled = enabled; changed = true; }
        ImGui.BeginDisabled(!config.UiTransparencyEnabled);
        try
        {
        ImGui.SetNextItemWidth(96 * AethertekUI.MaterialTheme.Metrics.Scale);
        var normal = config.UiWindowOpacityPercent;
        if (UiGui.AppearanceInputInt("Opacity (%)" + "###window-opacity", ref normal))
        { config.UiWindowOpacityPercent = normal; changed = true; }
        var autoFade = config.UiAutoFade;
        if (UiGui.Checkbox("Auto-fade when unfocused" + "###window-auto-fade", ref autoFade))
        { config.UiAutoFade = autoFade; changed = true; }
        ImGui.BeginDisabled(!config.UiAutoFade);
        try
        {
        ImGui.SetNextItemWidth(96 * AethertekUI.MaterialTheme.Metrics.Scale);
        var faded = config.UiFadedOpacityPercent;
        if (UiGui.AppearanceInputInt("Unfocused opacity (%)" + "###window-faded-opacity", ref faded))
        { config.UiFadedOpacityPercent = faded; changed = true; }
        ImGui.SetNextItemWidth(96 * AethertekUI.MaterialTheme.Metrics.Scale);
        var delay = config.UiUnfocusedDelaySeconds;
        if (UiGui.AppearanceInputInt("Unfocused delay (seconds)" + "###window-unfocused-delay", ref delay))
        { config.UiUnfocusedDelaySeconds = delay; changed = true; }
        }
        finally { ImGui.EndDisabled(); }
        }
        finally { ImGui.EndDisabled(); }
        if (changed) Configuration.Save();
    }

    internal void DrawLanguageSelector()
    {
        var language = appliedLanguage;
        using var controls = MaterialControls.Push(VermaxionPresentation.Controls(28,18));
        if (!MaterialAppearanceSelector.DrawLanguage("appearance", ref language, languageOptions, 140)) return;
        Configuration.UiLanguage = language;
        Configuration.Save();
    }
}
