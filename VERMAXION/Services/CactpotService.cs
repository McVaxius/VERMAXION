using System;
using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace VERMAXION.Services;

public class CactpotService : IDisposable
{
    private readonly ICommandManager commandManager;
    private readonly IPluginLog log;
    private readonly IClientState clientState;

    private const ushort GoldSaucerTerritoryId = 144;

    private CactpotState state = CactpotState.Idle;
    private DateTime stateEnteredAt = DateTime.MinValue;
    private int currentTicket = 1;
    private int totalTickets = 3;

    public enum CactpotState
    {
        Idle,
        // Mini Cactpot states
        MiniTeleporting,
        MiniWaitingForZone,
        MiniNavigating,
        MiniWaitingForArrival,
        MiniTargeting,
        MiniInteracting,
        MiniSelectingTicket,
        MiniWaitingForSaucy,
        MiniComplete,
        // Jumbo Cactpot Buy states
        JumboLifestreaming,
        JumboWaitingForZone,
        JumboNavigatingToBroker,
        JumboWaitingForArrival,
        JumboTargetingBroker,
        JumboInteractingBroker,
        JumboSelectingPurchase,
        JumboWaitingForNumpad,
        JumboComplete,
        // Jumbo Cactpot Check states (Saturday)
        JumboCheckLifestreaming,
        JumboCheckWaitingForZone,
        JumboCheckNavigatingToCashier,
        JumboCheckWaitingForArrival,
        JumboCheckTargetingCashier,
        JumboCheckInteractingCashier,
        JumboCheckComplete,
        // Final
        Complete,
        Failed,
    }

    public CactpotState State => state;
    public bool IsActive => state != CactpotState.Idle && state != CactpotState.Complete && state != CactpotState.Failed;
    public bool IsComplete => state == CactpotState.Complete;
    public bool IsFailed => state == CactpotState.Failed;
    public string StatusText => state.ToString();

    public CactpotService(ICommandManager commandManager, IPluginLog log, IClientState clientState)
    {
        this.commandManager = commandManager;
        this.log = log;
        this.clientState = clientState;
    }

    public void StartMiniCactpot()
    {
        log.Information("[Cactpot] Starting Mini Cactpot sequence");
        
        // Initialize multi-ticket sequence
        currentTicket = 1;
        totalTickets = 3;
        
        if (clientState.TerritoryType == GoldSaucerTerritoryId)
        {
            log.Information("[Cactpot] Already in Gold Saucer, skipping teleport");
            SetState(CactpotState.MiniNavigating);
        }
        else
        {
            SetState(CactpotState.MiniTeleporting);
        }
    }

    public void StartJumboCactpot()
    {
        log.Information("[Cactpot] Starting Jumbo Cactpot Buy sequence");
        SetState(CactpotState.JumboLifestreaming);
    }

    public void StartJumboCactpotCheck()
    {
        log.Information("[Cactpot] Starting Jumbo Cactpot Check (Saturday) sequence");
        SetState(CactpotState.JumboCheckLifestreaming);
    }

    public void RunMiniCactpot()
    {
        log.Information("[VERMAXION] Manual Mini Cactpot triggered");
        StartMiniCactpot();
    }

    public void RunJumboCactpot()
    {
        log.Information("[VERMAXION] Manual Jumbo Cactpot triggered");
        StartJumboCactpot();
    }

    public void Reset()
    {
        SetState(CactpotState.Idle);
    }

