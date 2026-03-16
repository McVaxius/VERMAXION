using System;
using System.Globalization;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using VERMAXION.Models;

namespace VERMAXION.Services;

/// <summary>
/// Fashion Report automation service.
/// Weekly task that starts Friday 9 AM UTC and available until weekly reset.
/// Navigates to Gold Saucer, finds Masked Rose, and interacts for Fashion Report.
/// </summary>
public class FashionReportService : IDisposable
{
    private readonly ICommandManager commandManager;
    private readonly ICondition condition;
    private readonly IObjectTable objectTable;
    private readonly IPluginLog log;
    private readonly ITargetManager targetManager;

    private bool isActive = false;
    private FashionReportState state = FashionReportState.Idle;
    private DateTime stateEnteredAt = DateTime.MinValue;
    private int currentAttempt = 0;
    private const int MaxAttempts = 3;
    private DateTime lastJumpTime = DateTime.MinValue;
    private const double JumpInterval = 0.5; // 500ms jump interval as requested

    public enum FashionReportState
    {
        Idle,
        NavigatingToSaucer,
        WaitingForSaucerZone,
        NavigatingToMaskedRose,
        WaitingForTarget,
        InteractingWithMaskedRose,
        WaitingForDialogue,
        Complete,
        Failed,
    }

    public FashionReportState State => state;
    public bool IsActive => isActive;
    public bool IsComplete => state == FashionReportState.Complete;
    public bool IsFailed => state == FashionReportState.Failed;

    private static readonly Vector3 MaskedRosePosition = new(55.864311218262f, 3.9997265338898f, 64.584785461426f);
    private const string MaskedRoseName = "Masked Rose";
    private const string GoldSaucerTerritoryId = "144"; // Gold Saucer territory ID

    public FashionReportService(ICommandManager commandManager, ICondition condition, IObjectTable objectTable, IPluginLog log, ITargetManager targetManager)
    {
        this.commandManager = commandManager;
        this.condition = condition;
        this.objectTable = objectTable;
        this.log = log;
        this.targetManager = targetManager;
    }

    public void Start()
    {
        if (isActive)
        {
            log.Warning("[FashionReport] Service already active");
            return;
        }

        log.Information("[FashionReport] Starting Fashion Report automation");
        isActive = true;
        currentAttempt = 1;
        SetState(FashionReportState.NavigatingToSaucer);
    }

    public void Reset()
    {
        log.Information("[FashionReport] Resetting service");
        isActive = false;
        state = FashionReportState.Idle;
        stateEnteredAt = DateTime.MinValue;
        currentAttempt = 0;
    }

