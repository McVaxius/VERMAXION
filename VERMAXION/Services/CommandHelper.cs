using System;
using System.Text;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.System.String;

namespace VERMAXION.Services;

public static class CommandHelper
{
    public static void SendCommand(string command) => TrySendCommand(command);

    public static unsafe bool TrySendCommand(string command)
    {
        try
        {
            Plugin.Log.Debug($"[CommandHelper] Sending command: {command}");
            
            if (Plugin.CommandManager.ProcessCommand(command))
            {
                Plugin.Log.Debug($"[CommandHelper] CommandManager processed: {command}");
                return true;
            }

            var uiModule = UIModule.Instance();
            if (uiModule == null)
            {
                Plugin.Log.Error("UIModule is null, cannot send command");
                return false;
            }

            var bytes = Encoding.UTF8.GetBytes(command);
            var utf8String = Utf8String.FromSequence(bytes);
            uiModule->ProcessChatBoxEntry(utf8String, nint.Zero);
            Plugin.Log.Debug($"[CommandHelper] Sent via UIModule: {command}");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"Command failed [{command}]: {ex.Message}");
            return false;
        }
    }
}
