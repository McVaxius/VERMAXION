using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Memory;
using Dalamud.Plugin.Services;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using VERMAXION.IPC;
using VERMAXION.Models;

namespace VERMAXION.Services;

public sealed class RetainerListingRefillService
{
    private enum RefillState
    {
        Idle,
        TravelingToBell,
        MovingToBell,
        InteractingBell,
        SelectingRetainer,
        OpeningSellList,
        ScanningListings,
        WithdrawingNextListing,
        OpeningContextMenu,
        SelectingReturnToInventory,
        ConfirmingReturn,
        VerifyingWithdrawal,
        ClosingRetainerUi,
        Complete,
        Failed,
    }

    private sealed record RetainerTarget(string Name, int RetainerIndex, int DisplayOrder, int MarketItemCount);
    private sealed record RetainerListEntry(int Index, string Name);
    private sealed record ListingSlot(int Slot, uint ItemId, int Quantity, bool IsHq, string ItemName);

    private const uint RevenantsTollTerritoryId = 156;
    private const string RetainerListAddonName = "RetainerList";
    private const string RetainerSellListAddonName = "RetainerSellList";
    private const string SelectStringAddonName = "SelectString";
    private const string ContextMenuAddonName = "ContextMenu";
    private static readonly Vector3 RevenantsTollBellApproachPosition = new(12.188f, 29.000f, -735.430f);
    private static readonly TimeSpan DefaultStepTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan TravelTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan WithdrawalTimeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan CloseRetryInterval = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan RetainerListCloseSecondCallbackDelay = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan CloseSignatureLogInterval = TimeSpan.FromSeconds(5);
    private static readonly string[] RetainerCloseAddonPriority =
    {
        "SelectYesno",
        ContextMenuAddonName,
        RetainerSellListAddonName,
        "RetainerItemTransferList",
        "InventoryRetainerLarge",
        "InventoryRetainer",
        "RetainerGrid0",
        "RetainerGrid1",
        "RetainerGrid2",
        "RetainerGrid3",
        "RetainerGrid4",
        "RetainerCrystalGrid",
        SelectStringAddonName,
        RetainerListAddonName,
    };

    private readonly IPluginLog log;
    private readonly ConfigManager configManager;
    private readonly VNavmeshIPC vnavmesh;

    private RefillState state = RefillState.Idle;
    private DateTime stateEnteredAt = DateTime.MinValue;
    private DateTime nextActionAt = DateTime.MinValue;
    private DateTime lastNavigationCommandAt = DateTime.MinValue;
    private DateTime lastRetainerCloseAttemptAt = DateTime.MinValue;
    private DateTime lastCloseSignatureLoggedAt = DateTime.MinValue;
    private string closeVisibleAddonSignature = string.Empty;
    private bool retainerListCloseSecondPending;
    private DateTime retainerListCloseSecondReadyAt = DateTime.MinValue;
    private int closeAttemptCount;
    private bool closeThenFail;
    private bool bellInteracted;
    private bool retainerSelected;
    private bool sellMenuSelected;
    private bool contextOpened;
    private bool confirmationClicked;
    private RefillFromListingsSelectionMode selectionMode = RefillFromListingsSelectionMode.All;
    private List<RetainerTarget> targets = new();
    private List<ListingSlot> listingPlan = new();
    private int targetIndex;
    private int listingIndex;
    private ListingSlot? pendingListing;
    private int pendingInventoryCount;

    public string StatusText { get; private set; } = "Idle.";
    public string LastError { get; private set; } = string.Empty;
    public bool IsActive => state != RefillState.Idle && state != RefillState.Complete && state != RefillState.Failed;
    public bool IsComplete => state == RefillState.Complete;
    public bool IsFailed => state == RefillState.Failed;

    public RetainerListingRefillService(
        IPluginLog log,
        ConfigManager configManager,
        VNavmeshIPC vnavmesh)
    {
        this.log = log;
        this.configManager = configManager;
        this.vnavmesh = vnavmesh;
    }

    public void Start(CharacterConfig config)
    {
        if (IsActive)
            return;

        Reset();
        selectionMode = config.RefillFromListingsSelectionMode;

        if (!TryBuildRetainerTargets(out targets, out var error))
        {
            Fail(error);
            return;
        }

        if (targets.Count == 0)
        {
            log.Information("[Listings] No retainers have market listings.");
            SetState(RefillState.Complete, "No retainer listings found.");
            return;
        }

        log.Information($"[Listings] Starting refill from listings. targets={targets.Count}, mode={selectionMode}");
        if (TryFindNearestBell(out _))
            SetState(RefillState.MovingToBell, "Moving to retainer bell...");
        else
            SetState(RefillState.TravelingToBell, "No nearby bell found. Traveling to Revenant's Toll...");
    }

