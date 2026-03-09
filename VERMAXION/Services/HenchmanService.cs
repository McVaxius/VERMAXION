using Dalamud.Game.Command;
using Dalamud.Plugin.Services;

namespace VERMAXION.Services;

public class HenchmanService
{
    private readonly ICommandManager commandManager;
    private readonly IPluginLog log;
    private bool wasRunning = false;

    public bool IsManaging { get; private set; } = false;

    public HenchmanService(ICommandManager commandManager, IPluginLog log)
    {
        this.commandManager = commandManager;
        this.log = log;
    }

    public void StopHenchman()
    {
        wasRunning = true;
        IsManaging = true;
        log.Information("[Henchman] Stopping Henchman via /henchman off");
        commandManager.ProcessCommand("/henchman off");
    }

    public void StartHenchman()
    {
        if (wasRunning)
        {
            log.Information("[Henchman] Restarting Henchman via /henchman on");
            commandManager.ProcessCommand("/henchman on");
        }
        wasRunning = false;
        IsManaging = false;
    }

    public void ForceRestart()
    {
        log.Information("[Henchman] Force restarting Henchman via /henchman on");
        commandManager.ProcessCommand("/henchman on");
        wasRunning = false;
        IsManaging = false;
    }
}
