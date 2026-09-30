// Imported from awgil/ffxiv_satisfy revision 1ab3f9f; adapted for VERMAXION.
using System;
using System.Collections.Generic;
using System.Linq;
using IGameObject = Dalamud.Game.ClientState.Objects.Types.IGameObject;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.Network;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.Interop;
using System.Numerics;
using VERMAXION.Services;

namespace VERMAXION.CustomDeliveries;

// utilities for interacting with game
public static unsafe class Game
{
    public static int NumItemsInInventory(uint itemId, short minCollectibility)
    {
        var manager = InventoryManager.Instance();
        if (manager == null) return 0;
        var count = 0;
        foreach (var type in new[] { InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4 })
        {
            var container = manager->GetInventoryContainer(type);
            if (container == null || !container->IsLoaded) continue;
            for (var slot = 0; slot < container->Size; ++slot)
            {
                var item = container->GetInventorySlot(slot);
                if (item == null || item->ItemId != itemId) continue;
                if (minCollectibility > 0 && (!item->Flags.HasFlag(InventoryItem.ItemFlags.Collectable)
                    || item->SpiritbondOrCollectability < minCollectibility)) continue;
                count += checked((int)item->Quantity);
            }
        }
        return count;
    }

    public static AtkUnitBase* GetFocusedAddonByID(uint id)
    {
        var stage = AtkStage.Instance();
        if (stage == null || stage->RaptureAtkUnitManager == null) return null;
        var unitManager = &stage->RaptureAtkUnitManager->AtkUnitManager.FocusedUnitsList;
        foreach (var j in Enumerable.Range(0, Math.Min(unitManager->Count, unitManager->Entries.Length)))
        {
            var unitBase = unitManager->Entries[j].Value;
            if (unitBase != null && unitBase->Id == id)
            {
                return unitBase;
            }
        }
        return null;
    }

    public static bool IsShopOpen(uint shopId = 0)
    {
        var agent = AgentShop.Instance();
        var addon = agent != null ? GetFocusedAddonByID(agent->AddonId) : null;
        if (agent == null || !agent->IsAgentActive() || agent->EventReceiver == null || addon == null || !addon->IsReady)
            return false;
        if (shopId == 0)
            return true; // some shop is open...
        if (!EventFramework.Instance()->EventHandlerModule.EventHandlerMap.TryGetValuePointer(shopId, out var eh) || eh == null || eh->Value == null)
            return false;
        var proxy = (ShopEventHandler.AgentProxy*)agent->EventReceiver;
        return proxy->Handler == eh->Value;
    }

    public static bool OpenShop(IGameObject vendorObject, uint shopId)
    {
        var vendor = (GameObject*)vendorObject.Address;
        if (vendor == null) return false;
        var selector = EventHandlerSelector.Instance();
        if (selector == null || selector->Target == null
            || (!GameHelpers.IsAddonVisible("SelectString") && !GameHelpers.IsAddonVisible("SelectIconString")))
            return GameHelpers.InteractWithObject(vendorObject);

        if (selector->Target != vendor)
        {
            Service.Log.Error($"Unexpected selector target {(ulong)selector->Target->GetGameObjectId():X} when trying to interact with {(ulong)vendor->GetGameObjectId():X}");
            return false;
        }

        for (int i = 0; i < selector->OptionsCount; ++i)
        {
            if (selector->Options[i].Handler->Info.EventId.Id == shopId)
            {
                Service.Log.Debug($"Selecting selector option {i} for shop {shopId:X}");
                EventFramework.Instance()->InteractWithHandlerFromSelector(i);
                return true;
            }
        }

        Service.Log.Error($"Failed to find shop {shopId:X} in selector for {(ulong)vendor->GetGameObjectId():X}");
        return false;
    }

