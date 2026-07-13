using System;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using VERMAXION.IPC;
using VERMAXION.Models;

namespace VERMAXION.Services;

public sealed class VendorStockService
{
    private const uint GysahlGreensItemId = 4868;
    private const uint Grade8DarkMatterItemId = 33916;
    private const uint VersatileLureItemId = 29717;
    private const float TargetInteractDistance = 3.0f;
    private static readonly TimeSpan VersatileLureRestockTimeout = TimeSpan.FromMinutes(5);

    private readonly ICommandManager commandManager;
    private readonly IPluginLog log;
    private readonly ConfigManager configManager;
    private readonly VNavmeshIPC vnavmesh;

    private VendorStockState state = VendorStockState.Idle;
    private DateTime stateEnteredAt = DateTime.MinValue;
    private DateTime lastNavigationCommandAt = DateTime.MinValue;
    private DateTime lastInteractionAttemptAt = DateTime.MinValue;
    private DateTime lastPurchaseAttemptAt = DateTime.MinValue;
    private DateTime lastCleanupAttemptAt = DateTime.MinValue;
    private DateTime versatileLureRestockStartedAt = DateTime.MinValue;
    private uint travelOriginTerritory;
    private bool sawTravelTransition;
    private VendorStockState cleanupNextState = VendorStockState.Complete;
    private int observedGysahlCount;
    private int observedDarkMatterCount;
    private int observedVersatileLureCount;
    private int versatileLureTarget;
    private int gysahlShopMenuIndex;
    private bool gysahlAlternateShopAttempted;
    private bool versatileLureVendorAcquiredLogged;

    public enum VendorStockState
    {
        Idle,
        CheckingNeeds,
        GysahlLifestreaming,
        GysahlWaitingForArrival,
        GysahlNavigatingToVendor,
        GysahlInteractingVendor,
        GysahlSelectingShopMenu,
        GysahlPurchasing,
        ClosingVendorUi,
        DarkMatterLifestreaming,
        DarkMatterWaitingForArrival,
        DarkMatterNavigatingToVendor,
        DarkMatterInteractingVendor,
        DarkMatterPurchasing,
        VersatileLureLifestreaming,
        VersatileLureWaitingForArrival,
        VersatileLureNavigatingToVendor,
        VersatileLureInteractingVendor,
        VersatileLureSelectingShopMenu,
        VersatileLurePurchasing,
        Complete,
        Failed,
    }

    public VendorStockState State => state;
    public bool IsActive => state != VendorStockState.Idle && state != VendorStockState.Complete && state != VendorStockState.Failed;
    public bool IsComplete => state == VendorStockState.Complete;
    public bool IsFailed => state == VendorStockState.Failed;
    public string StatusText => state.ToString();

    public VendorStockService(
        ICommandManager commandManager,
        IPluginLog log,
        ConfigManager configManager,
        VNavmeshIPC vnavmesh)
    {
        this.commandManager = commandManager;
        this.log = log;
        this.configManager = configManager;
        this.vnavmesh = vnavmesh;
    }

    public void Start()
    {
        if (IsActive)
            return;

        observedGysahlCount = (int)GameHelpers.GetInventoryItemCount(GysahlGreensItemId);
        observedDarkMatterCount = (int)GameHelpers.GetInventoryItemCount(Grade8DarkMatterItemId);
        observedVersatileLureCount = (int)GameHelpers.GetInventoryItemCount(VersatileLureItemId);
        versatileLureTarget = 0;
        versatileLureRestockStartedAt = DateTime.MinValue;
        gysahlShopMenuIndex = 0;
        gysahlAlternateShopAttempted = false;
        versatileLureVendorAcquiredLogged = false;
        GameHelpers.ResetInteractionState();
        SetState(VendorStockState.CheckingNeeds);
    }

    public void StartVersatileLureRestock(int target)
    {
        if (IsActive)
            return;

        versatileLureTarget = Math.Max(0, target);
        versatileLureRestockStartedAt = DateTime.MinValue;
        observedGysahlCount = (int)GameHelpers.GetInventoryItemCount(GysahlGreensItemId);
        observedDarkMatterCount = (int)GameHelpers.GetInventoryItemCount(Grade8DarkMatterItemId);
        observedVersatileLureCount = (int)GameHelpers.GetInventoryItemCount(VersatileLureItemId);
        gysahlShopMenuIndex = 0;
        gysahlAlternateShopAttempted = false;
        versatileLureVendorAcquiredLogged = false;
        GameHelpers.ResetInteractionState();
        SetState(VendorStockState.CheckingNeeds);
    }