    public void RunTask()
    {
        log.Information("[VERMAXION] Manual retainer listing refill triggered");
        Start(configManager.GetActiveConfig());
    }

    public void Reset()
    {
        vnavmesh.Stop();
        state = RefillState.Idle;
        stateEnteredAt = DateTime.MinValue;
        nextActionAt = DateTime.MinValue;
        lastNavigationCommandAt = DateTime.MinValue;
        LastError = string.Empty;
        StatusText = "Idle.";
        targets = new List<RetainerTarget>();
        listingPlan = new List<ListingSlot>();
        targetIndex = 0;
        listingIndex = 0;
        pendingListing = null;
        pendingInventoryCount = 0;
        closeThenFail = false;
        ResetRetainerPhaseFlags();
        ResetCloseTracking();
    }

    public void Update()
    {
        if (state is RefillState.Idle or RefillState.Complete or RefillState.Failed)
            return;

        if (DateTime.UtcNow < nextActionAt)
            return;

        if (IsTimedOut())
        {
            Fail($"Timed out during {state}.");
            return;
        }

        switch (state)
        {
            case RefillState.TravelingToBell:
                TickTravelingToBell();
                break;
            case RefillState.MovingToBell:
                TickMovingToBell();
                break;
            case RefillState.InteractingBell:
                TickInteractingBell();
                break;
            case RefillState.SelectingRetainer:
                TickSelectingRetainer();
                break;
            case RefillState.OpeningSellList:
                TickOpeningSellList();
                break;
            case RefillState.ScanningListings:
                TickScanningListings();
                break;
            case RefillState.WithdrawingNextListing:
                TickWithdrawingNextListing();
                break;
            case RefillState.OpeningContextMenu:
                TickOpeningContextMenu();
                break;
            case RefillState.SelectingReturnToInventory:
                TickSelectingReturnToInventory();
                break;
            case RefillState.ConfirmingReturn:
                TickConfirmingReturn();
                break;
            case RefillState.VerifyingWithdrawal:
                TickVerifyingWithdrawal();
                break;
            case RefillState.ClosingRetainerUi:
                TickClosingRetainerUi();
                break;
        }
    }

    public void CloseRetainerUi()
    {
        if (state != RefillState.ClosingRetainerUi)
        {
            closeThenFail = true;
            SetState(RefillState.ClosingRetainerUi, "Closing retainer UI...");
        }

        TickClosingRetainerUi();
    }

    private void TickTravelingToBell()
    {
        if (IsLoading())
        {
            StatusText = "Traveling to retainer bell...";
            nextActionAt = DateTime.UtcNow.AddSeconds(1);
            return;
        }

        if (!bellInteracted)
        {
            bellInteracted = true;
            CommandHelper.SendCommand("/li Revenant's Toll");
            nextActionAt = DateTime.UtcNow.AddSeconds(5);
            return;
        }

        if (Plugin.ClientState.TerritoryType != RevenantsTollTerritoryId)
        {
            StatusText = "Waiting for Revenant's Toll arrival...";
            nextActionAt = DateTime.UtcNow.AddSeconds(1);
            return;
        }

        SetState(RefillState.MovingToBell, "Moving to Revenant's Toll retainer bell...");
    }

    private void TickMovingToBell()
    {
        var player = Plugin.ObjectTable.LocalPlayer;
        if (player == null)
        {
            StatusText = "Waiting for player before moving to retainer bell...";
            nextActionAt = DateTime.UtcNow.AddSeconds(1);
            return;
        }

        if (Plugin.ClientState.TerritoryType != RevenantsTollTerritoryId && !TryFindNearestBell(out _))
        {
            SetState(RefillState.TravelingToBell, "Traveling to Revenant's Toll retainer bell...");
            return;
        }

        if (!TryFindNearestBell(out var bell))
        {
            var approachDistance = Vector3.Distance(player.Position, RevenantsTollBellApproachPosition);
            if (Plugin.ClientState.TerritoryType == RevenantsTollTerritoryId && approachDistance > 3f)
            {
                MoveTo(RevenantsTollBellApproachPosition, $"Moving to Revenant's Toll bell approach... ({approachDistance:F1}y)");
                return;
            }

            StatusText = "Waiting for Summoning Bell object...";
            nextActionAt = DateTime.UtcNow.AddSeconds(1);
            return;
        }

        var distance = Vector3.Distance(player.Position, bell.Position);
        if (distance > 4f)
        {
            MoveTo(bell.Position, $"Moving to retainer bell... ({distance:F1}y)");
            return;
        }

        vnavmesh.Stop();
        SetState(RefillState.InteractingBell, "Opening retainer bell...");
    }

