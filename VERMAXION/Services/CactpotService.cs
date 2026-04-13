using System;
using System.Globalization;
using System.Numerics;
using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using VERMAXION.Models;

namespace VERMAXION.Services;

// [OK] - Complete implementation with 3-ticket sequence and NUMPAD+ exit
public class CactpotService : IDisposable
{
    private readonly ICommandManager commandManager;
    private readonly IPluginLog log;
    private readonly IClientState clientState;
    private readonly ConfigManager configManager;

    private const ushort GoldSaucerTerritoryId = 144;
    private static readonly Vector3 JumboBrokerPosition = new(121.13345336914f, 13.001298904419f, -11.011554718018f);
    private static readonly Vector3 JumboCashierPosition = new(124.05115509033f, 13.002527236938f, -19.590528488159f);
    private const string JumboBrokerMoveCommand = "/vnav moveto 121.13345336914 13.001298904419 -11.011554718018";
    private const string JumboCashierMoveCommand = "/vnav moveto 124.05115509033 13.002527236938 -19.590528488159";
    private const double JumboAetheryteSettleDelay = 8.0;
    private const double JumboNavigationRetryInterval = 2.0;
    private const float JumboArrivalDistance = 3.0f;
    private const double JumboArrivalTimeout = 60.0;
    private const double JumboCloseApproachTimeout = 20.0;
    private const double JumboPostNavigationSettleDelay = 0.5;
    private const double JumboTargetRetryInterval = 0.75;

    private CactpotState state = CactpotState.Idle;
    private DateTime stateEnteredAt = DateTime.MinValue;
    private int currentTicket = 1;
    private int totalTickets = 3;
    private int currentJumboNumber;
    private DateTime lastJumboNavigationAttempt = DateTime.MinValue;
    private DateTime lastJumboTargetAttempt = DateTime.MinValue;

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
        MiniReturningHome,
        MiniWaitingForHome,
        // Jumbo Cactpot Buy states
        JumboLifestreaming,
        JumboWaitingForZone,
        JumboNavigatingToBroker,
        JumboWaitingForArrival,
        JumboClosingToBroker,
        JumboTargetingBroker,
        JumboInteractingBroker,
        JumboSelectingPurchase,
        JumboWaitingForInputWindow,
        JumboWaitingForConfirmation,
        JumboComplete,
        // Jumbo Cactpot Check states (Saturday)
        JumboCheckLifestreaming,
        JumboCheckWaitingForZone,
        JumboCheckNavigatingToCashier,
        JumboCheckWaitingForArrival,
        JumboCheckClosingToCashier,
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

