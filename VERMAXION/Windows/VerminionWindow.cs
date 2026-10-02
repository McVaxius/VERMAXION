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
    private bool showPlannedMinions;

    public VerminionWindow(Plugin plugin) : base("Lord of Verminion##VermaxionVerminion", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        this.plugin = plugin;
        Size = new Vector2(760, 680);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new(520, 480), MaximumSize = new(float.MaxValue, float.MaxValue) };
    }

    public override void PreDraw() => UIConstants.PushStyle(plugin.Configuration.CompactUi);
    public override void PostDraw() => UIConstants.PopStyle();

    public override void Draw()
    {
        var config = plugin.ConfigManager.GetActiveConfig();
        var service = plugin.VerminionService;
        var loggedIn = Plugin.PlayerState.IsLoaded && !string.IsNullOrEmpty(plugin.ConfigManager.CurrentCharacterKey);
        UIConstants.Heading("Lord of Verminion", plugin.Configuration.CompactUi);
        ImGui.TextWrapped("Current character: " + (loggedIn ? plugin.ConfigManager.CurrentCharacterKey : "unavailable"));
        var progress = config.VerminionProgress;

        var cleared = Enumerable.Range(0, 24).Count(stage => (progress.ClearedChallengeMask & (1u << stage)) != 0);
        ImGui.TextWrapped($"Permanent CPU campaign: {cleared}/24 cleared");
        ImGui.ProgressBar(cleared / 24f, new Vector2(-1, 20f * UIConstants.Scale), $"{cleared}/24");
        if (UIConstants.FullStopButton()) plugin.FullStop();
        ImGui.BeginDisabled(!loggedIn || plugin.Engine.IsRunning || service.IsActive || service.HasQuestAcquisition || plugin.DadHandoffBlocksNewWork);
        if (config.VerminionPaused || progress.RunMode != null)
        {
            UIConstants.SameLineIfFits(progress.CampaignRequested || progress.RunIsCampaign ? "Resume CPU campaign" : "Resume");
            if (UIConstants.Button(progress.CampaignRequested || progress.RunIsCampaign ? "Resume CPU campaign" : "Resume")) plugin.RunDashboardAction(service.ResumeTask);
        }
        ImGui.EndDisabled();
        var stageAttempts = progress.CampaignStage == progress.NextUnclearedChallenge ? progress.CampaignStageAttempts : 0;
        var status = service.StatusText;
        if (!loggedIn) status += "\nLog in and wait for character registration to configure or run Verminion.";
        if (config.VerminionPaused) status += "\nPaused. Explicit Resume is required, including after a reload.";
        status += $"\nNext stage attempts: {stageAttempts}/3 · Losses: {progress.ConsecutiveLosses} · Weekly: {progress.WeeklyMatches}/5 matches; {progress.WeeklyWins} wins.";
        if (progress.RunMode == VerminionMode.MissionRepeat && !progress.RunIsCampaign)
            status += $"\nMission {progress.RunMission}: {progress.RunWins}/{progress.RunRepeatCount} clears; attempts {progress.RunAttempts}/{progress.RunAttemptLimit}.";
        UIConstants.Status("VerminionStatus", status, UIConstants.Metadata);
        var warnings = new System.Collections.Generic.List<string>();
        if (progress.PendingPurchase is { } purchase)
            warnings.Add($"Unresolved purchase: {purchase.Gil:N0} gil / {purchase.Mgp:N0} MGP / {purchase.Certificates:N0} certificates reserved. Further purchases are blocked until acquisition and currency evidence agree. Reload, FULL STOP and weekly reset keep this reservation.");
        if (progress.MinionAcquisition is { } acquisition)
            warnings.Add($"ADS minion acquisition: item {acquisition.ItemId}. FULL STOP cancels only this request; reload reconciles it without submitting it again.");
        if (progress.PendingTournamentReward is { } reward)
            warnings.Add($"Unresolved prize: {reward.Title}, {reward.Mgp:N0} MGP. " +
                (reward.Acknowledged ? "Acknowledgement saved; waiting for the exact MGP receipt." : "Acceptance outcome is unverified; it will not be submitted again."));
        if (warnings.Count > 0) UIConstants.Status("VerminionWarnings", string.Join("\n", warnings), UIConstants.Amber);
        ImGui.Separator();
        if (ImGui.BeginTabBar("VerminionTabs", ImGuiTabBarFlags.FittingPolicyScroll))
        {
            if (ImGui.BeginTabItem("Run"))
            {
                if (ImGui.BeginChild("VerminionRun", Vector2.Zero, false)) DrawRun(config, loggedIn);
                ImGui.EndChild();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Minions & guide"))
            {
                if (ImGui.BeginChild("VerminionGuide", Vector2.Zero, false)) DrawMinionsGuide(config, loggedIn);
                ImGui.EndChild();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Purchase limits"))
            {
                if (ImGui.BeginChild("VerminionPurchases", Vector2.Zero, false))
                {
                    UIConstants.Heading("Purchase limits for this character", plugin.Configuration.CompactUi);
                    ImGui.BeginDisabled(!loggedIn || service.IsActive || service.HasQuestAcquisition);
                    if (DrawSettings(config, includeGoalSettings: false, includeStatus: false)) plugin.ConfigManager.SaveCurrentAccount();
                    ImGui.EndDisabled();
                }
                ImGui.EndChild();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Tournament"))
            {
                if (ImGui.BeginChild("VerminionTournament", Vector2.Zero, false))
                {
                    UIConstants.Heading("Tournament and prizes", plugin.Configuration.CompactUi);
                    DrawTournamentInfo(config);
                    ImGui.TextWrapped("Tournament checks / prizes checks the Recordkeeper on your Home World, handles the known registration and prize prompts, and reads the game's visible tournament allowance. Claims need the owned prize acknowledgement and exact MGP receipt; interrupted acceptance is never resubmitted. When registration is closed or all 15 matches are used, it finishes any remaining weekly participation through ordinary CPU losses. Entry preparation stops before Join in Master Tournament. Open-period registration and reward collection need live verification; automatic tournament battles remain under development. Hidden counters remain unknown.");
                    ImGui.TextWrapped("Choose Tournament checks / prizes under Run to start this activity.");
                }
                ImGui.EndChild();
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
    }

    private void DrawRun(CharacterConfig config, bool loggedIn)
    {
        var service = plugin.VerminionService;
        ImGui.TextWrapped("Run Verminion on the current character independently of Run All. Opening this window does not start a match.");
        ImGui.BeginDisabled(!loggedIn || plugin.Engine.IsRunning || service.IsActive || service.HasQuestAcquisition || plugin.DadHandoffBlocksNewWork);
        if (UIConstants.BeginPanel("PermanentCampaign", "Permanent CPU campaign"))
        {
            ImGui.TextWrapped("Complete unfinished challenges in order. Cleared stages stay complete across weekly resets.");
            if (UIConstants.Button("Complete or continue 24 CPU challenges")) plugin.RunDashboardAction(service.RunChallenges);
            UIConstants.EndPanel();
        }
        if (UIConstants.BeginPanel("MissionWeekly", "Mission replay / weekly goals"))
        {
        if (DrawGoalSettings(config)) plugin.ConfigManager.SaveCurrentAccount();
        var runLabel = config.VerminionMode == VerminionMode.MissionRepeat
            ? $"Run mission {Math.Clamp(config.VerminionMission, 1, 24)} ({Math.Clamp(config.VerminionRepeatCount, 1, 1000)} clears)"
            : config.VerminionMode == VerminionMode.CpuRewards ? "Check tournament / prizes" : "Run weekly participation";
        if (UIConstants.Button(runLabel)) plugin.RunDashboardAction(service.RunTask);
        UIConstants.EndPanel();
        }
        ImGui.EndDisabled();
    }

    private void DrawMinionsGuide(CharacterConfig config, bool loggedIn)
    {
        var service = plugin.VerminionService;
        var progress = config.VerminionProgress;
        ImGui.BeginDisabled(!loggedIn);
        if (UIConstants.Button("Check planned minions")) showPlannedMinions = true;
        ImGui.EndDisabled();
        if (showPlannedMinions)
        {
            var planned = Enumerable.Range(1, 24).SelectMany(VerminionRoster.Required)
                .DistinctBy(minion => minion.Id)
                .Select(minion => (Minion: minion, Owned: loggedIn ? VerminionGameInteraction.OwnsMinion(minion.Id) : null)).ToArray();
            var registered = planned.Count(minion => minion.Owned == true);
            ImGui.TextWrapped(planned.Any(minion => minion.Owned == null)
                ? "Ownership is unavailable; log in and wait for character data."
                : $"Current character: {registered}/{planned.Length} planned minions registered for challenges 1-24 and mission replays.");
            ImGui.TextWrapped("This check reads ownership only. You can get missing minions yourself; use their inventory items to register them, then check again.");
            foreach (var (minion, owned) in planned)
            {
                ImGui.BulletText($"{minion.Name}: {(owned == true ? "registered" : owned == false ? "missing / not registered" : "ownership unavailable")}");
                if (owned != true) ImGui.TextWrapped("Get it: " + minion.Acquisition);
            }
            if (registered == planned.Length) ImGui.TextWrapped("All planned minions are registered.");
            ImGui.Separator();
        }
        ImGui.TextWrapped("Setup registers inventory minions first. If fewer than three are registered, it can buy only the missing entry minions from the Minion Trader: Mammet #001, Wayward Hatchling and Cherry Bomb, each 2,400 gil. The whole entry plan must fit your remaining cap and leave the minimum gil balance. Stages 12, 15, 23 and 24 require Wind-up Gentleman from Her Last Vow. Select its acquisition action below to hand the ARR Hildibrand chain to Questionable. The full first-entry sequence, other vendor routes and CPU tournaments remain unverified or unavailable.");

        ImGui.Separator();
        var next = VerminionService.PlannedStage(config, progress.CampaignRequested);
        var stage = previewStage == 0 ? Math.Min(next, 24) : previewStage;
        if (ImGui.SliderInt("Preview challenge", ref stage, 1, 24)) previewStage = stage;
        if (UIConstants.Button("Follow next stage")) { previewStage = 0; stage = Math.Min(next, 24); }
        DrawPlan(config, stage, loggedIn);
        ImGui.TextWrapped("Achievement minions: acquire guide support separately before selecting a tested composition. ADS visits Jonathas, claims available certificates and buys only the requested missing item. Minion of Light is excluded until its White Mage form can be selected reliably.");
        foreach (var offer in VerminionRoster.AchievementOffers)
        {
            var owned = loggedIn ? VerminionGameInteraction.OwnsMinion(offer.MinionId) : null;
            ImGui.TextWrapped($"{offer.Name}: {(owned == true ? "registered" : owned == false ? "not registered" : "ownership unknown")}; two certificates.");
            ImGui.BeginDisabled(!loggedIn || owned != false || plugin.Engine.IsRunning || service.IsActive || service.HasQuestAcquisition || plugin.DadHandoffBlocksNewWork);
            if (UIConstants.Button($"Acquire {offer.Name} through ADS")) plugin.AcquireVerminionMinion(offer.MinionId);
            ImGui.EndDisabled();
        }
        foreach (var minion in VerminionRoster.Required(stage).Where(minion => VerminionRoster.AcquisitionQuests(minion.Id).Length > 0))
        {
            if (!loggedIn || VerminionGameInteraction.OwnsMinion(minion.Id) != false) continue;
            ImGui.TextWrapped($"Acquire {minion.Name}: Questionable handles the unfinished quests and its configured DAD / FrenRider / ADS duty routes, stopping after the reward quest. Requires WigglyQuest's native priority/stop controls, an empty priority list and AutoRetainer multi mode off. This can be a long quest chain; quest travel spends gil separately from the minion purchase caps. Configure the quest and duty providers before starting.");
            ImGui.TextWrapped("Hildibrand includes three eight-player trials. For a solo run with this stack, configure those duties as unsynced in Questionable and enable FrenRider's eight-player ADS handoff. Check that ADS permits each trial at your chosen maturity threshold. Verminion uses these provider settings; it does not change them or guarantee a trial clear.");
            var dutyBlocker = plugin.VerminionQuestHandoffBlocker();
            if (dutyBlocker != null) ImGui.TextWrapped(dutyBlocker);
            ImGui.BeginDisabled(dutyBlocker != null || plugin.Engine.IsRunning || service.IsActive || service.HasQuestAcquisition || plugin.DadHandoffBlocksNewWork);
            if (UIConstants.Button($"Acquire {minion.Name} with Questionable")) plugin.AcquireVerminionMinion(minion.Id);
            ImGui.EndDisabled();
            ImGui.TextWrapped("Verminion stays paused while Questionable runs. Reload observes the handoff without starting it again. FULL STOP cancels the owned acquisition. After the reward, Resume registers the minion and continues the selected Verminion goal.");
        }
        ImGui.TextWrapped("Preview does not select a stage to run. Complete or continue 24 CPU challenges starts with the first unfinished stage.");
    }

    private static bool DrawGoalSettings(CharacterConfig config)
    {
        var changed = false;
        var mode = (int)config.VerminionMode;
        if (UIConstants.Combo("Activity##Verminion", ref mode, "Weekly participation - 5 matches\0Win selected mission - repeat clears\0Tournament checks / prizes\0"))
        { config.VerminionMode = (VerminionMode)mode; changed = true; }
        ImGui.TextWrapped(config.VerminionMode switch
        {
            VerminionMode.Participation => "Finish the remaining weekly participation matches through intentional CPU losses. Five matches award 27,000 base MGP; existing participation is counted.",
            VerminionMode.MissionRepeat => "Repeat one CPU mission for farming or testing. Run starts a fresh clear count; Resume keeps it. Count successful clears only; defeats retry, stopping after three consecutive losses or the attempt limit.",
            _ => "Check tournament notices, registration and claimable prizes on your Home World. Entry currently stops before Join. When registration is closed or all 15 matches are used, finish remaining weekly participation through CPU losses. Registration and prize handling await live verification; automatic tournament battles remain under development.",
        });
        if (config.VerminionMode == VerminionMode.MissionRepeat)
        {
            var mission = config.VerminionMission;
            ImGui.TextWrapped("Mission number (1-24)"); UIConstants.SetNextItemWidth(-1);
            if (UIConstants.InputInt("##VerminionMission", ref mission))
            { config.VerminionMission = Math.Clamp(mission, 1, 24); changed = true; }
            ImGui.TextDisabled("Default: 2 (recommended / optimal for farming).");
            var count = config.VerminionRepeatCount;
            ImGui.TextWrapped("Number of clears"); UIConstants.SetNextItemWidth(-1);
            if (UIConstants.InputInt("##VerminionClearCount", ref count))
            { config.VerminionRepeatCount = Math.Clamp(count, 1, 1000); changed = true; }
            ImGui.TextWrapped("Earlier wins and permanent clears do not count. Locked missions require preceding campaign clears. Mission 1 is the tutorial and earns no weekly participation.");
        }
        return changed;
    }

    private static void DrawTournamentInfo(CharacterConfig config)
    {
        if (config.VerminionProgress.LastTournamentInfo is { } tournament)
        {
            ImGui.TextWrapped(config.VerminionProgress.TournamentObservedUtc == default
                ? "Last tournament observation (time unavailable):"
                : $"Tournament last checked: {config.VerminionProgress.TournamentObservedUtc:u}");
            ImGui.TextWrapped(tournament.Summary);
            ImGui.TextWrapped("Saved observation for this character; Tournament checks / prizes reads it again before acting. Weekly completion does not establish tournament completion or a reward claim.");
        }
        else if (config.VerminionMode == VerminionMode.CpuRewards)
            ImGui.TextWrapped("No tournament observation saved for this character. Check tournament / prizes to read the current notice and registration.");
        if (config.VerminionProgress.PendingTournamentReward is { } reward)
            ImGui.TextWrapped($"Unresolved prize: {reward.Title}, {reward.Mgp:N0} MGP. " +
                (reward.Acknowledged ? "Acknowledgement saved; waiting for the exact MGP receipt. " : "Acceptance outcome is unverified. ") +
                "Reload and FULL STOP preserve this intent; it will not be submitted again.");
        if (config.VerminionProgress.LastTournamentReward is { ConfirmedUtc: var claimedAt } claimed && claimedAt != default)
            ImGui.TextWrapped($"Last verified prize: {claimed.Title}, {claimed.Mgp:N0} MGP at {claimedAt:u}.");
        if (config.VerminionMode == VerminionMode.CpuRewards && VerminionService.TournamentWorldRequirement() is { } worldRequirement)
            ImGui.TextWrapped(worldRequirement);
    }

    internal static bool DrawSettings(CharacterConfig config, bool includeGoalSettings = true, bool includeStatus = true)
    {
        var changed = includeGoalSettings && DrawGoalSettings(config);
        if (includeStatus) DrawTournamentInfo(config);
        var gil = (int)Math.Min(config.VerminionGilPurchaseCap, int.MaxValue);
        var mgp = (int)Math.Min(config.VerminionMgpPurchaseCap, int.MaxValue);
        var certificates = (int)Math.Min(config.VerminionCertificatePurchaseCap, int.MaxValue);
        var reserve = (int)Math.Min(config.VerminionGilReserve, int.MaxValue);
        ImGui.TextWrapped("Cumulative gil purchase cap"); UIConstants.SetNextItemWidth(-1);
        if (UIConstants.InputInt("##VerminionGilCap", ref gil))
        { config.VerminionGilPurchaseCap = (uint)Math.Max(0, gil); changed = true; }
        ImGui.TextWrapped("Cumulative MGP purchase cap"); UIConstants.SetNextItemWidth(-1);
        if (UIConstants.InputInt("##VerminionMgpCap", ref mgp))
        { config.VerminionMgpPurchaseCap = (uint)Math.Max(0, mgp); changed = true; }
        ImGui.TextWrapped("Cumulative certificate purchase cap"); UIConstants.SetNextItemWidth(-1);
        if (UIConstants.InputInt("##VerminionCertificateCap", ref certificates))
        { config.VerminionCertificatePurchaseCap = (uint)Math.Max(0, certificates); changed = true; }
        ImGui.TextWrapped($"Achievement Certificates spent: {config.VerminionProgress.CertificatesSpent:N0}/{config.VerminionCertificatePurchaseCap:N0}. This separate per-character cap defaults to zero, persists across weeks, and authorizes no gil or MGP spending. Claims do not consume this budget. Travel costs are separate.");
        ImGui.TextWrapped("Minimum gil balance"); UIConstants.SetNextItemWidth(-1);
        if (UIConstants.InputInt("##VerminionGilReserve", ref reserve))
        { config.VerminionGilReserve = (uint)Math.Max(0, reserve); changed = true; }
        ImGui.TextWrapped("ADS handles every vendor purchase, including travel and menus. Its guarded purchase API is required. Each gil purchase must leave the minimum balance; the saved cap and current funds are checked before ADS submits or confirms it.");
        ImGui.TextWrapped($"Spent on this character: {config.VerminionProgress.GilSpent:N0}/{config.VerminionGilPurchaseCap:N0} gil; {config.VerminionProgress.MgpSpent:N0}/{config.VerminionMgpPurchaseCap:N0} MGP. Caps are cumulative and do not reset weekly. Zero prevents purchases. Entry minions cost 2,400 gil each; the campaign's Zu Hatchling costs 10,000 MGP from permanent Gold Saucer stock. Already owned or unregistered inventory minions are used before buying.");
        ImGui.TextWrapped("The selected roster has no FATE-vendor requirement. Stage 6 cleared with Zu defenders. The Zu-only Stage 7 roster cleared on its third attempt after two defeats; repeated-win reliability is not established. Stage 15 cleared with Wind-up Gentleman on its second attempt. Stage 16 cleared on the first attempt with Mammet stone attackers and Zu defenders, after three Zu-only defeats.");
        ImGui.TextWrapped("Achievement vendor: Jonathas in Old Gridania sells these minions for two Achievement Certificates each. Wind-up Odin and Wind-up Cursor can be requested below through ADS within the certificate cap. Travel, certificate claiming, both purchases and registration are verified; their support compositions are not yet selected by the battle strategies.");
        if (includeStatus) DrawPurchaseWarnings(config);
        return changed;
    }

    private static void DrawPurchaseWarnings(CharacterConfig config)
    {
        if (config.VerminionProgress.PendingPurchase is { } pending)
            ImGui.TextWrapped($"Unresolved purchase: {pending.Gil:N0} gil / {pending.Mgp:N0} MGP / {pending.Certificates:N0} certificates reserved. Further purchases are blocked until both acquisition and currency evidence agree. Reload, FULL STOP and weekly reset keep this reservation.");
        if (config.VerminionProgress.MinionAcquisition is { } acquisition)
            ImGui.TextWrapped($"ADS minion acquisition: item {acquisition.ItemId}. FULL STOP cancels only this request; reload reconciles it without submitting it again.");
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
            stage == 6 ? "Airship attackers and Zu defenders: one fresh live clear verified, including Imp phases and the next-stage transition." :
            stage == 7 ? "Zu baseline: one fresh live clear in three attempts. Both losses counted only toward participation. Repeated-win reliability remains unverified." :
            stage == 12 ? "Gentleman baseline: one fresh live clear in three attempts with named-boss targeting, wider camera framing and rear pursuit. Repeated-win reliability remains unverified." :
            stage == 15 ? "Gentleman baseline: the fresh local replay cleared with the revised recovery and final-phase pursuit. Repeated-win reliability remains unverified." :
            stage == 16 ? "Mammet attackers with Zu defenders: one fresh live clear on the first mixed-roster attempt, including Nasty Peck and the next-stage transition. Repeated-win reliability remains unverified." :
            stage == 19 ? "Airship baseline: one live clear verified, including Cargo's ATK buff. Repeated-win reliability remains unverified." :
            stage == 20 ? "Mammet attackers with Wayward Hatchling defenders: one fresh live clear verified, including the mixed opening, defender summons and special. Repeated-win reliability remains unverified." :
            stage is 21 or 22 ? "Mammet baseline: one live clear verified, including minimap camera positioning. Repeated-win reliability remains unverified." :
            "Guide-based strategy under development. A recorded character clear does not establish repeated-win reliability.");
        foreach (var minion in VerminionRoster.Required(stage))
        {
            var owned = showOwnership ? VerminionGameInteraction.OwnsMinion(minion.Id) : null;
            ImGui.BulletText($"{minion.Name} - cost {minion.Cost}; {(owned == true ? "registered" : owned == false ? "missing / not registered" : "ownership unknown")}");
            ImGui.TextWrapped(minion.Acquisition);
        }
        if (VerminionRoster.AchievementSupport(stage) is { } achievementSupport)
        {
            ImGui.TextWrapped("Achievement-vendor guide option: " + achievementSupport);
            ImGui.TextWrapped("Jonathas, Old Gridania (10.6, 6.2): two Achievement Certificates per minion item. Prefer an already registered minion or its inventory item. ADS travel, certificate claiming, Odin and Cursor purchases, and registration are verified.");
            if (showOwnership && stage is 16 or 19 or 22 or 23)
            {
                var supportId = (ushort)(stage == 23 ? 51 : 76);
                var supportName = stage == 23 ? "Wind-up Cursor" : "Wind-up Odin";
                var owned = VerminionGameInteraction.OwnsMinion(supportId);
                ImGui.TextWrapped($"{supportName}: {(owned == true ? "registered" : owned == false ? "not registered (inventory item may still be available)" : "ownership unknown")}. This guide option is not a current admission requirement.");
            }
        }
        if (stage == 23) ImGui.TextWrapped("Campaign prerequisite: register Wind-up Gentleman, rewarded by Her Last Vow (level 50 ARR Hildibrand). Both published guides recommend it for Twintania. Airship, Nero and Zu attempts failed, so the bot no longer buys a substitute for this stage. This guide roster has one observed clear on its first attempt; repeated-win reliability and the special's actual effect remain unverified.");
        if (stage == 24) ImGui.TextWrapped("Wind-up Gentleman baseline: an earlier clear took 7:51; the latest local replay cleared on attempt three in 8:10 with unchanged tactics. Native warning removal, circle responses, tower arrivals and replacement summons are verified. One attempt stopped on an input timeout without credit; another was a verified defeat. The latest test character has 24/24 clears confirmed. Repeated-win reliability remains unverified. Stage 24 uses a nine-minute control window and waits for an explicit result; the normal three-attempt campaign limit applies.");
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
        if (stage is 12 or 15 or 23 or 24)
            ImGui.TextWrapped("Gentleman-only guide: https://na.finalfantasyxiv.com/lodestone/character/28572768/blog/4629440/");
    }
}
