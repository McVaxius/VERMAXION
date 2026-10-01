using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.Automation;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using VERMAXION.Models;
using VERMAXION.CustomDeliveries;

namespace VERMAXION.Services;

/// <summary>One owned stable visit and optional onion quest handoff. Roster identities are transient and never logged.</summary>
public sealed unsafe class ChocoboStablesService
{
    public const uint FeedItemId = 8165; // Krakka Root; training, not colour feeding or onions.
    public const uint BroomItemId = 8168;
    private const int PersonalBirdRow = -1;
    private enum Step { Idle, Travel, Approach, Menu, CleanPrompt, CleanResult, Scan, SelectBird, Inventory, Reward, TrainingResult, OnionQuest, OnionPurchase }
    private static readonly InventoryType[] Bags = [InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4];
    private static readonly string[] InventoryAddons = ["Inventory", "InventoryLarge", "InventoryExpansion", "InventoryGrid", "InventoryGrid3E"];
    private readonly Plugin plugin;
    private readonly Dictionary<string, uint> ownedUi = new();
    private readonly HashSet<string> preexistingInventory = new();
    private readonly List<StableBird> birds = [];
    private ChocoboStablesSettings settings = new();
    private Step step;
    private DateTime entered, nextAction;
    private ulong character, stableObject;
    private TeleportInfo destination;
    private bool scanOnly, moving, travelStarted, stableLookupLogged, rosterShapeLogged, cleanupPending;
    private DateTime cleanupQuietSince = DateTime.MinValue;
    private int page, feedBefore, broomBefore;
    private StableBird? selected;
    private ulong scannedCharacter;
    private StableDestination scannedDestination;
    private ushort onionQuestId;
    private object? onionQuestProvider;
    private bool onionQuestDispatched, onionQuestAcknowledged, onionQuestStopRequested, onionQuestStopWarningLogged, onionQuestPriorityInserted;
    private DateTime onionQuestAcknowledgementDeadline;
    private string onionOrderId = string.Empty, onionRequestId = string.Empty;
    private bool onionOrderCancelRequested;
    private DateTime onionOrderStartedUtc;
    private long onionGilBefore;
    public bool IsActive => step != Step.Idle || cleanupPending;
    public bool IsCleanupPending => cleanupPending;
    public bool IsComplete { get; private set; }
    public bool IsFailed { get; private set; }
    public bool Trained { get; private set; }
    public bool Cleaned { get; private set; }
    public string StatusText { get; private set; } = "Not scanned";
    public string Cleanliness { get; private set; } = "Unknown";
    public DateTime NextTrainingUtc { get; private set; }
    public int FeedStock => Count(FeedItemId);
    public int BroomStock => Count(BroomItemId);
    public int OnionStock
    {
        get
        {
            if (!NativeStateReady) return -1;
            var manager = InventoryManager.Instance();
            if (manager == null) return -1;
            foreach (var bag in Bags)
            {
                var container = manager->GetInventoryContainer(bag);
                if (container == null || !container->IsLoaded) return -1;
            }
            return manager->GetInventoryItemCount(ChocoboOnionQuests.ItemId);
        }
    }
    public bool IsAcquiringOnion => step is Step.OnionQuest or Step.OnionPurchase;
    public IReadOnlyList<StableBird> GetRoster(StableDestination choice) => scannedCharacter == Plugin.PlayerState.ContentId && scannedDestination == choice ? birds : [];

    public ChocoboStablesService(Plugin plugin) => this.plugin = plugin;

    private static bool CharacterDataReady => Plugin.ClientState.IsLoggedIn && Plugin.PlayerState.IsLoaded
        && Plugin.PlayerState.ContentId != 0 && Plugin.ObjectTable.LocalPlayer != null
        && !Plugin.Condition[ConditionFlag.BetweenAreas] && !Plugin.Condition[ConditionFlag.BetweenAreas51];
    private bool NativeStateReady => plugin.IsCharacterRegistered && CharacterDataReady;

