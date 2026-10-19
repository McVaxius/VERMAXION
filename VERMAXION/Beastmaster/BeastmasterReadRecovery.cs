using System;

namespace VERMAXION.Beastmaster;

// Only a verified missing native field uses this recovery path. Other errors
// still reach the run's normal exception handler.
internal sealed class BeastmasterReadUnavailableException(string window, string detail)
    : Exception($"{window}: {detail}")
{
    internal string Window { get; } = window;
}

internal static class BeastmasterReadBounds
{
    internal static bool Contains(int available, int offset, uint count = 1, int stride = 1, int width = 1)
    {
        if (available < 0 || offset < 0 || stride < 1 || width < 1) return false;
        if (count == 0) return offset <= available;
        var end = (ulong)offset + (ulong)(count - 1) * (uint)stride + (uint)width;
        return end <= (ulong)available;
    }

    internal static void MarkResultsUnconfirmed(BeastmasterCharacterData data, System.Collections.Generic.IEnumerable<uint> participants)
    {
        data.RanksObservedAtUtc = DateTime.MinValue;
        foreach (var number in participants)
            if (data.Familiars.TryGetValue(number, out var beast))
            {
                // Rank is a known lower bound. Old EXP is not a reading from the
                // completed board and must not appear as confirmed current EXP.
                beast.Exp = string.Empty;
                beast.ObservedAtUtc = DateTime.MinValue;
            }
    }
}

// A callback is only an attempt; a still-open window can be checked again after
// the normal delay, including when the game reuses the same native address.
internal readonly record struct BeastmasterMenuSubmission(nint Address, DateTime SubmittedAt)
{
    internal bool IsPending(nint currentAddress, DateTime now) =>
        Address != 0 && Address == currentAddress && now - SubmittedAt < BeastmasterReadRecovery.Delay;

    internal BeastmasterMenuSubmission ResumeAfterPause(TimeSpan elapsed) =>
        this with { SubmittedAt = SubmittedAt + elapsed };
}

// One current native screen per observer. A dispatched recovery remains consumed
// until the screen is replaced or a successful observation clears it.
internal sealed class BeastmasterReadRecovery
{
    internal static readonly TimeSpan Delay = TimeSpan.FromSeconds(5);
    private string? window;
    private nint address;
    private DateTime since;
    private bool submitted;

    internal bool Ready(string currentWindow, nint currentAddress, DateTime now)
    {
        if (window != currentWindow || address != currentAddress)
        {
            window = currentWindow; address = currentAddress; since = now; submitted = false;
        }
        return !submitted && now - since >= Delay;
    }

    internal void Submitted() => submitted = true;
    internal void Reset() { window = null; address = 0; since = default; submitted = false; }
    internal void ResumeAfterPause(TimeSpan elapsed) { if (window != null) since += elapsed; }
}
