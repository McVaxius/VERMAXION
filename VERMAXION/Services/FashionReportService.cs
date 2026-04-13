using System;
using System.Globalization;
using System.Numerics;
using Dalamud.Plugin.Services;

namespace VERMAXION.Services;

/// <summary>
/// Fashion Report automation service.
/// Travels to the Gold Saucer, interacts with Masked Rose, then closes the dialogue so the engine can continue.
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

    private const int MaxAttempts = 3;
    private const ushort GoldSaucerTerritoryId = 144;
    private const string MaskedRoseName = "Masked Rose";
    private static readonly Vector3 MaskedRosePosition = new(55.864311218262f, 3.9997265338898f, 64.584785461426f);

    private DateTime lastJumpTime = DateTime.MinValue;
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
        WaitingForDialogue,
        ClosingDialogue,
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
        lastJumpTime = DateTime.MinValue;
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
                    log.Information("[FashionReport] Interacted with Masked Rose");
                    SetState(FashionReportState.WaitingForDialogue);
                }
                else if (elapsed > 12)
                {
                    RetryOrFail("Timed out targeting Masked Rose");
                }
                break;

            case FashionReportState.WaitingForDialogue:
                if (elapsed < 1.5)
                    return;

                if (IsFashionReportUiVisible())
                {
                    log.Information("[FashionReport] Dialogue visible, closing it so the engine can continue");
                }
                else
                {
                    log.Information("[FashionReport] No visible dialogue detected after interaction, closing cleanly");
                }

                SetState(FashionReportState.ClosingDialogue);
                break;

            case FashionReportState.ClosingDialogue:
                if (elapsed < 0.5)
                    return;

                GameHelpers.SendEnd();
                GameHelpers.CloseCurrentAddon();
                SetState(FashionReportState.Complete);
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
            || GameHelpers.IsAddonVisible("JournalAccept")
            || GameHelpers.IsAddonVisible("Request");
    }

    private void RetryOrFail(string reason)
    {
        if (currentAttempt >= MaxAttempts)
        {
            log.Error($"[FashionReport] {reason}. Failed after {MaxAttempts} attempts");
            SetState(FashionReportState.Failed);
            return;
        }

        currentAttempt++;
        log.Warning($"[FashionReport] {reason}. Retrying ({currentAttempt}/{MaxAttempts})");

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

        log.Information($"[FashionReport] {state} -> {newState} (Attempt {Math.Max(currentAttempt, 1)}/{MaxAttempts})");
        state = newState;
        stateEnteredAt = DateTime.UtcNow;

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
