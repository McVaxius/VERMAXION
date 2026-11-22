using System.Reflection;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using VERMAXION.Models;

namespace VERMAXION.Services;

internal sealed unsafe class SilverChocoboFeatherService(Plugin plugin)
{
    private const uint FeatherItemId = 12995;
    private const int FeathersPerExchange = 5;
    private const uint SalvagerSpoilsTopic = 3276842;

    private sealed record Bundle(Item First, Item Second)
    {
        public int Tier => First.LevelEquip;
        public long CombinedItemLevel => (long)First.LevelItem.RowId + Second.LevelItem.RowId;
        public string Name => $"{First.Name} + {Second.Name}";
    }

    private sealed record Exchange(Bundle Bundle, int FeathersBefore, int FirstBefore, int SecondBefore);

    private readonly Queue<Bundle> remaining = new();
    private Exchange? pending;
    private string? operationId;
    private bool shopHeldOpen;
    private ulong ownerCharacter;
    private bool cancelRequested;
    private int verifiedExchanges;
    private int exchangeLimit;

    public bool IsRunning { get; private set; }
    public bool IsFailed { get; private set; }
    public string StatusText { get; private set; } = "Ready";
    public bool HasOwnedExchange => pending != null;

    private static InventoryManager* Inventory => InventoryManager.Instance();
    private static int FeatherCount => Inventory != null ? Inventory->GetInventoryItemCount(FeatherItemId, false, false, false) : 0;
    private static bool AdsLoaded => Plugin.PluginInterface.InstalledPlugins.Any(candidate => candidate.IsLoaded && candidate.InternalName == "ADS");
    private bool CharacterReady => plugin.IsCharacterRegistered && Plugin.ClientState.IsLoggedIn
        && Plugin.PlayerState.IsLoaded && Plugin.PlayerState.ContentId != 0 && Inventory != null;
    private bool OwnsCharacter => CharacterReady && Plugin.PlayerState.ContentId == ownerCharacter;
    private bool LostCharacter => !Plugin.ClientState.IsLoggedIn
        || (Plugin.PlayerState.ContentId != 0 && Plugin.PlayerState.ContentId != ownerCharacter);
    private static bool InDuty => Plugin.Condition[ConditionFlag.BoundByDuty] || Plugin.Condition[ConditionFlag.BoundByDuty56];

    public TaskEligibility GetEligibility(CharacterConfig config)
    {
        if (!config.EnableSilverChocoboFeathers)
            return TaskEligibility.Disabled("Silver Chocobo Feathers is off for this character.");
        if (config.SilverChocoboFeatherMode is not (SilverChocoboFeatherMode.Best or SilverChocoboFeatherMode.BestWeCanEquip))
            return TaskEligibility.Blocked("Select a Silver Chocobo Feather gear mode.");
        if (HasOwnedExchange)
            return TaskEligibility.Blocked("The previous ADS feather exchange is still settling.");
        if (!AdsLoaded)
            return TaskEligibility.Blocked("ADS is required for Silver Chocobo Feathers.");
        if (!CharacterReady || InDuty)
            return TaskEligibility.Blocked("A registered character outside a duty is required.");
        if (FeatherCount < FeathersPerExchange)
            return TaskEligibility.NotDue("Fewer than five Silver Chocobo Feathers in inventory.");
        return TaskEligibility.Runnable();
    }

    public void Start(CharacterConfig config)
    {
        if (IsRunning) return;

        IsFailed = false;
        verifiedExchanges = 0;
        if (!GetEligibility(config).IsRunnable) return;

        ownerCharacter = Plugin.PlayerState.ContentId;
        exchangeLimit = FeatherCount / FeathersPerExchange;
        var jobs = config.SilverChocoboFeatherMode == SilverChocoboFeatherMode.BestWeCanEquip ? ReadUnlockedJobs() : null;
        var eligible = ReadBundles()
            .Where(bundle => jobs == null || jobs.Any(job => job.Level >= bundle.Tier
                && job.Member.GetValue(bundle.First.ClassJobCategory.Value) is true
                && job.Member.GetValue(bundle.Second.ClassJobCategory.Value) is true))
            .ToArray();
        if (eligible.Length == 0)
        {
            Finish("No eligible five-feather gear bundle was found.");
            return;
        }

        var tier = eligible.Max(bundle => bundle.Tier);
        // Expert Delivery rewards scale with item level. Both pieces in each current tier share its item level.
        foreach (var bundle in eligible.Where(bundle => bundle.Tier == tier).OrderByDescending(bundle => bundle.CombinedItemLevel))
            remaining.Enqueue(bundle);

        IsRunning = true;
        StatusText = $"Selecting level {tier} gear";
    }

