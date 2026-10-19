using System;
using System.IO;
using System.Linq;
using System.Numerics;
using AethertekUI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using VERMAXION.Beastmaster;
using VERMAXION.Models;
using VERMAXION.Services;

namespace VERMAXION.Windows;

internal sealed partial class BeastmasterWindow : Window
{
    private const int SplashCount = BeastmasterSplashRotation.Count;
    private readonly AethertekUI.Dalamud.MaterialWindowMotion motion = new();
    private readonly Plugin plugin;
    private readonly BeastmasterSplashRotation splashes = new();
    private string viewedAccountId = "";
    private string viewedCharacterKey = "";
    private bool viewingAccountDefault;
    private CharacterConfig? ViewedConfig => plugin.ConfigManager.Accounts.TryGetValue(viewedAccountId, out var account)
        ? viewingAccountDefault ? account.DefaultConfig : account.Characters.GetValueOrDefault(viewedCharacterKey) : null;
    private BeastmasterCharacterData CharacterData => ViewedConfig!.Beastmaster ??= new();
    private BeastmasterSettings Settings => plugin.GetBeastmasterSettings(ViewedConfig!);
    private string DisplayIdentity(string value) => plugin.Configuration.KrangleEnabled ? KrangleService.KrangleName(value) : value;
    private string PlayingCharacterKey
    {
        get
        {
            var player = Plugin.ObjectTable.LocalPlayer;
            if (!Plugin.ClientState.IsLoggedIn || player == null || player.HomeWorld.RowId == 0) return "";
            var name = player.Name.ToString();
            var world = player.HomeWorld.Value.Name.ToString();
            return string.IsNullOrEmpty(name) || string.IsNullOrEmpty(world) ? "" : $"{name}@{world}";
        }
    }
    private bool ViewingCurrent => !viewingAccountDefault && Plugin.PlayerState.IsLoaded && plugin.IsCharacterRegistered &&
        string.Equals(viewedAccountId, plugin.ConfigManager.CurrentAccountId, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(viewedCharacterKey, plugin.ConfigManager.CurrentCharacterKey, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(viewedCharacterKey, PlayingCharacterKey, StringComparison.OrdinalIgnoreCase);
    private bool EditingLocked => plugin.BeastmasterOwnsWork && ViewingCurrent;
    private bool selectScheduleTab;
    private bool focusWeeklyTask;
    private bool scheduleTabActive;
    private int activeTab;
    private void SaveSettings()
    {
        if (ViewedConfig != null) plugin.ConfigManager.SaveAccount(viewedAccountId);
    }

    internal void SelectCurrentCharacter()
    {
        var registered = Plugin.ClientState.IsLoggedIn && plugin.IsCharacterRegistered;
        viewedAccountId = registered ? plugin.ConfigManager.CurrentAccountId : "";
        viewedCharacterKey = registered ? plugin.ConfigManager.CurrentCharacterKey : "";
        viewingAccountDefault = false;
        editorRenameTarget = null;
        editorPositionTarget = null;
        beastsListMessage = suppliesListMessage = "";
        if (ViewedConfig != null || Plugin.ClientState.IsLoggedIn) return;
        foreach (var account in plugin.ConfigManager.Accounts)
            foreach (var character in account.Value.Characters)
                if (character.Value.Beastmaster?.HasRecordedBeasts == true)
                {
                    viewedAccountId = account.Key;
                    viewedCharacterKey = character.Key;
                    return;
                }
    }

    internal void OpenSchedule(bool weekly)
    {
        if (!IsOpen) { IsOpen = true; SelectCurrentCharacter(); }
        var manager = plugin.ConfigManager;
        if (manager.Accounts.TryGetValue(manager.SelectedAccountId, out var account) &&
            (string.IsNullOrEmpty(manager.SelectedCharacterKey) || account.Characters.ContainsKey(manager.SelectedCharacterKey)))
        {
            viewedAccountId = manager.SelectedAccountId;
            viewedCharacterKey = manager.SelectedCharacterKey;
            viewingAccountDefault = string.IsNullOrEmpty(manager.SelectedCharacterKey);
        }
        editorRenameTarget = null;
        editorPositionTarget = null;
        beastsListMessage = suppliesListMessage = "";
        selectScheduleTab = true;
        focusWeeklyTask = weekly;
        focusSchedulePanel = true;
    }

    public BeastmasterWindow(Plugin plugin)
        : base("VERMAXION · Beastmaster##VermaxionBeastmaster", ImGuiWindowFlags.None)
    {
        this.plugin = plugin;
        Size = new Vector2(1120, 880);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(480, 480),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
        AddRunTitlebarActions(this);
    }

    public override void OnOpen()
    {
        splashes.Open(Environment.TickCount64);
        if (!selectScheduleTab) SelectCurrentCharacter();
        plugin.TryRefreshBeastmasterOwnership();
    }

    internal void OnMiniOpen()
    {
        SelectCurrentCharacter();
        plugin.TryRefreshBeastmasterOwnership();
    }

    public override void PreDraw()
    {
        UIConstants.PushStyle(plugin.Configuration.CompactUi);
        motion.Prepare(this, reducedMotion: false, roundedCorners: true);
    }

    public override void PostDraw()
    {
        motion.Restore(this);
        UIConstants.PopStyle();
    }

    public override void Draw()
    {
        motion.DrawChrome();
        if (ViewedConfig == null && plugin.IsCharacterRegistered) SelectCurrentCharacter();
        DrawShellBanner();
        DrawCharacterPicker();
        DrawTabHeaders();
        var ready = plugin.CanStartBeastmaster(out var blocker);
        DrawRunControls(ready, blocker);

        var bodyHeight = Math.Max(100 * UIConstants.Scale, ImGui.GetContentRegionAvail().Y);
        if (ImGui.BeginChild("BeastmasterBody", new Vector2(0, bodyHeight), false))
        {
            if (ViewedConfig == null) UiGui.TextWrapped("No character profile is available. Log in and wait for character registration.");
            else switch (activeTab)
            {
                case 0: DrawRun(); break;
                case 1: DrawBeasts(); break;
                case 2: DrawSupplies(); break;
                case 3: DrawSchedule(); break;
            }
        }
        ImGui.EndChild();
    }

    private void DrawTabHeaders()
    {
        if (ViewedConfig == null) return;
        using var tabStyle = new MaterialStyleScope();
        tabStyle.Color(ImGuiCol.Tab, Vector4.Zero);
        tabStyle.Color(ImGuiCol.TabActive, Vector4.Zero);
        tabStyle.Color(ImGuiCol.TabUnfocused, Vector4.Zero);
        tabStyle.Color(ImGuiCol.TabUnfocusedActive, Vector4.Zero);
        using var lineHeight = MaterialText.PushLineHeight(new[] { "Run", "Beasts", "Supplies", "VMX Schedule" }.Select(UiText.T).ToArray());
        if (!ImGui.BeginTabBar("BeastmasterTabs", ImGuiTabBarFlags.FittingPolicyScroll)) return;
        Tab("Run", 0);
        Tab("Beasts", 1);
        Tab("Supplies", 2);
        Tab("VMX Schedule###BeastmasterScheduleTab", 3, selectScheduleTab ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None);
        selectScheduleTab = false;
        scheduleTabActive = activeTab == 3;
        ImGui.EndTabBar();

        void Tab(string label, int index, ImGuiTabItemFlags flags = ImGuiTabItemFlags.None)
        {
            if (!UiGui.BeginTabItem(label, flags)) return;
            activeTab = index;
            DrawShellTabUnderline();
            ImGui.EndTabItem();
        }
    }

    internal void DrawMiniContents(BeastmasterSplashRotation rotation)
    {
        if (ViewedConfig == null && plugin.IsCharacterRegistered) SelectCurrentCharacter();
        DrawShellBanner(rotation, mini: true);
        DrawCharacterPicker();
        var ready = plugin.CanStartBeastmaster(out var blocker);
        DrawRunControls(ready, blocker, mini: true);
        if (ViewedConfig != null)
        {
            if (!ViewingCurrent) UiGui.TextDisabled(UiText.F("Saved data for {0}", DisplayIdentity(viewedCharacterKey)));
            var settings = ViewingCurrent && plugin.BeastmasterOwnsWork ? plugin.BeastmasterService.Settings : Settings;
            using (UiText.Font(UiFontRole.BodyStrong)) UiGui.TextWrapped(RunBoardName(settings.Board));
            UiGui.TextWrapped(settings.Goal == BeastmasterRunGoal.TrainOwned ? "Train owned familiars" : "Fixed board runs");
            UiGui.TextWrapped(TeamMethodCaption(settings));
            DrawTrainingProgress(settings, "Mini");
        }
        if (!ViewingCurrent && !string.IsNullOrEmpty(PlayingCharacterKey))
            UiGui.TextDisabled(UiText.F("Playing: {0}", DisplayIdentity(PlayingCharacterKey)));
        var service = plugin.BeastmasterService;
        if (service.RouteStepCount > 0)
            UiGui.TextDisabled(UiText.F("Route {0:00} · {1}/{2} steps", service.EffectiveBoard, service.RouteStep, service.RouteStepCount));
    }

    private static void DrawShellTabUnderline()
    {
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        ImGui.GetWindowDrawList().AddLine(new Vector2(min.X, max.Y), max, ImGui.GetColorU32(UIConstants.Blue), 2 * UIConstants.Scale);
    }

    private void DrawShellBanner(BeastmasterSplashRotation? rotation = null, bool mini = false)
    {
        var compact = plugin.Configuration.CompactUi;
        var show = mini ? plugin.Configuration.BeastmasterMiniSplashVisible : plugin.Configuration.BeastmasterSplashVisible;
        var expanded = mini ? plugin.Configuration.BeastmasterMiniSplashExpanded : plugin.Configuration.BeastmasterSplashExpanded;
        var selectedRotation = rotation ?? splashes;
        var width = ImGui.GetContentRegionAvail().X;
        var index = selectedRotation.Current(Environment.TickCount64);
        var texture = Plugin.TextureProvider.GetFromFile(Path.Combine(Plugin.PluginInterface.AssemblyLocation.DirectoryName ?? "",
            $"beastmaster-splash-{index + 1:00}-v{(index < 10 ? 2 : 1)}.jpg")).GetWrapOrEmpty();
        var height = show ? expanded && texture.Width > 0 && texture.Height > 0 ? width * texture.Height / texture.Width :
            Math.Clamp(width / (compact ? 5.8f : 4.8f), 92 * UIConstants.Scale, (compact ? 150 : 210) * UIConstants.Scale) :
            ImGui.GetFrameHeightWithSpacing() * (width < 720 * UIConstants.Scale ? 3 : 2) + 10 * UIConstants.Scale;
        var min = ImGui.GetCursorScreenPos();
        var max = min + new Vector2(width, height);
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(min, max, ImGui.GetColorU32(UIConstants.Panel));
        if (show && texture.Handle.Handle != 0 && texture.Width > 0 && texture.Height > 0)
        {
            // Cover the fixed banner without distorting the approved illustration.
            var visibleHeight = Math.Min(1f, height / width * texture.Width / texture.Height);
            var visibleWidth = Math.Min(1f, width / height * texture.Height / texture.Width);
            var uvMin = new Vector2((1 - visibleWidth) / 2, (1 - visibleHeight) / 2);
            draw.AddImage(texture.Handle, min, max, uvMin, Vector2.One - uvMin);
        }
        var shade = ImGui.ColorConvertFloat4ToU32(new Vector4(0.01f, .06f, .08f, .72f));
        var clear = ImGui.ColorConvertFloat4ToU32(Vector4.Zero);
        draw.AddRectFilledMultiColor(min, max, shade, clear, clear, shade);
        draw.PushClipRect(min, max, true);
        try
        {
            using (UiText.Font(UiFontRole.Title))
            {
                var callerScale = ImGuiP.GetCurrentWindow().FontWindowScale;
                var text = UiText.T("BEASTMASTER");
                var factor = Math.Min(1f, width * (show ? .44f : .35f) / Math.Max(1, MaterialText.Measure(text).X));
                ImGui.SetWindowFontScale(callerScale * factor);
                try
                {
                    MaterialText.AddText(draw, min + new Vector2(18 * UIConstants.Scale, height * (!show && width < 720 * UIConstants.Scale ? .5f : .36f)),
                        ImGui.ColorConvertFloat4ToU32(Vector4.One), text);
                }
                finally { ImGui.SetWindowFontScale(callerScale); }
            }
            using (UiText.Font(UiFontRole.BodyStrong))
            {
                var title = UiText.T("THE CREWCIBLE");
                MaterialText.AddText(draw, min + new Vector2(22 * UIConstants.Scale, height * (!show && width < 720 * UIConstants.Scale ? .8f : .7f)),
                    ImGui.ColorConvertFloat4ToU32(Vector4.One), title);
            }
        }
        finally { draw.PopClipRect(); }
        var count = UiText.F("{0:00} / {1:00}", index + 1, SplashCount);
        var expandLabel = expanded ? "Collapse" : "Expand";
        var showLabel = show ? "Hide" : "Show";
        var controlsWidth = new[] { "<", ">", expandLabel, showLabel }.Sum(label => UIConstants.ButtonWidth(label)) +
            MaterialText.Measure(count).X + ImGui.GetStyle().ItemSpacing.X * 4 + 12 * UIConstants.Scale;
        ImGui.SetCursorScreenPos(new Vector2(Math.Max(min.X + 4 * UIConstants.Scale, max.X - controlsWidth), min.Y + 6 * UIConstants.Scale));
        ImGui.PushID(mini ? "MiniSplash" : "FullSplash");
        using (var controls = MaterialControls.Push(MaterialControlContext.Toolbar))
        {
            if (UIConstants.Button("<")) selectedRotation.Previous(Environment.TickCount64);
            ImGui.SameLine();
            if (UIConstants.Button(">")) selectedRotation.Next(Environment.TickCount64);
            ImGui.SameLine();
            if (UIConstants.Button(expandLabel))
            {
                if (mini) plugin.Configuration.BeastmasterMiniSplashExpanded = !expanded;
                else plugin.Configuration.BeastmasterSplashExpanded = !expanded;
                plugin.Configuration.Save();
            }
            ImGui.SameLine();
            if (UIConstants.Button(showLabel))
            {
                if (mini) plugin.Configuration.BeastmasterMiniSplashVisible = !show;
                else plugin.Configuration.BeastmasterSplashVisible = !show;
                plugin.Configuration.Save();
            }
            ImGui.SameLine();
            MaterialText.Text(count);
        }
        ImGui.PopID();
        ImGui.SetCursorScreenPos(min);
        ImGui.Dummy(new Vector2(width, height));
    }

    private void DrawCharacterPicker()
    {
        UiGui.TextUnformatted("Character");
        UIConstants.SameLineIfFits("Current character", Math.Min(350 * UIConstants.Scale, ImGui.GetContentRegionAvail().X));
        ImGui.SetNextItemWidth(Math.Min(380 * UIConstants.Scale, ImGui.GetContentRegionAvail().X));
        var playingCharacter = PlayingCharacterKey;
        if (UIConstants.BeginCombo("##BeastmasterCharacter", ViewedConfig == null
                ? string.IsNullOrEmpty(playingCharacter) ? UiText.T("No recorded character") : DisplayIdentity(playingCharacter)
                : viewingAccountDefault ? UiText.T("Account default") : DisplayIdentity(viewedCharacterKey)))
        {
            foreach (var account in plugin.ConfigManager.Accounts)
                foreach (var character in account.Value.Characters)
                {
                    if (character.Value.Beastmaster?.HasRecordedBeasts != true) continue;
                    var label = DisplayIdentity(character.Key) + " · " + DisplayIdentity(string.IsNullOrEmpty(account.Value.AccountAlias) ? account.Key : account.Value.AccountAlias);
                    if (!UiGui.Selectable("###BeastmasterProfile" + account.Key + "/" + character.Key,
                            string.Equals(viewedAccountId, account.Key, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(viewedCharacterKey, character.Key, StringComparison.OrdinalIgnoreCase), label)) continue;
                    viewedAccountId = account.Key;
                    viewedCharacterKey = character.Key;
                    viewingAccountDefault = false;
                    editorRenameTarget = null;
                    editorPositionTarget = null;
                    beastsListMessage = suppliesListMessage = "";
                }
            ImGui.EndCombo();
        }
        var state = viewingAccountDefault ? "Account default" : ViewingCurrent ? "Playing" :
            Plugin.ClientState.IsLoggedIn && !plugin.IsCharacterRegistered ? "Waiting" : "Offline";
        UIConstants.SameLineIfFits(state);
        UiGui.TextColored(ViewingCurrent ? VermaxionPresentation.Ready : UIConstants.Metadata, state);
        if (!ViewingCurrent && !string.IsNullOrEmpty(playingCharacter))
            UiGui.TextDisabled(UiText.F("Playing: {0}", DisplayIdentity(playingCharacter)));
        EditorTooltip("Only characters with recorded beasts are listed.");
    }

    private void RunForViewedProfile(bool weekly, bool scheduled)
        => plugin.RunBeastmasterForCurrentCharacter(weekly, scheduled);

    private void DrawRunControls(bool ready, string blocker, bool mini = false)
    {
        ImGui.Separator();
        var service = plugin.BeastmasterService;
        var status = BuildRunDetail(ready, blocker, includeGoal: false);
        if (scheduleTabActive && !mini && !plugin.BeastmasterOwnsWork) status += "\n" + UiText.T("Settings saved automatically");
        UIConstants.WrappedText(service.IsPaused || service.State == BeastmasterService.RunState.Recovering ? UIConstants.Amber : service.State == BeastmasterService.RunState.Failed
            ? VermaxionPresentation.Error : ready || service.IsActive ? VermaxionPresentation.Ready : UIConstants.Metadata, status, translate: false);
        var goal = RunGoalSummary();
        if (goal.Length > 0) UIConstants.WrappedText(UIConstants.Metadata, goal, translate: false);
        ImGui.BeginDisabled(!Plugin.ClientState.IsLoggedIn);
        using (var startStyle = new MaterialStyleScope())
        {
            var colors = MaterialTheme.Current.Colors;
            startStyle.Color(ImGuiCol.Button, colors.Primary);
            startStyle.Color(ImGuiCol.ButtonHovered, MaterialColor.Layer(colors.Primary, colors.OnPrimary, .08f));
            startStyle.Color(ImGuiCol.ButtonActive, MaterialColor.Layer(colors.Primary, colors.OnPrimary, .14f));
            startStyle.Color(ImGuiCol.Border, colors.Primary);
            startStyle.Color(ImGuiCol.Text, colors.OnPrimary);
            if (UIConstants.ButtonWithCaption("Start##Beastmaster", UiText.T(PrimaryRunAction)))
                RunForViewedProfile(weekly: false, scheduled: false);
        }
        ImGui.EndDisabled();
        UIConstants.SameLineIfFits("Pause");
        ImGui.BeginDisabled(!service.IsActive || service.IsPaused ||
            service.State is BeastmasterService.RunState.Idle or BeastmasterService.RunState.Complete or BeastmasterService.RunState.Failed);
        if (UIConstants.Button("Pause##Beastmaster")) service.Pause();
        ImGui.EndDisabled();
        UIConstants.SameLineIfFits("Stop");
        if (UIConstants.Button("Stop##Beastmaster")) plugin.StopBeastmaster();
        var stopNextLabel = service.StopAfterNextOutcome ? "Cancel stop after next outcome" : "Stop after next wipe or clear";
        UIConstants.SameLineIfFits(stopNextLabel);
        ImGui.BeginDisabled(!service.CanStopAfterNextOutcome && !service.StopAfterNextOutcome);
        if (UIConstants.ButtonWithCaption("StopAfterNextOutcome##Beastmaster", UiText.T(stopNextLabel)))
            service.ToggleStopAfterNextOutcome();
        ImGui.EndDisabled();
        var viewLabel = mini ? "Full window" : "Mini view";
        UIConstants.SameLineIfFits(viewLabel);
        if (UIConstants.Button(viewLabel + "##BeastmasterView"))
        {
            if (mini) IsOpen = true;
            else plugin.BeastmasterMiniWindow.IsOpen = true;
        }
        var fullStopWidth = UIConstants.ButtonWidth("FULL STOP") + 28 * UIConstants.Scale;
        UIConstants.SameLineIfFits("FULL STOP", fullStopWidth);
        if (UIConstants.FullStopButton(new Vector2(fullStopWidth, 0))) plugin.FullStop();
    }

    private partial void DrawBeasts();
    private partial void DrawSupplies();
}