    private void TickInteractingBell()
    {
        if (GameHelpers.IsAddonVisible(RetainerListAddonName))
        {
            SetState(RefillState.SelectingRetainer, "Selecting retainer...");
            return;
        }

        if (!bellInteracted)
        {
            if (!TryFindNearestBell(out var bell))
            {
                Fail("Retainer bell disappeared before interaction.");
                return;
            }

            Plugin.TargetManager.Target = bell;
            GameHelpers.InteractWithObject(bell);
            bellInteracted = true;
            nextActionAt = DateTime.UtcNow.AddSeconds(2);
            return;
        }

        StatusText = "Waiting for retainer list...";
        nextActionAt = DateTime.UtcNow.AddSeconds(1);
    }

    private void TickSelectingRetainer()
    {
        var target = CurrentTarget;
        if (target == null)
        {
            BeginClosingRetainerUi(false, "No remaining retainer target.");
            return;
        }

        if (GameHelpers.IsAddonVisible(SelectStringAddonName) && retainerSelected)
        {
            SetState(RefillState.OpeningSellList, "Opening retainer market listings...");
            return;
        }

        if (GameHelpers.IsAddonVisible(SelectStringAddonName) && !retainerSelected)
        {
            var selectIndex = FindSelectStringIndex(target.Name);
            if (selectIndex >= 0)
            {
                GameHelpers.FireAddonCallback(SelectStringAddonName, true, selectIndex);
                retainerSelected = true;
                nextActionAt = DateTime.UtcNow.AddSeconds(2);
                return;
            }
        }

        if (GameHelpers.IsAddonVisible(RetainerListAddonName) && !retainerSelected)
        {
            if (!TryFindRetainerListIndex(target.Name, out var index, out var visibleNames))
            {
                var visible = visibleNames.Count == 0 ? "none parsed" : string.Join(", ", visibleNames);
                Fail($"RetainerList is visible but target retainer '{target.Name}' was not found. Visible: {visible}.");
                return;
            }

            log.Information($"[Listings] RetainerList target '{target.Name}' matched row {index}.");
            GameHelpers.FireAddonCallback(RetainerListAddonName, true, 2, index, 0, 0);
            retainerSelected = true;
            nextActionAt = DateTime.UtcNow.AddSeconds(2);
            return;
        }

        StatusText = $"Waiting to select retainer {target.Name}...";
        nextActionAt = DateTime.UtcNow.AddSeconds(1);
    }

    private void TickOpeningSellList()
    {
        if (GameHelpers.IsAddonVisible(RetainerSellListAddonName))
        {
            SetState(RefillState.ScanningListings, "Scanning retainer market listings...");
            return;
        }

        if (GameHelpers.IsAddonVisible(SelectStringAddonName) && !sellMenuSelected)
        {
            var index = FindSellItemsMenuIndex();
            if (index < 0)
            {
                Fail("Could not find retainer sell-items menu entry.");
                return;
            }

            GameHelpers.FireAddonCallback(SelectStringAddonName, true, index);
            sellMenuSelected = true;
            nextActionAt = DateTime.UtcNow.AddSeconds(2);
            return;
        }

        StatusText = "Waiting for retainer menu...";
        nextActionAt = DateTime.UtcNow.AddSeconds(1);
    }

    private void TickScanningListings()
    {
        if (!GameHelpers.IsAddonVisible(RetainerSellListAddonName))
        {
            StatusText = "Waiting for RetainerSellList...";
            nextActionAt = DateTime.UtcNow.AddSeconds(1);
            return;
        }

        if (!TryScanRetainerMarketListings(out var slots, out var detail))
        {
            Fail(detail);
            return;
        }

        listingPlan = BuildListingPlan(slots);
        listingIndex = 0;
        log.Information($"[Listings] {CurrentTarget?.Name}: scanned {slots.Count} listing(s), selected {listingPlan.Count} for withdrawal.");

        if (listingPlan.Count == 0)
        {
            BeginClosingRetainerUi(false, $"No selected listings for {CurrentTarget?.Name ?? "retainer"}. Closing retainer UI...");
            return;
        }

        SetState(RefillState.WithdrawingNextListing, "Withdrawing selected listings...");
    }

    private void TickWithdrawingNextListing()
    {
        while (listingIndex < listingPlan.Count)
        {
            var listing = listingPlan[listingIndex];
            var current = GetListingSlotSnapshot(listing.Slot);
            if (current == null || current.ItemId != listing.ItemId || current.Quantity != listing.Quantity)
            {
                log.Information($"[Listings] Slot {listing.Slot} already changed before withdrawal; skipping.");
                listingIndex++;
                continue;
            }

            pendingListing = listing;
            SetState(RefillState.OpeningContextMenu, $"Opening context menu for {listing.ItemName}...");
            return;
        }

        BeginClosingRetainerUi(false, $"Finished {CurrentTarget?.Name ?? "retainer"}. Closing retainer UI...");
    }

