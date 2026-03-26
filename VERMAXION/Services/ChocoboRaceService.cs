using System;
using Dalamud.Game.Command;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using VERMAXION.Models;

namespace VERMAXION.Services;

/// <summary>
/// Chocobo Racing queue service.
/// Uses AgentContentsFinder.OpenRegularDuty() for direct duty queuing.
/// Based on ChocoboRacing.lua: QueueRoulette(22) for Sagolii Road,
/// click ContentsFinderConfirm Commence, wait for race, wait for RaceChocoboResult.
/// This mirrors the VerminionService structure for consistency.
/// </summary>
public class ChocoboRaceService : IDisposable
{
    private readonly ICommandManager commandManager;
    private readonly ICondition condition;
    private readonly IPluginLog log;
    private readonly ConfigManager configManager;

    private const byte ChocoboRacingRouletteId = 22;

    private bool isActive = false;
    private ChocoboState state = ChocoboState.Idle;
    private DateTime stateEnteredAt = DateTime.MinValue;
    private int currentAttempt = 0;
    private int maxAttempts = 5;
    private bool joinAttempted = false;
    private DateTime lastJoinRetry = DateTime.MinValue;
    private ICallGateSubscriber<int, object>? chocoholicQueueSubscriber;
    private bool chocoholicLookupAttempted;

    public enum ChocoboState
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

    public ChocoboState State => state;
    public int CurrentAttempt => currentAttempt;
    public bool IsActive => state != ChocoboState.Idle && state != ChocoboState.Complete && state != ChocoboState.Failed;
    public bool IsComplete => state == ChocoboState.Complete;
    public bool IsFailed => state == ChocoboState.Failed;
    public string StatusText => state == ChocoboState.Idle ? "Idle" : $"{state} ({currentAttempt}/{maxAttempts})";

    public ChocoboRaceService(ICommandManager commandManager, IPluginLog log, ConfigManager configManager)
    {
        this.commandManager = commandManager;
        this.log = log;
        this.condition = Plugin.Condition;
        this.configManager = configManager;
    }

    public void Start()
    {
        // Get configured number of races from active character config
        var activeConfig = configManager?.GetActiveConfig();
        maxAttempts = activeConfig?.ChocoboRacesPerDay ?? 5;

        if (TryQueueWithChocoholic(maxAttempts))
        {
            log.Information($"[ChocoboRace] Queued {maxAttempts} races through Chocoholic IPC");
            SetState(ChocoboState.Complete);
            return;
        }
        
        currentAttempt = 0;
        SetState(ChocoboState.OpeningDutyFinder);
        log.Information($"[ChocoboRace] Starting Chocobo Racing cycle (0/{maxAttempts})");
    }

    public void RunTask()
    {
        log.Information("[VERMAXION] Manual Chocobo Racing triggered");
        Start();
    }

    public void Reset()
    {
        // If we're being reset while active, mark as Complete to clear pending count
        if (isActive)
        {
            log.Information("[ChocoboRace] Reset called while active, marking as Complete");
            SetState(ChocoboState.Complete);
        }
        else
        {
            SetState(ChocoboState.Idle);
        }
        isActive = false;
        state = ChocoboState.Idle;
        stateEnteredAt = DateTime.MinValue;
        currentAttempt = 0;
        joinAttempted = false;
        lastJoinRetry = DateTime.MinValue;
    }

    public void Dispose() { }

    private bool TryQueueWithChocoholic(int raceCount)
    {
        try
        {
            if (!chocoholicLookupAttempted)
            {
                chocoholicLookupAttempted = true;
                chocoholicQueueSubscriber = Plugin.PluginInterface.GetIpcSubscriber<int, object>("Chocoholic.QueueRace");
            }

            if (chocoholicQueueSubscriber == null)
            {
                log.Information("[ChocoboRace] Chocoholic IPC not available, falling back to manual queueing");
                return false;
            }

            chocoholicQueueSubscriber.InvokeAction(raceCount);
            return true;
        }
        catch (Exception ex)
        {
            log.Warning($"[ChocoboRace] Chocoholic IPC unavailable or failed ({ex.Message}), falling back to manual queueing");
            return false;
        }
    }

