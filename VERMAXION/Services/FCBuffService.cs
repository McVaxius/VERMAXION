using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Numerics;
using Dalamud.Game.Command;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using VERMAXION.Models;

namespace VERMAXION.Services;

public class FCBuffService : IDisposable
{
    private readonly ICommandManager commandManager;
    private readonly IPluginLog log;
    private readonly IClientState clientState;
    private readonly ICondition condition;
    private readonly IObjectTable objects;
    private readonly ITargetManager targetManager;
    private readonly ConfigManager configManager;
    private readonly Plugin plugin;

    private const int BaseMinGil = 16000; // Base minimum gil required
    private const int MaxPurchaseAttempts = 15;

    private FCBuffState state = FCBuffState.Idle;
    private DateTime stateEnteredAt = DateTime.MinValue;
    private int purchaseAttempts = 0;
    private int buyCount = 0;
    private int buyMax = 15;
    private int maxPurchaseAttempts = 2; // Try SS2 first, then SS1
    private bool isSealSweetenerTwo = true; // Try Seal Sweetener II first

    // FC points threshold from FUTA_GC.lua
    private const int MinFCPoints = 500000;
    // Gil requirement from FUTA_GC.lua
    private const int MinGil = 16000;

    public enum FCBuffState
    {
        Idle,
        CheckingFCPoints,
        OpeningFCWindow,
        WaitingForFCWindow,
        CheckingFCPointsInWindow,
        CheckingBuffInventory,
        CheckingIfRefillNeeded,
        NavigatingToGC,
        WaitingForAftArrival,
        WaitingForGridaniaArrival,
        WaitingForDahArrival,
        NavigatingToQuartermaster,
        WaitingForQuartermasterArrival,
        TargetingQuartermaster,
        InteractingQuartermaster,
        WaitingForSelectString1,
        WaitingForExchange,
        PurchasingBuff,
        WaitingForPurchaseConfirm,
        ConfirmingPurchase,
        PurchaseLoop,
        ClosingWindows,
        Complete,
        Failed
    }

    public FCBuffState State => state;
    public bool IsActive => state != FCBuffState.Idle && state != FCBuffState.Complete && state != FCBuffState.Failed;
    public bool IsComplete => state == FCBuffState.Complete;
    public bool IsFailed => state == FCBuffState.Failed;
    public string StatusText => state.ToString();

    public FCBuffService(ICommandManager commandManager, IPluginLog log, IClientState clientState, ICondition condition, IObjectTable objects, ITargetManager targetManager, ConfigManager configManager, Plugin plugin)
    {
        this.commandManager = commandManager;
        this.log = log;
        this.clientState = clientState;
        this.condition = condition;
        this.objects = objects;
        this.targetManager = targetManager;
        this.configManager = configManager;
        this.plugin = plugin;
    }

    public void Start(int maxAttempts = 2)
    {
        if (IsActive) return;
        
        // Force load config from file to get latest values
        configManager.LoadAllAccounts();
        log.Information("[FCBuff] Forced config load - getting latest values from file");
        
        // Get config AFTER loading to ensure we have the latest values
        var config = configManager.GetActiveConfig();
        log.Information($"[FCBuff] Config Debug: CurrentAccountId='{configManager.CurrentAccountId}', SelectedCharacterKey='{configManager.SelectedCharacterKey}'");
        log.Information($"[FCBuff] Task Start Config: FCBuffMinPoints={config.FCBuffMinPoints:N0}, FCBuffPurchaseAttempts={config.FCBuffPurchaseAttempts}");
        log.Information($"[FCBuff] Task Start Config: FCBuffMinGil={config.FCBuffMinGil:N0}");
        
        purchaseAttempts = config.FCBuffPurchaseAttempts;
        buyCount = 0;
        isSealSweetenerTwo = true; // Start with Seal Sweetener II
        SetState(FCBuffState.CheckingFCPoints);
        log.Information($"[FCBuff] Starting FC buff refill (max attempts: {config.FCBuffPurchaseAttempts})");
    }

