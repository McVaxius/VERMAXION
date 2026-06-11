#nullable enable

using VERMAXION.Models;
using Xunit;

namespace VERMAXION.Tests;

public sealed class AutomatedPostprocessPolicyTests
{
    [Fact]
    public void UnsafeHenchmanPreflightSkipsEngineAndReleasesAr()
    {
        var readiness = HenchmanTakeoverPolicy.Evaluate(
            loaded: true,
            busyReadSucceeded: true,
            busy: true,
            stateReadSucceeded: true,
            taskName: "AR Wait",
            taskDescription: "Waiting for AR PostProccess");

        var decision = AutomatedPostprocessPolicy.EvaluateHenchmanPreflight(readiness);

        Assert.False(decision.StartEngine);
        Assert.True(decision.FinishPostprocess);
        Assert.Equal(ARPostProcessFinishMode.ReleaseOnly, decision.FinishMode);
        Assert.True(decision.ReleaseAutoRetainerSuppression);
        Assert.Equal(RunOutcome.Skipped, decision.Outcome);
        Assert.Equal("Waiting for Henchman: Henchman is busy with AR Wait: Waiting for AR PostProccess", decision.Summary);
    }

    [Fact]
    public void IdleHenchmanPreflightStartsEngineNormally()
    {
        var readiness = HenchmanTakeoverPolicy.Evaluate(
            loaded: true,
            busyReadSucceeded: true,
            busy: false,
            stateReadSucceeded: false,
            taskName: null,
            taskDescription: null);

        var decision = AutomatedPostprocessPolicy.EvaluateHenchmanPreflight(readiness);

        Assert.True(decision.StartEngine);
        Assert.False(decision.FinishPostprocess);
        Assert.False(decision.ReleaseAutoRetainerSuppression);
        Assert.Equal(RunOutcome.None, decision.Outcome);
    }

    [Fact]
    public void SafeOceanWaitPreflightStartsEngineNormally()
    {
        var readiness = HenchmanTakeoverPolicy.Evaluate(
            loaded: true,
            busyReadSucceeded: true,
            busy: true,
            stateReadSucceeded: true,
            taskName: HenchmanTakeoverPolicy.SafeTaskName,
            taskDescription: HenchmanTakeoverPolicy.SafeTaskDescription);

        var decision = AutomatedPostprocessPolicy.EvaluateHenchmanPreflight(readiness);

        Assert.True(decision.StartEngine);
        Assert.False(decision.FinishPostprocess);
        Assert.False(decision.ReleaseAutoRetainerSuppression);
    }

    [Fact]
    public void ReleaseOnlyFinishSkipsBeforeArArmCallback()
    {
        Assert.True(ARPostProcessFinishPolicy.ShouldRunBeforeFinishCallback(ARPostProcessFinishMode.Normal));
        Assert.False(ARPostProcessFinishPolicy.ShouldRunBeforeFinishCallback(ARPostProcessFinishMode.ReleaseOnly));
    }
}
