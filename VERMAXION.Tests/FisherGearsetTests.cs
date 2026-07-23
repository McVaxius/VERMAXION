#nullable enable

using System;
using VERMAXION.Models;
using Xunit;

namespace VERMAXION.Tests;

public sealed class FisherGearsetTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 7, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ClassJobSourceOfTruthValuesAreStable()
    {
        Assert.Equal(18, ClassJobIds.Fisher);
        Assert.Equal(24, ClassJobIds.WhiteMage);
        Assert.Equal(6, ClassJobIds.Conjurer);
    }

    [Fact]
    public void SelectionUsesLowestExistingFisherSlotAndPreservesEntryId()
    {
        var selection = FisherGearsetSelectionPolicy.SelectLowestSavedFisher(
        [
            new SavedGearsetSnapshot(7, 91, ClassJobIds.Fisher, true),
            new SavedGearsetSnapshot(0, 12, ClassJobIds.Fisher, false),
            new SavedGearsetSnapshot(1, 44, ClassJobIds.WhiteMage, true),
            new SavedGearsetSnapshot(2, 83, ClassJobIds.Fisher, true),
        ]);

        Assert.Equal(new FisherGearsetSelection(2, 83), selection);
    }

    [Fact]
    public void MissingFisherGearsetImmediatelyRequestsFallback()
    {
        var runtime = new FakeRuntime
        {
            CurrentClassJobId = ClassJobIds.WhiteMage,
            Lookup = FisherGearsetLookupResult.Missing(),
        };
        var operation = NewOperation(runtime);

        var events = operation.Tick(Start);

        Assert.Equal(FisherGearsetEquipState.FallbackRequired, operation.State);
        Assert.Contains(events, entry =>
            entry.Kind == FisherGearsetEventKind.FallbackRequested &&
            entry.Message.Contains("Weathered Fishing Rod"));
        Assert.Equal(0, runtime.EquipRequests);
    }

    [Fact]
    public void UnavailableModuleRetriesAtFiveSeconds()
    {
        var runtime = new FakeRuntime
        {
            CurrentClassJobId = ClassJobIds.WhiteMage,
            Lookup = FisherGearsetLookupResult.ModuleUnavailable("module unavailable"),
        };
        var operation = NewOperation(runtime);

        operation.Tick(Start);
        runtime.Lookup = FisherGearsetLookupResult.Found(new FisherGearsetSelection(3, 17));
        runtime.EquipChangesJob = true;
        operation.Tick(Start.AddSeconds(4.999));
        var retryEvents = operation.Tick(Start.AddSeconds(5));

        Assert.Equal(2, runtime.LookupRequests);
        Assert.Equal(1, runtime.EquipRequests);
        Assert.True(operation.Succeeded);
        Assert.Contains(retryEvents, entry => entry.Kind == FisherGearsetEventKind.Verified);
    }

    [Fact]
    public void SuccessfulEquipLogsSelectionResultAndVerifiedFinalJob()
    {
        var runtime = new FakeRuntime
        {
            CurrentClassJobId = ClassJobIds.Conjurer,
            Lookup = FisherGearsetLookupResult.Found(new FisherGearsetSelection(4, 29)),
            EquipResult = FisherGearsetEquipRequestResult.Completed(0),
            EquipChangesJob = true,
        };
        var operation = NewOperation(runtime);

        var events = operation.Tick(Start);

        Assert.True(operation.Succeeded);
        Assert.Equal(ClassJobIds.Fisher, operation.FinalClassJobId);
        Assert.Contains(events, entry => entry.Message.Contains("slot 4, ID 29"));
        Assert.Contains(events, entry => entry.Message.Contains("Equip request result: 0"));
        Assert.Contains(events, entry => entry.Message.Contains("Verified final class-job ID: 18"));
    }

    [Fact]
    public void RejectedEquipRequestIsRetriedAfterFiveSeconds()
    {
        var runtime = new FakeRuntime
        {
            CurrentClassJobId = ClassJobIds.WhiteMage,
            Lookup = FisherGearsetLookupResult.Found(new FisherGearsetSelection(2, 83)),
            EquipResult = FisherGearsetEquipRequestResult.Completed(-1),
        };
        var operation = NewOperation(runtime);

        operation.Tick(Start);
        runtime.EquipResult = FisherGearsetEquipRequestResult.Completed(0);
        runtime.EquipChangesJob = true;
        operation.Tick(Start.AddSeconds(5));

        Assert.True(operation.Succeeded);
        Assert.Equal(2, runtime.EquipRequests);
    }

    [Fact]
    public void TenFailedEquipRequestsFallBackBeforeAnEleventh()
    {
        var runtime = new FakeRuntime
        {
            CurrentClassJobId = ClassJobIds.WhiteMage,
            Lookup = FisherGearsetLookupResult.Found(new FisherGearsetSelection(2, 83)),
            EquipResult = FisherGearsetEquipRequestResult.Completed(-1),
        };
        var operation = new FisherGearsetEquipOperation(
            runtime,
            Start,
            Start.AddMinutes(5));

        for (var attempt = 0; attempt < FisherGearsetEquipOperation.MaximumEquipRequests; attempt++)
            operation.Tick(Start.Add(FisherGearsetEquipOperation.RetryInterval * attempt));

        Assert.Equal(FisherGearsetEquipState.FallbackRequired, operation.State);
        Assert.Equal(10, runtime.EquipRequests);
        var afterTerminal = operation.Tick(Start.AddMinutes(2));
        Assert.Empty(afterTerminal);
        Assert.Equal(10, runtime.EquipRequests);
    }

    [Fact]
    public void VerificationTimesOutWithFinalJobAndLastTransientError()
    {
        var runtime = new FakeRuntime
        {
            CurrentClassJobId = ClassJobIds.Conjurer,
            Lookup = FisherGearsetLookupResult.ModuleUnavailable("module unavailable"),
        };
        var operation = NewOperation(runtime);

        operation.Tick(Start);
        var events = operation.Tick(Start.AddSeconds(45));

        Assert.Equal(FisherGearsetEquipState.TimedOut, operation.State);
        Assert.Contains(events, entry =>
            entry.Message.Contains("final class-job ID: 6") &&
            entry.Message.Contains("module unavailable"));
    }

    [Fact]
    public void AlreadyFisherIsIdempotentAndDoesNotTouchGearsets()
    {
        var runtime = new FakeRuntime
        {
            CurrentClassJobId = ClassJobIds.Fisher,
            Lookup = FisherGearsetLookupResult.ModuleUnavailable("must not be read"),
        };
        var operation = NewOperation(runtime);

        var events = operation.Tick(Start);

        Assert.True(operation.Succeeded);
        Assert.Equal(0, runtime.LookupRequests);
        Assert.Equal(0, runtime.EquipRequests);
        Assert.Single(events);
        Assert.Equal(FisherGearsetEventKind.Verified, events[0].Kind);
    }

    [Fact]
    public void FocusedOperationHasNoFishingRelogArOrSchedulingSideEffects()
    {
        var runtime = new FakeRuntime
        {
            CurrentClassJobId = ClassJobIds.WhiteMage,
            Lookup = FisherGearsetLookupResult.Found(new FisherGearsetSelection(1, 5)),
            EquipChangesJob = true,
        };
        var operation = NewOperation(runtime);

        operation.Tick(Start);

        Assert.True(operation.Succeeded);
        Assert.Equal(0, runtime.AutoRetainerCalls);
        Assert.Equal(0, runtime.RelogCalls);
        Assert.Equal(0, runtime.SchedulingGuardCalls);
        Assert.Equal(0, runtime.FishingFlowCalls);
        Assert.Equal(1, runtime.EquipRequests);
    }

    private static FisherGearsetEquipOperation NewOperation(FakeRuntime runtime)
        => new(runtime, Start, Start.AddSeconds(45));

    private sealed class FakeRuntime : IFisherGearsetRuntime
    {
        public int CurrentClassJobId { get; set; }
        public FisherGearsetLookupResult Lookup { get; set; } =
            FisherGearsetLookupResult.Missing();
        public FisherGearsetEquipRequestResult EquipResult { get; set; } =
            FisherGearsetEquipRequestResult.Completed(0);
        public bool EquipChangesJob { get; set; }

        public int LookupRequests { get; private set; }
        public int EquipRequests { get; private set; }
        public int AutoRetainerCalls { get; private set; }
        public int RelogCalls { get; private set; }
        public int SchedulingGuardCalls { get; private set; }
        public int FishingFlowCalls { get; private set; }

        public int GetCurrentClassJobId()
            => CurrentClassJobId;

        public FisherGearsetLookupResult FindFirstSavedFisherGearset()
        {
            LookupRequests++;
            return Lookup;
        }

        public FisherGearsetEquipRequestResult EquipGearset(int gearsetId)
        {
            EquipRequests++;
            if (EquipChangesJob)
                CurrentClassJobId = ClassJobIds.Fisher;
            return EquipResult;
        }
    }
}
