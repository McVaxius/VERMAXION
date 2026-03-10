using System;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Info;

namespace VERMAXION.Services;

public class FCBuffService : IDisposable
{
    private readonly ICommandManager commandManager;
    private readonly IPluginLog log;

    private FCBuffState state = FCBuffState.Idle;
    private DateTime stateEnteredAt = DateTime.MinValue;
    private int purchaseAttempts = 0;
    private int maxPurchaseAttempts = 15;
    private int buyCount = 0;
    private int buyMax = 15;

    // Status ID 414 = Seal Sweetener (I or II) from FUTA_GC.lua
    private const uint SealSweetenerStatusId = 414;

    public enum FCBuffState
    {
        Idle,
        CheckingStatus,
        OpeningFCWindow,
        WaitingForFCWindow,
        ClickingActionsTab,
        WaitingForActionsTab,
        SearchingForBuff,
        CastingBuff,
        WaitingForContextMenu,
        ConfirmingCast,
        CheckingAfterCast,
        // Purchase flow (if cast fails)
        NavigatingToGC,
        WaitingForGCArrival,
        TargetingQuartermaster,
        InteractingQuartermaster,
        WaitingForSelectString1,
        SelectingOption1,
        WaitingForSelectString2,
        SelectingOption2,
        WaitingForExchange,
        PurchasingItem,
        WaitingForPurchaseConfirm,
        ConfirmingPurchase,
        PurchaseLoop,
        ClosingWindows,
        Complete,
        Failed,
    }

    public FCBuffState State => state;
    public bool IsActive => state != FCBuffState.Idle && state != FCBuffState.Complete && state != FCBuffState.Failed;
    public bool IsComplete => state == FCBuffState.Complete;
    public bool IsFailed => state == FCBuffState.Failed;
    public string StatusText => state.ToString();

    public FCBuffService(ICommandManager commandManager, IPluginLog log, IGameGui gameGui)
    {
        this.commandManager = commandManager;
        this.log = log;
    }

    public void Start(int maxAttempts)
    {
        maxPurchaseAttempts = maxAttempts;
        purchaseAttempts = 0;
        buyCount = 0;
        buyMax = 15;
        SetState(FCBuffState.CheckingStatus);
        log.Information($"[FCBuff] Starting FC buff check (max {maxAttempts} purchase attempts)");
    }

    public void RunTask()
    {
        log.Information("[VERMAXION] Manual FC Buff Refill triggered");
        Start(15);
    }

    public void Reset() => SetState(FCBuffState.Idle);
    public void Dispose() { }

