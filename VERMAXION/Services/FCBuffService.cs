using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using Dalamud.Game.Command;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using ECommons.UIHelpers.AddonMasterImplementations;
using Lumina.Excel.Sheets;
using VERMAXION.IPC;
using VERMAXION.Models;

namespace VERMAXION.Services;

public class FCBuffService : IDisposable
{
    private readonly ICommandManager commandManager;
    private readonly IPluginLog log;
    private readonly ConfigManager configManager;
    private readonly YesAlreadyIPC yesAlready;
    private readonly Plugin plugin;

    private const int BaseMinGil = 16000; // Base minimum gil required
    private const string ContextMenuAddonName = "ContextMenu";
    private const uint SealSweetenerStatusId = 414;
    private const ushort SealSweetenerTwoMinimumStrength = 10;
    private const uint SealSweetenerTwoCompanyActionId = 36;
    private const uint ActivateEntryAddonRowId = 2817;
    private static readonly TimeSpan WindowCloseRetryInterval = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan WindowCloseLogThrottle = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan WindowCloseTimeout = TimeSpan.FromSeconds(20);
    private static readonly string[] FcCleanupAddonNames = ["SelectYesno", ContextMenuAddonName, "FreeCompanyAction", "FreeCompany"];
    private static readonly string[] FcCallbackCloseAddonNames = [ContextMenuAddonName, "FreeCompanyAction", "FreeCompany"];

    private FCBuffState state = FCBuffState.Idle;
    private DateTime stateEnteredAt = DateTime.MinValue;
    private int purchaseAttempts = 0;
    private int buyMax = 15;
    private DateTime lastWindowCloseAttemptAt = DateTime.MinValue;
    private DateTime lastWindowCloseLogAt = DateTime.MinValue;
    private bool failAfterClosingWindows = false;
    private ulong currentFreeCompanyId;
    private bool reconciliationRequired;
    private bool resumeAfterPurchase;
    private bool allowActivation;
    private bool maintainStockTarget;
    private int inactiveActionCapacity;
    private bool yesAlreadyPauseOwned;
    private int activationAttempts;
    private int lastSealSweetenerListIndex = -1;
    private string? purchaseOperationId;
    private DateTime nextPurchaseObservation;


    public enum FCBuffState
    {
        Idle,
        CheckingFCPoints,
        OpeningFCWindow,
        WaitingForFCCommand,
        WaitingForFCWindow,
        CheckingFCPointsInWindow,
        CheckingBuffInventory,
        CheckingIfRefillNeeded,
        AcquiringThroughAds,
        ClosingWindows,
        ActivatingBuff,
        WaitingForActivationMenu,
        WaitingForActivation,
        Complete,
        Failed
    }

    public FCBuffState State => state;
    public bool IsActive => state != FCBuffState.Idle && state != FCBuffState.Complete && state != FCBuffState.Failed;
    public bool IsComplete => state == FCBuffState.Complete;
    public bool IsFailed => state == FCBuffState.Failed;
    public string StatusText => state.ToString();
    internal bool CompletedViaRankOneToSevenShortcut { get; private set; }

    public FCBuffService(ICommandManager commandManager, IPluginLog log, IClientState clientState, ICondition condition, IObjectTable objects, ITargetManager targetManager, ConfigManager configManager, YesAlreadyIPC yesAlready, Plugin plugin)
    {
        this.commandManager = commandManager;
        this.log = log;
        this.configManager = configManager;
        this.yesAlready = yesAlready;
        this.plugin = plugin;
    }

