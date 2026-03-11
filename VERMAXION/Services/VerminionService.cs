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

    private int currentAttempt = 0;
    private const int MaxAttempts = 5;
    private DateTime stateEnteredAt = DateTime.MinValue;
    private VerminionState state = VerminionState.Idle;
    private bool joinAttempted = false;

    public enum VerminionState
    {
        Idle,
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
        currentAttempt = 0;
        SetState(VerminionState.OpeningDutyFinder);
        log.Information($"[Verminion] Starting Verminion queue cycle (0/{MaxAttempts})");
    }

    public void RunTask()
    {
        log.Information("[VERMAXION] Manual Verminion queue triggered");
        Start();
    }

    public void Reset()
    {
        currentAttempt = 0;
        SetState(VerminionState.Idle);
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
            case VerminionState.OpeningDutyFinder:
                if (elapsed < 1) return;
                log.Information($"[Verminion] Starting LoV queue (attempt {currentAttempt + 1}/{MaxAttempts})");
                
                // Use AgentContentsFinder to open DF directly to LoV Normal
                if (OpenDutyFinder(LovNormalCfcId))
                {
                    joinAttempted = false;
                    SetState(VerminionState.QueueingForDuty);
                }
                else
                {
                    log.Error("[Verminion] Failed to open duty finder");
                    SetState(VerminionState.Failed);
                }
                break;

            case VerminionState.QueueingForDuty:
                // Wait for ContentsFinder addon to appear, then click Join
                if (elapsed < 2) return;
                
                if (GameHelpers.IsAddonVisible("ContentsFinder"))
                {
                    if (!joinAttempted)
                    {
                        log.Information("[Verminion] ContentsFinder visible, clicking Join");
                        // ContentsFinder Join button = callback true 12 (Register for duty)
                        GameHelpers.FireAddonCallback("ContentsFinder", true, 12);
                        joinAttempted = true;
                    }
                    else if (elapsed > 5)
                    {
                        // If still showing after 5s, try clicking again
                        log.Information("[Verminion] ContentsFinder still visible, retrying Join");
                        GameHelpers.FireAddonCallback("ContentsFinder", true, 12);
                    }
                }
                
                // Check if we got queued (ContentsFinder should close)
                if (elapsed > 3 && !GameHelpers.IsAddonVisible("ContentsFinder"))
                {
                    log.Information("[Verminion] ContentsFinder closed, waiting for duty pop");
                    SetState(VerminionState.WaitingForDutyPop);
                }
                else if (elapsed > 15)
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
    }
}