    internal void StartVersatileLureRestockDockside(int target)
    {
        if (IsActive)
            return;

        versatileLureTarget = Math.Max(0, target);
        versatileLureRestockStartedAt = DateTime.MinValue;
        observedVersatileLureCount = (int)GameHelpers.GetInventoryItemCount(VersatileLureItemId);
        versatileLureVendorAcquiredLogged = false;
        lastNavigationCommandAt = DateTime.MinValue;
        lastInteractionAttemptAt = DateTime.MinValue;
        lastPurchaseAttemptAt = DateTime.MinValue;
        GameHelpers.ResetInteractionState();

        if (!NeedsVersatileLures())
        {
            log.Information(
                $"[VendorStock][LureDock] Inventory verification: {observedVersatileLureCount}/{versatileLureTarget}; purchase skipped");
            SetState(VendorStockState.Complete);
            return;
        }

        var quantity = OceanFishingDockPreparationPolicy.RequiredPurchaseQuantity(
            observedVersatileLureCount,
            versatileLureTarget);
        log.Information(
            $"[VendorStock][LureDock] Dock-local restock started at {observedVersatileLureCount}/{versatileLureTarget}; required purchase={quantity}; no Limsa travel command will be issued");
        SetState(VendorStockState.VersatileLureNavigatingToVendor);
    }

    public void RunTask()
    {
        log.Information("[VERMAXION] Manual Vendor Stock triggered");
        Start();
    }

    public void Reset()
    {
        vnavmesh.Stop();
        state = VendorStockState.Idle;
        stateEnteredAt = DateTime.MinValue;
        lastNavigationCommandAt = DateTime.MinValue;
        lastInteractionAttemptAt = DateTime.MinValue;
        lastPurchaseAttemptAt = DateTime.MinValue;
        lastCleanupAttemptAt = DateTime.MinValue;
        travelOriginTerritory = 0;
        sawTravelTransition = false;
        cleanupNextState = VendorStockState.Complete;
        versatileLureTarget = 0;
        versatileLureRestockStartedAt = DateTime.MinValue;
        observedVersatileLureCount = 0;
        gysahlShopMenuIndex = 0;
        gysahlAlternateShopAttempted = false;
        versatileLureVendorAcquiredLogged = false;
    }

