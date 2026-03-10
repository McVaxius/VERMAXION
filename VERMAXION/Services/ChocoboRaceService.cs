using System;
using Dalamud.Game.Command;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;

namespace VERMAXION.Services;

/// <summary>
/// Chocobo Racing service.
/// Based on ChocoboRacing.lua SND script: QueueRoulette(22) for Sagolii Road,
/// click ContentsFinderConfirm Commence, wait for race, spam abilities,
/// wait for RaceChocoboResult, callback RaceChocoboResult true 1.
/// </summary>
public class ChocoboRaceService : IDisposable
{
    private readonly ICommandManager commandManager;
    private readonly IPluginLog log;
    private readonly ICondition condition;

    private ChocoboState state = ChocoboState.Idle;
    private DateTime stateEnteredAt = DateTime.MinValue;
    private int racesCompleted = 0;
    private int racesRequested = 5;

    public enum ChocoboState
    {
        Idle,
        QueueingForRace,
        WaitingForDutyPop,
        ClickingCommence,
        WaitingForRaceStart,
        Racing,
        WaitingForResult,
        DismissingResult,
        WaitingForPlayerAvailable,
        Complete,
        Failed,
    }

    public ChocoboState State => state;
    public bool IsActive => state != ChocoboState.Idle && state != ChocoboState.Complete && state != ChocoboState.Failed;
    public bool IsComplete => state == ChocoboState.Complete;
    public bool IsFailed => state == ChocoboState.Failed;
    public string StatusText => state == ChocoboState.Idle ? "Idle" : $"{state} ({racesCompleted}/{racesRequested})";

    public ChocoboRaceService(ICommandManager commandManager, IPluginLog log)
    {
        this.commandManager = commandManager;
        this.log = log;
        this.condition = Plugin.Condition;
    }

    public void StartRaces(int count)
    {
        racesRequested = count;
        racesCompleted = 0;
        SetState(ChocoboState.QueueingForRace);
        log.Information($"[ChocoboRace] Starting {count} races");
    }

    public void Reset() => SetState(ChocoboState.Idle);

    public void RunTask()
    {
        log.Information("[VERMAXION] Manual Chocobo Racing triggered");
        StartRaces(5);
    }

    public void Dispose() { }

    public void Update()
    {
        if (state == ChocoboState.Idle || state == ChocoboState.Complete || state == ChocoboState.Failed)
            return;

        var elapsed = (DateTime.UtcNow - stateEnteredAt).TotalSeconds;

        switch (state)
        {
            case ChocoboState.QueueingForRace:
                // ChocoboRacing.lua: Instances.DutyFinder:QueueRoulette(22)
                log.Information($"[ChocoboRace] Queueing for Chocobo Race (race {racesCompleted + 1}/{racesRequested})");
                // Open duty finder for chocobo racing
                commandManager.ProcessCommand("/dutyfinder");
                SetState(ChocoboState.WaitingForDutyPop);
                break;

            case ChocoboState.WaitingForDutyPop:
                // ChocoboRacing.lua: wait for ContentsFinderConfirm
                if (GameHelpers.IsAddonVisible("ContentsFinderConfirm"))
                {
                    log.Information("[ChocoboRace] Race pop! Clicking Commence");
                    SetState(ChocoboState.ClickingCommence);
                }
                else if (condition[ConditionFlag.OccupiedInCutSceneEvent])
                {
                    log.Information("[ChocoboRace] In cutscene (race starting)");
                    SetState(ChocoboState.WaitingForRaceStart);
                }
                else if (elapsed > 120)
                {
                    log.Warning("[ChocoboRace] Queue timeout, retrying");
                    SetState(ChocoboState.QueueingForRace);
                }
                break;

            case ChocoboState.ClickingCommence:
                if (elapsed < 1) return;
                // ChocoboRacing.lua: /click ContentsFinderConfirm Commence
                if (GameHelpers.IsAddonVisible("ContentsFinderConfirm"))
                {
                    log.Information("[ChocoboRace] Clicking Commence");
                    GameHelpers.FireAddonCallback("ContentsFinderConfirm", true, 8);
                }
                SetState(ChocoboState.WaitingForRaceStart);
                break;

            case ChocoboState.WaitingForRaceStart:
                // ChocoboRacing.lua: wait for cutscene to end, then wait 6s, then use Super Sprint
                if (!condition[ConditionFlag.OccupiedInCutSceneEvent] && elapsed > 3)
                {
                    log.Information("[ChocoboRace] Race started, racing...");
                    SetState(ChocoboState.Racing);
                }
                else if (elapsed > 60)
                {
                    SetState(ChocoboState.Racing);
                }
                break;

            case ChocoboState.Racing:
                // ChocoboRacing.lua: spam KEY_1 and KEY_2 during race, wait for RaceChocoboResult
                if (GameHelpers.IsAddonVisible("RaceChocoboResult"))
                {
                    log.Information("[ChocoboRace] Race finished, result screen visible");
                    SetState(ChocoboState.WaitingForResult);
                }
                else if (elapsed > 300) // 5 min timeout per race
                {
                    log.Warning("[ChocoboRace] Race timeout");
                    SetState(ChocoboState.Failed);
                }
                break;

            case ChocoboState.WaitingForResult:
                if (elapsed < 1) return;
                // ChocoboRacing.lua: /callback RaceChocoboResult true 1
                if (GameHelpers.IsAddonVisible("RaceChocoboResult"))
                {
                    log.Information("[ChocoboRace] Dismissing race result");
                    GameHelpers.FireAddonCallback("RaceChocoboResult", true, 1);
                    SetState(ChocoboState.DismissingResult);
                }
                else
                {
                    SetState(ChocoboState.WaitingForPlayerAvailable);
                }
                break;

            case ChocoboState.DismissingResult:
                if (elapsed < 2) return;
                SetState(ChocoboState.WaitingForPlayerAvailable);
                break;

            case ChocoboState.WaitingForPlayerAvailable:
                // ChocoboRacing.lua: repeat until Player.Available and not Player.IsBusy
                if (elapsed < 2) return;
                if (GameHelpers.IsPlayerAvailable() || elapsed > 30)
                {
                    racesCompleted++;
                    log.Information($"[ChocoboRace] Race {racesCompleted}/{racesRequested} complete");

                    if (racesCompleted >= racesRequested)
                    {
                        log.Information($"[ChocoboRace] All {racesRequested} races complete!");
                        SetState(ChocoboState.Complete);
                    }
                    else
                    {
                        SetState(ChocoboState.QueueingForRace);
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