    public static bool? HasFreeCompany()
    {
        if (!CharacterDataReady || Plugin.ObjectTable.LocalPlayer is not { } player) return null;
        var native = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)player.Address;
        // The FC info proxy can describe an inspected character. Read the loaded local character instead.
        if (native == null || native->ContentId == 0 || native->ContentId != Plugin.PlayerState.ContentId) return null;
        return !string.IsNullOrWhiteSpace(native->FreeCompanyTagString);
    }

    public string? GetStartBlockedReason(ChocoboStablesSettings choice, bool forScan = false)
    {
        if (cleanupPending) return "The previous stable visit or owned onion quest is still cleaning up.";
        if (!Plugin.ClientState.IsLoggedIn || Plugin.ObjectTable.LocalPlayer == null) return "Log in on the character first.";
        if (!NativeStateReady) return "Waiting for loaded character data and the area transition to finish.";
        if (Plugin.Condition[ConditionFlag.BoundByDuty]) return "Leave the duty before visiting stables.";
        var player = Plugin.ObjectTable.LocalPlayer;
        if (player.CurrentWorld.RowId != player.HomeWorld.RowId) return "Return to the character's home world before visiting its stable.";
        if (choice.Destination == StableDestination.FreeCompanyEstate)
        {
            var member = HasFreeCompany();
            if (!member.HasValue) return "FC membership data is unavailable.";
            if (!member.Value) return "This character is not in a Free Company; choose another estate.";
        }
        if (!forScan && choice.Target == StableTarget.SpecificOther && (string.IsNullOrEmpty(choice.OtherOwner) || string.IsNullOrEmpty(choice.OtherChocobo)))
            return "Scan the selected stable and select one other chocobo first.";
        return TryResolveDestination(choice.Destination, out _) ? null : "The selected estate has no available teleport entry. Check ownership, tenancy and estate access.";
    }

    private bool TryResolveDestination(StableDestination choice, out TeleportInfo result)
    {
        result = default;
        if (!NativeStateReady) return false;
        var telepo = Telepo.Instance();
        if (telepo == null) return false;
        if (telepo->UpdateAetheryteList() == null) return false;
        var sharedIndex = 0;
        foreach (var entry in telepo->TeleportList)
        {
            if (entry.AetheryteId == 0 || entry.HouseId.Id == 0 || entry.HouseId.Id == ulong.MaxValue) continue;
            var matches = choice switch
            {
                StableDestination.SharedEstate1 or StableDestination.SharedEstate2 or StableDestination.SharedEstate3
                    => entry.IsSharedHouse && sharedIndex++ == (int)choice,
                StableDestination.PersonalEstate => entry.EstateType == EstateType.PersonalEstate && !entry.IsSharedHouse && !entry.IsApartment,
                StableDestination.Apartment => entry.IsApartment,
                StableDestination.FreeCompanyEstate => entry.EstateType == EstateType.FreeCompanyEstate,
                _ => false,
            };
            if (matches) { result = entry; return true; }
        }
        return false;
    }

    public void Start(ChocoboStablesSettings choice, bool onlyScan = false)
    {
        Reset();
        if (GetStartBlockedReason(choice, onlyScan) is { } reason) { Fail(reason); return; }
        // Do not take ownership of a menu/inventory the user already opened.
        if (Visible("SelectString") != null || Visible("HousingChocoboList") != null || Visible("ContextMenu") != null || Visible("SelectYesno") != null)
        { Fail("Close the current interaction menus before starting stables."); return; }
        settings = choice.Clone();
        character = Plugin.PlayerState.ContentId;
        preexistingInventory.Clear();
        foreach (var name in InventoryAddons) if (Visible(name) != null) preexistingInventory.Add(name);
        scanOnly = onlyScan;
        scannedCharacter = 0;
        birds.Clear();
        Cleanliness = "Unknown";
        if (!TryResolveDestination(settings.Destination, out destination)) { Fail("Selected estate is no longer available."); return; }
        if (!GameHelpers.IsPlayerAvailable()) { Fail("Character must be ready and out of combat before stable travel."); return; }
        if (AtDestination()) { Transition(Step.Approach, "Finding the selected estate's stable"); return; }
        var telepo = Telepo.Instance();
        if (telepo == null || !telepo->Teleport(destination.AetheryteId, destination.SubIndex)) { Fail("Native estate teleport was rejected."); return; }
        Transition(Step.Travel, "Travelling to the selected estate");
    }

    private bool AtDestination() => AtDestination(destination);

    private bool InDestinationWard()
    {
        if (!NativeStateReady || Plugin.ObjectTable.LocalPlayer is not { } player) return false;
        var housing = HousingManager.Instance();
        return housing != null && housing->CurrentTerritory != null && housing->OutdoorTerritory != null && housing->IsOutside()
            && player.CurrentWorld.RowId == destination.HouseId.WorldId
            && Plugin.ClientState.TerritoryType == destination.HouseId.TerritoryTypeId
            && housing->GetCurrentWard() == destination.HouseId.WardIndex;
    }

    private bool AtDestination(TeleportInfo destination)
    {
        if (!NativeStateReady || destination.HouseId.Id == 0 || destination.HouseId.Id == ulong.MaxValue) return false;
        var housing = HousingManager.Instance();
        if (housing == null || housing->CurrentTerritory == null || housing->OutdoorTerritory == null || !housing->IsOutside()) return false;
        var current = housing->GetCurrentHouseId();
        var expected = destination.HouseId;
        return current.WorldId == expected.WorldId && current.TerritoryTypeId == expected.TerritoryTypeId
            && current.WardIndex == expected.WardIndex && current.Unit.Value == expected.Unit.Value;
    }

    public void Update()
    {
        if (cleanupPending) { TickCleanup(); return; }
        if (!IsActive) return;
        if (!Plugin.ClientState.IsLoggedIn || Plugin.PlayerState.ContentId != character)
        { Fail("Stable visit interrupted by logout or character change."); return; }
        if (!NativeStateReady) return;
        if (step is Step.OnionQuest or Step.OnionPurchase)
        {
            if (DateTime.UtcNow < nextAction) return;
            nextAction = DateTime.UtcNow.AddMilliseconds(650);
            try
            {
                if (step == Step.OnionPurchase) UpdateOnionPurchase();
                else UpdateOnionQuest();
            }
            catch (Exception ex) { Plugin.Log.Error(ex, "[Stables] Onion acquisition failed"); Fail("Onion acquisition failed; see the plugin log."); }
            return;
        }
        if (Plugin.Condition[ConditionFlag.InCombat] || Plugin.Condition[ConditionFlag.BoundByDuty])
        { Fail("Stable visit interrupted by combat or a duty."); return; }
        if (step is not (Step.Travel or Step.Approach) && !AtDestination())
        { Fail("Character left the selected estate during its stable visit."); return; }
        var now = DateTime.UtcNow;
        if (now - entered > TimeSpan.FromSeconds(step == Step.Travel ? 120 : step == Step.Approach ? 90 : 30))
        {
            if (step == Step.Scan)
            {
                var roster = Own("HousingChocoboList");
                if (roster != null)
                {
                    TryReadPage(roster, out _, out _, out _, out var detail);
                    Plugin.Log.Warning($"[Stables] Roster read incomplete: {detail}");
                }
            }
            Fail($"Stables stopped while {step}: expected native progress was not observed."); return;
        }
        if (now < nextAction) return;
        nextAction = now.AddMilliseconds(650);
        try
        {
            switch (step)
            {
                case Step.Travel:
                    travelStarted |= !GameHelpers.IsPlayerAvailable();
                    if (GameHelpers.IsPlayerAvailable() && InDestinationWard()) Transition(Step.Approach, "Finding the selected estate's stable");
                    else if (travelStarted && GameHelpers.IsPlayerAvailable() && now - entered > TimeSpan.FromSeconds(15))
                    {
                        var housing = HousingManager.Instance();
                        var outdoor = housing != null && housing->CurrentTerritory != null && housing->OutdoorTerritory != null && housing->IsOutside();
                        Plugin.Log.Information($"[Stables] Estate arrival check: clientTerritory={Plugin.ClientState.TerritoryType}; expectedTerritory={destination.HouseId.TerritoryTypeId}; outdoors={outdoor}; worldMatches={Plugin.ObjectTable.LocalPlayer?.CurrentWorld.RowId == destination.HouseId.WorldId}; wardMatches={outdoor && housing->GetCurrentWard() == destination.HouseId.WardIndex}");
                        Fail("Travel settled outside the selected estate. Check the estate teleport and access.");
                    }
                    break;
                case Step.Approach: Approach(); break;
                case Step.Menu: HandleMenu(); break;
                case Step.CleanPrompt: ConfirmCleaning(); break;
                case Step.CleanResult:
                    if (Count(BroomItemId) == broomBefore - 1)
                    {
                        Cleaned = true;
                        AdvanceTalk();
                        if (StableMenu() != null) HandleMenu(); else InteractStable();
                    }
                    break;
                case Step.Scan: ScanPage(); break;
                case Step.SelectBird: SelectBird(); break;
                case Step.Inventory: OpenReward(); break;
                case Step.Reward: Reward(); break;
                case Step.TrainingResult: VerifyTraining(); break;
            }
        }
        catch (Exception ex) { Plugin.Log.Error(ex, "[Stables] Native stable interaction failed"); Fail("Native stable interaction failed; see the plugin log."); }
    }

    private IGameObject? StableObject() => Plugin.ObjectTable.FirstOrDefault(obj => obj.GameObjectId == stableObject);
    private void Approach()
    {
        if (Plugin.ObjectTable.LocalPlayer is not { } player || !GameHelpers.IsPlayerAvailable()) return;
        if (!InDestinationWard()) { Fail("Character is outside the selected estate's ward."); return; }
        // Only the selected yard is eligible. AtDestination is checked again after movement.
        var named = Plugin.ObjectTable.Where(o => o.Name.TextValue.Equals("Chocobo Stable", StringComparison.OrdinalIgnoreCase)).ToList();
        var obj = StableObject() ?? named.Where(o => o.ObjectKind is ObjectKind.HousingEventObject or ObjectKind.EventObj
            && Vector3.Distance(o.Position, player.Position) < 35)
            .OrderBy(o => Vector3.Distance(o.Position, player.Position)).FirstOrDefault();
        if (!stableLookupLogged)
        {
            stableLookupLogged = true;
            var candidates = string.Join("; ", named.Take(5).Select(o => $"kind={o.ObjectKind}; baseId={o.BaseId}; distance={Vector3.Distance(o.Position, player.Position):F1}; targetable={o.IsTargetable}"));
            Plugin.Log.Information($"[Stables] Stable lookup: loaded={named.Count}; selected={obj != null}; {candidates}");
        }
        if (obj == null) { StatusText = "Waiting for a Chocobo Stable on the selected estate; check placement and access"; return; }
        stableObject = obj.GameObjectId;
        var distance = Vector3.Distance(player.Position, obj.Position);
        if (distance > GameHelpers.GetValidInteractionDistance(obj) || !AtDestination())
        {
            // Estate teleport landings can be outside the plot boundary. Enter the yard
            // before interacting; the exact selected plot remains mandatory for every callback.
            if (plugin.VNavmeshIPC.TryFindReachablePointNear(obj.Position, AtDestination() ? 3 : 1, out var approach))
            { moving = true; plugin.VNavmeshIPC.PathfindAndMoveTo(approach); }
            StatusText = $"Approaching stable ({distance:F1} yalms)";
            return;
        }
        StopMovement();
        InteractStable();
        Transition(Step.Menu, "Opening stable menu");
    }

    private void InteractStable()
    {
        if (!AtDestination()) { Fail("Selected estate changed during the visit."); return; }
        if (StableObject() is { } obj && GameHelpers.IsPlayerAvailable()) GameHelpers.InteractWithObject(obj);
    }

    private AtkUnitBase* Visible(string name)
    {
        if (!NativeStateReady) return null;
        var addon = (AtkUnitBase*)Plugin.GameGui.GetAddonByName(name).Address;
        return addon != null && addon->IsVisible && addon->IsFullyLoaded() ? addon : null;
    }

    private AtkUnitBase* Own(string name)
    {
        var addon = Visible(name);
        if (addon != null) ownedUi[name] = addon->Id;
        return addon;
    }

    private AtkUnitBase* StableMenu()
    {
        var addon = VisibleStableMenu();
        if (addon == null) return null;
        var title = Text(addon->GetTextNodeById(2));
        ownedUi["SelectString"] = addon->Id;
        Cleanliness = title.Contains("Stable Cleanliness: Good", StringComparison.OrdinalIgnoreCase) ? "Good"
            : title.Contains("Stable Cleanliness: Fair", StringComparison.OrdinalIgnoreCase) ? "Fair"
            : title.Contains("Stable Cleanliness: Poor", StringComparison.OrdinalIgnoreCase) ? "Poor" : "Unknown";
        return addon;
    }

    private AtkUnitBase* VisibleStableMenu()
    {
        var addon = Visible("SelectString");
        return addon != null && Text(addon->GetTextNodeById(2)).Contains("Stable Cleanliness", StringComparison.OrdinalIgnoreCase)
            ? addon : null;
    }

    public void CleanupReloadMenu(ChocoboStablesSettings choice)
    {
        // Called after ordinary world-ready character registration and FULL STOP.
        if (!NativeStateReady || (VisibleStableMenu() == null && Visible("HousingChocoboList") == null)
            || !TryResolveDestination(choice.Destination, out var target) || !AtDestination(target)) return;
        destination = target;
        character = Plugin.PlayerState.ContentId;
        // This is called only for the saved, character-correlated stables reload task.
        // A roster close can return to SelectString with a new native addon ID.
        ownedUi["SelectString"] = 0;
        StableMenu();
        Own("HousingChocoboList");
        Plugin.Log.Information("[Stables] Closing selected stable menus left open across reload");
        Cleanup();
    }

    private void HandleMenu()
    {
        var addon = StableMenu();
        if (addon == null) { AdvanceTalk(); InteractStable(); return; }
        if (!scanOnly && settings.CleanStable && !Cleaned)
        {
            broomBefore = Count(BroomItemId);
            if (broomBefore < 1)
            {
                // Settings are cloned for this visit; keep the saved cleaning preference.
                settings.CleanStable = false;
                Plugin.Log.Information($"[Stables] Optional cleaning skipped: no Magicked Stable Broom (8168); freeTrial={Plugin.Condition[ConditionFlag.OnFreeTrial]}; fcMember={HasFreeCompany()}; continuing to the roster without purchasing supplies");
            }
        }
        if (!scanOnly && settings.CleanStable && Cleanliness is "Fair" or "Poor")
        {
            if (Cleaned) { Fail("Broom was consumed but the stable still reports unclean; no training dispatched."); return; }
            if (SelectEntry(addon, "Clean Stable")) Transition(Step.CleanPrompt, "Confirming stable cleaning");
            return;
        }
        if (!scanOnly && settings.CleanStable && Cleanliness == "Unknown") { Fail("Stable cleanliness is unknown; no broom or feed used."); return; }
        if (SelectEntry(addon, "Tend to a Specified Chocobo")) Transition(Step.Scan, "Scanning stable roster");
    }

    private static bool SelectEntry(AtkUnitBase* addon, string text)
    {
        foreach (var entry in new AddonMaster.SelectString(addon).Entries)
            if (entry.Text.Equals(text, StringComparison.OrdinalIgnoreCase)) { entry.Select(); return true; }
        return false;
    }

    private void ConfirmCleaning()
    {
        var prompt = Visible("SelectYesno");
        if (prompt == null) return;
        var master = new AddonMaster.SelectYesno(prompt);
        if (!master.Text.Contains("Use a magicked stable broom", StringComparison.OrdinalIgnoreCase))
        { Fail("Unexpected cleaning confirmation; no confirmation accepted."); return; }
        Own("SelectYesno");
        master.Yes();
        Transition(Step.CleanResult, "Waiting for broom consumption and Good cleanliness");
    }

    // Verified upstream native node contract: EasyStables/Plugin.cs at
    // aancuta/ffxiv_chocobo_feeder revision 4bc13c41efeacdca888f94fc423357d42287659b.
    // HousingChocoboList page list 3, other-bird list 18; name/owner/rank/training 4/7/10/16.
    // XIVLauncher7 native inspection also verifies the personal renderer at addon node 15.
    private static bool TryReadPage(AtkUnitBase* addon, out int currentPage, out int pages, out List<StableBird> rows, out string detail)
    {
        rows = [];
        currentPage = -1; pages = 0;
        detail = "Page or bird-list component is unavailable or outside the supported bounds";
        var pageList = addon->GetComponentListById(3);
        var list = addon->GetComponentListById(18);
        if (pageList == null || list == null || pageList->ListLength <= 0 || pageList->ListLength > 100
            || list->UldManager.NodeList == null || list->UldManager.NodeListCount <= 2 || list->UldManager.NodeListCount > 100) return false;
        currentPage = pageList->SelectedItemIndex; pages = pageList->ListLength;
        detail = $"Page selection={currentPage}; pages={pages}";
        if (currentPage < 0 || currentPage >= pages) return false;
        var renderers = 0;
        var placeholders = 0;
        var nameNodes = 0;
        var ownerNodes = 0;
        // The personal bird is outside list 18 and appears on every page. Include it once.
        var personalNode = addon->GetNodeById(15);
        var personal = personalNode == null ? null : personalNode->GetAsAtkComponentListItemRenderer();
        if (currentPage == 0 && personal != null && personalNode->IsVisible())
        {
            if (!TryReadBird(personal, 0, PersonalBirdRow, out var own, out detail)) return false;
            if (own != null) rows.Add(own);
        }
        // This addon manages bird renderers directly; its generic ListLength stays zero.
        for (var i = 2; i < list->UldManager.NodeListCount; i++)
        {
            var node = list->UldManager.NodeList[i];
            var row = node == null ? null : node->GetAsAtkComponentListItemRenderer();
            if (row == null) continue;
            renderers++;
            var nameNode = row->GetTextNodeById(4);
            var ownerNode = row->GetTextNodeById(7);
            if (nameNode != null) nameNodes++;
            if (ownerNode != null) ownerNodes++;
            if (!TryReadBird(row, currentPage, i - 2, out var bird, out detail)) return false;
            if (bird == null) { placeholders++; continue; }
            rows.Add(bird);
        }
        detail = $"renderers={renderers}; placeholders={placeholders}; nameNodes={nameNodes}; ownerNodes={ownerNodes}; populatedRows={rows.Count}; nodes={list->UldManager.NodeListCount}";
        if (rows.Count > 0) return true;
        // The native empty-state message distinguishes an empty page from unreadable rows.
        var listNode = addon->GetNodeById(18);
        var emptyNode = addon->GetTextNodeById(19);
        return listNode != null && !listNode->IsVisible() && emptyNode != null && emptyNode->IsVisible()
            && Dalamud.Game.Text.SeStringHandling.SeString.Parse(emptyNode->NodeText.AsSpan()).TextValue.Equals(
                Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Addon>().GetRow(6510).Text.ExtractText(), StringComparison.Ordinal);
    }

    private static bool TryReadBird(AtkComponentListItemRenderer* row, int page, int index, out StableBird? bird, out string detail)
    {
        bird = null;
        var name = Text(row->GetTextNodeById(4));
        var owner = Text(row->GetTextNodeById(7));
        detail = $"row={index}; namePresent={!string.IsNullOrWhiteSpace(name)}; ownerPresent={!string.IsNullOrWhiteSpace(owner)}";
        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(owner)) return true;
        var rankNode = row->GetTextNodeById(10);
        var rankText = Text(rankNode);
        var training = Text(row->GetTextNodeById(16));
        var rankParsed = int.TryParse(rankText, out var rank);
        detail += $"; rankNode={rankNode != null}; rankParsed={rankParsed}; trainingPresent={!string.IsNullOrWhiteSpace(training)}";
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(owner) || rankNode == null || string.IsNullOrWhiteSpace(training)
            || !rankParsed || rank < 0 || rank > 20) return false;
        bird = new(page, index, name, owner, rank, rankNode->TextColor.RGBA != 0xFFC5E1EE, training);
        return true;
    }

    private static string Text(AtkTextNode* node) => node == null ? string.Empty : node->GetText().ToString();

    private void ScanPage()
    {
        var addon = Own("HousingChocoboList");
        if (addon != null && !rosterShapeLogged)
        {
            rosterShapeLogged = true;
            var pageList = addon->GetComponentListById(3);
            var birdList = addon->GetComponentListById(18);
            Plugin.Log.Information($"[Stables] Roster layout: pages={(pageList == null ? -1 : pageList->ListLength)}; rows={(birdList == null ? -1 : birdList->ListLength)}; nodes={(birdList == null ? -1 : birdList->UldManager.NodeListCount)}; feedStock={Count(FeedItemId)}");
            // Reopening this addon can retain a selected page with an unpopulated bird list.
            // Refresh through its native event even when page zero is already selected.
            if (ClickList(addon, 3, page)) Plugin.Log.Information("[Stables] Refreshing the initial roster page through its native list event");
            return;
        }
        if (addon == null || !TryReadPage(addon, out var actual, out var pages, out var rows, out _)) return;
        if (actual != page) { ClickList(addon, 3, page); return; }
        birds.RemoveAll(b => b.Page == page);
        birds.AddRange(rows);
        Plugin.Log.Information($"[Stables] Scanned page {page + 1}/{pages}; rows={rows.Count}; ready={rows.Count(b => b.Ready)}; capped={rows.Count(b => b.Capped)}");
        if (++page < pages)
        {
            ClickList(addon, 3, page);
            entered = DateTime.UtcNow;
            return;
        }
        scannedCharacter = character;
        scannedDestination = settings.Destination;
        if (scanOnly) { Complete($"Scanned {birds.Count} chocobos; select a target in character settings"); return; }
        var owner = settings.Target == StableTarget.OwnChocobo ? Plugin.ObjectTable.LocalPlayer!.Name.TextValue : settings.OtherOwner;
        var matches = birds.Where(b => b.Owner.Equals(owner, StringComparison.Ordinal) && (settings.Target == StableTarget.OwnChocobo || b.Name.Equals(settings.OtherChocobo, StringComparison.Ordinal))).ToList();
        if (matches.Count != 1) { Fail(matches.Count == 0 ? "Selected chocobo is not stabled here; scan and check its destination." : "Selected bird identity is ambiguous; no training dispatched."); return; }
        selected = matches[0];
        Plugin.Log.Information($"[Stables] Selected bird: personal={selected.Row == PersonalBirdRow}; rank={selected.Rank}; capped={selected.Capped}; ready={selected.Ready}");
        if (selected.Rank >= 20 || selected.Capped)
        {
            if (settings.Target == StableTarget.OwnChocobo && selected.Rank is >= 10 and < 20 && OnionStock < 0)
            { Fail("Onion inventory data is unavailable; no quest dispatched."); return; }
            if (ChocoboOnionQuests.ShouldAcquire(selected, settings.Target, OnionStock) && settings.BuyOnionFromMarketboard)
            {
                Cleanup();
                Transition(Step.OnionPurchase, "Closing stable menus before purchasing one Thavnairian Onion");
                return;
            }
            if (ChocoboOnionQuests.ShouldAcquire(selected, settings.Target, OnionStock)
                && !ChocoboOnionQuests.RewardsComplete(QuestManager.IsQuestComplete))
            {
                Cleanup();
                Transition(Step.OnionQuest, "Closing stable menus before acquiring one quest-reward Thavnairian Onion");
            }
            else Complete(selected.Progression + (settings.Target == StableTarget.OwnChocobo && selected.Rank is >= 10 and < 20
                ? OnionStock > 0 ? $"; onions available: {OnionStock}" : ChocoboOnionQuests.RewardsComplete(QuestManager.IsQuestComplete)
                    ? "; both free onion rewards are already completed" : string.Empty : string.Empty));
            return;
        }
        if (!selected.Ready) { Complete($"Training cooldown: {selected.Training}"); return; }
        if (Count(FeedItemId) < 1) { Fail("Missing training feed: Krakka Root (8165)."); return; }
        Transition(Step.SelectBird, "Revalidating the selected chocobo before training");
    }

    private void SelectBird()
    {
        var addon = Own("HousingChocoboList");
        if (addon == null || selected == null || !TryReadPage(addon, out var actual, out _, out var rows, out _)) return;
        if (actual != selected.Page) { ClickList(addon, 3, selected.Page); return; }
        var current = rows.Where(b => b.Matches(selected.Owner, selected.Name)).ToList();
        if (current.Count != 1 || !current[0].Ready || current[0].Capped || current[0].Rank >= 20)
        { Fail("Selected chocobo changed or is no longer trainable; scan again."); return; }
        selected = current[0];
        feedBefore = Count(FeedItemId);
        if (feedBefore < 1) { Fail("Krakka Root is no longer available; no training dispatched."); return; }
        if (selected.Row == PersonalBirdRow ? ClickPersonalBird(addon) : ClickList(addon, 18, selected.Row))
            Transition(Step.Inventory, "Selecting one Krakka Root for training");
    }

    private static bool ClickPersonalBird(AtkUnitBase* addon)
    {
        var node = addon->GetNodeById(15);
        var row = node == null ? null : node->GetAsAtkComponentListItemRenderer();
        if (row == null || !node->IsVisible() || !row->IsEnabled) return false;
        AtkEvent* registered = null;
        var count = 0;
        for (var evt = node->AtkEventManager.Event; evt != null && count++ < 16; evt = evt->NextEvent)
        {
            if (evt->State.EventType != AtkEventType.ButtonClick || evt->Param != 1 || evt->Listener != (AtkEventListener*)addon
                || evt->State.StateFlags.HasFlag(AtkEventStateFlags.IsGlobalEvent)) continue;
            if (registered != null) return false;
            registered = evt;
        }
        if (registered == null) return false;
        var click = *registered;
        var data = new AtkEventData();
        click.Listener->ReceiveEvent(AtkEventType.ButtonClick, 1, &click, &data);
        Plugin.Log.Information("[Stables] Selected the revalidated personal chocobo through its registered button event");
        return true;
    }

    private static bool ClickList(AtkUnitBase* addon, uint nodeId, int index)
    {
        if (nodeId is not (3 or 18) || index < 0) return false;
        var node = addon->GetNodeById(nodeId);
        var list = node == null ? null : node->GetAsAtkComponentList();
        if (list == null || list->UldManager.NodeList == null || list->UldManager.NodeListCount > 100) return false;
        if (nodeId == 3 && index >= list->ListLength) return false;
        // Upstream native contract: page renderers start at 1, bird renderers at 2.
        var rendererIndex = index + (nodeId == 18 ? 2 : 1);
        if (rendererIndex >= list->UldManager.NodeListCount) return false;
        var rendererNode = list->UldManager.NodeList[rendererIndex];
        var renderer = rendererNode == null ? null : rendererNode->GetAsAtkComponentListItemRenderer();
        if (renderer == null || !renderer->IsEnabled) return false;
        var parameter = nodeId == 18 ? 3u : 2u;
        var evt = new AtkEvent
        {
            Target = nodeId == 3 ? (AtkEventTarget*)list : (AtkEventTarget*)node,
            Listener = &addon->AtkEventListener,
            Param = parameter,
            State = new AtkEventState { EventType = AtkEventType.ListItemClick, StateFlags = AtkEventStateFlags.Unk3 },
        };
        var data = new AtkEventData
        {
            ListItemData = new AtkEventData.AtkListItemData
            {
                ListItemRenderer = renderer,
                SelectedIndex = index,
                HoveredItemIndex3 = (short)index,
            },
        };
        addon->ReceiveEvent(AtkEventType.ListItemClick, (int)parameter, &evt, &data);
        // This addon does not update the generic page selection from a synthetic event.
        if (nodeId == 3) list->SelectedItemIndex = index;
        return true;
    }

    private void OpenReward()
    {
        if (Own("SelectYesno") != null)
        { Fail("Unexpected training confirmation; no feed action dispatched."); return; }
        AtkUnitBase* inventory = null;
        foreach (var name in InventoryAddons)
        {
            var addon = preexistingInventory.Contains(name) ? Visible(name) : Own(name);
            if (addon != null) inventory = addon;
        }
        if (inventory == null) return;
        foreach (var bag in Bags)
        {
            var manager = InventoryManager.Instance();
            var container = manager == null ? null : manager->GetInventoryContainer(bag);
            if (container == null || !container->IsLoaded) continue;
            for (var i = 0; i < container->Size; i++)
            {
                var slot = container->GetInventorySlot(i);
                if (slot == null || slot->GetItemId() != FeedItemId || slot->Quantity <= 0) continue;
                var agent = AgentInventoryContext.Instance();
                if (agent == null) return;
                agent->OpenForItemSlot(bag, i, 0, inventory->Id);
                Transition(Step.Reward, "Confirming the training Reward action");
                return;
            }
        }
        Fail("Krakka Root is no longer available in inventory.");
    }

    private void Reward()
    {
        var menu = Own("ContextMenu");
        var agent = AgentInventoryContext.Instance();
        if (menu == null || agent == null) return;
        var index = 0;
        foreach (var value in agent->EventParams)
        {
            if (value.Type != AtkValueType.String) continue;
            var label = value.String.ToString();
            if (label.Equals("Reward", StringComparison.OrdinalIgnoreCase))
            {
                if (Count(FeedItemId) != feedBefore) { Fail("Feed stock changed before Reward; no training dispatched."); return; }
                Plugin.Log.Information($"[Stables] Dispatching one Krakka Root Reward; feedStock={feedBefore}");
                Callback.Fire(menu, false, 0, index, 0, 0, 0);
                Transition(Step.TrainingResult, "Training dispatched; waiting for feed consumption and native cooldown");
                return;
            }
            index++;
        }
        Fail("Training Reward action is unavailable; no feed action dispatched.");
    }

    private void VerifyTraining()
    {
        AdvanceTalk();
        var count = Count(FeedItemId);
        if (count < feedBefore - 1) { Fail("Feed stock changed by more than one; training result is uncertain."); return; }
        if (count != feedBefore - 1) return;
        var addon = Own("HousingChocoboList");
        if (addon == null)
        {
            InteractStable();
            var menu = StableMenu();
            if (menu != null) SelectEntry(menu, "Tend to a Specified Chocobo");
            return;
        }
        if (selected == null || !TryReadPage(addon, out var actual, out _, out var rows, out _)) return;
        if (actual != selected.Page) { ClickList(addon, 3, selected.Page); return; }
        var current = rows.SingleOrDefault(b => b.Matches(selected.Owner, selected.Name));
        if (current == null || current.Ready) return;
        Trained = true;
        NextTrainingUtc = DateTime.UtcNow.AddHours(1);
        birds.RemoveAll(b => b.Page == actual); birds.AddRange(rows);
        Complete($"Training verified; {current.Progression}; cooldown {current.Training}");
    }

    private void AdvanceTalk()
    {
        var talk = Own("Talk");
        if (talk != null) Callback.Fire(talk, true, 0);
    }

    private void UpdateOnionPurchase()
    {
        if (Plugin.Condition[ConditionFlag.InCombat] || Plugin.Condition[ConditionFlag.BoundByDuty] || Plugin.Condition[ConditionFlag.BoundByDuty56])
        { Fail("Onion purchase interrupted by combat or a duty."); return; }
        if (onionOrderId.Length == 0)
        {
            if (OnionStock < 0) { Fail("Onion inventory data is unavailable; no order dispatched."); return; }
            if (OnionStock > 0) { Complete("Thavnairian Onion available; use one manually to raise the personal chocobo's rank cap"); return; }
            if (Plugin.Condition[ConditionFlag.OnFreeTrial]) { Fail("Marketboard purchasing is unavailable on a Free Trial."); return; }
            if (settings.OnionMaxUnitPrice <= 0 || settings.OnionGilBudget <= 0)
            { Fail("Set positive onion unit-price and total-gil limits in Marketboard settings."); return; }
            if (!GameHelpers.IsPlayerAvailable()) return;
            if (Plugin.PluginInterface.GetIpcSubscriber<int>("Emptor.ApiVersion").InvokeFunc() != 5)
            { Fail("Onion purchasing requires Emptor API 5."); return; }
            if (Plugin.PluginInterface.GetIpcSubscriber<bool>("Emptor.IsBusy").InvokeFunc())
            { Fail("Emptor already owns another task; no onion order dispatched."); return; }
            var inventory = InventoryManager.Instance();
            if (inventory == null) { Fail("Gil balance is unavailable; no order dispatched."); return; }
            onionGilBefore = inventory->GetGil();
            onionRequestId = $"VMX-stables-{Guid.NewGuid():N}";
            var request = FishCollectionPolicy.MarketRequestJson(onionRequestId,
                Plugin.ObjectTable.LocalPlayer!.CurrentWorld.Value.Name.ToString(),
                new CollectionSupply(ChocoboOnionQuests.ItemId, "Thavnairian Onion", false, 1, true, settings.OnionMaxUnitPrice),
                1, settings.OnionGilBudget, onionGilBefore);
            using var response = JsonDocument.Parse(Plugin.PluginInterface.GetIpcSubscriber<string, string>("Emptor.SubmitOrder").InvokeFunc(request));
            var root = response.RootElement;
            if (!root.TryGetProperty("orderId", out var order) || string.IsNullOrWhiteSpace(order.GetString()) ||
                !root.TryGetProperty("clientRequestId", out var correlation) || correlation.GetString() != onionRequestId)
            { Fail("Emptor did not accept the owned onion order."); return; }
            onionOrderId = order.GetString()!;
            onionOrderCancelRequested = false;
            onionOrderStartedUtc = DateTime.UtcNow;
            StatusText = "Purchasing one Thavnairian Onion on the current world";
            return;
        }

        using var result = JsonDocument.Parse(Plugin.PluginInterface.GetIpcSubscriber<string, string>("Emptor.GetOrder").InvokeFunc(onionOrderId));
        var status = result.RootElement;
        if (status.GetProperty("orderId").GetString() != onionOrderId || status.GetProperty("clientRequestId").GetString() != onionRequestId)
            throw new InvalidOperationException("Onion order ownership could not be verified.");
        if (!status.TryGetProperty("finishedUtc", out var finished) || finished.ValueKind == JsonValueKind.Null)
        {
            if (DateTime.UtcNow - onionOrderStartedUtc >= TimeSpan.FromMinutes(5))
                Fail("Onion purchase timed out; awaiting owned cancellation.");
            return;
        }
        var manager = InventoryManager.Instance();
        if (manager == null || OnionStock < 0) return;
        var reportedSpent = status.GetProperty("totalGilSpent").GetInt64();
        var spent = Math.Max(reportedSpent, onionGilBefore - manager->GetGil());
        onionOrderId = onionRequestId = string.Empty;
        if (reportedSpent < 0 || spent > settings.OnionGilBudget)
        { Fail("Onion order exceeded its total-gil limit."); return; }
        if (OnionStock != 1) { Fail("Onion order finished without an exact one-item inventory receipt."); return; }
        Complete($"Thavnairian Onion purchase verified ({spent} gil); use one manually to raise the personal chocobo's rank cap");
    }

    private bool StopOwnedOnionPurchase()
    {
        if (onionOrderId.Length == 0) return true;
        try
        {
            using var result = JsonDocument.Parse(Plugin.PluginInterface.GetIpcSubscriber<string, string>("Emptor.GetOrder").InvokeFunc(onionOrderId));
            var status = result.RootElement;
            if (status.GetProperty("orderId").GetString() != onionOrderId || status.GetProperty("clientRequestId").GetString() != onionRequestId)
                return false;
            if (status.TryGetProperty("finishedUtc", out var finished) && finished.ValueKind != JsonValueKind.Null)
            {
                onionOrderId = onionRequestId = string.Empty;
                return true;
            }
            if (!onionOrderCancelRequested)
                onionOrderCancelRequested = Plugin.PluginInterface.GetIpcSubscriber<string, bool>("Emptor.CancelOrder").InvokeFunc(onionOrderId);
        }
        catch { /* Retain ownership until the exact order can be observed terminal. */ }
        return false;
    }

    private void UpdateOnionQuest()
    {
        var provider = WigglyGathering.ResolvePlugin();
        var running = Plugin.PluginInterface.GetIpcSubscriber<bool>("WigglyQuest.IsRunning");
        var current = Plugin.PluginInterface.GetIpcSubscriber<string?>("WigglyQuest.GetCurrentQuestId");
        if (onionQuestProvider != null && !ReferenceEquals(provider, onionQuestProvider))
        { Fail("Wiggly Quest reloaded during onion acquisition; start a new stable visit to continue."); return; }
        if (!onionQuestDispatched && OnionStock > 0)
        { Complete($"Thavnairian Onion available ({OnionStock}); use one manually to raise the personal chocobo's rank cap"); return; }
        if (!onionQuestDispatched && OnionStock < 0)
        { Fail("Onion inventory data is unavailable; no quest dispatched."); return; }
        if (onionQuestId != 0)
        {
            var quest = onionQuestId.ToString();
            var isRunning = running.InvokeFunc();
            var currentId = current.InvokeFunc();
            if (!onionQuestDispatched && !QuestManager.IsQuestComplete(onionQuestId))
            {
                if (isRunning) { Fail("Another Wiggly task started before onion quest dispatch."); return; }
                // An accepted quest must first replace the resolver's cached class/MSQ selection.
                if (Plugin.PluginInterface.GetIpcSubscriber<string, bool>("WigglyQuest.IsQuestAccepted").InvokeFunc(quest)
                    && currentId != quest)
                {
                    if (DateTime.UtcNow >= onionQuestAcknowledgementDeadline)
                        Fail("Wiggly Quest did not select the prepared onion priority; no quest dispatched.");
                    return;
                }
                onionQuestDispatched = true;
                onionQuestAcknowledgementDeadline = DateTime.UtcNow.AddSeconds(5);
                if (!Plugin.PluginInterface.GetIpcSubscriber<string, bool>("WigglyQuest.StartSingleQuest").InvokeFunc(quest))
                { Fail("Wiggly Quest rejected the onion-chain single-quest route."); return; }
                StatusText = $"Acquiring one Thavnairian Onion: {Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Quest>().GetRow((uint)onionQuestId + 65536).Name}";
                Plugin.Log.Information($"[Stables] Dispatched owned onion-chain single quest: quest={quest}; onionStock={OnionStock}");
                return;
            }
            if (QuestManager.IsQuestComplete(onionQuestId))
            {
                if (isRunning)
                {
                    if (currentId != quest) { Fail("Another Wiggly quest owns the character after the onion quest."); return; }
                    StopOwnedOnionQuest(clearOwnership: false);
                    return;
                }
                if (!GameHelpers.IsPlayerAvailable()) return;
                if (!StopOwnedOnionQuest(clearOwnership: false)) return;
                if (ChocoboOnionQuests.IsReward(onionQuestId) && OnionStock < 1)
                { Fail("Onion reward quest completed but no Thavnairian Onion was found in inventory."); return; }
                Plugin.Log.Information($"[Stables] Native onion-chain quest completion verified: quest={onionQuestId}; onionStock={OnionStock}");
                onionQuestId = 0;
                onionQuestProvider = null;
                onionQuestDispatched = onionQuestAcknowledged = onionQuestStopRequested = onionQuestStopWarningLogged = false;
            }
            else
            {
                if (isRunning && currentId == quest)
                {
                    if (!onionQuestAcknowledged) Plugin.Log.Information($"[Stables] Wiggly acknowledged owned onion-chain quest {quest}");
                    onionQuestAcknowledged = true;
                }
                else if (onionQuestAcknowledged || DateTime.UtcNow >= onionQuestAcknowledgementDeadline)
                { Fail("The owned onion quest stopped, was replaced or did not start before native completion; no automatic redispatch."); }
                return;
            }
        }
        if (OnionStock < 0) { Fail("Onion inventory data is unavailable; no new quest dispatched."); return; }
        if (OnionStock > 0)
        { Complete($"Thavnairian Onion available ({OnionStock}); use one manually to raise the personal chocobo's rank cap"); return; }
        if (ChocoboOnionQuests.RewardsComplete(QuestManager.IsQuestComplete))
        { Complete("Both free onion reward quests are already complete; obtain an onion manually"); return; }
        if (!GameHelpers.IsPlayerAvailable()) return;
        if (running.InvokeFunc()) { Fail("Wiggly Quest already owns another task; no onion quest dispatched."); return; }
        var locked = Plugin.PluginInterface.GetIpcSubscriber<string, bool>("WigglyQuest.IsQuestLocked");
        var accepted = Plugin.PluginInterface.GetIpcSubscriber<string, bool>("WigglyQuest.IsQuestAccepted");
        var ready = Plugin.PluginInterface.GetIpcSubscriber<string, bool>("WigglyQuest.IsReadyToAcceptQuest");
        var quests = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Quest>();
        var next = ChocoboOnionQuests.SelectNext(QuestManager.IsQuestComplete, id =>
            quests.TryGetRow((uint)id + 65536, out var quest) && Plugin.ObjectTable.LocalPlayer!.Level >= quest.ClassJobLevel[0]
            && !locked.InvokeFunc(id.ToString()) && (accepted.InvokeFunc(id.ToString()) || ready.InvokeFunc(id.ToString())));
        if (next == 0)
        { Fail("No unfinished onion-chain quest is available through Wiggly Quest; check its routes, current job/level and MSQ prerequisites."); return; }
        // Keep the selected quest in Wiggly's resolver without introducing saved state.
        onionQuestProvider = provider;
        onionQuestId = next;
        onionQuestDispatched = false;
        onionQuestAcknowledged = false;
        onionQuestStopRequested = onionQuestStopWarningLogged = false;
        onionQuestAcknowledgementDeadline = DateTime.UtcNow.AddSeconds(5);
        if (!Plugin.PluginInterface.GetIpcSubscriber<string, bool>("WigglyQuest.IsQuestInPriority").InvokeFunc(next.ToString()))
        {
            const System.Reflection.BindingFlags members = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var services = provider.GetType().GetField("_serviceProvider", members)?.GetValue(provider) as IServiceProvider;
            var configuration = services?.GetService(provider.GetType().Assembly.GetType("Questionable.Configuration", true)!);
            var general = configuration?.GetType().GetProperty("General")?.GetValue(configuration);
            if (general?.GetType().GetProperty("PersistPriorityQuestsBetweenSessions")?.GetValue(general) is not false)
            { Fail("Wiggly priority persistence must be disabled for temporary onion acquisition; its saved settings were not changed."); return; }
            // Record ownership before mutation; remove only an entry we inserted.
            onionQuestPriorityInserted = true;
            if (!Plugin.PluginInterface.GetIpcSubscriber<int, string, bool>("WigglyQuest.InsertQuestPriority").InvokeFunc(0, next.ToString())
                || !Plugin.PluginInterface.GetIpcSubscriber<string, bool>("WigglyQuest.IsQuestInPriority").InvokeFunc(next.ToString()))
            { Fail("Wiggly Quest did not retain the temporary onion priority; no quest dispatched."); return; }
        }
        StatusText = $"Preparing {quests.GetRow((uint)next + 65536).Name} through Wiggly Quest";
        Plugin.Log.Information($"[Stables] Prepared onion-chain quest: quest={next}; temporaryPriority={onionQuestPriorityInserted}; onionStock={OnionStock}");
    }

    private bool StopOwnedOnionQuest(bool clearOwnership = true)
    {
        if (onionQuestId == 0) return true;
        try
        {
            if (Plugin.PluginInterface.InstalledPlugins.Any(p => p.IsLoaded && p.InternalName == "WigglyQuest")
                && ReferenceEquals(WigglyGathering.ResolvePlugin(), onionQuestProvider)
                && (!Plugin.ClientState.IsLoggedIn || Plugin.PlayerState.ContentId == character)
                && onionQuestDispatched
                && Plugin.PluginInterface.GetIpcSubscriber<string?>("WigglyQuest.GetCurrentQuestId").InvokeFunc() == onionQuestId.ToString()
                && Plugin.PluginInterface.GetIpcSubscriber<bool>("WigglyQuest.IsRunning").InvokeFunc())
            {
                if (!onionQuestStopRequested)
                {
                    onionQuestStopRequested = true;
                    if (!Plugin.CommandManager.ProcessCommand("/wqst stop"))
                    {
                        onionQuestStopWarningLogged = true;
                        Plugin.Log.Warning("[Stables] Wiggly Quest did not accept owned onion-quest cancellation; cleanup remains pending");
                    }
                }
                // Confirm synchronous Stop as well, so unload can release the temporary priority.
                if (Plugin.PluginInterface.GetIpcSubscriber<bool>("WigglyQuest.IsRunning").InvokeFunc()) return false;
            }
            if (onionQuestPriorityInserted && Plugin.PluginInterface.InstalledPlugins.Any(p => p.IsLoaded && p.InternalName == "WigglyQuest")
                && ReferenceEquals(WigglyGathering.ResolvePlugin(), onionQuestProvider))
            {
                if (Plugin.PluginInterface.GetIpcSubscriber<string, bool>("WigglyQuest.IsQuestInPriority").InvokeFunc(onionQuestId.ToString())
                    && (!Plugin.PluginInterface.GetIpcSubscriber<string, bool>("WigglyQuest.RemovePriorityQuest").InvokeFunc(onionQuestId.ToString())
                        || Plugin.PluginInterface.GetIpcSubscriber<string, bool>("WigglyQuest.IsQuestInPriority").InvokeFunc(onionQuestId.ToString())))
                    throw new InvalidOperationException("The temporary onion priority could not be removed.");
                onionQuestPriorityInserted = false;
                Plugin.Log.Information($"[Stables] Temporary onion priority released: quest={onionQuestId}");
            }
        }
        catch (Exception ex)
        {
            if (!onionQuestStopWarningLogged) Plugin.Log.Warning(ex, "[Stables] Owned onion-quest cancellation could not be confirmed; cleanup remains pending");
            onionQuestStopWarningLogged = true;
            return false;
        }
        if (clearOwnership)
        {
            onionQuestId = 0;
            onionQuestProvider = null;
            onionQuestDispatched = onionQuestAcknowledged = onionQuestStopRequested = onionQuestStopWarningLogged = false;
            onionQuestPriorityInserted = false;
        }
        return true;
    }

    private int Count(uint itemId)
    {
        if (!NativeStateReady) return 0;
        var manager = InventoryManager.Instance();
        return manager == null ? 0 : manager->GetInventoryItemCount(itemId);
    }

    private void Transition(Step next, string status)
    {
        step = next; entered = DateTime.UtcNow;
        StatusText = status;
        Plugin.Log.Information($"[Stables] {next}: {status}");
    }

    private void StopMovement()
    {
        if (moving) plugin.VNavmeshIPC.Stop();
        moving = false;
    }

    private void Cleanup()
    {
        var questStopped = StopOwnedOnionQuest();
        var purchaseStopped = StopOwnedOnionPurchase();
        StopMovement();
        step = Step.Idle;
        cleanupPending = !questStopped || !purchaseStopped || ownedUi.Count > 0;
        if (cleanupPending)
        {
            cleanupQuietSince = DateTime.MinValue;
            nextAction = DateTime.MinValue;
            // UI buttons, FULL STOP and disposal can call here outside a framework update.
            // Native menu callbacks belong to Update(), after readiness is rechecked.
        }
    }

    private void TickCleanup()
    {
        // Closing a child can reopen its parent on a later framework tick.
        // Retain ownership through the same two-second quiet period used by task handoffs.
        var now = DateTime.UtcNow;
        if (now < nextAction) return;
        nextAction = now.AddMilliseconds(650);
        if (!StopOwnedOnionQuest() || !StopOwnedOnionPurchase()) return;
        if (Plugin.ClientState.IsLoggedIn && Plugin.PlayerState.ContentId == character)
        {
            if (!NativeStateReady) return;
            if (ownedUi.ContainsKey("SelectString") && AtDestination()) StableMenu();
            foreach (var (name, id) in ownedUi.Reverse().ToArray())
            {
                var addon = Visible(name);
                if (addon == null || addon->Id != id) continue;
                cleanupQuietSince = DateTime.MinValue;
                Plugin.Log.Debug($"[Stables] Closing owned addon {name}");
                if (name == "SelectString") GameHelpers.TryCloseAddonByCallback(name);
                else addon->Close(true);
                return;
            }
            if (cleanupQuietSince == DateTime.MinValue) { cleanupQuietSince = now; return; }
            if (now - cleanupQuietSince < TimeSpan.FromSeconds(2)) return;
        }
        ownedUi.Clear();
        preexistingInventory.Clear();
        cleanupPending = false;
        Plugin.Log.Information("[Stables] Owned stable menus closed; cleanup settled");
    }

    private void Fail(string reason)
    {
        Cleanup(); IsFailed = true; IsComplete = false; StatusText = reason;
        Plugin.Log.Warning($"[Stables] {reason}");
    }

    private void Complete(string status)
    {
        Cleanup(); IsComplete = true; StatusText = status;
        Plugin.Log.Information($"[Stables] Completed visit: trained={Trained}; cleaned={Cleaned}; feedStock={Count(FeedItemId)}; broomStock={Count(BroomItemId)}; onionStock={OnionStock}; {status}");
    }

    public void Cancel()
    {
        if (IsActive) { Cleanup(); IsFailed = true; IsComplete = false; StatusText = "Stable visit cancelled"; Plugin.Log.Information("[Stables] Cancelled owned visit"); }
    }

    public void Reset()
    {
        Cleanup(); IsComplete = IsFailed = Trained = Cleaned = false;
        NextTrainingUtc = DateTime.MinValue;
        stableObject = 0; page = 0; selected = null; travelStarted = stableLookupLogged = rosterShapeLogged = false;
        nextAction = DateTime.MinValue;
    }
}
