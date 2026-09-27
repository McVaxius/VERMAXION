using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using VERMAXION.Models;
using VERMAXION.Services;

namespace VERMAXION.Windows;

internal sealed class VerminionWindow : Window
{
    private readonly Plugin plugin;
    private int previewStage;

    public VerminionWindow(Plugin plugin) : base("Lord of Verminion##VermaxionVerminion")
    {
        this.plugin = plugin;
        Size = new Vector2(690, 720);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new(480, 360), MaximumSize = new(1200, 1200) };
    }

    public override void Draw()
    {
        var config = plugin.ConfigManager.GetActiveConfig();
        var service = plugin.VerminionService;
        var loggedIn = Plugin.PlayerState.IsLoaded && !string.IsNullOrEmpty(plugin.ConfigManager.CurrentCharacterKey);
        ImGui.TextWrapped("Run Verminion on the current character independently of Run All. Opening this window does not start a match.");
        if (!loggedIn) ImGui.TextWrapped("Log in and wait for character registration to configure or run Verminion.");
        if (config.VerminionPaused) ImGui.TextWrapped("Paused. Explicit Resume is required, including after a reload.");
        ImGui.TextWrapped(service.StatusText);
        var progress = config.VerminionProgress;
        var stageAttempts = progress.CampaignStage == progress.NextUnclearedChallenge ? progress.CampaignStageAttempts : 0;
        ImGui.TextWrapped($"Campaign: {Math.Min(progress.NextUnclearedChallenge - 1, 24)}/24 sequential stages cleared. Attempts on next uncleared stage: {stageAttempts}/3. Consecutive losses: {progress.ConsecutiveLosses}.");

        ImGui.BeginDisabled(!loggedIn || service.IsActive || service.HasQuestAcquisition);
        if (DrawSettings(config)) plugin.ConfigManager.SaveCurrentAccount();
        ImGui.EndDisabled();
        ImGui.Separator();

        ImGui.BeginDisabled(!loggedIn || plugin.Engine.IsRunning || service.IsActive || service.HasQuestAcquisition || plugin.DadHandoffBlocksNewWork);
        if (ImGui.Button("Run weekly goal")) plugin.RunDashboardAction(service.RunTask);
        ImGui.SameLine();
        if (ImGui.Button("Clear all challenges")) plugin.RunDashboardAction(service.RunChallenges);
        if (config.VerminionPaused)
        {
            ImGui.SameLine();
            if (ImGui.Button("Resume")) plugin.RunDashboardAction(service.ResumeTask);
        }
        ImGui.EndDisabled();
        if (ImGui.Button("FULL STOP")) plugin.FullStop();
        ImGui.TextWrapped("Setup registers inventory minions first. If fewer than three are registered, it can buy only the missing entry minions from the Minion Trader: Mammet #001, Wayward Hatchling and Cherry Bomb, each 2,400 gil. The whole entry plan must fit your remaining cap and leave the minimum gil balance. Stages 23 and 24 require Wind-up Gentleman from Her Last Vow. Select its acquisition action below to hand the ARR Hildibrand chain to Questionable. The full first-entry sequence, other vendor routes and CPU tournaments remain unverified or unavailable.");

        ImGui.Separator();
        var next = VerminionService.PlannedStage(config, progress.CampaignRequested);
        var stage = previewStage == 0 ? Math.Min(next, 24) : previewStage;
        if (ImGui.SliderInt("Preview challenge", ref stage, 1, 24)) previewStage = stage;
        ImGui.SameLine();
        if (ImGui.SmallButton("Follow next stage")) { previewStage = 0; stage = Math.Min(next, 24); }
        DrawPlan(config, stage, loggedIn);
        foreach (var minion in VerminionRoster.Required(stage).Where(minion => VerminionRoster.AcquisitionQuests(minion.Id).Length > 0))
        {
            if (!loggedIn || VerminionGameInteraction.OwnsMinion(minion.Id) != false) continue;
            ImGui.TextWrapped($"Acquire {minion.Name}: Questionable handles the unfinished quests and its configured DAD / FrenRider / ADS duty routes, stopping after the reward quest. Requires WigglyQuest's native priority/stop controls, an empty priority list and AutoRetainer multi mode off. This can be a long quest chain; quest travel spends gil separately from the minion purchase caps. Configure the quest and duty providers before starting.");
            ImGui.TextWrapped("Hildibrand includes three eight-player trials. For a solo run with this stack, configure those duties as unsynced in Questionable and enable FrenRider's eight-player ADS handoff. Check that ADS permits each trial at your chosen maturity threshold. Verminion uses these provider settings; it does not change them or guarantee a trial clear.");
            ImGui.BeginDisabled(plugin.Engine.IsRunning || service.IsActive || service.HasQuestAcquisition || plugin.DadHandoffBlocksNewWork);
            if (ImGui.Button($"Acquire {minion.Name} with Questionable")) plugin.AcquireVerminionMinion(minion.Id);
            ImGui.EndDisabled();
            ImGui.TextWrapped("Verminion stays paused while Questionable runs. Reload observes the handoff without starting it again. FULL STOP cancels the owned acquisition. After the reward, Resume registers the minion and continues the selected Verminion goal.");
        }
        ImGui.TextWrapped("Preview does not select a stage to run. Clear all challenges always starts with the first unfinished stage.");
    }

    internal static bool DrawSettings(CharacterConfig config)
    {
        var changed = false;
        var mode = (int)config.VerminionMode;
        if (ImGui.Combo("Weekly mode##Verminion", ref mode, "Participation - 5 matches\0Win X\0CPU rewards\0"))
        { config.VerminionMode = (VerminionMode)mode; changed = true; }
        ImGui.TextWrapped(config.VerminionMode switch
        {
            VerminionMode.Participation => "Finish the remaining weekly participation matches through intentional CPU losses. Five matches award 27,000 base MGP; existing participation is counted.",
            VerminionMode.WinTarget => "Win on Stage 2 until the weekly victory target is reached. Losses count only toward participation. Stops after three consecutive losses or the attempt limit.",
            _ => "CPU tournament registration, up to 15 NPC matches, and later reward collection are planned. This mode is currently unavailable; weekly completion does not imply tournament completion.",
        });
        if (config.VerminionMode == VerminionMode.CpuRewards && VerminionService.TournamentWorldRequirement() is { } worldRequirement)
            ImGui.TextWrapped(worldRequirement);
        if (config.VerminionMode == VerminionMode.WinTarget)
        {
            var target = config.VerminionVictoryTarget;
            if (ImGui.InputInt("Weekly victories##Verminion", ref target))
            { config.VerminionVictoryTarget = Math.Clamp(target, 1, 1000); changed = true; }
        }
        var gil = (int)Math.Min(config.VerminionGilPurchaseCap, int.MaxValue);
        var mgp = (int)Math.Min(config.VerminionMgpPurchaseCap, int.MaxValue);
        var reserve = (int)Math.Min(config.VerminionGilReserve, int.MaxValue);
        if (ImGui.InputInt("Cumulative gil purchase cap##Verminion", ref gil))
        { config.VerminionGilPurchaseCap = (uint)Math.Max(0, gil); changed = true; }
        if (ImGui.InputInt("Cumulative MGP purchase cap##Verminion", ref mgp))
        { config.VerminionMgpPurchaseCap = (uint)Math.Max(0, mgp); changed = true; }
        if (ImGui.InputInt("Minimum gil balance##Verminion", ref reserve))
        { config.VerminionGilReserve = (uint)Math.Max(0, reserve); changed = true; }
        ImGui.TextWrapped("Each gil purchase must leave the minimum balance. Setup, purchase submission and confirmation all recheck it against current funds.");
        ImGui.TextWrapped($"Spent on this character: {config.VerminionProgress.GilSpent:N0}/{config.VerminionGilPurchaseCap:N0} gil; {config.VerminionProgress.MgpSpent:N0}/{config.VerminionMgpPurchaseCap:N0} MGP. Caps are cumulative and do not reset weekly. Zero prevents purchases. The Minion Trader's three basic gil minions, Nero (30,000 MGP) and Zu Hatchling (10,000 MGP) are supported. Mammet #001, Wayward Hatchling, Nero and Zu Hatchling have verified live purchases.");
        if (config.VerminionProgress.PendingPurchase is { } pending)
            ImGui.TextWrapped($"Unresolved purchase: {pending.Gil:N0} gil / {pending.Mgp:N0} MGP reserved. Further purchases are blocked until both acquisition and currency evidence agree. Reload, FULL STOP and weekly reset keep this reservation.");
        return changed;
    }

    internal static void DrawPlan(CharacterConfig config, int stage, bool showOwnership)
    {
        if (stage is < 1 or > 24) { ImGui.TextWrapped("All campaign challenges are recorded complete."); return; }
        ImGui.Text($"Stage {stage}/24");
        ImGui.TextWrapped(VerminionRoster.Tactics(stage));
        if ((config.VerminionProgress.ClearedChallengeMask & (1u << (stage - 1))) != 0)
            ImGui.TextWrapped("Character clear recorded.");
        ImGui.TextWrapped(stage == 1 ? "Tutorial control has historical runtime evidence." :
            stage == 2 ? "Mammet baseline: ten consecutive live victories verified, with exact stopping at the configured weekly victory target and battlefield control while the game is out of focus." :
            stage == 19 ? "Airship baseline: one live clear verified, including Cargo's ATK buff. Repeated-win reliability remains unverified." :
            stage == 20 ? "Mammet attackers with Wayward Hatchling defenders: one live clear verified. The battle began before the mixed opening was loaded; a fresh start and repeated wins still need verification." :
            stage is 21 or 22 ? "Mammet baseline: one live clear verified, including minimap camera positioning. Repeated-win reliability remains unverified." :
            "Accessible baseline: awaiting fresh runtime verification. Guide suggestions and substitutions are not guaranteed clears.");
        foreach (var minion in VerminionRoster.Required(stage))
        {
            var owned = showOwnership ? VerminionGameInteraction.OwnsMinion(minion.Id) : null;
            ImGui.BulletText($"{minion.Name} - cost {minion.Cost}; {(owned == true ? "registered" : owned == false ? "missing / not registered" : "ownership unknown")}");
            ImGui.TextWrapped(minion.Acquisition);
        }
        if (stage == 23) ImGui.TextWrapped("Campaign prerequisite: register Wind-up Gentleman, rewarded by Her Last Vow (level 50 ARR Hildibrand). Both published guides recommend it for Twintania. Airship, Nero and Zu attempts failed, so the bot no longer buys a substitute for this stage. This guide roster has not yet been verified by the bot.");
        if (stage == 24) ImGui.TextWrapped("Blocked in ordinary runs: tower assignment and circle avoidance are not implemented. The Gentleman roster and one-add targeting come from the linked guides and have no live verification by this bot. A development build with this character's Verminion reload test selected can observe one battle for up to two minutes; that observation does not verify these mechanics.");
        if (showOwnership && VerminionRoster.Missing(stage, VerminionGameInteraction.OwnsMinion) is { } missing)
            ImGui.TextWrapped(missing);
        ImGui.TextWrapped("Entry also requires three registered minions, Gold Saucer access and the preceding challenge clears. Setup checks these in game.");
        if (showOwnership && VerminionGameInteraction.TryReadOwnedMinions(out var registered))
        {
            ImGui.TextWrapped($"Registered minions: {registered.Length} (entry requires 3).");
            var entry = VerminionRoster.EntryPurchases(registered);
            if (entry.Length > 0)
                ImGui.TextWrapped($"After registering inventory items, entry may need {string.Join(", ", entry.Select(offer => offer.Name))} ({entry.Sum(offer => (long)offer.Gil):N0} gil total). Purchases require enough remaining cap and funds.");
        }
        ImGui.TextWrapped("Stage guide: " + VerminionRoster.GuideUrl(stage));
        if (stage is 23 or 24)
            ImGui.TextWrapped("Gentleman-only guide: https://na.finalfantasyxiv.com/lodestone/character/28572768/blog/4629440/");
    }
}