    public void Update()
    {
        if (state == VendorStockState.Idle || state == VendorStockState.Complete || state == VendorStockState.Failed)
            return;

        var elapsed = (DateTime.UtcNow - stateEnteredAt).TotalSeconds;
        if (IsVersatileLureState(state) &&
            versatileLureRestockStartedAt != DateTime.MinValue &&
            DateTime.UtcNow - versatileLureRestockStartedAt > VersatileLureRestockTimeout)
        {
            Fail(
                $"[VendorStock][LureDock] Bounded failure: Versatile Lure restock exceeded {VersatileLureRestockTimeout.TotalMinutes:F0} minutes");
            return;
        }

        switch (state)
        {
            case VendorStockState.CheckingNeeds:
                if (NeedsGysahlGreens())
                {
                    log.Information("[VendorStock] Gysahl Greens are below target, traveling to Maisenta");
                    SetState(VendorStockState.GysahlLifestreaming);
                }
                else if (NeedsDarkMatter())
                {
                    log.Information("[VendorStock] Grade 8 Dark Matter is below target, traveling to Alaric");
                    SetState(VendorStockState.DarkMatterLifestreaming);
                }
                else if (NeedsVersatileLures())
                {
                    log.Information("[VendorStock] Versatile Lures are below target, traveling to Limsa Merchant & Mender");
                    SetState(VendorStockState.VersatileLureLifestreaming);
                }
                else
                {
                    log.Information("[VendorStock] Inventory already meets all configured targets");
                    SetState(VendorStockState.Complete);
                }
                break;

            case VendorStockState.GysahlLifestreaming:
                log.Information("[VendorStock] Lifestreaming to Gridania: /li gridania");
                BeginTravelWait();
                commandManager.ProcessCommand("/li gridania");
                SetState(VendorStockState.GysahlWaitingForArrival);
                break;

            case VendorStockState.GysahlWaitingForArrival:
                if (HasArrivedNearVendor("Maisenta", 3.0, 8.0))
                {
                    SetState(VendorStockState.GysahlNavigatingToVendor);
                }
                else if (elapsed > 30)
                {
                    Fail("[VendorStock] Timed out waiting to arrive near Maisenta");
                }
                break;

            case VendorStockState.GysahlNavigatingToVendor:
                if (AdvanceToVendor("Maisenta"))
                {
                    SetState(VendorStockState.GysahlInteractingVendor);
                }
                else if (elapsed > 45)
                {
                    Fail("[VendorStock] Timed out navigating to Maisenta");
                }
                break;

            case VendorStockState.GysahlInteractingVendor:
                if (!EnsureVendorInRange("Maisenta"))
                    break;

                if (GameHelpers.IsAddonVisible("Shop"))
                {
                    SetState(VendorStockState.GysahlPurchasing);
                }
                else if (GameHelpers.IsAddonVisible("SelectIconString"))
                {
                    SetState(VendorStockState.GysahlSelectingShopMenu);
                }
                else if (TryInteract("Maisenta"))
                {
                    // Wait for menu to appear.
                }
                else if (elapsed > 30)
                {
                    Fail("[VendorStock] Timed out opening Maisenta's menu");
                }
                break;

            case VendorStockState.GysahlSelectingShopMenu:
                if (GameHelpers.IsAddonVisible("Shop"))
                {
                    SetState(VendorStockState.GysahlPurchasing);
                }
                else if (GameHelpers.IsAddonVisible("SelectIconString"))
                {
                    if (TryFirePurchaseAction(0.75))
                    {
                        log.Information($"[VendorStock] Opening Maisenta's Gysahl Greens shop with menu index {gysahlShopMenuIndex}");
                        GameHelpers.FireAddonCallback("SelectIconString", true, gysahlShopMenuIndex);
                    }
                }
                else if (!gysahlAlternateShopAttempted && gysahlShopMenuIndex == 0 && elapsed > 2.5)
                {
                    log.Warning("[VendorStock] Menu index 0 did not open Shop; retrying Maisenta with menu index 1");
                    gysahlShopMenuIndex = 1;
                    gysahlAlternateShopAttempted = true;
                    lastPurchaseAttemptAt = DateTime.MinValue;
                    lastInteractionAttemptAt = DateTime.MinValue;
                    GameHelpers.ResetInteractionState();
                    BeginVendorCleanup(VendorStockState.GysahlInteractingVendor);
                }
                else if (elapsed > 8)
                {
                    SetState(VendorStockState.GysahlInteractingVendor);
                }
                break;

            case VendorStockState.GysahlPurchasing:
                if (HandleShopPurchases(
                    GysahlGreensItemId,
                    GetGysahlTarget(),
                    ref observedGysahlCount,
                    "[VendorStock] Gysahl Greens",
                    1.5,
                    99,
                    0,
                    5))
                {
                    FinishGysahlPhase();
                }
                else if (elapsed > 180)
                {
                    Fail("[VendorStock] Timed out stocking Gysahl Greens");
                }
                break;

            case VendorStockState.ClosingVendorUi:
                if (!IsVendorUiStillOpen())
                {
                    SetState(cleanupNextState);
                }
                else if (TryCloseVendorUi(0.8))
                {
                    log.Information("[VendorStock] Closing vendor UI with NUMPAD+");
                    GameHelpers.SendNumpadPlus();
                }
                else if (elapsed > 8)
                {
                    log.Warning("[VendorStock] Vendor UI remained open after cleanup attempts, continuing");
                    SetState(cleanupNextState);
                }
                break;

            case VendorStockState.DarkMatterLifestreaming:
                log.Information("[VendorStock] Lifestreaming to Leatherworkers' Guild: /li leather");
                BeginTravelWait();
                commandManager.ProcessCommand("/li leather");
                SetState(VendorStockState.DarkMatterWaitingForArrival);
                break;

            case VendorStockState.DarkMatterWaitingForArrival:
                if (HasArrivedNearVendor("Alaric", 3.0, 8.0))
                {
                    SetState(VendorStockState.DarkMatterNavigatingToVendor);
                }
                else if (elapsed > 30)
                {
                    Fail("[VendorStock] Timed out waiting to arrive near Alaric");
                }
                break;

            case VendorStockState.DarkMatterNavigatingToVendor:
                if (AdvanceToVendor("Alaric"))
                {
                    SetState(VendorStockState.DarkMatterInteractingVendor);
                }
                else if (elapsed > 45)
                {
                    Fail("[VendorStock] Timed out navigating to Alaric");
                }
                break;

            case VendorStockState.DarkMatterInteractingVendor:
                if (!EnsureVendorInRange("Alaric"))
                    break;

                if (GameHelpers.IsAddonVisible("Shop"))
                {
                    SetState(VendorStockState.DarkMatterPurchasing);
                }
                else if (TryInteract("Alaric"))
                {
                    // Wait for shop to appear.
                }
                else if (elapsed > 30)
                {
                    Fail("[VendorStock] Timed out opening Alaric's shop");
                }
                break;

            case VendorStockState.DarkMatterPurchasing:
                if (HandleShopPurchases(
                    Grade8DarkMatterItemId,
                    GetDarkMatterTarget(),
                    ref observedDarkMatterCount,
                    "[VendorStock] Grade 8 Dark Matter",
                    2.0,
                    99,
                    0,
                    40))
                {
                    FinishDarkMatterPhase();
                }
                else if (elapsed > 180)
                {
                    Fail("[VendorStock] Timed out stocking Grade 8 Dark Matter");
                }
                break;

            case VendorStockState.VersatileLureLifestreaming:
                log.Information("[VendorStock] Lifestreaming to Limsa: /li limsa");
                BeginTravelWait();
                commandManager.ProcessCommand("/li limsa");
                SetState(VendorStockState.VersatileLureWaitingForArrival);
                break;

            case VendorStockState.VersatileLureWaitingForArrival:
                if (HasArrivedInTerritory(
                        OceanFishingDockPreparationPolicy.LimsaTerritoryType,
                        3.0,
                        12.0))
                {
                    log.Information("[VendorStock][LureDock] Limsa settlement confirmed; starting dock navigation without waiting for Merchant & Mender to load");
                    SetState(VendorStockState.VersatileLureNavigatingToVendor);
                }
                else if (elapsed > 45)
                {
                    Fail("[VendorStock][LureDock] Bounded failure: timed out waiting for settled Limsa territory");
                }
                break;

            case VendorStockState.VersatileLureNavigatingToVendor:
                if (AdvanceToVendor(
                        "Merchant & Mender",
                        OceanFishingDockPreparationPolicy.MerchantAndMenderDataId,
                        OceanFishingDockPreparationPolicy.MerchantAndMenderPosition))
                {
                    SetState(VendorStockState.VersatileLureInteractingVendor);
                }
                else if (elapsed > 45)
                {
                    Fail("[VendorStock][LureDock] Bounded failure: timed out navigating to Merchant & Mender");
                }
                break;

            case VendorStockState.VersatileLureInteractingVendor:
                if (!EnsureVendorInRange(
                        "Merchant & Mender",
                        OceanFishingDockPreparationPolicy.MerchantAndMenderDataId,
                        OceanFishingDockPreparationPolicy.MerchantAndMenderPosition))
                    break;

                if (GameHelpers.IsAddonVisible("Shop"))
                {
                    SetState(VendorStockState.VersatileLurePurchasing);
                }
                else if (GameHelpers.IsAddonVisible("SelectIconString"))
                {
                    SetState(VendorStockState.VersatileLureSelectingShopMenu);
                }
                else if (TryInteract(
                             "Merchant & Mender",
                             OceanFishingDockPreparationPolicy.MerchantAndMenderDataId))
                {
                    // Wait for menu to appear.
                }
                else if (elapsed > 30)
                {
                    Fail("[VendorStock][LureDock] Bounded failure: timed out opening Merchant & Mender's menu");
                }
                break;

            case VendorStockState.VersatileLureSelectingShopMenu:
                if (GameHelpers.IsAddonVisible("Shop"))
                {
                    SetState(VendorStockState.VersatileLurePurchasing);
                }
                else if (GameHelpers.IsAddonVisible("SelectIconString") && TryFirePurchaseAction(0.75))
                {
                    log.Information("[VendorStock][LureDock] Menu opening submission: SelectIconString callback entry 0");
                    GameHelpers.FireAddonCallback("SelectIconString", true, 0);
                }
                else if (elapsed > 8)
                {
                    SetState(VendorStockState.VersatileLureInteractingVendor);
                }
                break;

            case VendorStockState.VersatileLurePurchasing:
                if (HandleShopPurchases(
                    VersatileLureItemId,
                    GetVersatileLureTarget(),
                    ref observedVersatileLureCount,
                    "[VendorStock][LureDock] Versatile Lures",
                    1.5,
                    99,
                    0,
                    3))
                {
                    FinishVersatileLurePhase();
                }
                else if (elapsed > 180)
                {
                    Fail("[VendorStock][LureDock] Bounded failure: timed out stocking Versatile Lures");
                }
                break;
        }
    }