    private void TickOpeningContextMenu()
    {
        if (pendingListing == null)
        {
            SetState(RefillState.WithdrawingNextListing, "Selecting next listing...");
            return;
        }

        if (!GameHelpers.IsAddonVisible(RetainerSellListAddonName))
        {
            StatusText = "Waiting for RetainerSellList before opening context menu...";
            nextActionAt = DateTime.UtcNow.AddSeconds(1);
            return;
        }

        if (!contextOpened)
        {
            if (!TryOpenContextMenu(pendingListing, out var detail))
            {
                Fail(detail);
                return;
            }

            log.Information(detail);
            contextOpened = true;
            nextActionAt = DateTime.UtcNow.AddMilliseconds(750);
            return;
        }

        SetState(RefillState.SelectingReturnToInventory, "Selecting Return to Inventory...");
    }

    private void TickSelectingReturnToInventory()
    {
        if (!GameHelpers.IsAddonVisible(ContextMenuAddonName))
        {
            StatusText = "Waiting for listing context menu...";
            nextActionAt = DateTime.UtcNow.AddMilliseconds(500);
            return;
        }

        var result = TrySelectReturnToInventory(out var detail);
        switch (result)
        {
            case ContextSelectResult.Selected:
                log.Information(detail);
                SetState(RefillState.ConfirmingReturn, "Confirming listing return...");
                break;
            case ContextSelectResult.Disabled:
                Fail(detail);
                break;
            case ContextSelectResult.NotFound:
                StatusText = detail;
                nextActionAt = DateTime.UtcNow.AddMilliseconds(500);
                break;
        }
    }

    private void TickConfirmingReturn()
    {
        if (GameHelpers.IsAddonVisible("SelectYesno"))
        {
            if (!confirmationClicked)
            {
                GameHelpers.ClickYesIfVisible();
                confirmationClicked = true;
                nextActionAt = DateTime.UtcNow.AddSeconds(1);
                return;
            }
        }

        if (!confirmationClicked && DateTime.UtcNow - stateEnteredAt < TimeSpan.FromSeconds(1.5))
        {
            StatusText = "Waiting for listing return confirmation...";
            nextActionAt = DateTime.UtcNow.AddMilliseconds(250);
            return;
        }

        SetState(RefillState.VerifyingWithdrawal, "Verifying listing return...");
    }

    private void TickVerifyingWithdrawal()
    {
        if (pendingListing == null)
        {
            SetState(RefillState.WithdrawingNextListing, "Selecting next listing...");
            return;
        }

        if (IsWithdrawalVerified(pendingListing, out var detail))
        {
            log.Information(detail);
            listingIndex++;
            pendingListing = null;
            SetState(RefillState.WithdrawingNextListing, "Selecting next listing...");
            nextActionAt = DateTime.UtcNow.AddMilliseconds(500);
            return;
        }

        StatusText = detail;
        nextActionAt = DateTime.UtcNow.AddMilliseconds(500);
    }

    private void TickClosingRetainerUi()
    {
        if (!TryCloseVisibleRetainerUi(out var status))
        {
            if (closeThenFail)
            {
                SetState(RefillState.Failed, LastError);
                return;
            }

            targetIndex++;
            if (targetIndex >= targets.Count)
            {
                SetState(RefillState.Complete, "Retainer listing refill complete.");
                return;
            }

            ResetRetainerPhaseFlags();
            if (TryFindNearestBell(out _))
                SetState(RefillState.MovingToBell, $"Moving to next retainer: {CurrentTarget?.Name}...");
            else
                SetState(RefillState.TravelingToBell, "Traveling to retainer bell...");
            return;
        }

        StatusText = status;
    }

    private bool TryOpenContextMenu(ListingSlot listing, out string detail)
    {
        detail = string.Empty;
        nint addonPtr = Plugin.GameGui.GetAddonByName(RetainerSellListAddonName, 1);
        if (addonPtr == 0)
        {
            detail = "RetainerSellList addon is not available.";
            return false;
        }

        unsafe
        {
            var addon = (AtkUnitBase*)addonPtr;
            var agent = AgentInventoryContext.Instance();
            if (agent == null)
            {
                detail = "AgentInventoryContext is unavailable.";
                return false;
            }

            pendingInventoryCount = GetPlayerInventoryCount(listing.ItemId, listing.IsHq);
            agent->OpenForItemSlot(InventoryType.RetainerMarket, listing.Slot, 0, addon->Id);
            detail = $"[Listings] Opened context for {listing.ItemName} in RetainerMarket[{listing.Slot}] via {RetainerSellListAddonName}#{addon->Id}.";
            return true;
        }
    }

