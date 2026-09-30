// Imported from awgil/ffxiv_satisfy revision 1ab3f9f; adapted for bounded routes and host cleanup.
using System;
using System.Linq;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using VERMAXION.DeliverySupport;
using VERMAXION.Services;

namespace VERMAXION.CustomDeliveries;

public abstract class AutoCommon(DeliveryRoute route) : TaskBase
{
    protected DeliveryRoute Route => route;

    protected async Task TurnIn()
    {
        using var scope = BeginScope("TurnIn");
        var npc = route.Npc;
        ErrorIf(npc.CraftData == null, "Turn-in location is missing");
        using var closeOwnedDeliveryUi = new OnDispose(() =>
        {
            if (Game.IsTurnInRequestInProgress(route.ItemId))
                GameHelpers.TryCloseAddonByCallback("Request");
            if (Game.IsTurnInSupplyInProgress(npc))
                GameHelpers.TryCloseAddonByCallback("SatisfactionSupply");
        });
        var startingUsed = ReadUsedDeliveries(npc.Index);
        var targetUsed = startingUsed + route.Count;
        if (!Game.IsTurnInSupplyInProgress(npc))
        {
            await InteractWithNpc(npc.TurninId, npc.CraftData!.TurnInInstanceId,
                npc.CraftData.TurnInLocation, () => Game.IsTurnInSupplyInProgress(npc));
        }

        while (ReadUsedDeliveries(npc.Index) < targetUsed)
        {
            CancelToken.ThrowIfCancellationRequested();
            ErrorIf(Game.NumItemsInInventory(route.ItemId, (short)route.MinCollectibility) <= 0,
                $"No eligible collectible {ItemName(route.ItemId)} remains");
            Status = $"Turning in to {npc.Name}: {ReadUsedDeliveries(npc.Index) - startingUsed}/{route.Count}";
            await WaitUntilSkipping(() => Game.IsTurnInSupplyInProgress(npc), "Wait for delivery dialog", UiSkipOptions.Talk);
            var before = ReadUsedDeliveries(npc.Index);
            var lastConfirmation = DateTime.MinValue;
            Game.TurnInSupply(route.Slot);
            await WaitUntil(() =>
            {
                if (Game.IsTurnInRequestInProgress(route.ItemId)) return true;
                ConfirmScripCap();
                return false;
            }, "Waiting for collectible request");
            ErrorIf(!Game.TurnInRequestCommit(route.ItemId, (short)route.MinCollectibility),
                "Unable to select and commit the required collectible in the native trade request");
            await WaitUntilSkipping(() =>
            {
                if (ReadUsedDeliveries(npc.Index) > before) return true;
                ConfirmScripCap();
                return false;
            },
                "Verifying delivery allowance changed", UiSkipOptions.Talk);
            Service.Log.Information($"[CustomDeliveries] Verified turn-in: npc={npc.Name}; item={route.ItemId}; usedBefore={before}; usedAfter={ReadUsedDeliveries(npc.Index)}; completed={ReadUsedDeliveries(npc.Index) - startingUsed}/{route.Count}");
            // Route count is capped to the current rank. Resolve a changed request in a new route.
            if (Service.Conditions[ConditionFlag.OccupiedInCutSceneEvent])
                await WaitUntilSkipping(() => !Service.Conditions[ConditionFlag.OccupiedInCutSceneEvent],
                    "Waiting for rank cutscene", UiSkipOptions.Talk);

            void ConfirmScripCap()
            {
                CancelToken.ThrowIfCancellationRequested();
                if (!Game.IsTurnInNpcActive(npc) || DateTime.UtcNow - lastConfirmation < TimeSpan.FromMilliseconds(500))
                    return;
                lastConfirmation = DateTime.UtcNow;
                // Only handle the compensation warning during this owned turn-in.
                // Delivery credit still requires a native allowance change.
                GameHelpers.TryClickYesIfPromptAllowed(DeliveryPlanning.IsScripOverflowPrompt,
                    $"custom delivery to {npc.Name} at the scrip cap", false, out _);
            }
        }
        // Return to the world before another native task can acquire the client.
        GameHelpers.TryCloseAddonByCallback("SatisfactionSupply");
        await WaitUntilSkipping(() => !GameHelpers.IsAddonVisible("SatisfactionSupply")
            && !Service.Conditions[ConditionFlag.OccupiedInEvent]
            && !Service.Conditions[ConditionFlag.OccupiedInCutSceneEvent],
            "Waiting for delivery cleanup", UiSkipOptions.Talk);
    }

    private static unsafe int ReadUsedDeliveries(int index)
    {
        var manager = SatisfactionSupplyManager.Instance();
        if (manager == null || index < 0 || index >= manager->UsedAllowances.Length)
            throw new InvalidOperationException("Custom delivery allowance data is unavailable");
        return manager->UsedAllowances[index];
    }

    protected static string ItemName(uint itemId) => Service.LuminaRow<Lumina.Excel.Sheets.Item>(itemId)?.Name.ToString() ?? itemId.ToString();
}
