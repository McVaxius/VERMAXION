using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace VERMAXION.Beastmaster;

internal enum BeastmasterWeeklyState { Complete, Unfinished, Locked, GearBlocked, Unavailable }

internal sealed record BeastmasterWeeklyObjective(uint Board, uint ContentsNoteRowId, string Name, string BoardName,
    BeastmasterWeeklyState State, string Reason, int MinimumLevel, int MinimumItemLevel, bool Unlocked = true);

// Session-only evidence. A pre-existing Loaded flag is not a fresh load receipt.
internal sealed class BeastmasterWeeklyFreshness
{
    internal ulong Character { get; private set; }
    internal long ResetTimestamp { get; private set; }
    internal bool Fresh { get; private set; }
    private bool pendingLoad;

    internal bool Observe(ulong character, bool ready, int loadState, long nextReset, long now)
    {
        if (!ready || character == 0)
        {
            Character = 0; ResetTimestamp = 0; Fresh = pendingLoad = false;
            return false;
        }
        if (Character != character)
        {
            Character = character; ResetTimestamp = 0; Fresh = pendingLoad = false;
        }
        if (ResetTimestamp != nextReset)
        {
            // Do not carry a load witness across a known reset epoch.
            pendingLoad = false;
            ResetTimestamp = nextReset;
            Fresh = false;
        }
        if (nextReset <= now || nextReset - now > 604800 || now <= 0)
        {
            Fresh = pendingLoad = false;
            return false;
        }
        if (loadState is 0 or 1)
        {
            pendingLoad = true;
            Fresh = false;
            return false;
        }
        if (loadState != 2)
        {
            if (loadState != 2) pendingLoad = false;
            Fresh = false;
            return false;
        }
        if (pendingLoad) { Fresh = true; pendingLoad = false; }
        return Fresh;
    }
}

