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
    private readonly SaucyMiniCactpotService saucyMiniCactpotService;

    private const ushort GoldSaucerTerritoryId = 144;
    private const string MiniBrokerNpcName = "Mini Cactpot Broker";
    private static readonly string[] MiniCactpotYesPromptFragments =
    [
        "Mini Cactpot",
        "Cactpot ticket",
        "purchase a ticket",
        "another ticket",
        "purchase another",
    ];
    private static readonly Vector3 MiniBrokerPosition = new(-46.655319213867f, 1.5999846458435f, 20.395349502563f);
    private const string MiniBrokerMoveCommand = "/vnav moveto -46.655319213867 1.5999846458435 20.395349502563";
    private const double MiniAetheryteSettleDelay = 8.0;
    private const double MiniNavigationRetryInterval = 5.0;
    private const float MiniArrivalDistance = 3.0f;
    private const double MiniArrivalTimeout = 60.0;
    private const double MiniCloseApproachTimeout = 20.0;
    private const double MiniPostNavigationSettleDelay = 0.5;
    private const double MiniTargetRetryInterval = 2.0;
    private const double MiniTicketConfirmTimeout = 12.0;
    private const double MiniLotteryDailyOpenTimeout = 20.0;
    private const double MiniLotteryDailyCloseTimeout = 120.0;
    private const double MiniNextTicketRetargetDelay = 2.0;
    private const int MiniMaxPathfindsWithJumpAssist = 3;
    private const int MiniJumpsPerArmedPathfind = 2;
    private const int MiniMaxJumpsPerRun = 6;
    private const double MiniJumpInterval = 4.0;
    private const float MiniJumpStopDistance = 10f;
    private static readonly Vector3 JumboBrokerPosition = new(121.13345336914f, 13.001298904419f, -11.011554718018f);
    private static readonly Vector3 JumboCashierPosition = new(124.05115509033f, 13.002527236938f, -19.590528488159f);
    private const string JumboCashierNpcName = "Cactpot Cashier";
    private const int JumboPayoutClaimCount = 3;
    private const string JumboBrokerMoveCommand = "/vnav moveto 121.13345336914 13.001298904419 -11.011554718018";
    private const string JumboCashierMoveCommand = "/vnav moveto 124.05115509033 13.002527236938 -19.590528488159";
    private const double JumboAetheryteSettleDelay = 8.0;
    private const double JumboNavigationRetryInterval = 5.0;
    private const float JumboArrivalDistance = 3.0f;
    private const double JumboArrivalTimeout = 60.0;
    private const double JumboCloseApproachTimeout = 20.0;
    private const double JumboPostNavigationSettleDelay = 0.5;
    private const double JumboTargetRetryInterval = 0.75;

    private CactpotState state = CactpotState.Idle;
    private DateTime stateEnteredAt = DateTime.MinValue;
    private int currentTicket = 1;
    private int totalTickets = 3;
    public int MiniTicketsCompletedThisRun { get; private set; }
    private bool miniRunActive;
    private int currentJumboNumber;
    private DateTime lastMiniNavigationAttempt = DateTime.MinValue;
    private DateTime lastMiniTargetAttempt = DateTime.MinValue;
    private DateTime lastMiniJumpTime = DateTime.MinValue;
    private int miniPathfindAttempts;
    private int miniTargetAttempts;
    private int miniJumpAssistAvailableJumps;
    private int miniJumpAssistTotalJumps;
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
        MiniClosingToBroker,
        MiniTargeting,
        MiniInteracting,
        MiniSelectingTicket,
        MiniConfirmingTicketPurchase,
        MiniWaitingForLotteryDaily,
        MiniWaitingForLotteryDailyClose,
        MiniPreparingNextTicket,
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
        JumboCheckSelectingPayoutOption,
        JumboCheckWaitingForRewardList,
        JumboCheckClosingRewardList,
        JumboCheckConfirmingMorePrizes,
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

    public CactpotService(
        ICommandManager commandManager,
        IPluginLog log,
        IClientState clientState,
        ConfigManager configManager,
        SaucyMiniCactpotService saucyMiniCactpotService)
    {
        this.commandManager = commandManager;
        this.log = log;
        this.clientState = clientState;
        this.configManager = configManager;
        this.saucyMiniCactpotService = saucyMiniCactpotService;
    }

    public void StartMiniCactpot()
    {
        log.Information("[Cactpot] Starting Mini Cactpot sequence");

        var activeConfig = configManager.GetActiveConfig();
        totalTickets = 3;
        MiniTicketsCompletedThisRun = 0;
        currentTicket = Math.Clamp(activeConfig.MiniCactpotTicketsToday + 1, 1, totalTickets);
        ResetMiniNavigationState();

        if (activeConfig.MiniCactpotTicketsToday >= totalTickets)
        {
            MarkMiniCactpotDailyCompleteIfNeeded(activeConfig, "existing ticket count");
            log.Information("[Cactpot] Mini Cactpot already has {Tickets}/{TotalTickets} tickets today", activeConfig.MiniCactpotTicketsToday, totalTickets);
            SetState(CactpotState.Complete);
            return;
        }

        if (!saucyMiniCactpotService.BeginMiniCactpotRun(activeConfig.RequireSaucyForMiniCactpot, out var saucyStatus))
        {
            log.Error("[Cactpot] Mini Cactpot cannot start because Saucy is unavailable: {SaucyStatus}", saucyStatus);
            Plugin.ChatGui.Print($"[Vermaxion] Mini Cactpot blocked: {saucyStatus}");
            SetState(CactpotState.Failed);
            return;
        }

        if (!string.IsNullOrWhiteSpace(saucyStatus) &&
            !activeConfig.RequireSaucyForMiniCactpot &&
            saucyStatus.StartsWith("Saucy unavailable:", StringComparison.OrdinalIgnoreCase))
        {
            log.Warning("[Cactpot] Mini Cactpot continuing without required Saucy guarantee: {SaucyStatus}", saucyStatus);
        }

        miniRunActive = true;

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
        currentTicket = 1;
        totalTickets = JumboPayoutClaimCount;
        log.Information("[Cactpot] Starting Jumbo Cactpot payout check sequence");
        SetState(CactpotState.JumboCheckLifestreaming);
    }

    public void RunMiniCactpot()
    {
        log.Information("[VERMAXION] Manual Mini Cactpot triggered");
        StartMiniCactpot();
    }

    public void RunJumboCactpot()
    {
        var activeConfig = configManager.GetActiveConfig();
        var now = DateTime.UtcNow;

        if (ResetDetectionService.TaskIsCompleted(activeConfig.JumboCactpotLastCompleted, activeConfig.JumboCactpotNextReset))
        {
            if (ResetDetectionService.IsJumboPurchasePendingPayout(activeConfig.JumboCactpotLastCompleted, activeConfig.JumboCactpotNextReset))
            {
                var dataCenterName = ResetDetectionService.GetCurrentCharacterJumboDataCenterName();
                var payoutTime = activeConfig.JumboCactpotNextReset == DateTime.MinValue
                    ? ResetDetectionService.GetNextJumboCactpotPayoutAvailability(now)
                    : activeConfig.JumboCactpotNextReset;
                var formattedPayoutTime = FormatUtc(payoutTime);
                Plugin.ChatGui.Print($"[Vermaxion] Too early to check Jumbo payout. Ticket already purchased; payout opens for {dataCenterName} at {formattedPayoutTime}.");
                log.Warning("[VERMAXION] Manual Jumbo payout check blocked because payout is not available yet. Next payout for {DataCenterName}: {PayoutTime}.",
                    dataCenterName,
                    formattedPayoutTime);
                return;
            }

            var formattedReset = FormatUtc(activeConfig.JumboCactpotNextReset);
            Plugin.ChatGui.Print($"[Vermaxion] Jumbo Cactpot already completed until {formattedReset}.");
            log.Information("[VERMAXION] Manual Jumbo Cactpot blocked because the task is already complete until {ResetTime}.",
                formattedReset);
            return;
        }

        if (ResetDetectionService.IsJumboCactpotPayoutAvailable(now))
        {
            log.Information("[VERMAXION] Manual Jumbo payout check triggered");
            StartJumboCactpotCheck();
            return;
        }

        log.Information("[VERMAXION] Manual Jumbo Cactpot purchase triggered");
        StartJumboCactpot();
    }

    public void Reset()
    {
        FinishMiniCactpotRun("reset");
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
                if (elapsed > MiniAetheryteSettleDelay &&
                    clientState.TerritoryType == GoldSaucerTerritoryId &&
                    GameHelpers.IsPlayerAvailable())
                {
                    log.Information("[Cactpot] Mini broker aetheryte travel settled, starting navigation");
                    IssueMiniNavigation(MiniBrokerMoveCommand, MiniBrokerNpcName, true);
                    SetState(CactpotState.MiniWaitingForArrival);
                }
                else if (elapsed > 30)
                {
                    log.Error("[Cactpot] Timeout waiting for player available");
                    SetState(CactpotState.Failed);
                }
                break;

            case CactpotState.MiniWaitingForArrival:
                if (TryTransitionMiniWaypointToTargeting())
                {
                    break;
                }

                if (TryTransitionMiniNpcRangeToTargeting())
                {
                    break;
                }

                if (elapsed > MiniArrivalTimeout)
                {
                    log.Error("[Cactpot] Timeout waiting to reach Mini Cactpot Broker");
                    SetState(CactpotState.Failed);
                    break;
                }
                else if (RetryMiniNavigationIfNeeded(MiniBrokerMoveCommand, MiniBrokerNpcName, true))
                {
                    // Keep feeding the broker waypoint until arrival is confirmed.
                }

                TrySendMiniJumpAssist();
                break;

            case CactpotState.MiniClosingToBroker:
                if (TryTransitionMiniNpcRangeToTargeting())
                {
                    break;
                }

                if (elapsed > MiniCloseApproachTimeout)
                {
                    log.Error("[Cactpot] Timeout while closing the last few yalms to Mini Cactpot Broker");
                    SetState(CactpotState.Failed);
                }
                else if (RetryMiniCloseApproachIfNeeded())
                {
                    // Dedicated post-stop movement phase. No targeting happens here.
                }
                break;

            case CactpotState.MiniTargeting:
                if (elapsed < MiniPostNavigationSettleDelay)
                {
                    break;
                }

                if (TryBeginMiniCloseApproachIfOutOfRange())
                {
                    break;
                }

                if (TryTargetAndInteractMiniNpc())
                {
                    log.Information("[Cactpot] Successfully interacted with Mini Cactpot Broker");
                    SetState(CactpotState.MiniInteracting);
                }
                else if (elapsed > 15)
                {
                    log.Error("[Cactpot] Failed to target and interact with Mini Cactpot Broker after arriving");
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
                if (GameHelpers.IsAddonVisible("LotteryDaily"))
                {
                    log.Information("[Cactpot] LotteryDaily already visible for Mini Cactpot ticket {CurrentTicket}/{TotalTickets}", currentTicket, totalTickets);
                    SetState(CactpotState.MiniWaitingForLotteryDailyClose);
                    break;
                }

                if (!GameHelpers.IsAddonVisible("SelectIconString"))
                {
                    if (elapsed > 3)
                    {
                        log.Warning("[Cactpot] Mini Cactpot ticket menu disappeared before selection; retargeting broker");
                        SetState(CactpotState.MiniTargeting);
                    }

                    break;
                }

                if (elapsed > 0.5)
                {
                    log.Information("[Cactpot] Selecting 'Purchase a Mini Cactpot ticket' (SelectIconString 0) for ticket {CurrentTicket}/{TotalTickets}", currentTicket, totalTickets);
                    // Callback: SelectIconString index 0 = "Purchase a Mini Cactpot ticket"
                    if (GameHelpers.TryFireAddonCallback("SelectIconString", true, 0))
                        SetState(CactpotState.MiniConfirmingTicketPurchase);
                }
                break;

            case CactpotState.MiniConfirmingTicketPurchase:
                if (GameHelpers.IsAddonVisible("LotteryDaily"))
                {
                    log.Information("[Cactpot] LotteryDaily opened before confirmation prompt handling for ticket {CurrentTicket}/{TotalTickets}", currentTicket, totalTickets);
                    SetState(CactpotState.MiniWaitingForLotteryDailyClose);
                    break;
                }

                if (TryConfirmMiniCactpotYes("Mini Cactpot ticket purchase", allowUnreadable: elapsed > 2.5))
                {
                    SetState(CactpotState.MiniWaitingForLotteryDaily);
                    break;
                }

                if (GameHelpers.IsAddonVisible("SelectIconString") && elapsed > 3)
                {
                    log.Warning("[Cactpot] Mini Cactpot confirmation did not appear; selecting ticket again");
                    SetState(CactpotState.MiniSelectingTicket);
                    break;
                }

                if (elapsed > MiniTicketConfirmTimeout)
                {
                    log.Error("[Cactpot] Timeout waiting for guarded Mini Cactpot purchase confirmation");
                    SetState(CactpotState.Failed);
                }
                break;

            case CactpotState.MiniWaitingForLotteryDaily:
                if (GameHelpers.IsAddonVisible("LotteryDaily"))
                {
                    log.Information("[Cactpot] LotteryDaily visible for Mini Cactpot ticket {CurrentTicket}/{TotalTickets}; waiting for Saucy to close it", currentTicket, totalTickets);
                    SetState(CactpotState.MiniWaitingForLotteryDailyClose);
                }
                else if (GameHelpers.IsAddonVisible("SelectYesno") &&
                         TryConfirmMiniCactpotYes("Mini Cactpot follow-up confirmation", allowUnreadable: elapsed > 2.5))
                {
                    stateEnteredAt = DateTime.UtcNow;
                }
                else if (GameHelpers.IsAddonVisible("SelectIconString"))
                {
                    SetState(CactpotState.MiniSelectingTicket);
                }
                else if (elapsed > MiniLotteryDailyOpenTimeout)
                {
                    log.Error("[Cactpot] Timeout waiting for LotteryDaily to open after Mini Cactpot ticket confirmation");
                    SetState(CactpotState.Failed);
                }
                break;

            case CactpotState.MiniWaitingForLotteryDailyClose:
                if (GameHelpers.IsAddonVisible("LotteryDaily"))
                {
                    if (elapsed > MiniLotteryDailyCloseTimeout)
                    {
                        log.Error("[Cactpot] Timeout waiting for Saucy to finish and close LotteryDaily for ticket {CurrentTicket}/{TotalTickets}", currentTicket, totalTickets);
                        SetState(CactpotState.Failed);
                    }

                    break;
                }

                if (elapsed < 0.5)
                    break;

                RecordMiniCactpotTicketComplete();
                if (GetMiniCactpotTicketsToday() >= totalTickets)
                {
                    log.Information("[Cactpot] All Mini Cactpot tickets completed");
                    SetState(CactpotState.MiniComplete);
                }
                else
                {
                    currentTicket = Math.Clamp(GetMiniCactpotTicketsToday() + 1, 1, totalTickets);
                    log.Information("[Cactpot] Preparing Mini Cactpot ticket {CurrentTicket}/{TotalTickets}", currentTicket, totalTickets);
                    SetState(CactpotState.MiniPreparingNextTicket);
                }
                break;

            case CactpotState.MiniPreparingNextTicket:
                if (GetMiniCactpotTicketsToday() >= totalTickets)
                {
                    SetState(CactpotState.MiniComplete);
                    break;
                }

                if (GameHelpers.IsAddonVisible("LotteryDaily"))
                {
                    log.Information("[Cactpot] LotteryDaily reopened for Mini Cactpot ticket {CurrentTicket}/{TotalTickets}", currentTicket, totalTickets);
                    SetState(CactpotState.MiniWaitingForLotteryDailyClose);
                }
                else if (GameHelpers.IsAddonVisible("SelectYesno"))
                {
                    if (TryConfirmMiniCactpotYes("Mini Cactpot next-ticket prompt", allowUnreadable: elapsed > 2.5))
                        SetState(CactpotState.MiniWaitingForLotteryDaily);
                    else if (elapsed > MiniTicketConfirmTimeout)
                    {
                        log.Error("[Cactpot] Timeout waiting for guarded Mini Cactpot next-ticket confirmation");
                        SetState(CactpotState.Failed);
                    }
                }
                else if (GameHelpers.IsAddonVisible("SelectIconString"))
                {
                    SetState(CactpotState.MiniSelectingTicket);
                }
                else if (elapsed > MiniNextTicketRetargetDelay)
                {
                    log.Information("[Cactpot] No next-ticket prompt visible; retargeting Mini Cactpot Broker for ticket {CurrentTicket}/{TotalTickets}", currentTicket, totalTickets);
                    SetState(CactpotState.MiniTargeting);
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
                if (elapsed > JumboAetheryteSettleDelay &&
                    clientState.TerritoryType == GoldSaucerTerritoryId &&
                    GameHelpers.IsPlayerAvailable())
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

                if (TryTransitionJumboNpcRangeToTargeting("Jumbo Cactpot Broker", CactpotState.JumboTargetingBroker))
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
                if (elapsed > JumboAetheryteSettleDelay &&
                    clientState.TerritoryType == GoldSaucerTerritoryId &&
                    GameHelpers.IsPlayerAvailable())
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
                log.Information("[Cactpot] Navigating to {CashierName}", JumboCashierNpcName);
                IssueJumboNavigation(JumboCashierMoveCommand, JumboCashierNpcName);
                SetState(CactpotState.JumboCheckWaitingForArrival);
                break;

            case CactpotState.JumboCheckWaitingForArrival:
                if (TryTransitionJumboWaypointToTargeting(
                        JumboCashierNpcName,
                        JumboCashierPosition,
                        CactpotState.JumboCheckTargetingCashier))
                {
                    break;
                }

                if (TryTransitionJumboNpcRangeToTargeting(JumboCashierNpcName, CactpotState.JumboCheckTargetingCashier))
                {
                    break;
                }

                if (elapsed > JumboArrivalTimeout)
                {
                    log.Error("[Cactpot] Timeout waiting to reach {CashierName}", JumboCashierNpcName);
                    SetState(CactpotState.Failed);
                }
                else if (RetryJumboNavigationIfNeeded(JumboCashierMoveCommand, JumboCashierNpcName))
                {
                    // Keep feeding the original waypoint path until it is time to stop and target.
                }
                break;

            case CactpotState.JumboCheckClosingToCashier:
                if (TryTransitionJumboNpcRangeToTargeting(JumboCashierNpcName, CactpotState.JumboCheckTargetingCashier))
                {
                    break;
                }

                if (elapsed > JumboCloseApproachTimeout)
                {
                    log.Error("[Cactpot] Timeout while closing the last few yalms to {CashierName}", JumboCashierNpcName);
                    SetState(CactpotState.Failed);
                }
                else if (RetryJumboCloseApproachIfNeeded(JumboCashierNpcName))
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
                        JumboCashierNpcName,
                        CactpotState.JumboCheckClosingToCashier))
                {
                    break;
                }

                if (TryTargetAndInteractJumboNpc(JumboCashierNpcName))
                    SetState(CactpotState.JumboCheckInteractingCashier);
                else if (elapsed > 15)
                {
                    log.Error("[Cactpot] Failed to target and interact with {CashierName} after arriving", JumboCashierNpcName);
                    SetState(CactpotState.Failed);
                }
                break;

            case CactpotState.JumboCheckInteractingCashier:
                if (GameHelpers.IsAddonVisible("LotteryWeeklyRewardList"))
                {
                    log.Information("[Cactpot] LotteryWeeklyRewardList opened for payout claim {CurrentClaim}/{TotalClaims}",
                        currentTicket,
                        totalTickets);
                    SetState(CactpotState.JumboCheckClosingRewardList);
                }
                else if (GameHelpers.IsAddonVisible("SelectString"))
                {
                    log.Information("[Cactpot] Cashier dialog opened, selecting the payout option");
                    SetState(CactpotState.JumboCheckSelectingPayoutOption);
                }
                else if (elapsed > 6)
                {
                    log.Warning("[Cactpot] Cashier interaction did not surface payout UI; finishing Jumbo payout check without a reward loop");
                    SetState(CactpotState.JumboCheckComplete);
                }
                break;

            case CactpotState.JumboCheckSelectingPayoutOption:
                if (GameHelpers.IsAddonVisible("SelectString"))
                {
                    log.Information("[Cactpot] Selecting Jumbo payout option (SelectString 0)");
                    GameHelpers.FireAddonCallback("SelectString", true, 0);
                    SetState(CactpotState.JumboCheckWaitingForRewardList);
                }
                else if (GameHelpers.IsAddonVisible("LotteryWeeklyRewardList"))
                {
                    SetState(CactpotState.JumboCheckClosingRewardList);
                }
                else if (elapsed > 4)
                {
                    log.Warning("[Cactpot] Payout option dialog disappeared before the reward list appeared; finishing Jumbo payout check");
                    SetState(CactpotState.JumboCheckComplete);
                }
                break;

            case CactpotState.JumboCheckWaitingForRewardList:
                if (GameHelpers.IsAddonVisible("LotteryWeeklyRewardList"))
                {
                    log.Information("[Cactpot] LotteryWeeklyRewardList visible for payout claim {CurrentClaim}/{TotalClaims}",
                        currentTicket,
                        totalTickets);
                    SetState(CactpotState.JumboCheckClosingRewardList);
                }
                else if (GameHelpers.IsAddonVisible("SelectYesno"))
                {
                    SetState(CactpotState.JumboCheckConfirmingMorePrizes);
                }
                else if (elapsed > 6)
                {
                    log.Warning("[Cactpot] Reward list did not appear for payout claim {CurrentClaim}/{TotalClaims}; finishing Jumbo payout check",
                        currentTicket,
                        totalTickets);
                    SetState(CactpotState.JumboCheckComplete);
                }
                break;

            case CactpotState.JumboCheckClosingRewardList:
                if (GameHelpers.IsAddonVisible("LotteryWeeklyRewardList"))
                {
                    log.Information("[Cactpot] Closing LotteryWeeklyRewardList for payout claim {CurrentClaim}/{TotalClaims}",
                        currentTicket,
                        totalTickets);
                    GameHelpers.FireAddonCallback("LotteryWeeklyRewardList", true, -1);
                    SetState(CactpotState.JumboCheckConfirmingMorePrizes);
                }
                else if (elapsed > 2)
                {
                    SetState(CactpotState.JumboCheckConfirmingMorePrizes);
                }
                break;

            case CactpotState.JumboCheckConfirmingMorePrizes:
                if (currentTicket >= totalTickets)
                {
                    if (!GameHelpers.IsAddonVisible("LotteryWeeklyRewardList") && !GameHelpers.IsAddonVisible("SelectYesno"))
                    {
                        log.Information("[Cactpot] Finished all expected Jumbo payout claims");
                        SetState(CactpotState.JumboCheckComplete);
                    }
                    else if (elapsed > 3)
                    {
                        log.Information("[Cactpot] Final Jumbo payout UI settled after the expected claim count");
                        SetState(CactpotState.JumboCheckComplete);
                    }

                    break;
                }

                if (GameHelpers.IsAddonVisible("SelectYesno"))
                {
                    if (GameHelpers.ClickYesIfVisible())
                    {
                        var completedClaim = currentTicket;
                        currentTicket++;
                        log.Information("[Cactpot] Confirmed 'Claim more prizes?' after claim {CompletedClaim}/{TotalClaims}; continuing to claim {NextClaim}/{TotalClaims}",
                            completedClaim,
                            totalTickets,
                            currentTicket,
                            totalTickets);
                        SetState(CactpotState.JumboCheckWaitingForRewardList);
                    }
                }
                else if (GameHelpers.IsAddonVisible("LotteryWeeklyRewardList"))
                {
                    currentTicket++;
                    log.Information("[Cactpot] Reward list advanced without an intermediate Yes/No prompt; continuing to claim {CurrentClaim}/{TotalClaims}",
                        currentTicket,
                        totalTickets);
                    SetState(CactpotState.JumboCheckClosingRewardList);
                }
                else if (elapsed > 6)
                {
                    log.Warning("[Cactpot] Follow-up payout prompt did not appear after claim {CurrentClaim}/{TotalClaims}; finishing Jumbo payout check",
                        currentTicket,
                        totalTickets);
                    SetState(CactpotState.JumboCheckComplete);
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
        var previousState = state;
        log.Information($"[Cactpot] {state} -> {newState}");

        if (newState == CactpotState.MiniTargeting)
        {
            lastMiniTargetAttempt = DateTime.MinValue;
            miniTargetAttempts = 0;
            miniJumpAssistAvailableJumps = 0;
        }
        else if (newState == CactpotState.MiniClosingToBroker)
        {
            lastMiniNavigationAttempt = DateTime.MinValue;
            miniJumpAssistAvailableJumps = 0;
        }
        else if (newState == CactpotState.MiniNavigating)
        {
            lastMiniNavigationAttempt = DateTime.MinValue;
            lastMiniTargetAttempt = DateTime.MinValue;
        }
        else if (newState == CactpotState.JumboNavigatingToBroker ||
                 newState == CactpotState.JumboClosingToBroker ||
                 newState == CactpotState.JumboCheckNavigatingToCashier ||
                 newState == CactpotState.JumboCheckClosingToCashier)
        {
            lastJumboNavigationAttempt = DateTime.MinValue;
        }
        else if (newState == CactpotState.JumboTargetingBroker || newState == CactpotState.JumboCheckTargetingCashier)
        {
            lastJumboNavigationAttempt = DateTime.MinValue;
            lastJumboTargetAttempt = DateTime.MinValue;
        }

        if (miniRunActive &&
            IsMiniCactpotState(previousState) &&
            (newState == CactpotState.MiniComplete ||
             newState == CactpotState.Complete ||
             newState == CactpotState.Failed ||
             newState == CactpotState.Idle))
        {
            FinishMiniCactpotRun(newState.ToString());
        }
        
        state = newState;
        stateEnteredAt = DateTime.UtcNow;
    }

    private static bool IsMiniCactpotState(CactpotState value)
        => value is CactpotState.MiniTeleporting
            or CactpotState.MiniWaitingForZone
            or CactpotState.MiniNavigating
            or CactpotState.MiniWaitingForArrival
            or CactpotState.MiniClosingToBroker
            or CactpotState.MiniTargeting
            or CactpotState.MiniInteracting
            or CactpotState.MiniSelectingTicket
            or CactpotState.MiniConfirmingTicketPurchase
            or CactpotState.MiniWaitingForLotteryDaily
            or CactpotState.MiniWaitingForLotteryDailyClose
            or CactpotState.MiniPreparingNextTicket
            or CactpotState.MiniComplete
            or CactpotState.MiniReturningHome
            or CactpotState.MiniWaitingForHome;

    private void FinishMiniCactpotRun(string reason)
    {
        if (!miniRunActive)
            return;

        miniRunActive = false;
        saucyMiniCactpotService.EndMiniCactpotRun(reason);
    }

    private bool TryConfirmMiniCactpotYes(string reason, bool allowUnreadable)
    {
        if (!GameHelpers.TryClickYesIfPromptContains(MiniCactpotYesPromptFragments, reason, allowUnreadable, out var promptText))
            return false;

        log.Information("[Cactpot] Confirmed {Reason}{PromptText}",
            reason,
            string.IsNullOrWhiteSpace(promptText) ? string.Empty : $": '{promptText}'");
        return true;
    }

    private int GetMiniCactpotTicketsToday()
        => Math.Clamp(configManager.GetActiveConfig().MiniCactpotTicketsToday, 0, totalTickets);

    private void RecordMiniCactpotTicketComplete()
    {
        var activeConfig = configManager.GetActiveConfig();
        var previousTicketsToday = Math.Clamp(activeConfig.MiniCactpotTicketsToday, 0, totalTickets);
        var ticketsToday = Math.Clamp(Math.Max(previousTicketsToday + 1, currentTicket), 0, totalTickets);
        activeConfig.MiniCactpotTicketsToday = ticketsToday;
        MiniTicketsCompletedThisRun++;

        if (ticketsToday >= totalTickets)
            MarkMiniCactpotDailyCompleteIfNeeded(activeConfig, "ticket count reached 3");
        else
            activeConfig.MiniCactpotCompletedToday = false;

        configManager.SaveCurrentAccount();
        log.Information("[Cactpot] Mini Cactpot ticket {CompletedTicket}/{TotalTickets} complete; tickets today {TicketsToday}/{TotalTickets}; tickets this run {TicketsThisRun}",
            currentTicket,
            totalTickets,
            ticketsToday,
            totalTickets,
            MiniTicketsCompletedThisRun);
    }

    private void MarkMiniCactpotDailyCompleteIfNeeded(CharacterConfig activeConfig, string reason)
    {
        if (activeConfig.MiniCactpotCompletedToday &&
            activeConfig.MiniCactpotLastCompleted != DateTime.MinValue &&
            activeConfig.MiniCactpotNextReset != DateTime.MinValue)
        {
            return;
        }

        var completedAt = DateTime.UtcNow;
        activeConfig.MiniCactpotLastCompleted = completedAt;
        activeConfig.MiniCactpotNextReset = ResetDetectionService.GetNextDailyReset(completedAt);
        activeConfig.MiniCactpotCompletedToday = true;
        activeConfig.MiniCactpotTicketsToday = totalTickets;
        configManager.SaveCurrentAccount();
        log.Information("[Cactpot] Mini Cactpot daily completion recorded after {Reason}; next reset {NextReset:u}",
            reason,
            activeConfig.MiniCactpotNextReset);
    }

    private void ResetMiniNavigationState()
    {
        lastMiniNavigationAttempt = DateTime.MinValue;
        lastMiniTargetAttempt = DateTime.MinValue;
        lastMiniJumpTime = DateTime.MinValue;
        miniPathfindAttempts = 0;
        miniTargetAttempts = 0;
        miniJumpAssistAvailableJumps = 0;
        miniJumpAssistTotalJumps = 0;
    }

    private bool TryTransitionMiniWaypointToTargeting()
    {
        if (!TryGetMiniWaypointDistance(out var distance) || distance > MiniArrivalDistance)
            return false;

        StopMiniNavigation();
        log.Information($"[Cactpot] Reached Mini Cactpot Broker waypoint ({distance:F1}y <= {MiniArrivalDistance:F1}y), stopping pathfinding before targeting");
        SetState(CactpotState.MiniTargeting);
        return true;
    }

    private bool TryTransitionMiniNpcRangeToTargeting()
    {
        if (!TryGetMiniNpcInteractionData(out _, out var distance, out var maxDistance) ||
            distance > maxDistance)
        {
            return false;
        }

        StopMiniNavigation();
        log.Information($"[Cactpot] Mini Cactpot Broker is within interaction range ({distance:F1}y <= {maxDistance:F1}y), stopping pathfinding before targeting");
        SetState(CactpotState.MiniTargeting);
        return true;
    }

    private bool TryBeginMiniCloseApproachIfOutOfRange()
    {
        if (!TryGetMiniNpcInteractionData(out _, out var distance, out var maxDistance) ||
            distance <= maxDistance)
        {
            return false;
        }

        log.Information($"[Cactpot] Mini Cactpot Broker is still outside interaction range after stopping pathfinding ({distance:F1}y > {maxDistance:F1}y), entering close-in movement");
        SetState(CactpotState.MiniClosingToBroker);
        return true;
    }

    private bool RetryMiniCloseApproachIfNeeded()
    {
        if (!TryGetMiniNpcInteractionData(out var npcPosition, out var distance, out var maxDistance))
        {
            return false;
        }

        var dynamicMoveCommand = TryBuildMiniApproachMoveCommand(npcPosition, maxDistance, out var approachMoveCommand)
            ? approachMoveCommand
            : BuildMoveCommand(npcPosition);

        return RetryMiniNavigationIfNeeded(
            dynamicMoveCommand,
            $"{MiniBrokerNpcName} ({distance:F1}y > {maxDistance:F1}y, close approach after stop)",
            false);
    }

    private bool TryTargetAndInteractMiniNpc()
    {
        if (!GameHelpers.IsPlayerAvailable())
            return false;

        var now = DateTime.UtcNow;
        if ((now - lastMiniTargetAttempt).TotalSeconds < MiniTargetRetryInterval)
            return false;

        lastMiniTargetAttempt = now;
        miniTargetAttempts++;
        log.Information($"[Cactpot] Mini broker interaction attempt {miniTargetAttempts}");

        var player = Plugin.ObjectTable.LocalPlayer;
        var target = GameHelpers.FindObjectByName(MiniBrokerNpcName);
        if (player == null || target == null)
            return false;

        var distance = Vector3.Distance(player.Position, target.Position);
        var maxDistance = GameHelpers.GetValidInteractionDistance(target);
        if (distance > maxDistance)
        {
            log.Information($"[Cactpot] Mini broker interaction attempt {miniTargetAttempts} skipped; still out of range ({distance:F1}y > {maxDistance:F1}y)");
            return false;
        }

        log.Information($"[Cactpot] Targeting and interacting with Mini Cactpot Broker ({distance:F1}y <= {maxDistance:F1}y)");
        return GameHelpers.TargetAndInteract(MiniBrokerNpcName);
    }

    private bool TryGetMiniWaypointDistance(out float distance)
    {
        distance = float.MaxValue;

        if (!GameHelpers.IsPlayerAvailable())
            return false;

        var player = Plugin.ObjectTable.LocalPlayer;
        if (player == null)
            return false;

        distance = Vector3.Distance(player.Position, MiniBrokerPosition);
        return true;
    }

    private bool TryGetMiniNpcInteractionData(out Vector3 npcPosition, out float distance, out float maxDistance)
    {
        npcPosition = Vector3.Zero;
        distance = float.MaxValue;
        maxDistance = 0f;

        if (!GameHelpers.IsPlayerAvailable())
            return false;

        var player = Plugin.ObjectTable.LocalPlayer;
        var target = GameHelpers.FindObjectByName(MiniBrokerNpcName);
        if (player == null || target == null)
            return false;

        npcPosition = target.Position;
        distance = Vector3.Distance(player.Position, target.Position);
        maxDistance = GameHelpers.GetValidInteractionDistance(target);
        return true;
    }

    private void StopMiniNavigation()
    {
        commandManager.ProcessCommand("/vnav stop");
    }

    private void IssueMiniNavigation(string command, string destinationLabel, bool armJumpAssist)
    {
        lastMiniNavigationAttempt = DateTime.UtcNow;
        commandManager.ProcessCommand(command);

        if (!armJumpAssist)
        {
            log.Information($"[Cactpot] Mini close-in retry toward {destinationLabel}");
            return;
        }

        miniPathfindAttempts++;
        var addedJumpBudget = 0;
        if (miniPathfindAttempts <= MiniMaxPathfindsWithJumpAssist &&
            miniJumpAssistTotalJumps + miniJumpAssistAvailableJumps < MiniMaxJumpsPerRun)
        {
            var remainingBudget = MiniMaxJumpsPerRun - miniJumpAssistTotalJumps - miniJumpAssistAvailableJumps;
            addedJumpBudget = Math.Min(MiniJumpsPerArmedPathfind, remainingBudget);
            miniJumpAssistAvailableJumps += addedJumpBudget;
        }

        log.Information($"[Cactpot] Mini pathfind attempt {miniPathfindAttempts} toward {destinationLabel}; jump assist +{addedJumpBudget}, queued {miniJumpAssistAvailableJumps}, used {miniJumpAssistTotalJumps}/{MiniMaxJumpsPerRun}");
    }

    private bool RetryMiniNavigationIfNeeded(string command, string destinationLabel, bool armJumpAssist)
    {
        var now = DateTime.UtcNow;
        if ((now - lastMiniNavigationAttempt).TotalSeconds < MiniNavigationRetryInterval)
            return false;

        IssueMiniNavigation(command, destinationLabel, armJumpAssist);
        return true;
    }

    private void TrySendMiniJumpAssist()
    {
        if (miniJumpAssistAvailableJumps <= 0 || miniJumpAssistTotalJumps >= MiniMaxJumpsPerRun)
            return;

        if (!GameHelpers.IsPlayerAvailable())
            return;

        var player = Plugin.ObjectTable.LocalPlayer;
        if (player == null)
            return;

        var distance = Vector3.Distance(player.Position, MiniBrokerPosition);
        if (distance <= MiniJumpStopDistance)
            return;

        var now = DateTime.UtcNow;
        if (lastMiniJumpTime != DateTime.MinValue &&
            (now - lastMiniJumpTime).TotalSeconds < MiniJumpInterval)
        {
            return;
        }

        GameHelpers.SendJump();
        lastMiniJumpTime = now;
        miniJumpAssistAvailableJumps--;
        miniJumpAssistTotalJumps++;
        log.Information($"[Cactpot] Mini jump assist {miniJumpAssistTotalJumps}/{MiniMaxJumpsPerRun}; queued {miniJumpAssistAvailableJumps}, broker waypoint {distance:F1}y away");
    }

    private bool TryBuildMiniApproachMoveCommand(Vector3 npcPosition, float maxDistance, out string command)
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
        command = BuildMoveCommand(approachPosition);
        return true;
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
        => BuildMoveCommand(destination);

    private static string BuildMoveCommand(Vector3 destination)
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

    private static string FormatUtc(DateTime timestamp)
    {
        return timestamp == DateTime.MinValue
            ? "unknown"
            : timestamp.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'");
    }

    public void Dispose()
    {
        FinishMiniCactpotRun("dispose");
    }
}
