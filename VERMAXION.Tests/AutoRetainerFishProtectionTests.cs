using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;
using VERMAXION.IPC;
using VERMAXION.Models;
using Xunit;

namespace VERMAXION.Tests;

public sealed class AutoRetainerFishProtectionTests
{
    private static readonly HashSet<uint> FishIds = [7688, 7696];

    private class Plan
    {
        public ICollection<uint> IMDiscardList = new List<uint>();
        public ICollection<uint> IMAutoVendorHard = new List<uint>();
        public ICollection<uint> IMAutoVendorSoft = new List<uint>();
    }
    private sealed class PlanWithFlags : Plan
    {
        public ICollection<uint> IMAutoVendorHardIgnoreStack = new HashSet<uint>();
        public ICollection<uint> IMDiscardIgnoreStack = new List<uint>();
        public List<uint> IMProtectList = [7688, 600];
    }
    private sealed class Config
    {
        public object DefaultIMSettings = new Plan();
        public List<object> AdditionalIMSettings = [];
        public List<uint> IMAutoVendorHard = [7688, 700]; // Obsolete, outside active plans.
    }

    [Fact]
    public void RemovesTargetsAndDuplicatesFromEveryPlanAndPersistsWithoutTouchingOtherItems()
    {
        var current = new PlanWithFlags
        {
            IMDiscardList = new List<uint> { 7688, 4932, 7688 },
            IMAutoVendorHard = new List<uint> { 7696, 200 },
            IMAutoVendorSoft = new List<uint> { 7688, 201 },
            IMAutoVendorHardIgnoreStack = new HashSet<uint> { 7696, 202 },
            IMDiscardIgnoreStack = new List<uint> { 7688, 203 },
        };
        var additional = new PlanWithFlags
        {
            IMDiscardList = new List<uint> { 7696, 300 },
            IMAutoVendorHard = new List<uint> { 7688, 301 },
            IMAutoVendorSoft = new List<uint> { 7696, 302 },
            IMAutoVendorHardIgnoreStack = new HashSet<uint> { 7688 },
        };
        var config = new Config { DefaultIMSettings = current, AdditionalIMSettings = [additional] };
        var saves = 0;
        var result = AutoRetainerFishProtectionReflection.Remove(config, FishIds, () => saves++);

        Assert.True(result.Success, result.Error);
        Assert.Equal(2, result.Plans);
        Assert.Equal(3, result.Discard);
        Assert.Equal(2, result.UnconditionalSell);
        Assert.Equal(2, result.QuickVentureSell);
        Assert.Equal(3, result.StackFlags);
        Assert.Equal(1, saves);
        Assert.Equal(new uint[] { 4932 }, current.IMDiscardList);
        Assert.Equal(new uint[] { 200 }, current.IMAutoVendorHard);
        Assert.Equal(new uint[] { 201 }, current.IMAutoVendorSoft);
        Assert.Equal(new uint[] { 202 }, current.IMAutoVendorHardIgnoreStack);
        Assert.Equal(new uint[] { 203 }, current.IMDiscardIgnoreStack);
        Assert.Equal(new uint[] { 300 }, additional.IMDiscardList);
        Assert.Equal(new uint[] { 301 }, additional.IMAutoVendorHard);
        Assert.Equal(new uint[] { 302 }, additional.IMAutoVendorSoft);
        Assert.Empty(additional.IMAutoVendorHardIgnoreStack);
        Assert.Equal(new uint[] { 7688, 600 }, current.IMProtectList);
        Assert.Equal(new uint[] { 7688, 700 }, config.IMAutoVendorHard);
    }

    [Theory]
    [InlineData("missing list")]
    [InlineData("wrong type")]
    [InlineData("read only")]
    [InlineData("read only flags")]
    public void AnIncompatibleLaterPlanFailsBeforeAnyMutationOrSave(string failure)
    {
        var current = new Plan { IMAutoVendorHard = new List<uint> { 7688, 200 } };
        object incompatible = failure switch
        {
            "missing list" => new { IMDiscardList = new List<uint>(), IMAutoVendorHard = new List<uint>() },
            "wrong type" => new { IMDiscardList = new List<uint>(), IMAutoVendorHard = new List<uint>(), IMAutoVendorSoft = new List<string>() },
            "read only" => new Plan { IMDiscardList = new ReadOnlyCollection<uint>(new List<uint> { 7696 }) },
            _ => new PlanWithFlags { IMDiscardIgnoreStack = new uint[] { 7696 } },
        };
        var config = new Config { DefaultIMSettings = current, AdditionalIMSettings = [incompatible] };
        var saves = 0;
        var result = AutoRetainerFishProtectionReflection.Remove(config, FishIds, () => saves++);

        Assert.False(result.Success);
        Assert.Contains("mutable item-ID list", result.Error);
        Assert.Equal(new uint[] { 7688, 200 }, current.IMAutoVendorHard);
        Assert.Equal(0, saves);
    }

    [Fact]
    public void SaveFailureIsVisibleAndManualReapplyPersistsTheAlreadyClearPlan()
    {
        var plan = new Plan { IMAutoVendorHard = new List<uint> { 7688, 200 } };
        var config = new Config { DefaultIMSettings = plan };
        var saves = 0;
        var failed = AutoRetainerFishProtectionReflection.Remove(config, FishIds, () =>
        { saves++; throw new InvalidOperationException("save failed"); });

        Assert.False(failed.Success);
        Assert.Equal("save failed", failed.Error);
        Assert.Equal(1, failed.UnconditionalSell);
        Assert.Equal(new uint[] { 200 }, plan.IMAutoVendorHard);
        var reapplied = AutoRetainerFishProtectionReflection.Remove(config, FishIds, () => saves++);
        Assert.True(reapplied.Success, reapplied.Error);
        Assert.Equal(0, reapplied.UnconditionalSell);
        Assert.Equal(2, saves);
    }

    [Fact]
    public void PersistenceThatReintroducesATargetCannotReportSuccess()
    {
        var plan = new Plan { IMAutoVendorHard = new List<uint> { 7688 } };
        var result = AutoRetainerFishProtectionReflection.Remove(new Config { DefaultIMSettings = plan },
            FishIds, () => plan.IMAutoVendorHard.Add(7696));
        Assert.False(result.Success);
        Assert.Contains("did not remain clear", result.Error);
    }

    [Fact]
    public void CleanupOptionRequiresOptInAndRoundTripsThroughTheNativeSerializer()
    {
        Assert.False(JsonSerializer.Deserialize<FishCollectionSettings>("{}")!.RemoveFishFromAutoRetainerLists);
        Assert.False(Newtonsoft.Json.JsonConvert.DeserializeObject<FishCollectionSettings>("{}")!.RemoveFishFromAutoRetainerLists);
        var selected = new FishCollectionSettings { RemoveFishFromAutoRetainerLists = true, PinnedItemId = 7688 };
        var restored = Newtonsoft.Json.JsonConvert.DeserializeObject<FishCollectionSettings>(
            Newtonsoft.Json.JsonConvert.SerializeObject(selected))!;
        Assert.True(restored.RemoveFishFromAutoRetainerLists);
        Assert.Equal(7688u, restored.PinnedItemId);
        restored.RemoveFishFromAutoRetainerLists = false;
        Assert.False(Newtonsoft.Json.JsonConvert.DeserializeObject<FishCollectionSettings>(
            Newtonsoft.Json.JsonConvert.SerializeObject(restored))!.RemoveFishFromAutoRetainerLists);
    }
}
