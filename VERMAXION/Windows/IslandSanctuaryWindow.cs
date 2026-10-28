using System;
using System.Linq;
using System.Numerics;
using AethertekUI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using VERMAXION.Models;
using VERMAXION.Services;

namespace VERMAXION.Windows;

internal sealed class IslandSanctuaryWindow : Window
{
    private readonly Plugin plugin;
    private readonly AethertekUI.Dalamud.MaterialWindowMotion motion = new();
    private bool materialBackdropStylePushed;
    private string viewedAccountId = "";
    private string viewedCharacterKey = "";
    private bool selectSettings;
    private int activeTab;

    public IslandSanctuaryWindow(Plugin plugin) : base("Island Sanctuary##VermaxionIslandSanctuary")
    {
        this.plugin = plugin;
        Size = new(1060, 850);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new() { MinimumSize = new(520, 500), MaximumSize = new(float.MaxValue, float.MaxValue) };
    }

    public override void OnOpen() { if (!selectSettings) SelectProfile(false); }

    internal void Open(bool settings = false)
    {
        selectSettings = settings;
        IsOpen = true;
        SelectProfile(settings);
    }

    private void SelectProfile(bool selected)
    {
        var manager = plugin.ConfigManager;
        viewedAccountId = selected ? manager.SelectedAccountId : manager.CurrentAccountId;
        viewedCharacterKey = selected ? manager.SelectedCharacterKey : manager.CurrentCharacterKey;
        if (!manager.Accounts.ContainsKey(viewedAccountId))
        {
            viewedAccountId = manager.SelectedAccountId;
            viewedCharacterKey = manager.SelectedCharacterKey;
        }
    }

    public override void PreDraw()
    {
        UIConstants.PushStyle(plugin.Configuration.CompactUi);
        if (VermaxionPresentation.HasMaterial)
        {
            ImGui.PushStyleColor(ImGuiCol.WindowBg, Vector4.Zero);
            materialBackdropStylePushed = true;
        }
        motion.Prepare(this, reducedMotion: false, roundedCorners: true);
    }

    public override void PostDraw()
    {
        motion.Restore(this);
        ReleaseMaterialBackdropStyle();
        UIConstants.PopStyle();
    }

    private void BeginMaterialContent()
    {
        ReleaseMaterialBackdropStyle();
        MaterialSkin.Backdrop();
    }

    private void ReleaseMaterialBackdropStyle()
    {
        if (!materialBackdropStylePushed) return;
        ImGui.PopStyleColor();
        materialBackdropStylePushed = false;
    }

    public override void Draw()
    {
        BeginMaterialContent();
        motion.DrawChrome();
        if (MaterialTheme.Current.IsLcars) MaterialLcars.Header(UiText.T(WindowName.Split("##", 2)[0]));
        UIConstants.Heading("Island Sanctuary", plugin.Configuration.CompactUi);
        UiGui.TextWrapped("Cowrie routine · Activity provider: Calypso · TBD");
        DrawProfilePicker();
        var accountId = viewedAccountId;
        if (!plugin.ConfigManager.Accounts.TryGetValue(accountId, out var account))
        {
            UiGui.TextWrapped("No character profile is available. Log in and wait for character registration.");
            return;
        }
        var config = string.IsNullOrEmpty(viewedCharacterKey) ? account.DefaultConfig : account.Characters.GetValueOrDefault(viewedCharacterKey);
        if (config == null) return;
        using (MaterialText.PushLineHeight(new[] { UiText.T("Run"), UiText.T("Settings") }))
        {
            if (ImGui.BeginTabBar("IslandTabs", ImGuiTabBarFlags.FittingPolicyScroll))
            {
                if (UiGui.BeginTabItem("Run")) { activeTab = 0; ImGui.EndTabItem(); }
                if (UiGui.BeginTabItem("Settings", selectSettings ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None))
                { activeTab = 1; ImGui.EndTabItem(); }
                ImGui.EndTabBar();
            }
        }
        selectSettings = false;
        var bodyHeight = Math.Max(80 * UIConstants.Scale, ImGui.GetContentRegionAvail().Y - 95 * UIConstants.Scale);
        if (ImGui.BeginChild("IslandBody", new(0, bodyHeight), false))
        {
            IslandSanctuaryView.DrawProviderStatus();
            if (activeTab == 1)
            {
                if (IslandSanctuaryView.DrawSettings(config)) plugin.ConfigManager.SaveAccount(accountId);
            }
            else IslandSanctuaryView.DrawRun(config);
        }
        ImGui.EndChild();
        IslandSanctuaryView.DrawActions();
    }

