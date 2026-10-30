using System;
using System.Collections.Generic;
using System.Linq;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using NativeFramework = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework;
using NoteRow = Lumina.Excel.Sheets.ContentsNote;
using Lumina.Excel.Sheets;

namespace VERMAXION.Beastmaster;

internal sealed unsafe partial class BeastmasterService
{
    private readonly BeastmasterWeeklyFreshness weeklyFreshness = new();
    private readonly HashSet<uint> weeklyAttempted = [];
    private IReadOnlyList<BeastmasterWeeklyObjective> weeklyObjectives = [];
    private bool weeklyRegistered;
    private bool weeklyActive;
    private long weeklyRunReset;
    private DateTime weeklyCreditDeadline;
    private uint weeklyEnteredBoard;
    private DateTime nextWeeklyChallengeLogRequest;
    private bool ownsWeeklyChallengeLog;

    internal bool WeeklyActive => weeklyActive;
    internal DateTime? WeeklyResetUtc => weeklyFreshness.Fresh && weeklyFreshness.ResetTimestamp > 0
        ? DateTimeOffset.FromUnixTimeSeconds(weeklyFreshness.ResetTimestamp).UtcDateTime : null;
    internal IReadOnlyList<BeastmasterWeeklyObjective> WeeklyObjectives => weeklyObjectives;
    internal IReadOnlyList<BeastmasterWeeklyObjective> SelectedWeeklyObjectives => BeastmasterWeeklyPolicy.SelectedObjectives(Settings, weeklyObjectives);
    internal bool SelectedWeeklyComplete => SelectedWeeklyObjectives is { Count: > 0 } selected &&
        selected.All(row => row.State == BeastmasterWeeklyState.Complete);
    internal uint EffectiveBoard => Settings.Board;
    internal int EffectiveRunCount => weeklyActive
        ? weeklyAttempted.Count + SelectedWeeklyObjectives.Count(row => row.State == BeastmasterWeeklyState.Unfinished && !weeklyAttempted.Contains(row.Board))
        : Math.Max(1, Settings.RunCount);
    internal string WeeklyStatusText
    {
        get
        {
            if (weeklyActive || EntryPending && entryAttempt!.StopRequested) return StatusText;
            var selected = SelectedWeeklyObjectives;
            if (weeklyObjectives.Count != 5 || selected.Any(row => row.State == BeastmasterWeeklyState.Unavailable) ||
                Settings.WeeklyBoards == null && selected.Count == 0 && weeklyObjectives.Any(row => row.State == BeastmasterWeeklyState.Unavailable))
                return "Weekly Challenge Log data is unavailable.";
            if (selected.Count == 0) return "No weekly boards selected.";
            if (weeklyObjectives.All(row => row.State == BeastmasterWeeklyState.Complete)) return "All five weekly challenges are complete.";
            if (selected.All(row => row.State == BeastmasterWeeklyState.Complete)) return "Selected weekly challenges are complete.";
            return $"Selected weekly challenges complete: {selected.Count(row => row.State == BeastmasterWeeklyState.Complete)}/{selected.Count}.";
        }
    }

