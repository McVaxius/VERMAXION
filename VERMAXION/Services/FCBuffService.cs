using System;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using Dalamud.Game.ClientState.Objects.SubKinds;

namespace VERMAXION.Services;

public class FCBuffService
{
    private readonly ICommandManager commandManager;
    private readonly IPluginLog log;
    private readonly IGameGui gameGui;

    private FCBuffState state = FCBuffState.Idle;
    private DateTime stateEnteredAt = DateTime.MinValue;
    private int purchaseAttempts = 0;
    private int maxPurchaseAttempts = 15;

    // Status ID 414 = Seal Sweetener (I or II) from FUTA_GC.lua
    private const uint SealSweetenerStatusId = 414;

    public enum FCBuffState
    {
        Idle,
        CheckingStatus,
        OpeningFCWindow,
        TryingToCastBuff,
        NavigatingToQuartermaster,
        PurchasingBuff,
        ClosingWindows,
        Complete,
        Failed,
    }

    public FCBuffState State => state;
    public bool IsActive => state != FCBuffState.Idle && state != FCBuffState.Complete && state != FCBuffState.Failed;
    public bool IsComplete => state == FCBuffState.Complete;
    public bool IsFailed => state == FCBuffState.Failed;

    public FCBuffService(ICommandManager commandManager, IPluginLog log, IGameGui gameGui)
    {
        this.commandManager = commandManager;
        this.log = log;
        this.gameGui = gameGui;
    }

    public void Start(int maxAttempts)
    {
        maxPurchaseAttempts = maxAttempts;
        purchaseAttempts = 0;
        SetState(FCBuffState.CheckingStatus);
        log.Information($"[FCBuff] Starting FC buff check (max {maxAttempts} purchase attempts)");
    }

    public void Reset()
    {
        SetState(FCBuffState.Idle);
    }

    public unsafe void Update()
    {
        if (state == FCBuffState.Idle || state == FCBuffState.Complete || state == FCBuffState.Failed)
            return;

        var elapsed = (DateTime.UtcNow - stateEnteredAt).TotalSeconds;

        switch (state)
        {
            case FCBuffState.CheckingStatus:
                // Check if we're in an FC
                var fcProxy = InfoProxyFreeCompany.Instance();
                if (fcProxy == null || fcProxy->Id == 0)
                {
                    log.Information("[FCBuff] Not in an FC - skipping buff refill");
                    SetState(FCBuffState.Complete);
                    return;
                }

                // TODO: Check if Seal Sweetener is already active
                // Status ID 414 = Seal Sweetener (from FUTA_GC.lua: GetStatusTimeRemaining(414))
                // Need to check via character StatusManager: chara->GetStatusManager()
                // For now, always proceed to try to apply/buy the buff
                log.Information("[FCBuff] Checking Seal Sweetener status (stub - always proceeds)");

                SetState(FCBuffState.OpeningFCWindow);
                break;

            case FCBuffState.OpeningFCWindow:
                if (elapsed < 1) return; // Wait 1s settle time

                // Open FC window to check FC points and try to cast buff
                // From FUTA_GC.lua: /freecompanycmd
                log.Information("[FCBuff] Opening FC window");
                commandManager.ProcessCommand("/freecompanycmd");
                SetState(FCBuffState.TryingToCastBuff);
                break;

            case FCBuffState.TryingToCastBuff:
                if (elapsed < 2) return; // Wait for addon to open

                // From FUTA_GC.lua pattern:
                // 1. Click Actions tab: /callback FreeCompany false 0 4u
                // 2. Search FreeCompanyAction addon for "Seal Sweetener II"
                // 3. Cast it: /callback FreeCompanyAction false 1 {index}u
                // 4. Confirm context menu: /callback ContextMenu true 0 0 1u 0 0
                // 5. Confirm yes: /callback SelectYesno true 0

                // TODO: Implement actual addon interaction
                // For now, this is a stub - needs in-game testing to verify addon node structure
                log.Information("[FCBuff] TODO: FC Action addon interaction not yet implemented");
                log.Information("[FCBuff] Attempting to navigate to OIC Quartermaster for purchase");
                SetState(FCBuffState.NavigatingToQuartermaster);
                break;

            case FCBuffState.NavigatingToQuartermaster:
                if (elapsed < 2) return;

                // From FUTA_GC.lua:
                // /li gc  (Lifestream to GC headquarters)
                // Target "OIC Quartermaster"
                // /interact
                // /callback SelectString true 0 (twice)
                // Then buy from FreeCompanyExchange
                log.Information("[FCBuff] Navigating to GC via Lifestream");

                // Close any open FC windows first
                commandManager.ProcessCommand("/freecompanycmd");

                // Use Lifestream to go to GC
                try
                {
                    commandManager.ProcessCommand("/li gc");
                }
                catch (Exception ex)
                {
                    log.Warning($"[FCBuff] Lifestream command failed: {ex.Message}");
                    log.Warning("[FCBuff] Lifestream may not be installed");
                }

                SetState(FCBuffState.PurchasingBuff);
                break;

            case FCBuffState.PurchasingBuff:
                if (elapsed < 8) return; // Wait for Lifestream teleport

                purchaseAttempts++;
                if (purchaseAttempts > maxPurchaseAttempts)
                {
                    log.Warning($"[FCBuff] Max purchase attempts ({maxPurchaseAttempts}) reached");
                    SetState(FCBuffState.Failed);
                    return;
                }

                // From FUTA_GC.lua:
                // Target "OIC Quartermaster"
                // /interact
                // /callback SelectString true 0  (select first option)
                // /callback SelectString true 0  (select first option again)
                // /callback FreeCompanyExchange false 2 22u  (Seal Sweetener II, index 22)
                // /callback SelectYesno true 0  (confirm purchase)
                // Repeat buymax times

                // TODO: This needs proper implementation with addon waits
                // For now, fire the commands and hope for the best
                log.Information($"[FCBuff] TODO: Purchase interaction not yet implemented (attempt {purchaseAttempts}/{maxPurchaseAttempts})");
                log.Warning("[FCBuff] FC buff purchase needs addon interaction research");
                SetState(FCBuffState.ClosingWindows);
                break;

            case FCBuffState.ClosingWindows:
                if (elapsed < 1) return;
                log.Information("[FCBuff] Closing windows and completing");
                SetState(FCBuffState.Complete);
                break;
        }
    }

    private void SetState(FCBuffState newState)
    {
        log.Debug($"[FCBuff] State: {state} -> {newState}");
        state = newState;
        stateEnteredAt = DateTime.UtcNow;
    }
}