    public CactpotService(ICommandManager commandManager, IPluginLog log, IClientState clientState, ConfigManager configManager)
    {
        this.commandManager = commandManager;
        this.log = log;
        this.clientState = clientState;
        this.configManager = configManager;
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
        currentTicket = 1;
        totalTickets = 3;
        currentJumboNumber = GetConfiguredJumboNumber();
        log.Information($"[Cactpot] Starting Jumbo Cactpot Buy sequence using {GetConfiguredJumboModeLabel()} number {currentJumboNumber:0000}");
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
    private DateTime lastJumpTime = DateTime.MinValue;
    //private const double JumpInterval = 0.5; // 500ms jump interval as requested
    private const double JumpInterval = 3; // 3s jump interval as requested
    private const float JumpStopDistance = 10f; // Stop jumping when within 10 yalms

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
            log.Information("[Cactpot] Attempt 1: Using improved TargetAndInteract");
            if (GameHelpers.TargetAndInteract("Mini Cactpot Broker"))
            {
                // AutoRetainer pattern: TargetAndInteract already handles interaction
                success = true;
            }
        }
        // Strategy 2: Retry with exact name
        else if (targetAttempts == 2)
        {
            log.Information("[Cactpot] Attempt 2: Using improved TargetAndInteract");
            if (GameHelpers.TargetAndInteract("Mini Cactpot Broker"))
            {
                // AutoRetainer pattern: TargetAndInteract already handles interaction
                success = true;
            }
        }
        // Strategy 3: Retry with exact name
        else if (targetAttempts == 3)
        {
            log.Information("[Cactpot] Attempt 3: Using improved TargetAndInteract");
            if (GameHelpers.TargetAndInteract("Mini Cactpot Broker"))
            {
                // AutoRetainer pattern: TargetAndInteract already handles interaction
                success = true;
            }
        }
        // Strategy 4: Retry with exact name
        else if (targetAttempts == 4)
        {
            log.Information("[Cactpot] Attempt 4: Using improved TargetAndInteract");
            if (GameHelpers.TargetAndInteract("Mini Cactpot Broker"))
            {
                // AutoRetainer pattern: TargetAndInteract already handles interaction
                success = true;
            }
        }
        // Strategy 5: Final retry with exact name
        else if (targetAttempts == 5)
        {
            log.Information("[Cactpot] Attempt 5: Using improved TargetAndInteract");
            if (GameHelpers.TargetAndInteract("Mini Cactpot Broker"))
            {
                // AutoRetainer pattern: TargetAndInteract already handles interaction
                success = true;
            }
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
                // Send periodic jumps to help with pathing if stuck on aetheryte
                SendPeriodicJump(new Vector3(-46.655319213867f, 1.5999846458435f, 20.395349502563f));
                break;

            case CactpotState.MiniWaitingForArrival:
                if (elapsed > 15)
                {
                    log.Information("[Cactpot] Arrived at Cactpot Board area");
                    SetState(CactpotState.MiniTargeting);
                }
                // Send periodic jumps during arrival wait to help with pathing
                SendPeriodicJump(new Vector3(-46.655319213867f, 1.5999846458435f, 20.395349502563f));
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
                        // AutoRetainer pattern: TargetAndInteract already handled interaction
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
                log.Information("[Cactpot] Mini Cactpot sequence finished, closing menus and returning home");
                GameHelpers.SendNumpadPlus();
                SetState(CactpotState.MiniReturningHome);
                break;

            case CactpotState.MiniReturningHome:
                if (elapsed < 0.5)
                    return;

                log.Information("[Cactpot] Returning home after Mini Cactpot: /li home");
                commandManager.ProcessCommand("/li home");
                SetState(CactpotState.MiniWaitingForHome);
                break;

            case CactpotState.MiniWaitingForHome:
                if (clientState.TerritoryType != GoldSaucerTerritoryId && GameHelpers.IsPlayerAvailable())
                {
                    log.Information("[Cactpot] Returned home after Mini Cactpot");
                    SetState(CactpotState.Complete);
                }
                else if (elapsed > 12 && GameHelpers.IsPlayerAvailable())
                {
                    log.Information("[Cactpot] /li home settled without a territory change, continuing");
                    SetState(CactpotState.Complete);
                }
                else if (elapsed > 25)
                {
                    log.Warning("[Cactpot] Timed out waiting for /li home to settle, continuing");
                    SetState(CactpotState.Complete);
                }
                break;

            // ==================== JUMBO CACTPOT BUY ====================
            case CactpotState.JumboLifestreaming:
                log.Information("[Cactpot] Lifestreaming to Cactpot area: /li Cactpot");
                commandManager.ProcessCommand("/li Cactpot");
                SetState(CactpotState.JumboWaitingForZone);
                break;

            case CactpotState.JumboWaitingForZone:
                if (elapsed > JumboAetheryteSettleDelay && GameHelpers.IsPlayerAvailable())
                {
                    log.Information("[Cactpot] Jumbo broker aetheryte travel settled, starting navigation");
                    SetState(CactpotState.JumboNavigatingToBroker);
                }
                else if (elapsed > 30)
                {
                    log.Error("[Cactpot] Timeout waiting for Jumbo Cactpot lifestream to settle");
                    SetState(CactpotState.Failed);
                }
                break;

            case CactpotState.JumboNavigatingToBroker:
                log.Information("[Cactpot] Navigating to Jumbo Cactpot Broker");
                IssueJumboNavigation(JumboBrokerMoveCommand, "Jumbo Cactpot Broker");
                SetState(CactpotState.JumboWaitingForArrival);
                break;

            case CactpotState.JumboWaitingForArrival:
                if (TryTransitionJumboWaypointToTargeting(
                        "Jumbo Cactpot Broker",
                        JumboBrokerPosition,
                        CactpotState.JumboTargetingBroker))
                {
                    break;
                }

                if (elapsed > JumboArrivalTimeout)
                {
                    log.Error("[Cactpot] Timeout waiting to reach Jumbo Cactpot Broker");
                    SetState(CactpotState.Failed);
                }
                else if (RetryJumboNavigationIfNeeded(JumboBrokerMoveCommand, "Jumbo Cactpot Broker"))
                {
                    // Keep feeding the original waypoint path until it is time to stop and target.
                }
                SendPeriodicJump(JumboBrokerPosition);
                break;

            case CactpotState.JumboClosingToBroker:
                if (TryTransitionJumboNpcRangeToTargeting("Jumbo Cactpot Broker", CactpotState.JumboTargetingBroker))
                {
                    break;
                }

                if (elapsed > JumboCloseApproachTimeout)
                {
                    log.Error("[Cactpot] Timeout while closing the last few yalms to Jumbo Cactpot Broker");
                    SetState(CactpotState.Failed);
                }
                else if (RetryJumboCloseApproachIfNeeded("Jumbo Cactpot Broker"))
                {
                    // Dedicated post-stop movement phase. No targeting happens here.
                }
                break;

            case CactpotState.JumboTargetingBroker:
                if (elapsed < JumboPostNavigationSettleDelay)
                {
                    break;
                }

                if (TryBeginJumboCloseApproachIfOutOfRange(
                        "Jumbo Cactpot Broker",
                        CactpotState.JumboClosingToBroker))
                {
                    break;
                }

                if (TryTargetAndInteractJumboNpc("Jumbo Cactpot Broker"))
                    SetState(CactpotState.JumboInteractingBroker);
                else if (elapsed > 15)
                {
                    log.Error("[Cactpot] Failed to target and interact with Jumbo Cactpot Broker after arriving");
                    SetState(CactpotState.Failed);
                }
                break;

            case CactpotState.JumboInteractingBroker:
                if (GameHelpers.ClickYesIfVisible())
                {
                    log.Information("[Cactpot] Accepted the broker's Jumbo Cactpot confirmation prompt");
                    stateEnteredAt = DateTime.UtcNow;
                    break;
                }

                if (elapsed > 1.5)
                {
                    if (GameHelpers.IsAddonVisible("SelectString"))
                    {
                        SetState(CactpotState.JumboSelectingPurchase);
                    }
                    else
                    {
                        // AutoRetainer pattern: TargetAndInteract already handled interaction
                        if (elapsed > 10)
                        {
                            log.Warning("[Cactpot] Broker menu did not open in time; assuming Jumbo flow is already complete");
                            SetState(CactpotState.JumboComplete);
                        }
                    }
                }
                break;

            case CactpotState.JumboSelectingPurchase:
                if (elapsed > 0.5)
                {
                    log.Information("[Cactpot] Selecting purchase option (SelectString 0)");
                    GameHelpers.FireAddonCallback("SelectString", true, 0);
                    SetState(CactpotState.JumboWaitingForInputWindow);
                }
                break;

            case CactpotState.JumboWaitingForInputWindow:
                if (GameHelpers.IsAddonVisible("LotteryWeeklyInput"))
                {
                    log.Information($"[Cactpot] LotteryWeeklyInput visible for Jumbo ticket {currentTicket}/{totalTickets}, entering number {currentJumboNumber:0000}");
                    GameHelpers.FireAddonCallback("LotteryWeeklyInput", true, currentJumboNumber);
                    SetState(CactpotState.JumboWaitingForConfirmation);
                }
                else if (currentTicket > 1 && GameHelpers.IsAddonVisible("SelectYesno") && GameHelpers.ClickYesIfVisible())
                {
                    log.Information($"[Cactpot] Accepted follow-up Jumbo Yes/No prompt while waiting for ticket {currentTicket}/{totalTickets}");
                    stateEnteredAt = DateTime.UtcNow;
                }
                else if (currentTicket > 1 && GameHelpers.IsAddonVisible("SelectString"))
                {
                    log.Information($"[Cactpot] SelectString returned for Jumbo ticket {currentTicket}/{totalTickets}, selecting purchase option again");
                    SetState(CactpotState.JumboSelectingPurchase);
                }
                else if (elapsed > 10)
                {
                    log.Warning($"[Cactpot] LotteryWeeklyInput did not appear for Jumbo ticket {currentTicket}/{totalTickets}; assuming purchase flow is already complete");
                    SetState(CactpotState.JumboComplete);
                }
                break;

            case CactpotState.JumboWaitingForConfirmation:
                if (GameHelpers.ClickYesIfVisible())
                {
                    log.Information($"[Cactpot] Accepted Jumbo Cactpot Yes/No prompt for ticket {currentTicket}/{totalTickets}");
                    if (currentTicket >= totalTickets)
                    {
                        SetState(CactpotState.JumboComplete);
                    }
                    else
                    {
                        currentTicket++;
                        SetState(CactpotState.JumboWaitingForInputWindow);
                    }
                }
                else if (elapsed > 10)
                {
                    log.Warning($"[Cactpot] Jumbo confirmation stage stalled for ticket {currentTicket}/{totalTickets}; assuming purchase flow completed");
                    SetState(CactpotState.JumboComplete);
                }
                break;

            case CactpotState.JumboComplete:
                log.Information("[Cactpot] Jumbo Cactpot Buy sequence finished");
                GameHelpers.SendNumpadPlus();
                SetState(CactpotState.Complete);
                break;

            // ==================== JUMBO CACTPOT CHECK (Saturday) ====================
            case CactpotState.JumboCheckLifestreaming:
                log.Information("[Cactpot] Lifestreaming to Cactpot area for check: /li Cactpot");
                commandManager.ProcessCommand("/li Cactpot");
                SetState(CactpotState.JumboCheckWaitingForZone);
                break;

            case CactpotState.JumboCheckWaitingForZone:
                if (elapsed > JumboAetheryteSettleDelay && GameHelpers.IsPlayerAvailable())
                {
                    log.Information("[Cactpot] Jumbo cashier aetheryte travel settled, starting navigation");
                    SetState(CactpotState.JumboCheckNavigatingToCashier);
                }
                else if (elapsed > 30)
                {
                    log.Error("[Cactpot] Timeout waiting for Jumbo Cactpot cashier lifestream to settle");
                    SetState(CactpotState.Failed);
                }
                break;

            case CactpotState.JumboCheckNavigatingToCashier:
                log.Information("[Cactpot] Navigating to Jumbo Cactpot Cashier");
                IssueJumboNavigation(JumboCashierMoveCommand, "Jumbo Cactpot Cashier");
                SetState(CactpotState.JumboCheckWaitingForArrival);
                break;

            case CactpotState.JumboCheckWaitingForArrival:
                if (TryTransitionJumboWaypointToTargeting(
                        "Jumbo Cactpot Cashier",
                        JumboCashierPosition,
                        CactpotState.JumboCheckTargetingCashier))
                {
                    break;
                }

                if (elapsed > JumboArrivalTimeout)
                {
                    log.Error("[Cactpot] Timeout waiting to reach Jumbo Cactpot Cashier");
                    SetState(CactpotState.Failed);
                }
                else if (RetryJumboNavigationIfNeeded(JumboCashierMoveCommand, "Jumbo Cactpot Cashier"))
                {
                    // Keep feeding the original waypoint path until it is time to stop and target.
                }
                SendPeriodicJump(JumboCashierPosition);
                break;

            case CactpotState.JumboCheckClosingToCashier:
                if (TryTransitionJumboNpcRangeToTargeting("Jumbo Cactpot Cashier", CactpotState.JumboCheckTargetingCashier))
                {
                    break;
                }

                if (elapsed > JumboCloseApproachTimeout)
                {
                    log.Error("[Cactpot] Timeout while closing the last few yalms to Jumbo Cactpot Cashier");
                    SetState(CactpotState.Failed);
                }
                else if (RetryJumboCloseApproachIfNeeded("Jumbo Cactpot Cashier"))
                {
                    // Dedicated post-stop movement phase. No targeting happens here.
                }
                break;

            case CactpotState.JumboCheckTargetingCashier:
                if (elapsed < JumboPostNavigationSettleDelay)
                {
                    break;
                }

                if (TryBeginJumboCloseApproachIfOutOfRange(
                        "Jumbo Cactpot Cashier",
                        CactpotState.JumboCheckClosingToCashier))
                {
                    break;
                }

                if (TryTargetAndInteractJumboNpc("Jumbo Cactpot Cashier"))
                    SetState(CactpotState.JumboCheckInteractingCashier);
                else if (elapsed > 15)
                {
                    log.Error("[Cactpot] Failed to target and interact with Jumbo Cactpot Cashier after arriving");
                    SetState(CactpotState.Failed);
                }
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
                        // AutoRetainer pattern: TargetAndInteract already handled interaction
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
        else if (newState == CactpotState.JumboNavigatingToBroker ||
                 newState == CactpotState.JumboWaitingForArrival ||
                 newState == CactpotState.JumboClosingToBroker ||
                 newState == CactpotState.JumboCheckNavigatingToCashier ||
                 newState == CactpotState.JumboCheckWaitingForArrival ||
                 newState == CactpotState.JumboCheckClosingToCashier)
        {
            lastJumboNavigationAttempt = DateTime.MinValue;
            lastJumpTime = DateTime.MinValue;
        }
        else if (newState == CactpotState.JumboTargetingBroker || newState == CactpotState.JumboCheckTargetingCashier)
        {
            lastJumboNavigationAttempt = DateTime.MinValue;
            lastJumboTargetAttempt = DateTime.MinValue;
        }
        
        state = newState;
        stateEnteredAt = DateTime.UtcNow;
    }

