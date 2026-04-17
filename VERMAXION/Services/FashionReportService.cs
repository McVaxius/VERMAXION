using System;
using System.Globalization;
using System.Numerics;
using Dalamud.Plugin.Services;

namespace VERMAXION.Services;

/// <summary>
/// Fashion Report automation service.
/// Travels to the Gold Saucer, interacts with Masked Rose, completes the full judging loop four times,
/// then returns control to the engine once the player is back in the world with no Fashion Report UI open.
/// </summary>
public class FashionReportService : IDisposable
{
    private readonly ICommandManager commandManager;
    private readonly IClientState clientState;
    private readonly IObjectTable objectTable;
    private readonly IPluginLog log;

    private FashionReportState state = FashionReportState.Idle;
    private DateTime stateEnteredAt = DateTime.MinValue;
    private int currentAttempt;
    private int completedJudgings;

    private const int MaxRetries = 3;
    private const int RequiredJudgings = 4;
    private const ushort GoldSaucerTerritoryId = 144;
    private const string MaskedRoseName = "Masked Rose";
    private static readonly Vector3 MaskedRosePosition = new(55.864311218262f, 3.9997265338898f, 64.584785461426f);
    private const double DialogueAdvanceIntervalSeconds = 1.0;
    private const double FashionCheckCloseIntervalSeconds = 1.0;
    private const double FashionCheckResultTimeoutSeconds = 75.0;
    private const double PostJudgingSettleTimeoutSeconds = 30.0;

    private DateTime lastJumpTime = DateTime.MinValue;
    private DateTime lastDialogueAdvanceTime = DateTime.MinValue;
    private DateTime lastFashionCheckCloseTime = DateTime.MinValue;
    //private const double JumpIntervalSeconds = 0.5;
    private const double JumpIntervalSeconds = 3;
    private const float JumpStopDistance = 10f;

    public enum FashionReportState
    {
        Idle,
        TeleportingToSaucer,
        WaitingForSaucerZone,
        NavigatingToMaskedRose,
        WaitingForArrival,
        InteractingWithMaskedRose,
        WaitingForDialogueOption,
        ConfirmingJudging,
        WaitingForFashionCheck,
        ClosingFashionCheck,
        WaitingForPostJudgingReturn,
        Complete,
        Failed,
    }

    public FashionReportState State => state;
    public bool IsActive => state != FashionReportState.Idle && state != FashionReportState.Complete && state != FashionReportState.Failed;
    public bool IsComplete => state == FashionReportState.Complete;
    public bool IsFailed => state == FashionReportState.Failed;

    public FashionReportService(ICommandManager commandManager, IClientState clientState, IObjectTable objectTable, IPluginLog log)
    {
        this.commandManager = commandManager;
        this.clientState = clientState;
        this.objectTable = objectTable;
        this.log = log;
    }

    public void Start()
    {
        if (IsActive)
        {
            log.Warning("[FashionReport] Service already active");
            return;
        }

        currentAttempt = 1;
        completedJudgings = 0;
        if (clientState.TerritoryType == GoldSaucerTerritoryId)
        {
            log.Information("[FashionReport] Already in Gold Saucer, skipping teleport");
            SetState(FashionReportState.NavigatingToMaskedRose);
        }
        else
        {
            SetState(FashionReportState.TeleportingToSaucer);
        }
    }

    public void Reset()
    {
        state = FashionReportState.Idle;
        stateEnteredAt = DateTime.MinValue;
        currentAttempt = 0;
        completedJudgings = 0;
        lastJumpTime = DateTime.MinValue;
        lastDialogueAdvanceTime = DateTime.MinValue;
        lastFashionCheckCloseTime = DateTime.MinValue;
    }

    public void Update()
    {
        if (!IsActive)
            return;

        var elapsed = (DateTime.UtcNow - stateEnteredAt).TotalSeconds;

        switch (state)
        {
            case FashionReportState.TeleportingToSaucer:
                if (elapsed < 0.5)
                    return;

                log.Information("[FashionReport] Teleporting to Gold Saucer: /li saucer");
                commandManager.ProcessCommand("/li saucer");
                SetState(FashionReportState.WaitingForSaucerZone);
                break;

            case FashionReportState.WaitingForSaucerZone:
                if (clientState.TerritoryType == GoldSaucerTerritoryId && GameHelpers.IsPlayerAvailable())
                {
                    log.Information("[FashionReport] Arrived at Gold Saucer");
                    SetState(FashionReportState.NavigatingToMaskedRose);
                }
                else if (elapsed > 30)
                {
                    RetryOrFail("Timed out waiting for Gold Saucer zone");
                }
                break;

            case FashionReportState.NavigatingToMaskedRose:
                if (elapsed < 0.5)
                    return;

                log.Information("[FashionReport] Navigating to Masked Rose");
                var coords = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:F8} {1:F8} {2:F8}",
                    MaskedRosePosition.X,
                    MaskedRosePosition.Y,
                    MaskedRosePosition.Z);
                commandManager.ProcessCommand($"/vnav moveto {coords}");
                SetState(FashionReportState.WaitingForArrival);
                break;

