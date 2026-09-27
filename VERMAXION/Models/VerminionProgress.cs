using System;
using System.Linq;

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

/// <summary>One reserved purchase. A reload must reconcile it, never submit it again.</summary>
public sealed record VerminionPurchase(uint ItemId, ushort MinionId, uint Gil, uint Mgp,
    uint GilBefore, uint MgpBefore, uint ItemsBefore);

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
    public ulong GilSpent { get; set; }
    public ulong MgpSpent { get; set; }
    public VerminionPurchase? PendingPurchase { get; set; }
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
        if (duty is < 552 or > 579 || PendingMatch != 0) return 0;
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
    public bool WeeklyGoalReached(VerminionMode mode, int victoryTarget, DateTime weekStartUtc) =>
        !CampaignRequested && WeekStartUtc == weekStartUtc && mode != VerminionMode.CpuRewards && Remaining(mode, victoryTarget) == 0;

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

    public bool CanSpend(uint gil, uint mgp, uint gilCap, uint mgpCap) =>
        PendingPurchase == null && GilSpent <= gilCap && MgpSpent <= mgpCap &&
        gil <= gilCap - GilSpent && mgp <= mgpCap - MgpSpent;

    public static bool PreservesGilReserve(uint balance, uint price, uint reserve) =>
        price == 0 || balance >= reserve && price <= balance - reserve;

    public bool ReservePurchase(uint itemId, ushort minionId, uint gil, uint mgp,
        uint gilBefore, uint mgpBefore, uint itemsBefore, bool alreadyOwned, uint gilCap, uint mgpCap, uint gilReserve = 0)
    {
        if (itemId == 0 || minionId == 0 || alreadyOwned || itemsBefore != 0 ||
            (gil == 0) == (mgp == 0) || gil > gilBefore || mgp > mgpBefore ||
            !PreservesGilReserve(gilBefore, gil, gilReserve) ||
            !CanSpend(gil, mgp, gilCap, mgpCap)) return false;
        PendingPurchase = new(itemId, minionId, gil, mgp, gilBefore, mgpBefore, itemsBefore);
        return true;
    }

    public bool ConfirmPurchase(uint gilNow, uint mgpNow, uint itemsNow, bool nowOwned)
    {
        var pending = PendingPurchase;
        if (pending == null || pending.Gil > pending.GilBefore || pending.Mgp > pending.MgpBefore ||
            (itemsNow <= pending.ItemsBefore && !nowOwned) ||
            gilNow != pending.GilBefore - pending.Gil || mgpNow != pending.MgpBefore - pending.Mgp)
            return false;
        // Currency alone or item ownership alone cannot prove this transaction.
        GilSpent = checked(GilSpent + pending.Gil);
        MgpSpent = checked(MgpSpent + pending.Mgp);
        PendingPurchase = null;
        return true;
    }
}