    private bool HasReachedJumboDestination(Vector3 destination)
    {
        var player = Plugin.ObjectTable.LocalPlayer;
        if (player == null || !GameHelpers.IsPlayerAvailable())
            return false;

        return Vector3.Distance(player.Position, destination) <= JumboArrivalDistance;
    }

    private void StopJumboNavigation()
    {
        commandManager.ProcessCommand("/vnav stop");
    }

    private bool TryTransitionJumboWaypointToTargeting(string npcName, Vector3 waypointPosition, CactpotState targetingState)
    {
        if (!HasReachedJumboDestination(waypointPosition))
            return false;

        StopJumboNavigation();
        log.Information($"[Cactpot] Reached {npcName} waypoint, stopping pathfinding before targeting");
        SetState(targetingState);
        return true;
    }

    private bool TryTransitionJumboNpcRangeToTargeting(string npcName, CactpotState targetingState)
    {
        if (!TryGetJumboNpcInteractionData(npcName, out _, out var distance, out var maxDistance) ||
            distance > maxDistance)
        {
            return false;
        }

        StopJumboNavigation();
        log.Information($"[Cactpot] {npcName} is within interaction range ({distance:F1}y <= {maxDistance:F1}y), stopping pathfinding before targeting");
        SetState(targetingState);
        return true;
    }