    /// <summary>
    /// Open the Duty Finder to a specific roulette using AgentContentsFinder.
    /// This avoids fragile menu indexing when Verminion or other Gold Saucer duties are locked.
    /// </summary>
    /// <param name="rouletteId">Content roulette row ID</param>
    public static unsafe bool OpenDutyRoulette(byte rouletteId)
    {
        try
        {
            var agent = AgentContentsFinder.Instance();
            if (agent == null)
            {
                Plugin.Log.Error("[DutyQueue] AgentContentsFinder is null");
                return false;
            }
            agent->OpenRouletteDuty(rouletteId);
            Plugin.Log.Information($"[DutyQueue] Opened duty finder for roulette ID {rouletteId}");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[DutyQueue] Failed to open duty roulette: {ex.Message}");
            return false;
        }
    }

    public void Update()
    {
        if (state == ChocoboState.Idle || state == ChocoboState.Complete || state == ChocoboState.Failed)
            return;

        var elapsed = (DateTime.UtcNow - stateEnteredAt).TotalSeconds;

        switch (state)
        {
            case ChocoboState.OpeningDutyFinder:
                if (elapsed < 1) return;
                log.Information($"[ChocoboRace] Starting Chocobo Racing queue (attempt {currentAttempt + 1}/{maxAttempts})");
                
                if (OpenDutyRoulette(ChocoboRacingRouletteId))
                {
                    joinAttempted = false;
                    lastJoinRetry = DateTime.MinValue;
                    SetState(ChocoboState.QueueingForDuty);
                }
                else
                {
                    log.Error("[ChocoboRace] Failed to open duty finder");
                    SetState(ChocoboState.Failed);
                }
                break;

            case ChocoboState.QueueingForDuty:
                // Wait for ContentsFinder addon to appear, then click Join.
                // OpenRouletteDuty already selects the correct Chocobo Racing entry.
                if (elapsed < 6) return;
                
                if (GameHelpers.IsAddonVisible("ContentsFinder"))
                {
                    if (!joinAttempted && elapsed > 8)
                    {
                        log.Information("[ChocoboRace] Roulette selected, clicking Join");
                        GameHelpers.FireAddonCallback("ContentsFinder", true, 12, 0);
                        joinAttempted = true;
                        lastJoinRetry = DateTime.UtcNow;
                    }
                    else if (joinAttempted && (DateTime.UtcNow - lastJoinRetry).TotalSeconds >= 5)
                    {
                        log.Information($"[ChocoboRace] ContentsFinder still visible after {elapsed:F1}s, retrying Join");
                        GameHelpers.FireAddonCallback("ContentsFinder", true, 12, 0);
                        lastJoinRetry = DateTime.UtcNow;
                    }
                }
                if (elapsed > 8 && !GameHelpers.IsAddonVisible("ContentsFinder"))
                {
                    log.Information("[ChocoboRace] ContentsFinder closed, waiting for duty pop");
                    SetState(ChocoboState.WaitingForDutyPop);
                }
                else if (elapsed > 30)
                {
                    log.Warning("[ChocoboRace] Timeout waiting for queue registration, retrying");
                    SetState(ChocoboState.OpeningDutyFinder);
                }
                break;

            case ChocoboState.WaitingForDutyPop:
                // Check for ContentsFinderConfirm addon (duty pop)
                if (GameHelpers.IsAddonVisible("ContentsFinderConfirm"))
                {
                    log.Information("[ChocoboRace] Race pop! Clicking Commence");
                    SetState(ChocoboState.ClickingCommence);
                }
                // Also check if already in duty
                else if (condition[ConditionFlag.BoundByDuty])
                {
                    log.Information("[ChocoboRace] Already in duty");
                    SetState(ChocoboState.InDuty);
                }
                else if (elapsed > 120) // 2 min timeout
                {
                    log.Warning("[ChocoboRace] Duty queue timeout - retrying");
                    SetState(ChocoboState.OpeningDutyFinder);
                }
                break;

            case ChocoboState.ClickingCommence:
                if (elapsed < 1) return;
                if (GameHelpers.IsAddonVisible("ContentsFinderConfirm"))
                {
                    log.Information("[ChocoboRace] Clicking Commence on ContentsFinderConfirm");
                    // Fire commence callback - typically callback index 8 = Commence button
                    GameHelpers.FireAddonCallback("ContentsFinderConfirm", true, 8);
                    SetState(ChocoboState.InDuty);
                }
                else
                {
                    SetState(ChocoboState.WaitingForDutyPop);
                }
                break;

            case ChocoboState.InDuty:
                // Press W during the race and wait for RaceChocoboResult addon to appear
                if (GameHelpers.IsAddonVisible("RaceChocoboResult"))
                {
                    log.Information("[ChocoboRace] Race ended, RaceChocoboResult addon visible");
                    SetState(ChocoboState.WaitingForResult);
                }
                else if (!condition[ConditionFlag.BoundByDuty] && elapsed > 10)
                {
                    // Duty ended without result screen
                    log.Information("[ChocoboRace] Duty ended");
                    SetState(ChocoboState.WaitingForPlayerAvailable);
                }
                else if (elapsed > 600) // 10 min timeout
                {
                    log.Warning("[ChocoboRace] Race timeout");
                    SetState(ChocoboState.Failed);
                }
                else if (elapsed > 5 && elapsed < 7) // Hold W key after 5 seconds
                {
                    GameHelpers.KeyDown(VirtualKey.W);
                }
                break;

            case ChocoboState.WaitingForResult:
                if (elapsed < 1) return;
                // Release W key when race ends
                GameHelpers.KeyUp(VirtualKey.W);
                
                // Check both possible addon names and log which one we're using
                bool raceResult = GameHelpers.IsAddonVisible("RaceChocoboResult");
                bool chocoboResult = GameHelpers.IsAddonVisible("ChocoboResult");
                
                if (raceResult)
                {
                    log.Information("[ChocoboRace] Dismissing RaceChocoboResult screen");
                    GameHelpers.FireAddonCallback("RaceChocoboResult", true, 1);
                    SetState(ChocoboState.DismissingResult);
                }
                else if (chocoboResult)
                {
                    log.Information("[ChocoboRace] Dismissing ChocoboResult screen");
                    GameHelpers.FireAddonCallback("ChocoboResult", true, 1);
                    SetState(ChocoboState.DismissingResult);
                }
                else
                {
                    SetState(ChocoboState.WaitingForPlayerAvailable);
                }
                break;

            case ChocoboState.DismissingResult:
                if (elapsed < 1) return;
                bool raceResultCheck = GameHelpers.IsAddonVisible("RaceChocoboResult");
                bool chocoboResultCheck = GameHelpers.IsAddonVisible("ChocoboResult");
                
                if (raceResultCheck)
                {
                    // Try again to dismiss RaceChocoboResult
                    GameHelpers.FireAddonCallback("RaceChocoboResult", true, 1);
                }
                else if (chocoboResultCheck)
                {
                    // Try again to dismiss ChocoboResult
                    GameHelpers.FireAddonCallback("ChocoboResult", true, 1);
                }
                SetState(ChocoboState.WaitingForPlayerAvailable);
                break;

            case ChocoboState.WaitingForPlayerAvailable:
                // Wait until player is available for next race
                if (elapsed < 2) return;
                if (GameHelpers.IsPlayerAvailable() || elapsed > 30)
                {
                    currentAttempt++;
                    log.Information($"[ChocoboRace] Race {currentAttempt}/{maxAttempts} complete");

                    if (currentAttempt >= maxAttempts)
                    {
                        log.Information($"[ChocoboRace] All {maxAttempts} races complete!");
                        SetState(ChocoboState.Complete);
                    }
                    else
                    {
                        log.Information($"[ChocoboRace] Starting race {currentAttempt + 1}/{maxAttempts}");
                        SetState(ChocoboState.OpeningDutyFinder);
                    }
                }
                break;
        }
    }

    private void SetState(ChocoboState newState)
    {
        log.Information($"[ChocoboRace] {state} -> {newState}");
        state = newState;
        stateEnteredAt = DateTime.UtcNow;
    }
}
