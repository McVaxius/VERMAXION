using System;
using System.Linq;
using Dalamud.Game.Command;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using VERMAXION.Models;

namespace VERMAXION.Services;

public class FCBuffService : IDisposable
{
    private readonly ICommandManager commandManager;
    private readonly IPluginLog log;
    private readonly IClientState clientState;
    private readonly ICondition condition;
    private readonly IObjectTable objects;
    private readonly ITargetManager targetManager;

    private FCBuffState state = FCBuffState.Idle;
    private DateTime stateEnteredAt = DateTime.MinValue;
    private int purchaseAttempts = 0;
    private int buyCount = 0;
    private int buyMax = 15;
    private int maxPurchaseAttempts = 2; // Try SS2 first, then SS1
    private bool isSealSweetenerTwo = true; // Try Seal Sweetener II first

    // FC points threshold from FUTA_GC.lua
    private const int MinFCPoints = 500000;
    // Gil requirement from FUTA_GC.lua
    private const int MinGil = 16000;

    public enum FCBuffState
    {
        Idle,
        CheckingFCPoints,
        OpeningFCWindow,
        WaitingForFCWindow,
        CheckingFCPointsInWindow,
        CheckingIfRefillNeeded,
        NavigatingToGC,
        WaitingForGCArrival,
        TargetingQuartermaster,
        InteractingQuartermaster,
        WaitingForSelectString1,
        SelectingPurchaseOption,
        WaitingForSelectString2,
        SelectingBuffType,
        WaitingForExchange,
        PurchasingBuff,
        WaitingForPurchaseConfirm,
        ConfirmingPurchase,
        PurchaseLoop,
        ClosingWindows,
        Complete,
        Failed
    }

    public FCBuffState State => state;
    public bool IsActive => state != FCBuffState.Idle && state != FCBuffState.Complete && state != FCBuffState.Failed;
    public bool IsComplete => state == FCBuffState.Complete;
    public bool IsFailed => state == FCBuffState.Failed;
    public string StatusText => state.ToString();

    public FCBuffService(ICommandManager commandManager, IPluginLog log, IClientState clientState, ICondition condition, IObjectTable objects, ITargetManager targetManager)
    {
        this.commandManager = commandManager;
        this.log = log;
        this.clientState = clientState;
        this.condition = condition;
        this.objects = objects;
        this.targetManager = targetManager;
    }

