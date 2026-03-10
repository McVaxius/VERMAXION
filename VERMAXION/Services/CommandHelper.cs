using System;
using System.Text;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.System.String;

namespace VERMAXION.Services;

public static class CommandHelper
{
    public static unsafe void SendCommand(string command)
    {
        try
        {
            Plugin.Log.Debug($"[CommandHelper] Sending command: {command}");
            
            if (Plugin.CommandManager.ProcessCommand(command))
            {
                Plugin.Log.Debug($"[CommandHelper] CommandManager processed: {command}");
                return;
            }

            var uiModule = UIModule.Instance();
            if (uiModule == null)
            {
                Plugin.Log.Error("UIModule is null, cannot send command");
                return;
            }

            var bytes = Encoding.UTF8.GetBytes(command);
            var utf8String = Utf8String.FromSequence(bytes);
            uiModule->ProcessChatBoxEntry(utf8String, nint.Zero);
            Plugin.Log.Debug($"[CommandHelper] Sent via UIModule: {command}");
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"Command failed [{command}]: {ex.Message}");
        }
    }
}