    public void Update()
    {
        if (!IsRunning) return;
        if (LostCharacter || InDuty || (OwnsCharacter && !plugin.ConfigManager.GetActiveConfig().EnableSilverChocoboFeathers))
            Cancel();

        if (pending is { } exchange)
        {
            Observe(exchange);
            return;
        }

        if (!IsRunning || !OwnsCharacter) return;
        var feathers = FeatherCount;
        if (verifiedExchanges == exchangeLimit || feathers < FeathersPerExchange)
        {
            Finish($"Complete: {verifiedExchanges} exchanges verified.");
            return;
        }

        if (!GameHelpers.IsPlayerAvailable() && !(shopHeldOpen && GameHelpers.IsAddonVisible("ShopExchangeItem")))
            return;

        while (remaining.TryDequeue(out var bundle))
        {
            var firstBefore = CountOwned(bundle.First.RowId);
            var secondBefore = CountOwned(bundle.Second.RowId);
            if ((bundle.First.IsUnique && firstBefore != 0) || (bundle.Second.IsUnique && secondBefore != 0))
                continue;

            var id = operationId ?? $"VMX-silver-feathers-{Guid.NewGuid():N}";
            if (!plugin.AdsIpcClient.StartCurrencyShopPurchase(id, bundle.First.RowId, 1, "Item",
                    FeatherItemId, FeathersPerExchange, out var failure, keepShopOpen: feathers >= 2 * FeathersPerExchange))
            {
                Finish(string.IsNullOrWhiteSpace(failure) ? "ADS declined the feather exchange." : failure, failed: true);
                return;
            }

            operationId = id;
            pending = new Exchange(bundle, feathers, firstBefore, secondBefore);
            StatusText = $"Exchanging five feathers for {bundle.Name}";
            return;
        }

        Finish($"{verifiedExchanges} exchanges verified. {feathers} feathers remain because no further distinct bundle is eligible.");
    }

    public void Cancel()
    {
        if (!IsRunning || cancelRequested) return;
        if (pending == null)
        {
            Finish("Silver Chocobo Feather exchange stopped.");
            return;
        }

        cancelRequested = true;
        remaining.Clear();
        StatusText = "Waiting for the owned ADS exchange to stop.";
        if (operationId is { } id)
            plugin.AdsIpcClient.CancelShopPurchase(id);
    }

    public void ObserveCleanup()
    {
        if (IsRunning && LostCharacter) Cancel();
        if (cancelRequested && pending is { } exchange) Observe(exchange);
    }

    private void Observe(Exchange exchange)
    {
        var result = plugin.AdsIpcClient.RefreshShopPurchase();
        if (!result.StatusReadable)
        {
            if (!AdsLoaded) Finish("ADS unloaded before the feather exchange was verified.", failed: true);
            return;
        }
        if (result.OperationId != operationId || result.ItemId != exchange.Bundle.First.RowId)
        {
            Finish("ADS no longer reports the owned feather exchange.", failed: true);
            return;
        }
        if (!result.IsTerminal) return;

        if (cancelRequested && (result.Succeeded != true || !OwnsCharacter))
        {
            Finish($"Stopped after {verifiedExchanges} verified exchanges.");
            return;
        }

        var bundle = exchange.Bundle;
        if (result.Succeeded != true || result.AcquiredQuantity != 1)
        {
            Finish(string.IsNullOrWhiteSpace(result.FailureMessage) ? "ADS did not verify the feather exchange." : result.FailureMessage, failed: true);
            return;
        }
        if (!OwnsCharacter) return;
        if (exchange.FeathersBefore - FeatherCount != FeathersPerExchange
            || CountOwned(bundle.First.RowId) - exchange.FirstBefore != 1
            || CountOwned(bundle.Second.RowId) - exchange.SecondBefore != 1)
        {
            Finish("Inventory did not confirm both items and the five-feather spend.", failed: true);
            return;
        }

        pending = null;
        shopHeldOpen = result.ShopHeldOpen;
        verifiedExchanges++;
        if (cancelRequested)
            Finish($"Stopped after {verifiedExchanges} verified exchanges.");
        else
            StatusText = $"Verified {verifiedExchanges}: {bundle.Name}";
    }