    internal void RefreshWeeklyStatus(bool characterRegistered)
    {
        weeklyRegistered = characterRegistered;
        var rows = new List<BeastmasterWeeklyObjective>(5);
        var ready = characterRegistered && Svc.ClientState.IsLoggedIn && Svc.PlayerState.IsLoaded && Svc.PlayerState.ContentId != 0;
        try
        {
            var ui = ready ? UIState.Instance() : null;
            var note = ui != null ? &ui->ContentsNote : null;
            var now = ready ? NativeFramework.GetServerTime() : 0;
            var fresh = weeklyFreshness.Observe(ready ? Svc.PlayerState.ContentId : 0, ready,
                note != null ? (int)note->State : -1, ui != null ? ui->NextChallengeLogResetTimestamp : 0, now);
            var notes = Svc.Data.GetExcelSheet<NoteRow>();
            var duties = Svc.Data.GetExcelSheet<ContentFinderCondition>();
            for (var index = 0; index < BeastmasterWeeklyPolicy.ObjectiveRows.Length; index++)
            {
                var board = (uint)index + 1;
                var rowId = BeastmasterWeeklyPolicy.ObjectiveRows[index];
                var name = BeastmasterWeeklyPolicy.ObjectiveNames[index];
                var boardName = string.Empty;
                var level = 0; var itemLevel = 0;
                // Unlocks are separate from temporary gear, job and party restrictions.
                var unlocked = ready && QuestManager.IsQuestComplete(BeastmasterWeeklyPolicy.UnlockQuests[index]) &&
                    QuestManager.IsQuestComplete(66967);
                var state = BeastmasterWeeklyState.Unavailable;
                var reason = "Weekly Challenge Log data is unavailable.";
                if (notes.TryGetRow(rowId, out var objective) && objective.ContentType.RowId == 11 && objective.MenuOrder == board &&
                    objective.RequiredAmount == 1 && objective.ReqUnlock.RowId == BeastmasterWeeklyPolicy.UnlockQuests[index] &&
                    duties.TryGetRow(1087 + board, out var duty) && duty.TerritoryType.RowId == TerritoryForBoard(board) &&
                    duty.UnlockCriteria.RowId == objective.ReqUnlock.RowId && duty.ClassJobLevelRequired > 0)
                {
                    name = objective.Name.ExtractText(); boardName = duty.Name.ExtractText();
                    level = duty.ClassJobLevelRequired; itemLevel = duty.ItemLevelRequired;
                    var complete = false;
                    var completionKnown = fresh && note != null && BeastmasterWeeklyPolicy.TryReadCompletion(note->CompletionFlags,
                        note->CompletionFlagsBitArray.BitCount, rowId, out complete);
                    if (completionKnown && complete) { state = BeastmasterWeeklyState.Complete; reason = "Complete"; }
                    else if (ready)
                    {
                        // Public Quest row 66967 is Rising to the Challenge, the Challenge Log unlock.
                        if (!unlocked)
                        { state = BeastmasterWeeklyState.Locked; reason = "Required board or Challenge Log quest is incomplete."; }
                        else if (Svc.Objects.LocalPlayer is not { } player || player.ClassJob.Value.Abbreviation.ExtractText() != "BST")
                        { state = BeastmasterWeeklyState.Locked; reason = "Crucible requires Beastmaster"; }
                        else if (player.Level < Math.Max(level, objective.LevelUnlock))
                        { state = BeastmasterWeeklyState.Locked; reason = $"Requires Beastmaster level {Math.Max(level, objective.LevelUnlock)}."; }
                        else if (EquippedItemLevel is not { } equipped)
                        { reason = "Equipped item level is unavailable."; }
                        else if (equipped < itemLevel)
                        { state = BeastmasterWeeklyState.GearBlocked; reason = $"Requires item level {itemLevel}; current item level {equipped}."; }
                        else if (Svc.Party.Length > 1)
                        { state = BeastmasterWeeklyState.Locked; reason = "Leave the party before entering the Crucible"; }
                        else if (completionKnown) { state = BeastmasterWeeklyState.Unfinished; reason = "Unfinished"; }
                        else reason = "Waiting for a fresh Challenge Log load for this character and reset.";
                    }
                }
                rows.Add(new BeastmasterWeeklyObjective(board, rowId, name, boardName, state, reason, level, itemLevel, unlocked));
            }
        }
        catch (Exception)
        {
            weeklyFreshness.Observe(0, false, -1, 0, 0);
            rows.Clear();
            for (var index = 0; index < BeastmasterWeeklyPolicy.ObjectiveRows.Length; index++)
                rows.Add(new BeastmasterWeeklyObjective((uint)index + 1, BeastmasterWeeklyPolicy.ObjectiveRows[index],
                    BeastmasterWeeklyPolicy.ObjectiveNames[index], string.Empty, BeastmasterWeeklyState.Unavailable,
                    "Weekly Challenge Log data is unavailable.", 0, 0, Unlocked: false));
        }
        weeklyObjectives = rows;
    }

    internal bool CanStartWeekly(out string reason) => CanStartWeekly(getSettings(), out reason);