    public void RunTask()
    {
        log.Information("[VERMAXION] Manual FC Buff Refill triggered");
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

    private unsafe int GetCurrentGCTerritory()
    {
        // Get Free Company's Grand Company for teleportation
        try
        {
            var infoProxyFreeCompany = InfoProxyFreeCompany.Instance();
            if (infoProxyFreeCompany != null)
            {
                var fcGrandCompany = infoProxyFreeCompany->GrandCompany;
                var gcString = fcGrandCompany.ToString();
                
                log.Information($"[FCBuff] FC Grand Company: {gcString}");
                
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
                
                log.Information($"[FCBuff] Using FC GC Choice: {gcChoice} ({gcString})");
                
                // Convert GC ID to territory ID
                return gcChoice switch
                {
                    1 => 128, // Maelstrom (Limsa)
                    2 => 132, // Order of the Twin Adder (Gridania) - territory 132
                    3 => 130, // Immortal Flames (Ul'dah)
                    _ => 128, // Default to Limsa
                };
            }
            else
            {
                log.Warning("[FCBuff] InfoProxyFreeCompany is null, using player GC");
                // Fallback to player's GC
                return GetPlayerGCTerritory();
            }
        }
        catch (Exception ex)
        {
            log.Error($"[FCBuff] Failed to get FC GC: {ex.Message}, using player GC");
            return GetPlayerGCTerritory();
        }
    }

    private int GetPlayerGCTerritory()
    {
        // Get player's Grand Company from PlayerState (fallback)
        try
        {
            if (Plugin.PlayerState != null)
            {
                var gc = Plugin.PlayerState.GrandCompany;
                var gcId = gc.RowId;
                return gcId switch
                {
                    1 => 128, // Maelstrom (Limsa)
                    2 => 132, // Order of the Twin Adder (Gridania) - territory 132
                    3 => 130, // Immortal Flames (Ul'dah)
                    _ => 128, // Default to Limsa
                };
            }
        }
        catch
        {
            log.Warning("[FCBuff] Failed to get player GC, defaulting to Limsa");
        }
        return 128; // Default to Limsa
    }

    public void Reset() => SetState(FCBuffState.Idle);
    public void Dispose() { }

    public unsafe void Update()
    {
        if (!IsActive) return;

        var elapsed = (DateTime.UtcNow - stateEnteredAt).TotalSeconds;

        switch (state)
        {
            case FCBuffState.CheckingFCPoints:
                if (elapsed < 1) return;
                log.Information("[FCBuff] Checking if we have enough FC points for refill");
                SetState(FCBuffState.OpeningFCWindow);
                break;

            case FCBuffState.OpeningFCWindow:
                if (elapsed < 1) return;
                log.Information("[FCBuff] Opening FC window: /freecompanycmd");
                CommandHelper.SendCommand("/freecompanycmd");
                SetState(FCBuffState.WaitingForFCWindow);
                break;

            case FCBuffState.WaitingForFCWindow:
                if (elapsed < 3)
                {
                    // Check if FC window is ready
                    if (GameHelpers.IsAddonVisible("FreeCompany"))
                    {
                        log.Information("[FCBuff] FC window is ready");
                        SetState(FCBuffState.CheckingFCPointsInWindow);
                    }
                    return;
                }
                // Try opening again if first attempt failed
                if (elapsed < 6)
                {
                    log.Information("[FCBuff] First attempt failed, trying again");
                    commandManager.ProcessCommand("/freecompanycmd");
                    SetState(FCBuffState.WaitingForFCWindow);
                    return;
                }
                log.Error("[FCBuff] Failed to open FC window");
                SetState(FCBuffState.Failed);
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
                    var sealSweetenerCount = CountSealSweetenerBuffs();
                    log.Information($"[FCBuff] Seal Sweetener II count: {sealSweetenerCount}");
                    
                    if (sealSweetenerCount == 0)
                    {
                        log.Information("[FCBuff] No Seal Sweetener II found, proceeding with refill");
                        SetState(FCBuffState.CheckingIfRefillNeeded);
                    }
                    else
                    {
                        log.Information($"[FCBuff] Found {sealSweetenerCount} Seal Sweetener II, skipping refill");
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
                // Check gil
                var activeConfig = configManager.GetActiveConfig();
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
                SetState(FCBuffState.NavigatingToGC);
                break;

            case FCBuffState.NavigatingToGC:
                if (elapsed < 1) return;
                
                // Get current GC territory to determine navigation
                var currentGCTerritory = GetCurrentGCTerritory();
                var currentTerritory = clientState.TerritoryType;
                
                // Check if we're already in the right GC territory
                switch (currentGCTerritory)
                {
                    case 128: // Limsa Lominsa
                        if (currentTerritory == 128)
                        {
                            log.Information("[FCBuff] Already in Limsa GC territory, skipping teleport");
                            SetState(FCBuffState.NavigatingToQuartermaster);
                        }
                        else
                        {
                            log.Information("[FCBuff] Navigating to Limsa GC: /li aft");
                            CommandHelper.SendCommand("/li aft");
                            SetState(FCBuffState.WaitingForAftArrival);
                        }
                        break;
                    case 129: // Gridania
                        if (currentTerritory == 132) // Gridania is territory 132
                        {
                            log.Information("[FCBuff] Already in Gridania GC territory, skipping teleport");
                            SetState(FCBuffState.NavigatingToQuartermaster);
                        }
                        else
                        {
                            log.Information("[FCBuff] Navigating to Gridania GC: /li gridania");
                            CommandHelper.SendCommand("/li gridania");
                            SetState(FCBuffState.WaitingForGridaniaArrival);
                        }
                        break;
                    case 130: // Ul'dah
                        if (currentTerritory == 130)
                        {
                            log.Information("[FCBuff] Already in Ul'dah GC territory, skipping teleport");
                            SetState(FCBuffState.NavigatingToQuartermaster);
                        }
                        else
                        {
                            log.Information("[FCBuff] Navigating to Ul'dah GC: /li dah");
                            CommandHelper.SendCommand("/li dah");
                            SetState(FCBuffState.WaitingForDahArrival);
                        }
                        break;
                    default:
                        log.Error($"[FCBuff] Unknown GC territory: {currentGCTerritory}");
                        SetState(FCBuffState.Failed);
                        break;
                }
                break;

            case FCBuffState.WaitingForAftArrival:
                if (elapsed < 1) return;
                // Wait for arrival at Aft (Upper Decks)
                if (condition[ConditionFlag.BetweenAreas] || condition[ConditionFlag.BetweenAreas51])
                {
                    log.Information("[FCBuff] Still teleporting to Aft...");
                    return;
                }
                if (clientState.TerritoryType == 128 && elapsed >= 3)
                {
                    log.Information("[FCBuff] Arrived at Limsa Aft, navigating to Quartermaster");
                    SetState(FCBuffState.NavigatingToQuartermaster);
                }
                return;

            case FCBuffState.WaitingForGridaniaArrival:
                if (elapsed < 1) return;
                // Wait for arrival at Gridania
                if (condition[ConditionFlag.BetweenAreas] || condition[ConditionFlag.BetweenAreas51])
                {
                    log.Information("[FCBuff] Still teleporting to Gridania...");
                    return;
                }
                if (clientState.TerritoryType == 132 && elapsed >= 3)
                {
                    log.Information("[FCBuff] Arrived at Gridania, navigating to Quartermaster");
                    SetState(FCBuffState.NavigatingToQuartermaster);
                }
                return;

            case FCBuffState.WaitingForDahArrival:
                if (elapsed < 1) return;
                // Wait for arrival at Dah (Ul'dah)
                if (condition[ConditionFlag.BetweenAreas] || condition[ConditionFlag.BetweenAreas51])
                {
                    log.Information("[FCBuff] Still teleporting to Dah...");
                    return;
                }
                if (clientState.TerritoryType == 130 && elapsed >= 3)
                {
                    log.Information("[FCBuff] Arrived at Ul'dah, navigating to Quartermaster");
                    SetState(FCBuffState.NavigatingToQuartermaster);
                }
                return;

            case FCBuffState.NavigatingToQuartermaster:
                if (elapsed < 1) return;
                // Navigate to Quartermaster location based on GC
                var gcTerritory = GetCurrentGCTerritory();
                switch (gcTerritory)
                {
                    case 128: // Limsa - Upper Decks
                        log.Information("[FCBuff] Navigating to Limsa Quartermaster via VNavmesh IPC");
                        plugin.VNavmeshIPC.PathfindAndMoveTo(new Vector3(93, 40f, 68f));
                        break;
                    case 129: // Gridania
                        log.Information("[FCBuff] Navigating to Gridania Quartermaster via VNavmesh IPC");
                        plugin.VNavmeshIPC.PathfindAndMoveTo(new Vector3(-71, -0.5f, -5f));
                        break;
                    case 130: // Ul'dah
                        log.Information("[FCBuff] Navigating to Ul'dah Quartermaster via VNavmesh IPC");
                        plugin.VNavmeshIPC.PathfindAndMoveTo(new Vector3(-144, 4f, -100f));
                        break;
                }
                SetState(FCBuffState.WaitingForQuartermasterArrival);
                break;

            case FCBuffState.WaitingForQuartermasterArrival:
                // Wait for vnav navigation to complete (60s timeout)
                if (elapsed > 60)
                {
                    log.Error("[FCBuff] Timeout waiting for Quartermaster arrival");
                    plugin.VNavmeshIPC.Stop();
                    SetState(FCBuffState.Failed);
                    return;
                }
                
                // Check if we're close enough to target
                var player = objects.LocalPlayer;
                if (player == null) return;
                
                var targetGCTerritory = GetCurrentGCTerritory();
                var targetPos = targetGCTerritory switch
                {
                    128 => new Vector3(93, 40f, 68f),  // Limsa
                    129 => new Vector3(-71, -0.5f, -5f), // Gridania
                    130 => new Vector3(-144, 4f, -100f), // Ul'dah
                    _ => Vector3.Zero
                };
                
                var distance = Vector3.Distance(player.Position, targetPos);
                if (distance < 5f) // Within 5 yalms of target
                {
                    log.Information($"[FCBuff] Arrived at Quartermaster location (distance: {distance:F1}y)");
                    SetState(FCBuffState.TargetingQuartermaster);
                }
                else if (elapsed % 5 == 0) // Log every 5 seconds
                {
                    log.Information($"[FCBuff] Still navigating to Quartermaster... ({elapsed}s elapsed, distance: {distance:F1}y)");
                }
                return;

            case FCBuffState.TargetingQuartermaster:
                if (elapsed < 1) return;
                log.Information("[FCBuff] Targeting OIC Quartermaster");
                
                // Try to target "Quartermaster" first (more specific)
                var quartermaster = GameHelpers.FindObjectByName("Quartermaster");
                if (quartermaster != null)
                {
                    log.Information("[FCBuff] Found Quartermaster, targeting...");
                    targetManager.Target = quartermaster;
                    SetState(FCBuffState.InteractingQuartermaster);
                }
                else
                {
                    // Fallback: try "OIC Quartermaster"
                    var oicQuartermaster = GameHelpers.FindObjectByName("OIC Quartermaster");
                    if (oicQuartermaster != null)
                    {
                        log.Information("[FCBuff] Found OIC Quartermaster, targeting...");
                        targetManager.Target = oicQuartermaster;
                        SetState(FCBuffState.InteractingQuartermaster);
                    }
                    else
                    {
                        // Last resort: use /target command
                        log.Information("[FCBuff] NPC not found, using /target Quartermaster");
                        commandManager.ProcessCommand("/target Quartermaster");
                        SetState(FCBuffState.InteractingQuartermaster);
                    }
                }
                break;

            case FCBuffState.InteractingQuartermaster:
                if (elapsed < 1) return;
                log.Information("[FCBuff] Interacting with OIC Quartermaster");
                if (targetManager.Target != null && GameHelpers.InteractWithObject(targetManager.Target))
                {
                    SetState(FCBuffState.WaitingForSelectString1);
                }
                else
                {
                    log.Error("[FCBuff] Failed to interact with OIC Quartermaster");
                    SetState(FCBuffState.Failed);
                }
                break;

            case FCBuffState.WaitingForSelectString1:
                if (elapsed < 5)
                {
                    if (GameHelpers.IsAddonVisible("SelectString"))
                    {
                        log.Information("[FCBuff] SelectString appeared, selecting purchase option");
                        GameHelpers.FireAddonCallback("SelectString", true, 0);
                        SetState(FCBuffState.WaitingForExchange);
                    }
                    return;
                }
                log.Error("[FCBuff] SelectString did not appear");
                SetState(FCBuffState.Failed);
                break;

            case FCBuffState.WaitingForExchange:
                if (elapsed < 5)
                {
                    if (GameHelpers.IsAddonVisible("FreeCompanyExchange"))
                    {
                        log.Information("[FCBuff] FreeCompanyExchange appeared, starting purchase loop");
                        buyCount = 0;
                        var config = configManager.GetActiveConfig();
                        buyMax = isSealSweetenerTwo ? config.FCBuffPurchaseAttempts : 1; // Buy configured amount of SS2, only 1 of SS1
                        log.Information($"[FCBuff] Purchase setup: buyMax={buyMax}, isSealSweetenerTwo={isSealSweetenerTwo}, FCBuffPurchaseAttempts={config.FCBuffPurchaseAttempts}");
                        SetState(FCBuffState.PurchasingBuff);
                    }
                    return;
                }
                log.Error("[FCBuff] FreeCompanyExchange did not appear");
                SetState(FCBuffState.Failed);
                break;

            case FCBuffState.PurchasingBuff:
                if (elapsed < 1) return;
                if (buyCount >= buyMax)
                {
                    log.Information($"[FCBuff] Purchase complete: {buyCount} buffs bought");
                    SetState(FCBuffState.ClosingWindows);
                    return;
                }
                
                var buffIndex = isSealSweetenerTwo ? 22 : 5; // 22u for SS2, 5u for SS1
                log.Information($"[FCBuff] Purchasing buff {buyCount + 1}/{buyMax} (index: {buffIndex})");
                GameHelpers.FireAddonCallback("FreeCompanyExchange", false, 2, (uint)buffIndex);
                SetState(FCBuffState.WaitingForPurchaseConfirm);
                break;

            case FCBuffState.WaitingForPurchaseConfirm:
                if (elapsed < 5)
                {
                    if (GameHelpers.IsAddonVisible("SelectYesno"))
                    {
                        log.Information("[FCBuff] Confirming purchase");
                        GameHelpers.FireAddonCallback("SelectYesno", true, 0);
                        buyCount++;
                        SetState(FCBuffState.PurchasingBuff);
                    }
                    return;
                }
                log.Error("[FCBuff] Purchase confirmation did not appear");
                SetState(FCBuffState.Failed);
                break;

            case FCBuffState.ClosingWindows:
                if (elapsed < 0.5) return;
                log.Information("[FCBuff] Closing windows");
                GameHelpers.CloseCurrentAddon();
                if (elapsed > 2)
                {
                    SetState(FCBuffState.Complete);
                }
                break;
        }
    }

    private unsafe int CountSealSweetenerBuffs()
    {
        try
        {
            var addon = RaptureAtkUnitManager.Instance()->GetAddonByName("FreeCompanyAction");
            if (addon == null || !addon->IsVisible)
            {
                log.Error("[FCBuff] FreeCompanyAction addon not found or not visible for buff counting");
                return 0;
            }

            // Switch to Actions tab (index 4)
            GameHelpers.FireAddonCallback("FreeCompany", true, 0, 4);
            
            // Wait a moment for tab switch
            System.Threading.Thread.Sleep(500);
            
            // Count occurrences of specific buff names
            int sealSweetenerCount = 0;
            
            // Navigate the node path: GetNode(1, 10, 14, i, 3)
            for (uint i = 51001; i <= 51016; i++)
            {
                try
                {
                    // Step 1: Get node 1 from addon
                    var node1 = addon->GetNodeById(1);
                    if (node1 == null) continue;
                    
                    // Step 2: Get child node 10 from node 1
                    var node10 = node1->ChildNode;
                    if (node10 == null) continue;
                    
                    // Step 3: Find the actual List Component Node 14 from children of node 10
                    var node14 = node10->ChildNode;
                    bool foundComponent = false;
                    int childIndex = 0;
                    
                    while (node14 != null && childIndex < 50)
                    {
                        if ((int)node14->Type >= 1000)
                        {
                            foundComponent = true;
                            break;
                        }
                        node14 = node14->PrevSiblingNode;
                        childIndex++;
                    }
                    
                    if (!foundComponent || node14 == null) continue;
                    
                    // Step 4: Get list item renderer i from the list component using UldManager.NodeList
                    var componentNode14 = node14->GetAsAtkComponentNode();
                    if (componentNode14 == null) continue;
                    
                    var listComponent = componentNode14->GetComponent();
                    var nodeList = listComponent->UldManager.NodeList;
                    
                    // Find the i-th ListItemRenderer (using index, not buff ID)
                    int listIndex = (int)(i - 51001) + 1;
                    
                    var listItemNode = nodeList[listIndex];
                    if (listItemNode == null) continue;
                    
                    // Step 5: Get text node 3 from the ListItemRenderer using its component
                    var listItemComponent = listItemNode->GetAsAtkComponentNode();
                    if (listItemComponent == null) continue;
                    
                    var listItemComp = listItemComponent->GetComponent();
                    if (listItemComp == null) continue;
                    
                    var textNode = listItemComp->GetTextNodeById(3);
                    if (textNode == null) continue;
                    
                    // Read the text from node 3
                    var text = textNode->NodeText.ToString();
                    
                    // Count Seal Sweetener II occurrences
                    if (text == "Seal Sweetener II")
                    {
                        sealSweetenerCount++;
                    }
                }
                catch (Exception ex)
                {
                    log.Debug($"[FCBuff] Error reading buff {i}: {ex.Message}");
                }
            }
            
            return sealSweetenerCount;
        }
        catch (Exception ex)
        {
            log.Error($"[FCBuff] Error counting Seal Sweetener buffs: {ex.Message}");
            return 0;
        }
    }

    private void SetState(FCBuffState newState)
    {
        log.Information($"[FCBuff] {state} -> {newState}");
        state = newState;
        stateEnteredAt = DateTime.UtcNow;
    }
}
