using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace VERMAXION.Models;

public enum VerminionMode
{
    Participation,
    WinTarget,
    CpuRewards,
}

public enum VerminionBattleOutcome
{
    Unknown,
    Defeat,
    Victory,
}

public static class VerminionDutyRules
{
    public static bool IsCpu(uint duty) => duty is >= 552 and <= 579;
    public static int ChallengeStage(uint duty) => duty is >= 552 and <= 575 ? (int)duty - 551 : 0;

    // Zero means an empty slot. Non-Regular entries must be mapped to MaxValue,
    // never to zero. The caller supplies poppedDuty only for Ready/Accepted queues.
    public static bool OnlyExpectedQueue(uint expectedDuty, ReadOnlySpan<uint> queuedDuties, uint poppedDuty)
    {
        if (!IsCpu(expectedDuty) || poppedDuty != 0 && poppedDuty != expectedDuty) return false;
        var found = false;
        foreach (var duty in queuedDuties)
        {
            if (duty == 0) continue;
            if (duty != expectedDuty) return false;
            found = true;
        }
        return found || poppedDuty == expectedDuty;
    }
}

// A displayed observation, never proof of registration, a reward or a victory.
public sealed record VerminionTournamentInfo(string Title, string Notice, int? Matches, int? Wins, int? Points)
{
    public static VerminionTournamentInfo? FromDisplay(string title, string notice,
        bool countersVisible, int matches, int wins, int points, bool receivingData = false)
    {
        if (receivingData || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(notice) ||
            title.Contains("Receiving data", StringComparison.OrdinalIgnoreCase) ||
            notice.Contains("Receiving data", StringComparison.OrdinalIgnoreCase)) return null;
        if (!countersVisible) return new(title, notice, null, null, null);
        if (matches is < 0 or > 15 || wins < 0 || wins > matches || points < 0) return null;
        return new(title, notice, matches, wins, points);
    }

    public string Summary => $"{Title}: {Notice}" + (Matches.HasValue
        ? $" Displayed counters: {Matches}/15 matches, {Wins} wins, {Points} points; {15 - Matches.Value} tournament matches remaining."
        : " Tournament counters are not displayed; registration and allowance are unknown.");
}

public enum VerminionTournamentMenu { Unknown, Upcoming, Current }
public enum VerminionTournamentDialogue
{
    Unknown, Greeting, Rules, Registered, Rejected, EntriesClosed,
    PrizeRankings, PrizeOffer, PrizeThanks, PrizeExpired, PrizeNone, PrizeRejected,
}

public static class VerminionTournamentRewardRules
{
    public const string Confirmation = "Accept your prize?";
    public const uint MgpMaximum = 9_999_999;
    private const string RankingsSuffix = " has come to a close! Let's have a look at the final rankings, shall we?";

    public static bool IsTournamentTitle(string? title) => !string.IsNullOrEmpty(title) && title.Length < 100 && Regex.IsMatch(title,
        "\\AThe [1-9][0-9]*(?:st|nd|rd|th) Lord of Verminion Tournament\\z", RegexOptions.CultureInvariant);

    public static bool TryReadTitle(string text, out string title)
    {
        title = text.EndsWith(RankingsSuffix, StringComparison.Ordinal) ? text[..^RankingsSuffix.Length] : string.Empty;
        return IsTournamentTitle(title);
    }

    // GoldSaucerTalk441/442. Read the total, never the placement bonus.
    public static bool TryReadOffer(string text, out uint mgp)
    {
        mgp = 0;
        if (text.Length > 2048) return false;
        const string amount = "(?<mgp>(?:[1-9][0-9]{0,2}(?:,[0-9]{3})+|[1-9][0-9]*))";
        var match = Regex.Match(text,
            "^Congratulations, .+! You've finished .+ on the board! Along with an added bonus of [0-9,]+ points, your total winnings come to\\s*" + amount + "\\s*MGP!$",
            RegexOptions.CultureInvariant);
        if (!match.Success)
            match = Regex.Match(text,
                "^\\.\\.\\.Ah, I'm afraid you didn't quite earn a winning rank this time around\\. You are, however, eligible to receive a participation gift of\\s*" + amount + "\\s*MGP\\.$",
                RegexOptions.CultureInvariant);
        return match.Success && uint.TryParse(match.Groups["mgp"].Value, NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture, out mgp) && mgp <= MgpMaximum;
    }