    private void Finish(string status, bool failed = false)
    {
        if (operationId is { } id)
            plugin.AdsIpcClient.CancelShopPurchase(id);
        operationId = null;
        shopHeldOpen = false;
        pending = null;
        remaining.Clear();
        cancelRequested = false;
        ownerCharacter = 0;
        IsRunning = false;
        IsFailed = failed;
        StatusText = failed ? $"Failed: {status}" : status;
    }

    private static int CountOwned(uint itemId) => Inventory->GetInventoryItemCount(itemId, false, false, true);

    private static IReadOnlyList<Bundle> ReadBundles()
    {
        var bundles = new List<Bundle>();
        if (!Plugin.DataManager.GetExcelSheet<TopicSelect>().TryGetRow(SalvagerSpoilsTopic, out var topic))
            return bundles;

        var shops = Plugin.DataManager.GetExcelSheet<SpecialShop>();
        var items = Plugin.DataManager.GetExcelSheet<Item>();
        foreach (var link in topic.Shop)
        {
            if (!link.Is<SpecialShop>() || !shops.TryGetRow(link.RowId, out var shop)) continue;
            foreach (var row in shop.Item)
            {
                var costs = row.ItemCosts.Where(cost => cost.ItemCost.RowId != 0 || cost.CurrencyCost != 0).ToArray();
                if (costs.Length != 1 || costs[0].ItemCost.RowId != FeatherItemId
                    || costs[0].CurrencyCost != FeathersPerExchange || costs[0].CostType != 0 || costs[0].CollectabilityCost != 0)
                    continue;

                var outputs = row.ReceiveItems.Where(output => output.Item.RowId != 0).ToArray();
                if (outputs.Length != 2 || outputs.Any(output => output.ReceiveCount != 1 || output.ReceiveHq)
                    || outputs[0].Item.RowId == outputs[1].Item.RowId
                    || !items.TryGetRow(outputs[0].Item.RowId, out var first)
                    || !items.TryGetRow(outputs[1].Item.RowId, out var second)
                    || first.EquipSlotCategory.RowId == 0 || second.EquipSlotCategory.RowId == 0
                    || first.LevelEquip == 0 || first.LevelEquip != second.LevelEquip)
                    continue;

                bundles.Add(new Bundle(first, second));
            }
        }

        // ADS takes the first item as the request key. Skip rows with ambiguous keys.
        return bundles.GroupBy(bundle => bundle.First.RowId)
            .Where(group => group.Count() == 1)
            .Select(group => group.Single())
            .ToArray();
    }

    private static List<(PropertyInfo Member, int Level)> ReadUnlockedJobs()
    {
        var result = new List<(PropertyInfo Member, int Level)>();
        var state = PlayerState.Instance();
        if (state == null || !state->IsLoaded) return result;

        var quests = Plugin.DataManager.GetExcelSheet<Quest>();
        foreach (var job in Plugin.DataManager.GetExcelSheet<ClassJob>())
        {
            if (job.RowId == 0) continue;
            var level = state->GetClassJobLevel((int)job.RowId, false);
            if (level == 0) continue;
            // Jobs with a base class can share its level before the job itself is unlocked.
            if (job.UnlockQuest.RowId != 0 && (!quests.TryGetRow(job.UnlockQuest.RowId, out var quest)
                || !Plugin.UnlockState.IsQuestCompleted(quest)))
                continue;

            var member = typeof(ClassJobCategory).GetProperty(job.Abbreviation.ExtractText(),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            if (member?.PropertyType == typeof(bool)) result.Add((member, level));
        }
        return result;
    }
}