    public void Update()
    {
        if (state == CactpotState.Idle || state == CactpotState.Complete || state == CactpotState.Failed)
            return;

        var elapsed = (DateTime.UtcNow - stateEnteredAt).TotalSeconds;

        switch (state)
        {
            // ==================== MINI CACTPOT ====================
            case CactpotState.MiniTeleporting:
                log.Information("[Cactpot] Teleporting to Gold Saucer: /tp gold");
                commandManager.ProcessCommand("/tp gold");
                SetState(CactpotState.MiniWaitingForZone);
                break;

            case CactpotState.MiniWaitingForZone:
                if (clientState.TerritoryType == GoldSaucerTerritoryId)
                {
                    log.Information("[Cactpot] Arrived in Gold Saucer");
                    SetState(CactpotState.MiniNavigating);
                }
                else if (elapsed > 30)
                {
                    log.Error("[Cactpot] Timeout waiting for Gold Saucer zone");
                    SetState(CactpotState.Failed);
                }
                break;

            case CactpotState.MiniNavigating:
                if (elapsed > 3.0 && GameHelpers.IsPlayerAvailable())
                {
                    log.Information("[Cactpot] Navigating to Cactpot Board");
                    commandManager.ProcessCommand("/vnav moveto -46.655319213867 1.5999846458435 20.395349502563");
                    SetState(CactpotState.MiniWaitingForArrival);
                }
                else if (elapsed > 30)
                {
                    log.Error("[Cactpot] Timeout waiting for player available");
                    SetState(CactpotState.Failed);
                }
                break;

            case CactpotState.MiniWaitingForArrival:
                if (elapsed > 15)
                {
                    log.Information("[Cactpot] Arrived at Cactpot Board area");
                    SetState(CactpotState.MiniTargeting);
                }
                break;

            case CactpotState.MiniTargeting:
                log.Information("[Cactpot] Targeting and interacting with Cactpot Board");
                if (GameHelpers.TargetAndInteract("Mini Cactpot Broker"))
                {
                    log.Information("[Cactpot] Interacted with Mini Cactpot Broker via TargetSystem");
                }
                else
                {
                    // Fallback: try /target + NUMPAD0
                    commandManager.ProcessCommand("/target Cactpot");
                }
                SetState(CactpotState.MiniInteracting);
                break;

            case CactpotState.MiniInteracting:
                if (elapsed > 1.5)
                {
                    if (GameHelpers.IsAddonVisible("SelectIconString"))
                    {
                        SetState(CactpotState.MiniSelectingTicket);
                    }
                    else
                    {
                        // Try NUMPAD0 to interact
                        GameHelpers.SendConfirm();
                        if (elapsed > 5)
                        {
                            log.Warning("[Cactpot] Failed to open Cactpot menu, retrying target");
                            SetState(CactpotState.MiniTargeting);
                        }
                    }
                }
                break;

            case CactpotState.MiniSelectingTicket:
                if (elapsed > 0.5)
                {
                    log.Information("[Cactpot] Selecting 'Purchase a Mini Cactpot ticket' (SelectIconString 0)");
                    // Callback: SelectIconString index 0 = "Purchase a Mini Cactpot ticket"
                    GameHelpers.FireAddonCallback("SelectIconString", true, 0);
                    SetState(CactpotState.MiniWaitingForSaucy);
                }
                break;

            case CactpotState.MiniWaitingForSaucy:
                // Just wait a moment then continue to next ticket
                if (elapsed > 2.0)
                {
                    log.Information($"[Cactpot] Mini Cactpot ticket {currentTicket} complete");
                    
                    // Check if we have more tickets to process
                    if (currentTicket < totalTickets)
                    {
                        currentTicket++;
                        log.Information($"[Cactpot] Starting ticket {currentTicket}/{totalTickets}");
                        SetState(CactpotState.MiniTargeting);
                    }
                    else
                    {
                        log.Information("[Cactpot] All Mini Cactpot tickets completed");
                        SetState(CactpotState.MiniComplete);
                    }
                }
                break;

            case CactpotState.MiniComplete:
                log.Information("[Cactpot] Mini Cactpot sequence finished, exiting with NUMPAD+");
                // Exit with NUMPAD+ like FC buff purchasing
                GameHelpers.SendNumpadPlus();
                SetState(CactpotState.Complete);
                break;

            // ==================== JUMBO CACTPOT BUY ====================
            case CactpotState.JumboLifestreaming:
                log.Information("[Cactpot] Lifestreaming to Cactpot area: /li Cactpot");
                commandManager.ProcessCommand("/li Cactpot");
                SetState(CactpotState.JumboWaitingForZone);
                break;

            case CactpotState.JumboWaitingForZone:
                if (elapsed > 10)
                {
                    log.Information("[Cactpot] Assumed arrival near Jumbo Cactpot area");
                    SetState(CactpotState.JumboNavigatingToBroker);
                }
                break;

            case CactpotState.JumboNavigatingToBroker:
                log.Information("[Cactpot] Navigating to Jumbo Cactpot Broker");
                commandManager.ProcessCommand("/vnav moveto 121.13345336914 13.001298904419 -11.011554718018");
                SetState(CactpotState.JumboWaitingForArrival);
                break;

            case CactpotState.JumboWaitingForArrival:
                if (elapsed > 15)
                {
                    log.Information("[Cactpot] Arrived at Broker");
                    SetState(CactpotState.JumboTargetingBroker);
                }
                break;

            case CactpotState.JumboTargetingBroker:
                log.Information("[Cactpot] Targeting and interacting with Broker");
                if (GameHelpers.TargetAndInteract("Jumbo Cactpot Broker"))
                {
                    log.Information("[Cactpot] Interacted with Jumbo Cactpot Broker via TargetSystem");
                }
                else
                {
                    commandManager.ProcessCommand("/target Broker");
                }
                SetState(CactpotState.JumboInteractingBroker);
                break;

            case CactpotState.JumboInteractingBroker:
                if (elapsed > 1.5)
                {
                    if (GameHelpers.IsAddonVisible("SelectString"))
                    {
                        SetState(CactpotState.JumboSelectingPurchase);
                    }
                    else
                    {
                        GameHelpers.SendConfirm();
                        if (elapsed > 5)
                        {
                            log.Warning("[Cactpot] Broker menu didn't open");
                            SetState(CactpotState.Failed);
                        }
                    }
                }
                break;

            case CactpotState.JumboSelectingPurchase:
                if (elapsed > 0.5)
                {
                    log.Information("[Cactpot] Selecting purchase option (SelectString 0)");
                    GameHelpers.FireAddonCallback("SelectString", true, 0);
                    SetState(CactpotState.JumboWaitingForNumpad);
                }
                break;

            case CactpotState.JumboWaitingForNumpad:
                // The numpad UI appears (LotteryDaily addon)
                // Use the randomize button (dice icon) then purchase
                if (GameHelpers.IsAddonVisible("LotteryDaily"))
                {
                    log.Information("[Cactpot] Numpad UI visible, clicking randomize then purchase");
                    // Click randomize button (dice) - callback index for random
                    GameHelpers.FireAddonCallback("LotteryDaily", true, 1);
                    SetState(CactpotState.JumboComplete);
                }
                else if (elapsed > 5)
                {
                    log.Warning("[Cactpot] LotteryDaily addon didn't appear");
                    SetState(CactpotState.JumboComplete);
                }
                break;

            case CactpotState.JumboComplete:
                log.Information("[Cactpot] Jumbo Cactpot Buy sequence finished");
                SetState(CactpotState.Complete);
                break;

            // ==================== JUMBO CACTPOT CHECK (Saturday) ====================
            case CactpotState.JumboCheckLifestreaming:
                log.Information("[Cactpot] Lifestreaming to Cactpot area for check: /li Cactpot");
                commandManager.ProcessCommand("/li Cactpot");
                SetState(CactpotState.JumboCheckWaitingForZone);
                break;

            case CactpotState.JumboCheckWaitingForZone:
                if (elapsed > 10)
                {
                    SetState(CactpotState.JumboCheckNavigatingToCashier);
                }
                break;

            case CactpotState.JumboCheckNavigatingToCashier:
                log.Information("[Cactpot] Navigating to Jumbo Cactpot Cashier");
                commandManager.ProcessCommand("/vnav moveto 124.05115509033 13.002527236938 -19.590528488159");
                SetState(CactpotState.JumboCheckWaitingForArrival);
                break;

            case CactpotState.JumboCheckWaitingForArrival:
                if (elapsed > 15)
                {
                    SetState(CactpotState.JumboCheckTargetingCashier);
                }
                break;

            case CactpotState.JumboCheckTargetingCashier:
                log.Information("[Cactpot] Targeting and interacting with Cashier");
                if (GameHelpers.TargetAndInteract("Jumbo Cactpot Cashier"))
                {
                    log.Information("[Cactpot] Interacted with Cashier via TargetSystem");
                }
                else
                {
                    commandManager.ProcessCommand("/target Cashier");
                }
                SetState(CactpotState.JumboCheckInteractingCashier);
                break;

            case CactpotState.JumboCheckInteractingCashier:
                if (elapsed > 1.5)
                {
                    if (GameHelpers.IsAddonVisible("SelectString") || GameHelpers.IsAddonVisible("LotteryWeekly"))
                    {
                        log.Information("[Cactpot] Cashier dialog opened, auto-confirming");
                        if (GameHelpers.IsAddonVisible("SelectString"))
                            GameHelpers.FireAddonCallback("SelectString", true, 0);
                        SetState(CactpotState.JumboCheckComplete);
                    }
                    else
                    {
                        GameHelpers.SendConfirm();
                        if (elapsed > 5)
                            SetState(CactpotState.JumboCheckComplete);
                    }
                }
                break;

            case CactpotState.JumboCheckComplete:
                log.Information("[Cactpot] Jumbo Cactpot Check sequence finished");
                SetState(CactpotState.Complete);
                break;
        }
    }

    private void SetState(CactpotState newState)
    {
        log.Information($"[Cactpot] {state} -> {newState}");
        state = newState;
        stateEnteredAt = DateTime.UtcNow;
    }

    public void Dispose() { }
}