    internal bool CanStartWeekly(BeastmasterSettings settings, out string reason)
    {
        RefreshWeeklyStatus(weeklyRegistered);
        if (disposed || IsActive || IsPaused) { reason = "A Beastmaster run or its cleanup is already active"; return false; }
        if (!(HasOpenRegistrationScreen ? RegistrationInteractionReady : BeastmasterPlayer.IsReady))
        { reason = "Wait until the current character is ready"; return false; }
        if (IsCrucibleTerritory(Svc.ClientState.TerritoryType))
        { reason = "Leave the current Crucible duty before starting weekly challenges."; return false; }
        if (!NativeQueueIsIdle()) { reason = "An existing duty queue must settle before starting Beastmaster."; return false; }
        if (!Plugin.CommandManager.Commands.ContainsKey("/vnav")) { reason = "vnavmesh is required for board navigation"; return false; }
        if (!CanStartCombat(settings, out reason)) return false;
        if (!RunContextReady(out reason)) return false;
        if (Svc.Objects.LocalPlayer?.ClassJob.Value.Abbreviation.ExtractText() != "BST")
        { reason = "Crucible requires Beastmaster"; return false; }
        var selected = BeastmasterWeeklyPolicy.SelectedObjectives(settings, weeklyObjectives);
        if (selected.Count == 0 && (settings.WeeklyBoards != null ||
            weeklyObjectives.Count == 5 && weeklyObjectives.All(row => row.State != BeastmasterWeeklyState.Unavailable)))
        { reason = "No weekly boards selected."; return false; }
        if (selected.Count > 0 && !selected.Any(row => row.State is BeastmasterWeeklyState.Unfinished or BeastmasterWeeklyState.Unavailable))
        {
            reason = selected.All(row => row.State == BeastmasterWeeklyState.Complete)
                ? "Selected weekly challenges are complete."
                : "No unfinished eligible weekly challenges. Locked or gear-blocked boards remain.";
            return false;
        }
        // Run acquires unloaded native data before choosing any board. Readiness
        // must not require a load that only the accepted Run can request.
        if (!weeklyFreshness.Fresh)
        { reason = string.Empty; return true; }
        if (weeklyObjectives.Count != 5 || selected.Count == 0 || selected.Any(row => row.State == BeastmasterWeeklyState.Unavailable))
        { reason = "Weekly Challenge Log data is unavailable."; return false; }
        reason = string.Empty;
        return true;
    }

    internal bool StartWeekly() => StartConfigured(getSettings(), true);

    private bool StartWeeklyConfigured(BeastmasterSettings settings)
    {
        if (!CanStartWeekly(settings, out var reason)) { status = reason; return false; }
        if (!RunContextReady(out reason)) { status = reason; return false; }
        if (!TryCaptureCombatProvider(settings, out reason)) { status = "Combat restoration pending: " + reason; return false; }
        runSettings = BeastmasterWeeklyPolicy.CopySettings(settings);
        runSettings.Goal = BeastmasterRunGoal.FixedRuns;
        runData = getCharacterData(); owner = Svc.PlayerState.ContentId;
        operatorStopped = false; recoveryAt = default;
        preparedOwned = null;
        runData.EnsureFamiliars(CrucibleTeam.FamiliarSheetDatas.Keys);
        weeklyActive = true; weeklyRunReset = 0; weeklyAttempted.Clear();
        CurrentRun = 0; IsPaused = false; pausedAt = default; waitingForRevival = false; ResultObserved = resultSeen = false;
        BoardRunCompleted = false;
        RememberRunDisplay(weekly: true);
        PrepareRunOwnership(out _); // Native registration can finish loading ownership.
        RequestFastRankScan();
        if (!combat.TrySuspend(out var combatReason)) { Fail(combatReason); return true; }
        BeginWeeklyPreparation();
        return weeklyActive;
    }

    private void BeginWeeklyPreparation()
    {
        CloseWeeklyChallengeLog();
        Transition(RunState.WeeklyPreparing, "Waiting for a fresh Challenge Log load for this character and reset.");
    }

    private void UpdateWeeklyPreparation()
    {
        if (operatorStopped || IsPaused || !weeklyActive || !CanObserveCharacter || !RegistrationPlayerReady) return;
        if (!weeklyFreshness.Fresh)
        {
            var now = DateTime.UtcNow;
            if (now < nextWeeklyChallengeLogRequest) return;
            nextWeeklyChallengeLogRequest = now.AddSeconds(5);
            var module = AgentModule.Instance();
            var agent = module == null ? null : module->GetAgentByInternalId(AgentId.ContentsNote);
            if (agent == null) return;
            ownsWeeklyChallengeLog |= !agent->IsAgentActive();
            agent->Show();
            return;
        }
        // The native global completion flags need no category selection. An
        // already-loaded current-character log is usable without reopening it.
        CloseWeeklyChallengeLog();
        weeklyRunReset = weeklyFreshness.ResetTimestamp;
        runSettings!.WeeklyBoards = BeastmasterWeeklyPolicy.SelectedObjectives(Settings, weeklyObjectives)
            .Select(row => row.Board).ToHashSet();
        displayedRunLimit = EffectiveRunCount;
        BeginNextWeeklyBoard();
    }

