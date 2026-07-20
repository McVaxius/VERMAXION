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
using Dalamud.Plugin.Services;
using ECommons;
using VERMAXION.IPC;
using VERMAXION.Services;
using VERMAXION.Models;
using VERMAXION.Windows;

namespace VERMAXION;

public sealed class Plugin : IDalamudPlugin, IFishingStartupRuntime
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IDtrBar DtrBar { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static ITargetManager TargetManager { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IUnlockState UnlockState { get; private set; } = null!;
    [PluginService] internal static IDutyState DutyState { get; private set; } = null!;

    private const string CommandName = "/vermaxion";
    private const string AliasCommandName = "/vmx";
    private const string ExpectedDebugPluginPath = @"D:\temp\VERMAXION\VERMAXION\bin\x64\Debug\VERMAXION.dll";

    public Configuration Configuration { get; init; }
    public ConfigManager ConfigManager { get; init; }
    public ResetDetectionService ResetDetectionService { get; init; }
    public FCBuffService FCBuffService { get; init; }
    public FCBuffInventoryService FCBuffInventoryService { get; init; }
    public VerminionService VerminionService { get; init; }
    public CactpotService CactpotService { get; init; }
    public ChocoboRaceService ChocoboRaceService { get; init; }
    public FashionReportService FashionReportService { get; init; }
    public RegisterRegistrablesService RegisterRegistrablesService { get; init; }
    public VendorStockService VendorStockService { get; init; }
    public RetainerListingRefillService RetainerListingRefillService { get; init; }
    public ARPostProcessService ARPostProcessService { get; init; }
    public AutoRetainerIPC AutoRetainerIPC { get; init; }
    internal AutoRetainerSelectionGuard AutoRetainerSelectionGuard { get; init; }
    public AutoHookIPC AutoHookIPC { get; init; }
    public XADatabaseIPCClient XADatabaseIPCClient { get; init; }
    public AdsIpcClient AdsIpcClient { get; init; }
    public RegistrableConfigManager RegistrableConfigManager { get; init; }
    public MinionRouletteService MinionRouletteService { get; init; }
    public SeasonalGearService SeasonalGearService { get; init; }
    public GearUpdaterService GearUpdaterService { get; init; }
    public HighestCombatJobService HighestCombatJobService { get; init; }
    public CurrentJobEquipmentService CurrentJobEquipmentService { get; init; }
    public YesAlreadyIPC YesAlreadyIPC { get; init; }
    public VNavmeshIPC VNavmeshIPC { get; init; }
    public LifestreamIPC LifestreamIPC { get; init; }
    public MomIPCClient MomIPCClient { get; init; }
    public DadIPCClient DadIPCClient { get; init; }
    public LootGoblinIPCClient LootGoblinIPCClient { get; init; }
    public LootGoblinMapGatherService LootGoblinMapGatherService { get; init; }
    public LootGoblinMapGatherManualRunCoordinator LootGoblinMapGatherManualRunCoordinator { get; init; }
    public WorkshopBellService WorkshopBellService { get; init; }
    public FishingService FishingService { get; init; }
    public FishingRelogCoordinator FishingRelogCoordinator { get; init; }
    public FishingStartupCoordinator FishingStartupCoordinator { get; init; }
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
    public RegistrableConfigWindow RegistrableConfigWindow { get; init; }

    private IDtrBarEntry? dtrEntry;
    private bool wasLoggedIn;
    private bool pendingBeforeArLogin;
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
    public bool IsFishingRunActive => FishingRunLifecycle.IsActive ||
                                      FishingService.IsActive ||
                                      FishingRelogCoordinator.IsActive ||
                                      FishingStartupCoordinator.HasPendingRelogContinuation;
    public string FishingRunStatusText
    {
        get
        {
            var prefix = FishingRunLifecycle.StatusPrefix;
            if (FishingRelogCoordinator.IsActive)
                return prefix + FishingRelogCoordinator.StatusText;
            if (FishingStartupCoordinator.HasPendingRelogContinuation)
                return prefix + $"Relog pending {FishingStartupCoordinator.PendingRelogCharacterKey}";
            return FishingService.StatusText;
        }
    }

    public Plugin()
    {
        ECommonsMain.Init(PluginInterface, this);
        LogPluginAssemblyDetails();

        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        ConfigManager = new ConfigManager(PluginInterface, Log);
        ApplyLegacyFishingOperationSettingsIfNeeded();
        RegistrableConfigManager = new RegistrableConfigManager(Log, DataManager, PluginInterface.ConfigDirectory.FullName);
        AutoRetainerIPC = new AutoRetainerIPC(PluginInterface, Log);
        AutoRetainerSelectionGuard = new AutoRetainerSelectionGuard(
            AutoRetainerIPC,
            message => Log.Information(message),
            message => Log.Warning(message));
        AutoHookIPC = new AutoHookIPC(PluginInterface, Log);
        XADatabaseIPCClient = new XADatabaseIPCClient(PluginInterface, Log);
        AdsIpcClient = new AdsIpcClient(PluginInterface, Log);

        if (!string.IsNullOrEmpty(Configuration.LastAccountId))
            ConfigManager.CurrentAccountId = Configuration.LastAccountId;

        if (ClientState.IsLoggedIn)
        {
            wasLoggedIn = true;
            BeginBeforeArLoginPendingFromPluginLoad();
        }

        // Subscribe to character change events
        ConfigManager.OnCharacterChanged += OnCharacterChanged;

        // Initialize services
        ResetDetectionService = new ResetDetectionService(Log);
        FCBuffService = new FCBuffService(CommandManager, Log, ClientState, Condition, ObjectTable, TargetManager, ConfigManager, this);
        FCBuffInventoryService = new FCBuffInventoryService(CommandManager, Log, GameGui);
        VerminionService = new VerminionService(CommandManager, Condition, Log);
        CactpotService = new CactpotService(CommandManager, Log, ClientState, ConfigManager, new SaucyMiniCactpotService(Log));
        ChocoboRaceService = new ChocoboRaceService(CommandManager, Log, ConfigManager);
        FashionReportService = new FashionReportService(CommandManager, ClientState, ObjectTable, Log);
        RegisterRegistrablesService = new RegisterRegistrablesService(CommandManager, ObjectTable, Log, ConfigManager);
        MinionRouletteService = new MinionRouletteService(CommandManager, Log);
        SeasonalGearService = new SeasonalGearService(CommandManager, Log);
        GearUpdaterService = new GearUpdaterService(CommandManager, Log, ClientState, PlayerState);
        HighestCombatJobService = new HighestCombatJobService(CommandManager, Log, PlayerState, ClientState, ObjectTable, DataManager);
        CurrentJobEquipmentService = new CurrentJobEquipmentService(CommandManager, Log, PlayerState);
        YesAlreadyIPC = new YesAlreadyIPC(Log);
        FishingRunLifecycle = new FishingRunLifecycle(Log, YesAlreadyIPC, AutoRetainerIPC, AutoHookIPC);
        FisherGearsetRuntime = new FisherGearsetRuntime();
        FisherGearsetTestService = new FisherGearsetTestService(
            FisherGearsetRuntime,
            Log,
            message => ChatGui.Print(message));
        VNavmeshIPC = new VNavmeshIPC(Log, CommandManager);
        LifestreamIPC = new LifestreamIPC(PluginInterface, Log, CommandManager);
        MomIPCClient = new MomIPCClient(PluginInterface, Log);
        DadIPCClient = new DadIPCClient(PluginInterface, Log);
        LootGoblinIPCClient = new LootGoblinIPCClient(PluginInterface, Log);
        LootGoblinMapGatherService = new LootGoblinMapGatherService(Log, LootGoblinIPCClient);
        LootGoblinMapGatherManualRunCoordinator = new LootGoblinMapGatherManualRunCoordinator(Log, ConfigManager, LootGoblinMapGatherService);
        VendorStockService = new VendorStockService(CommandManager, Log, ConfigManager, VNavmeshIPC);
        WorkshopBellService = new WorkshopBellService(Log, LifestreamIPC, VNavmeshIPC);
        RetainerListingRefillService = new RetainerListingRefillService(Log, ConfigManager, VNavmeshIPC, WorkshopBellService, AutoRetainerIPC);
        IncidentWriter = new VermaxionIncidentWriter(PluginInterface.ConfigDirectory.FullName);

        // AR PostProcess - fires OnARCharacterReady when AR signals us
        ARPostProcessService = new ARPostProcessService(
            PluginInterface,
            Log,
            OnARCharacterReady,
            ArmBeforeArSuppressionFromPostprocess,
            () => !DadHandoffBlocksNewWork);
        FishingRelogCoordinator = new FishingRelogCoordinator(Log, ARPostProcessService, AutoRetainerIPC, ConfigManager);
        FishingService = new FishingService(Log, Configuration, ConfigManager, XADatabaseIPCClient, VendorStockService, AdsIpcClient, VNavmeshIPC, LifestreamIPC, AutoRetainerIPC, FishingRunLifecycle, FisherGearsetRuntime, DutyState);
        FishingStartupCoordinator = new FishingStartupCoordinator(this);

        // Engine - orchestrates all tasks
        Engine = new VermaxionEngine(
            Log, Configuration, ConfigManager, ResetDetectionService,
            FCBuffService, FCBuffInventoryService, VerminionService,
            CactpotService, ChocoboRaceService, FashionReportService,
            VendorStockService, FishingService,
            RegisterRegistrablesService, RetainerListingRefillService, WorkshopBellService, ARPostProcessService, YesAlreadyIPC,
            ClientState, MomIPCClient, DadIPCClient, LootGoblinMapGatherService, AutoRetainerIPC, VNavmeshIPC, LifestreamIPC, IncidentWriter);
        Engine.StartBlocker = () => DadHandoffBlocksNewWork
            ? "A granted or pending DAD handoff reservation blocks new VERMAXION work."
            : null;
        AutomationStatusIpcProvider = new AutomationStatusIpcProvider(PluginInterface, BuildAutomationStatus);
        DadHandoffIpcProvider = new DadHandoffIpcProvider(
            PluginInterface,
            Log,
            AutoRetainerIPC,
            BuildAutomationStatus,
            YieldUnstartedBeforeArGateToDad);

        // Windows
        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this);
        RegistrableConfigWindow = new RegistrableConfigWindow(Log, RegistrableConfigManager, ConfigManager, DataManager);
        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);
        WindowSystem.AddWindow(RegistrableConfigWindow);

        // Commands
        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the Vermaxion main window."
        });
        CommandManager.AddHandler(AliasCommandName, new CommandInfo(OnAliasCommand)
        {
            HelpMessage = "Vermaxion: /vmx [on|off|run|config] or /vmx to open UI."
        });

        // Events
        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
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

    private static void LogPluginAssemblyDetails()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version?.ToString() ?? "unknown";
        var path = string.IsNullOrWhiteSpace(assembly.Location) ? "unknown" : assembly.Location;
        Log.Information($"[Plugin] VERMAXION assembly loaded from '{path}', version '{version}', expected debug path '{ExpectedDebugPluginPath}'");
    }

    public void Dispose()
    {
        DadHandoffIpcProvider.Dispose();
        AutomationStatusIpcProvider.Dispose();
        ChatGui.ChatMessage -= OnChatMessage;
        RecommendedEquipHelper.Dispose();
        Framework.Update -= OnFrameworkUpdate;
        ClientState.Login -= OnLoginEvent;
        ConfigManager.OnCharacterChanged -= OnCharacterChanged;

        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

        WindowSystem.RemoveAllWindows();
        ConfigWindow.Dispose();
        MainWindow.Dispose();

        ARPostProcessService.Dispose();
        FishingService.Dispose();
        FishingRunLifecycle.ForceCleanup("plugin disposal");
        AutoRetainerIPC.ReleaseSuppressionIfOwned(force: true);
        YesAlreadyIPC.Dispose();
        VNavmeshIPC.Dispose();
        HighestCombatJobService.Dispose();
        CurrentJobEquipmentService.Dispose();

        dtrEntry?.Remove();

        CommandManager.RemoveHandler(AliasCommandName);
        CommandManager.RemoveHandler(CommandName);

        ECommonsMain.Dispose();
    }

    private AutomationStatus BuildAutomationStatus()
    {
        var fishingCleanup = FishingRunLifecycle.IsCleanupPending ||
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
            return (true, RegisterRegistrablesService.State.ToString(), $"Register Registrables: {RegisterRegistrablesService.State}");
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
        if (ChocoboRaceService.IsActive)
            return (true, ChocoboRaceService.State.ToString(), ChocoboRaceService.StatusText);
        if (CurrentJobEquipmentService.IsActive)
            return (true, CurrentJobEquipmentService.State.ToString(), CurrentJobEquipmentService.StatusText);
        if (HighestCombatJobService.IsActive)
            return (true, "HighestCombatJob", HighestCombatJobService.StatusText);

        return (false, "Idle", "VERMAXION is idle.");
    }

    private void OnChatMessage(IChatMessage message)
    {
        CactpotService.HandleChatMessage(
            message.LogKind.ToString(),
            message.Sender.TextValue,
            message.Message.TextValue);
    }

    private void OnARCharacterReady(string pluginName)
    {
        Log.Information($"[Plugin] AR signaled character ready for postprocess");

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

            return;
        }

        Engine.StartPostProcess();
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

    public void ResetFishingStartupGate()
    {
        FishingStartupCoordinator.ResetCurrentWindow(DateTimeOffset.UtcNow);
        Log.Information("[Fishing][Startup] Startup gate reset by user");
        ChatGui.Print("[Vermaxion] Fishing startup gate reset.");
    }

    private FishingStartupResult RunFishingStartupTrigger(FishingStartupTrigger trigger)
    {
        var result = FishingStartupCoordinator.Poll(DateTimeOffset.UtcNow, trigger);
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
            var preserveFishingRun = FishingRelogCoordinator.IsActive || FishingStartupCoordinator.HasPendingRelogContinuation;
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
            
            // Reset engine state if running
            if (Engine.IsRunning)
            {
                Log.Information("[Plugin] Stopping engine due to character change");
                Engine.Stop();
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
                var cfg = ConfigManager.GetActiveConfig();
                cfg.Enabled = arg == "on";
                ConfigManager.SaveCurrentAccount();
                Log.Information($"Vermaxion {(cfg.Enabled ? "enabled" : "disabled")} via /vmx {arg}");
                ChatGui.Print($"[Vermaxion] {(cfg.Enabled ? "Enabled" : "Disabled")}");
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
        beforeArStartedThisLogin = false;
        BeginBeforeArLoginPending("ClientState.Login");
    }

    private void BeginBeforeArLoginPendingFromPluginLoad()
    {
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
        if (DadHandoffBlocksNewWork)
            return;

        if (!pendingBeforeArLogin)
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

            Log.Information($"[AR] Login world-ready resolved: character={charName}@{worldName}, contentId={contentId:X16}");
            var characterKey = $"{charName}@{worldName}";
            ConfigManager.EnsureAccountSelected(contentId, null, characterKey);
            ConfigManager.EnsureCharacterExists(charName, worldName);
            ApplyLegacyFishingOperationSettingsIfNeeded();
            Configuration.LastAccountId = ConfigManager.CurrentAccountId;
            Configuration.Save();
            ConfigManager.LoadAllAccounts();

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
        if (BeforeArGate is not (BeforeArGateState.Armed or BeforeArGateState.WaitingForWorldReady))
            return;
        if (BeforeArGate == BeforeArGateState.WaitingForWorldReady && !AutoRetainerIPC.SuppressionOwnedByVermaxion)
            return;

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

    private void OnFrameworkUpdate(IFramework fw)
    {
        DadHandoffIpcProvider.Update();

        // Login detection
        if (ClientState.IsLoggedIn && !wasLoggedIn)
        {
            wasLoggedIn = true;
            beforeArStartedThisLogin = false;
            BeginBeforeArLoginPending("framework login transition");
        }
        else if (!ClientState.IsLoggedIn && wasLoggedIn)
        {
            wasLoggedIn = false;
            beforeArStartedThisLogin = false;
            Log.Information("[AR] Preserving VMX ownership across logout transition.");
            ClearPendingBeforeArLogin("framework logout transition");
        }

        AutoRetainerSelectionGuard.Update(
            Configuration.AutoRestoreRetainerCheckingAfterWork,
            ClientState.IsLoggedIn,
            PlayerState.ContentId,
            DateTime.UtcNow);

        ProcessBeforeArSuppressionRecovery();
        FishingRunLifecycle.Update();
        ProcessFishingRecovery();
        ProcessBeforeArArmedStallGuard();
        ProcessReleaseOnlyPostprocessFinishPending();
        ProcessBeforeArReleasePending();
        var fishingContinuationBlocksBeforeAr = ProcessPendingFishingRelogContinuation();
        if (!fishingContinuationBlocksBeforeAr)
            ProcessPendingBeforeArLogin();

        // Update engine (runs the state machine)
        Engine.Update();
        FisherGearsetTestService.Update();
        UpdateBeforeArGateAfterEngine();
        ProcessBeforeArReleasePending();

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
            CurrentJobEquipmentService.Update();
            FishingService.Update();
        }
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

        dtrEntry.Shown = Configuration.DtrBarEnabled;
        if (!Configuration.DtrBarEnabled) return;

        var config = ConfigManager.GetActiveConfig();
        var isEnabled = config?.Enabled ?? false;
        
        // DTR modes: 0=text-only, 1=icon+text, 2=icon-only
        var iconEnabled = string.IsNullOrEmpty(Configuration.DtrIconEnabled) ? "\uE03C" : Configuration.DtrIconEnabled;
        var iconDisabled = string.IsNullOrEmpty(Configuration.DtrIconDisabled) ? "\uE03D" : Configuration.DtrIconDisabled;
        var glyph = isEnabled ? iconEnabled : iconDisabled;
        var operationalStatus = GetDtrOperationalStatus();

        string statusText;
        string tooltipText;

        switch (Configuration.DtrBarMode)
        {
            case 1: // icon+text
                statusText = $"{glyph} VMX";
                tooltipText = operationalStatus != null
                    ? $"Vermaxion: {operationalStatus}"
                    : isEnabled
                        ? "Vermaxion ready - waiting for AR postprocess"
                        : "Vermaxion disabled";
                break;
            case 2: // icon-only
                statusText = glyph;
                tooltipText = operationalStatus != null
                    ? $"Vermaxion: {operationalStatus}"
                    : isEnabled
                        ? "Vermaxion ready"
                        : "Vermaxion disabled";
                break;
            default: // text-only
                if (operationalStatus != null)
                {
                    statusText = $"VMX: {operationalStatus}";
                }
                else
                {
                    statusText = isEnabled ? "VMX: Ready" : "VMX: Off";
                }
                tooltipText = operationalStatus != null
                    ? $"Vermaxion: {operationalStatus}"
                    : isEnabled
                        ? "Vermaxion ready - waiting for AR postprocess"
                        : "Vermaxion disabled";
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
    public void FullStop()
    {
        Log.Information("[FULL STOP] ========== STOPPING ALL OPERATIONS ==========");

        FisherGearsetTestService.Cancel();
        if (FishingRunLifecycle.Mode != FishingRunMode.Test)
            FishingStartupCoordinator.SuppressCurrentWindow(DateTimeOffset.UtcNow);
        FishingStartupCoordinator.CancelPendingRun();
        ClearFishingRelogContinuationReadiness();

        LootGoblinMapGatherManualRunCoordinator.Cancel();
        Log.Information("[FULL STOP] LootGoblin map gather cancel requested");

        Engine.ForceStop();
        Log.Information("[FULL STOP] Engine force-stopped");

        MomIPCClient.CancelActiveRun();
        Log.Information("[FULL STOP] mom IPC cancel requested");

        // Stop all services that have state machines
        FCBuffService.Reset();
        VerminionService.Reset();
        CactpotService.Reset();
        ChocoboRaceService.Reset();
        FashionReportService.Reset();
        VendorStockService.Reset();
        FishingService.Reset();
        FishingRelogCoordinator.Reset();
        FishingRunLifecycle.ForceCleanup("Full Stop");
        RetainerListingRefillService.Reset();
        WorkshopBellService.Reset();
        RegisterRegistrablesService.Reset();
        LootGoblinMapGatherManualRunCoordinator.Reset();
        MinionRouletteService.Reset();
        SeasonalGearService.Reset();
        GearUpdaterService.Reset();
        Log.Information("[FULL STOP] All services reset");

        // Stop VNavmesh navigation
        VNavmeshIPC.Stop();
        Log.Information("[FULL STOP] VNavmesh stopped");

        // Unpause YesAlready
        YesAlreadyIPC.Unpause();
        Log.Information("[FULL STOP] YesAlready unpaused");

        AutoRetainerIPC.ReleaseSuppressionIfOwned(force: true);
        releaseOnlyPostprocessFinishPending = false;
        releaseOnlyPostprocessFinishReason = string.Empty;
        beforeArArmedByPostprocess = false;
        ClearBeforeArArmedTracking();
        pendingBeforeArLogin = false;
        SetBeforeArGate(BeforeArGateState.Idle, "Full Stop");
        Log.Information("[FULL STOP] AutoRetainer suppression released if owned");

        Log.Information("[FULL STOP] ========== ALL OPERATIONS HALTED ==========");
        ChatGui.Print("[Vermaxion] FULL STOP - All operations halted.");
    }

    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();

    int IFishingStartupRuntime.PreWindowOffsetMinutes
        => Configuration.OceanFishingPreWindowOffsetMinutes;

    bool IFishingStartupRuntime.CanInitiateStartup
        => ClientState.IsLoggedIn &&
           ObjectTable.LocalPlayer != null &&
           !Condition[ConditionFlag.BetweenAreas] &&
           !Condition[ConditionFlag.BetweenAreas51] &&
           !Engine.IsRunning &&
           !DadHandoffBlocksNewWork;

    bool IFishingStartupRuntime.IsFishingActive => FishingService.IsActive;
    bool IFishingStartupRuntime.IsRelogActive => FishingRelogCoordinator.IsActive;

    IReadOnlyList<FishingSelectionResult> IFishingStartupRuntime.BuildCandidateQueue()
        => FishingService.BuildFishingCandidateQueue(fishingWindowActive: true);

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
        string targetCharacterKey,
        DateTimeOffset registrationStartUtc,
        DateTimeOffset registrationDeadlineUtc)
    {
        if (FishingRunLifecycle.TryBegin(mode, targetCharacterKey, registrationStartUtc, registrationDeadlineUtc, out var error))
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
}
