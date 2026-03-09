using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using System;
using Vermaxion.Models;
using Vermaxion.Services;
using Vermaxion.Windows;

namespace Vermaxion;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;

    private Configuration _config;
    private MainWindow _mainWindow;
    private CharacterManager _characterManager;
    private GoldSaucerService _goldSaucerService;
    private MiniCactpotService _miniCactpotService;
    private ChocoboRaceService _chocoboRaceService;
    private JumboCactpotService _jumboCactpotService;
    // private VarminionService _varminionService;

    public string Name => "Vermaxion";

    public void Initialize()
    {
        // Initialize configuration
        _config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        _config.Initialize(PluginInterface);

        // Initialize services
        _characterManager = new CharacterManager(_config, ClientState, Log);
        _miniCactpotService = new MiniCactpotService(Log, GameGui, ChatGui);
        _chocoboRaceService = new ChocoboRaceService(Log, GameGui, ChatGui);
        _jumboCactpotService = new JumboCactpotService(Log, GameGui, ChatGui);
        // _varminionService = new VarminionService(_config, Log);
        _goldSaucerService = new GoldSaucerService(_config, _characterManager, Log, 
            _miniCactpotService, _chocoboRaceService, _jumboCactpotService/*, _varminionService*/);

        // Initialize UI
        _mainWindow = new MainWindow(_goldSaucerService, _characterManager, _config, Log);

        // Register commands
        CommandManager.AddHandler("/vermaxion", new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens Vermaxion configuration window"
        });

        CommandManager.AddHandler("/vmini", new CommandInfo(OnMiniCactpotCommand)
        {
            HelpMessage = "Execute Mini Cactpot automation for current character"
        });

        CommandManager.AddHandler("/vrace", new CommandInfo(OnChocoboRaceCommand)
        {
            HelpMessage = "Execute Chocobo Race automation for current character"
        });

        CommandManager.AddHandler("/vjumbo", new CommandInfo(OnJumboCactpotCommand)
        {
            HelpMessage = "Execute Jumbo Cactpot automation for current character"
        });

        CommandManager.AddHandler("/vvarminion", new CommandInfo(OnVarminionCommand)
        {
            HelpMessage = "Execute Varminion automation for current character"
        });

        // Detect current character on startup
        var currentCharacter = _characterManager.GetOrCreateCurrentCharacter();
        if (currentCharacter != null)
        {
            Log.Information($"Detected current character: {currentCharacter.GetDisplayName()}");
        }

        Log.Information("Vermaxion plugin initialized");
    }

    public void Dispose()
    {
        // Cleanup
        CommandManager.RemoveHandler("/vermaxion");
        CommandManager.RemoveHandler("/vmini");
        CommandManager.RemoveHandler("/vrace");
        CommandManager.RemoveHandler("/vjumbo");
        CommandManager.RemoveHandler("/vvarminion");

        _mainWindow?.Dispose();
        _goldSaucerService?.Dispose();
        _characterManager?.Dispose();
        _miniCactpotService?.Dispose();
        _chocoboRaceService?.Dispose();
        _jumboCactpotService?.Dispose();
        // _varminionService?.Dispose();

        Log.Information("Vermaxion plugin disposed");
    }

    private void OnCommand(string command, string args)
    {
        // Toggle main window visibility
        _mainWindow.IsOpen = !_mainWindow.IsOpen;
    }

    private void OnMiniCactpotCommand(string command, string args)
    {
        var currentCharacter = _characterManager.CurrentCharacter;
        if (currentCharacter == null)
        {
            ChatGui.Print("No current character detected");
            return;
        }

        if (!_goldSaucerService.CanExecuteAutomation(ActionType.MiniCactpot, currentCharacter))
        {
            ChatGui.Print("Mini Cactpot automation not available");
            return;
        }

        ChatGui.Print("Starting Mini Cactpot automation...");
        _ = _goldSaucerService.ExecuteAutomation(ActionType.MiniCactpot, currentCharacter);
    }

    private void OnChocoboRaceCommand(string command, string args)
    {
        var currentCharacter = _characterManager.CurrentCharacter;
        if (currentCharacter == null)
        {
            ChatGui.Print("No current character detected");
            return;
        }

        if (!_goldSaucerService.CanExecuteAutomation(ActionType.ChocoboRaces, currentCharacter))
        {
            ChatGui.Print("Chocobo Race automation not available");
            return;
        }

        ChatGui.Print("Starting Chocobo Race automation...");
        _ = _goldSaucerService.ExecuteAutomation(ActionType.ChocoboRaces, currentCharacter);
    }

    private void OnJumboCactpotCommand(string command, string args)
    {
        var currentCharacter = _characterManager.CurrentCharacter;
        if (currentCharacter == null)
        {
            ChatGui.Print("No current character detected");
            return;
        }

        if (!_goldSaucerService.CanExecuteAutomation(ActionType.JumboCactpot, currentCharacter))
        {
            ChatGui.Print("Jumbo Cactpot automation not available");
            return;
        }

        ChatGui.Print("Starting Jumbo Cactpot automation...");
        _ = _goldSaucerService.ExecuteAutomation(ActionType.JumboCactpot, currentCharacter);
    }

    private void OnVarminionCommand(string command, string args)
    {
        var currentCharacter = _characterManager.CurrentCharacter;
        if (currentCharacter == null)
        {
            ChatGui.Print("No current character detected");
            return;
        }

        if (!_goldSaucerService.CanExecuteAutomation(ActionType.Varminion, currentCharacter))
        {
            ChatGui.Print("Varminion automation not available");
            return;
        }

        ChatGui.Print("Starting Varminion automation...");
        _ = _goldSaucerService.ExecuteAutomation(ActionType.Varminion, currentCharacter);
    }
}
