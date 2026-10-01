using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using VERMAXION.Models;

namespace VERMAXION.Services;

internal sealed unsafe class FishCollectionSupplies(Plugin plugin)
{
    private readonly Queue<CollectionSupply> remaining = new();
    private readonly List<(string Kind, uint Item, uint Bundle, uint Cost)> vendors = [];
    private CollectionSupply? current;
    private string operationId = "", marketOrderId = "";
    private int beforeCount, requested;
    private long gilBefore;
    private ulong ownerCharacter;
    private DateTimeOffset dispatched;
    private bool marketAttempted, cancelling;
    private bool cancellationSent;
    public List<CollectionSupply> Supplies { get; } = [];
    public List<string> PartialFailures { get; } = [];
    public long GilSpent { get; private set; }
    public string Status { get; private set; } = "";
    public bool IsComplete { get; private set; }
    public bool HasOwnedPurchase => operationId.Length > 0 || marketOrderId.Length > 0;
    public string Failure { get; private set; } = "";

    public void Begin(IEnumerable<CollectionSupply> supplies)
    {
        if (HasOwnedPurchase) throw new InvalidOperationException("The previous owned purchase has not settled.");
        Supplies.Clear(); Supplies.AddRange(supplies);
        remaining.Clear(); foreach (var supply in Supplies) remaining.Enqueue(supply);
        PartialFailures.Clear(); GilSpent = 0; Failure = ""; current = null;
        ownerCharacter = Plugin.PlayerState.ContentId;
        IsComplete = cancelling = cancellationSent = false;
    }
    private static int Count(CollectionSupply supply) => FishCollectionObservation.Count(supply.ItemId, supply.Hq);
    private static long Gil => InventoryManager.Instance() is var inventory && inventory != null
        ? inventory->GetGil() : throw new InvalidOperationException("Gil balance is unavailable.");
    public void Update()
    {
        if (IsComplete && !HasOwnedPurchase) return;
        if (HasOwnedPurchase) { ObservePurchase(); return; }
        if (cancelling) { IsComplete = true; return; }
        if (current == null)
        {
            if (!remaining.TryDequeue(out current)) { IsComplete = true; return; }
            vendors.Clear(); marketAttempted = false;
            if (Count(current) >= current.Target) { current = null; return; }
            FindVendors();
        }
        var deficit = Math.Max(0, current.Target - Count(current));
        if (deficit == 0) { current = null; return; }
        var budget = Math.Max(0, plugin.Configuration.FishCollection.PreparationGilLimit - GilSpent);
        while (vendors.Count > 0)
        {
            var vendor = vendors[0]; vendors.RemoveAt(0);
            var quantity = FishCollectionPolicy.BundleQuantity(deficit, vendor.Bundle);
            var cost = checked((long)quantity / vendor.Bundle * vendor.Cost);
            if (vendor.Kind.Equals("gil", StringComparison.OrdinalIgnoreCase) && (cost > budget || cost > Gil ||
                (long)vendor.Cost > (long)current.UnitLimit * vendor.Bundle)) continue;
            var id = $"VMX-collection-{Guid.NewGuid():N}";
            beforeCount = Count(current); gilBefore = Gil;
            plugin.FishingRunLifecycle.SuspendYesAlreadyPauseForShopping("collection supplies");
            if (!plugin.AdsIpcClient.StartCurrencyShopPurchase(id, current.ItemId, quantity,
                vendor.Kind, vendor.Item, cost, out var reason))
            { Status = $"Vendor unavailable: {reason}"; continue; }
            operationId = id; requested = quantity; dispatched = DateTimeOffset.UtcNow;
            cancellationSent = false;
            Status = $"ADS: {current.Name}, {quantity}, {vendor.Kind}:{vendor.Item}, maximum {cost}";
            return;
        }
        if (!marketAttempted)
        {
            marketAttempted = true;
            var item = Plugin.DataManager.GetExcelSheet<Item>().GetRow(current.ItemId);
            if (!Plugin.Condition[ConditionFlag.OnFreeTrial] && !item.IsUntradable && current.UnitLimit > 0 && budget > 0 && Gil > 0)
            {
                try
                {
                if (Plugin.PluginInterface.GetIpcSubscriber<int>("Emptor.ApiVersion").InvokeFunc() != 5)
                    throw new InvalidOperationException("Collection purchases require Emptor API 5.");
                if (Plugin.PluginInterface.GetIpcSubscriber<bool>("Emptor.IsBusy").InvokeFunc())
                { FinishItem("Emptor is already busy"); return; }
                var id = $"VMX-collection-{Guid.NewGuid():N}";
                var json = FishCollectionPolicy.MarketRequestJson(id,
                    Plugin.ObjectTable.LocalPlayer!.CurrentWorld.Value.Name.ExtractText(), current, deficit, budget, Gil);
                beforeCount = Count(current); gilBefore = Gil;
                using var response = JsonDocument.Parse(Plugin.PluginInterface.GetIpcSubscriber<string, string>("Emptor.SubmitOrder").InvokeFunc(json));
                var root = response.RootElement;
                if (root.TryGetProperty("orderId", out var order) && !string.IsNullOrWhiteSpace(order.GetString()) &&
                    root.TryGetProperty("clientRequestId", out var correlation) && correlation.GetString() == id)
                {
                    marketOrderId = order.GetString()!; operationId = id; requested = deficit;
                    cancellationSent = false;
                    dispatched = DateTimeOffset.UtcNow; Status = $"Emptor: {current.Name}, {deficit} {current.Quality}";
                    return;
                }
                Status = root.TryGetProperty("message", out var message) ? message.GetString() ?? "Order rejected" : "Order rejected";
                }
                catch (Exception error) when (!HasOwnedPurchase)
                { Status = "Emptor unavailable: " + error.GetBaseException().Message; }
            }
        }
        FinishItem(Status.Length == 0 ? "No affordable accessible supplier" : Status);
    }
    private void FindVendors()
    {
        // ADS supplies exact currency identities and enforces native access and balances at start.
        // Shops offer NQ food; an HQ meal is never substituted with a vendor's NQ output.
        if (current!.Hq == true) return;
        try
        {
            var query = JsonSerializer.Serialize(new { version = 1, query = current.ItemId.ToString(), limit = 100 });
            using var result = JsonDocument.Parse(Plugin.PluginInterface.GetIpcSubscriber<string, string>("ADS.SearchShopCatalogJson").InvokeFunc(query));
            foreach (var row in result.RootElement.GetProperty("rows").EnumerateArray())
            {
                if (row.GetProperty("itemId").GetUInt32() != current.ItemId) continue;
                var kind = row.GetProperty("currencyKind").GetString()!;
                var identity = row.GetProperty("currencyItemId").GetUInt32();
                var bundle = row.GetProperty("receiveCount").GetUInt32();
                var cost = row.GetProperty("currencyCostPerTransaction").GetUInt32();
                if (bundle > 0) vendors.Add((kind, identity, bundle, cost));
            }
            var ordered = vendors.Distinct().OrderBy(v => v.Kind.Equals("gil", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(v => (decimal)v.Cost / v.Bundle).ToArray();
            vendors.Clear(); vendors.AddRange(ordered);
        }
        catch (Exception error) { Status = $"ADS catalog unavailable: {error.GetBaseException().Message}"; }
    }
    private void ObservePurchase()
    {
        if (marketOrderId.Length > 0)
        {
            using var result = JsonDocument.Parse(Plugin.PluginInterface.GetIpcSubscriber<string, string>("Emptor.GetOrder").InvokeFunc(marketOrderId));
            var root = result.RootElement;
            if (root.GetProperty("orderId").GetString() != marketOrderId || root.GetProperty("clientRequestId").GetString() != operationId)
                throw new InvalidOperationException("Emptor order ownership could not be verified; cleanup retains the owned order ID.");
            if (!root.TryGetProperty("finishedUtc", out var finished) || finished.ValueKind == JsonValueKind.Null)
            { CheckTimeout(); return; }
            var spent = root.GetProperty("totalGilSpent").GetInt64();
            if (spent >= 0 && Plugin.ClientState.IsLoggedIn && Plugin.PlayerState.ContentId == ownerCharacter)
                spent = Math.Max(spent, gilBefore - Gil); // Native debit includes purchase tax even if a provider quote omitted it.
            CompletePurchase(spent, root.TryGetProperty("message", out var message) ? message.GetString() ?? "" : "");
        }
        else
        {
            var result = plugin.AdsIpcClient.RefreshShopPurchase();
            if (!result.StatusReadable || result.OperationId != operationId)
            { CheckTimeout(); return; }
            if (!result.IsTerminal) { CheckTimeout(); return; }
            CompletePurchase(Math.Max(0, gilBefore - Gil), result.FailureMessage);
        }
    }
    private void CheckTimeout()
    {
        if (DateTimeOffset.UtcNow - dispatched < TimeSpan.FromMinutes(5)) return;
        Cancel(); Status = "Supply provider timed out; waiting for owned cancellation and navigation to settle";
    }
    private void CompletePurchase(long spent, string reason)
    {
        if (cancelling && (!Plugin.ClientState.IsLoggedIn || !plugin.IsCharacterRegistered || Plugin.PlayerState.ContentId != ownerCharacter))
        {
            operationId = marketOrderId = "";
            Status = "Owned provider stopped after disconnect; inventory receipt remains unverified";
            IsComplete = true; return;
        }
        var received = Count(current!) - beforeCount;
        GilSpent = checked(GilSpent + spent);
        operationId = marketOrderId = "";
        if (spent < 0 || GilSpent > plugin.Configuration.FishCollection.PreparationGilLimit)
            throw new InvalidOperationException("Verified preparation spending exceeded its limit.");
        if (received < 0 || received > requested)
            throw new InvalidOperationException("Supply inventory receipt did not match the owned request.");
        if (Count(current!) >= current!.Target) current = null;
        else if (marketAttempted) FinishItem($"Partial acquisition ({received}/{requested}). {reason}");
        else Status = reason; // Try another distinct currency, then the current-world market.
    }
    private void FinishItem(string reason)
    {
        var supply = current!;
        PartialFailures.Add($"{supply.Name}: {reason}");
        if (!supply.ReadyForAttempt(Count(supply)))
        { Failure = $"Missing mandatory {supply.Name}: {reason}"; IsComplete = true; remaining.Clear(); }
        current = null;
    }
    public bool Cancel()
    {
        cancelling = true; remaining.Clear();
        if (cancellationSent) return !HasOwnedPurchase;
        if (marketOrderId.Length > 0)
            Plugin.PluginInterface.GetIpcSubscriber<string, bool>("Emptor.CancelOrder").InvokeFunc(marketOrderId);
        else if (operationId.Length > 0) plugin.AdsIpcClient.CancelShopPurchase(operationId);
        cancellationSent = HasOwnedPurchase;
        if (HasOwnedPurchase) return false;
        IsComplete = true; return true;
    }
}