    private ContextSelectResult TrySelectReturnToInventory(out string detail)
    {
        detail = "Waiting for Return to Inventory context entry...";
        var addonPtr = Plugin.GameGui.GetAddonByName(ContextMenuAddonName, 1);
        if (addonPtr == 0)
            return ContextSelectResult.NotFound;

        try
        {
            var expected = GetReturnToInventoryTexts();
            var menu = new AddonMaster.ContextMenu(addonPtr);
            foreach (var entry in menu.Entries)
            {
                var text = CleanAddonText(entry.Text);
                if (!MatchesAny(text, expected))
                    continue;

                if (!entry.Enabled)
                {
                    detail = $"Return to Inventory is disabled for {pendingListing?.ItemName ?? "listing"}. Inventory may be full.";
                    return ContextSelectResult.Disabled;
                }

                if (!entry.Select())
                {
                    detail = $"Return to Inventory context entry was found but could not be selected: {text}.";
                    return ContextSelectResult.Disabled;
                }

                detail = $"[Listings] Selected context entry '{text}' for {pendingListing?.ItemName ?? "listing"}.";
                return ContextSelectResult.Selected;
            }

            var visible = string.Join(", ", menu.Entries.Select(entry => CleanAddonText(entry.Text)));
            detail = $"Waiting for Return to Inventory context entry. Visible entries: {visible}";
            return ContextSelectResult.NotFound;
        }
        catch (Exception ex)
        {
            detail = $"Failed to read context menu: {ex.Message}";
            return ContextSelectResult.Disabled;
        }
    }

    private bool IsWithdrawalVerified(ListingSlot listing, out string detail)
    {
        var current = GetListingSlotSnapshot(listing.Slot);
        if (current == null)
        {
            detail = $"[Listings] Verified {listing.ItemName}: RetainerMarket[{listing.Slot}] is empty.";
            return true;
        }

        if (current.ItemId != listing.ItemId || current.Quantity != listing.Quantity)
        {
            detail = $"[Listings] Verified {listing.ItemName}: RetainerMarket[{listing.Slot}] changed.";
            return true;
        }

        var inventoryCount = GetPlayerInventoryCount(listing.ItemId, listing.IsHq);
        if (inventoryCount > pendingInventoryCount)
        {
            detail = $"[Listings] Verified {listing.ItemName}: inventory count increased {pendingInventoryCount}->{inventoryCount}.";
            return true;
        }

        detail = $"Waiting for {listing.ItemName} return confirmation...";
        return false;
    }

    private List<ListingSlot> BuildListingPlan(IReadOnlyList<ListingSlot> slots)
    {
        if (selectionMode == RefillFromListingsSelectionMode.All)
            return slots.ToList();

        var selected = new List<ListingSlot>();
        foreach (var slot in slots)
        {
            var roll = Random.Shared.Next(2);
            log.Information($"[Listings] Random roll for {CurrentTarget?.Name} slot {slot.Slot} {slot.ItemName}: {roll}");
            if (roll == 1)
                selected.Add(slot);
        }

        return selected;
    }

    private unsafe bool TryScanRetainerMarketListings(out List<ListingSlot> slots, out string detail)
    {
        slots = new List<ListingSlot>();
        detail = string.Empty;

        var manager = InventoryManager.Instance();
        if (manager == null)
        {
            detail = "InventoryManager is unavailable.";
            return false;
        }

        var container = manager->GetInventoryContainer(InventoryType.RetainerMarket);
        if (container == null || !container->IsLoaded)
        {
            detail = "RetainerMarket inventory container is not loaded.";
            return false;
        }

        for (var i = 0; i < container->Size; i++)
        {
            var item = container->GetInventorySlot(i);
            if (item == null || item->ItemId == 0 || item->Quantity <= 0)
                continue;

            var isHq = (item->Flags & InventoryItem.ItemFlags.HighQuality) != 0;
            slots.Add(new ListingSlot(i, item->ItemId, item->Quantity, isHq, GameHelpers.GetItemName(item->ItemId)));
        }

        detail = $"Scanned {slots.Count} RetainerMarket listing(s).";
        return true;
    }

    private unsafe ListingSlot? GetListingSlotSnapshot(int slot)
    {
        var manager = InventoryManager.Instance();
        if (manager == null)
            return null;

        var container = manager->GetInventoryContainer(InventoryType.RetainerMarket);
        if (container == null || !container->IsLoaded || slot < 0 || slot >= container->Size)
            return null;

        var item = container->GetInventorySlot(slot);
        if (item == null || item->ItemId == 0 || item->Quantity <= 0)
            return null;

        var isHq = (item->Flags & InventoryItem.ItemFlags.HighQuality) != 0;
        return new ListingSlot(slot, item->ItemId, item->Quantity, isHq, GameHelpers.GetItemName(item->ItemId));
    }

    private unsafe int GetPlayerInventoryCount(uint itemId, bool isHq)
    {
        var manager = InventoryManager.Instance();
        return manager == null
            ? 0
            : manager->GetInventoryItemCount(itemId, isHq, false, false);
    }