            case FashionReportState.WaitingForArrival:
                if (IsNearPosition(MaskedRosePosition, 4.0f))
                {
                    log.Information("[FashionReport] Reached Masked Rose area");
                    SetState(FashionReportState.InteractingWithMaskedRose);
                }
                else if (elapsed > 35)
                {
                    RetryOrFail("Timed out navigating to Masked Rose");
                }
                else
                {
                    SendPeriodicJump(MaskedRosePosition);
                }
                break;

            case FashionReportState.InteractingWithMaskedRose:
                if (elapsed < 0.5)
                    return;

                if (GameHelpers.TargetAndInteract(MaskedRoseName))
                {
                    log.Information($"[FashionReport] Interacted with Masked Rose for judging {completedJudgings + 1}/{RequiredJudgings}");
                    SetState(FashionReportState.WaitingForDialogueOption);
                }
                else if (elapsed > 12)
                {
                    RetryOrFail("Timed out targeting Masked Rose");
                }
                break;

            case FashionReportState.WaitingForDialogueOption:
                if (elapsed < 0.75)
                    return;

                if (GameHelpers.IsAddonVisible("SelectString"))
                {
                    log.Information($"[FashionReport] Selecting 'Present yourself for judging' ({completedJudgings + 1}/{RequiredJudgings})");
                    GameHelpers.FireAddonCallback("SelectString", true, 1);
                    SetState(FashionReportState.ConfirmingJudging);
                }
                else if (GameHelpers.IsAddonVisible("SelectYesno"))
                {
                    SetState(FashionReportState.ConfirmingJudging);
                }
                else if (IsDialogueAdvanceUiVisible())
                {
                    TryAdvanceDialogue("initial Masked Rose dialogue");
                }
                else if (elapsed > 15)
                {
                    RetryOrFail("Timed out waiting for Fashion Report dialogue options");
                }
                break;

            case FashionReportState.ConfirmingJudging:
                if (GameHelpers.ClickYesIfVisible())
                {
                    log.Information($"[FashionReport] Accepted judging confirmation ({completedJudgings + 1}/{RequiredJudgings})");
                    SetState(FashionReportState.WaitingForFashionCheck);
                }
                else if (IsFashionCheckVisible())
                {
                    SetState(FashionReportState.ClosingFashionCheck);
                }
                else if (IsDialogueAdvanceUiVisible())
                {
                    TryAdvanceDialogue("judging confirmation lead-in");
                }
                else if (elapsed > 15)
                {
                    RetryOrFail("Timed out waiting for Fashion Report confirmation");
                }
                break;

            case FashionReportState.WaitingForFashionCheck:
                if (IsFashionCheckVisible())
                {
                    log.Information($"[FashionReport] FashionCheck result window visible ({completedJudgings + 1}/{RequiredJudgings})");
                    SetState(FashionReportState.ClosingFashionCheck);
                }
                else if (GameHelpers.ClickYesIfVisible())
                {
                    log.Information("[FashionReport] Accepted an additional Yes/No prompt while waiting for FashionCheck");
                    stateEnteredAt = DateTime.UtcNow;
                }
                else if (IsDialogueAdvanceUiVisible())
                {
                    TryAdvanceDialogue("post-confirmation dialogue");
                }
                else if (elapsed > FashionCheckResultTimeoutSeconds)
                {
                    RetryOrFail("Timed out waiting for FashionCheck result");
                }
                break;

            case FashionReportState.ClosingFashionCheck:
                if (elapsed < 0.5)
                    return;

                if (IsFashionCheckVisible())
                {
                    TryCloseFashionCheck();

                    if (elapsed > 8 && GameHelpers.IsAddonVisible("FashionCheck"))
                    {
                        log.Warning("[FashionReport] FashionCheck still visible after callback close attempts, sending Escape fallback");
                        GameHelpers.CloseCurrentAddon();
                    }

                    if (elapsed > 15)
                    {
                        RetryOrFail("Timed out closing FashionCheck");
                    }

                    break;
                }

                completedJudgings++;
                log.Information($"[FashionReport] Completed judging {completedJudgings}/{RequiredJudgings}");
                SetState(FashionReportState.WaitingForPostJudgingReturn);
                break;