    private void CloseWeeklyChallengeLog()
    {
        if (ownsWeeklyChallengeLog && CanObserveCharacter)
        {
            var module = AgentModule.Instance();
            var agent = module == null ? null : module->GetAgentByInternalId(AgentId.ContentsNote);
            if (agent != null && agent->IsAgentActive()) agent->Hide();
        }
        ownsWeeklyChallengeLog = false;
        nextWeeklyChallengeLogRequest = default;
    }

    private void BeginNextWeeklyBoard()
    {
        RefreshWeeklyStatus(weeklyRegistered);
        if (!ValidateWeeklyFreshness(out var reason)) { Fail(reason); return; }
        var selected = SelectedWeeklyObjectives;
        var next = BeastmasterWeeklyPolicy.Next(selected, weeklyAttempted);
        if (next == null)
        {
            var text = selected.Count == 0 ? "No weekly boards selected."
                : weeklyObjectives.All(row => row.State == BeastmasterWeeklyState.Complete) ? "All five weekly challenges are complete."
                : selected.All(row => row.State == BeastmasterWeeklyState.Complete) ? "Selected weekly challenges are complete."
                : "Eligible selected weekly challenges are complete. Locked or gear-blocked boards remain.";
            Stop(); Transition(RunState.Complete, text);
            return;
        }
        BeastmasterWeeklyPolicy.SelectBoard(runSettings!, next.Board);
        weeklyAttempted.Add(next.Board);
        weeklyEnteredBoard = 0;
        CurrentRun++; boardStep = 0; ResultObserved = resultSeen = false;
        BeginRegistration();
    }

    private bool ValidateWeeklyFreshness(out string reason)
    {
        if (!weeklyActive || owner == 0 || Svc.PlayerState.ContentId != owner || !weeklyFreshness.Fresh ||
            weeklyFreshness.Character != owner || weeklyFreshness.ResetTimestamp != weeklyRunReset ||
            weeklyObjectives.Count != 5 || SelectedWeeklyObjectives.Any(row => row.State == BeastmasterWeeklyState.Unavailable))
        { reason = "Weekly Challenge Log data changed or became unavailable. No further board was queued."; return false; }
        reason = string.Empty;
        return true;
    }

    private bool ValidateWeeklyEntry(out string reason)
    {
        if (!ValidateWeeklyFreshness(out reason)) return false;
        var row = SelectedWeeklyObjectives.FirstOrDefault(row => row.Board == Settings.Board);
        if (row == null) { reason = "The current weekly board is not selected."; return false; }
        if (row?.State != BeastmasterWeeklyState.Unfinished)
        { reason = row?.State == BeastmasterWeeklyState.Complete ? "The selected weekly challenge is already complete. No further entry was submitted." : row?.Reason ?? "Weekly Challenge Log data is unavailable."; return false; }
        return true;
    }

    private void BeginWeeklyCreditVerification()
    {
        if (!SuspendCombat()) return;
        Save(); StopNavigation(); menus.Reset(); route = null;
        weeklyCreditDeadline = DateTime.UtcNow.AddSeconds(30);
        Transition(RunState.WeeklyVerifying, "Waiting for fresh weekly challenge credit.");
    }

    private void VerifyWeeklyCredit()
    {
        if (!ValidateWeeklyFreshness(out var reason)) { Fail(reason); return; }
        var row = SelectedWeeklyObjectives.FirstOrDefault(row => row.Board == Settings.Board);
        if (BeastmasterWeeklyPolicy.ConfirmsCredit(weeklyFreshness.Fresh, weeklyFreshness.Character, owner,
            weeklyFreshness.ResetTimestamp, weeklyRunReset, false, row?.State == BeastmasterWeeklyState.Complete,
            resultSeen && weeklyEnteredBoard == Settings.Board, !IsCrucibleTerritory(Svc.ClientState.TerritoryType)))
        { BeginNextWeeklyBoard(); return; }
        if (DateTime.UtcNow >= weeklyCreditDeadline)
            Fail("Fresh weekly challenge credit was not confirmed after the board result. No board was repeated.");
    }

}
