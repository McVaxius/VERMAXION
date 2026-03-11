using System;
using Dalamud.Game.Command;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using Dalamud.Plugin.Services;

namespace VERMAXION.Services;

/// <summary>
/// Lord of Verminion queue service.
/// Uses AutoDuty IPC for reliable duty queuing.
/// LoV.lua: QueueDuty(576) for Normal mode, wait for Condition[14] (playingLordOfVerminion),
/// click ContentsFinderConfirm Commence, wait for LovmResult, callback LovmResult false -2 then true -1.
/// </summary>
public class VerminionService : IDisposable
{
    private readonly ICommandManager commandManager;
    private readonly ICondition condition;
    private readonly IPluginLog log;
    private readonly IDalamudPluginInterface pluginInterface;

    // LoV.lua: ModeIDs = { Normal = 576, Hard = 577, Extreme = 578 }
    private const int LovNormalDutyId = 576;
    private const uint LovNormalTerritoryId = 576; // Territory ID matches duty ID for LoV
    // LoV.lua: CharacterCondition.playingLordOfVerminion = 14
    // ConditionFlag index 14 = Condition[14] in SND

    // AutoDuty IPC channels
    private readonly ICallGateSubscriber<uint, bool> _contentHasPath;
    private readonly ICallGateSubscriber<string, string, object> _setConfig;
    private readonly ICallGateSubscriber<uint, int, bool, object> _run;
    private readonly ICallGateSubscriber<bool> _isStopped;
    private readonly ICallGateSubscriber<object> _stop;

    private int currentAttempt = 0;
    private const int MaxAttempts = 5;
    private DateTime stateEnteredAt = DateTime.MinValue;
    private VerminionState state = VerminionState.Idle;
    private bool autoDutyAvailable = false;

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

    public VerminionService(ICommandManager commandManager, ICondition condition, IPluginLog log, IDalamudPluginInterface pluginInterface)
    {
        this.commandManager = commandManager;
        this.condition = condition;
        this.log = log;
        this.pluginInterface = pluginInterface;

        // Initialize AutoDuty IPC channels
        try
        {
            _contentHasPath = pluginInterface.GetIpcSubscriber<uint, bool>("AutoDuty.ContentHasPath");
            _setConfig = pluginInterface.GetIpcSubscriber<string, string, object>("AutoDuty.SetConfig");
            _run = pluginInterface.GetIpcSubscriber<uint, int, bool, object>("AutoDuty.Run");
            _isStopped = pluginInterface.GetIpcSubscriber<bool>("AutoDuty.IsStopped");
            _stop = pluginInterface.GetIpcSubscriber<object>("AutoDuty.Stop");

            // Test if AutoDuty is available
            autoDutyAvailable = TestAutoDutyAvailability();
            if (autoDutyAvailable)
            {
                log.Information("[Verminion] AutoDuty IPC available - will use for duty queuing");
            }
            else
            {
                log.Warning("[Verminion] AutoDuty IPC not available - falling back to manual queue");
            }
        }
        catch (Exception ex)
        {
            log.Warning($"[Verminion] Failed to initialize AutoDuty IPC: {ex.Message}");
            autoDutyAvailable = false;
        }
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

    private bool TestAutoDutyAvailability()
    {
        try
        {
            // Try to call ContentHasPath for LoV territory
            var hasPath = _contentHasPath.InvokeFunc(LovNormalTerritoryId);
            log.Debug($"[Verminion] AutoDuty ContentHasPath for LoV: {hasPath}");
            return true;
        }
        catch (IpcError ex)
        {
            log.Debug($"[Verminion] AutoDuty IPC test failed: {ex.Message}");
            return false;
        }
    }

    private bool QueueWithAutoDuty()
    {
        try
        {
            log.Information("[Verminion] Using AutoDuty IPC to queue for LoV Normal");
            
            // Configure AutoDuty for LoV (normal synced mode - unsync not needed for LoV)
            _setConfig.InvokeAction("Unsynced", "false");
            _setConfig.InvokeAction("dutyModeEnum", "Regular");
            
            // Start the duty (territoryId, count, bareMode)
            _run.InvokeAction(LovNormalTerritoryId, 1, true);
            
            log.Information("[Verminion] AutoDuty queue command sent successfully");
            return true;
        }
        catch (IpcError ex)
        {
            log.Error($"[Verminion] AutoDuty queue failed: {ex.Message}");
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
                log.Information($"[Verminion] Starting LoV queue (attempt {currentAttempt + 1}/{MaxAttempts})");
                
                if (autoDutyAvailable)
                {
                    // Use AutoDuty IPC for reliable queuing
                    if (QueueWithAutoDuty())
                    {
                        SetState(VerminionState.WaitingForDutyPop);
                    }
                    else
                    {
                        log.Warning("[Verminion] AutoDuty queue failed, retrying");
                        SetState(VerminionState.OpeningDutyFinder);
                    }
                }
                else
                {
                    // Fallback to manual ContentsFinder method
                    log.Information("[Verminion] AutoDuty not available, using manual queue");
                    commandManager.ProcessCommand("/dutyfinder");
                    SetState(VerminionState.QueueingForDuty);
                }
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
