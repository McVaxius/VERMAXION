using System;
using VERMAXION.Beastmaster;
using Xunit;

namespace VERMAXION.Tests;

public sealed class BeastmasterReadRecoveryTests
{
    private static readonly DateTime Start = new(2026, 10, 18, 16, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(274, 273)] // Notebook magic resistance, the last detail field.
    [InlineData(262, 261)] // Notebook EXP.
    [InlineData(73, 72)] // Result beast count.
    public void LastAvailableNativeFieldIsReadableButOneShortIsRejected(int available, int offset)
    {
        Assert.True(BeastmasterReadBounds.Contains(available, offset));
        Assert.False(BeastmasterReadBounds.Contains(available - 1, offset));
    }

    [Theory]
    [InlineData(224, 24, 25u, 8, 8)] // All notebook entry fields.
    [InlineData(237, 6, 3u, 77, 77)] // Three complete party entries.
    [InlineData(140, 73, 3u, 1, 65)] // Result rows include the parallel new-rank column.
    public void DynamicRowsRequireTheirFinalFieldInsteadOfOnlyTheirStartingOffset(
        int available, int offset, uint count, int stride, int width)
    {
        Assert.True(BeastmasterReadBounds.Contains(available, offset, count, stride, width));
        Assert.False(BeastmasterReadBounds.Contains(available - 1, offset, count, stride, width));
    }

    [Fact]
    public void EmptyRowsDoNotRequireAnElementButTheirStartMustBeWithinTheBuffer()
    {
        Assert.True(BeastmasterReadBounds.Contains(24, 24, 0));
        Assert.False(BeastmasterReadBounds.Contains(23, 24, 0));
    }

    [Theory]
    [InlineData(-1, 0, 1, 1)]
    [InlineData(10, -1, 1, 1)]
    [InlineData(10, 0, 0, 1)]
    [InlineData(10, 0, -1, 1)]
    [InlineData(10, 0, 1, 0)]
    [InlineData(10, 0, 1, -1)]
    public void InvalidNativeBoundsAreRejected(int available, int offset, int stride, int width)
    {
        Assert.False(BeastmasterReadBounds.Contains(available, offset, 1, stride, width));
    }

    [Fact]
    public void CorruptUnsignedCountsCannotWrapIntoAReadableRange()
    {
        Assert.False(BeastmasterReadBounds.Contains(int.MaxValue, 73, uint.MaxValue, 1, 65));
        Assert.False(BeastmasterReadBounds.Contains(int.MaxValue, 6, uint.MaxValue, int.MaxValue, 77));
        Assert.False(BeastmasterReadBounds.Contains(int.MaxValue, int.MaxValue));
    }

    [Fact]
    public void MenuSubmissionRetriesAfterFiveActiveSecondsWithoutPermanentlyConsumingAReusedAddress()
    {
        var submission = new BeastmasterMenuSubmission((nint)101, Start);
        Assert.True(submission.IsPending((nint)101, Start));
        Assert.True(submission.IsPending((nint)101, Start.AddSeconds(5).AddTicks(-1)));
        Assert.False(submission.IsPending((nint)101, Start.AddSeconds(5)));
        Assert.False(submission.IsPending((nint)101, Start.AddMinutes(1)));
        Assert.False(submission.IsPending((nint)102, Start));
        Assert.False(default(BeastmasterMenuSubmission).IsPending(0, Start));

        // A retry or a new logical screen at the same address gets its own delay.
        submission = new((nint)101, Start.AddSeconds(5));
        Assert.True(submission.IsPending((nint)101, Start.AddSeconds(5)));
        Assert.True(submission.IsPending((nint)101, Start.AddSeconds(10).AddTicks(-1)));
        Assert.False(submission.IsPending((nint)101, Start.AddSeconds(10)));

        // Pause after four seconds; twenty paused seconds leave one second to wait.
        submission = submission.ResumeAfterPause(TimeSpan.FromSeconds(20));
        Assert.True(submission.IsPending((nint)101, Start.AddSeconds(29)));
        Assert.True(submission.IsPending((nint)101, Start.AddSeconds(30).AddTicks(-1)));
        Assert.False(submission.IsPending((nint)101, Start.AddSeconds(30)));
    }

    [Fact]
    public void RecoveryBecomesReadyAtExactlyFiveSeconds()
    {
        var recovery = new BeastmasterReadRecovery();
        Assert.Equal(TimeSpan.FromSeconds(5), BeastmasterReadRecovery.Delay);
        Assert.False(recovery.Ready("XBMResult", (nint)101, Start));
        Assert.False(recovery.Ready("XBMResult", (nint)101, Start.AddSeconds(5).AddTicks(-1)));
        Assert.True(recovery.Ready("XBMResult", (nint)101, Start.AddSeconds(5)));
    }