    private string Identity(string value) => plugin.Configuration.KrangleEnabled ? KrangleService.KrangleName(value) : value;

    private void DrawProfilePicker()
    {
        UiGui.TextUnformatted("Character");
        ImGui.SetNextItemWidth(Math.Min(600 * UIConstants.Scale, ImGui.GetContentRegionAvail().X));
        var label = string.IsNullOrEmpty(viewedCharacterKey) ? UiText.T("Account default") : Identity(viewedCharacterKey);
        if (!UIConstants.BeginCombo("##IslandProfile", label)) return;
        foreach (var (accountId, account) in plugin.ConfigManager.Accounts)
        {
            var alias = Identity(string.IsNullOrEmpty(account.AccountAlias) ? accountId : account.AccountAlias);
            Choice("", UiText.T("Account default") + " · " + alias);
            foreach (var key in account.Characters.Keys) Choice(key, Identity(key) + " · " + alias);
            void Choice(string key, string caption)
            {
                if (!UiGui.Selectable("###IslandProfile/" + accountId + "/" + key,
                        viewedAccountId == accountId && viewedCharacterKey == key, caption)) return;
                viewedAccountId = accountId;
                viewedCharacterKey = key;
            }
        }
        ImGui.EndCombo();
    }
}

// Rendering saved preferences is independent of provider and gameplay services.
internal static class IslandSanctuaryView
{
    internal const string Unavailable = "Calypso integration TBD. Run, Stop and Open Calypso are unavailable. No schedule is created.";

    internal static void DrawProviderStatus() => UIConstants.Status("IslandProvider", Unavailable, UIConstants.Amber);