    public unsafe void Start(int maxAttempts = 2)
    {
        if (IsActive) return;

        if (!Plugin.PluginInterface.InstalledPlugins.Any(candidate => candidate.IsLoaded && candidate.InternalName == "ADS"))
        {
            log.Warning("[FCBuff] ADS is required for FC Buff Refill. Install and enable ADS before starting this task.");
            SetState(FCBuffState.Failed);
            return;
        }

        CancelOwnedPurchase();
        purchaseOperationId = null;
        nextPurchaseObservation = DateTime.MinValue;

        CompletedViaRankOneToSevenShortcut = false;
        var freeCompanyRank = GetCurrentFreeCompanyRank();
        if (FCBuffRecoveryPolicy.UsesRankOneToSevenShortcut(freeCompanyRank))
        {
            CompletedViaRankOneToSevenShortcut = true;
            log.Information($"[FCBuff] Free Company rank {freeCompanyRank} cannot purchase Seal Sweetener actions; completing without purchase work.");
            SetState(FCBuffState.Complete);
            return;
        }

        var capacity = FcBuffStockPolicy.InactiveActionCapacityForRank(freeCompanyRank);
        if (!capacity.HasValue)
        {
            log.Error($"[FCBuff] Free Company rank/capacity could not be read (rank: {freeCompanyRank?.ToString() ?? "unknown"}); stopping before purchase work.");
            SetState(FCBuffState.Failed);
            return;
        }
        inactiveActionCapacity = capacity.Value;

        // Force load config from file to get latest values
        configManager.LoadAllAccounts();
        log.Information("[FCBuff] Forced config load - getting latest values from file");

        // Get config AFTER loading to ensure we have the latest values
        var config = configManager.GetActiveConfig();
        log.Information($"[FCBuff] Config Debug: CurrentAccountId='{configManager.CurrentAccountId}', SelectedCharacterKey='{configManager.SelectedCharacterKey}'");
        log.Information($"[FCBuff] Task Start Config: FCBuffMinPoints={config.FCBuffMinPoints:N0}, FCBuffPurchaseAttempts={config.FCBuffPurchaseAttempts}");
        log.Information($"[FCBuff] Task Start Config: FCBuffMinGil={config.FCBuffMinGil:N0}, AllowFCBuffActivation={config.AllowFCBuffActivation}, MaintainFCBuffStockTarget={config.MaintainFCBuffStockTarget}");

        purchaseAttempts = FCBuffRecoveryPolicy.ClampPurchaseAttempts(config.FCBuffPurchaseAttempts);
        failAfterClosingWindows = false;
        ResetWindowCloseTracking();

        currentFreeCompanyId = GetCurrentFreeCompanyId();
        allowActivation = config.AllowFCBuffActivation;
        maintainStockTarget = config.MaintainFCBuffStockTarget;
        plugin.Configuration.FcActionStockByFreeCompanyId.TryGetValue(currentFreeCompanyId, out var cachedStock);
        reconciliationRequired = maintainStockTarget ||
                                 FcBuffStockPolicy.Decide(
                                     allowActivation,
                                     sealSweetenerTwoAlreadyActive: false,
                                     cachedStock,
                                     reconciliationRequired: !allowActivation || currentFreeCompanyId == 0 || cachedStock == null || cachedStock.KnownSealSweetenerTwoCount <= 0)
                                 == FcBuffStockAction.Reconcile;
        buyMax = 0;
        resumeAfterPurchase = false;
        activationAttempts = 0;
        lastSealSweetenerListIndex = -1;
        SetState(FCBuffState.CheckingFCPoints);
        log.Information($"[FCBuff] Starting FC buff refill (max attempts: {purchaseAttempts})");
    }

    internal unsafe int? GetCurrentFreeCompanyRank()
    {
        var freeCompany = InfoProxyFreeCompany.Instance();
        return freeCompany == null ? null : freeCompany->Rank;
    }

    public void RunTask()
    {
        log.Information("[VERMAXION] Manual FC Buff Refill triggered");
        if (!TryAcquireManualYesAlreadyPause())
        {
            log.Error("[FCBuff] Could not acquire the VERMAXION YesAlready pause for the manual run.");
            SetState(FCBuffState.Failed);
            return;
        }
        var config = configManager.GetActiveConfig();
        Start(config.FCBuffPurchaseAttempts);
    }

    public unsafe void TestFreeCompanyGC()
    {
        log.Information("[VERMAXION] Testing Free Company Grand Company detection");

        try
        {
            // Using the pattern from Jaksuhn's SND and XA docs
            var infoProxyFreeCompany = InfoProxyFreeCompany.Instance();
            if (infoProxyFreeCompany != null)
            {
                var fcGrandCompany = infoProxyFreeCompany->GrandCompany;
                var gcString = fcGrandCompany.ToString();

                log.Information($"[FCBuff] Free Company Grand Company: {gcString}");

                // GC names mapping from Jaksuhn's SND
                var gcNames = new Dictionary<string, int>
                {
                    { "Maelstrom", 1 },
                    { "TwinAdder", 2 },
                    { "ImmortalFlames", 3 }
                };

                int gcChoice = 1; // Default to Maelstrom
                foreach (var gc in gcNames)
                {
                    if (gc.Key == gcString)
                    {
                        gcChoice = gc.Value;
                        break;
                    }
                }

                log.Information($"[FCBuff] GC Choice: {gcChoice} ({gcString})");

                // Also log player's current Grand Company for reference
                var playerState = PlayerState.Instance();
                log.Information($"[FCBuff] Player Grand Company: {playerState->GrandCompany}");
            }
            else
            {
                log.Error("[FCBuff] InfoProxyFreeCompany is null");
            }
        }
        catch (Exception ex)
        {
            log.Error($"[FCBuff] Error testing Free Company GC: {ex.Message}");
        }
    }

