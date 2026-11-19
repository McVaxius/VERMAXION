using System;
using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Inventory;
using Dalamud.Game.Text.SeStringHandling;
using VERMAXION.Services;

namespace VERMAXION;

// The utility is manual-only; it has no automation-catalog or saved configuration entry.
public sealed partial class Plugin
{
    internal void OpenCofferUtility() { if (!CofferOpening.IsDisposed) CofferOpeningWindow.IsOpen = true; }

    internal string? CofferOpeningBlocker()
    {
        if (!ClientState.IsLoggedIn || !PlayerState.IsLoaded || !IsCharacterRegistered)
            return "Waiting for the current character to be registered and ready.";
        if (DadHandoffBlocksNewWork || OfflineLogoutBlocksOrdinaryAutomation ||
            Condition[ConditionFlag.InCombat] || Condition[ConditionFlag.BetweenAreas] ||
            Condition[ConditionFlag.BetweenAreas51] || Condition[ConditionFlag.BoundByDuty] ||
            Condition[ConditionFlag.BoundByDuty56] || Condition[ConditionFlag.InDutyQueue] ||
            Condition[ConditionFlag.WaitingForDutyFinder])
            return "Another automation or activity owns the character.";
        var automation = BuildAutomationStatus();
        if (automation.IsBusy && automation.State != "CofferOpening")
            return "Another automation or activity owns the character.";
        if (PluginInterface.InstalledPlugins.Any(plugin => plugin.InternalName == "AutoRetainer" && plugin.IsLoaded))
        {
            var ar = AutoRetainerIPC.ReadBusyState();
            if (!ar.Success || ar.Busy) return "AutoRetainer is busy or its state is unavailable.";
        }
        return null;
    }

    internal CofferOpeningReply StartCoffers(uint itemId, int quantity = 0)
    {
        if (CofferOpening.IsDisposed) return new(false, "", "Coffer utility is unloaded.", CofferOpening.Status);
        if (CofferOpening.IsActive) return new(false, "", CofferOpening.Status.Summary, CofferOpening.Status);
        var blocker = CofferOpeningBlocker();
        if (blocker != null) return new(false, "", blocker, CofferOpening.Status);
        CofferOpeningReply result = new(false, "", "Coffer item use is unavailable.", CofferOpening.Status);
        RunDashboardAction(() => result = CofferOpening.Start(itemId, quantity));
        return result;
    }

    private void OnCofferMenuOpened(IMenuOpenedArgs args)
    {
        if (args.MenuType != ContextMenuType.Inventory || args.Target is not MenuTargetInventory target ||
            target.TargetItem is not { } item || item.Quantity == 0 || item.Quantity > int.MaxValue ||
            item.IsHq || item.ContainerType is not (GameInventoryType.Inventory1 or GameInventoryType.Inventory2 or
                GameInventoryType.Inventory3 or GameInventoryType.Inventory4) ||
            !NativeCofferInventory.IsEligible(item.ItemId)) return;
        var itemId = item.ItemId;
        var quantity = (int)item.Quantity;
        // Draw callbacks own the UI translation scope; this native callback does not.
        var language = UiText.Languages.Any(value => value.Code == Configuration.UiLanguage) ? Configuration.UiLanguage : "en";
        var caption = new System.Resources.ResourceManager("VERMAXION.Localization.Strings_" + language.Replace('-', '_'), typeof(UiText).Assembly)
            .GetString("Open coffer stack", System.Globalization.CultureInfo.InvariantCulture) ?? "Open coffer stack";
        args.AddMenuItem(new MenuItem
        {
            Name = new SeStringBuilder().AddText(caption).Build(),
            OnClicked = click =>
            {
                _ = Framework.RunOnFrameworkThread(() =>
                {
                    var result = StartCoffers(itemId, quantity);
                    OpenCofferUtility();
                    if (!result.Accepted) ChatGui.Print($"[Vermaxion] {result.Reason}");
                });
            },
        });
    }
}