    private bool TryBuildRetainerTargets(out List<RetainerTarget> retainerTargets, out string error)
    {
        retainerTargets = new List<RetainerTarget>();
        error = string.Empty;

        try
        {
            unsafe
            {
                var manager = RetainerManager.Instance();
                if (manager == null || !manager->IsReady)
                {
                    error = "RetainerManager is not ready.";
                    return false;
                }

                for (var i = 0; i < manager->Retainers.Length; i++)
                {
                    var retainer = manager->Retainers[i];
                    if (retainer.RetainerId == 0 || retainer.MarketItemCount == 0)
                        continue;

                    var name = retainer.NameString.Trim();
                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    retainerTargets.Add(new RetainerTarget(
                        name,
                        i,
                        manager->DisplayOrder.IndexOf((byte)i),
                        retainer.MarketItemCount));
                }
            }

            if (retainerTargets.Any(target => target.DisplayOrder < 0))
                retainerTargets = retainerTargets.OrderBy(target => target.RetainerIndex).ToList();
            else
                retainerTargets = retainerTargets.OrderBy(target => target.DisplayOrder).ToList();

            foreach (var target in retainerTargets)
                log.Information($"[Listings] Target retainer: {target.Name}, market listings={target.MarketItemCount}, displayOrder={target.DisplayOrder}");

            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to read RetainerManager listing counts: {ex.Message}";
            return false;
        }
    }

    private RetainerTarget? CurrentTarget
        => targetIndex >= 0 && targetIndex < targets.Count ? targets[targetIndex] : null;

    private bool TryFindNearestBell(out IGameObject bell)
    {
        var player = Plugin.ObjectTable.LocalPlayer;
        bell = null!;
        if (player == null)
            return false;

        bell = Plugin.ObjectTable
            .Where(obj => obj != null && obj.ObjectKind == ObjectKind.EventObj)
            .Where(obj => obj.Name.TextValue.Contains("Summoning Bell", StringComparison.OrdinalIgnoreCase))
            .OrderBy(obj => Vector3.Distance(player.Position, obj.Position))
            .FirstOrDefault()!;

        return bell != null;
    }

    private void MoveTo(Vector3 position, string status)
    {
        if ((DateTime.UtcNow - lastNavigationCommandAt).TotalSeconds >= 2)
        {
            lastNavigationCommandAt = DateTime.UtcNow;
            vnavmesh.PathfindAndMoveTo(position);
        }

        StatusText = status;
        nextActionAt = DateTime.UtcNow.AddSeconds(1);
    }

    private int FindSellItemsMenuIndex()
    {
        var addonText = GetAddonText(2381);
        var index = FindSelectStringIndex(addonText);
        if (index >= 0)
            return index;

        return FindSelectStringIndex("Sell items in your retainer's inventory on the market", "Sell items");
    }

    private string[] GetReturnToInventoryTexts()
    {
        return new[]
        {
            GetAddonText(1388),
            GetAddonText(6947),
            "Return to Inventory",
        }
        .Where(text => !string.IsNullOrWhiteSpace(text))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
    }

