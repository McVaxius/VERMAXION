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
    private readonly AethertekUI.Dalamud.MaterialWindowLock movementLock = new();
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
        if (VermaxionPresentation.HasMaterial || plugin.BackgroundBlurEnabled)
        {
            ImGui.PushStyleColor(ImGuiCol.WindowBg, Vector4.Zero);
            materialBackdropStylePushed = true;
        }
        motion.Prepare(this, reducedMotion: !plugin.Configuration.UiStylingEnabled, roundedCorners: plugin.Configuration.UiStylingEnabled);
        movementLock.Apply(this, plugin.Configuration.UiWindowsLocked);
    }

    public override void PostDraw()
    {
        movementLock.Restore(this);
        motion.Restore(this);
        ReleaseMaterialBackdropStyle();
        UIConstants.PopStyle();
    }

    private void BeginMaterialContent()
    {
        ReleaseMaterialBackdropStyle();
        plugin.DrawWindowBackdrop(this);
    }

    private void ReleaseMaterialBackdropStyle()
    {
        if (!materialBackdropStylePushed) return;
        ImGui.PopStyleColor();
        materialBackdropStylePushed = false;
    }

    public override void Draw()
    {
        motion.DrawChrome();
        BeginMaterialContent();
        using var structuralBackgrounds = AethertekUI.MaterialSkin.StructuralBackgroundStyle();
        if (MaterialTheme.Current.IsLcars) MaterialLcars.Header(UiText.T(WindowName.Split("##", 2)[0]));
        UIConstants.Heading("Island Sanctuary", plugin.Configuration.CompactUi);
        UiGui.TextWrapped("Island chores · Activity provider: Calypso");
        DrawProfilePicker();
        var accountId = viewedAccountId;
        if (!plugin.ConfigManager.Accounts.TryGetValue(accountId, out var account))
        {
            UiGui.TextWrapped("No character profile is available. Log in and wait for character registration.");
            return;
        }
        var config = string.IsNullOrEmpty(viewedCharacterKey) ? account.DefaultConfig : account.Characters.GetValueOrDefault(viewedCharacterKey);
        var activeProfile = !string.IsNullOrEmpty(viewedCharacterKey) && viewedAccountId == plugin.ConfigManager.CurrentAccountId &&
            viewedCharacterKey == plugin.ConfigManager.CurrentCharacterKey;
        var runtime = activeProfile ? plugin.IslandSanctuaryRun : null;
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
            IslandSanctuaryView.DrawProviderStatus(plugin.IslandSanctuaryRun.ProviderStatus);
            if (activeTab == 1)
            {
                if (IslandSanctuaryView.DrawSettings(config)) plugin.ConfigManager.SaveAccount(accountId);
            }
            else IslandSanctuaryView.DrawRun(config, runtime);
        }
        ImGui.EndChild();
        IslandSanctuaryView.DrawActions(plugin, activeProfile);
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
    internal const string Unavailable = "Workshop agenda and exports are supported. Other chores and island travel are unavailable.";

    internal static void DrawProviderStatus(VERMAXION.IPC.CalypsoIslandStatus? status = null)
        => UIConstants.Status("IslandProvider", status == null ? "Calypso is unavailable or incompatible."
            : status.Busy ? "Calypso is running." : "Calypso is ready.", status == null ? UIConstants.Amber : UIConstants.Metadata);

    internal static bool DrawSettings(CharacterConfig config)
    {
        var changed = false;
        var columns = ImGui.GetContentRegionAvail().X >= 760 * UIConstants.Scale ? 2 : 1;
        if (ImGui.BeginTable("IslandSettingsCards", columns, ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.SizingStretchSame))
        {
            ImGui.TableNextColumn();
            if (UIConstants.BeginPanel("IslandCharacter", "Character automation"))
            {
                var enabled = config.EnableIslandSanctuary;
                if (UIConstants.Checkbox("Enable Island routine", ref enabled)) { config.EnableIslandSanctuary = enabled; changed = true; }
                UiGui.TextDisabled("Default: off for each character.");
                var cadence = (int)config.IslandSanctuary.Cadence;
                UIConstants.SetNextItemWidth(-1);
                if (UIConstants.Combo("Visit cadence", ref cadence, "Weekly\0Daily\0Manual only\0"))
                { config.IslandSanctuary.Cadence = (IslandVisitCadence)cadence; changed = true; }
                UiGui.TextWrapped("Cadence follows Calypso's Island cycle. Manual Run can bypass cadence.");
                UiGui.TextWrapped("Enter your island first. Island travel is unavailable.");
                UIConstants.EndPanel();
            }
            ImGui.TableNextColumn();
            if (UIConstants.BeginPanel("IslandPolicies", "Policies live in Calypso"))
            {
                UiGui.TextWrapped("Choose the chores here. Configure their detailed rules in Calypso.");
                UiGui.TextWrapped("Do not run Calypso separately while this routine is active.");
                UIConstants.EndPanel();
            }
            ImGui.EndTable();
        }
        if (UIConstants.BeginPanel("IslandRoutineSettings", "Island routine"))
        {
            UiGui.TextWrapped(Unavailable);
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
                        ImGui.BeginDisabled(!IslandSanctuarySettings.IsSupported(chore));
                        if (UIConstants.Checkbox(step.Label, ref selected)) { config.IslandSanctuary.SetSelected(chore, selected); changed = true; }
                        ImGui.EndDisabled();
                    }
                    else UiGui.TextWrapped(step.Stage + " · " + UiText.T(step.Label));
                    ImGui.BeginDisabled(step.Chore is not { } detailChore || !IslandSanctuarySettings.IsSupported(detailChore));
                    UiGui.TextWrapped(step.Detail);
                    ImGui.EndDisabled();
                    ImGui.PopID();
                    ImGui.TableNextColumn();
                    UiGui.TextColored(UIConstants.Metadata, step.Capability);
                }
                ImGui.EndTable();
            }
            var selectedSupported = Enum.GetValues<IslandChore>().Count(chore =>
                IslandSanctuarySettings.IsSupported(chore) && config.IslandSanctuary.IsSelected(chore));
            UiGui.TextWrapped(UiText.F("{0}/2 supported chores selected.", selectedSupported));
            UIConstants.EndPanel();
        }
        return changed;
    }

    internal static void DrawRun(CharacterConfig config, IslandSanctuaryRun? run = null)
    {
        if (UIConstants.BeginPanel("IslandRun", "Island routine"))
        {
            UiGui.TextWrapped(run?.StatusText ?? "Island routine not started.");
            UiGui.TextWrapped(UiText.F("Agenda cycle: {0} · Exports cycle: {1}",
                string.IsNullOrEmpty(config.IslandAgendaConfirmedCycle) ? UiText.T("Not confirmed") : config.IslandAgendaConfirmedCycle,
                string.IsNullOrEmpty(config.IslandExportsConfirmedCycle) ? UiText.T("Not confirmed") : config.IslandExportsConfirmedCycle));
            if (ImGui.BeginTable("IslandRunList", 3, ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.BordersInnerH))
            {
                ImGui.TableSetupColumn("Task", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Selection", ImGuiTableColumnFlags.WidthFixed, 110 * UIConstants.Scale);
                ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthStretch);
                UiGui.TableHeadersRow();
                foreach (var step in IslandRoutineStep.Route)
                {
                    ImGui.TableNextRow(); ImGui.TableNextColumn();
                    var supported = step.Chore is { } chore && IslandSanctuarySettings.IsSupported(chore);
                    ImGui.BeginDisabled(!supported);
                    UiGui.TextWrapped(step.Stage + " · " + UiText.T(step.Label));
                    ImGui.EndDisabled();
                    ImGui.TableNextColumn();
                    UiGui.TextDisabled(step.Chore == null ? "Required first" : !supported ? "Unavailable" :
                        config.IslandSanctuary.IsSelected(step.Chore.Value) ? "Selected" : "Not selected");
                    ImGui.TableNextColumn();
                    var status = run?.ProviderStatus;
                    if (step.Chore == null)
                        UiGui.TextDisabled(status?.InSanctuary == true ? "Already on island" : "Travel unavailable");
                    else if (!supported)
                        UiGui.TextDisabled("TBD");
                    else
                    {
                        var task = status?.Task(step.Chore == IslandChore.Agenda ? "workshop.agenda" : "exports.sell");
                        var receiptCycle = step.Chore == IslandChore.Agenda
                            ? config.IslandAgendaConfirmedCycle : config.IslandExportsConfirmedCycle;
                        var confirmed = status != null && VERMAXION.IPC.CalypsoIslandStatus.ValidCycle(status.Cycle) &&
                            (receiptCycle == status.Cycle ||
                             run != null && run.ConfirmedCycle == status.Cycle &&
                             string.Equals(run.OwnerCharacter, status.Character, StringComparison.OrdinalIgnoreCase) &&
                             run.ConfirmedChores.Contains(step.Chore.Value));
                        UiGui.TextWrapped(confirmed
                            ? task?.Reason == "planned today (suggest only)" ? "Planned only" : "Confirmed"
                            : task?.State == "ready" ? "Ready" : task?.State == "running" ? "Running" : "Not confirmed");
                    }
                }
                ImGui.EndTable();
            }
            UiGui.TextWrapped("Provider inactivity or a stop request does not prove the routine completed.");
            UIConstants.EndPanel();
        }
    }

    internal static void DrawActions(Plugin? plugin = null, bool activeProfile = false)
    {
        ImGui.Separator();
        var island = plugin?.IslandSanctuaryRun;
        var eligible = plugin != null && activeProfile && !plugin.Engine.IsRunning &&
            !plugin.DadHandoffBlocksNewWork && !plugin.BeastmasterOwnsWork &&
            island!.Eligibility(plugin.ConfigManager.GetActiveConfig(), plugin.ConfigManager.CurrentCharacterKey, manual: true).IsRunnable;
        ImGui.BeginDisabled(!eligible);
        if (UIConstants.ButtonWithCaption("Run##Island", UiText.T("Run")) && eligible)
            plugin!.RunDashboardAction(() => plugin.Engine.ManualStartIslandSanctuary());
        ImGui.EndDisabled();
        UIConstants.SameLineIfFits(UiText.T("Stop"));
        var canStop = plugin != null && activeProfile && island?.IsActive == true &&
            plugin.Engine.State == VermaxionEngine.EngineState.RunningIslandSanctuary;
        ImGui.BeginDisabled(!canStop);
        if (UIConstants.ButtonWithCaption("Stop##Island", UiText.T("Stop")) && canStop) plugin!.Engine.Cancel();
        ImGui.EndDisabled();
        UIConstants.SameLineIfFits(UiText.T("Open Calypso"));
        ImGui.BeginDisabled();
        UIConstants.ButtonWithCaption("Open Calypso##Island", UiText.T("Open Calypso") + " · " + UiText.T("TBD"));
        ImGui.EndDisabled();
        if (!activeProfile) UiGui.TextWrapped("Run and Stop use the active character only.");
        else if (plugin != null && !eligible && !island!.IsActive)
            UiGui.TextWrapped(island.Eligibility(plugin.ConfigManager.GetActiveConfig(), plugin.ConfigManager.CurrentCharacterKey, manual: true).Reason);
    }
}