    public static bool CanReceive(uint mgp, uint before) => mgp > 0 && (ulong)before + mgp <= MgpMaximum;
}

// Saved immediately before prize acceptance; an interrupted intent is never resubmitted.
public sealed record VerminionTournamentRewardClaim(string Title, uint Mgp, uint MgpBefore,
    DateTime RequestedUtc, bool Acknowledged = false, DateTime ConfirmedUtc = default);

public static class VerminionTournamentEntryRules
{
    public const uint CpuDuty = 579;
    public const string Enter = "Enter the current tournament.";
    public const string Confirmation = "Enter the current tournament?";

    // GoldSaucerTalk 422/427/428/434/435/469. The NPC and owned interaction are
    // checked separately by the coordinator; a menu title alone is insufficient.
    public static VerminionTournamentMenu Menu(string title, string[] entries) =>
        title != "What will you do?" ? VerminionTournamentMenu.Unknown :
        entries.SequenceEqual(new[] { "Ask about the upcoming tournament.", "Ask about Lord of Verminion tournaments.", "Nothing." })
            ? VerminionTournamentMenu.Upcoming :
        entries.SequenceEqual(new[] { Enter, "Ask about the current tournament.", "Ask about Lord of Verminion tournaments.", "Nothing." })
            ? VerminionTournamentMenu.Current : VerminionTournamentMenu.Unknown;

    public static bool CanConfirm(string prompt, bool entryRequested, bool confirmed) =>
        entryRequested && !confirmed && prompt == Confirmation;

    public static VerminionTournamentDialogue Dialogue(string text)
    {
        if (VerminionTournamentRewardRules.TryReadTitle(text, out _)) return VerminionTournamentDialogue.PrizeRankings;
        if (VerminionTournamentRewardRules.TryReadOffer(text, out _)) return VerminionTournamentDialogue.PrizeOffer;
        if (text.StartsWith("Thank you for entering, ", StringComparison.Ordinal) &&
            text.EndsWith(", and I hope to see you return for our next Lord of Verminion tournament!", StringComparison.Ordinal))
            return VerminionTournamentDialogue.PrizeThanks;
        if (text == "Oh dear... The reward period for the tournament in question seems to have expired. Please remember to collect any prizes before the beginning of the next scheduled tournament!")
            return VerminionTournamentDialogue.PrizeExpired;
        if (text.StartsWith("Unfortunately, you didn't qualify for any prizes. But that just gives you a goal to aim for, right, ", StringComparison.Ordinal) &&
            text.EndsWith('?')) return VerminionTournamentDialogue.PrizeNone;
        if (text == "You cannot accept your prize at this time. The gaming gods decree that it is time you took a break.")
            return VerminionTournamentDialogue.PrizeRejected;
        if (text.Contains("You are now officially registered for the ", StringComparison.Ordinal) &&
            text.Contains("Lord of Verminion Tournament.", StringComparison.Ordinal) ||
            text == "...Oh. You seem to have registered for this tournament already.")
            return VerminionTournamentDialogue.Registered;
        if (text.StartsWith("Ah, another challenger! Just so there are no surprises,", StringComparison.Ordinal) &&
            text.EndsWith("Shall I sign you up, then?", StringComparison.Ordinal))
            return VerminionTournamentDialogue.Rules;
        if (text.StartsWith("Greetings, ", StringComparison.Ordinal) &&
            (text.Contains("Lord of Verminion tournament needs!", StringComparison.Ordinal) ||
             text.Contains("Lord of Verminion Tournament is currently underway!", StringComparison.Ordinal)))
            return VerminionTournamentDialogue.Greeting;
        // GoldSaucerTalk 470 ends the interaction. Other rejections do not
        // establish that the tournament is closed or the allowance is exhausted.
        if (text.StartsWith("I'm terribly sorry, ", StringComparison.Ordinal) &&
            text.EndsWith("but we are no longer accepting entries for this tournament. Not to worry, though! There'll be another competition starting soon!", StringComparison.Ordinal))
            return VerminionTournamentDialogue.EntriesClosed;
        if (text.Contains("I cannot register your entry at this time.", StringComparison.Ordinal) ||
            text.Contains("I cannot register you for tournaments until", StringComparison.Ordinal) ||
            text.Contains("I'm afraid you don't meet the prerequisite", StringComparison.Ordinal))
            return VerminionTournamentDialogue.Rejected;
        return VerminionTournamentDialogue.Unknown;
    }