    public static bool CloseShop()
    {
        var agent = AgentShop.Instance();
        if (agent == null || agent->EventReceiver == null)
            return false;
        AtkValue res = default, arg = default;
        var proxy = (ShopEventHandler.AgentProxy*)agent->EventReceiver;
        proxy->Handler->CancelInteraction();
        arg.SetInt(-1);
        agent->ReceiveEvent(&res, &arg, 1, 0);
        return true;
    }

    public static bool BuyItemFromShop(uint shopId, uint itemId, int count)
    {
        if (!EventFramework.Instance()->EventHandlerModule.EventHandlerMap.TryGetValuePointer(shopId, out var eh) || eh == null || eh->Value == null)
        {
            Service.Log.Error($"Event handler for shop {shopId:X} not found");
            return false;
        }

        if (eh->Value->Info.EventId.ContentId != EventHandlerContent.Shop)
        {
            Service.Log.Error($"{shopId:X} is not a shop");
            return false;
        }

        var shop = (ShopEventHandler*)eh->Value;
        for (int i = 0; i < shop->VisibleItemsCount; ++i)
        {
            var index = shop->VisibleItems[i];
            if (shop->Items[index].ItemId == itemId)
            {
                Service.Log.Debug($"Buying {count}x {itemId} from {shopId:X}");
                shop->BuyItemIndex = index;
                shop->ExecuteBuy(count);
                return true;
            }
        }

        Service.Log.Error($"Did not find item {itemId} in shop {shopId:X}");
        return false;
    }

    public static bool ShopTransactionInProgress(uint shopId)
    {
        if (!EventFramework.Instance()->EventHandlerModule.EventHandlerMap.TryGetValuePointer(shopId, out var eh) || eh == null || eh->Value == null)
        {
            Service.Log.Error($"Event handler for shop {shopId:X} not found");
            return false;
        }

        if (eh->Value->Info.EventId.ContentId != EventHandlerContent.Shop)
        {
            Service.Log.Error($"{shopId:X} is not a shop");
            return false;
        }

        var shop = (ShopEventHandler*)eh->Value;
        return shop->WaitingForTransactionToFinish;
    }

    public static void ExitCrafting()
    {
        //AtkValue res = default, param = default;
        //param.SetInt(-1);
        //AgentRecipeNote.Instance()->ReceiveEvent(&res, &param, 1, 0);
        AgentRecipeNote.Instance()->Hide();
    }

    public static bool IsTalkInProgress()
    {
        var addon = RaptureAtkUnitManager.Instance()->GetAddonByName("Talk");
        return addon != null && addon->IsVisible && addon->IsReady;
    }

    public static void ProgressTalk()
    {
        var addon = RaptureAtkUnitManager.Instance()->GetAddonByName("Talk");
        if (addon != null && addon->IsReady)
        {
            var evt = new AtkEvent() { Listener = &addon->AtkEventListener, Target = &AtkStage.Instance()->AtkEventTarget };
            var data = new AtkEventData();
            addon->ReceiveEvent(AtkEventType.MouseClick, 0, &evt, &data);
        }
    }

    // TODO: this really needs revision...
    public static void SelectTurnIn()
    {
        var addon = RaptureAtkUnitManager.Instance()->GetAddonByName("SelectString");
        if (addon != null && addon->IsReady)
        {
            AtkValue val = default;
            val.SetInt(0);
            addon->FireCallback(1, &val, true);
        }
    }

    public static bool IsTurnInSupplyInProgress(NPCInfo npc)
    {
        var agent = AgentSatisfactionSupply.Instance();
        if (agent == null) return false;
        var addon = GetFocusedAddonByID(agent->AddonId);
        return agent->IsAgentActive() && agent->NpcInfo.Id == npc.Index + 1 && agent->NpcInfo.Valid && agent->NpcInfo.Initialized && addon != null && addon->IsVisible;
    }

    public static bool IsTurnInNpcActive(NPCInfo npc)
    {
        var agent = AgentSatisfactionSupply.Instance();
        return agent != null && agent->IsAgentActive() && agent->NpcInfo.Id == npc.Index + 1
            && agent->NpcInfo.Valid && agent->NpcInfo.Initialized;
    }