    private bool NeedsGysahlGreens()
    {
        var target = GetGysahlTarget();
        return target > 0 && (int)GameHelpers.GetInventoryItemCount(GysahlGreensItemId) < target;
    }

    private bool NeedsDarkMatter()
    {
        var target = GetDarkMatterTarget();
        return target > 0 && (int)GameHelpers.GetInventoryItemCount(Grade8DarkMatterItemId) < target;
    }

    private bool NeedsVersatileLures()
    {
        var target = GetVersatileLureTarget();
        return target > 0 && (int)GameHelpers.GetInventoryItemCount(VersatileLureItemId) < target;
    }

    private int GetGysahlTarget()
        => Math.Max(0, configManager.GetActiveConfig().VendorStockGysahlGreensTarget);

    private int GetDarkMatterTarget()
        => Math.Max(0, configManager.GetActiveConfig().VendorStockGrade8DarkMatterTarget);

    private int GetVersatileLureTarget()
        => Math.Max(0, versatileLureTarget);

    private bool AdvanceToVendor(
        string npcName,
        uint dataId = 0,
        Vector3? fallbackPosition = null)
    {
        var dataIdNpc = dataId == 0 ? null : GameHelpers.FindObjectByDataId(dataId);
        var nameNpc = GameHelpers.FindObjectByName(npcName);
        var npc = dataIdNpc ?? nameNpc;
        var approachPosition = OceanFishingDockPreparationPolicy.ResolveMerchantApproachPosition(
            fallbackPosition ?? default,
            dataIdNpc?.Position,
            nameNpc?.Position);
        if (npc == null && !fallbackPosition.HasValue)
            return false;

        var distance = GetDistanceTo(approachPosition);
        if (npc != null && distance <= GetDesiredInteractDistance(npc))
        {
            vnavmesh.Stop();
            if (dataId == OceanFishingDockPreparationPolicy.MerchantAndMenderDataId &&
                !versatileLureVendorAcquiredLogged)
            {
                versatileLureVendorAcquiredLogged = true;
                var source = dataIdNpc != null ? "data ID" : "name fallback";
                log.Information(
                    $"[VendorStock][LureDock] Vendor acquisition: Merchant & Mender resolved by {source} at {npc.Position}");
            }
            return true;
        }

        if (npc == null && distance <= TargetInteractDistance)
        {
            vnavmesh.Stop();
            return false;
        }

        if ((DateTime.UtcNow - lastNavigationCommandAt).TotalSeconds >= 2)
        {
            lastNavigationCommandAt = DateTime.UtcNow;
            var source = npc == null ? "fixed fallback" : dataIdNpc != null ? "data ID" : "name fallback";
            var prefix = dataId == OceanFishingDockPreparationPolicy.MerchantAndMenderDataId
                ? "[VendorStock][LureDock]"
                : "[VendorStock]";
            log.Information($"{prefix} Navigating to {npcName} ({distance:F1}y, source={source})");
            vnavmesh.PathfindAndMoveTo(approachPosition);
        }

        return false;
    }