    private bool RetryJumboCloseApproachIfNeeded(string npcName)
    {
        if (!TryGetJumboNpcInteractionData(npcName, out var npcPosition, out var distance, out var maxDistance))
        {
            return false;
        }

        var dynamicMoveCommand = TryBuildJumboApproachMoveCommand(npcPosition, maxDistance, out var approachMoveCommand)
            ? approachMoveCommand
            : BuildJumboMoveCommand(npcPosition);

        return RetryJumboNavigationIfNeeded(
            dynamicMoveCommand,
            $"{npcName} ({distance:F1}y > {maxDistance:F1}y, close approach after stop)");
    }

    private bool TryBeginJumboCloseApproachIfOutOfRange(string npcName, CactpotState movementState)
    {
        if (!TryGetJumboNpcInteractionData(npcName, out _, out var distance, out var maxDistance) ||
            distance <= maxDistance)
        {
            return false;
        }

        log.Information($"[Cactpot] {npcName} is still outside interaction range after stopping pathfinding ({distance:F1}y > {maxDistance:F1}y), entering a dedicated close-in movement phase");
        SetState(movementState);
        return true;
    }

    private static string BuildJumboMoveCommand(Vector3 destination)
    {
        var x = destination.X.ToString("0.############", CultureInfo.InvariantCulture);
        var y = destination.Y.ToString("0.############", CultureInfo.InvariantCulture);
        var z = destination.Z.ToString("0.############", CultureInfo.InvariantCulture);
        return $"/vnav moveto {x} {y} {z}";
    }