    public static bool ConfirmsRegistration(string text, string? expectedTitle) =>
        VerminionTournamentRewardRules.IsTournamentTitle(expectedTitle) &&
        (text == "...Oh. You seem to have registered for this tournament already." ||
         text.StartsWith("There we are, ", StringComparison.Ordinal) &&
         text.EndsWith($"! You are now officially registered for the {expectedTitle![4..]}. Best of luck!", StringComparison.Ordinal));

    public static bool CanPrepare(string? registeredTitle, VerminionTournamentInfo? info) =>
        !string.IsNullOrEmpty(registeredTitle) && info is { Matches: >= 0 and < 15 } && registeredTitle == info.Title &&
        info.Notice.StartsWith("Matches available until ", StringComparison.Ordinal);

    public static bool AllowanceExhausted(string? registeredTitle, VerminionTournamentInfo? info) =>
        !string.IsNullOrEmpty(registeredTitle) && info is { Matches: 15 } && registeredTitle == info.Title &&
        info.Notice.StartsWith("Matches available until ", StringComparison.Ordinal);

    public static bool OnlyCpuSelection(uint highlightedDuty, uint[] selectedDuties) =>
        highlightedDuty == CpuDuty && selectedDuties.SequenceEqual(new[] { CpuDuty });
}

/// <summary>One reserved purchase. A reload must reconcile it, never submit it again.</summary>
public sealed record VerminionPurchase(uint ItemId, ushort MinionId, uint Gil, uint Mgp,
    uint GilBefore, uint MgpBefore, uint ItemsBefore, uint Certificates = 0, uint CertificatesBefore = 0);

public sealed record VerminionMinionAcquisition(string OperationId, uint ItemId, ushort MinionId,
    bool DispatchAttempted = false, bool Standalone = false);

/// <summary>A native quest handoff, persisted before submission. Reloads only observe it.</summary>
public sealed record VerminionQuestAcquisition(ulong Owner, ushort MinionId, string Provider,
    ushort[] Quests, bool DispatchAttempted = false, bool CancellationRequested = false, bool OwnershipReleased = false)
{
    public ushort RewardQuest => Quests.LastOrDefault();
    public bool OwnsQuest(ulong character, string? quest) => character != 0 && character == Owner &&
        DispatchAttempted && !OwnershipReleased && ushort.TryParse(quest, out var id) && Quests.Contains(id);
    public VerminionQuestAcquisition Copy() => this with { Quests = (ushort[])Quests.Clone() };
}

/// <summary>Character-owned facts. Admission and result evidence are supplied by the game interaction layer.</summary>
public sealed class VerminionProgress
{
    public DateTime WeekStartUtc { get; set; }
    public int WeeklyMatches { get; set; }
    public int WeeklyWins { get; set; }
    public uint ClearedChallengeMask { get; set; }
    // Display history only. Registration and entry always use fresh game observations.
    public VerminionTournamentInfo? LastTournamentInfo { get; set; }
    public DateTime TournamentObservedUtc { get; set; }
    public VerminionTournamentRewardClaim? PendingTournamentReward { get; set; }
    public VerminionTournamentRewardClaim? LastTournamentReward { get; set; }
    public ulong GilSpent { get; set; }
    public ulong MgpSpent { get; set; }
    public ulong CertificatesSpent { get; set; }
    public VerminionPurchase? PendingPurchase { get; set; }
    public VerminionMinionAcquisition? MinionAcquisition { get; set; }
    public long MatchSequence { get; set; }
    public long PendingMatch { get; set; }
    public uint PendingDuty { get; set; }
    public DateTime PendingWeekStartUtc { get; set; }
    public int PendingMatchesBeforeAdmission { get; set; }
    public VerminionMode? RunMode { get; set; }
    public DateTime RunWeekStartUtc { get; set; }
    public int RunVictoryTarget { get; set; }
    public int RunAttempts { get; set; }
    public int RunAttemptLimit { get; set; }
    public int ConsecutiveLosses { get; set; }
    public ushort UnlockQuestId { get; set; }
    public string UnlockQuestProvider { get; set; } = string.Empty;
    public bool UnlockPriorityInserted { get; set; }
    public VerminionQuestAcquisition? QuestAcquisition { get; set; }
    public int SelectedChallengeStage { get; set; }
    public bool CampaignRequested { get; set; }
    public int CampaignStage { get; set; }
    public int CampaignStageAttempts { get; set; }

