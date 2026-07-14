using System;
using VERMAXION.Models;
using Xunit;

namespace VERMAXION.Tests;

public sealed class DadSelectionResultRulesTests
{
    [Theory]
    [InlineData(DadSchedulerPresetPhase.Skipped, true, DadSelectionExecutionState.Skipped)]
    [InlineData(DadSchedulerPresetPhase.StartedPlanner, true, DadSelectionExecutionState.Running)]
    [InlineData(DadSchedulerPresetPhase.Blocked, false, DadSelectionExecutionState.Failed)]
    [InlineData(DadSchedulerPresetPhase.Cancelled, false, DadSelectionExecutionState.Cancelled)]
    public void MapsExactSchedulerTerminalResults(
        DadSchedulerPresetPhase phase,
        bool success,
        DadSelectionExecutionState expected)
        => Assert.Equal(expected, DadSelectionResultRules.FromSchedulerPhase(phase, success));

    [Theory]
    [InlineData(DadScheduleRunStatus.Running, 0, 0, DadSelectionExecutionState.Running)]
    [InlineData(DadScheduleRunStatus.Completed, 2, 0, DadSelectionExecutionState.Completed)]
    [InlineData(DadScheduleRunStatus.Completed, 2, 2, DadSelectionExecutionState.Skipped)]
    [InlineData(DadScheduleRunStatus.Blocked, 1, 0, DadSelectionExecutionState.Failed)]
    public void MapsScheduleCompletionAndAllSkipped(
        DadScheduleRunStatus status,
        int completed,
        int skipped,
        DadSelectionExecutionState expected)
        => Assert.Equal(expected, DadSelectionResultRules.FromSchedule(status, completed, skipped));

    [Fact]
    public void CharacterConfigClonePreservesStableDadSelectionWithoutMigratingLegacyFields()
    {
        var source = new CharacterConfig
        {
            NagYourDadSelectionKind = DadSelectionKind.Schedule,
            NagYourDadSelectionId = "schedule-id",
            NagYourDadSelectionDisplayName = "Nightly",
            NagYourDadDungeonName = "legacy",
        };

        var clone = source.Clone();
        Assert.Equal(DadSelectionKind.Schedule, clone.NagYourDadSelectionKind);
        Assert.Equal("schedule-id", clone.NagYourDadSelectionId);
        Assert.Equal("Nightly", clone.NagYourDadSelectionDisplayName);
        Assert.Equal("legacy", clone.NagYourDadDungeonName);
    }

    [Theory]
    [InlineData(DadSelectionKind.Preset, "group-id", DadSelectionSubmissionRules.StartPresetEndpoint)]
    [InlineData(DadSelectionKind.Schedule, "schedule-id", DadSelectionSubmissionRules.StartScheduleEndpoint)]
    public void SubmissionUsesExactStableIdAndVermaxionOperationTag(
        DadSelectionKind kind,
        string stableId,
        string expectedEndpoint)
    {
        var prepared = DadSelectionSubmissionRules.TryPrepare(
            kind,
            $"  {stableId}  ",
            "token-123",
            out var exactId,
            out var requestedBy,
            out var endpoint,
            out var rejection);

        Assert.True(prepared, rejection);
        Assert.Equal(stableId, exactId);
        Assert.Equal("VERMAXION:token-123", requestedBy);
        Assert.Equal(expectedEndpoint, endpoint);
    }

    [Fact]
    public void MissingStablePresetIdIsRejectedWithoutDadFallback()
    {
        var prepared = DadSelectionSubmissionRules.TryPrepare(
            DadSelectionKind.Preset,
            "   ",
            "token",
            out var exactId,
            out _,
            out var endpoint,
            out var rejection);

        Assert.False(prepared);
        Assert.Empty(exactId);
        Assert.Equal(DadSelectionSubmissionRules.StartPresetEndpoint, endpoint);
        Assert.Contains("nothing was submitted", rejection, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PresetAcceptanceRequiresExactVermaxionOwnerAndSchedulerRecord()
    {
        Assert.True(DadSelectionSubmissionRules.IsPresetAccepted(
            DadRunStatus.Queued,
            "VERMAXION:token",
            "VERMAXION:token",
            "job-id"));
        Assert.False(DadSelectionSubmissionRules.IsPresetAccepted(
            DadRunStatus.Queued,
            "VERMAXION:token",
            "crew-ui",
            "job-id"));
        Assert.False(DadSelectionSubmissionRules.IsPresetAccepted(
            DadRunStatus.Queued,
            "VERMAXION:token",
            "VERMAXION:token",
            string.Empty));
    }

    [Fact]
    public void ExistingCrewUiScheduleCannotBeAdoptedByVermaxion()
    {
        Assert.False(DadSelectionSubmissionRules.IsScheduleAccepted(
            DadScheduleRunStatus.Running,
            "schedule-id",
            "schedule-id",
            "VERMAXION:new-token",
            "crew-ui",
            "existing-run"));
        Assert.True(DadSelectionSubmissionRules.IsScheduleAccepted(
            DadScheduleRunStatus.Running,
            "schedule-id",
            "schedule-id",
            "VERMAXION:new-token",
            "VERMAXION:new-token",
            "new-run"));
    }

    [Fact]
    public void StatusSurfacesSubmittedIdAndDadAcceptanceResponse()
    {
        var execution = new DadSelectionExecution
        {
            Kind = DadSelectionKind.Preset,
            SelectionId = "group-id",
            Endpoint = DadSelectionSubmissionRules.StartPresetEndpoint,
            RequestedBy = "VERMAXION:token",
            SubmissionAccepted = true,
            DadResponseStatus = DadRunStatus.Queued.ToString(),
            State = DadSelectionExecutionState.Running,
            Summary = "Scheduler accepted preset.",
        };

        Assert.Contains("id=group-id", execution.StatusText, StringComparison.Ordinal);
        Assert.Contains("requestedBy=VERMAXION:token", execution.StatusText, StringComparison.Ordinal);
        Assert.Contains("DAD accepted (Queued)", execution.StatusText, StringComparison.Ordinal);
    }
}