    private bool TryInteract(string npcName, uint dataId = 0)
    {
        if ((DateTime.UtcNow - lastInteractionAttemptAt).TotalSeconds < 5.25)
            return false;

        lastInteractionAttemptAt = DateTime.UtcNow;
        var prefix = dataId == OceanFishingDockPreparationPolicy.MerchantAndMenderDataId
            ? "[VendorStock][LureDock]"
            : "[VendorStock]";
        log.Information($"{prefix} Interacting with {npcName}{(dataId == 0 ? string.Empty : $" dataId={dataId}")}");
        return dataId == 0
            ? GameHelpers.TargetAndInteract(npcName)
            : GameHelpers.TargetAndInteractByDataId(dataId, npcName);
    }

    private bool HandleShopPurchases(
        uint itemId,
        int targetCount,
        ref int observedCount,
        string label,
        double repeatDelaySeconds,
        int maxPurchaseQuantity,
        params object[] purchaseArgPrefix)
    {
        var currentCount = (int)GameHelpers.GetInventoryItemCount(itemId);
        if (currentCount > observedCount)
        {
            observedCount = currentCount;
            log.Information($"{label} inventory verification: count increased to {currentCount}/{targetCount}");
        }

        if (currentCount >= targetCount)
        {
            log.Information($"{label} inventory verification complete: target reached at {currentCount}/{targetCount}");
            return true;
        }

        GameHelpers.ClickYesIfVisible();

        if (!GameHelpers.IsAddonVisible("Shop"))
            return false;

        if (!TryFirePurchaseAction(repeatDelaySeconds))
            return false;

        var purchaseQuantity = OceanFishingDockPreparationPolicy.RequiredPurchaseQuantity(
            currentCount,
            targetCount,
            maxPurchaseQuantity);
        if (purchaseQuantity <= 0)
            return true;

        var purchaseArgs = new object[purchaseArgPrefix.Length + 1];
        Array.Copy(purchaseArgPrefix, purchaseArgs, purchaseArgPrefix.Length);
        purchaseArgs[^1] = purchaseQuantity;

        log.Information(
            $"{label} purchase submission: Shop callback ({string.Join(", ", purchaseArgPrefix)}, {purchaseQuantity}) for item {itemId}; inventory={currentCount}/{targetCount}");
        GameHelpers.FireAddonCallback("Shop", true, purchaseArgs);
        return false;
    }

