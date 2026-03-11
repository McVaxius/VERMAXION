using System;
using Dalamud.Game.Command;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using VERMAXION.IPC;
using VERMAXION.Services;
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
    public ARPostProcessService ARPostProcessService { get; init; }
    public MinionRouletteService MinionRouletteService { get; init; }
    public SeasonalGearService SeasonalGearService { get; init; }
    public GearUpdaterService GearUpdaterService { get; init; }
    public HighestCombatJobService HighestCombatJobService { get; init; }
    public CurrentJobEquipmentService CurrentJobEquipmentService { get; init; }
    public YesAlreadyIPC YesAlreadyIPC { get; init; }
    public VNavmeshIPC VNavmeshIPC { get; init; }
    public VermaxionEngine Engine { get; init; }

    public readonly WindowSystem WindowSystem = new("VERMAXION");
    private ConfigWindow ConfigWindow { get; init; }
    private MainWindow MainWindow { get; init; }

    private IDtrBarEntry? dtrEntry;
    private bool wasLoggedIn;
    private int loginDetectionDelay;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        ConfigManager = new ConfigManager(PluginInterface, Log);

        if (!string.IsNullOrEmpty(Configuration.LastAccountId))
            ConfigManager.CurrentAccountId = Configuration.LastAccountId;

        // Initialize services
        ResetDetectionService = new ResetDetectionService(Log);
        HenchmanService = new HenchmanService(CommandManager, Log);
        FCBuffService = new FCBuffService(CommandManager, Log, ClientState, Condition, ObjectTable, TargetManager, ConfigManager, this);
        FCBuffInventoryService = new FCBuffInventoryService(CommandManager, Log, GameGui);
        VerminionService = new VerminionService(CommandManager, Condition, Log, PluginInterface);
        CactpotService = new CactpotService(CommandManager, Log, ClientState);
        ChocoboRaceService = new ChocoboRaceService(CommandManager, Log);
        MinionRouletteService = new MinionRouletteService(CommandManager, Log);
        SeasonalGearService = new SeasonalGearService(CommandManager, Log);
        GearUpdaterService = new GearUpdaterService(CommandManager, Log, ClientState, PlayerState);
        HighestCombatJobService = new HighestCombatJobService(CommandManager, Log, PlayerState, ClientState, ObjectTable, DataManager);
        CurrentJobEquipmentService = new CurrentJobEquipmentService(CommandManager, Log, PlayerState);
        YesAlreadyIPC = new YesAlreadyIPC(Log);
        VNavmeshIPC = new VNavmeshIPC(Log, CommandManager);

        // AR PostProcess - fires OnARCharacterReady when AR signals us
        ARPostProcessService = new ARPostProcessService(PluginInterface, Log, OnARCharacterReady);

        // Engine - orchestrates all tasks
        Engine = new VermaxionEngine(
            Log, ConfigManager, ResetDetectionService,
            HenchmanService, FCBuffService, VerminionService,
            CactpotService, ChocoboRaceService, ARPostProcessService);

        // Windows
        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this);
        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);

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

        if (ClientState.IsLoggedIn)
        {
            wasLoggedIn = true;
            loginDetectionDelay = 3;
        }

        Log.Information("===Vermaxion loaded!===");
    }

    public void Dispose()
    {
        Framework.Update -= OnFrameworkUpdate;
        ClientState.Login -= OnLoginEvent;

        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

        WindowSystem.RemoveAllWindows();
        ConfigWindow.Dispose();
        MainWindow.Dispose();

        YesAlreadyIPC.Dispose();
        VNavmeshIPC.Dispose();
        ARPostProcessService.Dispose();
        HighestCombatJobService.Dispose();
        CurrentJobEquipmentService.Dispose();

        dtrEntry?.Remove();

        CommandManager.RemoveHandler(AliasCommandName);
        CommandManager.RemoveHandler(CommandName);
    }

    private void OnARCharacterReady(string pluginName)
    {
        Log.Information($"[Plugin] AR signaled character ready for postprocess");
        Engine.StartPostProcess();
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

            case "cancel":
                Engine.Cancel();
                ChatGui.Print("[Vermaxion] Cancelled");
                break;

            case "config":
                ConfigWindow.Toggle();
                break;

            default:
                MainWindow.Toggle();
                break;
        }
    }

    private void OnLoginEvent()
    {
        loginDetectionDelay = 3;
    }

    private void OnLogin()
    {
        try
        {
            var charName = ObjectTable.LocalPlayer?.Name.ToString() ?? "";
            var worldName = ObjectTable.LocalPlayer?.HomeWorld.Value.Name.ToString() ?? "";
            if (!string.IsNullOrEmpty(charName) && !string.IsNullOrEmpty(worldName))
            {
                var contentId = PlayerState.ContentId;
                Log.Information($"OnLogin: Character={charName}@{worldName}, ContentId={contentId:X16}");
                ConfigManager.EnsureAccountSelected(contentId, null);
                ConfigManager.EnsureCharacterExists(charName, worldName);
                Configuration.LastAccountId = ConfigManager.CurrentAccountId;
                Configuration.Save();
                Log.Information($"Character detected: {charName}@{worldName} -> Account {ConfigManager.CurrentAccountId}");
                // Force reload config after character selection to ensure we have the right character config
                ConfigManager.LoadAllAccounts();
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Error during login detection: {ex.Message}");
        }
    }

    private void OnFrameworkUpdate(IFramework fw)
    {
        // Login detection
        if (ClientState.IsLoggedIn && !wasLoggedIn)
        {
            wasLoggedIn = true;
            loginDetectionDelay = 3;
        }
        else if (!ClientState.IsLoggedIn && wasLoggedIn)
        {
            wasLoggedIn = false;
            loginDetectionDelay = 0;
        }

        if (loginDetectionDelay > 0)
        {
            loginDetectionDelay--;
            if (loginDetectionDelay == 0)
                OnLogin();
        }

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
        string statusText;

        if (Engine.IsRunning)
        {
            statusText = $"VMX: {Engine.StatusText}";
        }
        else
        {
            statusText = config.Enabled ? "VMX: Ready" : "VMX: Off";
        }

        dtrEntry.Text = new SeString(new TextPayload(statusText));
        dtrEntry.Tooltip = new SeString(new TextPayload(
            Engine.IsRunning
                ? $"Vermaxion running: {Engine.StatusText}"
                : config.Enabled
                    ? "Vermaxion ready - waiting for AR postprocess"
                    : "Vermaxion disabled"));
    }

    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();
}
