using System;
using System.Linq;
using Dalamud.Game.Command;
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

public sealed class Plugin : IDalamudPlugin
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

    private const string CommandName = "/vermaxion";
    private const string AliasCommandName = "/vmx";

    public Configuration Configuration { get; init; }
    public ConfigManager ConfigManager { get; init; }
    public ResetDetectionService ResetDetectionService { get; init; }
    public HenchmanService HenchmanService { get; init; }
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
    public WorkshopBellService WorkshopBellService { get; init; }
    public VermaxionEngine Engine { get; init; }

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
    private const int BeforeArLoginTimeoutSeconds = 120;
    private const double BeforeArWorldReadyStableSeconds = 2.0;

    public Plugin()
    {
        ECommonsMain.Init(PluginInterface, this);

        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        ConfigManager = new ConfigManager(PluginInterface, Log);
        RegistrableConfigManager = new RegistrableConfigManager(Log, DataManager, PluginInterface.ConfigDirectory.FullName);
        AutoRetainerIPC = new AutoRetainerIPC(PluginInterface, Log);

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
        HenchmanService = new HenchmanService(CommandManager, Log);
        FCBuffService = new FCBuffService(CommandManager, Log, ClientState, Condition, ObjectTable, TargetManager, ConfigManager, this);
        FCBuffInventoryService = new FCBuffInventoryService(CommandManager, Log, GameGui);
        VerminionService = new VerminionService(CommandManager, Condition, Log);
        CactpotService = new CactpotService(CommandManager, Log, ClientState, ConfigManager);
        ChocoboRaceService = new ChocoboRaceService(CommandManager, Log, ConfigManager);
        FashionReportService = new FashionReportService(CommandManager, ClientState, ObjectTable, Log);
        RegisterRegistrablesService = new RegisterRegistrablesService(CommandManager, ObjectTable, Log, ConfigManager);
        MinionRouletteService = new MinionRouletteService(CommandManager, Log);
        SeasonalGearService = new SeasonalGearService(CommandManager, Log);
        GearUpdaterService = new GearUpdaterService(CommandManager, Log, ClientState, PlayerState);
        HighestCombatJobService = new HighestCombatJobService(CommandManager, Log, PlayerState, ClientState, ObjectTable, DataManager);
        CurrentJobEquipmentService = new CurrentJobEquipmentService(CommandManager, Log, PlayerState);
        YesAlreadyIPC = new YesAlreadyIPC(Log);
        VNavmeshIPC = new VNavmeshIPC(Log, CommandManager);
        LifestreamIPC = new LifestreamIPC(PluginInterface, Log, CommandManager);
        MomIPCClient = new MomIPCClient(PluginInterface, Log);
        DadIPCClient = new DadIPCClient(PluginInterface, Log);
        VendorStockService = new VendorStockService(CommandManager, Log, ConfigManager, VNavmeshIPC);
        WorkshopBellService = new WorkshopBellService(Log, LifestreamIPC, VNavmeshIPC);
        RetainerListingRefillService = new RetainerListingRefillService(Log, ConfigManager, VNavmeshIPC, WorkshopBellService, AutoRetainerIPC);

        // AR PostProcess - fires OnARCharacterReady when AR signals us
        ARPostProcessService = new ARPostProcessService(PluginInterface, Log, OnARCharacterReady, ArmBeforeArSuppressionFromPostprocess);

        // Engine - orchestrates all tasks
        Engine = new VermaxionEngine(
            Log, Configuration, ConfigManager, ResetDetectionService,
            HenchmanService, FCBuffService, VerminionService,
            CactpotService, ChocoboRaceService, FashionReportService,
            VendorStockService,
            RegisterRegistrablesService, RetainerListingRefillService, ARPostProcessService, YesAlreadyIPC,
            ClientState, MomIPCClient, DadIPCClient, AutoRetainerIPC);

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

        Log.Information("===Vermaxion loaded!===");
    }

    public void Dispose()
    {
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
        AutoRetainerIPC.ReleaseSuppressionIfOwned();
        YesAlreadyIPC.Dispose();
        VNavmeshIPC.Dispose();
        HighestCombatJobService.Dispose();
        CurrentJobEquipmentService.Dispose();

        dtrEntry?.Remove();

        CommandManager.RemoveHandler(AliasCommandName);
        CommandManager.RemoveHandler(CommandName);

        ECommonsMain.Dispose();
    }

    private void OnARCharacterReady(string pluginName)
    {
        Log.Information($"[Plugin] AR signaled character ready for postprocess");
        Engine.StartPostProcess();
    }

    private void OnCharacterChanged(string oldCharacterKey, string newCharacterKey)
    {
        Log.Information($"[Plugin] Character changed: '{oldCharacterKey}' -> '{newCharacterKey}'");
        
        // Reset all services to prevent state persistence between characters
        try
        {
            Log.Information("[Plugin] Resetting all services due to character change");
            
            FCBuffService.Reset();
            VerminionService.Reset();
            CactpotService.Reset();
            ChocoboRaceService.Reset();
            FashionReportService.Reset();
            VendorStockService.Reset();
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
                AutoRetainerIPC.ReleaseSuppressionIfOwned();
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
                if (!Engine.IsRunning)
                {
                    Engine.ManualStart();
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
        var currentSuppressed = AutoRetainerIPC.GetSuppressed();
        var arBusy = AutoRetainerIPC.IsBusy();
        Log.Information($"[AR] Plugin-load before-AR check: configuredBeforeArCount={configuredCount}, owned={AutoRetainerIPC.SuppressionOwnedByVermaxion}, currentSuppressed={currentSuppressed}, arBusy={arBusy}");

        if (configuredCount == 0)
            return;

        if (arBusy || currentSuppressed)
        {
            Log.Warning($"[AR] Late before-AR skip on plugin load: AutoRetainer already active/queued or suppressed; reason=arBusy:{arBusy}, currentSuppressed:{currentSuppressed}. VMX will arm next login from character postprocess handoff.");
            beforeArStartedThisLogin = true;
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

        var configuredCount = GetConfiguredBeforeAutoRetainerTaskCount();
        Log.Information($"[AR] Before-AR login trigger: reason={reason}, configuredBeforeArCount={configuredCount}");
        if (configuredCount == 0)
            return;

        if (!beforeArArmedByPostprocess && !AutoRetainerIPC.SuppressionOwnedByVermaxion && AutoRetainerIPC.IsBusy())
        {
            Log.Warning($"[AR] Late before-AR skip on login: AutoRetainer already active/queued; reason={reason}. VMX will arm next login from character postprocess handoff.");
            beforeArStartedThisLogin = true;
            return;
        }

        if (!pendingBeforeArLogin)
        {
            pendingBeforeArLogin = true;
            beforeArLoginPendingSince = DateTime.UtcNow;
            beforeArLoginLastDiagnosticAt = DateTime.MinValue;
        }

        var suppressed = AutoRetainerIPC.GetSuppressed();
        Log.Information($"[AR] Before-AR login pending: reason={reason}, owned={AutoRetainerIPC.SuppressionOwnedByVermaxion}, currentSuppressed={suppressed}");
    }

    private void ArmBeforeArSuppressionFromPostprocess()
    {
        var configuredCount = GetConfiguredBeforeAutoRetainerTaskCount();
        var currentSuppressedBefore = AutoRetainerIPC.GetSuppressed();
        Log.Information($"[AR] Postprocess before-AR arm check: configuredBeforeArCount={configuredCount}, owned={AutoRetainerIPC.SuppressionOwnedByVermaxion}, currentSuppressed={currentSuppressedBefore}");

        if (configuredCount == 0)
            return;

        var acquired = AutoRetainerIPC.TryAcquireSuppression();
        var currentSuppressedAfter = AutoRetainerIPC.GetSuppressed();
        beforeArArmedByPostprocess = AutoRetainerIPC.SuppressionOwnedByVermaxion;
        Log.Information($"[AR] Postprocess before-AR suppression arm: acquired={acquired}, armedByPostprocess={beforeArArmedByPostprocess}, owned={AutoRetainerIPC.SuppressionOwnedByVermaxion}, currentSuppressed={currentSuppressedAfter}");
    }

    private void ProcessPendingBeforeArLogin()
    {
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
            if (elapsed.TotalSeconds >= BeforeArLoginTimeoutSeconds)
            {
                Log.Warning($"[AR] Timed out waiting for world-ready login after {BeforeArLoginTimeoutSeconds}s; releasing VMX-owned suppression.");
                AutoRetainerIPC.ReleaseSuppressionIfOwned();
                beforeArArmedByPostprocess = false;
                ClearPendingBeforeArLogin("login resolution timeout");
                return;
            }

            if (!TryGetWorldReadyCharacter(out var charName, out var worldName, out var contentId, out var notReadyReason))
            {
                LogPendingBeforeArDiagnostic(notReadyReason);
                return;
            }

            Log.Information($"[AR] Login world-ready resolved: character={charName}@{worldName}, contentId={contentId:X16}");
            ConfigManager.EnsureAccountSelected(contentId, null);
            ConfigManager.EnsureCharacterExists(charName, worldName);
            Configuration.LastAccountId = ConfigManager.CurrentAccountId;
            Configuration.Save();
            ConfigManager.LoadAllAccounts();

            var activeConfig = ConfigManager.GetActiveConfig();
            var configuredCount = GetConfiguredBeforeAutoRetainerTaskCount();
            var dueTaskIds = Engine.GetRunnableTaskIdsForPhase(PostProcessTaskPhase.BeforeAR).ToList();
            var currentSuppressed = AutoRetainerIPC.GetSuppressed();
            Log.Information($"[AR] Before-AR gate: accountId={ConfigManager.CurrentAccountId}, characterKey='{ConfigManager.CurrentCharacterKey}', configuredBeforeArCount={configuredCount}, dueBeforeArTaskIds=[{string.Join(", ", dueTaskIds)}], owned={AutoRetainerIPC.SuppressionOwnedByVermaxion}, currentSuppressed={currentSuppressed}, configEnabled={activeConfig.Enabled}");

            if (configuredCount == 0)
            {
                Log.Information("[AR] Releasing before-AR suppression: no tasks configured for BeforeAR.");
                AutoRetainerIPC.ReleaseSuppressionIfOwned();
                beforeArArmedByPostprocess = false;
                ClearPendingBeforeArLogin("no BeforeAR tasks configured");
                return;
            }

            if (!activeConfig.Enabled || dueTaskIds.Count == 0)
            {
                Log.Information($"[AR] Releasing before-AR suppression: enabled={activeConfig.Enabled}, dueBeforeArTaskCount={dueTaskIds.Count}.");
                AutoRetainerIPC.ReleaseSuppressionIfOwned();
                beforeArArmedByPostprocess = false;
                ClearPendingBeforeArLogin("no enabled/due BeforeAR tasks");
                return;
            }

            var acquiredSuppression = AutoRetainerIPC.TryAcquireSuppression();
            currentSuppressed = AutoRetainerIPC.GetSuppressed();
            Log.Information($"[AR] Before-AR world-ready suppression acquire: acquired={acquiredSuppression}, owned={AutoRetainerIPC.SuppressionOwnedByVermaxion}, currentSuppressed={currentSuppressed}");
            if (!acquiredSuppression || !AutoRetainerIPC.SuppressionOwnedByVermaxion)
            {
                Log.Warning($"[AR] Skipping before-AR tasks because VMX could not acquire AutoRetainer suppression after world-ready gate; currentSuppressed={currentSuppressed}.");
                beforeArArmedByPostprocess = false;
                ClearPendingBeforeArLogin("suppression not owned by VMX");
                return;
            }

            beforeArStartedThisLogin = true;
            beforeArArmedByPostprocess = false;
            ClearPendingBeforeArLogin("starting before-AR engine");
            Engine.StartBeforeAutoRetainer();
        }
        catch (Exception ex)
        {
            Log.Error($"Error in pending before-AR login processing: {ex.Message}");
            AutoRetainerIPC.ReleaseSuppressionIfOwned();
            beforeArArmedByPostprocess = false;
            ClearPendingBeforeArLogin("exception");
        }
    }

    private void ClearPendingBeforeArLogin(string reason)
    {
        if (!pendingBeforeArLogin)
            return;

        Log.Information($"[AR] Clearing pending before-AR login: reason={reason}, owned={AutoRetainerIPC.SuppressionOwnedByVermaxion}, currentSuppressed={AutoRetainerIPC.GetSuppressed()}");
        pendingBeforeArLogin = false;
        beforeArLoginPendingSince = DateTime.MinValue;
        beforeArLoginLastDiagnosticAt = DateTime.MinValue;
        beforeArWorldReadySince = DateTime.MinValue;
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
        Log.Information($"[AR] Pending before-AR login: {reason}, elapsed={(now - beforeArLoginPendingSince).TotalSeconds:F1}s, owned={AutoRetainerIPC.SuppressionOwnedByVermaxion}, currentSuppressed={AutoRetainerIPC.GetSuppressed()}");
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

    private int GetConfiguredBeforeAutoRetainerTaskCount()
    {
        if (PostProcessTaskOrder.Normalize(Configuration))
            Configuration.Save();

        return Configuration.PostProcessTaskPlacement.Values.Count(phase => phase == PostProcessTaskPhase.BeforeAR);
    }

    private void OnFrameworkUpdate(IFramework fw)
    {
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
            if (beforeArArmedByPostprocess)
                Log.Information("[AR] Preserving postprocess-armed before-AR suppression across logout transition.");
            else
            {
                AutoRetainerIPC.ReleaseSuppressionIfOwned();
                beforeArArmedByPostprocess = false;
            }
            ClearPendingBeforeArLogin("framework logout transition");
        }

        ProcessPendingBeforeArLogin();

        // Update DTR bar
        UpdateDtrBar();

        // Update engine (runs the state machine)
        Engine.Update();

        // Update individual services for manual testing (when not running through engine)
        if (!Engine.IsRunning)
        {
            FCBuffService.Update();
            FCBuffInventoryService.Update();
            VerminionService.Update();
            CactpotService.Update();
            ChocoboRaceService.Update();
            FashionReportService.Update();
            VendorStockService.Update();
            RetainerListingRefillService.Update();
            WorkshopBellService.Update();
            RegisterRegistrablesService.Update();
            MinionRouletteService.Update();
            SeasonalGearService.Update();
            GearUpdaterService.Update();
            CurrentJobEquipmentService.Update();
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

        string statusText;
        string tooltipText;

        switch (Configuration.DtrBarMode)
        {
            case 1: // icon+text
                statusText = $"{glyph} VMX";
                tooltipText = Engine.IsRunning
                    ? $"Vermaxion running: {Engine.StatusText}"
                    : isEnabled
                        ? "Vermaxion ready - waiting for AR postprocess"
                        : "Vermaxion disabled";
                break;
            case 2: // icon-only
                statusText = glyph;
                tooltipText = Engine.IsRunning
                    ? $"Vermaxion: {Engine.StatusText}"
                    : isEnabled
                        ? "Vermaxion ready"
                        : "Vermaxion disabled";
                break;
            default: // text-only
                if (Engine.IsRunning)
                {
                    statusText = $"VMX: {Engine.StatusText}";
                }
                else
                {
                    statusText = isEnabled ? "VMX: Ready" : "VMX: Off";
                }
                tooltipText = Engine.IsRunning
                    ? $"Vermaxion running: {Engine.StatusText}"
                    : isEnabled
                        ? "Vermaxion ready - waiting for AR postprocess"
                        : "Vermaxion disabled";
                break;
        }

        dtrEntry.Text = new SeString(new TextPayload(statusText));
        dtrEntry.Tooltip = new SeString(new TextPayload(tooltipText));
    }

    /// <summary>
    /// FULL STOP - Immediately halts ALL plugin operations, services, and navigation.
    /// </summary>
    public void FullStop()
    {
        Log.Information("[FULL STOP] ========== STOPPING ALL OPERATIONS ==========");

        // Stop engine
        if (Engine.IsRunning)
        {
            Engine.Stop();
            Log.Information("[FULL STOP] Engine stopped");
        }

        MomIPCClient.CancelActiveRun();
        Log.Information("[FULL STOP] mom IPC cancel requested");

        // Stop all services that have state machines
        FCBuffService.Reset();
        VerminionService.Reset();
        CactpotService.Reset();
        ChocoboRaceService.Reset();
        FashionReportService.Reset();
        VendorStockService.Reset();
        RetainerListingRefillService.Reset();
        WorkshopBellService.Reset();
        RegisterRegistrablesService.Reset();
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

        AutoRetainerIPC.ReleaseSuppressionIfOwned();
        beforeArArmedByPostprocess = false;
        Log.Information("[FULL STOP] AutoRetainer suppression released if owned");

        Log.Information("[FULL STOP] ========== ALL OPERATIONS HALTED ==========");
        ChatGui.Print("[Vermaxion] FULL STOP - All operations halted.");
    }

    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();
}
