using System;
using Dalamud.Game.Command;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace VERMAXION.Services;

/// <summary>
/// Lord of Verminion queue service.
/// Uses AgentContentsFinder.OpenRegularDuty() for direct duty queuing.
/// LoV.lua: QueueDuty(576) for Normal mode, wait for Condition[14] (playingLordOfVerminion),
/// click ContentsFinderConfirm Commence, wait for LovmResult, callback LovmResult false -2 then true -1.
/// This pattern is reusable for Chocobo Racing and other Gold Saucer duties.
/// </summary>
public class VerminionService : IDisposable
{
    private readonly ICommandManager commandManager;
    private readonly ICondition condition;
    private readonly IPluginLog log;

    // LoV.lua: ModeIDs = { Normal = 576, Hard = 577, Extreme = 578 }
    // ContentFinderCondition row ID for Lord of Verminion (Normal)
    private const uint LovNormalCfcId = 576;

    private bool isActive = false;
    private VerminionState state = VerminionState.Idle;
    private DateTime stateEnteredAt = DateTime.MinValue;
    private int currentAttempt = 0;
    private const int MaxAttempts = 5;
    private bool joinAttempted = false;
    private bool dutySelected = false;
    private DateTime lastJoinRetry = DateTime.MinValue;
    private int dutySelectionAttempts = 0;
    private ushort returnHomeOriginTerritory;

    public enum VerminionState
    {
        Idle,
        ReturningHome,
        WaitingForHomeReady,
        OpeningDutyFinder,
        QueueingForDuty,
        WaitingForDutyPop,
        ClickingCommence,
        InDuty,
        WaitingForResult,
        DismissingResult,
        WaitingForPlayerAvailable,
        Complete,
        Failed,
    }

    public VerminionState State => state;
    public int CurrentAttempt => currentAttempt;
    public bool IsActive => state != VerminionState.Idle && state != VerminionState.Complete && state != VerminionState.Failed;
    public bool IsComplete => state == VerminionState.Complete;
    public bool IsFailed => state == VerminionState.Failed;
    public string StatusText => state == VerminionState.Idle ? "Idle" : $"{state} ({currentAttempt}/{MaxAttempts})";

    public VerminionService(ICommandManager commandManager, ICondition condition, IPluginLog log)
    {
        this.commandManager = commandManager;
        this.condition = condition;
        this.log = log;
    }

    public void Start()
    {
        isActive = true;
        currentAttempt = 0;
        SetState(VerminionState.ReturningHome);
        log.Information($"[Verminion] Preparing Verminion queue cycle (0/{MaxAttempts}) with /li home");
    }

    public void RunTask()
    {
        log.Information("[VERMAXION] Manual Verminion queue triggered");
        Start();
    }

    public void Reset()
    {
        // If we're being reset while active, mark as Complete to clear pending count
        if (isActive)
        {
            log.Information("[Verminion] Reset called while active, marking as Complete");
            SetState(VerminionState.Complete);
        }
        else
        {
            SetState(VerminionState.Idle);
        }
        isActive = false;
        state = VerminionState.Idle;
        stateEnteredAt = DateTime.MinValue;
        currentAttempt = 0;
        joinAttempted = false;
        dutySelected = false;
        lastJoinRetry = DateTime.MinValue;
        dutySelectionAttempts = 0;
        returnHomeOriginTerritory = 0;
    }

    public void Dispose() { }

    /// <summary>
    /// Open the Duty Finder to a specific duty using AgentContentsFinder.
    /// Reusable for LoV, Chocobo Racing, and other Gold Saucer duties.
    /// </summary>
    /// <param name="contentFinderConditionId">ContentFinderCondition row ID</param>
    public static unsafe bool OpenDutyFinder(uint contentFinderConditionId)
    {
        try
        {
            var agent = AgentContentsFinder.Instance();
            if (agent == null)
            {
                Plugin.Log.Error("[DutyQueue] AgentContentsFinder is null");
                return false;
            }
            agent->OpenRegularDuty(contentFinderConditionId);
            Plugin.Log.Information($"[DutyQueue] Opened duty finder for CFC ID {contentFinderConditionId}");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[DutyQueue] Failed to open duty finder: {ex.Message}");
            return false;
        }
    }

