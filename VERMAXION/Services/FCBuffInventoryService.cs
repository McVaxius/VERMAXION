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
        OpeningFCWindow,
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
                log.Information("[FCBuffInventory] Opening FC command window");
                commandManager.ProcessCommand("/freecompanycmd");
                SetState(FCBuffInventoryState.WaitingForFCCommand);
                break;

            case FCBuffInventoryState.WaitingForFCCommand:
                if (elapsed.TotalSeconds < 1) return;
                log.Information("[FCBuffInventory] Opening FC window");
                GameHelpers.FireAddonCallback("FreeCompanyTopics", false, -2);
                SetState(FCBuffInventoryState.WaitingForFCWindow);
                break;

            case FCBuffInventoryState.OpeningFCWindow:
                if (elapsed.TotalSeconds < 1) return;
                log.Information("[FCBuffInventory] Opening FC action window");
                GameHelpers.FireAddonCallback("FreeCompany", true, 0, 4);
                SetState(FCBuffInventoryState.WaitingForFCWindow);
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

            log.Information("[FCBuffInventory] FC Action window opened successfully");
            log.Information("[FCBuffInventory] TODO: Implement buff reading from nodes 51001-51016");
            log.Information("[FCBuffInventory] For now, manually check the window for buff counts");
            
            // TODO: Implement proper node traversal to read buff counts
            // The path from Jaksuhn's SND: GetNode(1, 10, 14, i, 3) where i = 51001 to 51016
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