    private bool TryFirePurchaseAction(double repeatDelaySeconds)
    {
        if ((DateTime.UtcNow - lastPurchaseAttemptAt).TotalSeconds < repeatDelaySeconds)
            return false;

        lastPurchaseAttemptAt = DateTime.UtcNow;
        return true;
    }

    private void FinishGysahlPhase()
    {
        gysahlShopMenuIndex = 0;
        gysahlAlternateShopAttempted = false;
        GameHelpers.ResetInteractionState();
        if (NeedsDarkMatter())
        {
            SetState(VendorStockState.DarkMatterLifestreaming);
        }
        else if (NeedsVersatileLures())
        {
            SetState(VendorStockState.VersatileLureLifestreaming);
        }
        else
        {
            BeginVendorCleanup(VendorStockState.Complete);
        }
    }

    private void FinishDarkMatterPhase()
    {
        GameHelpers.ResetInteractionState();
        if (NeedsVersatileLures())
            SetState(VendorStockState.VersatileLureLifestreaming);
        else
            BeginVendorCleanup(VendorStockState.Complete);
    }

    private void FinishVersatileLurePhase()
    {
        versatileLureTarget = 0;
        versatileLureRestockStartedAt = DateTime.MinValue;
        GameHelpers.ResetInteractionState();
        BeginVendorCleanup(VendorStockState.Complete);
    }

    private void Fail(string message)
    {
        log.Warning(message);
        vnavmesh.Stop();
        SetState(VendorStockState.Failed);
    }

    private void SetState(VendorStockState newState)
    {
        log.Information($"[VendorStock] {state} -> {newState}");
        state = newState;
        stateEnteredAt = DateTime.UtcNow;
        if (IsVersatileLureState(newState) && versatileLureRestockStartedAt == DateTime.MinValue)
            versatileLureRestockStartedAt = stateEnteredAt;
    }

    private static bool IsVersatileLureState(VendorStockState candidate)
        => candidate is VendorStockState.VersatileLureLifestreaming
            or VendorStockState.VersatileLureWaitingForArrival
            or VendorStockState.VersatileLureNavigatingToVendor
            or VendorStockState.VersatileLureInteractingVendor
            or VendorStockState.VersatileLureSelectingShopMenu
            or VendorStockState.VersatileLurePurchasing;

