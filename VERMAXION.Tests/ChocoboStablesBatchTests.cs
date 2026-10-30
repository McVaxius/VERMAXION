extern alias live;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Text.Json;
using VERMAXION.Models;
using Xunit;

namespace VERMAXION.Tests;

public sealed class ChocoboStablesBatchTests
{
    private static StableBird Bird(int page = 0, int row = -1, string name = "Fixture bird", string owner = "Fixture owner",
        int rank = 5, bool capped = false, string training = "Ready") => new(page, row, name, owner, rank, capped, training);

    [Fact]
    public void SavedTargetsKeepNumericValuesAndAllTrainableRoundTripsWithoutChangingDefaults()
    {
        Assert.Equal(0, (int)StableTarget.OwnChocobo);
        Assert.Equal(1, (int)StableTarget.SpecificOther);
        Assert.Equal(2, (int)StableTarget.AllTrainable);
        Assert.Equal(StableTarget.OwnChocobo, new ChocoboStablesSettings().Target);
        var config = new CharacterConfig { ChocoboStablesSettings = new() { Target = StableTarget.AllTrainable } };
        var restored = JsonSerializer.Deserialize<CharacterConfig>(JsonSerializer.Serialize(config))!;
        var copy = restored.Clone();
        Assert.Equal(StableTarget.AllTrainable, copy.ChocoboStablesSettings.Target);
        copy.ChocoboStablesSettings.Target = StableTarget.OwnChocobo;
        Assert.Equal(StableTarget.AllTrainable, restored.ChocoboStablesSettings.Target);
        Assert.False(ChocoboOnionQuests.ShouldAcquire(Bird(rank: 10, capped: true), StableTarget.AllTrainable, 0));
    }

    [Fact]
    public void MultiPageRosterIncludesPersonalBirdOnceAndNeedsEveryEligibleReceipt()
    {
        var own = Bird();
        var firstOther = Bird(0, 0, "Fixture other one", "Fixture other owner one");
        var lastOther = Bird(2, 1, "Fixture other two", "Fixture other owner two");
        var batch = new StableTrainingBatch([own, firstOther, lastOther,
            Bird(1, 0, "Capped", capped: true), Bird(1, 1, "Maximum", rank: 20), Bird(2, 0, "Waiting", training: "42m")]);
        Assert.Equal(3, batch.Remaining);
        Assert.Equal(3, batch.Skipped);
        var stock = 5;
        foreach (var candidate in new[] { own, firstOther, lastOther })
        {
            Assert.Equal(candidate, batch.Current);
            Assert.True(batch.TryRevalidate([candidate], out var row));
            Assert.Equal(candidate, row);
            Assert.True(batch.Dispatch(stock));
            Assert.False(batch.Dispatch(stock)); // A redraw cannot redispatch an admitted feed.
            Assert.Equal(StableTrainingReceipt.Pending, batch.ObserveReceipt(stock, [candidate]));
            Assert.Equal(StableTrainingReceipt.Pending, batch.ObserveReceipt(stock - 1, [candidate]));
            Assert.Equal(StableTrainingReceipt.Verified, batch.ObserveReceipt(--stock, [candidate with { Training = "59m" }]));
            Assert.Equal(3 - batch.Trained, batch.Remaining);
            Assert.Equal(batch.Trained == 3, batch.CanRecordCompletion);
        }
        Assert.True(batch.Complete);
        Assert.Null(batch.Current);
        Assert.Equal(3, batch.Trained);
        Assert.False(batch.Dispatch(stock));
        Assert.Equal(StableTrainingReceipt.Uncertain, batch.ObserveReceipt(stock - 1, [lastOther with { Training = "58m" }]));
        Assert.Equal(3, batch.Trained);
    }

    [Theory]
    [InlineData(0, 1, "Fixture bird", "Fixture owner")]
    [InlineData(1, -1, "Fixture bird", "Fixture owner")]
    [InlineData(0, -1, "Replacement", "Fixture owner")]
    [InlineData(0, -1, "Fixture bird", "Replacement")]
    public void ReorderedOrChangedNativeIdentityStopsInsteadOfRetargeting(int page, int row, string name, string owner)
    {
        var batch = new StableTrainingBatch([Bird()]);
        Assert.False(batch.TryRevalidate([Bird(page, row, name, owner)], out _));
        Assert.True(batch.Stopped);
        Assert.False(batch.Dispatch(5));
        Assert.False(batch.CanRecordCompletion);
        Assert.Equal(1, batch.Remaining);
    }