    public void Reset()
    {
        CancelOwnedPurchase();
        purchaseOperationId = null;
        SetState(FCBuffState.Idle);
    }

    public void Dispose()
    {
        CancelOwnedPurchase();
        ReleaseOwnedYesAlreadyPause("service disposal");
    }

    public unsafe void Update()
    {
        if (!IsActive) return;

        var elapsed = (DateTime.UtcNow - stateEnteredAt).TotalSeconds;

        switch (state)
        {
            case FCBuffState.CheckingFCPoints:
                if (elapsed < 1) return;
                if (allowActivation && IsSealSweetenerTwoActive() && !maintainStockTarget)
                {
                    log.Information("[FCBuff] Seal Sweetener II is already active; stock is unchanged.");
                    SetState(FCBuffState.Complete);
                    break;
                }
                if (currentFreeCompanyId == 0)
                {
                    log.Error("[FCBuff] No Free Company ID is available for stock ledger ownership.");
                    SetState(FCBuffState.Failed);
                    break;
                }
                SetState(FCBuffState.OpeningFCWindow);
                break;

            case FCBuffState.OpeningFCWindow:
                if (elapsed < 1) return;
                log.Information("[FCBuff] Opening FC window: /freecompanycmd");
                CommandHelper.SendCommand("/freecompanycmd");
                SetState(FCBuffState.WaitingForFCCommand);
                break;

            case FCBuffState.WaitingForFCCommand:
                if (elapsed < 3)
                {
                    // Check if FC window is ready
                    if (GameHelpers.IsAddonVisible("FreeCompany"))
                    {
                        if (elapsed < 1)
                        {
                            // Wait 1 second for window data to populate
                            return;
                        }

                        log.Information("[FCBuff] Switching to Actions tab");
                        log.Information("[FCBuff] [Callback] Firing FreeCompany with args (true, 0, 4)");
                        GameHelpers.FireAddonCallback("FreeCompany", true, 0, 4);
                        SetState(FCBuffState.WaitingForFCWindow);
                    }
                    return;
                }
                // Try opening again if first attempt failed
                if (elapsed < 6)
                {
                    log.Information("[FCBuff] First attempt failed, trying again");
                    CommandHelper.SendCommand("/freecompanycmd");
                    SetState(FCBuffState.WaitingForFCCommand);
                    return;
                }
                log.Error("[FCBuff] Failed to open FC window");
                SetState(FCBuffState.Failed);
                break;

            case FCBuffState.WaitingForFCWindow:
                if (elapsed < 2) return;
                if (GameHelpers.IsAddonVisible("FreeCompanyAction"))
                {
                    if (reconciliationRequired)
                    {
                        log.Information("[FCBuff] FC Action window appeared; reconciling stock.");
                        SetState(FCBuffState.CheckingBuffInventory);
                    }
                    else
                    {
                        log.Information("[FCBuff] FC Action window appeared; activating from positive cached stock.");
                        SetState(FCBuffState.ActivatingBuff);
                    }
                }
                else if (elapsed > 5)
                {
                    log.Error("[FCBuff] FC Action window did not appear");
                    SetState(FCBuffState.Failed);
                }
                break;

            case FCBuffState.CheckingFCPointsInWindow:
                if (elapsed < 1) return;
                // Get FC points from the window
                var fcProxy = InfoProxyFreeCompany.Instance();
                if (fcProxy != null && fcProxy->Id != 0)
                {
                    // FC points are at node 1,4,16,17 in the FC window
                    var fcPointsNode = GameHelpers.GetFCPointsNode();
                    var fcPoints = fcPointsNode ?? 0;
                    log.Information($"[FCBuff] Current FC points: {fcPoints:N0}");

                    // Check if we have enough FC points
                    var config = configManager.GetActiveConfig();
                    var minFCPoints = config.FCBuffMinPoints;
                    if (fcPoints < minFCPoints)
                    {
                        log.Information($"[FCBuff] Not enough FC points ({fcPoints:N0} < {minFCPoints:N0}), skipping refill");
                        SetState(FCBuffState.Complete);
                        return;
                    }

                    SetState(FCBuffState.CheckingBuffInventory);
                }
                else
                {
                    if (elapsed > 5)
                    {
                        log.Error("[FCBuff] Timeout waiting for FC window");
                        SetState(FCBuffState.Failed);
                    }
                }
                break;

            case FCBuffState.CheckingBuffInventory:
                if (elapsed < 1) return;
                log.Information("[FCBuff] Checking FC buff inventory for Seal Sweetener II");

                try
                {
                    // Use the FCBuffInventoryService to count buffs
                    var inventoryRead = ReadSealSweetenerBuffs();
                    if (inventoryRead.Status != FcActionInventoryReadStatus.Success)
                    {
                        log.Error($"[FCBuff] FC action inventory read failed: {inventoryRead.Failure}");
                        SetState(FCBuffState.Failed);
                        break;
                    }

                    var sealSweetenerCount = inventoryRead.Count;
                    FcBuffStockPolicy.ApplyReconciliation(
                        plugin.Configuration.FcActionStockByFreeCompanyId,
                        currentFreeCompanyId,
                        inventoryRead,
                        DateTime.UtcNow);
                    plugin.Configuration.Save();
                    reconciliationRequired = false;
                    log.Information($"[FCBuff] Reconciled Seal Sweetener II count: {sealSweetenerCount}");

                    var alreadyActive = IsSealSweetenerTwoActive();
                    var willActivate = allowActivation && !alreadyActive;
                    buyMax = FcBuffStockPolicy.RequiredPurchaseQuantity(
                        maintainStockTarget,
                        purchaseAttempts,
                        inactiveActionCapacity,
                        sealSweetenerCount,
                        willActivate);
                    if (buyMax > 0)
                    {
                        log.Information(
                            maintainStockTarget
                                ? $"[FCBuff] Found {sealSweetenerCount} Seal Sweetener II; buying {buyMax} to finish at effective stock target {Math.Min(purchaseAttempts, inactiveActionCapacity)}/{inactiveActionCapacity}{(willActivate ? " after one activation" : string.Empty)}."
                                : "[FCBuff] No Seal Sweetener II found, proceeding with refill");
                        SetState(FCBuffState.CheckingIfRefillNeeded);
                    }
                    else if (willActivate)
                    {
                        log.Information($"[FCBuff] Found {sealSweetenerCount} Seal Sweetener II; activating one.");
                        SetState(FCBuffState.ActivatingBuff);
                    }
                    else if (maintainStockTarget && alreadyActive)
                    {
                        log.Information($"[FCBuff] Seal Sweetener II is already active and effective stock target {Math.Min(purchaseAttempts, inactiveActionCapacity)}/{inactiveActionCapacity} is satisfied; completing without another activation.");
                        SetState(FCBuffState.Complete);
                    }
                    else
                    {
                        log.Information($"[FCBuff] Found {sealSweetenerCount} Seal Sweetener II; activation is disabled, completing without consuming stock.");
                        SetState(FCBuffState.Complete);
                    }
                }
                catch (Exception ex)
                {
                    log.Error($"[FCBuff] Error checking buff inventory: {ex.Message}");
                    SetState(FCBuffState.Failed);
                }
                break;

            case FCBuffState.CheckingIfRefillNeeded:
                if (elapsed < 1) return;
                var purchaseFcPointsNode = GameHelpers.GetFCPointsNode();
                if (!purchaseFcPointsNode.HasValue)
                {
                    log.Error("[FCBuff] FC points could not be read while a purchase was necessary.");
                    SetState(FCBuffState.Failed);
                    return;
                }
                var activeConfig = configManager.GetActiveConfig();
                if (purchaseFcPointsNode.Value < activeConfig.FCBuffMinPoints)
                {
                    log.Information(
                        $"[FCBuff] Not enough FC points ({purchaseFcPointsNode.Value:N0} < {activeConfig.FCBuffMinPoints:N0}); purchase skipped.");
                    SetState(FCBuffState.Complete);
                    return;
                }
                var minGil = Math.Max(BaseMinGil, activeConfig.FCBuffMinGil);
                var gil = GameHelpers.GetInventoryItemCount(1);
                log.Information($"[FCBuff] Current gil: {gil:N0}");
                if (gil < minGil)
                {
                    log.Information($"[FCBuff] Not enough gil ({gil:N0} < {minGil:N0}), skipping refill");
                    SetState(FCBuffState.Complete);
                    return;
                }
                log.Information("[FCBuff] Proceeding with FC buff refill");
                SetState(FCBuffState.AcquiringThroughAds);
                break;

            case FCBuffState.AcquiringThroughAds:
                TickAdsPurchase();
                break;

            case FCBuffState.ClosingWindows:
                TickClosingWindows(elapsed);
                break;

            case FCBuffState.ActivatingBuff:
                if (elapsed < 0.5)
                    return;
                if (!allowActivation)
                {
                    log.Warning("[FCBuff] Activation state was reached while activation is disabled; forcing live stock reconciliation.");
                    reconciliationRequired = true;
                    lastSealSweetenerListIndex = -1;
                    SetState(FCBuffState.CheckingBuffInventory);
                    break;
                }
                if (lastSealSweetenerListIndex < 0)
                {
                    var read = ReadSealSweetenerBuffs();
                    if (read.Status != FcActionInventoryReadStatus.Success || read.Count <= 0)
                    {
                        reconciliationRequired = true;
                        SetState(FCBuffState.CheckingBuffInventory);
                        break;
                    }
                }
                activationAttempts++;
                log.Information(
                    $"[FCBuff] Opening Seal Sweetener II context menu for stock row {lastSealSweetenerListIndex}; attempt={activationAttempts}");
                if (!GameHelpers.TryFireAddonCallback(
                    "FreeCompanyAction",
                    true,
                    1,
                    (uint)Math.Max(0, lastSealSweetenerListIndex)))
                {
                    HandleUnverifiedActivation("The FreeCompanyAction context-menu callback failed.");
                    break;
                }
                SetState(FCBuffState.WaitingForActivationMenu);
                break;

            case FCBuffState.WaitingForActivationMenu:
                if (!GameHelpers.IsAddonVisible(ContextMenuAddonName))
                {
                    if (elapsed < 10)
                        return;

                    HandleUnverifiedActivation("The FC action context menu did not appear.");
                    break;
                }

                if (elapsed < 0.5)
                    return;

                if (!TrySelectLocalizedActivateEntry(out var activationMenuDetail))
                {
                    HandleUnverifiedActivation(activationMenuDetail);
                    break;
                }

                log.Information($"[FCBuff] {activationMenuDetail}");
                SetState(FCBuffState.WaitingForActivation);
                break;

            case FCBuffState.WaitingForActivation:
                if (IsSealSweetenerTwoActive())
                {
                    if (FcBuffStockPolicy.ApplyConfirmedActivation(
                            plugin.Configuration.FcActionStockByFreeCompanyId,
                            currentFreeCompanyId,
                            DateTime.UtcNow))
                    {
                        plugin.Configuration.Save();
                    }
                    log.Information("[FCBuff] Seal Sweetener II activation verified; persisted stock decremented once.");
                    SetState(FCBuffState.Complete);
                    break;
                }

                if (!GameHelpers.AdsOwnsVendorUi() && GameHelpers.IsAddonVisible("SelectYesno"))
                {
                    var localizedActionName = GetLocalizedSealSweetenerTwoName();
                    if (!string.IsNullOrWhiteSpace(localizedActionName))
                    {
                        GameHelpers.TryClickYesIfPromptContains(
                            [localizedActionName],
                            "FC action activation",
                            allowUnreadable: false,
                            out _);
                    }
                }

                if (elapsed < 10)
                    return;

                HandleUnverifiedActivation("Seal Sweetener II did not become active after confirmation.");
                break;
        }
    }