    private static bool MatchesAny(string text, IReadOnlyCollection<string> expected)
    {
        var normalized = NormalizeText(text);
        return expected.Any(value =>
        {
            var candidate = NormalizeText(value);
            return normalized == candidate ||
                   normalized.Contains(candidate, StringComparison.OrdinalIgnoreCase) ||
                   candidate.Contains(normalized, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static unsafe int FindSelectStringIndex(params string[] needles)
    {
        try
        {
            nint addonPtr = Plugin.GameGui.GetAddonByName(SelectStringAddonName, 1);
            if (addonPtr == 0)
                return -1;

            var addon = (AtkUnitBase*)addonPtr;
            if (!addon->IsVisible)
                return -1;

            var master = new AddonMaster.SelectString(addonPtr);
            for (var i = 0; i < master.EntryCount; i++)
            {
                var text = CleanAddonText(master.Entries[i].Text);
                if (needles.Any(needle => !string.IsNullOrWhiteSpace(needle) &&
                                           MatchesAny(text, new[] { needle })))
                    return i;
            }
        }
        catch
        {
        }

        return -1;
    }

    private static unsafe bool TryFindRetainerListIndex(string targetName, out int index, out List<string> visibleNames)
    {
        index = -1;
        visibleNames = new List<string>();

        if (string.IsNullOrWhiteSpace(targetName))
            return false;

        try
        {
            nint addonPtr = Plugin.GameGui.GetAddonByName(RetainerListAddonName, 1);
            if (addonPtr == 0)
                return false;

            var addon = (AtkUnitBase*)addonPtr;
            if (!addon->IsVisible)
                return false;

            var entries = ReadRetainerListEntries(addon);
            visibleNames = entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
                .Select(entry => entry.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var normalizedTarget = NormalizeRetainerName(targetName);
            var match = entries.FirstOrDefault(entry => NormalizeRetainerName(entry.Name) == normalizedTarget);
            if (match == null)
                return false;

            index = match.Index;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static unsafe List<RetainerListEntry> ReadRetainerListEntries(AtkUnitBase* addon)
    {
        var reader = new RetainerListReader(addon);
        return reader.Retainers
            .Where(entry => entry.IsActive && IsPlausibleRetainerName(entry.Name))
            .Select(entry => new RetainerListEntry(entry.Index, entry.Name.Trim()))
            .ToList();
    }

    private sealed unsafe class RetainerListReader(AtkUnitBase* addon)
    {
        public List<RetainerEntryReader> Retainers => Loop(3, 10, 10);

        private List<RetainerEntryReader> Loop(int offset, int size, int maxLength)
        {
            var entries = new List<RetainerEntryReader>();
            for (var i = 0; i < maxLength; i++)
            {
                var entry = new RetainerEntryReader(addon, offset + i * size, i);
                if (entry.IsNull)
                    break;

                entries.Add(entry);
            }

            return entries;
        }
    }

    private sealed unsafe class RetainerEntryReader(AtkUnitBase* addon, int beginOffset, int index)
    {
        public int Index => index;
        public bool IsNull => addon->AtkValuesCount == 0 || ReadValue(0)->Type == 0;
        public string Name => ReadString(0);
        public bool IsActive => ReadBool(8) ?? false;

        private AtkValue* ReadValue(int offset)
        {
            var valueIndex = beginOffset + offset;
            if (valueIndex < 0 || valueIndex >= addon->AtkValuesCount)
                throw new ArgumentOutOfRangeException(nameof(offset));

            return &addon->AtkValues[valueIndex];
        }

        private string ReadString(int offset)
        {
            var value = ReadValue(offset);
            if (value->Type == 0)
                return string.Empty;

            if (value->Type is not (AtkValueType.String or AtkValueType.ManagedString or AtkValueType.String8 or AtkValueType.WideString))
                return string.Empty;

            return value->String.Value == null
                ? string.Empty
                : MemoryHelper.ReadStringNullTerminated((nint)value->String.Value);
        }

        private bool? ReadBool(int offset)
        {
            var value = ReadValue(offset);
            if (value->Type == 0)
                return null;

            if (value->Type != AtkValueType.Bool)
                return null;

            return value->Byte != 0;
        }
    }

    private bool TryCloseVisibleRetainerUi(out string status)
    {
        var now = DateTime.UtcNow;
        status = "Closing retainer UI...";

        if (retainerListCloseSecondPending)
        {
            if (now < retainerListCloseSecondReadyAt)
                return true;

            retainerListCloseSecondPending = false;
            if (GameHelpers.IsAddonVisible(RetainerListAddonName))
            {
                log.Information("[Listings] Closing RetainerList: -2");
                GameHelpers.FireAddonCallback(RetainerListAddonName, true, -2);
            }

            lastRetainerCloseAttemptAt = now;
            return true;
        }

        var visibleAddons = GetVisibleRetainerCloseAddons();
        if (visibleAddons.Count == 0)
        {
            ResetCloseTracking();
            return false;
        }

        LogVisibleCloseAddons(visibleAddons, now);
        status = $"Closing retainer UI... ({string.Join(", ", visibleAddons)})";

        if (closeAttemptCount > 0 && now - lastRetainerCloseAttemptAt < CloseRetryInterval)
        {
            nextActionAt = lastRetainerCloseAttemptAt.Add(CloseRetryInterval);
            return true;
        }

        var addonToClose = visibleAddons[0];
        if (addonToClose == RetainerListAddonName)
        {
            log.Information("[Listings] Closing RetainerList: -1");
            GameHelpers.FireAddonCallback(RetainerListAddonName, true, -1);
            retainerListCloseSecondPending = true;
            retainerListCloseSecondReadyAt = now.Add(RetainerListCloseSecondCallbackDelay);
        }
        else
        {
            var useCallback = closeAttemptCount % 2 == 0;
            if (useCallback)
            {
                if (!GameHelpers.TryCloseAddonByCallback(addonToClose))
                    GameHelpers.CloseCurrentAddon();
            }
            else
            {
                GameHelpers.CloseCurrentAddon();
            }
        }

        closeAttemptCount++;
        lastRetainerCloseAttemptAt = now;
        nextActionAt = now.Add(CloseRetryInterval);
        return true;
    }

    private static List<string> GetVisibleRetainerCloseAddons()
    {
        var visibleAddons = new List<string>();
        foreach (var addonName in RetainerCloseAddonPriority)
        {
            if (GameHelpers.IsAddonVisible(addonName))
                visibleAddons.Add(addonName);
        }

        return visibleAddons;
    }

    private void LogVisibleCloseAddons(IReadOnlyCollection<string> visibleAddons, DateTime now)
    {
        var signature = string.Join(", ", visibleAddons);
        if (string.Equals(closeVisibleAddonSignature, signature, StringComparison.Ordinal) &&
            now - lastCloseSignatureLoggedAt < CloseSignatureLogInterval)
        {
            return;
        }

        closeVisibleAddonSignature = signature;
        lastCloseSignatureLoggedAt = now;
        log.Information($"[Listings] Retainer close surfaces visible: {signature}.");
    }

    private void BeginClosingRetainerUi(bool failed, string status)
    {
        closeThenFail = failed;
        SetState(RefillState.ClosingRetainerUi, status);
    }

    private void Fail(string message)
    {
        LastError = message;
        log.Warning($"[Listings] {message}");
        if (state == RefillState.ClosingRetainerUi)
        {
            SetState(RefillState.Failed, message);
            return;
        }

        BeginClosingRetainerUi(true, $"Failure: {message}. Closing retainer UI...");
    }

    private bool IsTimedOut()
    {
        var elapsed = DateTime.UtcNow - stateEnteredAt;
        return state switch
        {
            RefillState.TravelingToBell => elapsed > TravelTimeout,
            RefillState.OpeningContextMenu or RefillState.SelectingReturnToInventory or RefillState.ConfirmingReturn or RefillState.VerifyingWithdrawal => elapsed > WithdrawalTimeout,
            RefillState.ClosingRetainerUi => elapsed > DefaultStepTimeout,
            _ => elapsed > DefaultStepTimeout,
        };
    }

    private void SetState(RefillState newState, string status)
    {
        log.Information($"[Listings] {state} -> {newState}: {status}");
        state = newState;
        stateEnteredAt = DateTime.UtcNow;
        nextActionAt = DateTime.UtcNow;
        StatusText = status;

        switch (newState)
        {
            case RefillState.TravelingToBell:
                bellInteracted = false;
                break;
            case RefillState.InteractingBell:
                bellInteracted = false;
                break;
            case RefillState.SelectingRetainer:
                retainerSelected = false;
                break;
            case RefillState.OpeningSellList:
                sellMenuSelected = false;
                break;
            case RefillState.OpeningContextMenu:
                contextOpened = false;
                confirmationClicked = false;
                break;
            case RefillState.ConfirmingReturn:
                confirmationClicked = false;
                break;
            case RefillState.ClosingRetainerUi:
                ResetCloseTracking();
                break;
        }
    }

    private void ResetRetainerPhaseFlags()
    {
        bellInteracted = false;
        retainerSelected = false;
        sellMenuSelected = false;
        contextOpened = false;
        confirmationClicked = false;
        listingPlan.Clear();
        listingIndex = 0;
        pendingListing = null;
        pendingInventoryCount = 0;
    }

    private void ResetCloseTracking()
    {
        closeAttemptCount = 0;
        lastRetainerCloseAttemptAt = DateTime.MinValue;
        lastCloseSignatureLoggedAt = DateTime.MinValue;
        closeVisibleAddonSignature = string.Empty;
        retainerListCloseSecondPending = false;
        retainerListCloseSecondReadyAt = DateTime.MinValue;
    }

    private static bool IsPlausibleRetainerName(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length is < 2 or > 24)
            return false;

        if (text.Contains(' ') || text.Any(char.IsDigit))
            return false;

        return text.All(c => char.IsLetter(c) || c == '\'' || c == '-');
    }

    private static string NormalizeRetainerName(string name)
        => new(name.Where(c => char.IsLetterOrDigit(c) || c == '\'' || c == '-').Select(char.ToLowerInvariant).ToArray());

    private static string NormalizeText(string text)
        => CleanAddonText(text).ToLowerInvariant();

    private static string CleanAddonText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var cleaned = text.Replace('\u0002', ' ').Replace('\u0003', ' ').Trim();
        if (cleaned.Contains('\u0001'))
            cleaned = string.Join("", cleaned.Split('\u0001', StringSplitOptions.RemoveEmptyEntries));

        return cleaned.Trim();
    }

    private static bool IsLoading()
        => Plugin.Condition[ConditionFlag.BetweenAreas] ||
           Plugin.Condition[ConditionFlag.BetweenAreas51];

    private static string GetAddonText(uint rowId)
    {
        try
        {
            var sheet = Plugin.DataManager.GetExcelSheet<Addon>();
            return sheet.TryGetRow(rowId, out var row)
                ? CleanAddonText(row.Text.ToString())
                : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private enum ContextSelectResult
    {
        Selected,
        NotFound,
        Disabled,
    }
}