    public void Start(int maxAttempts = 2)
    {
        if (IsActive)
        {
            log.Warning("[FCBuff] Service already active");
            return;
        }

        maxPurchaseAttempts = maxAttempts;
        purchaseAttempts = 0;
        buyCount = 0;
        isSealSweetenerTwo = true; // Start with Seal Sweetener II
        SetState(FCBuffState.CheckingFCPoints);
        log.Information($"[FCBuff] Starting FC buff refill (max attempts: {maxAttempts})");
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
        if (!IsActive) return;

        var elapsed = (DateTime.UtcNow - stateEnteredAt).TotalSeconds;

        switch (state)
        {
            case FCBuffState.CheckingFCPoints:
                if (elapsed < 1) return;
                log.Information("[FCBuff] Checking if we have enough FC points for refill");
                SetState(FCBuffState.OpeningFCWindow);
                break;

            case FCBuffState.OpeningFCWindow:
                if (elapsed < 1) return;
                log.Information("[FCBuff] Opening FC window: /freecompanycmd");
                commandManager.ProcessCommand("/freecompanycmd");
                SetState(FCBuffState.WaitingForFCWindow);
                break;

            case FCBuffState.WaitingForFCWindow:
                if (elapsed < 3)
                {
                    // Check if FC window is ready
                    if (GameHelpers.IsAddonVisible("FreeCompany"))
                    {
                        log.Information("[FCBuff] FC window is ready");
                        SetState(FCBuffState.CheckingFCPointsInWindow);
                    }
                    return;
                }
                // Try opening again if first attempt failed
                if (elapsed < 6)
                {
                    log.Information("[FCBuff] First attempt failed, trying again");
                    commandManager.ProcessCommand("/freecompanycmd");
                    SetState(FCBuffState.WaitingForFCWindow);
                    return;
                }
                log.Error("[FCBuff] Failed to open FC window");
                SetState(FCBuffState.Failed);
                break;

            case FCBuffState.CheckingFCPointsInWindow:
                if (elapsed < 1) return;
                // Get FC points from the window
                var fcProxy = InfoProxyFreeCompany.Instance();
                if (fcProxy != null && fcProxy->Id != 0)
                {
                    // FC points are at node 1,4,16,17 in the FC window
                    var fcPointsNode = GameHelpers.GetFCPointsNode();
                    var fcPoints = fcPointsNode ?? 0;
                    log.Information($"[FCBuff] Current FC points: {fcPoints:N0}");
                    
                    // Check if we have enough FC points
                    if (fcPoints < MinFCPoints)
                    {
                        log.Information($"[FCBuff] Not enough FC points ({fcPoints:N0} < {MinFCPoints:N0}), skipping refill");
                        SetState(FCBuffState.Complete);
                        return;
                    }
                    
                    SetState(FCBuffState.CheckingIfRefillNeeded);
                }
                else
                {
                    if (elapsed > 5)
                    {
                        log.Error("[FCBuff] Timeout waiting for FC window");
                        SetState(FCBuffState.Failed);
                    }
                }
                break;

            case FCBuffState.CheckingIfRefillNeeded:
                if (elapsed < 1) return;
                // Check if we have enough gil
                var gil = GameHelpers.GetInventoryItemCount(1);
                log.Information($"[FCBuff] Current gil: {gil:N0}");
                if (gil < MinGil)
                {
                    log.Information($"[FCBuff] Not enough gil ({gil:N0} < {MinGil:N0}), skipping refill");
                    SetState(FCBuffState.Complete);
                    return;
                }
                log.Information("[FCBuff] Proceeding with FC buff refill");
                SetState(FCBuffState.NavigatingToGC);
                break;

            case FCBuffState.NavigatingToGC:
                if (elapsed < 1) return;
                // Navigate to current GC
                log.Information("[FCBuff] Navigating to GC: /li gc");
                commandManager.ProcessCommand("/li gc");
                SetState(FCBuffState.WaitingForGCArrival);
                break;

            case FCBuffState.WaitingForGCArrival:
                if (elapsed < 10)
                {
                    // Wait for territory change
                    if (condition[ConditionFlag.BetweenAreas] || condition[ConditionFlag.BetweenAreas51])
                        return;
                    // Check if we're in a GC territory (IDs 128, 129, 130)
                    var territory = clientState.TerritoryType;
                    if (territory >= 128 && territory <= 130)
                    {
                        log.Information($"[FCBuff] Arrived at GC territory: {territory}");
                        SetState(FCBuffState.TargetingQuartermaster);
                    }
                    return;
                }
                log.Error("[FCBuff] Failed to arrive at GC");
                SetState(FCBuffState.Failed);
                break;

            case FCBuffState.TargetingQuartermaster:
                if (elapsed < 1) return;
                log.Information("[FCBuff] Targeting OIC Quartermaster");
                
                // Try to target "Quartermaster" first (more specific)
                var quartermaster = GameHelpers.FindObjectByName("Quartermaster");
                if (quartermaster != null)
                {
                    log.Information("[FCBuff] Found Quartermaster, targeting...");
                    targetManager.Target = quartermaster;
                    SetState(FCBuffState.InteractingQuartermaster);
                }
                else
                {
                    // Fallback: try "OIC Quartermaster"
                    var oicQuartermaster = GameHelpers.FindObjectByName("OIC Quartermaster");
                    if (oicQuartermaster != null)
                    {
                        log.Information("[FCBuff] Found OIC Quartermaster, targeting...");
                        targetManager.Target = oicQuartermaster;
                        SetState(FCBuffState.InteractingQuartermaster);
                    }
                    else
                    {
                        // Last resort: use /target command
                        log.Information("[FCBuff] NPC not found, using /target Quartermaster");
                        commandManager.ProcessCommand("/target Quartermaster");
                        SetState(FCBuffState.InteractingQuartermaster);
                    }
                }
                break;

            case FCBuffState.InteractingQuartermaster:
                if (elapsed < 1) return;
                log.Information("[FCBuff] Interacting with OIC Quartermaster");
                if (targetManager.Target != null && GameHelpers.InteractWithObject(targetManager.Target))
                {
                    SetState(FCBuffState.WaitingForSelectString1);
                }
                else
                {
                    log.Error("[FCBuff] Failed to interact with OIC Quartermaster");
                    SetState(FCBuffState.Failed);
                }
                break;

            case FCBuffState.WaitingForSelectString1:
                if (elapsed < 5)
                {
                    if (GameHelpers.IsAddonVisible("SelectString"))
                    {
                        log.Information("[FCBuff] SelectString appeared, selecting purchase option");
                        GameHelpers.FireAddonCallback("SelectString", true, 0);
                        SetState(FCBuffState.WaitingForSelectString2);
                    }
                    return;
                }
                log.Error("[FCBuff] SelectString did not appear");
                SetState(FCBuffState.Failed);
                break;

            case FCBuffState.WaitingForSelectString2:
                if (elapsed < 5)
                {
                    if (GameHelpers.IsAddonVisible("SelectString"))
                    {
                        log.Information("[FCBuff] Second SelectString appeared, selecting buff type");
                        GameHelpers.FireAddonCallback("SelectString", true, 0);
                        SetState(FCBuffState.WaitingForExchange);
                    }
                    return;
                }
                log.Error("[FCBuff] Second SelectString did not appear");
                SetState(FCBuffState.Failed);
                break;

            case FCBuffState.WaitingForExchange:
                if (elapsed < 5)
                {
                    if (GameHelpers.IsAddonVisible("FreeCompanyExchange"))
                    {
                        log.Information("[FCBuff] FreeCompanyExchange appeared, starting purchase loop");
                        buyCount = 0;
                        buyMax = isSealSweetenerTwo ? 15 : 1; // Buy 15 of SS2, only 1 of SS1
                        SetState(FCBuffState.PurchasingBuff);
                    }
                    return;
                }
                log.Error("[FCBuff] FreeCompanyExchange did not appear");
                SetState(FCBuffState.Failed);
                break;

            case FCBuffState.PurchasingBuff:
                if (elapsed < 1) return;
                if (buyCount >= buyMax)
                {
                    log.Information($"[FCBuff] Purchase complete: {buyCount} buffs bought");
                    SetState(FCBuffState.ClosingWindows);
                    return;
                }
                
                var buffIndex = isSealSweetenerTwo ? 22 : 5; // 22u for SS2, 5u for SS1
                log.Information($"[FCBuff] Purchasing buff {buyCount + 1}/{buyMax} (index: {buffIndex})");
                GameHelpers.FireAddonCallback("FreeCompanyExchange", false, 2, (uint)buffIndex);
                SetState(FCBuffState.WaitingForPurchaseConfirm);
                break;

            case FCBuffState.WaitingForPurchaseConfirm:
                if (elapsed < 5)
                {
                    if (GameHelpers.IsAddonVisible("SelectYesno"))
                    {
                        log.Information("[FCBuff] Confirming purchase");
                        GameHelpers.FireAddonCallback("SelectYesno", true, 0);
                        buyCount++;
                        SetState(FCBuffState.PurchasingBuff);
                    }
                    return;
                }
                log.Error("[FCBuff] Purchase confirmation did not appear");
                SetState(FCBuffState.Failed);
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
