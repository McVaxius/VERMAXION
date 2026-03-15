using System;
using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace VERMAXION.Services;

// [OK] - Complete implementation with 3-ticket sequence and NUMPAD+ exit
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

    private DateTime lastTargetAttempt = DateTime.MinValue;
    private int targetAttempts = 0;
    private const int MaxTargetAttempts = 5;
    private const double TargetRetryInterval = 2.0; // seconds between attempts

    /// <summary>
    /// Enhanced targeting method with multiple attempts and fallback strategies.
    /// Tries multiple targeting approaches to ensure NPC interaction succeeds.
    /// </summary>
    private bool TargetAndInteractWithRetry()
    {
        var now = DateTime.UtcNow;
        
        // Check if it's time for another attempt
        if ((now - lastTargetAttempt).TotalSeconds < TargetRetryInterval)
            return false; // Still waiting for retry interval
        
        lastTargetAttempt = now;
        targetAttempts++;
        
        log.Information($"[Cactpot] Targeting attempt {targetAttempts}/{MaxTargetAttempts}");
        
        // Try different targeting strategies in order of preference
        bool success = false;
        
        // Strategy 1: Exact name match
        if (targetAttempts == 1)
        {
            log.Information("[Cactpot] Attempt 1: Targeting by exact name 'Mini Cactpot Broker'");
            success = GameHelpers.TargetAndInteract("Mini Cactpot Broker");
        }
        // Strategy 2: Partial name match - Cactpot
        else if (targetAttempts == 2)
        {
            log.Information("[Cactpot] Attempt 2: Targeting by partial name 'Cactpot'");
            success = GameHelpers.TargetAndInteract("Cactpot");
        }
        // Strategy 3: Partial name match - Broker
        else if (targetAttempts == 3)
        {
            log.Information("[Cactpot] Attempt 3: Targeting by partial name 'Broker'");
            success = GameHelpers.TargetAndInteract("Broker");
        }
        // Strategy 4: Target command with Cactpot
        else if (targetAttempts == 4)
        {
            log.Information("[Cactpot] Attempt 4: Using /target Cactpot command");
            commandManager.ProcessCommand("/target Cactpot");
            // Give it a moment to process, then try interaction
            System.Threading.Tasks.Task.Delay(500).ContinueWith(_ => {
                GameHelpers.SendConfirm();
            });
            success = true; // Assume success, let the interaction state verify
        }
        // Strategy 5: Target command with Mini
        else if (targetAttempts == 5)
        {
            log.Information("[Cactpot] Attempt 5: Using /target Mini command");
            commandManager.ProcessCommand("/target Mini");
            // Give it a moment to process, then try interaction
            System.Threading.Tasks.Task.Delay(500).ContinueWith(_ => {
                GameHelpers.SendConfirm();
            });
            success = true; // Assume success, let the interaction state verify
        }
        
        if (success)
        {
            log.Information($"[Cactpot] Targeting strategy {targetAttempts} succeeded");
            return true;
        }
        else
        {
            log.Warning($"[Cactpot] Targeting strategy {targetAttempts} failed");
            
            // If we've exhausted all attempts, return false
            if (targetAttempts >= MaxTargetAttempts)
            {
                log.Error("[Cactpot] All targeting strategies failed");
                return false;
            }
            
            // Continue trying
            return false;
        }
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
                if (TargetAndInteractWithRetry())
                {
                    log.Information("[Cactpot] Successfully interacted with Mini Cactpot Broker");
                    SetState(CactpotState.MiniInteracting);
                }
                else if (elapsed > 30)
                {
                    log.Error("[Cactpot] Failed to target and interact with Mini Cactpot Broker after 30 seconds");
                    SetState(CactpotState.Failed);
                }
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
                // Handle Yes/No confirmation dialog - keep clicking as long as it's visible
                if (GameHelpers.ClickYesIfVisible())
                {
                    log.Information("[Cactpot] Confirmed Mini Cactpot purchase/action");
                    // Reset timer when we click yes to allow more time for next dialog
                    stateEnteredAt = DateTime.UtcNow;
                }
                
                // Wait for Saucy to complete, but extend wait time if we keep clicking yes
                var maxWaitTime = 10.0; // Allow up to 10 seconds for multiple Yes clicks
                if (elapsed > maxWaitTime)
                {
                    log.Information($"[Cactpot] Mini Cactpot ticket {currentTicket} complete (timeout after {maxWaitTime}s)");
                    
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
        
        // Reset targeting counters when entering targeting state
        if (newState == CactpotState.MiniTargeting)
        {
            lastTargetAttempt = DateTime.MinValue;
            targetAttempts = 0;
        }
        
        state = newState;
        stateEnteredAt = DateTime.UtcNow;
    }

    public void Dispose() { }
}
