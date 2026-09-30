// Imported from awgil/ffxiv_satisfy revision 1ab3f9f; adapted for VERMAXION.
using System;
using System.Collections.Generic;
using System.Linq;
using VERMAXION.DeliverySupport;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Ipc;
using System.Threading.Tasks;

namespace VERMAXION.CustomDeliveries;

// execute full crafting delivery: teleport to zone, buy ingredients if needed, craft if needed, turn in
public sealed class AutoCraft(DeliveryRoute route) : AutoCommon(route)
{
    private readonly ICallGateSubscriber<ushort, int, object> _artisanCraft = Service.PluginInterface.GetIpcSubscriber<ushort, int, object>("Artisan.CraftItem");
    private readonly ICallGateSubscriber<bool> _artisanInProgress = Service.PluginInterface.GetIpcSubscriber<bool>("Artisan.GetEnduranceStatus");
    private readonly ICallGateSubscriber<bool, object> _artisanSetEndurance = Service.PluginInterface.GetIpcSubscriber<bool, object>("Artisan.SetEnduranceStatus");

    protected override async Task Execute()
    {
        var npc = Route.Npc;
        var remainingTurnins = Route.Count;
        if (remainingTurnins <= 0)
            return; // nothing to do

        if (npc.CraftData == null)
            throw new Exception("Craft data is not initialized");

        var turnInItemId = Route.ItemId;
        var remainingCrafts = DeliveryPlanning.MissingItems(remainingTurnins,
            Game.NumItemsInInventory(turnInItemId, (short)Route.MinCollectibility));
        await EquipJob(Route.Job);
        if (remainingCrafts > 0)
        {
            var ingredient = CraftTurnin.GetCraftIngredient(turnInItemId, Route.Job);
            ErrorIf(ingredient.id == 0 || ingredient.count <= 0, "Crafting ingredient data is missing");
            var requiredIngredients = ingredient.count * remainingCrafts;
            var missingIngredients = requiredIngredients - Game.NumItemsInInventory(ingredient.id, 0);
            if (missingIngredients > 0)
            {
                Status = $"Buying {missingIngredients}x {ItemName(ingredient.id)}";
                if (Service.ClientState.TerritoryType == npc.TerritoryId)
                    await MoveTo(npc.CraftData.VendorLocation, MovementConfig.InteractRange, allowTeleportIfFaster: false, allowAethernet: false);
                else
                    await MoveTo(npc.TerritoryId, npc.CraftData.VendorLocation, MovementConfig.InteractRange, allowTeleportIfFaster: false, allowAethernetWithinTerritory: false);
                await Dismount();
                await BuyFromShop(npc.CraftData, ingredient.id, missingIngredients);
            }
            Status = $"Crafting {remainingCrafts}x {ItemName(turnInItemId)}";
            await CraftItem(turnInItemId, remainingCrafts, remainingTurnins);
        }

        Status = $"Turning in {remainingTurnins}x {ItemName(turnInItemId)}";
        if (Service.ClientState.TerritoryType == npc.TerritoryId)
            await MoveTo(npc.CraftData.TurnInLocation, MovementConfig.InteractRange, allowTeleportIfFaster: false, allowAethernet: false);
        else
            await MoveTo(npc.TerritoryId, npc.CraftData.TurnInLocation, MovementConfig.InteractRange, allowTeleportIfFaster: false, allowAethernetWithinTerritory: false);
        await TurnIn();
    }

    private async Task BuyFromShop(CraftTurnin data, uint itemId, int count)
    {
        using var scope = BeginScope("Buy");
        var shopId = data.VendorShopId;
        var vendorInstanceId = data.VendorInstanceId;
        using var closeOwnedShop = new OnDispose(() => { if (Game.IsShopOpen(shopId)) Game.CloseShop(); });
        if (!Game.IsShopOpen(shopId))
        {
            Log("Opening shop...");
            await InteractWithNpc(data.VendorBaseId, vendorInstanceId, data.VendorLocation,
                () => Game.IsShopOpen(shopId), obj => Game.OpenShop(obj, shopId), selectStringIndex: null);
            await WaitWhile(() => !Service.Conditions[ConditionFlag.OccupiedInEvent], "WaitForCondition");
        }

        Log("Buying...");
        ErrorIf(!Game.BuyItemFromShop(shopId, itemId, count), $"Failed to buy {count}x {itemId} from shop {vendorInstanceId:X}.{shopId:X}");
        await WaitWhile(() => Game.ShopTransactionInProgress(shopId), "Transaction");
        Log("Closing shop...");
        ErrorIf(!Game.CloseShop(), $"Failed to close shop {vendorInstanceId:X}.{shopId:X}");
        await WaitWhile(() => Game.IsShopOpen(), "WaitForClose");
        await WaitWhile(() => Service.Conditions[ConditionFlag.OccupiedInEvent], "WaitForCondition");
        await NextFrame();
    }

    private async Task CraftItem(uint itemId, int count, int finalCount)
    {
        using var scope = BeginScope("Craft");
        if (CraftTurnin.GetRecipe(itemId, Route.Job) is { RowId: var rowId } && rowId != 0)
        {
            ErrorIf(ArtisanInProgress(), "Artisan is already running another crafting request");
            using var stopOwnedCrafting = new OnDispose(() => _artisanSetEndurance.InvokeAction(false));
            ArtisanCraft((ushort)rowId, count);
            await WaitWhile(() => !ArtisanInProgress(), "WaitStart");
            await WaitWhile(ArtisanInProgress, "WaitProgress");
            await WaitWhile(() => !Service.Conditions[ConditionFlag.PreparingToCraft], "WaitFinish");
            ErrorIf(Game.NumItemsInInventory(itemId, (short)Route.MinCollectibility) < finalCount, $"Artisan did not produce enough qualifying collectibles for {ItemName(itemId)}");
            await NextFrame();

            Game.ExitCrafting();
            await WaitWhile(() => Service.Conditions[ConditionFlag.Crafting], "WaitCraftClose");
        }
        else
            Error($"Failed to find recipe for {itemId}");
    }

    private void ArtisanCraft(ushort recipe, int count)
    {
        Service.Log.Information($"[CustomDeliveries][Crafting] Artisan request: recipe={recipe}; toProduce={count}; plannedTurnins={Route.Count}");
        _artisanCraft.InvokeAction(recipe, count);
    }
    private bool ArtisanInProgress() => _artisanInProgress.InvokeFunc();

}
