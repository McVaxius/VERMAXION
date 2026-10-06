using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Inventory;
using Dalamud.Game.Inventory.InventoryEventArgTypes;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;
using VERMAXION.Models;
using NativePlayerState = FFXIVClientStructs.FFXIV.Client.Game.UI.PlayerState;

namespace VERMAXION.Services;

internal sealed unsafe class FishCollectionObservation : IDisposable
{
    private readonly Plugin plugin;
    private readonly FishCollectionCatalog catalog;
    private DateTimeOffset captureAfter = DateTimeOffset.MaxValue;
    private string lastCharacter = "";
    public Dictionary<string, CharacterFishKnowledge> Knowledge { get; } = new();
    public Dictionary<uint, uint> FishIds { get; } = new();
    public Dictionary<uint, int> RequiredLevels { get; } = new();
    public List<FisherMeal> Meals { get; } = new();
    public string ValidationError { get; private set; } = "";

    public FishCollectionObservation(Plugin plugin, FishCollectionCatalog catalog)
    {
        this.plugin = plugin;
        this.catalog = catalog;
        try { LoadNativeData(); }
        catch (Exception error) { ValidationError = "Collection native data is unavailable: " + error.GetBaseException().Message; }
        Plugin.ClientState.ClassJobChanged += OnJobChanged;
        Plugin.ClientState.LevelChanged += OnLevelChanged;
        Plugin.GameInventory.InventoryChangedRaw += OnInventoryChanged;
    }
    private void LoadNativeData()
    {
        foreach (var row in Plugin.DataManager.GetExcelSheet<FishParameter>())
        {
            if (!row.IsInLog || !catalog.Targets.Any(f => f.ItemId == row.Item.RowId)) continue;
            FishIds.Add(row.Item.RowId, row.RowId);
            RequiredLevels.Add(row.Item.RowId, row.GatheringItemLevel.Value.GatheringItemLevel);
        }
        if (FishIds.Count != 348 || catalog.Targets.Any(f => f.Ocean && FishIds.GetValueOrDefault(f.ItemId) != f.FishParameterId))
            ValidationError = "Native FishParameter mappings do not match the bundled collection; collection is unavailable on this game data.";
        var foods = Plugin.DataManager.GetExcelSheet<ItemFood>();
        foreach (var item in Plugin.DataManager.GetExcelSheet<Item>())
        {
            if (item.ItemUICategory.RowId != 46 || !item.ItemAction.IsValid) continue;
            var action = item.ItemAction.Value;
            if (action.Data[0] != 48 || !foods.TryGetRow(action.Data[1], out var food)) continue;
            foreach (var hq in item.CanBeHq ? new[] { false, true } : new[] { false })
            {
                var bonuses = food.Params.Where(p => p.BaseParam.RowId is 10 or 72 or 73)
                    .Select(p => new FisherFoodBonus((int)p.BaseParam.RowId, p.IsRelative,
                        hq ? p.ValueHQ : p.Value, hq ? p.MaxHQ : p.Max)).ToArray();
                if (bonuses.Length > 0) Meals.Add(new(item.RowId, item.Name.ExtractText(), hq, food.RowId, bonuses) { RequiredLevel = item.LevelEquip });
            }
        }
        var sampledHqCaps = new Dictionary<uint, int>
        { [27870] = 1597, [36054] = 1794, [44078] = 2900, [46254] = 5999 };
        var changed = false;
        foreach (var meal in Meals.Where(m => m.Hq))
            if (sampledHqCaps.TryGetValue(meal.ItemId, out var cap))
                changed |= plugin.Configuration.FishCollection.FoodPriceLimits.TryAdd(meal.PriceKey, cap);
        if (changed) plugin.Configuration.Save();
    }
    private void OnJobChanged(uint id) { if (id == 18) Request(); }
    private void OnLevelChanged(uint id, uint level) { if (id == 18) Request(); }
    private void OnInventoryChanged(IReadOnlyCollection<InventoryEventArgs> events)
    {
        // Raw changes observe both ends of a move. Bags and GP regeneration do not request a stat read.
        if (Plugin.PlayerState.ClassJob.RowId == 18 && events.Any(data =>
            (int)data.Item.ContainerType == (int)InventoryType.EquippedItems && AffectsStats(data)))
            Request();
    }
    private static bool AffectsStats(InventoryEventArgs data)
    {
        if (data is not InventoryItemChangedArgs changed) return true;
        var old = changed.OldItemState;
        var item = changed.Item;
        // Spiritbond, glamour and ordinary wear do not alter Fisher stats. Breaking/repairing does.
        return old.ItemId != item.ItemId || old.IsHq != item.IsHq || (old.Condition == 0) != (item.Condition == 0) ||
            !old.Materia.SequenceEqual(item.Materia) || !old.MateriaGrade.SequenceEqual(item.MateriaGrade);
    }
    public void Request() => captureAfter = DateTimeOffset.UtcNow.AddSeconds(2);
    public void Update()
    {
        if (ValidationError.Length > 0) return;
        if (!plugin.IsCharacterRegistered || !GameHelpers.IsPlayerAvailable() ||
            Plugin.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.BetweenAreas] ||
            Plugin.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.BetweenAreas51]) { lastCharacter = ""; return; }
        var key = plugin.ConfigManager.CurrentCharacterKey;
        var config = plugin.ConfigManager.GetCurrentCharacterConfig(key);
        if (!config.FishCollectionSelected) return;
        if (key != lastCharacter) { lastCharacter = key; Request(); }
        if (Plugin.PlayerState.ClassJob.RowId == 18 && DateTimeOffset.UtcNow >= captureAfter)
        {
            captureAfter = DateTimeOffset.MaxValue;
            Capture();
        }
    }
    public FisherObservation Capture()
    {
        if (ValidationError.Length > 0) throw new InvalidOperationException(ValidationError);
        if (!plugin.IsCharacterRegistered || !GameHelpers.IsPlayerAvailable() || Plugin.PlayerState.ClassJob.RowId != 18)
            throw new InvalidOperationException("World-ready Fisher inspection is required.");
        var state = NativePlayerState.Instance();
        var gearsets = RaptureGearsetModule.Instance();
        if (state == null || !state->IsLoaded || gearsets == null) throw new InvalidOperationException("Native Fisher state is unavailable.");
        var effective = Effective();
        var certain = true;
        var foodStatus = Plugin.ObjectTable.LocalPlayer!.StatusList.FirstOrDefault(s => s.StatusId == 48);
        var baseline = effective;
        if (foodStatus != null)
        {
            var hq = foodStatus.Param >= 10000;
            var foodId = (uint)(foodStatus.Param % 10000);
            var food = Meals.FirstOrDefault(m => m.FoodId == foodId && m.Hq == hq);
            // A non-gathering meal has no relevant bonuses; native food data still must resolve it.
            if (food == null)
                certain = Plugin.DataManager.GetExcelSheet<ItemFood>().TryGetRow(foodId, out var nativeFood) &&
                    !nativeFood.Params.Any(p => p.BaseParam.RowId is 10 or 72 or 73);
            else
            {
                foreach (var bonus in food.Bonuses)
                {
                    var value = bonus.Stat == 72 ? baseline.Gathering : bonus.Stat == 73 ? baseline.Perception : baseline.MaximumGp;
                    var stripped = bonus.Remove(value);
                    if (!stripped.HasValue) { certain = false; break; }
                    baseline = bonus.Stat == 72 ? baseline with { Gathering = stripped.Value } :
                        bonus.Stat == 73 ? baseline with { Perception = stripped.Value } : baseline with { MaximumGp = stripped.Value };
                }
            }
        }
        var index = gearsets->CurrentGearsetIndex;
        var gearsetId = index is >= 0 and < 100 && gearsets->Entries[index].ClassJob == 18 &&
            (gearsets->Entries[index].Flags & RaptureGearsetModule.GearsetFlag.Exists) != 0
            ? gearsets->Entries[index].Id : -1;
        baseline = baseline with { GearsetId = gearsetId, BaselineCertain = certain };
        var key = plugin.ConfigManager.CurrentCharacterKey;
        plugin.ConfigManager.GetCurrentCharacterConfig(key).FisherObservation = baseline;
        plugin.ConfigManager.SaveCurrentAccount();
        ObserveLog();
        Plugin.Log.Information($"[FishCollection] Fisher observation: level={baseline.Level}; Gathering={baseline.Gathering}; Perception={baseline.Perception}; maximumGP={baseline.MaximumGp}; gearset={baseline.GearsetId}; baselineCertain={baseline.BaselineCertain}; effectiveGathering={effective.Gathering}; foodParam={foodStatus?.Param ?? 0}; foodSeconds={foodStatus?.RemainingTime ?? 0:F0}; caught={Knowledge[key].CaughtItems.Count}; unlocked={Knowledge[key].UnlockedItems.Count}; observed={baseline.ObservedAtUtc:O}");
        captureAfter = DateTimeOffset.MaxValue;
        return baseline;
    }
    public FisherObservation Effective()
    {
        var state = NativePlayerState.Instance();
        if (state == null || Plugin.PlayerState.ClassJob.RowId != 18) throw new InvalidOperationException("Fisher stats are unavailable.");
        return new(state->CurrentLevel, state->Attributes[72], state->Attributes[73], state->Attributes[10],
            -1, DateTimeOffset.UtcNow, true);
    }
    public void ObserveLog()
    {
        if (!plugin.IsCharacterRegistered || !GameHelpers.IsPlayerAvailable()) throw new InvalidOperationException("Registration is incomplete.");
        var state = NativePlayerState.Instance();
        if (state == null || !state->IsLoaded) throw new InvalidOperationException("Native fishing log is unavailable.");
        var caught = new HashSet<uint>();
        var unlocked = new HashSet<uint>();
        foreach (var fish in catalog.Targets)
        {
            var row = Plugin.DataManager.GetExcelSheet<FishParameter>().GetRow(FishIds[fish.ItemId]);
            if (state->IsFishCaught(row.RowId)) caught.Add(fish.ItemId);
            if (state->GetClassJobLevel(18, false) < RequiredLevels[fish.ItemId]) continue;
            if (row.GatheringSubCategory.RowId != 0 &&
                !state->IsFolkloreBookUnlocked(row.GatheringSubCategory.Value.Division)) continue;
            if (fish.Ocean && !QuestManager.IsQuestComplete(69379)) continue;
            if (fish.CatchPath.Length > 1 && !ActionUnlocked(297)) continue;
            if (fish.Snagging && !ActionUnlocked(4100)) continue;
            if (fish.ModestLure > 0 && !ActionUnlocked(37595)) continue;
            if (fish.AmbitiousLure > 0 && !ActionUnlocked(37594)) continue;
            unlocked.Add(fish.ItemId);
        }
        Knowledge[plugin.ConfigManager.CurrentCharacterKey] = new(caught, unlocked, DateTimeOffset.UtcNow);
    }
    public bool Caught(uint itemId)
    {
        var state = NativePlayerState.Instance();
        if (state == null || !plugin.IsCharacterRegistered) throw new InvalidOperationException("Fishing log is unavailable.");
        return state->IsFishCaught(FishIds[itemId]);
    }
    public static bool ActionUnlocked(uint id)
    {
        var row = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>().GetRow(id);
        var state = NativePlayerState.Instance();
        return state != null && state->CurrentLevel >= row.ClassJobLevel &&
            (row.UnlockLink.RowId == 0 || UIState.Instance()->IsUnlockLinkUnlocked(row.UnlockLink.RowId));
    }
    public static int Count(uint itemId, bool? hq = null)
    {
        var inventory = InventoryManager.Instance();
        if (inventory == null) throw new InvalidOperationException("Inventory is unavailable.");
        var count = 0;
        foreach (var type in new[] { InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4 })
        {
            var container = inventory->GetInventoryContainer(type);
            if (container == null || !container->IsLoaded) throw new InvalidOperationException("Inventory is not loaded.");
            for (var i = 0; i < container->Size; i++)
            {
                var slot = container->GetInventorySlot(i);
                if (slot != null && slot->ItemId == itemId && (!hq.HasValue ||
                    ((slot->Flags & InventoryItem.ItemFlags.HighQuality) != 0) == hq.Value)) count += (int)slot->Quantity;
            }
        }
        return count;
    }
    // The native cycle starts at Bloodbrine day. OceanTrip's independently observed
    // schedule uses the same 144-row order and an 88-slot phase from the Unix epoch.
    public static int OceanCycleIndex(DateTimeOffset registration) => FishCollectionPolicy.OceanCycleIndex(registration);