    private void HandleUnverifiedActivation(string reason)
    {
        if (GameHelpers.IsAddonVisible("SelectYesno"))
            TryClickSelectYesnoNo("unverified FC action activation");
        GameHelpers.TryCloseAddonByCallback(ContextMenuAddonName);

        if (activationAttempts == 1)
        {
            log.Warning($"[FCBuff] {reason} Forcing FC stock reconciliation before the second activation attempt.");
            reconciliationRequired = true;
            lastSealSweetenerListIndex = -1;
            SetState(FCBuffState.CheckingBuffInventory);
            return;
        }

        log.Error($"[FCBuff] {reason} Seal Sweetener II activation remained unverified after reconciliation.");
        SetState(FCBuffState.Failed);
    }

    private void TickAdsPurchase()
    {
        if (DateTime.UtcNow < nextPurchaseObservation) return;
        nextPurchaseObservation = DateTime.UtcNow.AddSeconds(1);
        try
        {
            if (currentFreeCompanyId == 0 || currentFreeCompanyId != GetCurrentFreeCompanyId())
                throw new InvalidOperationException("Free Company changed during the purchase request.");
            if (purchaseOperationId == null)
            {
                purchaseOperationId = Guid.NewGuid().ToString("N");
                var accepted = Plugin.PluginInterface.GetIpcSubscriber<string, uint, int, bool>("ADS.StartCompanyActionPurchase")
                    .InvokeFunc(purchaseOperationId, SealSweetenerTwoCompanyActionId, buyMax);
                if (!accepted)
                {
                    purchaseOperationId = null;
                    var rejected = ReadAdsPurchaseStatus();
                    throw new InvalidOperationException($"ADS rejected the company action purchase: {(string?)rejected["lastStartError"]}");
                }
                log.Information($"[FCBuff] ADS accepted {buyMax} Seal Sweetener II actions; awaiting exact action and credit deltas.");
                return;
            }
            var status = ReadAdsPurchaseStatus();
            if ((string?)status["operationId"] != purchaseOperationId || (bool?)status["companyAction"] != true ||
                (uint?)status["itemId"] != SealSweetenerTwoCompanyActionId || (int?)status["requestedQuantity"] != buyMax)
                throw new InvalidOperationException("ADS no longer reports the owned company action purchase; the request will not be repeated.");
            if ((bool?)status["running"] == true) return;
            if ((bool?)status["done"] != true || (bool?)status["succeeded"] != true || (int?)status["acquiredQuantity"] != buyMax)
                throw new InvalidOperationException($"ADS company action acquisition failed: {(string?)status["statusMessage"]}");
            purchaseOperationId = null;
            resumeAfterPurchase = true;
            reconciliationRequired = true;
            SetState(FCBuffState.ClosingWindows);
        }
        catch (Exception ex)
        {
            log.Error($"[FCBuff] {ex.Message}");
            CancelOwnedPurchase();
            SetState(FCBuffState.Failed);
        }
    }

