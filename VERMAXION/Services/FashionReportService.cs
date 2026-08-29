using System;
using System.Numerics;
using Dalamud.Plugin.Services;
using VERMAXION.IPC;

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
    private readonly VNavmeshIPC vnavmesh;

    private FashionReportState state = FashionReportState.Idle;
    private DateTime stateEnteredAt = DateTime.MinValue;
    private int currentAttempt;
    private int completedJudgings;

    private const int MaxRetries = 3;
    private const int RequiredJudgings = 4;
    private const ushort GoldSaucerTerritoryId = 144;
    private const string MaskedRoseName = "Masked Rose";
    private static readonly Vector3 MaskedRosePosition = new(55.864311218262f, 3.9997265338898f, 64.584785461426f);
    private const double AetheryteSettleDelaySeconds = 8.0;
    private const double NavigationRetryIntervalSeconds = 5.0;
    private const float ArrivalDistance = 3.0f;
    private const double ArrivalTimeoutSeconds = 60.0;
    private const double CloseApproachTimeoutSeconds = 20.0;
    private const double PostNavigationSettleDelaySeconds = 0.5;
    private const double TargetRetryIntervalSeconds = 2.0;
    private const double DialogueAdvanceIntervalSeconds = 1.0;
    private const double FashionCheckCloseIntervalSeconds = 1.0;
    private const double FashionCheckResultTimeoutSeconds = 75.0;
    private const double PostJudgingSettleTimeoutSeconds = 30.0;

    private DateTime lastNavigationAttempt = DateTime.MinValue;
    private DateTime lastTargetAttempt = DateTime.MinValue;
    private DateTime lastDialogueAdvanceTime = DateTime.MinValue;
    private DateTime lastFashionCheckCloseTime = DateTime.MinValue;
    private int targetAttempts;

    public enum FashionReportState
    {
        Idle,
        TeleportingToSaucer,
        WaitingForSaucerZone,
        NavigatingToMaskedRose,
        WaitingForArrival,
        ClosingToMaskedRose,
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

    public FashionReportService(
        ICommandManager commandManager,
        IClientState clientState,
        IObjectTable objectTable,
        IPluginLog log,
        VNavmeshIPC vnavmesh)
    {
        this.commandManager = commandManager;
        this.clientState = clientState;
        this.objectTable = objectTable;
        this.log = log;
        this.vnavmesh = vnavmesh;
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
        ResetNavigationState();
        SetState(FashionReportState.TeleportingToSaucer);
    }

    public void Reset()
    {
        state = FashionReportState.Idle;
        stateEnteredAt = DateTime.MinValue;
        currentAttempt = 0;
        completedJudgings = 0;
        lastNavigationAttempt = DateTime.MinValue;
        lastTargetAttempt = DateTime.MinValue;
        lastDialogueAdvanceTime = DateTime.MinValue;
        lastFashionCheckCloseTime = DateTime.MinValue;
        targetAttempts = 0;
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
                if (elapsed > AetheryteSettleDelaySeconds &&
                    clientState.TerritoryType == GoldSaucerTerritoryId &&
                    GameHelpers.IsPlayerAvailable())
                {
                    log.Information("[FashionReport] Gold Saucer travel settled, starting navigation to Masked Rose");
                    IssueNavigation(MaskedRosePosition, MaskedRoseName);
                    SetState(FashionReportState.WaitingForArrival);
                }
                else if (elapsed > 30)
                {
                    RetryOrFail("Timed out waiting for player availability before Masked Rose navigation");
                }
                break;

            case FashionReportState.WaitingForArrival:
                if (TryTransitionWaypointToInteraction())
                {
                    break;
                }

                if (TryTransitionNpcRangeToInteraction())
                {
                    break;
                }

                if (elapsed > ArrivalTimeoutSeconds)
                {
                    RetryOrFail("Timed out navigating to Masked Rose");
                }
                else if (RetryNavigationIfNeeded(MaskedRosePosition, MaskedRoseName))
                {
                    // Shared navigation observes progress here and suppresses healthy duplicate requests.
                }
                break;

            case FashionReportState.ClosingToMaskedRose:
                if (TryTransitionNpcRangeToInteraction())
                {
                    break;
                }

                if (elapsed > CloseApproachTimeoutSeconds)
                {
                    RetryOrFail("Timed out closing the last few yalms to Masked Rose");
                }
                else if (RetryCloseApproachIfNeeded())
                {
                    // Dedicated post-stop movement phase. No interaction happens here.
                }
                break;

            case FashionReportState.InteractingWithMaskedRose:
                if (elapsed < PostNavigationSettleDelaySeconds)
                    return;

                if (TryBeginCloseApproachIfOutOfRange())
                {
                    break;
                }

                if (TryTargetAndInteractMaskedRose())
                {
                    log.Information($"[FashionReport] Interacted with Masked Rose for judging {completedJudgings + 1}/{RequiredJudgings}");
                    SetState(FashionReportState.WaitingForDialogueOption);
                }
                else if (elapsed > 15)
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
                    log.Error("[FashionReport] Post-judging settle timed out before return-to-idle was verified");
                    SetState(FashionReportState.Failed);
                }
                break;
        }
    }

    private void ResetNavigationState()
    {
        lastNavigationAttempt = DateTime.MinValue;
        lastTargetAttempt = DateTime.MinValue;
        targetAttempts = 0;
    }

    private bool TryTransitionWaypointToInteraction()
    {
        if (!TryGetWaypointDistance(out var distance) || distance > ArrivalDistance)
            return false;

        StopNavigation();
        log.Information($"[FashionReport] Reached Masked Rose waypoint ({distance:F1}y <= {ArrivalDistance:F1}y), stopping pathfinding before interaction");
        SetState(FashionReportState.InteractingWithMaskedRose);
        return true;
    }

    private bool TryTransitionNpcRangeToInteraction()
    {
        if (!TryGetMaskedRoseInteractionData(out _, out var distance, out var maxDistance) ||
            distance > maxDistance)
        {
            return false;
        }

        StopNavigation();
        log.Information($"[FashionReport] Masked Rose is within interaction range ({distance:F1}y <= {maxDistance:F1}y), stopping pathfinding before interaction");
        SetState(FashionReportState.InteractingWithMaskedRose);
        return true;
    }

    private bool TryBeginCloseApproachIfOutOfRange()
    {
        if (!TryGetMaskedRoseInteractionData(out _, out var distance, out var maxDistance) ||
            distance <= maxDistance)
        {
            return false;
        }

        log.Information($"[FashionReport] Masked Rose is still outside interaction range after stopping pathfinding ({distance:F1}y > {maxDistance:F1}y), entering close-in movement");
        SetState(FashionReportState.ClosingToMaskedRose);
        return true;
    }

    private bool RetryCloseApproachIfNeeded()
    {
        if (!TryGetMaskedRoseInteractionData(out var npcPosition, out var distance, out var maxDistance))
        {
            return false;
        }

        var destination = TryBuildApproachPosition(npcPosition, maxDistance, out var approachPosition)
            ? approachPosition
            : npcPosition;

        return RetryNavigationIfNeeded(
            destination,
            $"{MaskedRoseName} ({distance:F1}y > {maxDistance:F1}y, close approach after stop)");
    }

    private bool TryTargetAndInteractMaskedRose()
    {
        if (!GameHelpers.IsPlayerAvailable())
            return false;

        var now = DateTime.UtcNow;
        if ((now - lastTargetAttempt).TotalSeconds < TargetRetryIntervalSeconds)
            return false;

        lastTargetAttempt = now;
        targetAttempts++;
        log.Information($"[FashionReport] Masked Rose interaction attempt {targetAttempts}");

        var player = objectTable.LocalPlayer;
        var target = GameHelpers.FindObjectByName(MaskedRoseName);
        if (player == null || target == null)
            return false;

        var distance = Vector3.Distance(player.Position, target.Position);
        var maxDistance = GameHelpers.GetValidInteractionDistance(target);
        if (distance > maxDistance)
        {
            log.Information($"[FashionReport] Masked Rose interaction attempt {targetAttempts} skipped; still out of range ({distance:F1}y > {maxDistance:F1}y)");
            return false;
        }

        log.Information($"[FashionReport] Targeting and interacting with Masked Rose ({distance:F1}y <= {maxDistance:F1}y)");
        return GameHelpers.TargetAndInteract(MaskedRoseName);
    }

    private bool TryGetWaypointDistance(out float distance)
    {
        distance = float.MaxValue;

        if (!GameHelpers.IsPlayerAvailable())
            return false;

        var player = objectTable.LocalPlayer;
        if (player == null)
            return false;

        distance = Vector3.Distance(player.Position, MaskedRosePosition);
        return true;
    }

    private bool TryGetMaskedRoseInteractionData(out Vector3 npcPosition, out float distance, out float maxDistance)
    {
        npcPosition = Vector3.Zero;
        distance = float.MaxValue;
        maxDistance = 0f;

        if (!GameHelpers.IsPlayerAvailable())
            return false;

        var player = objectTable.LocalPlayer;
        var target = GameHelpers.FindObjectByName(MaskedRoseName);
        if (player == null || target == null)
            return false;

        npcPosition = target.Position;
        distance = Vector3.Distance(player.Position, target.Position);
        maxDistance = GameHelpers.GetValidInteractionDistance(target);
        return true;
    }

    private void StopNavigation()
    {
        vnavmesh.Stop();
    }

    private void IssueNavigation(Vector3 destination, string destinationLabel)
    {
        lastNavigationAttempt = DateTime.UtcNow;
        if (vnavmesh.PathfindAndMoveTo(destination))
            log.Debug($"[FashionReport] Issued vnav movement toward {destinationLabel}");
    }

    private bool RetryNavigationIfNeeded(Vector3 destination, string destinationLabel)
    {
        var now = DateTime.UtcNow;
        if ((now - lastNavigationAttempt).TotalSeconds < NavigationRetryIntervalSeconds)
            return false;

        IssueNavigation(destination, destinationLabel);
        return true;
    }

    private bool TryBuildApproachPosition(Vector3 npcPosition, float maxDistance, out Vector3 position)
    {
        position = default;

        var player = objectTable.LocalPlayer;
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

        if (newState == FashionReportState.NavigatingToMaskedRose)
        {
            lastNavigationAttempt = DateTime.MinValue;
            lastTargetAttempt = DateTime.MinValue;
        }
        else if (newState == FashionReportState.ClosingToMaskedRose)
        {
            lastNavigationAttempt = DateTime.MinValue;
        }
        else if (newState == FashionReportState.InteractingWithMaskedRose)
        {
            lastNavigationAttempt = DateTime.MinValue;
            lastTargetAttempt = DateTime.MinValue;
            targetAttempts = 0;
        }
    }

    public void Dispose()
    {
        Reset();
    }
}