#pragma warning disable PendingExcelSchema // Native 144-row Ocean cycle; row count is checked before use.
    public FishOpportunity? NextOcean(CollectionFish fish, DateTimeOffset now)
    {
        var table = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Experimental.IKDRouteTable>();
        if (table.Count != 144) throw new InvalidOperationException("Native Ocean route cycle must contain 144 rows.");
        var start = DateTimeOffset.FromUnixTimeSeconds(now.ToUnixTimeSeconds() / 7200 * 7200);
        var matches = new List<(DateTimeOffset Start, uint Route)>();
        for (var i = 0; i <= 288 && matches.Count < 2; i++)
        {
            var time = start.AddHours(i * 2);
            if (time.AddMinutes(15) <= now) continue;
            var row = table.GetRow((uint)OceanCycleIndex(time));
            var route = fish.Routes.Contains(row.IndigoRoute.RowId) ? row.IndigoRoute.RowId :
                fish.Routes.Contains(row.RubyRoute.RowId) ? row.RubyRoute.RowId : 0;
            if (route != 0) matches.Add((time, route));
        }
        return matches.Count == 0 ? null : new(fish, matches[0].Start, matches[0].Start.AddMinutes(15),
            matches.Count > 1 ? matches[1].Start - matches[0].Start : TimeSpan.MaxValue, matches[0].Route);
    }
#pragma warning restore PendingExcelSchema
    public void Dispose()
    {
        Plugin.ClientState.ClassJobChanged -= OnJobChanged;
        Plugin.ClientState.LevelChanged -= OnLevelChanged;
        Plugin.GameInventory.InventoryChangedRaw -= OnInventoryChanged;
    }
}