    public int NextUnclearedChallenge
    {
        get
        {
            for (var stage = 1; stage <= 24; ++stage)
                if ((ClearedChallengeMask & (1u << (stage - 1))) == 0) return stage;
            return 25;
        }
    }

    public bool CampaignStageLimitReached(int stage) => stage is >= 1 and <= 24 &&
        (ClearedChallengeMask & (1u << (stage - 1))) == 0 && CampaignStage == stage && CampaignStageAttempts >= 3;

    public bool BeginTournamentReward(string title, uint mgp, uint mgpBefore, DateTime nowUtc)
    {
        if (PendingTournamentReward != null || PendingMatch != 0 || PendingPurchase != null || MinionAcquisition != null ||
            QuestAcquisition != null || LastTournamentReward?.Title == title || nowUtc == default ||
            !VerminionTournamentRewardRules.IsTournamentTitle(title) || !VerminionTournamentRewardRules.CanReceive(mgp, mgpBefore))
            return false;
        PendingTournamentReward = new(title, mgp, mgpBefore, nowUtc);
        return true;
    }

    public bool AcknowledgeTournamentReward(string title)
    {
        if (PendingTournamentReward is not { } pending || pending.Title != title) return false;
        PendingTournamentReward = pending with { Acknowledged = true };
        return true;
    }

    public bool ConfirmTournamentReward(uint mgpNow, DateTime nowUtc)
    {
        if (PendingTournamentReward is not { Acknowledged: true } pending || pending.RequestedUtc == default ||
            pending.ConfirmedUtc != default || nowUtc < pending.RequestedUtc ||
            !VerminionTournamentRewardRules.IsTournamentTitle(pending.Title) ||
            !VerminionTournamentRewardRules.CanReceive(pending.Mgp, pending.MgpBefore) ||
            mgpNow != pending.MgpBefore + pending.Mgp) return false;
        LastTournamentReward = pending with { ConfirmedUtc = nowUtc };
        PendingTournamentReward = null;
        return true;
    }

    public bool RejectTournamentReward(uint mgpNow)
    {
        if (PendingTournamentReward is not { Acknowledged: false } pending || mgpNow != pending.MgpBefore) return false;
        PendingTournamentReward = null;
        return true;
    }

    public void EnsureRun(VerminionMode mode, int victoryTarget, bool restart = false)
    {
        victoryTarget = Math.Clamp(victoryTarget, 1, 1000);
        if (!restart && RunMode == mode && RunVictoryTarget == victoryTarget && RunWeekStartUtc == WeekStartUtc) return;
        RunMode = mode;
        RunVictoryTarget = victoryTarget;
        RunWeekStartUtc = WeekStartUtc;
        RunAttempts = PendingMatch != 0 ? 1 : 0;
        RunAttemptLimit = mode == VerminionMode.WinTarget ? 3 * Remaining(mode, victoryTarget) : 0;
        ConsecutiveLosses = 0;
    }

    public bool WinningRunLimitReached => RunMode == VerminionMode.WinTarget &&
        (ConsecutiveLosses >= 3 || RunAttemptLimit > 0 && RunAttempts >= RunAttemptLimit);

    public VerminionProgress Clone()
    {
        var clone = (VerminionProgress)MemberwiseClone();
        clone.QuestAcquisition = QuestAcquisition?.Copy();
        return clone;
    }

    public bool ObserveWeek(DateTime weekStartUtc)
    {
        if (weekStartUtc <= WeekStartUtc) return false;
        WeekStartUtc = weekStartUtc;
        WeeklyMatches = 0;
        WeeklyWins = 0;
        return true;
    }

    public bool ObserveParticipation(DateTime weekStartUtc, int matches)
    {
        if (weekStartUtc < WeekStartUtc || matches < 0) return false;
        var changed = ObserveWeek(weekStartUtc);
        if (matches <= WeeklyMatches) return changed;
        WeeklyMatches = matches;
        return true;
    }

