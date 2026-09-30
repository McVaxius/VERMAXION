using System;
using System.Threading.Tasks;
using FFXIVClientStructs.FFXIV.Client.Game;
using VERMAXION.DeliverySupport;
using VERMAXION.Services;

namespace VERMAXION.CustomDeliveries;

// Only the game's pending satisfaction-rank quest is handed to Wiggly.
public sealed class AutoDeliveryQuest(NPCInfo npc, ushort questId, uint job) : TaskBase
{
    internal static string? GetBlockedReason()
    {
        try
        {
            WigglyGathering.ResolvePlugin();
            if (!Service.PluginInterface.GetIpcSubscriber<bool>("WigglyQuest.IsRunning").HasFunction
                || !Service.PluginInterface.GetIpcSubscriber<string?>("WigglyQuest.GetCurrentQuestId").HasFunction
                || !Service.PluginInterface.GetIpcSubscriber<string, bool>("WigglyQuest.StartSingleQuest").HasFunction
                || !Service.PluginInterface.GetIpcSubscriber<string, bool>("WigglyQuest.IsQuestLocked").HasFunction
                || !Service.PluginInterface.GetIpcSubscriber<string, bool>("WigglyQuest.IsReadyToAcceptQuest").HasFunction
                || !Service.PluginInterface.GetIpcSubscriber<string, bool>("WigglyQuest.IsQuestAccepted").HasFunction)
                return "Wiggly Questionable's single-quest integration is unavailable";
            if (Service.PluginInterface.GetIpcSubscriber<bool>("WigglyQuest.IsRunning").InvokeFunc())
                return "Wiggly Questionable is already running another task";
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    protected override async Task Execute()
    {
        var blocker = GetBlockedReason();
        ErrorIf(blocker != null, blocker ?? string.Empty);
        var provider = WigglyGathering.ResolvePlugin();
        var character = Service.PlayerState.ContentId;
        var currentQuest = Service.PluginInterface.GetIpcSubscriber<string?>("WigglyQuest.GetCurrentQuestId");
        var running = Service.PluginInterface.GetIpcSubscriber<bool>("WigglyQuest.IsRunning");
        var quest = questId.ToString();
        var name = Service.LuminaRow<Lumina.Excel.Sheets.Quest>((uint)questId + 65536)?.Name.ToString() ?? quest;
        var dispatched = false;
        using var ownedQuest = new OnDispose(() =>
        {
            if (dispatched && ReferenceEquals(WigglyGathering.ResolvePlugin(), provider)
                && (Service.PlayerState.ContentId == character || !Service.ClientState.IsLoggedIn)
                && currentQuest.InvokeFunc() == quest)
                Plugin.CommandManager.ProcessCommand("/wqst stop");
        });
        await EquipJob(job);
        if (npc.CraftData is { } position)
            await MoveTo(npc.TerritoryId, position.TurnInLocation, MovementConfig.InteractRange);
        await WaitUntil(GameHelpers.IsPlayerAvailable, "Waiting before rank quest");
        CancelToken.ThrowIfCancellationRequested();
        ErrorIf(!ReferenceEquals(WigglyGathering.ResolvePlugin(), provider), "Wiggly Questionable reloaded before the rank quest");
        ErrorIf(running.InvokeFunc(), "Another Wiggly Questionable task started before the rank quest");
        if (QuestManager.IsQuestComplete(questId)) return;
        ErrorIf(Service.PluginInterface.GetIpcSubscriber<string, bool>("WigglyQuest.IsQuestLocked").InvokeFunc(quest),
            $"{name} is locked or its Wiggly Questionable route is unavailable");
        ErrorIf(!Service.PluginInterface.GetIpcSubscriber<string, bool>("WigglyQuest.IsQuestAccepted").InvokeFunc(quest)
            && !Service.PluginInterface.GetIpcSubscriber<string, bool>("WigglyQuest.IsReadyToAcceptQuest").InvokeFunc(quest),
            $"{name} cannot yet be accepted; check its job, level and game prerequisites");
        Status = $"Completing {name} through Wiggly Questionable";
        dispatched = Service.PluginInterface.GetIpcSubscriber<string, bool>("WigglyQuest.StartSingleQuest").InvokeFunc(quest);
        ErrorIf(!dispatched, $"Wiggly Questionable rejected the single-quest route for {name}");
        Service.Log.Information($"[CustomDeliveries][Quest] Dispatched rank quest: npc={npc.Name}; quest={quest}; name={name}");
        var acknowledgementDeadline = DateTime.UtcNow.AddSeconds(5);
        var acknowledged = false;
        while (!QuestManager.IsQuestComplete(questId))
        {
            CancelToken.ThrowIfCancellationRequested();
            ErrorIf(Service.PlayerState.ContentId != character, "Character changed during the rank quest");
            ErrorIf(!ReferenceEquals(WigglyGathering.ResolvePlugin(), provider), "Wiggly Questionable reloaded during the rank quest");
            var current = currentQuest.InvokeFunc();
            var isRunning = running.InvokeFunc();
            if (current == quest && isRunning)
            {
                if (!acknowledged)
                    Service.Log.Information($"[CustomDeliveries][Quest] Single-quest execution acknowledged: npc={npc.Name}; quest={quest}");
                acknowledged = true;
            }
            else
            {
                ErrorIf(acknowledged, $"{name} stopped or was replaced before native completion");
                ErrorIf(DateTime.UtcNow >= acknowledgementDeadline, $"Wiggly Questionable did not start {name}; check its stop conditions and route");
            }
            await NextFrame();
        }
        Service.Log.Information($"[CustomDeliveries][Quest] Native completion verified: npc={npc.Name}; quest={quest}; name={name}");
        ownedQuest.Dispose();
        await WaitUntil(() =>
        {
            ErrorIf(!ReferenceEquals(WigglyGathering.ResolvePlugin(), provider), "Wiggly Questionable reloaded during rank-quest cleanup");
            ErrorIf(running.InvokeFunc() && currentQuest.InvokeFunc() != quest,
                "Another Wiggly Questionable quest owns the character after the rank quest");
            return !running.InvokeFunc() && GameHelpers.IsPlayerAvailable();
        }, "Waiting for rank-quest cleanup");
    }
}
