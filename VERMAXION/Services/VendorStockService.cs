using System;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using Newtonsoft.Json.Linq;
using VERMAXION.IPC;

namespace VERMAXION.Services;

public sealed class VendorStockService
{
    private const uint GysahlGreensItemId = 4868;
    private const uint Grade8DarkMatterItemId = 33916;
    private const uint VersatileLureItemId = 29717;
    private readonly IPluginLog log;
    private readonly ConfigManager configManager;
    private ulong owner;
    private string? operationId;
    private uint purchasingItem;
    private int purchasingQuantity;
    private int purchasingTarget;
    private int versatileLureTarget;
    private bool lureOnly;
    private DateTime nextObservation;
    private string reason = "Idle";

    public enum VendorStockState { Idle, CheckingNeeds, AcquiringThroughAds, Complete, Failed }

    public VendorStockState State { get; private set; }
    public bool IsActive => State is VendorStockState.CheckingNeeds or VendorStockState.AcquiringThroughAds;
    public bool IsComplete => State == VendorStockState.Complete;
    public bool IsFailed => State == VendorStockState.Failed;
    public string StatusText => reason;

    public VendorStockService(ICommandManager commandManager, IPluginLog log, ConfigManager configManager, VNavmeshIPC vnavmesh)
    {
        this.log = log;
        this.configManager = configManager;
    }

    public void Start() => Begin(0, false);
    public void StartVersatileLureRestock(int target) => Begin(target, true);
    internal void StartVersatileLureRestockDockside(int target) => Begin(target, true);
    public void RunTask() => Start();

    private void Begin(int lureTarget, bool onlyLures)
    {
        if (IsActive) return;
        owner = Plugin.PlayerState.ContentId;
        if (owner == 0) { SetState(VendorStockState.Failed, "No current character."); return; }
        operationId = null;
        versatileLureTarget = Math.Max(0, lureTarget);
        lureOnly = onlyLures;
        nextObservation = DateTime.MinValue;
        SetState(VendorStockState.CheckingNeeds, "Checking inventory targets before requesting ADS");
    }

    public void Reset()
    {
        CancelOwnedPurchase();
        operationId = null;
        owner = 0;
        State = VendorStockState.Idle;
        reason = "Idle";
    }

    public void Update()
    {
        if (!IsActive || DateTime.UtcNow < nextObservation) return;
        nextObservation = DateTime.UtcNow.AddSeconds(1);
        if (owner == 0 || owner != Plugin.PlayerState.ContentId)
        { Fail("Character changed; stock acquisition cancelled."); return; }
        try
        {
            if (State == VendorStockState.CheckingNeeds)
            {
                var config = configManager.GetActiveConfig();
                if (!lureOnly && RequestMissing(GysahlGreensItemId, config.VendorStockGysahlGreensTarget)) return;
                if (!lureOnly && RequestMissing(Grade8DarkMatterItemId, config.VendorStockGrade8DarkMatterTarget)) return;
                if (RequestMissing(VersatileLureItemId, versatileLureTarget)) return;
                SetState(VendorStockState.Complete, "Inventory meets all requested stock targets");
                return;
            }

            var status = ReadStatus();
            if ((bool?)status["companyAction"] == true || (string?)status["operationId"] != operationId || (uint?)status["itemId"] != purchasingItem ||
                (int?)status["requestedQuantity"] != purchasingQuantity)
            { Fail("ADS no longer reports the owned stock purchase; no request repeated."); return; }
            if ((bool?)status["running"] == true)
            { reason = $"ADS: {(string?)status["statusMessage"]}"; return; }
            if ((bool?)status["done"] != true || (bool?)status["succeeded"] != true)
            { Fail($"ADS stock purchase did not succeed: {(string?)status["statusMessage"]}"); return; }
            operationId = null;
            if (GameHelpers.GetInventoryItemCount(purchasingItem) < purchasingTarget)
            { Fail("ADS completed, but the requested inventory target is not present; no purchase repeated."); return; }
            SetState(VendorStockState.CheckingNeeds, "ADS purchase verified; checking remaining stock targets");
        }
        catch (Exception ex) { Fail($"ADS stock acquisition unavailable: {ex.Message}"); }
    }

    private bool RequestMissing(uint itemId, int target)
    {
        if (target <= 0) return false;
        var missing = (long)target - GameHelpers.GetInventoryItemCount(itemId);
        if (missing <= 0) return false;
        if (missing > 9999) { Fail("ADS accepts at most 9,999 additional items per request. Reduce the stock target."); return true; }
        purchasingItem = itemId;
        purchasingQuantity = (int)missing;
        purchasingTarget = target;
        operationId = Guid.NewGuid().ToString("N");
        SetState(VendorStockState.AcquiringThroughAds, $"Requesting {missing} of item {itemId} through ADS");
        var accepted = Plugin.PluginInterface.GetIpcSubscriber<string, uint, int, bool>("ADS.StartGilShopPurchase")
            .InvokeFunc(operationId, itemId, purchasingQuantity);
        if (!accepted)
        {
            operationId = null;
            Fail($"ADS rejected stock acquisition: {(string?)ReadStatus()["lastStartError"]}");
        }
        return true;
    }

    private static JObject ReadStatus() => JObject.Parse(Plugin.PluginInterface
        .GetIpcSubscriber<string>("ADS.GetShopPurchaseStatusJson").InvokeFunc());

    private void CancelOwnedPurchase()
    {
        if (operationId == null) return;
        try { Plugin.PluginInterface.GetIpcSubscriber<string, bool>("ADS.CancelShopPurchase").InvokeFunc(operationId); }
        catch (Exception ex) { log.Warning($"[VendorStock] ADS cancellation remains unresolved: {ex.Message}"); }
    }

    private void Fail(string message)
    {
        CancelOwnedPurchase();
        SetState(VendorStockState.Failed, message);
    }

    private void SetState(VendorStockState state, string message)
    {
        log.Information($"[VendorStock] {State} -> {state}: {message}");
        State = state;
        reason = message;
    }
}