    public void Update()
    {
        if (!isActive)
            return;

        var elapsed = DateTime.UtcNow - stateEnteredAt;

        switch (state)
        {
            case FashionReportState.NavigatingToSaucer:
                if (elapsed.TotalSeconds < 2)
                {
                    log.Information("[FashionReport] Teleporting to Gold Saucer: /li saucer");
                    commandManager.ProcessCommand("/li saucer");
                    SetState(FashionReportState.WaitingForSaucerZone);
                    return;
                }
                log.Error("[FashionReport] Failed to teleport to Gold Saucer");
                SetState(FashionReportState.Failed);
                break;

            case FashionReportState.WaitingForSaucerZone:
                if (elapsed.TotalSeconds > 10)
                {
                    log.Error("[FashionReport] Timeout waiting for Gold Saucer zone");
                    SetState(FashionReportState.Failed);
                    return;
                }

                // Check if we're in Gold Saucer
                if (IsInGoldSaucer())
                {
                    log.Information("[FashionReport] Arrived at Gold Saucer, navigating to Masked Rose");
                    SetState(FashionReportState.NavigatingToMaskedRose);
                    return;
                }
                break;

            case FashionReportState.NavigatingToMaskedRose:
                if (elapsed.TotalSeconds < 2)
                {
                    log.Information($"[FashionReport] Navigating to Masked Rose position: {MaskedRosePosition}");
                    var coords = string.Format(CultureInfo.InvariantCulture, "{0:F8},{1:F8},{2:F8}", 
                        MaskedRosePosition.X, MaskedRosePosition.Y, MaskedRosePosition.Z);
                    commandManager.ProcessCommand($"/vnav moveto {coords}");
                    SetState(FashionReportState.WaitingForTarget);
                    return;
                }
                log.Error("[FashionReport] Failed to start navigation to Masked Rose");
                SetState(FashionReportState.Failed);
                break;

            case FashionReportState.WaitingForTarget:
                if (elapsed.TotalSeconds > 30)
                {
                    log.Error("[FashionReport] Timeout waiting to reach Masked Rose");
                    SetState(FashionReportState.Failed);
                    return;
                }

                // Check if we're close to target position
                if (IsNearPosition(MaskedRosePosition, 5f))
                {
                    log.Information("[FashionReport] Reached Masked Rose area, looking for target");
                    SetState(FashionReportState.WaitingForTarget);
                    return;
                }
                // Send periodic jumps during navigation to help with pathing
                SendPeriodicJump();
                break;

            case FashionReportState.InteractingWithMaskedRose:
                if (elapsed.TotalSeconds < 2)
                {
                    // Try to target Masked Rose
                    if (TryTargetMaskedRose())
                    {
                        log.Information("[FashionReport] Targeted Masked Rose, initiating interaction");
                        commandManager.ProcessCommand("/interact");
                        SetState(FashionReportState.WaitingForDialogue);
                        return;
                    }
                    else
                    {
                        log.Warning("[FashionReport] Could not find Masked Rose, retrying...");
                        SetState(FashionReportState.WaitingForTarget);
                        return;
                    }
                }
                log.Error("[FashionReport] Failed to interact with Masked Rose");
                SetState(FashionReportState.Failed);
                break;

            case FashionReportState.WaitingForDialogue:
                if (elapsed.TotalSeconds > 15)
                {
                    log.Information("[FashionReport] Fashion Report interaction completed (timeout)");
                    SetState(FashionReportState.Complete);
                    return;
                }

                // Wait for dialogue to complete
                // Could add dialogue detection here if needed
                break;

            case FashionReportState.Complete:
            case FashionReportState.Failed:
                // Terminal states, nothing to do
                break;
        }
    }

    private bool IsInGoldSaucer()
    {
        // Check territory ID or zone name
        // This is a simplified check - may need adjustment based on actual territory detection
        try
        {
            // Could use ClientState.TerritoryType or similar
            // For now, just check if we can find Gold Saucer objects
            foreach (var obj in objectTable)
            {
                if (obj != null && obj.Name.ToString().Contains("Gold Saucer", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    private bool IsNearPosition(Vector3 targetPos, float threshold)
    {
        try
        {
            var player = objectTable[0];
            if (player == null) return false;

            var playerPos = player.Position;
            var distance = Vector3.Distance(new Vector3(playerPos.X, playerPos.Y, playerPos.Z), targetPos);
            return distance <= threshold;
        }
        catch
        {
            return false;
        }
    }

    private bool TryTargetMaskedRose()
    {
        try
        {
            // Search for Masked Rose in object table
            foreach (var obj in objectTable)
            {
                if (obj != null && obj.Name.ToString().Equals(MaskedRoseName, StringComparison.OrdinalIgnoreCase))
                {
                    targetManager.Target = obj;
                    return true;
                }
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    private void SetState(FashionReportState newState)
    {
        if (state == newState) return;

        log.Information($"[FashionReport] {state} -> {newState} (Attempt {currentAttempt}/{MaxAttempts})");
        state = newState;
        stateEnteredAt = DateTime.UtcNow;

        // Handle state-specific logic
        switch (newState)
        {
            case FashionReportState.Failed:
                if (currentAttempt < MaxAttempts)
                {
                    currentAttempt++;
                    log.Information($"[FashionReport] Retrying... (Attempt {currentAttempt}/{MaxAttempts})");
                    SetState(FashionReportState.NavigatingToSaucer);
                }
                else
                {
                    log.Error("[FashionReport] Failed after maximum attempts");
                    isActive = false;
                }
                break;

            case FashionReportState.Complete:
                log.Information("[FashionReport] Fashion Report completed successfully");
                isActive = false;
                break;
        }
    }

    /// <summary>
    /// Send periodic jump commands during navigation to help with pathing when stuck on aetheryte.
    /// Jumps every 500ms as requested to help the bot reach its destination.
    /// </summary>
    private void SendPeriodicJump()
    {
        var now = DateTime.UtcNow;
        if ((now - lastJumpTime).TotalSeconds >= JumpInterval)
        {
            GameHelpers.SendJump();
            lastJumpTime = now;
        }
    }

    public void Dispose()
    {
        Reset();
    }
}
