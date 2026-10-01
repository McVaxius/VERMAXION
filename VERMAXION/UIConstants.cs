using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

namespace VERMAXION;

public static class UIConstants
{
    public static float Scale => ImGuiHelpers.GlobalScale;
    // Applied before Begin, so window padding and child/table density agree across every surface.
    public static void PushStyle(bool compact)
    {
        var scale = ImGuiHelpers.GlobalScale;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(compact ? 8 : 16, compact ? 6 : 12) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(compact ? 6 : 10, compact ? 2 : 5) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(compact ? 6 : 10, compact ? 3 : 8) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemInnerSpacing, new Vector2(compact ? 4 : 8, compact ? 3 : 6) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(compact ? 5 : 10, compact ? 3 : 7) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 4f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.TabRounding, 4f * scale);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.055f, 0.095f, 0.115f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.065f, 0.115f, 0.135f, 1f));
        ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(0.075f, 0.135f, 0.155f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.95f, 0.96f, 1f));
        ImGui.PushStyleColor(ImGuiCol.TextDisabled, new Vector4(0.57f, 0.7f, 0.73f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.19f, 0.32f, 0.35f, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.095f, 0.19f, 0.215f, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.13f, 0.29f, 0.32f, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0.15f, 0.35f, 0.38f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.095f, 0.29f, 0.31f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.13f, 0.4f, 0.42f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.16f, 0.47f, 0.49f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.09f, 0.25f, 0.28f, 1f));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0.13f, 0.36f, 0.39f, 1f));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(0.16f, 0.43f, 0.46f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Tab, new Vector4(0.075f, 0.18f, 0.205f, 1f));
        ImGui.PushStyleColor(ImGuiCol.TabHovered, new Vector4(0.13f, 0.36f, 0.39f, 1f));
        ImGui.PushStyleColor(ImGuiCol.TabActive, new Vector4(0.1f, 0.29f, 0.32f, 1f));
        ImGui.PushStyleColor(ImGuiCol.CheckMark, new Vector4(0.36f, 0.83f, 0.76f, 1f));
        ImGui.PushStyleColor(ImGuiCol.TableHeaderBg, new Vector4(0.085f, 0.21f, 0.24f, 1f));
        ImGui.PushStyleColor(ImGuiCol.TableRowBgAlt, new Vector4(0.14f, 0.3f, 0.32f, 0.16f));
    }

    public static void PopStyle()
    {
        ImGui.PopStyleColor(21);
        ImGui.PopStyleVar(9);
    }

    public static void Heading(string text, bool compact = false)
    {
        if (!compact) ImGui.Spacing();
        ImGui.PushTextWrapPos(0f);
        ImGui.TextColored(new Vector4(0.4f, 0.85f, 0.79f, 1f), text);
        ImGui.PopTextWrapPos();
        ImGui.Separator();
    }

    public static void SameLineIfFits(string nextLabel)
    {
        var width = ImGui.CalcTextSize(nextLabel).X + ImGui.GetStyle().FramePadding.X * 2;
        var remaining = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X - ImGui.GetItemRectMax().X;
        if (remaining >= width + ImGui.GetStyle().ItemSpacing.X)
            ImGui.SameLine();
    }

    public static class ConfigLabels
    {
        // Global Settings
        public const string AutoWidthMainTaskColumns = "Auto width the columns";
        public const string KrangleNames = "Krangle Names";
        public const string AutoRestoreRetainerCheckingAfterWork = "Keep current/previous characters enabled in AutoRetainer";
        public const string EnableCharacterSelectStallRecovery = "Recover stalled character select";
        public const string DtrBarEntry = "DTR Bar Entry";
        
        // Character Settings
        public const string Enabled = "Character automation enabled";
        public const string FCBuffRefill = "FC Buff Refill (Seal Sweetener)";
        public const string AllowFCBuffActivation = "Allow VERMAXION to activate Seal Sweetener II";
        public const string MaintainFCBuffStockTarget = "Maintain configured Seal Sweetener II stock target";
        public const string MaxPurchaseAttempts = "Purchase quantity / stock target";
        public const string MinFCPoints = "Min FC Points";
        public const string MinGil = "Min Gil";
        public const string MinionRoulette = "Minion Roulette";
        public const string SeasonalGearRoulette = "Seasonal Gear Roulette";
        public const string GearUpdater = "Gear Updater";
        public const string VerminionQueue = "Lord of Verminion";
        public const string JumboCactpot = "Jumbo Cactpot (auto DC timing)";
        public const string MiniCactpot = "Mini Cactpot";
        public const string ChocoboRacing = "Chocobo Racing";
        public const string LootGoblinMapGather = "LootGoblin Map Gather";
        public const string NagYourMom = "nag your mom";
        public const string NagYourMomCasualCc = "Casual CC";
        public const string NagYourMomFrontline = "Frontline";
        public const string NagYourMomRivalWings = "Rival Wings";
        public const string NagYourMomRunsPerDay = "CC runs per day";
        public const string NagYourMomFrontlineRunsPerDay = "Frontline runs per day";
        public const string NagYourMomRivalWingsRunsPerDay = "Rival Wings runs per day";
        public const string NagYourMomJob = "mom job";
        public const string NagYourMomWindowStartLocal = "Local start (HH:mm)";
        public const string NagYourMomWindowEndLocal = "Local end (HH:mm)";
        public const string NagYourMomStopAtSeriesRank25 = "Stop at series rank 25";
        public const string NagYourDad = "nag your dad";
        public const string MiscCmd = "Misc Cmd";
        public const string NagYourDadDungeonCount = "dad dungeon count";
        public const string NagYourDadDungeonFrequency = "dad dungeon frequency";
        public const string NagYourDadDungeonName = "dad dungeon";
        public const string NagYourDadDungeonJob = "dad dungeon job";
        public const string NagYourDadQueueViaLanParty = "QUEUE via LAN PARTY module";
        public const string NagYourDadDungeonUnsynced = "Run dungeon unsynced";
        public const string NagYourDadDailyMsq = "Run daily MSQ via LAN Party";
        public const string NagYourDadLanPartyPreset = "LAN Party preset";
        public const string NagYourDadCommendationAttempts = "Commendation attempts";
        public const string NagYourDadAstropeAttempts = "Astrope attempts";
        public const string NagYourDadWindowStartLocal = "Astrope local start (HH:mm)";
        public const string NagYourDadWindowEndLocal = "Astrope local end (HH:mm)";
        public const string RacesPerDay = "Races Per Day";
        public const string SkipChocoboRacingIfLevel50 = "Don't race if racing chocobo is rank 50";
        
        // Section Headers
        public const string GlobalSettings = "Global Settings";
        public const string EveryARPostProcess = "Every AR PostProcess";
        public const string WeeklyTasks = "Weekly Tasks";
        public const string DailyTasks = "Daily Tasks";
        public const string VariableTimeTasks = "Variable time tasks";
        public const string WipTasks = "WIP tasks";
        
        // Other Labels
        public const string Account = "Account:";
        public const string Characters = "Characters";
        public const string Settings = "Settings";
        public const string NewCharactersInheritThese = "New characters inherit these settings";
        public const string AccountAlias = "Account Alias:";
        public const string Save = "Save";
    }
    
    public static class Tooltips
    {
        public const string AutoWidthMainTaskColumns = "Automatically fit the compact ★, When, Type, and Actions columns while Task uses the remaining space. Turn this off to drag the dividers; Dalamud saves and restores the manual widths for both All Tasks and Favorites.";
        public const string KrangleNames = "Replace character names with exercise words for screenshots";
        public const string AutoRestoreRetainerCheckingAfterWork = "Restore and persist AutoRetainer checking for the current and immediately previous character whenever either is disabled. Turn this off before intentionally deselecting either character.";
        public const string EnableCharacterSelectStallRecovery = "After five minutes waiting at character select during a VERMAXION fishing relog, attempt to load the first live character once. The same guard also controls the status test button.";
        public const string MinionRoulette = "Fire off /minion roulette once per AR postprocess";
        public const string SeasonalGearRoulette = "Randomly equip seasonal event gear for a fun ensemble each AR run";
        public const string GearUpdater = "Updates valid saved gearsets through optional Stylist IPC or the native recommended-equipment path. If none exist, first runs the bounded missing-gearset bootstrap.";
        public const string NagYourMom = "AR-only mom task with a local time window and per-route daily completed-match counts. At the end time, withdraw a waiting queue or finish the current match, then complete mom for this cycle. Requires mom queue-deadline support.";
        public const string NagYourDad = "AR-only DAD launch. Select one live saved DAD preset or schedule; VERMAXION tracks and cancels that exact scheduler job, planner request, or schedule run.";
        public const string MiscCmd = "Sends startup cleanup commands at the start of AutoRetainer and manual VERMAXION runs.";
    }
}