    public unsafe void Update()
    {
        if (state == FCBuffState.Idle || state == FCBuffState.Complete || state == FCBuffState.Failed)
            return;

        var elapsed = (DateTime.UtcNow - stateEnteredAt).TotalSeconds;

        switch (state)
        {
            // ====== CHECK IF BUFF IS ALREADY ACTIVE ======
            case FCBuffState.CheckingStatus:
                var fcProxy = InfoProxyFreeCompany.Instance();
                if (fcProxy == null || fcProxy->Id == 0)
                {
                    log.Information("[FCBuff] Not in an FC - skipping");
                    SetState(FCBuffState.Complete);
                    return;
                }

                var remaining = GameHelpers.GetStatusTimeRemaining(SealSweetenerStatusId);
                if (remaining > 0)
                {
                    log.Information($"[FCBuff] Seal Sweetener already active ({remaining:F0}s remaining) - done");
                    SetState(FCBuffState.Complete);
                    return;
                }

                log.Information("[FCBuff] No Seal Sweetener active, attempting to cast from FC actions");
                SetState(FCBuffState.OpeningFCWindow);
                break;

            // ====== TRY TO CAST BUFF FROM FC WINDOW ======
            // FUTA_GC.lua: /freecompanycmd -> wait -> /callback FreeCompany false 0 4u -> search nodes -> cast
            case FCBuffState.OpeningFCWindow:
                if (elapsed < 1) return;
                log.Information("[FCBuff] Opening FC window: /freecompanycmd");
                commandManager.ProcessCommand("/freecompanycmd");
                SetState(FCBuffState.WaitingForFCWindow);
                break;

            case FCBuffState.WaitingForFCWindow:
                if (GameHelpers.IsAddonVisible("FreeCompany"))
                {
                    SetState(FCBuffState.ClickingActionsTab);
                }
                else if (elapsed > 5)
                {
                    log.Warning("[FCBuff] FreeCompany addon didn't open, trying purchase route");
                    SetState(FCBuffState.NavigatingToGC);
                }
                break;

            case FCBuffState.ClickingActionsTab:
                if (elapsed < 0.5) return;
                // FUTA_GC.lua: /callback FreeCompany false 0 4u  (click Actions tab)
                log.Information("[FCBuff] Clicking FC Actions tab");
                GameHelpers.FireAddonCallback("FreeCompany", false, 0, 4u);
                SetState(FCBuffState.WaitingForActionsTab);
                break;

            case FCBuffState.WaitingForActionsTab:
                if (elapsed < 1.5) return;
                if (GameHelpers.IsAddonVisible("FreeCompanyAction"))
                {
                    SetState(FCBuffState.SearchingForBuff);
                }
                else if (elapsed > 5)
                {
                    log.Warning("[FCBuff] FreeCompanyAction addon didn't open, closing and trying purchase");
                    GameHelpers.CloseCurrentAddon();
                    SetState(FCBuffState.NavigatingToGC);
                }
                break;

            case FCBuffState.SearchingForBuff:
                // FUTA_GC.lua: scan nodes 51001-51016 in FreeCompanyAction for "Seal Sweetener II"
                // then: /callback FreeCompanyAction false 1 {index}u
                // We try index 1 first (common position for Seal Sweetener II)
                // The exact index varies per FC setup, so we try a few
                log.Information("[FCBuff] Attempting to cast Seal Sweetener II from FC actions");
                // Try casting - the callback with index sends the cast request
                // FUTA_GC.lua uses dynamic search, but for our first attempt we try common indices
                GameHelpers.FireAddonCallback("FreeCompanyAction", false, 1, 1u);
                SetState(FCBuffState.WaitingForContextMenu);
                break;

            case FCBuffState.WaitingForContextMenu:
                if (GameHelpers.IsAddonVisible("ContextMenu"))
                {
                    // FUTA_GC.lua: /callback ContextMenu true 0 0 1u 0 0
                    log.Information("[FCBuff] ContextMenu visible, selecting cast option");
                    GameHelpers.FireAddonCallback("ContextMenu", true, 0, 0, 1u, 0, 0);
                    SetState(FCBuffState.ConfirmingCast);
                }
                else if (elapsed > 3)
                {
                    log.Warning("[FCBuff] No ContextMenu appeared, buff may not be available to cast");
                    purchaseAttempts++;
                    // Close FreeCompanyAction window
                    GameHelpers.CloseCurrentAddon();
                    SetState(FCBuffState.NavigatingToGC);
                }
                break;

            case FCBuffState.ConfirmingCast:
                if (elapsed < 0.5) return;
                // FUTA_GC.lua: /callback SelectYesno true 0
                if (GameHelpers.IsAddonVisible("SelectYesno"))
                {
                    GameHelpers.ClickYesIfVisible();
                    SetState(FCBuffState.CheckingAfterCast);
                }
                else if (elapsed > 3)
                {
                    SetState(FCBuffState.CheckingAfterCast);
                }
                break;

            case FCBuffState.CheckingAfterCast:
                if (elapsed < 2) return;
                var afterCast = GameHelpers.GetStatusTimeRemaining(SealSweetenerStatusId);
                if (afterCast > 0)
                {
                    log.Information($"[FCBuff] Seal Sweetener is now active ({afterCast:F0}s)! Closing windows.");
                    // Close any remaining FC windows
                    if (GameHelpers.IsAddonVisible("FreeCompanyAction")) GameHelpers.CloseCurrentAddon();
                    if (GameHelpers.IsAddonVisible("FreeCompany")) GameHelpers.CloseCurrentAddon();
                    SetState(FCBuffState.Complete);
                }
                else
                {
                    log.Information("[FCBuff] Cast didn't work, trying purchase route");
                    if (GameHelpers.IsAddonVisible("FreeCompanyAction")) GameHelpers.CloseCurrentAddon();
                    if (GameHelpers.IsAddonVisible("FreeCompany")) GameHelpers.CloseCurrentAddon();
                    SetState(FCBuffState.NavigatingToGC);
                }
                break;

            // ====== PURCHASE BUFF FROM OIC QUARTERMASTER ======
            // FUTA_GC.lua: /li gc -> target OIC Quartermaster -> /interact -> SelectString x2 -> FreeCompanyExchange
            case FCBuffState.NavigatingToGC:
                if (elapsed < 1) return;
                log.Information("[FCBuff] Navigating to GC via /li gc");
                commandManager.ProcessCommand("/li gc");
                SetState(FCBuffState.WaitingForGCArrival);
                break;

            case FCBuffState.WaitingForGCArrival:
                if (elapsed < 8) return; // Wait for Lifestream teleport
                if (GameHelpers.IsPlayerAvailable() || elapsed > 20)
                {
                    log.Information("[FCBuff] Arrived at GC, targeting OIC Quartermaster");
                    SetState(FCBuffState.TargetingQuartermaster);
                }
                break;

            case FCBuffState.TargetingQuartermaster:
                if (elapsed < 2) return;
                // FUTA_GC.lua: darget("OIC Quartermaster") then /interact
                var qm = GameHelpers.FindObjectByName("OIC Quartermaster");
                if (qm != null)
                {
                    log.Information("[FCBuff] Found OIC Quartermaster, interacting");
                    GameHelpers.InteractWithObject(qm);
                    SetState(FCBuffState.WaitingForSelectString1);
                }
                else
                {
                    log.Warning("[FCBuff] OIC Quartermaster not found - may need to walk closer");
                    // Try /target command as fallback
                    commandManager.ProcessCommand("/target \"OIC Quartermaster\"");
                    SetState(FCBuffState.InteractingQuartermaster);
                }
                break;

            case FCBuffState.InteractingQuartermaster:
                if (elapsed < 2) return;
                // Try interact via NUMPAD0 (like SND /send NUMPAD0)
                GameHelpers.SendConfirm();
                SetState(FCBuffState.WaitingForSelectString1);
                break;

            case FCBuffState.WaitingForSelectString1:
                if (GameHelpers.IsAddonVisible("SelectString"))
                {
                    SetState(FCBuffState.SelectingOption1);
                }
                else if (elapsed > 5)
                {
                    log.Warning("[FCBuff] SelectString didn't appear after QM interact");
                    SetState(FCBuffState.Failed);
                }
                break;

            case FCBuffState.SelectingOption1:
                if (elapsed < 0.5) return;
                // FUTA_GC.lua: /callback SelectString true 0
                log.Information("[FCBuff] Selecting first option in QM menu");
                GameHelpers.FireAddonCallback("SelectString", true, 0);
                SetState(FCBuffState.WaitingForSelectString2);
                break;

            case FCBuffState.WaitingForSelectString2:
                if (elapsed < 1) return;
                if (GameHelpers.IsAddonVisible("SelectString"))
                {
                    SetState(FCBuffState.SelectingOption2);
                }
                else if (GameHelpers.IsAddonVisible("FreeCompanyExchange"))
                {
                    // Went directly to exchange
                    SetState(FCBuffState.WaitingForExchange);
                }
                else if (elapsed > 5)
                {
                    log.Warning("[FCBuff] Second SelectString didn't appear");
                    SetState(FCBuffState.Failed);
                }
                break;

            case FCBuffState.SelectingOption2:
                if (elapsed < 0.5) return;
                // FUTA_GC.lua: /callback SelectString true 0
                log.Information("[FCBuff] Selecting first option again");
                GameHelpers.FireAddonCallback("SelectString", true, 0);
                SetState(FCBuffState.WaitingForExchange);
                break;

            case FCBuffState.WaitingForExchange:
                if (GameHelpers.IsAddonVisible("FreeCompanyExchange"))
                {
                    buyCount = 0;
                    SetState(FCBuffState.PurchasingItem);
                }
                else if (elapsed > 5)
                {
                    log.Warning("[FCBuff] FreeCompanyExchange didn't appear");
                    SetState(FCBuffState.Failed);
                }
                break;

            case FCBuffState.PurchasingItem:
                if (elapsed < 0.5) return;
                if (buyCount >= buyMax)
                {
                    log.Information($"[FCBuff] Purchased {buyMax} Seal Sweetener items");
                    SetState(FCBuffState.ClosingWindows);
                    return;
                }
                // FUTA_GC.lua: /callback FreeCompanyExchange false 2 22u  (Seal Sweetener II = index 22)
                // If first purchase attempt failed, fallback to Seal Sweetener I = index 5
                var itemIndex = purchaseAttempts < 2 ? 22u : 5u;
                log.Information($"[FCBuff] Purchasing item index {itemIndex} (buy {buyCount + 1}/{buyMax})");
                GameHelpers.FireAddonCallback("FreeCompanyExchange", false, 2, itemIndex);
                SetState(FCBuffState.WaitingForPurchaseConfirm);
                break;

            case FCBuffState.WaitingForPurchaseConfirm:
                if (elapsed < 1) return;
                if (GameHelpers.IsAddonVisible("SelectYesno"))
                {
                    SetState(FCBuffState.ConfirmingPurchase);
                }
                else if (elapsed > 3)
                {
                    // No confirmation needed or item couldn't be purchased
                    buyCount++;
                    SetState(FCBuffState.PurchaseLoop);
                }
                break;

            case FCBuffState.ConfirmingPurchase:
                if (elapsed < 0.3) return;
                // FUTA_GC.lua: /callback SelectYesno true 0
                GameHelpers.ClickYesIfVisible();
                buyCount++;
                SetState(FCBuffState.PurchaseLoop);
                break;

            case FCBuffState.PurchaseLoop:
                if (elapsed < 1) return;
                if (buyCount < buyMax && GameHelpers.IsAddonVisible("FreeCompanyExchange"))
                {
                    SetState(FCBuffState.PurchasingItem);
                }
                else
                {
                    SetState(FCBuffState.ClosingWindows);
                }
                break;

            case FCBuffState.ClosingWindows:
                if (elapsed < 0.5) return;
                log.Information("[FCBuff] Closing windows");
                GameHelpers.CloseCurrentAddon();
                if (elapsed > 2)
                {
                    SetState(FCBuffState.Complete);
                }
                break;
        }
    }

    private void SetState(FCBuffState newState)
    {
        log.Information($"[FCBuff] {state} -> {newState}");
        state = newState;
        stateEnteredAt = DateTime.UtcNow;
    }
}
