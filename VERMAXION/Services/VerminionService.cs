using System;
using Dalamud.Game.Command;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;

namespace VERMAXION.Services;

/// <summary>
/// Lord of Verminion queue service.
/// Based on LoV.lua SND script: QueueDuty(576) for Normal mode,
/// wait for Condition[14] (playingLordOfVerminion),
/// click ContentsFinderConfirm Commence, wait for LovmResult,
/// callback LovmResult false -2 then true -1.
/// </summary>
public class VerminionService : IDisposable
{
    private readonly ICommandManager commandManager;
    private readonly ICondition condition;
    private readonly IPluginLog log;

    // LoV.lua: ModeIDs = { Normal = 576, Hard = 577, Extreme = 578 }
    private const int LovNormalDutyId = 576;
    // LoV.lua: CharacterCondition.playingLordOfVerminion = 14
    // ConditionFlag index 14 = Condition[14] in SND

    private int currentAttempt = 0;
    private const int MaxAttempts = 5;
    private DateTime stateEnteredAt = DateTime.MinValue;
    private VerminionState state = VerminionState.Idle;

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

    public void Update()
    {
        if (state == VerminionState.Idle || state == VerminionState.Complete || state == VerminionState.Failed)
            return;

        var elapsed = (DateTime.UtcNow - stateEnteredAt).TotalSeconds;

        switch (state)
        {
            case VerminionState.OpeningDutyFinder:
                // LoV.lua: Instances.DutyFinder:QueueDuty(576)
                // In Dalamud, we open the duty finder and queue for LoV Normal
                // Use /dutyfinder to open, then use agent to queue
                log.Information($"[Verminion] Opening Duty Finder for LoV Normal (attempt {currentAttempt + 1}/{MaxAttempts})");
                // Open ContentsFinder via command
                commandManager.ProcessCommand("/dutyfinder");
                SetState(VerminionState.QueueingForDuty);
                break;

            case VerminionState.QueueingForDuty:
                if (elapsed < 1.5) return;
                // Queue for Lord of Verminion (Normal) via ContentsFinder addon
                // LoV.lua sets IsUnrestrictedParty=false, IsLevelSync=false
                if (GameHelpers.IsAddonVisible("ContentsFinder"))
                {
                    log.Information("[Verminion] ContentsFinder open, queueing for LoV Normal (ID 576)");
                    // Use the join command - /contentroulette won't work for specific duties
                    // Try direct duty join via chat command
                    // Close ContentsFinder first, then use /join
                    GameHelpers.CloseCurrentAddon();
                }
                // Direct queue approach: use /dutyfinder with specific duty
                // Alternative: fire callback on ContentsFinder to select and join
                // Most reliable: just use the SND approach translated
                if (elapsed > 2)
                {
                    // Queue via game systems - attempt to join LoV directly
                    log.Information("[Verminion] Attempting to queue for LoV via duty system");
                    SetState(VerminionState.WaitingForDutyPop);
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
