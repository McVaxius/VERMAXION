using System;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using VERMAXION.IPC;

namespace VERMAXION.Services;

public sealed class VendorStockService
{
    private const uint GysahlGreensItemId = 4868;
    private const uint Grade8DarkMatterItemId = 33916;
    private const float TargetInteractDistance = 3.0f;

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
    private ushort travelOriginTerritory;
    private bool sawTravelTransition;
    private VendorStockState cleanupNextState = VendorStockState.Complete;
    private int observedGysahlCount;
    private int observedDarkMatterCount;

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
        GameHelpers.ResetInteractionState();
        SetState(VendorStockState.CheckingNeeds);
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
    }

    public void Update()
    {
        if (state == VendorStockState.Idle || state == VendorStockState.Complete || state == VendorStockState.Failed)
            return;

        var elapsed = (DateTime.UtcNow - stateEnteredAt).TotalSeconds;

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
                        log.Information("[VendorStock] Opening Maisenta's Gysahl Greens shop");
                        GameHelpers.FireAddonCallback("SelectIconString", true, 0);
                    }
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

    private int GetGysahlTarget()
        => Math.Max(0, configManager.GetActiveConfig().VendorStockGysahlGreensTarget);

    private int GetDarkMatterTarget()
        => Math.Max(0, configManager.GetActiveConfig().VendorStockGrade8DarkMatterTarget);

    private bool AdvanceToVendor(string npcName)
    {
        var npc = GameHelpers.FindObjectByName(npcName);
        if (npc == null)
            return false;

        var distance = GetDistanceTo(npc);
        var maxInteractDistance = GetDesiredInteractDistance(npc);
        if (distance <= maxInteractDistance)
        {
            vnavmesh.Stop();
            return true;
        }

        if ((DateTime.UtcNow - lastNavigationCommandAt).TotalSeconds >= 2)
        {
            lastNavigationCommandAt = DateTime.UtcNow;
            log.Information($"[VendorStock] Navigating to {npcName} ({distance:F1}y)");
            vnavmesh.PathfindAndMoveTo(npc.Position);
        }

        return false;
    }

    private bool TryInteract(string npcName)
    {
        if ((DateTime.UtcNow - lastInteractionAttemptAt).TotalSeconds < 5.25)
            return false;

        lastInteractionAttemptAt = DateTime.UtcNow;
        log.Information($"[VendorStock] Interacting with {npcName}");
        return GameHelpers.TargetAndInteract(npcName);
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
            log.Information($"{label} count increased to {currentCount}/{targetCount}");
        }

        if (currentCount >= targetCount)
        {
            log.Information($"{label} target reached: {currentCount}/{targetCount}");
            return true;
        }

        GameHelpers.ClickYesIfVisible();

        if (!GameHelpers.IsAddonVisible("Shop"))
            return false;

        if (!TryFirePurchaseAction(repeatDelaySeconds))
            return false;

        var remaining = Math.Max(1, targetCount - currentCount);
        var purchaseQuantity = Math.Clamp(remaining, 1, maxPurchaseQuantity);
        var purchaseArgs = new object[purchaseArgPrefix.Length + 1];
        Array.Copy(purchaseArgPrefix, purchaseArgs, purchaseArgPrefix.Length);
        purchaseArgs[^1] = purchaseQuantity;

        log.Information($"{label} below target ({currentCount}/{targetCount}), purchasing {purchaseQuantity}");
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
        GameHelpers.ResetInteractionState();
        if (NeedsDarkMatter())
        {
            SetState(VendorStockState.DarkMatterLifestreaming);
        }
        else
        {
            BeginVendorCleanup(VendorStockState.Complete);
        }
    }

    private void FinishDarkMatterPhase()
    {
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
    }

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

    private bool EnsureVendorInRange(string npcName)
    {
        var npc = GameHelpers.FindObjectByName(npcName);
        if (npc == null)
            return false;

        var maxInteractDistance = GetDesiredInteractDistance(npc);
        var distance = GetDistanceTo(npc);
        if (distance <= maxInteractDistance)
            return true;

        AdvanceToVendor(npcName);
        return false;
    }

    private static float GetDesiredInteractDistance(IGameObject npc)
        => Math.Min(TargetInteractDistance, GameHelpers.GetValidInteractionDistance(npc));
}