            case FashionReportState.WaitingForPostJudgingReturn:
                if (IsFashionCheckVisible())
                {
                    SetState(FashionReportState.ClosingFashionCheck);
                }
                else if (IsDialogueAdvanceUiVisible())
                {
                    TryAdvanceDialogue("post-judging dialogue");
                }
                else if (GameHelpers.IsPlayerAvailable() && !IsBlockingUiVisible())
                {
                    if (completedJudgings >= RequiredJudgings)
                    {
                        log.Information("[FashionReport] All four Fashion Report judgings complete");
                        SetState(FashionReportState.Complete);
                    }
                    else
                    {
                        log.Information($"[FashionReport] Preparing next judging {completedJudgings + 1}/{RequiredJudgings}");
                        SetState(FashionReportState.InteractingWithMaskedRose);
                    }
                }
                else if (elapsed > PostJudgingSettleTimeoutSeconds)
                {
                    log.Warning("[FashionReport] Post-judging settle timed out, continuing with best-effort state transition");
                    if (completedJudgings >= RequiredJudgings)
                        SetState(FashionReportState.Complete);
                    else
                        SetState(FashionReportState.InteractingWithMaskedRose);
                }
                break;
        }
    }

    private bool IsNearPosition(Vector3 targetPosition, float threshold)
    {
        var player = objectTable.LocalPlayer;
        if (player == null)
            return false;

        return Vector3.Distance(player.Position, targetPosition) <= threshold;
    }

    private bool IsFashionReportUiVisible()
    {
        return GameHelpers.IsAddonVisible("Talk")
            || GameHelpers.IsAddonVisible("SelectString")
            || GameHelpers.IsAddonVisible("SelectYesno")
            || GameHelpers.IsAddonVisible("JournalAccept")
            || GameHelpers.IsAddonVisible("Request")
            || IsFashionCheckVisible();
    }

    private bool IsDialogueAdvanceUiVisible()
    {
        return GameHelpers.IsAddonVisible("Talk")
            || GameHelpers.IsAddonVisible("JournalAccept")
            || GameHelpers.IsAddonVisible("Request");
    }

    private bool IsFashionCheckVisible()
    {
        return GameHelpers.IsAddonVisible("FashionCheck")
            || GameHelpers.IsAddonVisible("FashionCheckScoreGauge");
    }

    private bool IsBlockingUiVisible()
    {
        return IsFashionReportUiVisible();
    }

    private void TryAdvanceDialogue(string reason)
    {
        var now = DateTime.UtcNow;
        if ((now - lastDialogueAdvanceTime).TotalSeconds < DialogueAdvanceIntervalSeconds)
            return;

        log.Information($"[FashionReport] Advancing {reason}");
        GameHelpers.SendEnd();
        lastDialogueAdvanceTime = now;
    }

    private void TryCloseFashionCheck()
    {
        var now = DateTime.UtcNow;
        if ((now - lastFashionCheckCloseTime).TotalSeconds < FashionCheckCloseIntervalSeconds)
            return;

        if (GameHelpers.TryCloseAddonByCallback("FashionCheck"))
        {
            log.Information("[FashionReport] Closing FashionCheck via callback");
        }

        lastFashionCheckCloseTime = now;
    }

    private void RetryOrFail(string reason)
    {
        if (currentAttempt >= MaxRetries)
        {
            log.Error($"[FashionReport] {reason}. Failed after {MaxRetries} attempts");
            SetState(FashionReportState.Failed);
            return;
        }

        currentAttempt++;
        log.Warning($"[FashionReport] {reason}. Retrying ({currentAttempt}/{MaxRetries})");

        if (clientState.TerritoryType == GoldSaucerTerritoryId)
        {
            SetState(FashionReportState.NavigatingToMaskedRose);
        }
        else
        {
            SetState(FashionReportState.TeleportingToSaucer);
        }
    }

    private void SetState(FashionReportState newState)
    {
        if (state == newState)
            return;

        log.Information($"[FashionReport] {state} -> {newState} (Retry {Math.Max(currentAttempt, 1)}/{MaxRetries}, Judging {completedJudgings}/{RequiredJudgings})");
        state = newState;
        stateEnteredAt = DateTime.UtcNow;
        lastDialogueAdvanceTime = DateTime.MinValue;
        lastFashionCheckCloseTime = DateTime.MinValue;

        if (newState == FashionReportState.Complete || newState == FashionReportState.Failed)
            lastJumpTime = DateTime.MinValue;
    }

    private void SendPeriodicJump(Vector3 targetPosition)
    {
        var now = DateTime.UtcNow;
        if ((now - lastJumpTime).TotalSeconds < JumpIntervalSeconds)
            return;

        var player = objectTable.LocalPlayer;
        if (player != null)
        {
            var distance = Vector3.Distance(player.Position, targetPosition);
            if (distance <= JumpStopDistance)
                return;
        }

        GameHelpers.SendJump();
        lastJumpTime = now;
    }

    public void Dispose()
    {
        Reset();
    }
}