    private bool TryBuildJumboApproachMoveCommand(Vector3 npcPosition, float maxDistance, out string command)
    {
        command = string.Empty;

        var player = Plugin.ObjectTable.LocalPlayer;
        if (player == null)
            return false;

        var direction = player.Position - npcPosition;
        if (direction.LengthSquared() < 0.0001f)
            return false;

        direction = Vector3.Normalize(direction);
        var desiredStandOffDistance = MathF.Max(0.5f, maxDistance - 0.35f);
        var approachPosition = npcPosition + (direction * desiredStandOffDistance);
        command = BuildJumboMoveCommand(approachPosition);
        return true;
    }

    private bool TryGetJumboNpcInteractionData(string npcName, out Vector3 npcPosition, out float distance, out float maxDistance)
    {
        npcPosition = Vector3.Zero;
        distance = float.MaxValue;
        maxDistance = 0f;

        if (!GameHelpers.IsPlayerAvailable())
            return false;

        var player = Plugin.ObjectTable.LocalPlayer;
        var target = GameHelpers.FindObjectByName(npcName);
        if (player == null || target == null)
            return false;

        npcPosition = target.Position;
        distance = Vector3.Distance(player.Position, target.Position);
        maxDistance = GameHelpers.GetValidInteractionDistance(target);
        return true;
    }

