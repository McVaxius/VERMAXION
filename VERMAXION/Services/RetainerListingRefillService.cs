using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Memory;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.Game;
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
        PreparingTargets,
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

    private sealed record RetainerTarget(string Name, ulong RetainerId, int RetainerIndex, int DisplayOrder, int MarketItemCount);
    private sealed record RetainerListEntry(int Index, string Name);
    private sealed record ListingSlot(int Slot, uint ItemId, int Quantity, bool IsHq, string ItemName);
    private sealed record RetainerSellListRow(int RowIndex, ListingSlot Listing, string Text);

    private const string RetainerListAddonName = "RetainerList";
    private const string RetainerSellListAddonName = "RetainerSellList";
    private const string SelectStringAddonName = "SelectString";
    private const string ContextMenuAddonName = "ContextMenu";
    private const float MaxBellSearchDistance = 200f;
    private const float BellInteractionDistance = 2f;
    private static readonly TimeSpan DefaultStepTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan BellMoveTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan WithdrawalTimeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan CloseRetryInterval = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan RetainerListCloseSecondCallbackDelay = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan CloseSignatureLogInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan StallDiagnosticLogInterval = TimeSpan.FromSeconds(5);
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
    private DateTime lastSelectStringDiagnosticAt = DateTime.MinValue;
    private DateTime lastRetainerListDiagnosticAt = DateTime.MinValue;
    private DateTime lastRetainerMarketDiagnosticAt = DateTime.MinValue;
    private DateTime lastRetainerSellListDiagnosticAt = DateTime.MinValue;
    private DateTime lastContextMenuDiagnosticAt = DateTime.MinValue;
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
    private string lastRetainerMarketScanDetail = string.Empty;

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
        SetState(RefillState.PreparingTargets, "Reading retainer listings...");
        TickPreparingTargets();
    }

    private void TickPreparingTargets()
    {
        if (!TryBuildRetainerTargets(out targets, out var error))
        {
            StatusText = error;
            nextActionAt = DateTime.UtcNow.AddSeconds(1);
            return;
        }

        if (targets.Count == 0)
        {
            log.Information("[Listings] No retainers have market listings.");
            SetState(RefillState.Complete, "No retainer listings found.");
            return;
        }

        log.Information($"[Listings] Starting refill from listings. targets={targets.Count}, mode={selectionMode}");
        if (GameHelpers.IsAddonVisible(RetainerListAddonName))
        {
            SetState(RefillState.SelectingRetainer, "Selecting retainer...");
            return;
        }

        if (!TryFindNearestBell(out _, out var bellDistance))
        {
            LogBellSearchDiagnostics("start");
            Fail($"No Summoning Bell within {MaxBellSearchDistance:F0}y. Refill from listings must start near a bell.", closeRetainerUi: false);
            return;
        }

        if (bellDistance > BellInteractionDistance)
            SetState(RefillState.MovingToBell, $"Moving to retainer bell... ({bellDistance:F1}y)");
        else
            SetState(RefillState.InteractingBell, "Opening retainer bell...");
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
            Fail(GetTimeoutMessage());
            return;
        }

        switch (state)
        {
            case RefillState.PreparingTargets:
                TickPreparingTargets();
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

    private void TickMovingToBell()
    {
        var player = Svc.Objects.LocalPlayer;
        if (player == null)
        {
            StatusText = "Waiting for player before moving to retainer bell...";
            nextActionAt = DateTime.UtcNow.AddSeconds(1);
            return;
        }

        if (!TryFindNearestBell(out var bell, out var distance))
        {
            LogBellSearchDiagnostics("move");
            Fail($"No Summoning Bell within {MaxBellSearchDistance:F0}y. Refill from listings must start near a bell.", closeRetainerUi: false);
            return;
        }

        if (distance > BellInteractionDistance)
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
            if (!TryFindNearestBell(out var bell, out var distance))
            {
                LogBellSearchDiagnostics("interaction");
                Fail($"No Summoning Bell within {MaxBellSearchDistance:F0}y before interaction.", closeRetainerUi: false);
                return;
            }

            if (distance > BellInteractionDistance)
            {
                SetState(RefillState.MovingToBell, $"Moving to retainer bell... ({distance:F1}y)");
                return;
            }

            vnavmesh.Stop();
            Svc.Targets.Target = bell;
            if (GameHelpers.InteractWithObject(bell))
            {
                bellInteracted = true;
                nextActionAt = DateTime.UtcNow.AddSeconds(2);
            }
            else
            {
                StatusText = "Waiting to interact with retainer bell...";
                nextActionAt = DateTime.UtcNow.AddMilliseconds(500);
            }
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
            if (!IsExpectedActiveRetainer(target, out var detail))
            {
                LogSelectStringDiagnostics($"selected-retainer-wait: {detail}");
                StatusText = detail;
                nextActionAt = DateTime.UtcNow.AddMilliseconds(500);
                return;
            }

            SetState(RefillState.OpeningSellList, "Opening retainer market listings...");
            return;
        }

        if (GameHelpers.IsAddonVisible(RetainerListAddonName) && !retainerSelected)
        {
            if (!TryFindRetainerListIndex(target.Name, out var index, out var visibleNames))
            {
                LogRetainerListDiagnostics($"target-missing: {target.Name}");
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

        LogRetainerListDiagnostics($"waiting-select: {target.Name}");
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
                LogSelectStringDiagnostics("sell-menu-missing");
                StatusText = $"Waiting for retainer sell-items menu entry. Visible: {FormatSelectStringEntries()}";
                nextActionAt = DateTime.UtcNow.AddMilliseconds(500);
                return;
            }

            GameHelpers.FireAddonCallback(SelectStringAddonName, true, index);
            sellMenuSelected = true;
            nextActionAt = DateTime.UtcNow.AddSeconds(2);
            return;
        }

        LogSelectStringDiagnostics("waiting-retainer-menu");
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
            lastRetainerMarketScanDetail = detail;
            LogRetainerMarketDiagnostics(detail);
            StatusText = detail;
            nextActionAt = DateTime.UtcNow.AddMilliseconds(500);
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
                LogContextMenuDiagnostics(detail);
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
            if (!TryFindNearestBell(out _, out var bellDistance))
            {
                LogBellSearchDiagnostics("next-retainer");
                Fail($"No Summoning Bell within {MaxBellSearchDistance:F0}y before opening the next retainer.", closeRetainerUi: false);
                return;
            }

            if (bellDistance > BellInteractionDistance)
                SetState(RefillState.MovingToBell, $"Moving to next retainer: {CurrentTarget?.Name}... ({bellDistance:F1}y)");
            else
                SetState(RefillState.InteractingBell, $"Opening next retainer: {CurrentTarget?.Name}...");
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
            if (!addon->IsVisible)
            {
                detail = "RetainerSellList addon is not visible.";
                return false;
            }

            if (!TryFindRetainerSellListRow(listing, out var row, out detail))
            {
                LogRetainerSellListDiagnostics(detail);
                return false;
            }

            var current = GetListingSlotSnapshot(listing.Slot);
            if (current == null ||
                current.ItemId != listing.ItemId ||
                current.Quantity != listing.Quantity ||
                current.IsHq != listing.IsHq)
            {
                detail = $"Listing changed before row click. Expected RetainerMarket[{listing.Slot}] {FormatListing(listing)}, got {FormatListing(current)}.";
                LogRetainerSellListDiagnostics(detail);
                return false;
            }

            pendingInventoryCount = GetPlayerInventoryCount(listing.ItemId, listing.IsHq);
            GameHelpers.FireAddonCallback(RetainerSellListAddonName, true, 0, row.RowIndex, 1);
            detail = $"[Listings] Opened native {RetainerSellListAddonName} row {row.RowIndex} context for RetainerMarket[{listing.Slot}] {FormatListing(listing)}. Row text: {row.Text}";
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

    private bool TryFindRetainerSellListRow(ListingSlot listing, out RetainerSellListRow row, out string detail)
    {
        row = null!;

        if (!TryReadRetainerSellListRows(out var rows, out detail))
            return false;

        var match = rows.FirstOrDefault(candidate =>
            candidate.Listing.Slot == listing.Slot &&
            candidate.Listing.ItemId == listing.ItemId &&
            candidate.Listing.Quantity == listing.Quantity &&
            candidate.Listing.IsHq == listing.IsHq);

        if (match == null)
        {
            detail = $"Could not prove {RetainerSellListAddonName} row for RetainerMarket[{listing.Slot}] {FormatListing(listing)}. Rows: {FormatRetainerSellListRows(rows)}";
            return false;
        }

        row = match;
        detail = $"Matched {RetainerSellListAddonName} row {row.RowIndex} for RetainerMarket[{listing.Slot}] {FormatListing(listing)}.";
        return true;
    }

    private bool TryReadRetainerSellListRows(out List<RetainerSellListRow> rows, out string detail)
    {
        rows = new List<RetainerSellListRow>();
        detail = string.Empty;

        if (!TryScanRetainerMarketListings(out var slots, out var scanDetail))
        {
            detail = scanDetail;
            return false;
        }

        var rowTexts = TryReadRetainerSellListRowTexts(out var texts)
            ? texts
            : new List<string>();

        for (var i = 0; i < slots.Count; i++)
        {
            var text = i < rowTexts.Count && !string.IsNullOrWhiteSpace(rowTexts[i])
                ? rowTexts[i]
                : "(row text unavailable)";
            rows.Add(new RetainerSellListRow(i, slots[i], text));
        }

        var uiCount = rowTexts.Count == 0 ? "unreadable" : rowTexts.Count.ToString();
        detail = $"{scanDetail} {RetainerSellListAddonName} visibleRows={uiCount}. Rows: {FormatRetainerSellListRows(rows)}";
        return true;
    }

    private unsafe bool TryReadRetainerSellListRowTexts(out List<string> rows)
    {
        rows = new List<string>();

        nint addonPtr = Plugin.GameGui.GetAddonByName(RetainerSellListAddonName, 1);
        if (addonPtr == 0)
            return false;

        var addon = (AtkUnitBase*)addonPtr;
        if (!addon->IsVisible)
            return false;

        var componentList = FindFirstComponentList(addon);
        if (componentList == null || componentList->ListLength <= 0)
            return false;

        var count = Math.Min((int)componentList->ListLength, 20);
        for (var i = 0; i < count; i++)
        {
            var renderer = componentList->ItemRendererList[i].AtkComponentListItemRenderer;
            rows.Add(ReadListRendererText(renderer));
        }

        return rows.Count > 0;
    }

    private static unsafe AtkComponentList* FindFirstComponentList(AtkUnitBase* addon)
    {
        for (var i = 0; i < addon->UldManager.NodeListCount; i++)
        {
            var node = addon->UldManager.NodeList[i];
            if (node == null || (int)node->Type < 1000)
                continue;

            var componentNode = node->GetAsAtkComponentNode();
            var component = componentNode == null ? null : componentNode->GetComponent();
            if (component == null)
                continue;

            var list = (AtkComponentList*)component;
            if (list->ListLength > 0 && list->ListLength <= 100)
                return list;
        }

        return null;
    }

    private static unsafe string ReadListRendererText(AtkComponentListItemRenderer* renderer)
    {
        if (renderer == null)
            return string.Empty;

        var component = (AtkComponentBase*)renderer;
        var values = new List<string>();
        for (var i = 0; i < component->UldManager.NodeListCount; i++)
        {
            var node = component->UldManager.NodeList[i];
            if (node == null || node->Type != NodeType.Text)
                continue;

            var textNode = node->GetAsAtkTextNode();
            if (textNode == null)
                continue;

            var text = CleanAddonText(textNode->NodeText.ToString());
            if (!string.IsNullOrWhiteSpace(text))
                values.Add(text);
        }

        return values.Count == 0 ? string.Empty : string.Join(" | ", values.Distinct());
    }

    private static string FormatRetainerSellListRows(IReadOnlyCollection<RetainerSellListRow> rows)
        => rows.Count == 0
            ? "none"
            : string.Join("; ", rows.Select(row => $"{row.RowIndex}:RetainerMarket[{row.Listing.Slot}] {FormatListing(row.Listing)} text='{row.Text}'"));

    private static string FormatListing(ListingSlot? listing)
        => listing == null
            ? "empty/unavailable"
            : $"{listing.ItemName} id={listing.ItemId} qty={listing.Quantity} hq={listing.IsHq}";

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
                        retainer.RetainerId,
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
                log.Information($"[Listings] Target retainer: {target.Name}, id={target.RetainerId}, market listings={target.MarketItemCount}, displayOrder={target.DisplayOrder}");

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

    private bool TryFindNearestBell(out IGameObject bell, out float distance)
    {
        var player = Svc.Objects.LocalPlayer;
        bell = null!;
        distance = float.MaxValue;
        if (player == null)
            return false;

        var currentTarget = Svc.Targets.Target;
        if (currentTarget != null && IsBellCandidate(currentTarget))
        {
            var targetDistance = Vector3.Distance(player.Position, currentTarget.Position);
            if (targetDistance <= MaxBellSearchDistance)
            {
                bell = currentTarget;
                distance = targetDistance;
                return true;
            }
        }

        var nearest = Svc.Objects
            .Where(IsBellCandidate)
            .Select(obj => new
            {
                Bell = obj,
                Distance = Vector3.Distance(player.Position, obj.Position),
            })
            .Where(candidate => candidate.Distance <= MaxBellSearchDistance)
            .OrderBy(candidate => candidate.Distance)
            .FirstOrDefault();

        if (nearest == null)
            return false;

        bell = nearest.Bell;
        distance = nearest.Distance;
        return true;
    }

    private static bool IsBellCandidate(IGameObject? obj)
        => obj is { IsTargetable: true } and not IPlayerCharacter &&
           obj.Name.TextValue.Contains("Summoning Bell", StringComparison.OrdinalIgnoreCase);

    private void LogBellSearchDiagnostics(string context)
    {
        var player = Svc.Objects.LocalPlayer;
        var playerPosition = player?.Position ?? Vector3.Zero;
        var currentTarget = Svc.Targets.Target;
        var currentTargetText = currentTarget == null
            ? "none"
            : $"{currentTarget.Name.TextValue} kind={currentTarget.ObjectKind} targetable={currentTarget.IsTargetable} distance={DistanceFromPlayer(playerPosition, currentTarget):F1}y";

        var nearbyObjects = player == null
            ? "player unavailable"
            : string.Join("; ", Svc.Objects
                .Where(obj => obj is { IsTargetable: true } and not IPlayerCharacter)
                .Where(obj => !string.IsNullOrWhiteSpace(obj.Name.TextValue))
                .Select(obj => new
                {
                    Object = obj,
                    Distance = Vector3.Distance(playerPosition, obj.Position),
                })
                .Where(entry => entry.Distance <= 25f)
                .OrderBy(entry => entry.Distance)
                .Take(10)
                .Select(entry => $"{entry.Object.Name.TextValue} kind={entry.Object.ObjectKind} distance={entry.Distance:F1}y"));

        if (string.IsNullOrWhiteSpace(nearbyObjects))
            nearbyObjects = "none";

        log.Warning(
            $"[Listings] Bell search failed ({context}). territory={Plugin.ClientState.TerritoryType}, map={Plugin.ClientState.MapId}, playerPos={FormatPosition(playerPosition)}, target={currentTargetText}, nearestTargetable={nearbyObjects}");
    }

    private static float DistanceFromPlayer(Vector3 playerPosition, IGameObject obj)
        => playerPosition == Vector3.Zero ? float.NaN : Vector3.Distance(playerPosition, obj.Position);

    private static string FormatPosition(Vector3 position)
        => $"({position.X:F2}, {position.Y:F2}, {position.Z:F2})";

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

    private static unsafe string FormatSelectStringEntries()
    {
        try
        {
            nint addonPtr = Plugin.GameGui.GetAddonByName(SelectStringAddonName, 1);
            if (addonPtr == 0)
                return "SelectString not visible";

            var addon = (AtkUnitBase*)addonPtr;
            if (!addon->IsVisible)
                return "SelectString not visible";

            var master = new AddonMaster.SelectString(addonPtr);
            var entries = new List<string>();
            for (var i = 0; i < master.EntryCount; i++)
                entries.Add($"{i}:{CleanAddonText(master.Entries[i].Text)}");

            return entries.Count == 0 ? "none parsed" : string.Join(", ", entries);
        }
        catch (Exception ex)
        {
            return $"read failed: {ex.Message}";
        }
    }

    private static string FormatContextMenuEntries()
    {
        var addonPtr = Plugin.GameGui.GetAddonByName(ContextMenuAddonName, 1);
        if (addonPtr == 0)
            return "ContextMenu not visible";

        try
        {
            var menu = new AddonMaster.ContextMenu(addonPtr);
            var entries = menu.Entries
                .Select((entry, index) => $"{index}:{CleanAddonText(entry.Text)} enabled={entry.Enabled}")
                .ToList();

            return entries.Count == 0 ? "none parsed" : string.Join(", ", entries);
        }
        catch (Exception ex)
        {
            return $"read failed: {ex.Message}";
        }
    }

    private unsafe bool IsExpectedActiveRetainer(RetainerTarget target, out string detail)
    {
        detail = string.Empty;

        var manager = RetainerManager.Instance();
        if (manager == null || !manager->IsReady)
        {
            detail = "Waiting for RetainerManager after retainer selection...";
            return false;
        }

        if (manager->LastSelectedRetainerId != target.RetainerId)
        {
            detail = $"Waiting for active retainer {target.Name} ({target.RetainerId}); LastSelectedRetainerId={manager->LastSelectedRetainerId}.";
            return false;
        }

        var active = manager->GetActiveRetainer();
        if (active == null)
        {
            detail = $"Waiting for active retainer data for {target.Name} ({target.RetainerId}).";
            return false;
        }

        var activeName = active->NameString.Trim();
        if (active->RetainerId != target.RetainerId)
        {
            detail = $"Active retainer mismatch. Expected {target.Name} ({target.RetainerId}), got {activeName} ({active->RetainerId}).";
            return false;
        }

        log.Information($"[Listings] Active retainer confirmed: {activeName} ({active->RetainerId}).");
        return true;
    }

    private void LogSelectStringDiagnostics(string context)
    {
        var now = DateTime.UtcNow;
        if (now - lastSelectStringDiagnosticAt < StallDiagnosticLogInterval)
            return;

        lastSelectStringDiagnosticAt = now;
        log.Information($"[Listings] SelectString stall ({context}). Entries: {FormatSelectStringEntries()}");
    }

    private void LogRetainerListDiagnostics(string context)
    {
        var now = DateTime.UtcNow;
        if (now - lastRetainerListDiagnosticAt < StallDiagnosticLogInterval)
            return;

        lastRetainerListDiagnosticAt = now;
        if (TryReadRetainerListNames(out var names))
        {
            var visible = names.Count == 0 ? "none parsed" : string.Join(", ", names);
            log.Information($"[Listings] RetainerList stall ({context}). Visible: {visible}");
            return;
        }

        log.Information($"[Listings] RetainerList stall ({context}). RetainerList not readable.");
    }

    private void LogRetainerMarketDiagnostics(string detail)
    {
        var now = DateTime.UtcNow;
        if (now - lastRetainerMarketDiagnosticAt < StallDiagnosticLogInterval)
            return;

        lastRetainerMarketDiagnosticAt = now;
        log.Information($"[Listings] RetainerMarket stall. {detail}");
    }

    private void LogRetainerSellListDiagnostics(string detail)
    {
        var now = DateTime.UtcNow;
        if (now - lastRetainerSellListDiagnosticAt < StallDiagnosticLogInterval)
            return;

        lastRetainerSellListDiagnosticAt = now;
        if (TryReadRetainerSellListRows(out _, out var rowDetail))
            log.Information($"[Listings] RetainerSellList stall. {detail} {rowDetail}");
        else
            log.Information($"[Listings] RetainerSellList stall. {detail}");
    }

    private void LogContextMenuDiagnostics(string detail)
    {
        var now = DateTime.UtcNow;
        if (now - lastContextMenuDiagnosticAt < StallDiagnosticLogInterval)
            return;

        lastContextMenuDiagnosticAt = now;
        log.Information($"[Listings] Context menu stall. {detail} Entries: {FormatContextMenuEntries()}");
    }

    private static bool TryReadRetainerListNames(out List<string> names)
    {
        names = new List<string>();
        try
        {
            unsafe
            {
                nint addonPtr = Plugin.GameGui.GetAddonByName(RetainerListAddonName, 1);
                if (addonPtr == 0)
                    return false;

                var addon = (AtkUnitBase*)addonPtr;
                if (!addon->IsVisible)
                    return false;

                names = ReadRetainerListEntries(addon)
                    .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
                    .Select(entry => entry.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            return true;
        }
        catch
        {
            return false;
        }
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

    private void Fail(string message, bool closeRetainerUi = true)
    {
        LastError = message;
        log.Warning($"[Listings] {message}");
        vnavmesh.Stop();
        if (!closeRetainerUi)
        {
            SetState(RefillState.Failed, message);
            return;
        }

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
            RefillState.MovingToBell => elapsed > BellMoveTimeout,
            RefillState.OpeningContextMenu or RefillState.SelectingReturnToInventory or RefillState.ConfirmingReturn or RefillState.VerifyingWithdrawal => elapsed > WithdrawalTimeout,
            RefillState.ClosingRetainerUi => elapsed > DefaultStepTimeout,
            _ => elapsed > DefaultStepTimeout,
        };
    }

    private string GetTimeoutMessage()
    {
        return state switch
        {
            RefillState.PreparingTargets => $"Timed out waiting for RetainerManager readiness. {StatusText}",
            RefillState.OpeningSellList => $"Timed out opening retainer sell-items menu. Visible SelectString entries: {FormatSelectStringEntries()}",
            RefillState.ScanningListings => $"Timed out waiting for RetainerMarket inventory. {lastRetainerMarketScanDetail}",
            RefillState.SelectingRetainer => $"Timed out selecting retainer {CurrentTarget?.Name ?? "unknown"}. Visible RetainerList: {FormatRetainerListNames()}",
            _ => $"Timed out during {state}.",
        };
    }

    private static string FormatRetainerListNames()
    {
        return TryReadRetainerListNames(out var names)
            ? names.Count == 0 ? "none parsed" : string.Join(", ", names)
            : "RetainerList not readable";
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
        lastRetainerMarketScanDetail = string.Empty;
    }

    private void ResetCloseTracking()
    {
        closeAttemptCount = 0;
        lastRetainerCloseAttemptAt = DateTime.MinValue;
        lastCloseSignatureLoggedAt = DateTime.MinValue;
        closeVisibleAddonSignature = string.Empty;
        retainerListCloseSecondPending = false;
        retainerListCloseSecondReadyAt = DateTime.MinValue;
        lastSelectStringDiagnosticAt = DateTime.MinValue;
        lastRetainerListDiagnosticAt = DateTime.MinValue;
        lastRetainerMarketDiagnosticAt = DateTime.MinValue;
        lastRetainerSellListDiagnosticAt = DateTime.MinValue;
        lastContextMenuDiagnosticAt = DateTime.MinValue;
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
