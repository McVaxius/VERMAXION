using System;
using System.Collections.Generic;
using Dalamud.Game;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using static FFXIVClientStructs.FFXIV.Client.UI.RaptureAtkUnitManager;

namespace VERMAXION.Services;

public class FCBuffInventoryService
{
    private readonly ICommandManager commandManager;
    private readonly IPluginLog log;
    private readonly IGameGui gameGui;

    public enum FCBuffInventoryState
    {
        Idle,
        OpeningFCCommand,
        WaitingForFCCommand,
        WaitingForFCWindow,
        ReadingBuffs,
        Complete,
        Failed
    }

    private FCBuffInventoryState state = FCBuffInventoryState.Idle;
    private DateTime stateEnteredAt = DateTime.MinValue;

    public FCBuffInventoryService.FCBuffInventoryState State => state;
    public bool IsActive => state != FCBuffInventoryState.Idle && state != FCBuffInventoryState.Complete && state != FCBuffInventoryState.Failed;
    public bool IsComplete => state == FCBuffInventoryState.Complete;
    public bool IsFailed => state == FCBuffInventoryState.Failed;
    public string StatusText => state.ToString();

    public FCBuffInventoryService(ICommandManager commandManager, IPluginLog log, IGameGui gameGui)
    {
        this.commandManager = commandManager;
        this.log = log;
        this.gameGui = gameGui;
    }

    public void Start()
    {
        if (IsActive)
        {
            log.Warning("[FCBuffInventory] Service already active");
            return;
        }

        log.Information("[FCBuffInventory] Starting FC buff inventory check");
        SetState(FCBuffInventoryState.OpeningFCCommand);
    }

    public void Stop()
    {
        if (!IsActive) return;
        
        log.Information("[FCBuffInventory] Stopping FC buff inventory check");
        SetState(FCBuffInventoryState.Failed);
    }

    public void Update()
    {
        if (!IsActive) return;

        var elapsed = DateTime.UtcNow - stateEnteredAt;

        switch (state)
        {
            case FCBuffInventoryState.OpeningFCCommand:
                if (elapsed.TotalSeconds < 1) return;
                log.Information("[FCBuffInventory] Opening FC window: /freecompanycmd");
                CommandHelper.SendCommand("/freecompanycmd");
                SetState(FCBuffInventoryState.WaitingForFCCommand);
                break;

            case FCBuffInventoryState.WaitingForFCCommand:
                if (elapsed.TotalSeconds < 3)
                {
                    // Check if FC window is ready
                    if (GameHelpers.IsAddonVisible("FreeCompany"))
                    {
                        log.Information("[FCBuffInventory] FC window is ready, switching to Actions tab");
                        log.Information("[FCBuffInventory] [Callback] Firing FreeCompany with args (true, 0, 4)");
                        GameHelpers.FireAddonCallback("FreeCompany", true, 0, 4);
                        SetState(FCBuffInventoryState.WaitingForFCWindow);
                    }
                    return;
                }
                // Try opening again if first attempt failed
                if (elapsed.TotalSeconds < 6)
                {
                    log.Information("[FCBuffInventory] First attempt failed, trying again");
                    CommandHelper.SendCommand("/freecompanycmd");
                    SetState(FCBuffInventoryState.WaitingForFCCommand);
                    return;
                }
                log.Error("[FCBuffInventory] Failed to open FC window");
                SetState(FCBuffInventoryState.Failed);
                break;

            case FCBuffInventoryState.WaitingForFCWindow:
                if (elapsed.TotalSeconds < 2) return;
                if (GameHelpers.IsAddonVisible("FreeCompanyAction"))
                {
                    log.Information("[FCBuffInventory] FC Action window appeared, reading buffs");
                    SetState(FCBuffInventoryState.ReadingBuffs);
                }
                else if (elapsed.TotalSeconds > 5)
                {
                    log.Error("[FCBuffInventory] FC Action window did not appear");
                    SetState(FCBuffInventoryState.Failed);
                }
                break;

            case FCBuffInventoryState.ReadingBuffs:
                if (elapsed.TotalSeconds < 1) return;
                ReadBuffsFromWindow();
                SetState(FCBuffInventoryState.Complete);
                break;
        }
    }