    [Fact]
    public void DuplicateIdentitiesAndRepeatedPersonalRowsCannotDispatch()
    {
        var own = Bird();
        foreach (var roster in new[] { new[] { own, own with { Page = 1 } }, new[] { own, own with { Name = "Replacement" } } })
        {
            var batch = new StableTrainingBatch(roster);
            Assert.True(batch.Stopped);
            Assert.False(batch.Dispatch(2));
            Assert.Equal(0, batch.Trained);
        }
        var valid = new StableTrainingBatch([own]);
        Assert.False(valid.TryRevalidate([own, own with { Row = 0 }], out _));
    }

    [Theory]
    [InlineData("Ready")]
    [InlineData("")]
    [InlineData("Unknown")]
    [InlineData("In 59m")]
    [InlineData("61m")]
    [InlineData("-1m")]
    public void RootConsumptionAloneDoesNotVerifyTraining(string training)
    {
        var bird = Bird();
        var batch = new StableTrainingBatch([bird]);
        Assert.True(batch.Dispatch(3));
        var receipt = batch.ObserveReceipt(2, [bird with { Training = training }]);
        Assert.NotEqual(StableTrainingReceipt.Verified, receipt);
        Assert.Equal(0, batch.Trained);
        Assert.False(batch.CanRecordCompletion);
        Assert.False(batch.Dispatch(2));
    }

    [Theory]
    [InlineData("0m")]
    [InlineData("42m")]
    [InlineData("59m")]
    [InlineData("60m")]
    public void SupportedNativeMinuteTextNeedsExactlyOneRootAndSameBird(string cooldown)
    {
        var bird = Bird();
        var batch = new StableTrainingBatch([bird]);
        Assert.True(batch.Dispatch(3));
        Assert.Equal(StableTrainingReceipt.Pending, batch.ObserveReceipt(3, [bird with { Training = cooldown }]));
        Assert.Equal(StableTrainingReceipt.Verified, batch.ObserveReceipt(2, [bird with { Training = cooldown }]));
        Assert.True(batch.CanRecordCompletion);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(4)]
    public void UnreadableOrUnexpectedInventoryReceiptStopsTheBatch(int stock)
    {
        var bird = Bird();
        var batch = new StableTrainingBatch([bird]);
        Assert.True(batch.Dispatch(3));
        Assert.Equal(StableTrainingReceipt.Uncertain, batch.ObserveReceipt(stock, [bird with { Training = "59m" }]));
        Assert.True(batch.Stopped);
        Assert.False(batch.Dispatch(3));
        Assert.Equal(0, batch.Trained);
    }

    [Fact]
    public void PartialStockAndCancellationKeepVerifiedCreditWithoutWholeBatchCompletionOrReplay()
    {
        var first = Bird();
        var second = Bird(1, 0, "Fixture second");
        foreach (var cancelAfterDispatch in new[] { false, true })
        {
            var batch = new StableTrainingBatch([first, second]);
            Assert.True(batch.Dispatch(1));
            Assert.Equal(StableTrainingReceipt.Verified, batch.ObserveReceipt(0, [first with { Training = "59m" }]));
            Assert.Equal(1, batch.Trained);
            Assert.Equal(second, batch.Current);
            Assert.False(batch.CanRecordCompletion);
            if (cancelAfterDispatch)
            {
                Assert.True(batch.Dispatch(1));
                batch.Stop("Stable visit cancelled");
                Assert.Equal(StableTrainingReceipt.Uncertain, batch.ObserveReceipt(0, [second with { Training = "59m" }]));
            }
            else Assert.False(batch.Dispatch(0));
            Assert.True(batch.Stopped);
            Assert.Equal(1, batch.Trained);
            Assert.Equal(1, batch.Remaining);
            Assert.False(batch.Complete);
            Assert.False(batch.Dispatch(20)); // Supplies or cancellation cannot rearm the ended visit.
        }
        // A later configured visit scans current native eligibility, rather than replaying the old queue.
        var laterVisit = new StableTrainingBatch([first with { Training = "42m" }, second]);
        Assert.Equal(second, laterVisit.Current);
        Assert.Equal(1, laterVisit.Skipped);
        Assert.Equal(1, laterVisit.Remaining);
        Assert.Equal(0, laterVisit.Trained); // Counts are per-visit, not a saved ledger.
    }

    [Fact]
    public void ZeroEligibleAndNewlyUnavailableBirdsNeverStampTrainingCompletion()
    {
        var empty = new StableTrainingBatch([Bird(capped: true), Bird(1, 0, "Maximum", rank: 20)]);
        Assert.True(empty.Complete);
        Assert.Equal(2, empty.Skipped);
        Assert.False(empty.CanRecordCompletion);
        var batch = new StableTrainingBatch([Bird()]);
        Assert.True(batch.TryRevalidate([Bird(training: "42m")], out var current));
        Assert.False(current!.Trainable);
        batch.SkipCurrent();
        Assert.True(batch.Complete);
        Assert.Equal(1, batch.Skipped);
        Assert.Equal(0, batch.Remaining);
        Assert.False(batch.CanRecordCompletion);
    }