    public long BeginMatch(uint duty)
    {
        if (!VerminionDutyRules.IsCpu(duty) || PendingMatch != 0 || PendingTournamentReward != null) return 0;
        PendingDuty = duty;
        PendingWeekStartUtc = WeekStartUtc;
        PendingMatchesBeforeAdmission = WeeklyMatches;
        PendingMatch = checked(++MatchSequence);
        ++RunAttempts;
        if (CampaignRequested && duty <= 575)
        {
            var stage = (int)duty - 551;
            if (CampaignStage != stage) { CampaignStage = stage; CampaignStageAttempts = 0; }
            ++CampaignStageAttempts;
        }
        return PendingMatch;
    }

    public void AbandonMatch()
    {
        PendingMatch = 0;
        PendingDuty = 0;
        PendingWeekStartUtc = default;
        PendingMatchesBeforeAdmission = 0;
    }

    public bool RecordTutorialCompletion(long match, VerminionBattleOutcome outcome)
    {
        if (match == 0 || match != PendingMatch || PendingDuty != 552 || outcome != VerminionBattleOutcome.Victory)
            return false;
        RecordChallengeClear(1);
        AbandonMatch();
        return true;
    }

    public bool RecordResult(long match, VerminionBattleOutcome outcome, DateTime weekStartUtc)
    {
        if (match == 0 || match != PendingMatch || PendingDuty is < 553 or > 579 ||
            outcome is not (VerminionBattleOutcome.Defeat or VerminionBattleOutcome.Victory) ||
            weekStartUtc < WeekStartUtc) return false;
        ObserveWeek(weekStartUtc);
        // A challenge-log refresh may have already included this result.
        WeeklyMatches = Math.Max(WeeklyMatches,
            PendingWeekStartUtc == weekStartUtc ? PendingMatchesBeforeAdmission + 1 : 1);
        if (outcome == VerminionBattleOutcome.Victory)
        {
            ++WeeklyWins;
            ConsecutiveLosses = 0;
            if (PendingDuty <= 575) RecordChallengeClear((int)PendingDuty - 551);
        }
        else ++ConsecutiveLosses;
        AbandonMatch();
        return true;
    }

    public bool RecordChallengeClear(int stage)
    {
        if (stage is < 1 or > 24) return false;
        var bit = 1u << (stage - 1);
        if ((ClearedChallengeMask & bit) != 0) return false;
        ClearedChallengeMask |= bit;
        return true;
    }

    public bool ObserveAvailableChallenges(int highestAvailableStage)
    {
        if (highestAvailableStage is < 1 or > 24) return false;
        var changed = false;
        // Unlocking the next stage proves the preceding clears. Stage 24 still
        // needs its own positive victory evidence.
        for (var stage = 1; stage < highestAvailableStage; ++stage)
            changed |= RecordChallengeClear(stage);
        return changed;
    }

    public int Remaining(VerminionMode mode, int victoryTarget) => mode == VerminionMode.WinTarget
        ? Math.Max(0, Math.Clamp(victoryTarget, 1, 1000) - WeeklyWins)
        : Math.Max(0, 5 - WeeklyMatches);

    // Tournament opportunities and reward claims are independent of weekly matches.
    public bool WeeklyGoalReached(VerminionMode mode, int victoryTarget, DateTime weekStartUtc, bool tournamentEntryUnavailableThisRun = false) =>
        !CampaignRequested && WeekStartUtc == weekStartUtc &&
        (mode != VerminionMode.CpuRewards || tournamentEntryUnavailableThisRun) && Remaining(mode, victoryTarget) == 0;

    public string Summary(VerminionMode mode, int victoryTarget, DateTime weekStartUtc)
    {
        var matches = WeekStartUtc == weekStartUtc ? WeeklyMatches : 0;
        var wins = WeekStartUtc == weekStartUtc ? WeeklyWins : 0;
        if (CampaignRequested)
            return $"Challenge {Math.Min(NextUnclearedChallenge, 24)}/24; {matches} weekly matches, {wins} wins";
        return mode switch
        {
            VerminionMode.WinTarget => $"{matches} matches, {wins}/{Math.Clamp(victoryTarget, 1, 1000)} wins; {Math.Max(0, Math.Clamp(victoryTarget, 1, 1000) - wins)} wins remaining",
            VerminionMode.CpuRewards => $"{matches} matches, {wins} wins; CPU tournament opportunities pending",
            _ => $"{Math.Min(5, matches)}/5 participation, {wins} wins; {Math.Max(0, 5 - matches)} matches remaining",
        };
    }

