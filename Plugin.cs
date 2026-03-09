using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Interface.Windowing;
using System;
using Vermaxion.Services;
using Vermaxion.Windows;

namespace Vermaxion;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private Configuration _config;
    private MainWindow _mainWindow;
    private VerminionService _verminionService;
    private WindowSystem _windowSystem;

    public string Name => "Vermaxion";

    public Plugin()
    {
        // Initialize configuration
        _config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        _config.Initialize(PluginInterface);

        // Initialize service
        _verminionService = new VerminionService(_config, Log);

        // Initialize UI
        _windowSystem = new WindowSystem("Vermaxion");
        _mainWindow = new MainWindow(_verminionService, _config);
        _windowSystem.AddWindow(_mainWindow);
        PluginInterface.UiBuilder.Draw += _windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += () => _mainWindow.IsOpen = true;

        // Register command
        CommandManager.AddHandler("/vermaxion", new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens Vermaxion main window"
        });

        Log.Information("[Vermaxion] Plugin initialized");
    }

    public void Dispose()
    {
        // Cleanup
        CommandManager.RemoveHandler("/vermaxion");
        PluginInterface.UiBuilder.Draw -= _windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= () => _mainWindow.IsOpen = true;
        _windowSystem.RemoveAllWindows();
        _verminionService?.Dispose();

        Log.Information("[Vermaxion] Plugin disposed");
    }

    private void OnCommand(string command, string args)
    {
        _mainWindow.IsOpen = !_mainWindow.IsOpen;
    }
}