    private static JObject ReadAdsPurchaseStatus() => JObject.Parse(Plugin.PluginInterface
        .GetIpcSubscriber<string>("ADS.GetShopPurchaseStatusJson").InvokeFunc());

    private void CancelOwnedPurchase()
    {
        if (purchaseOperationId == null) return;
        try { Plugin.PluginInterface.GetIpcSubscriber<string, bool>("ADS.CancelShopPurchase").InvokeFunc(purchaseOperationId); }
        catch (Exception ex) { log.Warning($"[FCBuff] ADS purchase cancellation remains unresolved: {ex.Message}"); }
    }

    private bool TrySelectLocalizedActivateEntry(out string detail)
    {
        detail = string.Empty;
        var localizedActivateText = GetLocalizedAddonText(ActivateEntryAddonRowId);
        if (string.IsNullOrWhiteSpace(localizedActivateText))
        {
            detail = $"Localized Activate text was unavailable from Addon row {ActivateEntryAddonRowId}.";
            return false;
        }

        var addonPtr = Plugin.GameGui.GetAddonByName(ContextMenuAddonName, 1);
        if (addonPtr == 0)
        {
            detail = "The FC action context menu was not available.";
            return false;
        }

        try
        {
            var menu = new AddonMaster.ContextMenu(addonPtr);
            var entries = menu.Entries.ToList();
            var visibleEntries = string.Join(", ", entries.Select((entry, index) =>
                $"{index}:'{NormalizeAddonText(entry.Text)}' enabled={entry.Enabled}"));
            var matchingEntryWasDisabled = false;

            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var entryText = NormalizeAddonText(entry.Text);
                if (!string.Equals(entryText, localizedActivateText, StringComparison.Ordinal))
                    continue;

                if (!entry.Enabled)
                {
                    matchingEntryWasDisabled = true;
                    continue;
                }

                if (!entry.Select())
                {
                    detail = $"Localized Activate entry {i}:'{entryText}' could not be selected. Entries: {visibleEntries}";
                    return false;
                }

                detail = $"Selected localized Activate entry {i}:'{entryText}'. Entries: {visibleEntries}";
                return true;
            }

            detail = matchingEntryWasDisabled
                ? $"The localized Activate entry was disabled. Entries: {visibleEntries}"
                : $"The localized Activate entry was not present. Entries: {visibleEntries}";
            return false;
        }
        catch (Exception ex)
        {
            detail = $"Failed to inspect the FC action context menu: {ex.Message}";
            return false;
        }
    }

    private unsafe void TickClosingWindows(double elapsed)
    {
        if (elapsed < 0.25)
            return;

        if (GameHelpers.AdsOwnsVendorUi())
        {
            if (elapsed > WindowCloseTimeout.TotalSeconds)
            {
                log.Warning("[FCBuff] ADS vendor cleanup is still pending; leaving its dialogs untouched.");
                SetState(FCBuffState.Failed);
            }
            return;
        }

        var visibleAddons = GetVisibleFcCleanupAddons();
        if (visibleAddons.Count == 0)
        {
            if (resumeAfterPurchase && !failAfterClosingWindows)
            {
                resumeAfterPurchase = false;
                log.Information("[FCBuff] Purchase windows closed; reopening FC actions for required rescan.");
                SetState(FCBuffState.OpeningFCWindow);
                return;
            }
            log.Information(failAfterClosingWindows
                ? "[FCBuff] FC windows closed after failure"
                : "[FCBuff] All FC windows closed, task complete");
            SetState(failAfterClosingWindows ? FCBuffState.Failed : FCBuffState.Complete);
            return;
        }

        var now = DateTime.UtcNow;
        LogWindowCloseStatusThrottled(visibleAddons, now);

        if (elapsed > WindowCloseTimeout.TotalSeconds)
        {
            log.Error($"[FCBuff] Timeout closing FC windows: {string.Join(", ", visibleAddons)}");
            SetState(FCBuffState.Failed);
            return;
        }

        if (lastWindowCloseAttemptAt != DateTime.MinValue &&
            now - lastWindowCloseAttemptAt < WindowCloseRetryInterval)
        {
            return;
        }

        lastWindowCloseAttemptAt = now;

        var closeTarget = GetNextFcCleanupTarget();
        if (closeTarget == null)
            return;


        if (closeTarget == "SelectYesno")
        {
            TryClickSelectYesnoNo("cleanup");
            return;
        }

        log.Information($"[FCBuff] Closing {closeTarget} with callback true -1");
        GameHelpers.FireAddonCallback(closeTarget, true, -1);
    }

    private unsafe bool TryClickSelectYesnoNo(string reason)
    {
        if (GameHelpers.AdsOwnsVendorUi()) return false;
        try
        {
            nint addonPtr = Plugin.GameGui.GetAddonByName("SelectYesno", 1);
            if (addonPtr == 0)
                return false;

            var addon = (AddonSelectYesno*)addonPtr;
            if (!addon->AtkUnitBase.IsVisible)
                return false;

            var yesNo = new AddonMaster.SelectYesno(&addon->AtkUnitBase);
            var promptText = NormalizeAddonText(yesNo.Text);
            log.Warning($"[FCBuff] Closing SelectYesno with No during {reason}: '{promptText}'");
            yesNo.No();
            return true;
        }
        catch (Exception ex)
        {
            log.Warning($"[FCBuff] Failed to click No on SelectYesno during {reason}: {ex.Message}");
            return false;
        }
    }

    private unsafe List<string> GetVisibleFcCleanupAddons()
    {
        var visibleAddons = new List<string>();
        var manager = RaptureAtkUnitManager.Instance();

        foreach (var addonName in FcCleanupAddonNames)
        {
            var addon = manager->GetAddonByName(addonName);
            if (addon != null && addon->IsVisible)
                visibleAddons.Add(addonName);
        }

        return visibleAddons;
    }

    private string? GetNextFcCleanupTarget()
    {
        if (GameHelpers.IsAddonVisible("SelectYesno"))
            return "SelectYesno";


        return FcCallbackCloseAddonNames.FirstOrDefault(GameHelpers.IsAddonVisible);
    }

    private void LogWindowCloseStatusThrottled(IReadOnlyCollection<string> visibleAddons, DateTime now)
    {
        if (now - lastWindowCloseLogAt < WindowCloseLogThrottle)
            return;

        lastWindowCloseLogAt = now;
        log.Information($"[FCBuff] Closing FC windows: {string.Join(", ", visibleAddons)}");
    }

    private static string NormalizeAddonText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (char.IsControl(c))
            {
                if (builder.Length > 0 && builder[^1] != ' ')
                    builder.Append(' ');
                continue;
            }

            builder.Append(c);
        }

        return builder.ToString().Trim();
    }

    private static string GetLocalizedAddonText(uint rowId)
    {
        try
        {
            var sheet = Plugin.DataManager.GetExcelSheet<Addon>();
            return sheet.TryGetRow(rowId, out var row)
                ? NormalizeAddonText(row.Text.ToString())
                : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetLocalizedSealSweetenerTwoName()
    {
        try
        {
            var sheet = Plugin.DataManager.GetExcelSheet<CompanyAction>();
            return sheet.TryGetRow(SealSweetenerTwoCompanyActionId, out var row)
                ? NormalizeAddonText(row.Name.ToString())
                : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private unsafe FcActionInventoryReadResult ReadSealSweetenerBuffs()
    {
        lastSealSweetenerListIndex = -1;
        try
        {
            // At this point, FreeCompanyAction window should already be open and visible
            // thanks to the state machine (OpeningFCWindow -> WaitingForFCCommand -> WaitingForFCWindow)
            var addon = RaptureAtkUnitManager.Instance()->GetAddonByName("FreeCompanyAction");
            if (addon == null || !addon->IsVisible)
            {
                log.Error("[FCBuff] FreeCompanyAction addon not found or not visible for buff counting");
                return FcActionInventoryReadResult.Failed(
                    "FreeCompanyAction addon was not visible.");
            }

            // Resolve the existing FC action list through nodes 1, 10, and 14.
            var node1 = addon->GetNodeById(1);
            if (node1 == null)
                return FcActionInventoryReadResult.Failed("FC action root node was unavailable.");

            var node10 = node1->ChildNode;
            if (node10 == null)
                return FcActionInventoryReadResult.Failed("FC action container node was unavailable.");

            var node14 = node10->ChildNode;
            while (node14 != null && (int)node14->Type < 1000)
                node14 = node14->PrevSiblingNode;

            if (node14 == null)
                return FcActionInventoryReadResult.Failed("FC action list node was unavailable.");

            var list = node14->GetAsAtkComponentList();
            if (list == null)
                return FcActionInventoryReadResult.Failed("FC action list component was unavailable.");

            var itemCount = list->GetItemCount();
            if (itemCount < 0 || itemCount > 16)
                return FcActionInventoryReadResult.Failed($"FC action list count was invalid: {itemCount}.");

            int sealSweetenerCount = 0;
            int firstSealSweetenerListIndex = -1;
            // Allocated renderers can retain stale text beyond the current list length.
            for (int listIndex = 0; listIndex < itemCount; listIndex++)
            {
                var renderer = list->GetItemRenderer(listIndex);
                if (renderer == null)
                    return FcActionInventoryReadResult.Failed($"FC action inventory row {listIndex} was unavailable.");

                var textNode = renderer->GetTextNodeById(3);
                if (textNode == null)
                    return FcActionInventoryReadResult.Failed($"FC action inventory row {listIndex} text was unavailable.");

                var text = textNode->NodeText.ToString();
                if (string.IsNullOrWhiteSpace(text))
                    return FcActionInventoryReadResult.Failed($"FC action inventory row {listIndex} text was empty.");

                if (text == "Seal Sweetener II")
                {
                    sealSweetenerCount++;
                    if (firstSealSweetenerListIndex < 0)
                        firstSealSweetenerListIndex = listIndex;
                    log.Debug($"[FCBuff] Found Seal Sweetener II at stock row {listIndex}");
                }
            }

            lastSealSweetenerListIndex = firstSealSweetenerListIndex;
            log.Information($"[FCBuff] Seal Sweetener II count: {sealSweetenerCount}");
            commandManager.ProcessCommand($"/echo Seal Sweetener II count: {sealSweetenerCount}");

            return FcActionInventoryReadResult.Succeeded(sealSweetenerCount);
        }
        catch (Exception ex)
        {
            log.Error($"[FCBuff] Error counting Seal Sweetener buffs: {ex.Message}");
            return FcActionInventoryReadResult.Failed(ex.Message);
        }
    }

    private static unsafe ulong GetCurrentFreeCompanyId()
    {
        try
        {
            var proxy = InfoProxyFreeCompany.Instance();
            return proxy == null ? 0 : proxy->Id;
        }
        catch
        {
            return 0;
        }
    }

    private static bool IsSealSweetenerTwoActive()
    {
        try
        {
            var player = Plugin.ObjectTable.LocalPlayer;
            if (player == null)
                return false;

            foreach (var status in player.StatusList)
            {
                if (status.StatusId == SealSweetenerStatusId &&
                    status.Param >= SealSweetenerTwoMinimumStrength)
                {
                    return true;
                }
            }
        }
        catch
        {
            // A failed active-status read is not satisfaction evidence.
        }
        return false;
    }

    private void SetState(FCBuffState newState)
    {
        if (newState is FCBuffState.Complete or FCBuffState.Failed &&
            state != FCBuffState.ClosingWindows)
        {
            failAfterClosingWindows = newState == FCBuffState.Failed;
            newState = FCBuffState.ClosingWindows;
        }

        log.Information($"[FCBuff] {state} -> {newState}");
        state = newState;
        stateEnteredAt = DateTime.UtcNow;


        if (newState == FCBuffState.ClosingWindows)
        {
            ResetWindowCloseTracking();
        }

        if (newState == FCBuffState.Idle || newState == FCBuffState.Complete || newState == FCBuffState.Failed)
        {
            ReleaseOwnedYesAlreadyPause($"state {newState}");

            ResetWindowCloseTracking();
            failAfterClosingWindows = false;
        }

    }

    private bool TryAcquireManualYesAlreadyPause()
    {
        if (yesAlready.IsPaused)
            return true;

        yesAlready.Pause();
        yesAlreadyPauseOwned = yesAlready.IsPaused;
        if (yesAlreadyPauseOwned)
            log.Information("[FCBuff] Manual run acquired the VERMAXION YesAlready pause.");
        return yesAlreadyPauseOwned;
    }

    private void ReleaseOwnedYesAlreadyPause(string reason)
    {
        if (!yesAlreadyPauseOwned)
            return;

        yesAlready.Unpause();
        yesAlreadyPauseOwned = yesAlready.IsPaused;
        if (yesAlreadyPauseOwned)
            log.Warning($"[FCBuff] Could not release the owned YesAlready pause during {reason}.");
        else
            log.Information($"[FCBuff] Released the owned YesAlready pause during {reason}.");
    }

    private void ResetWindowCloseTracking()
    {
        lastWindowCloseAttemptAt = DateTime.MinValue;
        lastWindowCloseLogAt = DateTime.MinValue;
    }

}
