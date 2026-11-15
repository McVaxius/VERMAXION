using System;
using System.Collections.Generic;
using System.Linq;
using VERMAXION.IPC;
using VERMAXION.Models;

namespace VERMAXION.Services;

// One engine-owned visit. No daily-loop call, provider setting changes or retries.
public sealed class IslandSanctuaryRun(
    Func<CalypsoIslandStatus?> readStatus,
    Func<string, bool> runTask,
    Action stop)
{
    private readonly Queue<(IslandChore Chore, string Id, int Failures)> pending = new();
    private readonly HashSet<IslandChore> confirmed = [];
    private string owner = "";
    private string cycle = "";
    private (IslandChore Chore, string Id, int Failures)? current;
    private string currentName = "";
    private bool ownsRequest;
    private bool cancellationPending;
    private DateTime lastRead;

    public CalypsoIslandStatus? ProviderStatus { get; private set; }
    public bool IsActive { get; private set; }
    public bool IsCleanupPending => cancellationPending;
    public bool IsComplete { get; private set; }
    public bool IsFailed { get; private set; }
    public string StatusText { get; private set; } = "Island routine not started.";
    public string ConfirmedCycle => cycle;
    public string OwnerCharacter => owner;
    public string WatchdogProgress => $"{StatusText}|{current?.Id}|{confirmed.Count}";
    public IReadOnlyCollection<IslandChore> ConfirmedChores => confirmed;

    // Called on the framework thread; UI only reads the last provider snapshot.
    public void Refresh(DateTime now)
    {
        if (now - lastRead < TimeSpan.FromSeconds(1)) return;
        lastRead = now;
        ProviderStatus = readStatus();
        if (cancellationPending && ProviderStatus is { } status && !Owns(status, allowUnknownTask: true))
        {
            cancellationPending = ownsRequest = false;
            StatusText = "Island routine stopped; unfinished chores remain unconfirmed.";
        }
    }

    public TaskEligibility Eligibility(CharacterConfig config, string character, bool manual = false)
    {
        if (IsCleanupPending) return TaskEligibility.Blocked("Calypso stop is unconfirmed; waiting for provider status.");
        if (!manual && !config.EnableIslandSanctuary)
            return TaskEligibility.Disabled("Island routine off");
        if (!Enum.IsDefined(config.IslandSanctuary.Cadence))
            return TaskEligibility.Blocked("The saved Island cadence is invalid.");
        if (!manual && config.IslandSanctuary.Cadence == IslandVisitCadence.Manual)
            return TaskEligibility.NotDue("Island routine is manual only.");
        if (!config.IslandSanctuary.Agenda && !config.IslandSanctuary.Exports)
            return TaskEligibility.Unsupported("Select workshop agenda or exports. The other five chores are unavailable stubs.");
        if (ProviderStatus is not { } status)
            return TaskEligibility.Blocked("Enable Calypso with IPC 1 and status schema 1.");
        if (!CalypsoIslandStatus.ValidCycle(status.Cycle))
            return TaskEligibility.Blocked("Calypso returned an invalid Island cycle.");
        if (!Matches(status, character))
            return TaskEligibility.Blocked("Calypso has no matching current-character status.");
        if (!status.InSanctuary)
            return TaskEligibility.Blocked("Enter your island first. Calypso has no travel-only IPC.");
        if (status.Busy)
            return TaskEligibility.Blocked("Calypso is already running. Stop it there before starting an Island routine.");
        var chores = SelectedDueChores(config, status.Cycle, manual).ToArray();
        if (chores.Length == 0)
            return TaskEligibility.NotDue("Selected supported chores are confirmed for this Island cadence.");
        foreach (var chore in chores)
            foreach (var id in TaskIds(chore))
            {
                var task = status.Task(id);
                if (task == null) return TaskEligibility.Blocked("Calypso does not expose a selected Island task.");
                if (!task.Enabled) return TaskEligibility.Blocked("A selected task is disabled in Calypso.");
                if (task.Failures >= 3) return TaskEligibility.Blocked("A selected Calypso task has reached its three-failure daily cap.");
                if (task.State is "locked" || task.State == "done" && !task.HasCompletionEvidence)
                    return TaskEligibility.Blocked("A selected Island task is locked or switched off in Calypso.");
                if (task.State == "blocked" && !(id == "workshop.agenda" &&
                    task.Reason == "needs today's demand reading first" && status.Task("workshop.observe") is { State: "ready" }))
                    return TaskEligibility.Blocked("A selected task is blocked in Calypso. Check its task details.");
                if (task.HasCompletionEvidence && (id is "workshop.agenda" or "exports.sell") &&
                    !task.HasSuccessfulResult(status.LastResult))
                    return TaskEligibility.Blocked("No matching Island completion receipt.");
                if (task.State is not ("ready" or "done" or "blocked"))
                    return TaskEligibility.Blocked("Calypso returned an unsupported task state.");
            }
        return TaskEligibility.Runnable("Ready for selected supported Island chores.");
    }

    public void Start(CharacterConfig config, string character, bool manual)
    {
        if (IsCleanupPending) { Fail("Calypso stop is unconfirmed; waiting for provider status."); return; }
        Reset();
        ProviderStatus = readStatus();
        var eligibility = Eligibility(config, character, manual);
        if (!eligibility.IsRunnable) { Fail(eligibility.Reason); return; }
        owner = character;
        cycle = ProviderStatus!.Cycle;
        foreach (var chore in SelectedDueChores(config, cycle, manual))
            foreach (var id in TaskIds(chore)) pending.Enqueue((chore, id, ProviderStatus.Task(id)!.Failures));
        IsActive = true;
        StatusText = "Starting supported Island chores.";
    }

    public void Update(string character)
    {
        if (!IsActive) return;
        // Re-read immediately around mutation/receipt boundaries, independent of UI refresh pacing.
        ProviderStatus = readStatus();
        if (ProviderStatus is not { } status || !Matches(status, owner) ||
            !string.Equals(character, owner, StringComparison.OrdinalIgnoreCase) || status.Cycle != cycle || !status.InSanctuary)
        { Cancel(); Fail("Island status, character or cycle changed; completion is unconfirmed."); return; }

        if (current is { } active)
        {
            var task = status.Task(active.Id);
            if (task == null || !task.Enabled || task.Name != currentName || task.Failures != active.Failures)
            { Cancel(); Fail("Calypso task failed or its settings changed; completion is unconfirmed."); return; }
            if (status.Busy)
            {
                if (!Owns(status, allowUnknownTask: true))
                {
                    Cancel();
                    Fail(IsCleanupPending ? "Calypso stop is unconfirmed; waiting for provider status."
                        : "Calypso changed to other work; Island ownership was released.");
                    return;
                }
                // Single-task start/finish ticks can be busy with no CurrentTaskId.
                // Keep waiting unconfirmed; only an exact ID can authorize Stop.
                StatusText = string.IsNullOrEmpty(status.Detail) ? "Running selected Calypso task." : status.Detail;
                return;
            }
            ownsRequest = false;
            // Idle, accepted starts, arbitrary 'done' text and Stop never satisfy this receipt.
            if (!task.HasCompletionEvidence || !task.HasSuccessfulResult(status.LastResult))
            { Fail("Calypso stopped without a confirmed task result."); return; }
            ConfirmIfLast(active);
            current = null;
        }
        if (status.Busy) { Fail("Calypso is busy with other work; no request was sent."); return; }
        while (pending.TryDequeue(out var next))
        {
            var task = status.Task(next.Id);
            if (task is not { Enabled: true } || task.Failures >= 3)
            { Fail("Selected Calypso task is unavailable, disabled or at its failure cap."); return; }
            if (task.HasCompletionEvidence)
            {
                // A completed prerequisite may be skipped without crediting a chore.
                // The final task still needs its matching single result and stable failures.
                if (!pending.Any(item => item.Chore == next.Chore) &&
                    (task.Failures != next.Failures ||
                     !task.HasSuccessfulResult(status.LastResult)))
                { Fail("No matching Island completion receipt."); return; }
                ConfirmIfLast(next);
                continue;
            }
            if (task.State != "ready") { Fail("Selected Calypso task is not ready; no request was sent."); return; }
            current = (next.Chore, next.Id, task.Failures);
            currentName = task.Name;
            if (!runTask(next.Id)) { Fail("Calypso rejected the task; completion is unconfirmed."); return; }
            ownsRequest = true;
            StatusText = "Running selected Calypso task.";
            return;
        }
        IsActive = false;
        IsComplete = true;
        StatusText = confirmed.Contains(IslandChore.Agenda) &&
            ProviderStatus?.Task("workshop.agenda")?.Reason == "planned today (suggest only)"
            ? "Supported chores confirmed. Workshop agenda is planned only; it was not applied."
            : "Selected supported chores confirmed. Unavailable chores were not run or credited.";
    }

    private void ConfirmIfLast((IslandChore Chore, string Id, int Failures) task)
    {
        if (!pending.Any(next => next.Chore == task.Chore)) confirmed.Add(task.Chore);
    }

    private static bool Matches(CalypsoIslandStatus status, string character)
        => !string.IsNullOrWhiteSpace(character) && string.Equals(status.Character, character, StringComparison.OrdinalIgnoreCase);

    private bool Owns(CalypsoIslandStatus status, bool allowUnknownTask = false)
        => ownsRequest && current is { } task && Matches(status, owner) && status.Cycle == cycle &&
           status.Busy && status.Mode == "single" && (status.CurrentTaskId == task.Id || allowUnknownTask && string.IsNullOrEmpty(status.CurrentTaskId));

    public void Cancel()
    {
        IsActive = IsComplete = false;
        if (cancellationPending) return; // No repeated global Stop call.
        ProviderStatus = readStatus();
        cancellationPending = ownsRequest && (ProviderStatus == null || Owns(ProviderStatus, allowUnknownTask: true));
        if (cancellationPending && ProviderStatus != null && Owns(ProviderStatus))
        {
            try { stop(); }
            catch { /* Retain the owned-work blocker until a fresh status settles it; never retry. */ }
        }
        if (!cancellationPending) ownsRequest = false;
        StatusText = cancellationPending ? "Calypso stop is unconfirmed; waiting for provider status."
            : "Island routine stopped; unfinished chores remain unconfirmed.";
    }

    public void Reset()
    {
        if (IsActive) Cancel();
        if (IsCleanupPending) return;
        pending.Clear(); confirmed.Clear(); current = null;
        owner = cycle = currentName = "";
        ownsRequest = IsActive = IsComplete = IsFailed = false;
    }

    private void Fail(string reason)
    {
        if (!cancellationPending) ownsRequest = false;
        IsActive = IsComplete = false;
        IsFailed = true;
        StatusText = reason;
    }

    public static IEnumerable<string> TaskIds(IslandChore chore) => chore switch
    {
        IslandChore.Agenda => ["workshop.observe", "workshop.agenda"],
        IslandChore.Exports => ["exports.sell"],
        _ => [],
    };

    private static IEnumerable<IslandChore> SelectedDueChores(CharacterConfig config, string cycle, bool manual)
    {
        if (config.IslandSanctuary.Agenda && (manual || IsDue(config.IslandSanctuary.Cadence, config.IslandAgendaConfirmedCycle, cycle)))
            yield return IslandChore.Agenda;
        if (config.IslandSanctuary.Exports && (manual || IsDue(config.IslandSanctuary.Cadence, config.IslandExportsConfirmedCycle, cycle)))
            yield return IslandChore.Exports;
    }

    public static bool IsDue(IslandVisitCadence cadence, string last, string current)
        => !CalypsoIslandStatus.ValidCycle(last) || (cadence switch
        {
            IslandVisitCadence.Weekly => last.Split('.')[0] != current.Split('.')[0],
            IslandVisitCadence.Daily => last != current,
            _ => false,
        });
}
