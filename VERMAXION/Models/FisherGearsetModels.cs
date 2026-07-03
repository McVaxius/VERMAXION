using System;
using System.Collections.Generic;
using System.Linq;

namespace VERMAXION.Models;

public static class ClassJobIds
{
    public const int Conjurer = 6;
    public const int Fisher = 18;
    public const int WhiteMage = 24;
}

public readonly record struct SavedGearsetSnapshot(
    int Slot,
    int Id,
    int ClassJobId,
    bool Exists);

public readonly record struct FisherGearsetSelection(int Slot, int Id);

public static class FisherGearsetSelectionPolicy
{
    public static FisherGearsetSelection? SelectLowestSavedFisher(
        IEnumerable<SavedGearsetSnapshot> gearsets)
        => gearsets
            .Where(entry => entry.Exists && entry.ClassJobId == ClassJobIds.Fisher)
            .OrderBy(entry => entry.Slot)
            .Select(entry => (FisherGearsetSelection?)new FisherGearsetSelection(entry.Slot, entry.Id))
            .FirstOrDefault();
}

public enum FisherGearsetLookupStatus
{
    Found,
    Missing,
    ModuleUnavailable,
}

public readonly record struct FisherGearsetLookupResult(
    FisherGearsetLookupStatus Status,
    FisherGearsetSelection? Selection,
    string Error)
{
    public static FisherGearsetLookupResult Found(FisherGearsetSelection selection)
        => new(FisherGearsetLookupStatus.Found, selection, string.Empty);

    public static FisherGearsetLookupResult Missing()
        => new(
            FisherGearsetLookupStatus.Missing,
            null,
            "No saved Fisher gearset exists in slots 0-99.");

    public static FisherGearsetLookupResult ModuleUnavailable(string error)
        => new(FisherGearsetLookupStatus.ModuleUnavailable, null, error);
}

public readonly record struct FisherGearsetEquipRequestResult(
    bool Available,
    int Result,
    string Error)
{
    public static FisherGearsetEquipRequestResult Completed(int result)
        => new(true, result, string.Empty);

    public static FisherGearsetEquipRequestResult ModuleUnavailable(string error)
        => new(false, -1, error);
}

public interface IFisherGearsetRuntime
{
    int GetCurrentClassJobId();
    FisherGearsetLookupResult FindFirstSavedFisherGearset();
    FisherGearsetEquipRequestResult EquipGearset(int gearsetId);
}

public enum FisherGearsetEquipState
{
    Running,
    Succeeded,
    MissingGearset,
    TimedOut,
}

public enum FisherGearsetEventKind
{
    Selected,
    EquipRequested,
    TransientFailure,
    Verified,
    TerminalFailure,
}

public readonly record struct FisherGearsetEvent(
    FisherGearsetEventKind Kind,
    string Message);

/// <summary>
/// Shared polling operation for production fishing startup and the focused UI test.
/// It only reads the current class-job, resolves saved gearsets, and requests an equip.
/// </summary>
public sealed class FisherGearsetEquipOperation
{
    public static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(5);

    private readonly IFisherGearsetRuntime runtime;
    private readonly DateTimeOffset deadlineUtc;
    private DateTimeOffset nextAttemptUtc = DateTimeOffset.MinValue;
    private string lastTransientError = string.Empty;

    public FisherGearsetEquipOperation(
        IFisherGearsetRuntime runtime,
        DateTimeOffset startedAtUtc,
        DateTimeOffset deadlineUtc)
    {
        this.runtime = runtime;
        this.deadlineUtc = deadlineUtc.ToUniversalTime();
        StartedAtUtc = startedAtUtc.ToUniversalTime();
        StartingClassJobId = runtime.GetCurrentClassJobId();
    }

    public DateTimeOffset StartedAtUtc { get; }
    public int StartingClassJobId { get; }
    public int FinalClassJobId { get; private set; }
    public FisherGearsetSelection? Selection { get; private set; }
    public int? LastEquipRequestResult { get; private set; }
    public FisherGearsetEquipState State { get; private set; } = FisherGearsetEquipState.Running;
    public bool IsComplete => State != FisherGearsetEquipState.Running;
    public bool Succeeded => State == FisherGearsetEquipState.Succeeded;

    public IReadOnlyList<FisherGearsetEvent> Tick(DateTimeOffset nowUtc)
    {
        if (IsComplete)
            return Array.Empty<FisherGearsetEvent>();

        var events = new List<FisherGearsetEvent>();
        FinalClassJobId = runtime.GetCurrentClassJobId();
        if (FinalClassJobId == ClassJobIds.Fisher)
        {
            State = FisherGearsetEquipState.Succeeded;
            events.Add(new(
                FisherGearsetEventKind.Verified,
                $"Verified final class-job ID: {FinalClassJobId}."));
            return events;
        }

        nowUtc = nowUtc.ToUniversalTime();
        if (nowUtc >= deadlineUtc)
        {
            State = FisherGearsetEquipState.TimedOut;
            var suffix = string.IsNullOrWhiteSpace(lastTransientError)
                ? string.Empty
                : $" Last error: {lastTransientError}";
            events.Add(new(
                FisherGearsetEventKind.TerminalFailure,
                $"Timed out verifying Fisher; final class-job ID: {FinalClassJobId}.{suffix}"));
            return events;
        }

        if (nowUtc < nextAttemptUtc)
            return events;

        nextAttemptUtc = nowUtc + RetryInterval;
        var lookup = runtime.FindFirstSavedFisherGearset();
        if (lookup.Status == FisherGearsetLookupStatus.Missing)
        {
            State = FisherGearsetEquipState.MissingGearset;
            events.Add(new(FisherGearsetEventKind.TerminalFailure, lookup.Error));
            return events;
        }

        if (lookup.Status == FisherGearsetLookupStatus.ModuleUnavailable ||
            lookup.Selection == null)
        {
            lastTransientError = string.IsNullOrWhiteSpace(lookup.Error)
                ? "RaptureGearsetModule is unavailable."
                : lookup.Error;
            events.Add(new(FisherGearsetEventKind.TransientFailure, lastTransientError));
            return events;
        }

        if (Selection != lookup.Selection)
        {
            Selection = lookup.Selection;
            events.Add(new(
                FisherGearsetEventKind.Selected,
                $"Selected Fisher gearset slot {Selection.Value.Slot}, ID {Selection.Value.Id}."));
        }

        var equip = runtime.EquipGearset(Selection.Value.Id);
        if (!equip.Available)
        {
            lastTransientError = string.IsNullOrWhiteSpace(equip.Error)
                ? "RaptureGearsetModule became unavailable before the equip request."
                : equip.Error;
            events.Add(new(FisherGearsetEventKind.TransientFailure, lastTransientError));
            return events;
        }

        LastEquipRequestResult = equip.Result;
        events.Add(new(
            FisherGearsetEventKind.EquipRequested,
            $"Equip request result: {equip.Result} (gearset ID {Selection.Value.Id})."));

        FinalClassJobId = runtime.GetCurrentClassJobId();
        if (FinalClassJobId == ClassJobIds.Fisher)
        {
            State = FisherGearsetEquipState.Succeeded;
            events.Add(new(
                FisherGearsetEventKind.Verified,
                $"Verified final class-job ID: {FinalClassJobId}."));
        }

        return events;
    }
}