    private void BeginTravelWait()
    {
        travelOriginTerritory = Plugin.ClientState.TerritoryType;
        sawTravelTransition = false;
    }

    private bool HasArrivedNearVendor(string npcName, double minimumWaitSeconds, double sameTerritoryFallbackSeconds)
    {
        if ((DateTime.UtcNow - stateEnteredAt).TotalSeconds < minimumWaitSeconds)
            return false;

        if (Plugin.Condition[ConditionFlag.BetweenAreas] || Plugin.Condition[ConditionFlag.BetweenAreas51])
        {
            sawTravelTransition = true;
            return false;
        }

        if (!GameHelpers.IsPlayerAvailable())
            return false;

        if (!sawTravelTransition &&
            Plugin.ClientState.TerritoryType == travelOriginTerritory &&
            (DateTime.UtcNow - stateEnteredAt).TotalSeconds < sameTerritoryFallbackSeconds)
        {
            return false;
        }

        return GameHelpers.FindObjectByName(npcName) != null;
    }

    private bool HasArrivedInTerritory(
        ushort territoryType,
        double minimumWaitSeconds,
        double sameTerritoryFallbackSeconds)
    {
        if ((DateTime.UtcNow - stateEnteredAt).TotalSeconds < minimumWaitSeconds)
            return false;

        var betweenAreas = Plugin.Condition[ConditionFlag.BetweenAreas] ||
                           Plugin.Condition[ConditionFlag.BetweenAreas51];
        if (betweenAreas)
        {
            sawTravelTransition = true;
            return false;
        }

        if (!sawTravelTransition &&
            Plugin.ClientState.TerritoryType == travelOriginTerritory &&
            (DateTime.UtcNow - stateEnteredAt).TotalSeconds < sameTerritoryFallbackSeconds)
        {
            return false;
        }

        return OceanFishingDockPreparationPolicy.IsLimsaSettlementReady(
            Plugin.ClientState.TerritoryType,
            betweenAreas,
            GameHelpers.IsPlayerAvailable()) &&
               Plugin.ClientState.TerritoryType == territoryType;
    }

    private void BeginVendorCleanup(VendorStockState nextState)
    {
        cleanupNextState = nextState;
        lastCleanupAttemptAt = DateTime.MinValue;
        SetState(VendorStockState.ClosingVendorUi);
    }

    private bool TryCloseVendorUi(double repeatDelaySeconds)
    {
        if ((DateTime.UtcNow - lastCleanupAttemptAt).TotalSeconds < repeatDelaySeconds)
            return false;

        lastCleanupAttemptAt = DateTime.UtcNow;
        return true;
    }

    private static bool IsVendorUiStillOpen()
        => GameHelpers.IsAddonVisible("Shop")
           || GameHelpers.IsAddonVisible("SelectIconString")
           || GameHelpers.IsAddonVisible("SelectYesno");

    private static float GetDistanceTo(IGameObject npc)
    {
        var player = Plugin.ObjectTable.LocalPlayer;
        if (player == null)
            return float.MaxValue;

        return Vector3.Distance(player.Position, npc.Position);
    }

    private static float GetDistanceTo(Vector3 position)
    {
        var player = Plugin.ObjectTable.LocalPlayer;
        return player == null
            ? float.MaxValue
            : Vector3.Distance(player.Position, position);
    }

    private bool EnsureVendorInRange(
        string npcName,
        uint dataId = 0,
        Vector3? fallbackPosition = null)
    {
        var npc = (dataId == 0 ? null : GameHelpers.FindObjectByDataId(dataId)) ??
                  GameHelpers.FindObjectByName(npcName);
        if (npc == null)
        {
            AdvanceToVendor(npcName, dataId, fallbackPosition);
            return false;
        }

        var maxInteractDistance = GetDesiredInteractDistance(npc);
        var distance = GetDistanceTo(npc);
        if (distance <= maxInteractDistance)
            return true;

        AdvanceToVendor(npcName, dataId, fallbackPosition);
        return false;
    }

    private static float GetDesiredInteractDistance(IGameObject npc)
        => Math.Min(TargetInteractDistance, GameHelpers.GetValidInteractionDistance(npc));
}