    [Fact]
    public void ChangedRowCannotCreditAConsumedFeed()
    {
        var batch = new StableTrainingBatch([Bird()]);
        Assert.True(batch.Dispatch(2));
        Assert.Equal(StableTrainingReceipt.Uncertain,
            batch.ObserveReceipt(1, [Bird(row: 0, training: "59m")]));
        Assert.True(batch.Stopped);
        Assert.Equal(0, batch.Trained);
        Assert.False(batch.CanRecordCompletion);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void MissingOrUnreadableStockBeforeTheFirstFeedStopsWithoutCompletion(int stock)
    {
        var batch = new StableTrainingBatch([Bird()]);
        Assert.False(batch.Dispatch(stock));
        Assert.True(batch.Stopped);
        Assert.Equal(0, batch.Trained);
        Assert.Equal(1, batch.Remaining);
        Assert.False(batch.CanRecordCompletion);
    }

    [Fact]
    public void ProductionRewardRequiresTheSuccessfulSelectionProofWithoutReopeningTheRoster()
    {
        var serviceType = typeof(live::VERMAXION.Services.ChocoboStablesService);
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        var first = new live::VERMAXION.Models.StableBird(0, -1, "Fixture first", "Fixture owner", 5, false, "Ready");
        var second = first with { Row = 0, Name = "Fixture second" };
        var batchType = serviceType.Assembly.GetType("VERMAXION.Models.StableTrainingBatch", true)!;
        var admit = serviceType.GetMethod("TryAdmitReward", fields)!;
        var receipt = batchType.GetMethod("ObserveReceipt")!;
        var pendingFeed = batchType.GetField("feedBefore", fields)!;
        void Set(object service, string field, object? value) => serviceType.GetField(field, fields)!.SetValue(service, value);
        object Batch(live::VERMAXION.Models.StableBird initial) => batchType
            .GetConstructor([typeof(IReadOnlyList<live::VERMAXION.Models.StableBird>)])!.Invoke([new[] { initial, second }]);
        bool Admit(object service, int stock) => (bool)admit.Invoke(service, [stock, null])!;
        live::VERMAXION.Services.ChocoboStablesService Service(object batch)
        {
            // No game or UI services: Reward must use the successfully clicked target,
            // while fresh native roster identity remains mandatory for the later receipt.
            var service = (live::VERMAXION.Services.ChocoboStablesService)RuntimeHelpers.GetUninitializedObject(serviceType);
            Set(service, "batch", batch);
            Set(service, "selected", first);
            Set(service, "selectedForReward", first);
            Set(service, "feedBefore", 2);
            return service;
        }
        foreach (var scenario in new[] { "missing proof", "missing target", "changed target", "changed proof", "changed batch target", "changed stock" })
        {
            var batch = Batch(scenario == "changed batch target" ? first with { Name = "Fixture replacement" } : first);
            var service = Service(batch);
            if (scenario == "missing proof") Set(service, "selectedForReward", null);
            if (scenario == "missing target") Set(service, "selected", null);
            if (scenario == "changed target") Set(service, "selected", second);
            if (scenario == "changed proof") Set(service, "selectedForReward", first with { Row = 1 });
            Assert.False(Admit(service, scenario == "changed stock" ? 1 : 2));
            Assert.Null(pendingFeed.GetValue(batch));
            Assert.Null(serviceType.GetField("selectedForReward", fields)!.GetValue(service));
            Assert.Equal(0, service.TrainedCount);
            Assert.Equal(2, service.RemainingCount);
            Assert.False(service.CanRecordTrainingCompletion);
        }
        var validBatch = Batch(first);
        var valid = Service(validBatch);
        Assert.True(Admit(valid, 2));
        Assert.Equal(2, pendingFeed.GetValue(validBatch));
        Assert.False(Admit(valid, 2)); // Proof is consumed before any native callback can run or throw.
        Assert.Equal(0, valid.TrainedCount);
        Assert.False(valid.CanRecordTrainingCompletion);
        receipt.Invoke(validBatch, [1, new[] { first with { Training = "59m" } }]);
        Assert.Equal(1, valid.TrainedCount);
        Assert.Equal(1, valid.RemainingCount);
        Assert.Null(pendingFeed.GetValue(validBatch));
        Set(valid, "selected", second);
        Set(valid, "feedBefore", 1);
        Assert.False(Admit(valid, 1)); // The previous bird cannot authorize the next bird.
        Assert.Null(pendingFeed.GetValue(validBatch));
        Set(valid, "selectedForReward", second);
        Assert.True(Admit(valid, 1));
        receipt.Invoke(validBatch, [0, new[] { second with { Training = "59m" } }]);
        Assert.Equal(2, valid.TrainedCount);
        Assert.Equal(0, valid.RemainingCount);
    }

    [Fact]
    public void ProductionServiceCannotPersistOneVerifiedBirdAsACompletedBatch()
    {
        var serviceType = typeof(live::VERMAXION.Services.ChocoboStablesService);
        var service = (live::VERMAXION.Services.ChocoboStablesService)RuntimeHelpers.GetUninitializedObject(serviceType);
        var first = new live::VERMAXION.Models.StableBird(0, -1, "Fixture first", "Fixture owner", 5, false, "Ready");
        var second = first with { Page = 1, Row = 0, Name = "Fixture second" };
        var batchType = serviceType.Assembly.GetType("VERMAXION.Models.StableTrainingBatch", true)!;
        var batch = batchType.GetConstructor([typeof(IReadOnlyList<live::VERMAXION.Models.StableBird>)])!
            .Invoke([new[] { first, second }]);
        serviceType.GetField("batch", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(service, batch);
        serviceType.GetProperty("IsComplete")!.SetValue(service, true);
        serviceType.GetProperty("Trained")!.SetValue(service, true);
        Assert.False(service.CanRecordTrainingCompletion);
        var dispatch = batchType.GetMethod("Dispatch")!;
        var receipt = batchType.GetMethod("ObserveReceipt")!;
        Assert.Equal(true, dispatch.Invoke(batch, [2]));
        receipt.Invoke(batch, [1, new[] { first with { Training = "59m" } }]);
        Assert.Equal(1, service.TrainedCount);
        Assert.Equal(1, service.RemainingCount);
        Assert.False(service.CanRecordTrainingCompletion);
        Assert.Equal(true, dispatch.Invoke(batch, [1]));
        receipt.Invoke(batch, [0, new[] { second with { Training = "59m" } }]);
        Assert.Equal(2, service.TrainedCount);
        Assert.True(service.CanRecordTrainingCompletion);
        batchType.GetMethod("Stop")!.Invoke(batch, ["Stable visit cancelled"]);
        Assert.False(service.CanRecordTrainingCompletion);
    }

    [Fact]
    public void ProductionBatchStatusLocalizesItsAuthoredTailInEveryLanguage()
    {
        const string key = "All trainable: trained {0}; skipped {1}; remaining {2}. {3}";
        const string status = "All trainable: trained 1; skipped 2; remaining 0. Batch verified.";
        var assembly = typeof(live::VERMAXION.Services.ChocoboStablesService).Assembly;
        var type = assembly.GetType("VERMAXION.UiText", true)!;
        var constructor = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Single();
        var fontDelegateType = constructor.GetParameters()[1].ParameterType;
        var font = typeof(ChocoboStablesBatchTests).GetMethod(nameof(NoFont), BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(fontDelegateType.GenericTypeArguments[0]).CreateDelegate(fontDelegateType);
        foreach (var language in new[] { "en", "de", "es", "fr", "hi", "id", "it", "ja", "ko", "pl", "pt-BR", "ru", "tr", "vi", "zh-Hans" })
        {
            using var text = (IDisposable)constructor.Invoke([language, font]);
            using var frame = (IDisposable)type.GetMethod("Enter", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(text, null)!;
            var manager = new ResourceManager("VERMAXION.Localization.Strings_" + language.Replace('-', '_'), assembly);
            var resources = manager.GetResourceSet(CultureInfo.InvariantCulture, true, false)!;
            Assert.False(string.IsNullOrWhiteSpace(resources.GetString("Onion acquisition uses the Marketboard settings or unfinished free quest rewards. Free quests require Wiggly Quest, the right job and level, MSQ progress and supported routes.", false)));
            var expected = string.Format(CultureInfo.GetCultureInfo(language), resources.GetString(key, false)!,
                "1", "2", "0", resources.GetString("Batch verified.", false)!);
            var translated = type.GetMethod("T", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(null, [status]);
            Assert.Equal(expected, translated);
            manager.ReleaseAllResources();
        }
    }

    private static IDisposable NoFont<T>(T _) => new EmptyFont();
    private sealed class EmptyFont : IDisposable { public void Dispose() { } }
}