    public static void TurnInSupply(int slot)
    {
        var agent = AgentSatisfactionSupply.Instance();
        var res = new AtkValue();
        Span<AtkValue> values = stackalloc AtkValue[2];
        values[0].SetInt(1);
        values[1].SetInt(slot);
        agent->ReceiveEvent(&res, values.GetPointer(0), 2, 0);
    }

    public static bool IsTurnInRequestInProgress(uint itemId)
    {
        var ui = UIState.Instance();
        var agent = AgentNpcTrade.Instance();
        return agent != null && ui != null && agent->IsAgentActive()
            && ui->NpcTrade.Requests.Count == 1 && ui->NpcTrade.Requests.Items[0].ItemId == itemId;
    }

    public static bool TurnInRequestCommit(uint itemId, short minCollectibility)
    {
        var agent = AgentNpcTrade.Instance();
        if (agent == null || !agent->IsAgentActive())
        {
            Service.Log.Error("Agent not active...");
            return false;
        }

        if (agent->SelectedTurnInSlot >= 0)
        {
            Service.Log.Error($"Turn-in already in progress for slot {agent->SelectedTurnInSlot}");
            return false;
        }

        var res = new AtkValue();
        Span<AtkValue> param = stackalloc AtkValue[4];
        param[0].SetInt(2); // start turnin
        param[1].SetInt(0); // the NPCTrade request has one slot, regardless of the satisfaction category
        param[2].SetInt(0); // ???
        param[3].SetInt(0); // ???
        agent->ReceiveEvent(&res, param.GetPointer(0), 4, 0);

        if (agent->SelectedTurnInSlot != 0 || agent->SelectedTurnInSlotItemOptions <= 0)
        {
            Service.Log.Error($"Failed to start turn-in: cur slot={agent->SelectedTurnInSlot}, expected=0, count={agent->SelectedTurnInSlotItemOptions}");
            return false;
        }

        var option = -1;
        for (var i = 0; i < Math.Min(agent->SelectedTurnInSlotItemOptions, agent->SelectedTurnInSlotItemOptionValues.Length); ++i)
        {
            var item = agent->SelectedTurnInSlotItemOptionValues[i].Value;
            if (item == null || item->ItemId != itemId) continue;
            if (minCollectibility > 0 && (!item->Flags.HasFlag(InventoryItem.ItemFlags.Collectable)
                || item->SpiritbondOrCollectability < minCollectibility)) continue;
            option = i;
            break;
        }
        if (option < 0)
        {
            Service.Log.Error($"No qualifying collectible option for {itemId}, minimum {minCollectibility}");
            return false;
        }

        param[0].SetInt(0); // confirm
        param[1].SetInt(option);
        agent->ReceiveEvent(&res, param.GetPointer(0), 4, 1);

        if (agent->SelectedTurnInSlot >= 0)
        {
            Service.Log.Error($"Turn-in not confirmed: cur slot={agent->SelectedTurnInSlot}");
            return false;
        }

        // commit
        var addonId = agent->AddonId;
        agent->ReceiveEvent(&res, param.GetPointer(0), 4, 0);
        var addon = RaptureAtkUnitManager.Instance()->GetAddonById((ushort)addonId);
        if (addon != null && addon->IsVisible)
            addon->Close(false);
        return true;
    }

    public static bool IsTerritoryLoaded() => GameMain.Instance()->TerritoryLoadState == 2;

    public static bool IsCastingTeleport()
    {
        var info = ((FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)Service.Objects.LocalPlayer!.Address)->GetCastInfo();
        return info is not null && info->IsCasting && (ActionType)info->ActionType == ActionType.Action && info->ActionId == 5;
    }

    public static bool Interactable() => Service.Objects.LocalPlayer?.IsTargetable ?? false;
}