    public bool CanSpend(uint gil, uint mgp, uint gilCap, uint mgpCap, uint certificates = 0, uint certificateCap = 0) =>
        PendingPurchase == null && GilSpent <= gilCap && MgpSpent <= mgpCap &&
        gil <= gilCap - GilSpent && mgp <= mgpCap - MgpSpent &&
        (certificates == 0 || CertificatesSpent <= certificateCap && certificates <= certificateCap - CertificatesSpent);

    public static bool PreservesGilReserve(uint balance, uint price, uint reserve) =>
        price == 0 || balance >= reserve && price <= balance - reserve;

    public bool ReservePurchase(uint itemId, ushort minionId, uint gil, uint mgp,
        uint gilBefore, uint mgpBefore, uint itemsBefore, bool alreadyOwned, uint gilCap, uint mgpCap, uint gilReserve = 0,
        uint certificates = 0, uint certificatesBefore = 0, uint certificateCap = 0)
    {
        if (PendingTournamentReward != null || itemId == 0 || minionId == 0 || alreadyOwned || itemsBefore != 0 ||
            (gil > 0 ? 1 : 0) + (mgp > 0 ? 1 : 0) + (certificates > 0 ? 1 : 0) != 1 ||
            gil > gilBefore || mgp > mgpBefore || certificates > certificatesBefore ||
            !PreservesGilReserve(gilBefore, gil, gilReserve) ||
            !CanSpend(gil, mgp, gilCap, mgpCap, certificates, certificateCap)) return false;
        PendingPurchase = new(itemId, minionId, gil, mgp, gilBefore, mgpBefore, itemsBefore, certificates, certificatesBefore);
        return true;
    }

    public bool CanResumeReservedPurchase(uint gilNow, uint mgpNow, uint itemsNow, bool? nowOwned,
        uint certificatesNow, uint gilCap, uint mgpCap, uint certificateCap, uint gilReserve)
    {
        var pending = PendingPurchase;
        return pending != null && MinionAcquisition is { } acquisition &&
            acquisition.ItemId == pending.ItemId && acquisition.MinionId == pending.MinionId &&
            PendingTournamentReward == null && PendingMatch == 0 && nowOwned == false &&
            pending.ItemsBefore == 0 && itemsNow == 0 &&
            gilNow == pending.GilBefore && mgpNow == pending.MgpBefore &&
            (pending.Certificates == 0 || certificatesNow == pending.CertificatesBefore) &&
            (pending.Gil > 0 ? 1 : 0) + (pending.Mgp > 0 ? 1 : 0) + (pending.Certificates > 0 ? 1 : 0) == 1 &&
            pending.Gil <= gilNow && pending.Mgp <= mgpNow && pending.Certificates <= certificatesNow &&
            GilSpent <= gilCap && pending.Gil <= gilCap - GilSpent &&
            MgpSpent <= mgpCap && pending.Mgp <= mgpCap - MgpSpent &&
            (pending.Certificates == 0 || CertificatesSpent <= certificateCap && pending.Certificates <= certificateCap - CertificatesSpent) &&
            PreservesGilReserve(gilNow, pending.Gil, gilReserve);
    }

    public bool ConfirmPurchase(uint gilNow, uint mgpNow, uint itemsNow, bool nowOwned, uint certificatesNow = 0)
    {
        var pending = PendingPurchase;
        if (pending == null || pending.Gil > pending.GilBefore || pending.Mgp > pending.MgpBefore ||
            (itemsNow <= pending.ItemsBefore && !nowOwned) ||
            gilNow != pending.GilBefore - pending.Gil || mgpNow != pending.MgpBefore - pending.Mgp ||
            pending.Certificates > pending.CertificatesBefore ||
            pending.Certificates > 0 && certificatesNow != pending.CertificatesBefore - pending.Certificates)
            return false;
        // Currency alone or item ownership alone cannot prove this transaction.
        GilSpent = checked(GilSpent + pending.Gil);
        MgpSpent = checked(MgpSpent + pending.Mgp);
        CertificatesSpent = checked(CertificatesSpent + pending.Certificates);
        PendingPurchase = null;
        return true;
    }
}