    internal static bool DrawSettings(CharacterConfig config)
    {
        var changed = false;
        var columns = ImGui.GetContentRegionAvail().X >= 760 * UIConstants.Scale ? 2 : 1;
        if (ImGui.BeginTable("IslandSettingsCards", columns, ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.SizingStretchSame))
        {
            ImGui.TableNextColumn();
            if (UIConstants.BeginPanel("IslandCharacter", "This character"))
            {
                var enabled = config.EnableIslandSanctuary;
                if (UIConstants.Checkbox("Enable Island routine", ref enabled)) { config.EnableIslandSanctuary = enabled; changed = true; }
                UiGui.TextDisabled("Default: off for each character.");
                var cadence = (int)config.IslandSanctuary.Cadence;
                UIConstants.SetNextItemWidth(-1);
                if (UIConstants.Combo("Visit cadence", ref cadence, "Weekly\0Daily\0Manual only\0"))
                { config.IslandSanctuary.Cadence = (IslandVisitCadence)cadence; changed = true; }
                UiGui.TextWrapped("Saved preference only. Coverage and reset timing remain TBD; no visit is scheduled.");
                UiGui.TextWrapped("Next visit: TBD");
                UIConstants.EndPanel();
            }
            ImGui.TableNextColumn();
            if (UIConstants.BeginPanel("IslandPolicies", "Policies live in Calypso"))
            {
                UiGui.TextWrapped("Choose the chores here. Configure their detailed rules in Calypso.");
                foreach (var policy in new[] { "Agenda & overwrite", "Expedition assignments", "Crop & pasture care", "Material reserves", "Seed stock & spending", "Cowrie limits" })
                    UiGui.BulletText(policy);
                UIConstants.EndPanel();
            }
            ImGui.EndTable();
        }
        if (UIConstants.BeginPanel("IslandRoutineSettings", "Proposed routine"))
        {
            UiGui.TextWrapped("Both workshop actions share one visit. Refill seeds only when low. All execution remains TBD.");
            if (ImGui.BeginTable("IslandRoutineList", 2, ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.BordersInnerH))
            {
                var capabilityWidth = new[] { "TBD", "Partial", "Existing" }.Max(label => MaterialText.Measure(UiText.T(label)).X);
                ImGui.TableSetupColumn("Chore", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Capability", ImGuiTableColumnFlags.WidthFixed, capabilityWidth + 12 * UIConstants.Scale);
                foreach (var step in IslandRoutineStep.Route)
                {
                    ImGui.TableNextRow(); ImGui.TableNextColumn();
                    ImGui.PushID("IslandChore/" + step.Chore);
                    if (step.Chore is { } chore)
                    {
                        var selected = config.IslandSanctuary.IsSelected(chore);
                        ImGui.AlignTextToFramePadding();
                        UiGui.TextUnformatted(step.Stage + " ·");
                        ImGui.SameLine();
                        if (UIConstants.Checkbox(step.Label, ref selected)) { config.IslandSanctuary.SetSelected(chore, selected); changed = true; }
                    }
                    else UiGui.TextWrapped(step.Stage + " · " + UiText.T(step.Label));
                    UiGui.TextWrapped(step.Detail);
                    ImGui.PopID();
                    ImGui.TableNextColumn();
                    UiGui.TextColored(UIConstants.Metadata, step.Capability);
                }
                ImGui.EndTable();
            }
            UiGui.TextWrapped(UiText.F("{0}/7 chores selected. Execution TBD.", IslandRoutineStep.Route.Count(step => step.Chore is { } chore && config.IslandSanctuary.IsSelected(chore))));
            UIConstants.EndPanel();
        }
        return changed;
    }

    internal static void DrawRun(CharacterConfig config)
    {
        if (UIConstants.BeginPanel("IslandConstruction", "Construction splash / artwork TBD"))
        {
            UiGui.TextWrapped("Meteor watching mammets build. Reserved area only; no artwork supplied.");
            ImGui.Dummy(new(0, (UIConstants.Compact ? 20 : 44) * UIConstants.Scale));
            UIConstants.EndPanel();
        }
        if (UIConstants.BeginPanel("IslandRun", "Routine not started"))
        {
            UiGui.TextWrapped(config.EnableIslandSanctuary ? "Island routine selected. Execution remains TBD." : "Island routine off");
            UiGui.TextWrapped("Last completed: Unknown · Next visit: TBD · Chores confirmed: Unknown");
            if (ImGui.BeginTable("IslandRunList", 3, ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.BordersInnerH))
            {
                var selectionWidth = new[] { "Required first", "Only if low", "Selected", "Not selected" }.Max(label => MaterialText.Measure(UiText.T(label)).X);
                ImGui.TableSetupColumn("Task", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Selection", ImGuiTableColumnFlags.WidthFixed, selectionWidth + 12 * UIConstants.Scale);
                ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, Math.Max(60 * UIConstants.Scale, MaterialText.Measure(UiText.T("Status")).X));
                UiGui.TableHeadersRow();
                foreach (var step in IslandRoutineStep.Route)
                {
                    ImGui.TableNextRow(); ImGui.TableNextColumn();
                    UiGui.TextWrapped(step.Stage + " · " + UiText.T(step.Label));
                    ImGui.TableNextColumn();
                    UiGui.TextDisabled(step.Chore is not { } chore ? "Required first" : config.IslandSanctuary.IsSelected(chore)
                        ? chore == IslandChore.Seeds ? "Only if low" : "Selected" : "Not selected");
                    ImGui.TableNextColumn();
                    UiGui.TextDisabled("TBD");
                }
                ImGui.EndTable();
            }
            UiGui.TextWrapped("Provider inactivity or a stop request does not prove the routine completed.");
            UIConstants.EndPanel();
        }
    }

    internal static void DrawActions()
    {
        ImGui.Separator();
        ImGui.BeginDisabled();
        var run = UiText.T("Run") + " · " + UiText.T("TBD");
        var stop = UiText.T("Stop") + " · " + UiText.T("TBD");
        var open = UiText.T("Open Calypso") + " · " + UiText.T("TBD");
        UIConstants.ButtonWithCaption("Run##Island", run);
        UIConstants.SameLineIfFits(stop);
        UIConstants.ButtonWithCaption("Stop##Island", stop);
        UIConstants.SameLineIfFits(open);
        UIConstants.ButtonWithCaption("Open Calypso##Island", open);
        ImGui.EndDisabled();
        UiGui.TextWrapped(Unavailable);
    }
}