    private void IssueJumboNavigation(string command, string destinationLabel)
    {
        lastJumboNavigationAttempt = DateTime.UtcNow;
        commandManager.ProcessCommand(command);
        log.Debug($"[Cactpot] Issued vnav movement toward {destinationLabel}");
    }

    private bool RetryJumboNavigationIfNeeded(string command, string destinationLabel)
    {
        var now = DateTime.UtcNow;
        if ((now - lastJumboNavigationAttempt).TotalSeconds < JumboNavigationRetryInterval)
            return false;

        IssueJumboNavigation(command, destinationLabel);
        return true;
    }

    private bool TryTargetAndInteractJumboNpc(string npcName)
    {
        if (!GameHelpers.IsPlayerAvailable())
            return false;

        var now = DateTime.UtcNow;
        if ((now - lastJumboTargetAttempt).TotalSeconds < JumboTargetRetryInterval)
            return false;

        var player = Plugin.ObjectTable.LocalPlayer;
        var target = GameHelpers.FindObjectByName(npcName);
        if (player == null || target == null)
            return false;

        var distance = Vector3.Distance(player.Position, target.Position);
        var maxDistance = GameHelpers.GetValidInteractionDistance(target);
        if (distance > maxDistance)
            return false;

        lastJumboTargetAttempt = now;
        log.Information($"[Cactpot] Targeting and interacting with {npcName}");
        return GameHelpers.TargetAndInteract(npcName);
    }

    private int GetConfiguredJumboNumber()
    {
        var activeConfig = configManager.GetActiveConfig();
        return activeConfig.JumboCactpotNumberMode switch
        {
            JumboCactpotNumberMode.Fixed => Math.Clamp(activeConfig.JumboCactpotFixedNumber, 0, 9999),
            _ => Random.Shared.Next(0, 10000),
        };
    }

    private string GetConfiguredJumboModeLabel()
    {
        var activeConfig = configManager.GetActiveConfig();
        return activeConfig.JumboCactpotNumberMode == JumboCactpotNumberMode.Fixed
            ? "fixed"
            : "random";
    }

    /// <summary>
    /// Send periodic jump commands during navigation to help with pathing when stuck on aetheryte.
    /// Jumps every 500ms as requested to help the bot reach its destination.
    /// Stops jumping when within 10 yalms of target position.
    /// </summary>
    private void SendPeriodicJump(Vector3 targetPosition)
    {
        var now = DateTime.UtcNow;
        if ((now - lastJumpTime).TotalSeconds >= JumpInterval)
        {
            // Check distance to target - stop jumping if we're close
            var player = Plugin.ObjectTable.LocalPlayer;
            if (player != null)
            {
                var distance = Vector3.Distance(player.Position, targetPosition);
                if (distance <= JumpStopDistance)
                {
                    log.Debug($"[Cactpot] Stopping jumps - within {distance:F1} yalms of target (stop at {JumpStopDistance})");
                    return;
                }
            }
            
            GameHelpers.SendJump();
            lastJumpTime = now;
        }
    }

    public void Dispose() { }
}