internal static class BeastmasterEntryPrompt
{
    internal static bool Matches(string actual, string localizedText)
    {
        var prompt = Normalize(actual);
        if (prompt.Contains("PROCEEDTOCHALLENGETHISBOARD", StringComparison.Ordinal)) return true;
        var question = localizedText.Split(['\r', '\n', '\u2028', '\u2029'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? "";
        var expected = Normalize(question);
        return expected.Length > 0 && prompt.Contains(expected, StringComparison.Ordinal);
    }

    private static string Normalize(string value)
        => string.Concat(value.Normalize(NormalizationForm.FormKC).Where(char.IsLetterOrDigit)).ToUpperInvariant();
}

internal enum BeastmasterEntryOutcome { Pending, Entered, Withdrawn, SessionEnded }

// A dispatched native entry stays owned until an observed outcome settles it.
internal sealed class BeastmasterEntryAttempt(ulong character, uint board)
{
    internal ulong Character { get; } = character;
    internal uint Board { get; } = board;
    internal uint Duty => 1087 + Board;
    internal BeastmasterEntryOutcome Outcome { get; private set; }
    internal bool Pending => Outcome == BeastmasterEntryOutcome.Pending;
    internal bool StopRequested { get; private set; }
    internal bool CommenceSubmitted { get; private set; }
    internal bool SmallTeamSubmitted { get; private set; }
    internal bool PromptDeclineSubmitted { get; private set; }
    internal bool CancelSubmitted { get; private set; }
    internal bool UserDeclineObserved { get; private set; }
    internal bool MatchingQueueObserved { get; private set; }
    internal bool ConflictingQueueObserved { get; private set; }
    private bool promptVisible;
    private string submittedPrompt = string.Empty;
    private DateTime nextCommence, nextPrompt, nextCancel, nextDecline, idleSince;

    internal void Resume()
    {
        StopRequested = UserDeclineObserved = false;
        PromptDeclineSubmitted = CancelSubmitted = false;
        nextCommence = nextPrompt = nextCancel = nextDecline = idleSince = default;
        submittedPrompt = string.Empty;
    }

    internal void ObserveIdle(bool idle, DateTime now)
    {
        if (!Pending || !idle) { idleSince = default; return; }
        if (idleSince == default) idleSince = now;
        if (now - idleSince >= TimeSpan.FromSeconds(5)) Outcome = BeastmasterEntryOutcome.Withdrawn;
    }

    internal void ObservePrompt(bool visible)
    {
        if (!visible && promptVisible) { submittedPrompt = string.Empty; nextPrompt = default; }
        promptVisible = visible;
    }

    internal bool TryConfirmBoardPrompt(string prompt, DateTime? observedAt = null)
    {
        var now = observedAt ?? DateTime.UtcNow;
        if (!Pending || StopRequested || UserDeclineObserved || prompt.Length == 0 || submittedPrompt == prompt && now < nextPrompt) return false;
        submittedPrompt = prompt; nextPrompt = now.AddSeconds(5);
        SmallTeamSubmitted = true;
        return true;
    }

    internal void ObserveUserDecline(bool matchingPrompt, bool negativeButton)
    {
        if (Pending && matchingPrompt && negativeButton && !CommenceSubmitted) UserDeclineObserved = true;
    }

    internal void ObserveQueue(bool exactQueue, bool otherQueue = false)
    {
        if (exactQueue) MatchingQueueObserved = true;
        else if (otherQueue) ConflictingQueueObserved = true;
    }

    internal void RequestStop() => StopRequested = true;
    internal bool TryCommence(bool exactQueue, DateTime? observedAt = null)
    {
        var now = observedAt ?? DateTime.UtcNow;
        if (!Pending || StopRequested || UserDeclineObserved || !exactQueue || now < nextCommence) return false;
        CommenceSubmitted = true; nextCommence = now.AddSeconds(5);
        return true;
    }
    internal bool TryConfirmSmallTeam(bool matchingPrompt)
    {
        if (!Pending || StopRequested || UserDeclineObserved || SmallTeamSubmitted || CommenceSubmitted || !matchingPrompt) return false;
        SmallTeamSubmitted = true;
        return true;
    }
    internal bool TryDeclinePrompt(bool matchingPrompt)
    {
        if (!Pending || !StopRequested || SmallTeamSubmitted || CommenceSubmitted || PromptDeclineSubmitted || !matchingPrompt) return false;
        PromptDeclineSubmitted = true;
        return true;
    }

    internal bool TryDeclineBoardPrompt(string prompt, DateTime? observedAt = null)
    {
        var now = observedAt ?? DateTime.UtcNow;
        if (!Pending || !StopRequested || prompt.Length == 0 || now < nextDecline) return false;
        PromptDeclineSubmitted = true; nextDecline = now.AddSeconds(5);
        return true;
    }
    internal void ObserveDeclinedPrompt(bool promptVisible, bool queueIdle)
    {
        if (Pending && (PromptDeclineSubmitted || UserDeclineObserved) && !promptVisible && queueIdle)
            Outcome = BeastmasterEntryOutcome.Withdrawn;
    }
    internal bool TryCancel(bool exactQueue, DateTime? observedAt = null)
    {
        var now = observedAt ?? DateTime.UtcNow;
        if (!Pending || !StopRequested || !exactQueue || now < nextCancel) return false;
        CancelSubmitted = true; nextCancel = now.AddSeconds(5);
        return true;
    }
    internal void ObserveSession(bool loggedIn, bool characterLoaded, ulong observedCharacter)
    {
        if (!loggedIn || characterLoaded && observedCharacter != 0 && observedCharacter != Character)
            Outcome = BeastmasterEntryOutcome.SessionEnded;
    }
    internal void ObserveEntry(uint territory)
    {
        if (Pending && territory == 1338 + Board) Outcome = BeastmasterEntryOutcome.Entered;
    }
    internal void ObserveWithdrawal(bool exactQueue, bool sameCharacter)
    {
        // A matching receive-side withdrawal is evidence even after Commence was submitted.
        if (Pending && exactQueue && sameCharacter)
            Outcome = BeastmasterEntryOutcome.Withdrawn;
    }
}

internal static class BeastmasterWeeklyPolicy
{
    // Current public ContentsNote sheet: Beastmaster category 11, menu order 1-5.
    internal static readonly uint[] ObjectiveRows = [89, 90, 91, 92, 93];
    internal static readonly uint[] UnlockQuests = [71030, 71032, 71034, 71036, 71037];
    internal static readonly string[] ObjectiveNames = ["Soul Survivor I", "Soul Survivor II", "Soul Survivor III", "Master Class I", "Master Class II"];

    internal static bool TryReadCompletion(ReadOnlySpan<byte> flags, int bitCount, uint rowId, out bool complete)
    {
        complete = false;
        if (rowId == 0 || rowId > bitCount || bitCount <= 0 || bitCount > flags.Length * 8) return false;
        var index = (int)rowId - 1;
        complete = (flags[index / 8] & (1 << (index % 8))) != 0;
        return true;
    }

    internal static BeastmasterWeeklyObjective? Next(IEnumerable<BeastmasterWeeklyObjective> objectives, ISet<uint> attempted)
        => objectives.FirstOrDefault(row => row.State == BeastmasterWeeklyState.Unfinished && !attempted.Contains(row.Board));

    internal static IReadOnlyList<BeastmasterWeeklyObjective> SelectedObjectives(BeastmasterSettings settings,
        IEnumerable<BeastmasterWeeklyObjective> objectives)
        => objectives.Where(row => row.Board is >= 1 and <= 5 &&
            (settings.WeeklyBoards?.Contains(row.Board) ?? row.Unlocked)).ToArray();

    internal static bool ConfirmsCredit(bool fresh, ulong observedCharacter, ulong owner, long observedReset,
        long runReset, bool initiallyComplete, bool completeNow, bool resultObserved, bool outsideDuty)
        => fresh && owner != 0 && observedCharacter == owner && runReset > 0 && observedReset == runReset &&
           !initiallyComplete && completeNow && resultObserved && outsideDuty;

    internal static BeastmasterSettings CopySettings(BeastmasterSettings source) => new()
    {
        Goal = BeastmasterRunGoal.FixedRuns, Board = source.Board, RunCount = 1,
        WeeklyBoards = source.WeeklyBoards == null ? null : [..source.WeeklyBoards],
        CompletionCommand = string.Empty, Degree = source.Degree, TeamMode = source.TeamMode,
        LevelingMode = source.LevelingMode, CustomTeam = [..source.CustomTeam],
        ShopLists = source.ShopLists.Select(list => list.Clone()).ToList(), ShopListIndex = source.ShopListIndex,
        ShopGearOrder = [..source.ShopGearOrder], BeastTierLists = source.BeastTierLists.Select(list => list.Clone()).ToList(),
        BeastTierListIndex = source.BeastTierListIndex, FightPicks = source.FightPicks, Loot = source.Loot,
        Treasure = source.Treasure, Shop = source.Shop, Rest = source.Rest, Items = source.Items,
        RespectGearRequirements = source.RespectGearRequirements, RecommendedTeamCap = source.RecommendedTeamCap,
        ClassificationLock = source.ClassificationLock,
    };
}
