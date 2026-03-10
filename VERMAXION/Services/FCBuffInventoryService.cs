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
                    
                    // Step 2: Get child node 10 from node 1
                    var node10 = node1->ChildNode;
                    if (node10 == null)
                    {
                        log.Debug($"[FCBuffInventory] Node 10 (child of 1) not found for buff {i}");
                        continue;
                    }
                    log.Debug($"[FCBuffInventory] Found node 10 (child of 1), type: {node10->Type}");
                    
                    // Step 3: Get child node 14 from node 10
                    var node14 = node10->ChildNode;
                    if (node14 == null)
                    {
                        log.Debug($"[FCBuffInventory] Node 14 (child of 10) not found for buff {i}");
                        continue;
                    }
                    log.Debug($"[FCBuffInventory] Found node 14 (child of 10), type: {node14->Type}");
                    
                    // Step 4: Get child node i from node 14 (node i is a Res node)
                    var nodeI = node14->ChildNode;
                    int childIndex = 0;
                    
                    while (nodeI != null && childIndex < (i - 51001))
                    {
                        nodeI = nodeI->PrevSiblingNode;
                        childIndex++;
                    }
                    
                    if (nodeI == null)
                    {
                        log.Debug($"[FCBuffInventory] Node {i} (child of 14) not found");
                        continue;
                    }
                    log.Debug($"[FCBuffInventory] Found node {i} (child of 14), type: {nodeI->Type}");
                    
                    // Step 5: Get child node 3 from node i (node 3 is a Text node)
                    var node3 = nodeI->ChildNode;
                    if (node3 == null || node3->Type != NodeType.Text)
                    {
                        log.Warning($"[FCBuffInventory] Node 3 (child of {i}) not found or not text type");
                        continue;
                    }
                    log.Debug($"[FCBuffInventory] Found node 3 (child of {i}), type: {node3->Type}");
                    
                    // Read the text from node 3 using the exact same pattern as FC points
                    var textNode = (FFXIVClientStructs.FFXIV.Component.GUI.AtkTextNode*)node3;
                    var text = textNode->NodeText.ToString();
                    
                    log.Information($"[FCBuffInventory] SUCCESS: {i:D5} - Read from node 3: '{text}'");
                    commandManager.ProcessCommand($"/echo {i:D5}: {text}");
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
