using System;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using VERMAXION.IPC;
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
    private readonly VNavmeshIPC vnavmesh;
    private readonly LifestreamIPC lifestream;

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
    private static readonly Vector3 JumboBrokerPosition = new(121.13345336914f, 13.001298904419f, -11.011554718018f);
    private static readonly Vector3 JumboCashierPosition = new(124.05115509033f, 13.002527236938f, -19.590528488159f);
    private const string JumboCashierNpcName = "Cactpot Cashier";
    private const int JumboMaxPayoutClaimCount = 3;
    private const double JumboAetheryteSettleDelay = 8.0;
    private const double JumboNavigationRetryInterval = 5.0;
    private const float JumboArrivalDistance = 3.0f;
    private const double JumboArrivalTimeout = 60.0;
    private const double JumboCloseApproachTimeout = 20.0;
    private const double JumboPostNavigationSettleDelay = 0.5;
    private const double JumboTargetRetryInterval = 0.75;
    private const double JumboUiTimeout = 10.0;
    private const double JumboCleanupQuietSeconds = 1.0;
    private static readonly string[] JumboOwnedAddonNames =
    [
        "SelectString",
        "SelectYesno",
        "LotteryWeeklyInput",
        "LotteryWeeklyRewardList",
    ];

    private CactpotState state = CactpotState.Idle;
    private DateTime stateEnteredAt = DateTime.MinValue;
    private int currentTicket = 1;
    private int totalTickets = 3;
    public int MiniTicketsCompletedThisRun { get; private set; }
    private bool miniRunActive;
    private int currentJumboNumber;
    private DateTime lastMiniNavigationAttempt = DateTime.MinValue;
    private DateTime lastMiniTargetAttempt = DateTime.MinValue;
    private int miniTargetAttempts;
    private DateTime lastJumboNavigationAttempt = DateTime.MinValue;
    private DateTime lastJumboTargetAttempt = DateTime.MinValue;
    private DateTime lastJumboConfirmationAttemptAt = DateTime.MinValue;
    private DateTime lastJumboNavigationStopAttempt = DateTime.MinValue;
    private DateTime jumboTravelSettledSince = DateTime.MinValue;
    private DateTime lastJumboCleanupAttempt = DateTime.MinValue;
    private DateTime jumboCleanupQuietSince = DateTime.MinValue;
    private int jumboPurchasesVerified;
    private int jumboPayoutClaimsVerified;
    private bool jumboPayoutUiObserved;
    private bool staleJumboPayoutEvidenceObserved;
    private bool jumboCurrentTicketsAlreadyOwned;
    private bool failAfterJumboCleanup;
    private bool jumboCashierDialogueObserved;
    private bool jumboPayoutWasZeroResult;
    private DateTime jumboCashierStableSince = DateTime.MinValue;
    private JumboCactpotRouteDecision jumboRouteDecision;

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
        JumboVerifyingPurchase,
        JumboRecoveryClosingBroker,
        JumboRecoverySettlingBroker,
        JumboClosingWindows,
        JumboSettling,
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
        JumboCheckClosingWindows,
        JumboCheckSettling,
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
    internal JumboCactpotCompletionKind JumboCompletionKind { get; private set; }

    public CactpotService(
        ICommandManager commandManager,
        IPluginLog log,
        IClientState clientState,
        ConfigManager configManager,
        SaucyMiniCactpotService saucyMiniCactpotService,
        VNavmeshIPC vnavmesh,
        LifestreamIPC lifestream)
    {
        this.commandManager = commandManager;
        this.log = log;
        this.clientState = clientState;
        this.configManager = configManager;
        this.saucyMiniCactpotService = saucyMiniCactpotService;
        this.vnavmesh = vnavmesh;
        this.lifestream = lifestream;
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
            log.Information("[Cactpot] Mini Cactpot already has {Tickets}/{TotalTickets} tickets today; verifying return-to-idle before completion",
                activeConfig.MiniCactpotTicketsToday,
                totalTickets);
            SetState(clientState.TerritoryType == GoldSaucerTerritoryId
                ? CactpotState.MiniComplete
                : GameHelpers.IsPlayerAvailable()
                    ? CactpotState.Complete
                    : CactpotState.Failed);
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
        jumboPurchasesVerified = 0;
        jumboPayoutClaimsVerified = 0;
        jumboPayoutUiObserved = false;
        staleJumboPayoutEvidenceObserved = false;
        jumboCurrentTicketsAlreadyOwned = false;
        failAfterJumboCleanup = false;
        jumboCashierDialogueObserved = false;
        jumboPayoutWasZeroResult = false;
        jumboCashierStableSince = DateTime.MinValue;
        jumboRouteDecision = new JumboCactpotRouteDecision(JumboCactpotRoute.Broker, null, true);
        JumboCompletionKind = JumboCactpotCompletionKind.None;
        lastJumboConfirmationAttemptAt = DateTime.MinValue;
        lastJumboNavigationStopAttempt = DateTime.MinValue;
        ResetJumboCleanupTracking();
        currentJumboNumber = GetConfiguredJumboNumber();
        log.Information($"[Cactpot] Starting Jumbo Cactpot Buy sequence using {GetConfiguredJumboModeLabel()} number {currentJumboNumber:0000}");
        SetState(CactpotState.JumboLifestreaming);
    }

    public void StartJumboCactpotCheck()
    {
        var activeConfig = configManager.GetActiveConfig();
        var now = DateTime.UtcNow;
        var purchaseDue = ResetDetectionService.TaskNeedsRun(
            activeConfig.JumboCactpotLastCompleted,
            activeConfig.JumboCactpotNextReset);
        var decision = JumboCactpotRoutingPolicy.Decide(
            now,
            ResetDetectionService.IsJumboCactpotPayoutAvailable(now),
            activeConfig.JumboCactpotUnclaimedTickets,
            activeConfig.JumboCactpotPayoutAvailableAt,
            purchaseDue);

        if (decision.UsesCashier)
        {
            StartJumboCactpotCheck(decision);
        }
        else if (decision.Route == JumboCactpotRoute.Broker)
        {
            StartJumboCactpot();
        }
        else
        {
            log.Information("[Cactpot] Direct Jumbo start resolved to wait; no NPC interaction was started");
        }
    }

    internal void StartJumboCactpotCheck(JumboCactpotRouteDecision routeDecision)
    {
        if (!routeDecision.UsesCashier)
            throw new ArgumentException("Jumbo cashier start requires a cashier route.", nameof(routeDecision));

        currentTicket = 1;
        totalTickets = routeDecision.ExpectedClaims ?? JumboMaxPayoutClaimCount;
        jumboPayoutClaimsVerified = 0;
        jumboPayoutUiObserved = false;
        staleJumboPayoutEvidenceObserved = false;
        jumboCurrentTicketsAlreadyOwned = false;
        failAfterJumboCleanup = false;
        jumboCashierDialogueObserved = false;
        jumboPayoutWasZeroResult = false;
        jumboCashierStableSince = DateTime.MinValue;
        jumboRouteDecision = routeDecision;
        JumboCompletionKind = JumboCactpotCompletionKind.None;
        lastJumboConfirmationAttemptAt = DateTime.MinValue;
        lastJumboNavigationStopAttempt = DateTime.MinValue;
        ResetJumboCleanupTracking();
        log.Information("[Cactpot] Starting Jumbo cashier sequence route={Route}, expectedClaims={ExpectedClaims}",
            routeDecision.Route,
            routeDecision.ExpectedClaims?.ToString(CultureInfo.InvariantCulture) ?? "discovery");
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

        var purchaseDue = ResetDetectionService.TaskNeedsRun(
            activeConfig.JumboCactpotLastCompleted,
            activeConfig.JumboCactpotNextReset);
        var route = JumboCactpotRoutingPolicy.Decide(
            now,
            ResetDetectionService.IsJumboCactpotPayoutAvailable(now),
            activeConfig.JumboCactpotUnclaimedTickets,
            activeConfig.JumboCactpotPayoutAvailableAt,
            purchaseDue);

        if (route.Route == JumboCactpotRoute.Wait)
        {
            if (activeConfig.JumboCactpotUnclaimedTickets is > 0 &&
                activeConfig.JumboCactpotPayoutAvailableAt > now)
            {
                var dataCenterName = ResetDetectionService.GetCurrentCharacterJumboDataCenterName();
                var payoutTime = activeConfig.JumboCactpotPayoutAvailableAt;
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

        if (route.UsesCashier)
        {
            log.Information("[VERMAXION] Manual Jumbo cashier route triggered: {Route}", route.Route);
            StartJumboCactpotCheck(route);
            return;
        }

        log.Information("[VERMAXION] Manual Jumbo Cactpot purchase triggered");
        StartJumboCactpot();
    }

    public void Reset()
    {
        FinishMiniCactpotRun("reset");
        failAfterJumboCleanup = false;
        staleJumboPayoutEvidenceObserved = false;
        jumboCurrentTicketsAlreadyOwned = false;
        jumboCashierDialogueObserved = false;
        jumboPayoutWasZeroResult = false;
        jumboCashierStableSince = DateTime.MinValue;
        JumboCompletionKind = JumboCactpotCompletionKind.None;
        lastJumboConfirmationAttemptAt = DateTime.MinValue;
        lastJumboNavigationStopAttempt = DateTime.MinValue;
        ResetJumboCleanupTracking();
        SetState(CactpotState.Idle);
    }

    public void HandleChatMessage(string chatType, string speaker, string messageText)
    {
        if (JumboCactpotPurchaseMessagePolicy.TryParsePurchasedNumber(messageText, out var purchasedNumber))
            TryRecordJumboPurchaseFromSystemMessage(purchasedNumber);

        if (IsJumboCheckState(state) && JumboCactpotRecoveryPolicy.IsCashierDialogue(chatType, speaker))
        {
            jumboCashierDialogueObserved = true;
            log.Information("[Cactpot] Confirmed Cactpot Cashier dialogue while waiting for payout evidence");
        }

        if (!IsJumboPurchaseEvidenceState(state) ||
            JumboCactpotRecoveryPolicy.ClassifyDialogue(chatType, speaker, messageText) !=
            JumboDialogueEvidence.StaleRedeemablePayout)
        {
            return;
        }

        staleJumboPayoutEvidenceObserved = true;
        log.Warning("[Cactpot] Broker explicitly reported a stale redeemable Jumbo payout; preparing cashier recovery");
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
                    IssueMiniNavigation(MiniBrokerPosition, MiniBrokerNpcName);
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
                else if (RetryMiniNavigationIfNeeded(MiniBrokerPosition, MiniBrokerNpcName))
                {
                    // Shared navigation observes progress here and suppresses healthy duplicate requests.
                }
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
                    log.Error("[Cactpot] /li home settled without leaving Gold Saucer; Mini Cactpot return-to-idle was not verified");
                    SetState(CactpotState.Failed);
                }
                else if (elapsed > 25)
                {
                    log.Error("[Cactpot] Timed out waiting for verified Mini Cactpot return-to-idle");
                    SetState(CactpotState.Failed);
                }
                break;

            // ==================== JUMBO CACTPOT BUY ====================
            case CactpotState.JumboLifestreaming:
                log.Information("[Cactpot] Lifestreaming to Cactpot area: /li Cactpot");
                commandManager.ProcessCommand("/li Cactpot");
                SetState(CactpotState.JumboWaitingForZone);
                break;

            case CactpotState.JumboWaitingForZone:
                if (IsJumboTravelSettled())
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
                IssueJumboNavigation(JumboBrokerPosition, "Jumbo Cactpot Broker");
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
                else if (RetryJumboNavigationIfNeeded(JumboBrokerPosition, "Jumbo Cactpot Broker"))
                {
                    // Shared navigation observes progress here and suppresses healthy duplicate requests.
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
                if (TryBeginStaleJumboPayoutRecovery())
                    break;

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
                        if (elapsed > JumboUiTimeout)
                        {
                            log.Error("[Cactpot] Broker menu did not open in time; Jumbo purchase completion is ambiguous");
                            SetState(CactpotState.Failed);
                        }
                    }
                }
                break;

            case CactpotState.JumboSelectingPurchase:
                if (TryBeginStaleJumboPayoutRecovery())
                    break;

                if (elapsed > 0.5)
                {
                    log.Information("[Cactpot] Selecting purchase option (SelectString 0)");
                    GameHelpers.FireAddonCallback("SelectString", true, 0);
                    SetState(CactpotState.JumboWaitingForInputWindow);
                }
                break;

            case CactpotState.JumboWaitingForInputWindow:
                if (TryBeginStaleJumboPayoutRecovery())
                    break;

                var purchaseUiEvidence = JumboCactpotRecoveryPolicy.ClassifyPurchaseUi(
                    GameHelpers.IsAddonVisible("LotteryWeeklyInput"),
                    GameHelpers.IsAddonVisible("LotteryWeeklyRewardList"));

                if (purchaseUiEvidence == JumboPurchaseUiEvidence.PurchaseInput)
                {
                    log.Information($"[Cactpot] LotteryWeeklyInput visible for Jumbo ticket {currentTicket}/{totalTickets}, entering number {currentJumboNumber:0000}");
                    GameHelpers.FireAddonCallback("LotteryWeeklyInput", true, currentJumboNumber);
                    SetState(CactpotState.JumboWaitingForConfirmation);
                }
                else if (purchaseUiEvidence == JumboPurchaseUiEvidence.CurrentTicketsAlreadyOwned)
                {
                    jumboCurrentTicketsAlreadyOwned = true;
                    log.Information("[Cactpot] LotteryWeeklyRewardList replaced the purchase input; current Jumbo tickets already exist, completing the purchase phase without cashier routing");
                    SetState(CactpotState.JumboClosingWindows);
                }
                else if (currentTicket > 1 && GameHelpers.IsAddonVisible("SelectYesno") &&
                         GameHelpers.ClickYesIfVisible())
                {
                    log.Information($"[Cactpot] Accepted follow-up Jumbo Yes/No prompt while waiting for ticket {currentTicket}/{totalTickets}");
                    stateEnteredAt = DateTime.UtcNow;
                }
                else if (currentTicket > 1 && GameHelpers.IsAddonVisible("SelectString"))
                {
                    log.Information($"[Cactpot] SelectString returned for Jumbo ticket {currentTicket}/{totalTickets}, selecting purchase option again");
                    SetState(CactpotState.JumboSelectingPurchase);
                }
                else if (elapsed > JumboUiTimeout)
                {
                    log.Error($"[Cactpot] LotteryWeeklyInput did not appear for Jumbo ticket {currentTicket}/{totalTickets}; purchase was not verified");
                    SetState(CactpotState.Failed);
                }
                break;

            case CactpotState.JumboWaitingForConfirmation:
                if (jumboPurchasesVerified >= currentTicket)
                {
                    SetState(CactpotState.JumboVerifyingPurchase);
                }
                else if (TryConfirmJumboCactpotPurchaseYes("Jumbo Cactpot purchase confirmation", allowUnreadable: false))
                {
                    log.Information($"[Cactpot] Accepted Jumbo Cactpot Yes/No prompt for ticket {currentTicket}/{totalTickets}");
                    SetState(CactpotState.JumboVerifyingPurchase);
                }
                else if (elapsed > JumboUiTimeout)
                {
                    log.Error($"[Cactpot] Jumbo confirmation stage stalled for ticket {currentTicket}/{totalTickets}; purchase was not verified");
                    SetState(CactpotState.Failed);
                }
                break;

            case CactpotState.JumboVerifyingPurchase:
                if (jumboPurchasesVerified >= currentTicket)
                {
                    AdvanceAfterJumboPurchaseVerified("system message");
                    break;
                }

                if (GameHelpers.IsAddonVisible("SelectYesno"))
                {
                    if (elapsed > JumboUiTimeout)
                    {
                        log.Error($"[Cactpot] Jumbo purchase confirmation remained open for ticket {currentTicket}/{totalTickets}");
                        SetState(CactpotState.Failed);
                    }

                    break;
                }

                if (elapsed < 0.75)
                    break;

                jumboPurchasesVerified = Math.Max(jumboPurchasesVerified, currentTicket);
                AdvanceAfterJumboPurchaseVerified("confirmation closed");
                break;

            case CactpotState.JumboRecoveryClosingBroker:
                TickJumboCleanup(CactpotState.JumboRecoverySettlingBroker);
                break;

            case CactpotState.JumboRecoverySettlingBroker:
                if (TickJumboSettling(CactpotState.JumboRecoveryClosingBroker))
                {
                    log.Information("[Cactpot] Broker interaction settled; navigating directly to the cashier for stale payout recovery");
                    SetState(CactpotState.JumboCheckNavigatingToCashier);
                }
                break;

            case CactpotState.JumboClosingWindows:
                TickJumboCleanup(CactpotState.JumboSettling);
                break;

            case CactpotState.JumboSettling:
                if (TickJumboSettling(CactpotState.JumboClosingWindows))
                    SetState(failAfterJumboCleanup ? CactpotState.Failed : CactpotState.JumboComplete);
                break;

            case CactpotState.JumboComplete:
                if (!JumboCactpotRecoveryPolicy.CanCompletePurchase(
                        jumboCurrentTicketsAlreadyOwned,
                        jumboPurchasesVerified,
                        totalTickets))
                {
                    log.Error("[Cactpot] Refusing Jumbo purchase success with existingTickets={ExistingTickets}, verifiedPurchases={VerifiedPurchases}/{TotalTickets}",
                        jumboCurrentTicketsAlreadyOwned,
                        jumboPurchasesVerified,
                        totalTickets);
                    SetState(CactpotState.Failed);
                    break;
                }

                log.Information("[Cactpot] Jumbo Cactpot Buy sequence verified and settled (existingTickets={ExistingTickets}, verifiedPurchases={VerifiedPurchases}/{TotalTickets})",
                    jumboCurrentTicketsAlreadyOwned,
                    jumboPurchasesVerified,
                    totalTickets);
                JumboCompletionKind = JumboCactpotCompletionKind.PurchaseBatchEstablished;
                SetState(CactpotState.Complete);
                break;

            // ==================== JUMBO CACTPOT CHECK (Saturday) ====================
            case CactpotState.JumboCheckLifestreaming:
                log.Information("[Cactpot] Lifestreaming to Cactpot area for check: /li Cactpot");
                commandManager.ProcessCommand("/li Cactpot");
                SetState(CactpotState.JumboCheckWaitingForZone);
                break;

            case CactpotState.JumboCheckWaitingForZone:
                if (IsJumboTravelSettled())
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
                IssueJumboNavigation(JumboCashierPosition, JumboCashierNpcName);
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
                else if (RetryJumboNavigationIfNeeded(JumboCashierPosition, JumboCashierNpcName))
                {
                    // Shared navigation observes progress here and suppresses healthy duplicate requests.
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
                    jumboCashierDialogueObserved = true;
                    jumboPayoutUiObserved = true;
                    log.Information("[Cactpot] LotteryWeeklyRewardList opened for payout claim {CurrentClaim}/{TotalClaims}",
                        currentTicket,
                        totalTickets);
                    SetState(CactpotState.JumboCheckClosingRewardList);
                }
                else if (GameHelpers.IsAddonVisible("SelectString"))
                {
                    jumboCashierDialogueObserved = true;
                    log.Information("[Cactpot] Cashier dialog opened, selecting the payout option");
                    SetState(CactpotState.JumboCheckSelectingPayoutOption);
                }
                else if (elapsed > JumboUiTimeout)
                {
                    if (JumboCactpotPayoutProgressPolicy.CanAcceptZeroResult(
                            jumboRouteDecision.IsDiscovery,
                            jumboCashierDialogueObserved,
                            jumboPayoutUiObserved,
                            jumboPayoutClaimsVerified,
                            elapsed,
                            JumboUiTimeout))
                    {
                        CompleteZeroResultJumboDiscovery();
                    }
                    else
                    {
                        log.Error("[Cactpot] Cashier interaction produced neither confirmed dialogue nor payout UI; Jumbo payout completion is ambiguous");
                        SetState(CactpotState.Failed);
                    }
                }
                break;

            case CactpotState.JumboCheckSelectingPayoutOption:
                if (GameHelpers.IsAddonVisible("SelectString"))
                {
                    jumboCashierDialogueObserved = true;
                    log.Information("[Cactpot] Selecting Jumbo payout option (SelectString 0)");
                    GameHelpers.FireAddonCallback("SelectString", true, 0);
                    SetState(CactpotState.JumboCheckWaitingForRewardList);
                }
                else if (GameHelpers.IsAddonVisible("LotteryWeeklyRewardList"))
                {
                    jumboCashierDialogueObserved = true;
                    jumboPayoutUiObserved = true;
                    SetState(CactpotState.JumboCheckClosingRewardList);
                }
                else if (jumboCashierDialogueObserved && elapsed > 0.5)
                {
                    SetState(CactpotState.JumboCheckWaitingForRewardList);
                }
                else if (elapsed > JumboUiTimeout)
                {
                    log.Error("[Cactpot] Payout option dialog disappeared before the reward list appeared");
                    SetState(CactpotState.Failed);
                }
                break;

            case CactpotState.JumboCheckWaitingForRewardList:
                if (GameHelpers.IsAddonVisible("LotteryWeeklyRewardList"))
                {
                    jumboPayoutUiObserved = true;
                    log.Information("[Cactpot] LotteryWeeklyRewardList visible for payout claim {CurrentClaim}/{TotalClaims}",
                        currentTicket,
                        totalTickets);
                    SetState(CactpotState.JumboCheckClosingRewardList);
                }
                else if (GameHelpers.IsAddonVisible("SelectYesno"))
                {
                    if (GameHelpers.ClickYesIfVisible())
                    {
                        log.Information("[Cactpot] Accepted payout confirmation while waiting for reward list");
                        stateEnteredAt = DateTime.UtcNow;
                    }
                }
                else if (elapsed > JumboUiTimeout)
                {
                    if (JumboCactpotPayoutProgressPolicy.CanAcceptZeroResult(
                            jumboRouteDecision.IsDiscovery,
                            jumboCashierDialogueObserved,
                            jumboPayoutUiObserved,
                            jumboPayoutClaimsVerified,
                            elapsed,
                            JumboUiTimeout))
                    {
                        CompleteZeroResultJumboDiscovery();
                    }
                    else
                    {
                        log.Error("[Cactpot] Reward list did not appear for payout claim {CurrentClaim}/{TotalClaims}; payout was not verified",
                            currentTicket,
                            totalTickets);
                        SetState(CactpotState.Failed);
                    }
                }
                break;

            case CactpotState.JumboCheckClosingRewardList:
                if (GameHelpers.IsAddonVisible("LotteryWeeklyRewardList"))
                {
                    jumboPayoutUiObserved = true;
                    log.Information("[Cactpot] Closing LotteryWeeklyRewardList for payout claim {CurrentClaim}/{TotalClaims}",
                        currentTicket,
                        totalTickets);
                    GameHelpers.FireAddonCallback("LotteryWeeklyRewardList", true, -1);
                    stateEnteredAt = DateTime.UtcNow;
                }
                else if (elapsed > 0.5)
                {
                    RecordVerifiedJumboPayoutClaim();
                    log.Information("[Cactpot] Verified Jumbo payout claim {VerifiedClaims}/{TotalClaims} after reward list closed",
                        jumboPayoutClaimsVerified,
                        totalTickets);
                    SetState(CactpotState.JumboCheckConfirmingMorePrizes);
                }
                break;

            case CactpotState.JumboCheckConfirmingMorePrizes:
                if (jumboRouteDecision.IsDiscovery)
                {
                    if (currentTicket >= JumboMaxPayoutClaimCount)
                    {
                        if (GameHelpers.IsAddonVisible("SelectYesno"))
                        {
                            CloseFinalJumboClaimPrompt(elapsed);
                            break;
                        }

                        CompleteVerifiedJumboPayoutClaims();
                        break;
                    }

                    if (GameHelpers.IsAddonVisible("SelectYesno"))
                    {
                        jumboCashierStableSince = DateTime.MinValue;
                        if (GameHelpers.ClickYesIfVisible())
                        {
                            currentTicket++;
                            log.Information("[Cactpot] Discovery accepted 'Claim more prizes?'; continuing with claim {CurrentClaim}/{MaxClaims}",
                                currentTicket,
                                JumboMaxPayoutClaimCount);
                            SetState(CactpotState.JumboCheckWaitingForRewardList);
                        }
                    }
                    else if (GameHelpers.IsAddonVisible("LotteryWeeklyRewardList"))
                    {
                        jumboCashierStableSince = DateTime.MinValue;
                        currentTicket++;
                        SetState(CactpotState.JumboCheckClosingRewardList);
                    }
                    else if (GameHelpers.IsAddonVisible("SelectString"))
                    {
                        if (jumboCashierStableSince == DateTime.MinValue)
                            jumboCashierStableSince = DateTime.UtcNow;

                        var stableSeconds = (DateTime.UtcNow - jumboCashierStableSince).TotalSeconds;
                        if (JumboCactpotPayoutProgressPolicy.IsStableDiscoveryExhaustion(
                                discovery: true,
                                jumboPayoutClaimsVerified,
                                cashierReturnVisible: true,
                                stableSeconds,
                                JumboCleanupQuietSeconds))
                        {
                            log.Information("[Cactpot] Cashier return remained stable after {VerifiedClaims} discovered claim(s); treating the batch as exhausted",
                                jumboPayoutClaimsVerified);
                            CompleteVerifiedJumboPayoutClaims();
                        }
                    }
                    else
                    {
                        jumboCashierStableSince = DateTime.MinValue;
                        if (elapsed > JumboUiTimeout)
                        {
                            log.Error("[Cactpot] Discovery did not reach a stable cashier return after {VerifiedClaims} verified claim(s)",
                                jumboPayoutClaimsVerified);
                            SetState(CactpotState.Failed);
                        }
                    }

                    break;
                }

                if (currentTicket >= totalTickets)
                {
                    if (GameHelpers.IsAddonVisible("SelectYesno"))
                    {
                        CloseFinalJumboClaimPrompt(elapsed);
                        break;
                    }

                    if (jumboPayoutUiObserved &&
                        jumboPayoutClaimsVerified >= totalTickets &&
                        !GameHelpers.IsAddonVisible("LotteryWeeklyRewardList") &&
                        !GameHelpers.IsAddonVisible("SelectYesno"))
                    {
                        log.Information("[Cactpot] Finished all expected Jumbo payout claims");
                        CompleteVerifiedJumboPayoutClaims();
                    }
                    else if (elapsed > JumboUiTimeout)
                    {
                        log.Error("[Cactpot] Final Jumbo payout state did not settle after {VerifiedClaims}/{TotalClaims} verified claims",
                            jumboPayoutClaimsVerified,
                            totalTickets);
                        SetState(CactpotState.Failed);
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
                    log.Information("[Cactpot] Reward list advanced without an intermediate Yes/No prompt; closing claim {CurrentClaim}/{TotalClaims}",
                        currentTicket,
                        totalTickets);
                    SetState(CactpotState.JumboCheckClosingRewardList);
                }
                else if (elapsed > 6)
                {
                    log.Error("[Cactpot] Follow-up payout prompt did not appear after claim {CurrentClaim}/{TotalClaims}; payout was not fully verified",
                        currentTicket,
                        totalTickets);
                    SetState(CactpotState.Failed);
                }
                break;

            case CactpotState.JumboCheckClosingWindows:
                TickJumboCleanup(CactpotState.JumboCheckSettling);
                break;

            case CactpotState.JumboCheckSettling:
                if (TickJumboSettling(CactpotState.JumboCheckClosingWindows))
                    SetState(failAfterJumboCleanup ? CactpotState.Failed : CactpotState.JumboCheckComplete);
                break;

            case CactpotState.JumboCheckComplete:
                var verifiedExpectedClaims = jumboPayoutUiObserved &&
                    JumboCactpotPayoutProgressPolicy.CanCompleteClaims(
                        jumboRouteDecision.ExpectedClaims,
                        jumboPayoutClaimsVerified,
                        discoveryExhausted: jumboRouteDecision.IsDiscovery);
                var verifiedZeroResult = jumboRouteDecision.IsDiscovery &&
                    jumboCashierDialogueObserved &&
                    jumboPayoutWasZeroResult &&
                    jumboPayoutClaimsVerified == 0;

                if (!verifiedExpectedClaims && !verifiedZeroResult)
                {
                    log.Error("[Cactpot] Refusing Jumbo cashier success route={Route}, observedDialogue={ObservedDialogue}, observedUi={ObservedUi}, verifiedClaims={VerifiedClaims}/{ExpectedClaims}, zeroResult={ZeroResult}",
                        jumboRouteDecision.Route,
                        jumboCashierDialogueObserved,
                        jumboPayoutUiObserved,
                        jumboPayoutClaimsVerified,
                        jumboRouteDecision.ExpectedClaims?.ToString(CultureInfo.InvariantCulture) ?? "discovery",
                        jumboPayoutWasZeroResult);
                    SetState(CactpotState.Failed);
                    break;
                }

                if ((!jumboPayoutWasZeroResult && jumboRouteDecision.ContinueToBrokerAfterClaims) ||
                    (jumboPayoutWasZeroResult && jumboRouteDecision.ContinueToBrokerAfterZero))
                {
                    PrepareJumboPurchaseAfterRecoveredPayout();
                    break;
                }

                JumboCompletionKind = jumboRouteDecision.Route == JumboCactpotRoute.ScheduledCashier
                    ? JumboCactpotCompletionKind.ScheduledPayoutComplete
                    : JumboCactpotCompletionKind.PreservedExistingCompletion;
                log.Information("[Cactpot] Jumbo cashier sequence verified and settled with completion={CompletionKind}",
                    JumboCompletionKind);
                SetState(CactpotState.Complete);
                break;
        }
    }

    private void SetState(CactpotState newState)
    {
        var previousState = state;

        if (newState == CactpotState.Failed &&
            IsJumboState(previousState) &&
            previousState is not (CactpotState.JumboClosingWindows
                or CactpotState.JumboSettling
                or CactpotState.JumboCheckClosingWindows
                or CactpotState.JumboCheckSettling))
        {
            failAfterJumboCleanup = true;
            newState = IsJumboCheckState(previousState)
                ? CactpotState.JumboCheckClosingWindows
                : CactpotState.JumboClosingWindows;
        }

        log.Information($"[Cactpot] {state} -> {newState}");

        if (newState == CactpotState.MiniTargeting)
        {
            lastMiniTargetAttempt = DateTime.MinValue;
            miniTargetAttempts = 0;
        }
        else if (newState == CactpotState.MiniClosingToBroker)
        {
            lastMiniNavigationAttempt = DateTime.MinValue;
        }
        else if (newState == CactpotState.MiniNavigating)
        {
            lastMiniNavigationAttempt = DateTime.MinValue;
            lastMiniTargetAttempt = DateTime.MinValue;
        }
        else if (newState is CactpotState.JumboWaitingForZone or CactpotState.JumboCheckWaitingForZone)
        {
            jumboTravelSettledSince = DateTime.MinValue;
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
        else if (newState is CactpotState.JumboClosingWindows
                 or CactpotState.JumboSettling
                 or CactpotState.JumboCheckClosingWindows
                 or CactpotState.JumboCheckSettling)
        {
            ResetJumboCleanupTracking();
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

        if (newState is CactpotState.Idle or CactpotState.Complete or CactpotState.Failed)
            failAfterJumboCleanup = false;
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

    private static bool IsJumboState(CactpotState value)
        => value is CactpotState.JumboLifestreaming
            or CactpotState.JumboWaitingForZone
            or CactpotState.JumboNavigatingToBroker
            or CactpotState.JumboWaitingForArrival
            or CactpotState.JumboClosingToBroker
            or CactpotState.JumboTargetingBroker
            or CactpotState.JumboInteractingBroker
            or CactpotState.JumboSelectingPurchase
            or CactpotState.JumboWaitingForInputWindow
            or CactpotState.JumboWaitingForConfirmation
            or CactpotState.JumboVerifyingPurchase
            or CactpotState.JumboRecoveryClosingBroker
            or CactpotState.JumboRecoverySettlingBroker
            or CactpotState.JumboClosingWindows
            or CactpotState.JumboSettling
            or CactpotState.JumboComplete
            or CactpotState.JumboCheckLifestreaming
            or CactpotState.JumboCheckWaitingForZone
            or CactpotState.JumboCheckNavigatingToCashier
            or CactpotState.JumboCheckWaitingForArrival
            or CactpotState.JumboCheckClosingToCashier
            or CactpotState.JumboCheckTargetingCashier
            or CactpotState.JumboCheckInteractingCashier
            or CactpotState.JumboCheckSelectingPayoutOption
            or CactpotState.JumboCheckWaitingForRewardList
            or CactpotState.JumboCheckClosingRewardList
            or CactpotState.JumboCheckConfirmingMorePrizes
            or CactpotState.JumboCheckClosingWindows
            or CactpotState.JumboCheckSettling
            or CactpotState.JumboCheckComplete;

    private static bool IsJumboCheckState(CactpotState value)
        => value is CactpotState.JumboCheckLifestreaming
            or CactpotState.JumboCheckWaitingForZone
            or CactpotState.JumboCheckNavigatingToCashier
            or CactpotState.JumboCheckWaitingForArrival
            or CactpotState.JumboCheckClosingToCashier
            or CactpotState.JumboCheckTargetingCashier
            or CactpotState.JumboCheckInteractingCashier
            or CactpotState.JumboCheckSelectingPayoutOption
            or CactpotState.JumboCheckWaitingForRewardList
            or CactpotState.JumboCheckClosingRewardList
            or CactpotState.JumboCheckConfirmingMorePrizes
            or CactpotState.JumboCheckClosingWindows
            or CactpotState.JumboCheckSettling
            or CactpotState.JumboCheckComplete;

    private static bool IsJumboPurchaseEvidenceState(CactpotState value)
        => value is CactpotState.JumboInteractingBroker
            or CactpotState.JumboSelectingPurchase
            or CactpotState.JumboWaitingForInputWindow;

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

    private bool TryConfirmJumboCactpotPurchaseYes(string reason, bool allowUnreadable)
    {
        var now = DateTime.UtcNow;
        if (lastJumboConfirmationAttemptAt != DateTime.MinValue &&
            (now - lastJumboConfirmationAttemptAt).TotalSeconds < 1.25)
        {
            return false;
        }

        if (!GameHelpers.TryClickYesIfPromptAllowed(
                prompt => JumboCactpotPurchaseConfirmationPolicy.ShouldConfirmPurchasePrompt(prompt, allowUnreadable),
                reason,
                allowUnreadable,
                out var promptText))
        {
            return false;
        }

        lastJumboConfirmationAttemptAt = now;
        log.Information("[Cactpot] Confirmed {Reason}{PromptText}",
            reason,
            string.IsNullOrWhiteSpace(promptText) ? string.Empty : $": '{promptText}'");
        return true;
    }

    private bool TryBeginStaleJumboPayoutRecovery()
    {
        if (!staleJumboPayoutEvidenceObserved)
            return false;

        staleJumboPayoutEvidenceObserved = false;
        jumboPayoutClaimsVerified = 0;
        jumboPayoutUiObserved = false;
        currentTicket = 1;
        totalTickets = JumboMaxPayoutClaimCount;
        jumboRouteDecision = new JumboCactpotRouteDecision(
            JumboCactpotRoute.DiscoveryCashier,
            null,
            PurchaseDue: true);
        jumboCashierDialogueObserved = false;
        jumboPayoutWasZeroResult = false;
        jumboCashierStableSince = DateTime.MinValue;
        log.Warning("[Cactpot] Switching from broker purchase to stale payout recovery after authoritative broker dialogue");
        SetState(CactpotState.JumboRecoveryClosingBroker);
        return true;
    }

    private void PrepareJumboPurchaseAfterRecoveredPayout()
    {
        currentTicket = 1;
        totalTickets = 3;
        jumboPurchasesVerified = 0;
        jumboCurrentTicketsAlreadyOwned = false;
        staleJumboPayoutEvidenceObserved = false;
        jumboPayoutWasZeroResult = false;
        JumboCompletionKind = JumboCactpotCompletionKind.None;
        currentJumboNumber = GetConfiguredJumboNumber();
        log.Information("[Cactpot] Stale Jumbo payout verified; returning to the broker to buy three current-cycle tickets using {Mode} number {Number:0000}",
            GetConfiguredJumboModeLabel(),
            currentJumboNumber);
        SetState(CactpotState.JumboNavigatingToBroker);
    }

    private void RecordVerifiedJumboPayoutClaim()
    {
        if (jumboPayoutClaimsVerified >= currentTicket)
            return;

        jumboPayoutClaimsVerified = currentTicket;
        if (jumboRouteDecision.ExpectedClaims is { } expectedClaims)
        {
            var remaining = JumboCactpotPayoutProgressPolicy.RemainingAfterVerifiedClaims(
                expectedClaims,
                jumboPayoutClaimsVerified) ?? 0;
            PersistJumboTicketTruth(remaining, clearCompletionStamp: true, "verified payout claim");
        }
        else
        {
            PersistJumboTicketTruth(null, clearCompletionStamp: true, "verified discovery payout claim");
        }
    }

    private void CompleteZeroResultJumboDiscovery()
    {
        jumboPayoutWasZeroResult = true;
        PersistJumboTicketTruth(0, clearCompletionStamp: false, "zero-result cashier discovery");
        log.Information("[Cactpot] Confirmed cashier dialogue produced no payout UI through the full timeout; discovery found zero tickets");
        SetState(CactpotState.JumboCheckClosingWindows);
    }

    private void CompleteVerifiedJumboPayoutClaims()
    {
        PersistJumboTicketTruth(0, clearCompletionStamp: true, "completed payout batch");
        SetState(CactpotState.JumboCheckClosingWindows);
    }

    private void CloseFinalJumboClaimPrompt(double elapsed)
    {
        if (elapsed > JumboUiTimeout)
        {
            log.Error("[Cactpot] Final 'Claim more prizes?' prompt did not close after {VerifiedClaims}/{TotalClaims} verified claims",
                jumboPayoutClaimsVerified,
                totalTickets);
            SetState(CactpotState.Failed);
            return;
        }

        var now = DateTime.UtcNow;
        if (lastJumboCleanupAttempt != DateTime.MinValue &&
            (now - lastJumboCleanupAttempt).TotalSeconds < 0.75)
        {
            return;
        }

        lastJumboCleanupAttempt = now;
        log.Information("[Cactpot] Closing final 'Claim more prizes?' prompt after {VerifiedClaims}/{TotalClaims} verified claims",
            jumboPayoutClaimsVerified,
            totalTickets);
        GameHelpers.TryCloseAddonByCallback("SelectYesno");
        GameHelpers.CloseCurrentAddon();
    }

    private void PersistJumboTicketTruth(int? remainingTickets, bool clearCompletionStamp, string reason)
    {
        var config = configManager.GetActiveConfig();
        config.JumboCactpotUnclaimedTickets = remainingTickets;
        if (remainingTickets == 0)
            config.JumboCactpotPayoutAvailableAt = DateTime.MinValue;

        if (clearCompletionStamp)
        {
            config.JumboCactpotCompletedThisWeek = false;
            config.JumboCactpotLastCompleted = DateTime.MinValue;
            config.JumboCactpotNextReset = DateTime.MinValue;
        }

        configManager.SaveCurrentAccount();
        log.Information("[Cactpot] Persisted Jumbo ticket truth after {Reason}: remaining={Remaining}",
            reason,
            remainingTickets?.ToString(CultureInfo.InvariantCulture) ?? "unknown");
    }

    private bool TryRecordJumboPurchaseFromSystemMessage(int purchasedNumber)
    {
        if (state is not (CactpotState.JumboWaitingForConfirmation or CactpotState.JumboVerifyingPurchase) ||
            currentTicket < 1 ||
            currentTicket > totalTickets)
        {
            return false;
        }

        if (jumboPurchasesVerified >= currentTicket)
            return false;

        if (purchasedNumber != currentJumboNumber)
        {
            log.Warning("[Cactpot] Jumbo purchase system message number {PurchasedNumber:0000} did not match expected {ExpectedNumber:0000}; accepting system message as purchase confirmation",
                purchasedNumber,
                currentJumboNumber);
        }

        jumboPurchasesVerified = currentTicket;
        log.Information("[Cactpot] Verified Jumbo purchase {VerifiedPurchases}/{TotalTickets} from system message with number {PurchasedNumber:0000}",
            jumboPurchasesVerified,
            totalTickets,
            purchasedNumber);

        if (state == CactpotState.JumboWaitingForConfirmation)
            SetState(CactpotState.JumboVerifyingPurchase);

        return true;
    }

    private void AdvanceAfterJumboPurchaseVerified(string source)
    {
        log.Information("[Cactpot] Verified Jumbo purchase {VerifiedPurchases}/{TotalTickets} after {Source}",
            jumboPurchasesVerified,
            totalTickets,
            source);

        if (jumboPurchasesVerified >= totalTickets)
        {
            SetState(CactpotState.JumboClosingWindows);
            return;
        }

        currentTicket++;

        if (GameHelpers.IsAddonVisible("LotteryWeeklyInput") || GameHelpers.IsAddonVisible("SelectYesno"))
            SetState(CactpotState.JumboWaitingForInputWindow);
        else if (GameHelpers.IsAddonVisible("SelectString"))
            SetState(CactpotState.JumboSelectingPurchase);
        else
            SetState(CactpotState.JumboTargetingBroker);
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

        activeConfig.MiniCactpotCompletedToday = false;

        configManager.SaveCurrentAccount();
        log.Information("[Cactpot] Mini Cactpot ticket {CompletedTicket}/{TotalTickets} complete; tickets today {TicketsToday}/{TotalTickets}; tickets this run {TicketsThisRun}",
            currentTicket,
            totalTickets,
            ticketsToday,
            totalTickets,
            MiniTicketsCompletedThisRun);
    }

    private void ResetMiniNavigationState()
    {
        lastMiniNavigationAttempt = DateTime.MinValue;
        lastMiniTargetAttempt = DateTime.MinValue;
        miniTargetAttempts = 0;
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

        var destination = TryBuildMiniApproachPosition(npcPosition, maxDistance, out var approachPosition)
            ? approachPosition
            : npcPosition;

        return RetryMiniNavigationIfNeeded(
            destination,
            $"{MiniBrokerNpcName} ({distance:F1}y > {maxDistance:F1}y, close approach after stop)");
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
        vnavmesh.Stop();
    }

    private void IssueMiniNavigation(Vector3 destination, string destinationLabel)
    {
        lastMiniNavigationAttempt = DateTime.UtcNow;
        if (vnavmesh.PathfindAndMoveTo(destination))
            log.Debug($"[Cactpot] Issued vnav movement toward {destinationLabel}");
    }

    private bool RetryMiniNavigationIfNeeded(Vector3 destination, string destinationLabel)
    {
        var now = DateTime.UtcNow;
        if ((now - lastMiniNavigationAttempt).TotalSeconds < MiniNavigationRetryInterval)
            return false;

        IssueMiniNavigation(destination, destinationLabel);
        return true;
    }

    private bool TryBuildMiniApproachPosition(Vector3 npcPosition, float maxDistance, out Vector3 position)
    {
        position = default;

        var player = Plugin.ObjectTable.LocalPlayer;
        if (player == null)
            return false;

        var direction = player.Position - npcPosition;
        if (direction.LengthSquared() < 0.0001f)
            return false;

        direction = Vector3.Normalize(direction);
        var desiredStandOffDistance = MathF.Max(0.5f, maxDistance - 0.35f);
        position = npcPosition + (direction * desiredStandOffDistance);
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
        var now = DateTime.UtcNow;
        if (lastJumboNavigationStopAttempt != DateTime.MinValue &&
            (now - lastJumboNavigationStopAttempt).TotalSeconds < 1)
        {
            return;
        }

        lastJumboNavigationStopAttempt = now;
        vnavmesh.Stop();
    }

    private void TickJumboCleanup(CactpotState settlingState)
    {
        StopJumboNavigation();

        var visibleAddons = JumboOwnedAddonNames.Where(GameHelpers.IsAddonVisible).ToList();
        if (visibleAddons.Count == 0)
        {
            SetState(settlingState);
            return;
        }

        var now = DateTime.UtcNow;
        if (lastJumboCleanupAttempt != DateTime.MinValue &&
            (now - lastJumboCleanupAttempt).TotalSeconds < 0.75)
        {
            return;
        }

        lastJumboCleanupAttempt = now;
        log.Information("[Cactpot] Closing Jumbo addons: {VisibleAddons}", string.Join(", ", visibleAddons));
        foreach (var addonName in visibleAddons)
            GameHelpers.TryCloseAddonByCallback(addonName);

        GameHelpers.CloseCurrentAddon();
    }

    private bool TickJumboSettling(CactpotState cleanupState)
    {
        StopJumboNavigation();

        if (JumboOwnedAddonNames.Any(GameHelpers.IsAddonVisible))
        {
            jumboCleanupQuietSince = DateTime.MinValue;
            SetState(cleanupState);
            return false;
        }

        if (!GameHelpers.IsPlayerAvailable())
        {
            jumboCleanupQuietSince = DateTime.MinValue;
            return false;
        }

        var now = DateTime.UtcNow;
        if (jumboCleanupQuietSince == DateTime.MinValue)
            jumboCleanupQuietSince = now;

        return (now - jumboCleanupQuietSince).TotalSeconds >= JumboCleanupQuietSeconds;
    }

    private void ResetJumboCleanupTracking()
    {
        lastJumboCleanupAttempt = DateTime.MinValue;
        jumboCleanupQuietSince = DateTime.MinValue;
    }

    private bool IsJumboTravelSettled()
    {
        if (lifestream.IsBusy() ||
            clientState.TerritoryType != GoldSaucerTerritoryId ||
            !GameHelpers.IsPlayerAvailable())
        {
            jumboTravelSettledSince = DateTime.MinValue;
            return false;
        }

        var now = DateTime.UtcNow;
        if (jumboTravelSettledSince == DateTime.MinValue)
            jumboTravelSettledSince = now;

        return (now - jumboTravelSettledSince).TotalSeconds >= JumboAetheryteSettleDelay;
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

        var destination = TryBuildJumboApproachPosition(npcPosition, maxDistance, out var approachPosition)
            ? approachPosition
            : npcPosition;

        return RetryJumboNavigationIfNeeded(
            destination,
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

    private bool TryBuildJumboApproachPosition(Vector3 npcPosition, float maxDistance, out Vector3 position)
    {
        position = default;

        var player = Plugin.ObjectTable.LocalPlayer;
        if (player == null)
            return false;

        var direction = player.Position - npcPosition;
        if (direction.LengthSquared() < 0.0001f)
            return false;

        direction = Vector3.Normalize(direction);
        var desiredStandOffDistance = MathF.Max(0.5f, maxDistance - 0.35f);
        position = npcPosition + (direction * desiredStandOffDistance);
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

    private void IssueJumboNavigation(Vector3 destination, string destinationLabel)
    {
        lastJumboNavigationAttempt = DateTime.UtcNow;
        lastJumboNavigationStopAttempt = DateTime.MinValue;
        if (vnavmesh.PathfindAndMoveTo(destination))
            log.Debug($"[Cactpot] Issued vnav movement toward {destinationLabel}");
    }

    private bool RetryJumboNavigationIfNeeded(Vector3 destination, string destinationLabel)
    {
        var now = DateTime.UtcNow;
        if ((now - lastJumboNavigationAttempt).TotalSeconds < JumboNavigationRetryInterval)
            return false;

        IssueJumboNavigation(destination, destinationLabel);
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