    public void Update()
    {
        if (state == VerminionState.Idle || state == VerminionState.Complete || state == VerminionState.Failed)
            return;

        var elapsed = (DateTime.UtcNow - stateEnteredAt).TotalSeconds;

        switch (state)
        {
            case VerminionState.ReturningHome:
                if (elapsed < 0.5)
                    return;

                returnHomeOriginTerritory = Plugin.ClientState.TerritoryType;
                log.Information("[Verminion] Returning home before opening ContentsFinder: /li home");
                commandManager.ProcessCommand("/li home");
                SetState(VerminionState.WaitingForHomeReady);
                return;

            case VerminionState.WaitingForHomeReady:
                if (elapsed < 3)
                    return;

                if (Plugin.ClientState.TerritoryType != returnHomeOriginTerritory && GameHelpers.IsPlayerAvailable())
                {
                    log.Information("[Verminion] /li home completed, opening duty finder");
                    SetState(VerminionState.OpeningDutyFinder);
                }
                else if (elapsed > 12 && GameHelpers.IsPlayerAvailable())
                {
                    log.Information("[Verminion] /li home settled without a territory change, opening duty finder");
                    SetState(VerminionState.OpeningDutyFinder);
                }
                else if (elapsed > 25)
                {
                    log.Warning("[Verminion] Timed out waiting for /li home to settle, opening duty finder anyway");
                    SetState(VerminionState.OpeningDutyFinder);
                }
                return;

            case VerminionState.OpeningDutyFinder:
                if (elapsed < 1) return;
                log.Information($"[Verminion] Starting LoV queue (attempt {currentAttempt + 1}/{MaxAttempts})");
                
                // Use AgentContentsFinder to open DF directly to LoV Normal
                if (OpenDutyFinder(LovNormalCfcId))
                {
                    joinAttempted = false;
                    dutySelected = false;
                    lastJoinRetry = DateTime.MinValue;
                    dutySelectionAttempts = 0;
                    SetState(VerminionState.QueueingForDuty);
                }
                else
                {
                    log.Error("[Verminion] Failed to open duty finder");
                    SetState(VerminionState.Failed);
                }
                break;

            case VerminionState.QueueingForDuty:
                // Wait for ContentsFinder addon to appear, select duty, then click Join
                // Need to wait 5-8 seconds for DF window to fully load
                if (elapsed < 6) return;
                
                if (GameHelpers.IsAddonVisible("ContentsFinder"))
                {
                    // Clear duty selection on first run (currentAttempt == 0)
                    if (currentAttempt == 0 && elapsed < 6.5)
                    {
                        log.Information("[Verminion] Clearing duty selection for first run");
                        GameHelpers.FireAddonCallback("ContentsFinder", true, 12, 1);
                        return; // Give it a moment to process
                    }
                    
                    if (!dutySelected)
                    {
                        // Skip duty selection for 2nd+ attempts (currentAttempt > 0)
                        if (currentAttempt > 0)
                        {
                            log.Information($"[Verminion] Skipping duty selection for attempt {currentAttempt + 1}, directly joining");
                            dutySelected = true;
                            return;
                        }
                        
                        // Try duty selection multiple times if needed (only for first attempt)
                        if (dutySelectionAttempts < 3)
                        {
                            log.Information($"[Verminion] ContentsFinder visible, selecting Player Battle (Non-RP) (attempt {dutySelectionAttempts + 1}/3)");
                            // User confirmed: callback 3, 5 selects the correct duty
                            // Format: /callback ContentsFinder true 3 5
                            GameHelpers.FireAddonCallback("ContentsFinder", true, 3, 5);
                            
                            // Also try Join after each selection attempt
                            // Format: /callback ContentsFinder True 12 0
                            log.Information($"[Verminion] Attempting Join after selection (attempt {dutySelectionAttempts + 1})");
                            GameHelpers.FireAddonCallback("ContentsFinder", true, 12, 0);
                            
                            dutySelected = true; // Mark as selected since we tried
                            dutySelectionAttempts++;
                            return; // Give it a moment to process
                        }
                        else
                        {
                            log.Warning("[Verminion] Failed to select duty after 3 attempts, retrying from start");
                            SetState(VerminionState.OpeningDutyFinder);
                            return;
                        }
                    }
                    else if (!joinAttempted && elapsed > 8)
                    {
                        log.Information("[Verminion] Duty selected, clicking Join");
                        // ContentsFinder Join button = callback true 12 0 (Register for duty)
                        GameHelpers.FireAddonCallback("ContentsFinder", true, 12, 0);
                        joinAttempted = true;
                    }
                    else if (elapsed > 15 && elapsed % 5 < 0.1) // Rate limit retries to every 5 seconds
                    {
                        // If still showing after 15s, try clicking Join again (rate limited)
                        log.Information($"[Verminion] ContentsFinder still visible after {elapsed:F1}s, retrying Join");
                        GameHelpers.FireAddonCallback("ContentsFinder", true, 12, 0);
                    }
                }
                if (joinAttempted &&
                    (condition[ConditionFlag.WaitingForDutyFinder] || condition[ConditionFlag.WaitingForDuty]))
                {
                    log.Information("[Verminion] Duty queue registered, waiting for duty pop");
                    SetState(VerminionState.WaitingForDutyPop);
                }
                else if (elapsed > 8 && !GameHelpers.IsAddonVisible("ContentsFinder"))
                {
                    log.Information("[Verminion] ContentsFinder closed, waiting for duty pop");
                    SetState(VerminionState.WaitingForDutyPop);
                }
                else if (elapsed > 30)
                {
                    log.Warning("[Verminion] Timeout waiting for queue registration, retrying");
                    SetState(VerminionState.OpeningDutyFinder);
                }
                break;

            case VerminionState.WaitingForDutyPop:
                // LoV.lua: while not Svc.Condition[14] do wait
                // Check for ContentsFinderConfirm addon (duty pop)
                if (GameHelpers.IsAddonVisible("ContentsFinderConfirm"))
                {
                    log.Information("[Verminion] Duty pop! Clicking Commence");
                    SetState(VerminionState.ClickingCommence);
                }
                // Also check if already in duty (Condition 14 = playingLordOfVerminion)
                else if (condition[ConditionFlag.BoundByDuty])
                {
                    log.Information("[Verminion] Already in duty");
                    SetState(VerminionState.InDuty);
                }
                else if (elapsed > 120) // 2 min timeout
                {
                    log.Warning("[Verminion] Duty queue timeout - retrying");
                    SetState(VerminionState.OpeningDutyFinder);
                }
                break;

            case VerminionState.ClickingCommence:
                // LoV.lua: /click ContentsFinderConfirm Commence
                if (elapsed < 1) return;
                if (GameHelpers.IsAddonVisible("ContentsFinderConfirm"))
                {
                    log.Information("[Verminion] Clicking Commence on ContentsFinderConfirm");
                    // Fire commence callback - typically callback index 8 = Commence button
                    GameHelpers.FireAddonCallback("ContentsFinderConfirm", true, 8);
                    SetState(VerminionState.InDuty);
                }
                else
                {
                    SetState(VerminionState.WaitingForDutyPop);
                }
                break;

            case VerminionState.InDuty:
                // LoV.lua: The match plays out, we do nothing (intentional lose)
                // Wait for LovmResult addon to appear
                if (GameHelpers.IsAddonVisible("LovmResult"))
                {
                    log.Information("[Verminion] LoV match ended, result screen visible");
                    SetState(VerminionState.WaitingForResult);
                }
                else if (!condition[ConditionFlag.BoundByDuty] && elapsed > 10)
                {
                    // Duty ended without result screen
                    log.Information("[Verminion] Duty ended");
                    SetState(VerminionState.WaitingForPlayerAvailable);
                }
                else if (elapsed > 600) // 10 min timeout
                {
                    log.Warning("[Verminion] LoV match timeout");
                    SetState(VerminionState.Failed);
                }
                break;

            case VerminionState.WaitingForResult:
                if (elapsed < 1) return;
                // LoV.lua: /callback LovmResult false -2  then  /callback LovmResult true -1
                if (GameHelpers.IsAddonVisible("LovmResult"))
                {
                    log.Information("[Verminion] Dismissing LoV result screen");
                    GameHelpers.FireAddonCallback("LovmResult", false, -2);
                    SetState(VerminionState.DismissingResult);
                }
                else
                {
                    SetState(VerminionState.WaitingForPlayerAvailable);
                }
                break;

            case VerminionState.DismissingResult:
                if (elapsed < 1) return;
                if (GameHelpers.IsAddonVisible("LovmResult"))
                {
                    GameHelpers.FireAddonCallback("LovmResult", true, -1);
                }
                SetState(VerminionState.WaitingForPlayerAvailable);
                break;

            case VerminionState.WaitingForPlayerAvailable:
                // LoV.lua: repeat until Player.Available and not Player.IsBusy
                if (elapsed < 2) return;
                if (GameHelpers.IsPlayerAvailable() || elapsed > 30)
                {
                    currentAttempt++;
                    log.Information($"[Verminion] Run {currentAttempt}/{MaxAttempts} complete");

                    if (currentAttempt >= MaxAttempts)
                    {
                        log.Information($"[Verminion] All {MaxAttempts} runs complete!");
                        SetState(VerminionState.Complete);
                    }
                    else
                    {
                        log.Information($"[Verminion] Starting run {currentAttempt + 1}/{MaxAttempts}");
                        SetState(VerminionState.OpeningDutyFinder);
                    }
                }
                break;
        }
    }

    private void SetState(VerminionState newState)
    {
        log.Information($"[Verminion] {state} -> {newState}");
        state = newState;
        stateEnteredAt = DateTime.UtcNow;
        isActive = newState != VerminionState.Idle &&
                   newState != VerminionState.Complete &&
                   newState != VerminionState.Failed;
    }
}