    [Fact]
    public void SubmittedRecoveryCannotReplayForTheSameNativeScreen()
    {
        var recovery = new BeastmasterReadRecovery();
        recovery.Ready("XBMResult", (nint)101, Start);
        Assert.True(recovery.Ready("XBMResult", (nint)101, Start.AddSeconds(5)));
        recovery.Submitted();
        Assert.False(recovery.Ready("XBMResult", (nint)101, Start.AddSeconds(5)));
        Assert.False(recovery.Ready("XBMResult", (nint)101, Start.AddMinutes(1)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReplacedPointerOrWindowStartsItsOwnFiveSecondWait(bool replaceWindow)
    {
        var recovery = new BeastmasterReadRecovery();
        recovery.Ready("XBMResult", (nint)101, Start);
        Assert.True(recovery.Ready("XBMResult", (nint)101, Start.AddSeconds(5)));
        recovery.Submitted();

        var window = replaceWindow ? "XBMContentsTreasure" : "XBMResult";
        var address = replaceWindow ? (nint)101 : (nint)102;
        Assert.False(recovery.Ready(window, address, Start.AddSeconds(10)));
        Assert.False(recovery.Ready(window, address, Start.AddSeconds(15).AddTicks(-1)));
        Assert.True(recovery.Ready(window, address, Start.AddSeconds(15)));
    }

    [Fact]
    public void PauseTimeDoesNotConsumeTheRecoveryWait()
    {
        var recovery = new BeastmasterReadRecovery();
        recovery.Ready("XBMResult", (nint)101, Start);
        Assert.False(recovery.Ready("XBMResult", (nint)101, Start.AddSeconds(4)));
        recovery.ResumeAfterPause(TimeSpan.FromSeconds(20));
        Assert.False(recovery.Ready("XBMResult", (nint)101, Start.AddSeconds(24)));
        Assert.False(recovery.Ready("XBMResult", (nint)101, Start.AddSeconds(25).AddTicks(-1)));
        Assert.True(recovery.Ready("XBMResult", (nint)101, Start.AddSeconds(25)));
    }

    [Fact]
    public void ResetAfterSuccessfulObservationAllowsAFreshWaitForTheSameScreen()
    {
        var recovery = new BeastmasterReadRecovery();
        recovery.Ready("XBMResult", (nint)101, Start);
        Assert.True(recovery.Ready("XBMResult", (nint)101, Start.AddSeconds(5)));
        recovery.Submitted();
        recovery.Reset();
        recovery.ResumeAfterPause(TimeSpan.FromSeconds(20));
        Assert.False(recovery.Ready("XBMResult", (nint)101, Start.AddSeconds(10)));
        Assert.False(recovery.Ready("XBMResult", (nint)101, Start.AddSeconds(15).AddTicks(-1)));
        Assert.True(recovery.Ready("XBMResult", (nint)101, Start.AddSeconds(15)));
    }

    [Fact]
    public void MissingResultRanksPreserveKnownRanksAndOtherBeastsWithoutCompletingTrainingFromStaleData()
    {
        var first = new CrucibleFamiliar(13) { Rank = 5, Hp = 120, Strength = 18, Exp = "40", ObservedAtUtc = Start };
        var second = new CrucibleFamiliar(20) { Rank = 7, Exp = "0", ObservedAtUtc = Start };
        var reserve = new CrucibleFamiliar(27) { Rank = 6, Exp = "75", ObservedAtUtc = Start };
        var data = new BeastmasterCharacterData
        {
            Familiars = { [13] = first with { }, [20] = second with { }, [27] = reserve with { } },
            Owned = [13, 20, 27], Team = [13, 20],
            OwnershipObservedAtUtc = Start, RanksObservedAtUtc = Start,
        };
        uint[] owned = [13, 20, 27];
        Assert.True(BeastmasterSelection.TrainingComplete(owned, data.Familiars, 5));
        Assert.True(BeastmasterSelection.TrainingCompletionConfirmed(owned, data.Familiars, 5, true, owned));

        BeastmasterReadBounds.MarkResultsUnconfirmed(data, [13, 20, 13, 999]);

        Assert.Equal(first with { Exp = string.Empty, ObservedAtUtc = DateTime.MinValue }, data.Familiars[13]);
        Assert.Equal(second with { Exp = string.Empty, ObservedAtUtc = DateTime.MinValue }, data.Familiars[20]);
        Assert.Equal(reserve, data.Familiars[27]);
        Assert.Equal(3, data.Familiars.Count);
        Assert.Equal(DateTime.MinValue, data.RanksObservedAtUtc);
        Assert.Equal(Start, data.OwnershipObservedAtUtc);
        Assert.True(data.Owned.SetEquals(owned));
        Assert.Equal(new uint[] { 13, 20 }, data.Team);
        Assert.False(BeastmasterSelection.TrainingComplete(owned, data.Familiars, 5));
        Assert.False(BeastmasterSelection.TrainingCompletionConfirmed(owned, data.Familiars, 5, true, owned));
    }
}