    private unsafe void ReadBuffsFromWindow()
    {
        try
        {
            var addon = Instance()->GetAddonByName("FreeCompanyAction");
            if (addon == null || !addon->IsVisible)
            {
                log.Error("[FCBuffInventory] FreeCompanyAction addon not found or not visible");
                return;
            }

            log.Information("[FCBuffInventory] Reading FC buff inventory:");
            
            var buffNames = new Dictionary<uint, string>
            {
                { 51001, "Seal Sweetener I" },
                { 51002, "Seal Sweetener II" },
                { 51003, "Seal Sweetener III" },
                { 51004, "FC Heat of Battle I" },
                { 51005, "FC Heat of Battle II" },
                { 51006, "FC Heat of Battle III" },
                { 51007, "FC Stand Stand I" },
                { 51008, "FC Stand Stand II" },
                { 51009, "FC Stand Stand III" },
                { 51010, "FC Up in Arms I" },
                { 51011, "FC Up in Arms II" },
                { 51012, "FC Up in Arms III" },
                { 51013, "FC Back on Your Feet I" },
                { 51014, "FC Back on Your Feet II" },
                { 51015, "FC Back on Your Feet III" },
                { 51016, "FC Sprawling Synthetics" }
            };

            // Using the path from Jaksuhn's SND: GetNode(1, 10, 14, i, 3)
            // This translates to: addon->GetNodeById(1)->PrevSiblingNode->PrevSiblingNode->GetNodeById(i)->GetComponent()->GetTextNodeById(3)
            for (uint i = 51001; i <= 51016; i++)
            {
                try
                {
                    // Navigate the node path using FUTA_GC method: GetNode(1, 10, 14, i, 3)
                    // CORRECTED: Each comma is a child of the previous, not sibling
                    // Step 1: Get node 1 from addon
                    var node1 = addon->GetNodeById(1);
                    if (node1 == null)
                    {
                        log.Debug($"[FCBuffInventory] Node 1 not found for buff {i}");
                        continue;
                    }
                    log.Debug($"[FCBuffInventory] Found node 1, type: {node1->Type}");
                    
                    // Step 2: Get first child of node 1 (assuming this is node 10)
                    var node10 = node1->ChildNode;
                    if (node10 == null)
                    {
                        log.Debug($"[FCBuffInventory] Node 1 has no children for buff {i}");
                        continue;
                    }
                    log.Debug($"[FCBuffInventory] Found child of node 1 (node 10), type: {node10->Type}");
                    
                    // Step 3: Get the i-th child of node 10 (this is node 14 for slot i)
                    // We need to navigate to the i-th position in the children list
                    var buffNode = node10->ChildNode;
                    int slotIndex = (int)(i - 51001); // Convert buff ID to 0-based index (51001 = 0, 51002 = 1, etc.)
                    int currentIndex = 0;
                    
                    while (buffNode != null && currentIndex < slotIndex)
                    {
                        buffNode = buffNode->PrevSiblingNode;
                        currentIndex++;
                    }
                    
                    if (buffNode == null)
                    {
                        log.Debug($"[FCBuffInventory] Slot {slotIndex} (buff {i}) not found in children of node 10");
                        if (buffNames.TryGetValue(i, out var buffName))
                        {
                            log.Information($"[FCBuffInventory] {i:D5}: {buffName}: [Slot not found]");
                            commandManager.ProcessCommand($"/echo {i:D5}: {buffName}: [Slot not found]");
                        }
                        continue;
                    }
                    log.Debug($"[FCBuffInventory] Found slot {slotIndex} (buff {i}), type: {buffNode->Type}");
                    
                    // Step 4: Get child node 14 (text node) from the buff slot
                    var textNode = buffNode->ChildNode;
                    if (textNode == null || textNode->Type != NodeType.Text)
                    {
                        log.Warning($"[FCBuffInventory] No text node (node 14) found in slot {slotIndex} for buff {i}");
                        if (buffNames.TryGetValue(i, out var buffName))
                        {
                            log.Information($"[FCBuffInventory] {i:D5}: {buffName}: [No text node]");
                            commandManager.ProcessCommand($"/echo {i:D5}: {buffName}: [No text node]");
                        }
                        continue;
                    }
                    log.Debug($"[FCBuffInventory] Found text node (node 14) in slot {slotIndex}, type: {textNode->Type}");
                    
                    // Step 5: Get child node 3 from text node 14 (this should contain the actual text)
                    var contentNode = textNode->ChildNode;
                    if (contentNode == null || contentNode->Type != NodeType.Text)
                    {
                        log.Warning($"[FCBuffInventory] No content node (node 3) found in text node for buff {i}");
                        // Try reading directly from textNode instead
                        var textNodePtr = (FFXIVClientStructs.FFXIV.Component.GUI.AtkTextNode*)textNode;
                        if (textNodePtr != null && textNodePtr->NodeText.StringPtr != null)
                        {
                            var text = textNodePtr->NodeText.ToString();
                            log.Information($"[FCBuffInventory] SUCCESS: {i:D5} - Read from text node: '{text}'");
                            
                            if (buffNames.TryGetValue(i, out var buffName))
                            {
                                log.Information($"[FCBuffInventory] {i:D5}: {buffName}: {text}");
                                commandManager.ProcessCommand($"/echo {i:D5}: {buffName}: {text}");
                            }
                        }
                        else
                        {
                            if (buffNames.TryGetValue(i, out var buffName))
                            {
                                log.Information($"[FCBuffInventory] {i:D5}: {buffName}: [No content]");
                                commandManager.ProcessCommand($"/echo {i:D5}: {buffName}: [No content]");
                            }
                        }
                        continue;
                    }
                    
                    // Read the text from content node 3
                    var contentNodePtr = (FFXIVClientStructs.FFXIV.Component.GUI.AtkTextNode*)contentNode;
                    if (contentNodePtr != null && contentNodePtr->NodeText.StringPtr != null)
                    {
                        var text = contentNodePtr->NodeText.ToString();
                        log.Information($"[FCBuffInventory] SUCCESS: {i:D5} - Read from content node: '{text}'");
                        
                        if (buffNames.TryGetValue(i, out var buffName))
                        {
                            log.Information($"[FCBuffInventory] {i:D5}: {buffName}: {text}");
                            commandManager.ProcessCommand($"/echo {i:D5}: {buffName}: {text}");
                        }
                    }
                    else
                    {
                        log.Warning($"[FCBuffInventory] Content node 3 has no text for buff {i}");
                        if (buffNames.TryGetValue(i, out var buffName))
                        {
                            log.Information($"[FCBuffInventory] {i:D5}: {buffName}: [No text content]");
                            commandManager.ProcessCommand($"/echo {i:D5}: {buffName}: [No text content]");
                        }
                    }
                }
                catch (Exception ex)
                {
                    log.Debug($"[FCBuffInventory] Error reading buff {i}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            log.Error($"[FCBuffInventory] Error reading buffs: {ex.Message}");
        }
    }

    
    
    private void SetState(FCBuffInventoryState newState)
    {
        if (state == newState) return;
        
        log.Debug($"[FCBuffInventory] {state} -> {newState}");
        state = newState;
        stateEnteredAt = DateTime.UtcNow;
    }
}
