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
        OpeningWorkshopBell,
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

    private enum RetainerUiCloseMode
    {
        FullClose,
        ReturnToRetainerList,
    }

    private sealed record RetainerTarget(string Name, ulong RetainerId, int RetainerIndex, int DisplayOrder, int MarketItemCount);
    private sealed record RetainerListEntry(int Index, string Name);
    private sealed record ListingSlot(int Slot, uint ItemId, int Quantity, bool IsHq, string ItemName);
    private sealed record ListingSignature(uint ItemId, int Quantity, bool IsHq, string ItemName);
    private sealed record RetainerSellListRow(int RowIndex, ListingSlot Listing);

    private const string RetainerListAddonName = "RetainerList";
    private const string RetainerSellListAddonName = "RetainerSellList";
    private const string SelectStringAddonName = "SelectString";
    private const string ContextMenuAddonName = "ContextMenu";
    private const float MaxBellSearchDistance = 200f;
    private const float BellInteractionDistance = 2f;
    private static readonly TimeSpan DefaultStepTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan BellMoveTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan WithdrawalTimeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan RetainerCloseTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan CloseRetryInterval = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan CloseNoSurfaceGrace = TimeSpan.FromSeconds(2.5);
    private static readonly TimeSpan RetainerListCloseSecondCallbackDelay = TimeSpan.FromMilliseconds(350);
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
    private readonly WorkshopBellService workshopBellService;
    private readonly AutoRetainerIPC autoRetainerIPC;

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
    private DateTime closeNoSurfaceSince = DateTime.MinValue;
    private int retainerListCallbackCycles;
    private int closeAttemptCount;
    private bool closeThenFail;
    private RetainerUiCloseMode closeMode = RetainerUiCloseMode.FullClose;
    private bool bellInteracted;
    private bool retainerSelected;
    private bool sellMenuSelected;
    private bool confirmationClicked;
    private RefillFromListingsSelectionMode selectionMode = RefillFromListingsSelectionMode.All;
    private RefillFromListingsRoute route = RefillFromListingsRoute.Workshop;
    private List<RetainerTarget> targets = new();
    private List<ListingSlot> listingPlan = new();
    private Dictionary<ListingSignature, int> selectedListingCounts = new();
    private int targetIndex;
    private ListingSlot? pendingListing;
    private int pendingListingCount;
    private int pendingMarketItemCount;
    private string lastRetainerMarketScanDetail = string.Empty;
    private bool reopenSellListForCurrentPlan;
    private bool contextOpenRequested;

    public string StatusText { get; private set; } = "Idle.";
    public string LastError { get; private set; } = string.Empty;
    public bool IsActive => state != RefillState.Idle && state != RefillState.Complete && state != RefillState.Failed;
    public bool IsComplete => state == RefillState.Complete;
    public bool IsFailed => state == RefillState.Failed;

    public RetainerListingRefillService(
        IPluginLog log,
        ConfigManager configManager,
        VNavmeshIPC vnavmesh,
        WorkshopBellService workshopBellService,
        AutoRetainerIPC autoRetainerIPC)
    {
        this.log = log;
        this.configManager = configManager;
        this.vnavmesh = vnavmesh;
        this.workshopBellService = workshopBellService;
        this.autoRetainerIPC = autoRetainerIPC;
    }

    public void Start(CharacterConfig config)
    {
        if (IsActive)
            return;

        Reset();
        selectionMode = config.RefillFromListingsSelectionMode;
        route = config.RefillFromListingsRoute;
        SetState(RefillState.PreparingTargets, "Reading retainer listings...");
        TickPreparingTargets();
    }

    private void TickPreparingTargets()
    {
        if (!GameHelpers.IsAddonVisible(RetainerListAddonName))
        {
            log.Information($"[Listings] Opening retainer bell: route={route}, mode=Lifestream-first, lifestreamSkipped=False, state={state}, territory={Plugin.ClientState.TerritoryType}, map={Plugin.ClientState.MapId}, suppressionOwnedByVermaxion={autoRetainerIPC.SuppressionOwnedByVermaxion}, currentSuppressed={autoRetainerIPC.GetSuppressed()}");
            workshopBellService.Start(route);
            SetState(RefillState.OpeningWorkshopBell, $"Routing to {GetRouteLabel(route)} bell...");
            return;
        }

        if (!TryBuildRetainerTargets(out targets, out var error))
        {
            StatusText = error;
            nextActionAt = DateTime.UtcNow.AddSeconds(1);
            return;
        }

        if (targets.Count == 0)
        {
            log.Information("[Listings] No retainers have market listings after bell open.");
            BeginClosingRetainerUi(false, "No retainer listings found. Closing retainer UI...");
            return;
        }

        log.Information($"[Listings] Starting refill from listings. targets={targets.Count}, mode={selectionMode}");
        SetState(RefillState.SelectingRetainer, "Selecting retainer...");
    }

    public void RunTask()
    {
        log.Information("[VERMAXION] Manual retainer listing refill triggered");
        Start(configManager.GetActiveConfig());
    }

    public void Reset()
    {
        vnavmesh.Stop();
        workshopBellService.Reset();
        state = RefillState.Idle;
        stateEnteredAt = DateTime.MinValue;
        nextActionAt = DateTime.MinValue;
        lastNavigationCommandAt = DateTime.MinValue;
        LastError = string.Empty;
        StatusText = "Idle.";
        targets = new List<RetainerTarget>();
        listingPlan = new List<ListingSlot>();
        selectedListingCounts = new Dictionary<ListingSignature, int>();
        targetIndex = 0;
        pendingListing = null;
        pendingListingCount = 0;
        pendingMarketItemCount = 0;
        reopenSellListForCurrentPlan = false;
        contextOpenRequested = false;
        closeThenFail = false;
        closeMode = RetainerUiCloseMode.FullClose;
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
            case RefillState.OpeningWorkshopBell:
                TickOpeningWorkshopBell();
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
            closeMode = RetainerUiCloseMode.FullClose;
            SetState(RefillState.ClosingRetainerUi, "Closing retainer UI...");
        }

        TickClosingRetainerUi();
    }

    private void TickOpeningWorkshopBell()
    {
        workshopBellService.Update();

        if (workshopBellService.IsComplete)
        {
            SetState(RefillState.PreparingTargets, "Reading retainer listings after bell open...");
            return;
        }

        if (workshopBellService.IsFailed)
        {
            Fail($"Retainer bell route failed: {workshopBellService.LastError}", closeRetainerUi: false);
            return;
        }

        StatusText = workshopBellService.StatusText;
        nextActionAt = DateTime.UtcNow.AddMilliseconds(250);
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
                StatusText = $"Waiting for RetainerList to show {target.Name}... Visible: {visible}.";
                nextActionAt = DateTime.UtcNow.AddMilliseconds(500);
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
            if (reopenSellListForCurrentPlan)
            {
                reopenSellListForCurrentPlan = false;
                SetState(RefillState.WithdrawingNextListing, "Continuing selected listings...");
            }
            else
            {
                SetState(RefillState.ScanningListings, "Scanning retainer market listings...");
            }
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
        selectedListingCounts = BuildSelectedListingCounts(listingPlan);
        log.Information($"[Listings] {CurrentTarget?.Name}: scanned {slots.Count} listing(s), selected {listingPlan.Count}, mode={selectionMode}, order={FormatListingSlotOrder(listingPlan)}.");

        if (listingPlan.Count == 0)
        {
            var status = selectionMode == RefillFromListingsSelectionMode.Random && slots.Count > 0
                ? $"No selected listings for {CurrentTarget?.Name ?? "retainer"}. Closing retainer UI..."
                : $"No listings remain for {CurrentTarget?.Name ?? "retainer"}. Closing retainer UI...";
            BeginClosingRetainerUi(false, status);
            return;
        }

        SetState(RefillState.WithdrawingNextListing, "Withdrawing selected listings...");
    }

    private void TickWithdrawingNextListing()
    {
        if (!GameHelpers.IsAddonVisible(RetainerSellListAddonName))
        {
            StatusText = "Waiting for RetainerSellList before choosing listing...";
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

        var listing = selectionMode == RefillFromListingsSelectionMode.All
            ? OrderListings(slots).FirstOrDefault()
            : FindNextSelectedLiveListing(slots);

        if (listing != null)
        {
            pendingListing = listing;
            SetState(RefillState.OpeningContextMenu, $"Opening context menu for {listing.ItemName}...");
            return;
        }

        BeginClosingRetainerUi(false, $"Finished {CurrentTarget?.Name ?? "retainer"}. Closing retainer UI...");
    }

    private void TickOpeningContextMenu()
    {
        if (GameHelpers.IsAddonVisible(ContextMenuAddonName))
        {
            SetState(RefillState.SelectingReturnToInventory, "Selecting Return to Inventory...");
            return;
        }

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

        if (contextOpenRequested)
        {
            Fail(BuildContextMenuOpenFailure(pendingListing));
            return;
        }

        if (!TryOpenContextMenu(pendingListing, out var detail))
        {
            Fail(detail);
            return;
        }

        log.Information(detail);
        contextOpenRequested = true;
        nextActionAt = DateTime.UtcNow.AddMilliseconds(750);
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
                Fail(detail);
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
            MarkSelectedListingWithdrawn(pendingListing);
            pendingListing = null;
            if (GameHelpers.IsAddonVisible(RetainerSellListAddonName))
            {
                SetState(RefillState.WithdrawingNextListing, "Selecting next listing...");
            }
            else
            {
                reopenSellListForCurrentPlan = true;
                SetState(RefillState.OpeningSellList, "Reopening retainer market listings...");
            }
            nextActionAt = DateTime.UtcNow.AddMilliseconds(500);
            return;
        }

        StatusText = detail;
        nextActionAt = DateTime.UtcNow.AddMilliseconds(500);
    }

    private void TickClosingRetainerUi()
    {
        if (!TryCloseVisibleRetainerUi(closeMode, out var status))
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
            if (GameHelpers.IsAddonVisible(RetainerListAddonName))
            {
                log.Information($"[Listings] Returning to RetainerList for next target; no Lifestream route. targetIndex={targetIndex}, targets={targets.Count}, suppressionOwnedByVermaxion={autoRetainerIPC.SuppressionOwnedByVermaxion}, currentSuppressed={autoRetainerIPC.GetSuppressed()}");
                SetState(RefillState.SelectingRetainer, "Selecting next retainer...");
                return;
            }

            log.Information($"[Listings] RetainerList vanished before next target; reopening nearby bell locally without Lifestream route. targetIndex={targetIndex}, targets={targets.Count}, route={route}, suppressionOwnedByVermaxion={autoRetainerIPC.SuppressionOwnedByVermaxion}, currentSuppressed={autoRetainerIPC.GetSuppressed()}");
            SetState(RefillState.MovingToBell, "Reopening retainer bell locally...");
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

            pendingListingCount = GetRetainerMarketListingCount();
            pendingMarketItemCount = GetActiveRetainerMarketItemCount();
            if (!GameHelpers.TryFireAddonCallback(RetainerSellListAddonName, true, 0, row.RowIndex, 1))
            {
                detail = $"Failed to fire {RetainerSellListAddonName} callback true 0 {row.RowIndex} 1 for RetainerMarket[{listing.Slot}] {FormatListing(listing)}. listingCount={pendingListingCount}, marketItemCount={pendingMarketItemCount}. {FormatVisibleAddonDiagnostics()}";
                return false;
            }

            detail = $"callback={RetainerSellListAddonName} true 0 {row.RowIndex} 1, row={row.RowIndex}, slot={listing.Slot}, item={listing.ItemId}, qty={listing.Quantity}, hq={listing.IsHq}, listingCount={pendingListingCount}, marketItemCount={pendingMarketItemCount}";
            return true;
        }
    }

    private string BuildContextMenuOpenFailure(ListingSlot listing)
    {
        var rowDetail = TryFindRetainerSellListRow(listing, out var row, out var detail)
            ? $"row={row.RowIndex}, proof='{detail}'"
            : $"row=unproved, proof='{detail}'";

        return $"ContextMenu did not open for RetainerMarket[{listing.Slot}] {FormatListing(listing)} after callback true 0 row 1. {rowDetail}. {FormatVisibleAddonDiagnostics()}";
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
            var entries = menu.Entries.ToList();
            var visibleEntries = FormatContextMenuEntries(entries, includeEnabled: false);
            var visibleEntriesWithState = FormatContextMenuEntries(entries, includeEnabled: true);
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var text = CleanAddonText(entry.Text);
                if (!MatchesAny(text, expected))
                    continue;

                if (!entry.Enabled)
                {
                    detail = $"Return to Inventory context entry {i}:'{text}' is disabled for {FormatPendingListing()}. Inventory may be full. Entries: {visibleEntriesWithState}";
                    return ContextSelectResult.Disabled;
                }

                if (!entry.Select())
                {
                    detail = $"Return to Inventory context entry {i}:'{text}' was found but could not be selected for {FormatPendingListing()}. Entries: {visibleEntriesWithState}";
                    return ContextSelectResult.Disabled;
                }

                detail = $"[Listings] Selected context entry {i}:'{text}' for {FormatPendingListing()}. Entries: {visibleEntries}";
                return ContextSelectResult.Selected;
            }

            detail = $"Return to Inventory context entry not found for {FormatPendingListing()}. Entries: {visibleEntriesWithState}";
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

        if (current.ItemId != listing.ItemId ||
            current.Quantity != listing.Quantity ||
            current.IsHq != listing.IsHq)
        {
            detail = $"[Listings] Verified {listing.ItemName}: RetainerMarket[{listing.Slot}] changed.";
            return true;
        }

        var listingCount = GetRetainerMarketListingCount();
        if (listingCount >= 0 && pendingListingCount > 0 && listingCount < pendingListingCount)
        {
            detail = $"[Listings] Verified {listing.ItemName}: RetainerMarket listing count decreased {pendingListingCount}->{listingCount}.";
            return true;
        }

        var marketItemCount = GetActiveRetainerMarketItemCount();
        if (marketItemCount >= 0 && pendingMarketItemCount > 0 && marketItemCount < pendingMarketItemCount)
        {
            detail = $"[Listings] Verified {listing.ItemName}: active retainer MarketItemCount decreased {pendingMarketItemCount}->{marketItemCount}.";
            return true;
        }

        detail = $"Waiting for {listing.ItemName} return confirmation...";
        return false;
    }

    private static IEnumerable<ListingSlot> OrderListings(IEnumerable<ListingSlot> slots)
        => slots.OrderByDescending(slot => slot.Slot);

    private static string FormatListingSlotOrder(IReadOnlyCollection<ListingSlot> slots)
        => slots.Count == 0 ? "none" : string.Join(",", slots.Select(slot => slot.Slot));

    private List<ListingSlot> BuildListingPlan(IReadOnlyList<ListingSlot> slots)
    {
        var ordered = OrderListings(slots).ToList();
        if (selectionMode == RefillFromListingsSelectionMode.All)
            return ordered;

        var selected = new List<ListingSlot>();
        foreach (var listing in ordered)
        {
            var roll = Random.Shared.Next(2);
            log.Information($"[Listings] Random roll for {CurrentTarget?.Name} RetainerMarket[{listing.Slot}] {FormatListing(listing)}: {roll}");
            if (roll == 1)
                selected.Add(listing);
        }

        return selected;
    }

    private static Dictionary<ListingSignature, int> BuildSelectedListingCounts(IEnumerable<ListingSlot> listings)
    {
        var counts = new Dictionary<ListingSignature, int>();
        foreach (var listing in listings)
        {
            var signature = GetListingSignature(listing);
            counts.TryGetValue(signature, out var count);
            counts[signature] = count + 1;
        }

        return counts;
    }

    private ListingSlot? FindNextSelectedLiveListing(IEnumerable<ListingSlot> slots)
    {
        foreach (var listing in OrderListings(slots))
        {
            var signature = GetListingSignature(listing);
            if (selectedListingCounts.TryGetValue(signature, out var count) && count > 0)
                return listing;
        }

        return null;
    }

    private void MarkSelectedListingWithdrawn(ListingSlot listing)
    {
        if (selectionMode == RefillFromListingsSelectionMode.All)
            return;

        var signature = GetListingSignature(listing);
        if (!selectedListingCounts.TryGetValue(signature, out var count) || count <= 0)
            return;

        if (count == 1)
            selectedListingCounts.Remove(signature);
        else
            selectedListingCounts[signature] = count - 1;
    }

    private static ListingSignature GetListingSignature(ListingSlot listing)
        => new(listing.ItemId, listing.Quantity, listing.IsHq, listing.ItemName);

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

        for (var i = 0; i < slots.Count; i++)
            rows.Add(new RetainerSellListRow(i, slots[i]));

        detail = $"{scanDetail} {RetainerSellListAddonName} inventoryRows={rows.Count}. Rows: {FormatRetainerSellListRows(rows)}";
        return true;
    }

    private static string FormatRetainerSellListRows(IReadOnlyCollection<RetainerSellListRow> rows)
        => rows.Count == 0
            ? "none"
            : string.Join("; ", rows.Select(row => $"{row.RowIndex}:RetainerMarket[{row.Listing.Slot}] {FormatListing(row.Listing)}"));

    private static string FormatListing(ListingSlot? listing)
        => listing == null
            ? "empty/unavailable"
            : $"{listing.ItemName} id={listing.ItemId} qty={listing.Quantity} hq={listing.IsHq}";

    private unsafe int GetRetainerMarketListingCount()
    {
        var manager = InventoryManager.Instance();
        if (manager == null)
            return -1;

        var container = manager->GetInventoryContainer(InventoryType.RetainerMarket);
        if (container == null || !container->IsLoaded)
            return -1;

        var count = 0;
        for (var i = 0; i < container->Size; i++)
        {
            var item = container->GetInventorySlot(i);
            if (item != null && item->ItemId != 0 && item->Quantity > 0)
                count++;
        }

        return count;
    }

    private unsafe int GetActiveRetainerMarketItemCount()
    {
        var target = CurrentTarget;
        if (target == null)
            return -1;

        var manager = RetainerManager.Instance();
        if (manager == null || !manager->IsReady)
            return -1;

        for (var i = 0; i < manager->Retainers.Length; i++)
        {
            var retainer = manager->Retainers[i];
            if (retainer.RetainerId == target.RetainerId)
                return retainer.MarketItemCount;
        }

        return -1;
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

            log.Information($"[Listings] Retainer target scan after bell open: targets={retainerTargets.Count}, retainers={FormatRetainerMarketItemCounts()}");
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

    private static unsafe string FormatRetainerMarketItemCounts()
    {
        try
        {
            var manager = RetainerManager.Instance();
            if (manager == null)
                return "RetainerManager null";

            if (!manager->IsReady)
                return "RetainerManager not ready";

            var entries = new List<string>();
            for (var i = 0; i < manager->Retainers.Length; i++)
            {
                var retainer = manager->Retainers[i];
                if (retainer.RetainerId == 0)
                    continue;

                var name = retainer.NameString.Trim();
                if (string.IsNullOrWhiteSpace(name))
                    name = $"#{i}";

                entries.Add($"{name}:{retainer.MarketItemCount}");
            }

            return entries.Count == 0 ? "none" : string.Join(", ", entries);
        }
        catch (Exception ex)
        {
            return $"unreadable: {ex.Message}";
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
            "Return Items to Inventory",
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
            return FormatContextMenuEntries(menu.Entries.ToList(), includeEnabled: true);
        }
        catch (Exception ex)
        {
            return $"read failed: {ex.Message}";
        }
    }

    private static string FormatVisibleAddonDiagnostics()
    {
        var addonNames = new[]
        {
            RetainerSellListAddonName,
            ContextMenuAddonName,
            "SelectYesno",
            SelectStringAddonName,
            RetainerListAddonName,
        };

        var visible = addonNames
            .Where(GameHelpers.IsAddonVisible)
            .ToList();

        return $"Visible addons: {(visible.Count == 0 ? "none" : string.Join(", ", visible))}. ContextMenu entries: {FormatContextMenuEntries()}";
    }

    private static string FormatContextMenuEntries(IReadOnlyList<AddonMaster.ContextMenu.Entry> entries, bool includeEnabled)
    {
        if (entries.Count == 0)
            return "none parsed";

        return string.Join(", ", entries.Select((entry, index) =>
        {
            var text = CleanAddonText(entry.Text);
            return includeEnabled
                ? $"{index}:{text} enabled={entry.Enabled}"
                : $"{index}:{text}";
        }));
    }

    private string FormatPendingListing()
        => pendingListing == null
            ? "listing"
            : $"RetainerMarket[{pendingListing.Slot}] {FormatListing(pendingListing)}";

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

    private bool TryCloseVisibleRetainerUi(RetainerUiCloseMode mode, out string status)
    {
        var now = DateTime.UtcNow;
        status = "Closing retainer UI...";

        if (mode == RetainerUiCloseMode.FullClose && retainerListCloseSecondPending)
        {
            if (now < retainerListCloseSecondReadyAt)
                return true;

            retainerListCloseSecondPending = false;
            if (GameHelpers.IsAddonVisible(RetainerListAddonName))
            {
                LogRetainerCloseAction(closeAttemptCount + 1, RetainerListAddonName, "RetainerList true -2", GetVisibleRetainerCloseAddons());
                GameHelpers.FireAddonCallback(RetainerListAddonName, true, -2);
                closeAttemptCount++;
                retainerListCallbackCycles++;
            }

            lastRetainerCloseAttemptAt = now;
            return true;
        }

        var visibleAddons = GetVisibleRetainerCloseAddons(mode);
        if (visibleAddons.Count == 0)
        {
            if (closeNoSurfaceSince == DateTime.MinValue)
            {
                closeNoSurfaceSince = now;
                nextActionAt = now.AddMilliseconds(150);
                return true;
            }

            if (now - closeNoSurfaceSince < CloseNoSurfaceGrace)
            {
                nextActionAt = now.AddMilliseconds(150);
                return true;
            }

            if (mode == RetainerUiCloseMode.FullClose)
                log.Information($"[Listings] Retainer UI full close confirmed after {CloseNoSurfaceGrace.TotalSeconds:F1}s with no close surfaces visible.");

            ResetCloseTracking();
            return false;
        }

        closeNoSurfaceSince = DateTime.MinValue;
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
            if (retainerListCallbackCycles >= 5)
            {
                LogRetainerCloseAction(closeAttemptCount + 1, addonToClose, "CloseCurrentAddon ESC fallback", visibleAddons);
                GameHelpers.CloseCurrentAddon();
            }
            else
            {
                LogRetainerCloseAction(closeAttemptCount + 1, addonToClose, "RetainerList true -1", visibleAddons);
                GameHelpers.FireAddonCallback(RetainerListAddonName, true, -1);
                retainerListCloseSecondPending = true;
                retainerListCloseSecondReadyAt = now.Add(RetainerListCloseSecondCallbackDelay);
            }
        }
        else
        {
            var useCallback = closeAttemptCount % 2 == 0;
            if (useCallback)
            {
                LogRetainerCloseAction(closeAttemptCount + 1, addonToClose, "TryCloseAddonByCallback", visibleAddons);
                if (!GameHelpers.TryCloseAddonByCallback(addonToClose))
                {
                    LogRetainerCloseAction(closeAttemptCount + 1, addonToClose, "CloseCurrentAddon fallback", visibleAddons);
                    GameHelpers.CloseCurrentAddon();
                }
            }
            else
            {
                LogRetainerCloseAction(closeAttemptCount + 1, addonToClose, "CloseCurrentAddon", visibleAddons);
                GameHelpers.CloseCurrentAddon();
            }
        }

        closeAttemptCount++;
        lastRetainerCloseAttemptAt = now;
        nextActionAt = now.Add(CloseRetryInterval);
        return true;
    }

    private static List<string> GetVisibleRetainerCloseAddons(RetainerUiCloseMode mode = RetainerUiCloseMode.FullClose)
    {
        var visibleAddons = new List<string>();
        foreach (var addonName in RetainerCloseAddonPriority)
        {
            if (mode == RetainerUiCloseMode.ReturnToRetainerList && addonName == RetainerListAddonName)
                continue;

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

    private void LogRetainerCloseAction(int attempt, string addonName, string action, IReadOnlyCollection<string> visibleAddons)
    {
        var visible = visibleAddons.Count == 0 ? "none" : string.Join(", ", visibleAddons);
        log.Information($"[Listings] Retainer close attempt {attempt}: action={action}, target={addonName}, context={state}, visible={visible}.");
    }

    private void BeginClosingRetainerUi(bool failed, string status)
    {
        closeThenFail = failed;
        closeMode = failed || targetIndex + 1 >= targets.Count
            ? RetainerUiCloseMode.FullClose
            : RetainerUiCloseMode.ReturnToRetainerList;
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

    private static string GetRouteLabel(RefillFromListingsRoute route)
        => route switch
        {
            RefillFromListingsRoute.Inn => "inn",
            RefillFromListingsRoute.Limsa => "limsa",
            _ => "workshop",
        };

    private bool IsTimedOut()
    {
        var elapsed = DateTime.UtcNow - stateEnteredAt;
        return state switch
        {
            RefillState.OpeningWorkshopBell => false,
            RefillState.MovingToBell => elapsed > BellMoveTimeout,
            RefillState.OpeningContextMenu or RefillState.SelectingReturnToInventory or RefillState.ConfirmingReturn or RefillState.VerifyingWithdrawal => elapsed > WithdrawalTimeout,
            RefillState.ClosingRetainerUi => elapsed > RetainerCloseTimeout,
            _ => elapsed > DefaultStepTimeout,
        };
    }

    private string GetTimeoutMessage()
    {
        return state switch
        {
            RefillState.PreparingTargets => $"Timed out waiting for RetainerManager readiness. {StatusText}",
            RefillState.OpeningWorkshopBell => $"Timed out opening retainer bell. {workshopBellService.StatusText}",
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
                contextOpenRequested = false;
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
        confirmationClicked = false;
        listingPlan.Clear();
        selectedListingCounts.Clear();
        pendingListing = null;
        pendingListingCount = 0;
        pendingMarketItemCount = 0;
        reopenSellListForCurrentPlan = false;
        contextOpenRequested = false;
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
        closeNoSurfaceSince = DateTime.MinValue;
        retainerListCallbackCycles = 0;
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
