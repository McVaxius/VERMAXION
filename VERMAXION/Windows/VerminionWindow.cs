using System;
using AethertekUI;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using VERMAXION.Models;
using VERMAXION.Services;

namespace VERMAXION.Windows;

internal sealed class VerminionWindow : Window
{
    private readonly AethertekUI.Dalamud.MaterialWindowMotion motion = new();
    private bool materialBackdropStylePushed;
    private readonly AethertekUI.Dalamud.MaterialWindowLock movementLock = new();
    private readonly Plugin plugin;
    private int previewStage;
    private bool showPlannedMinions;

    public VerminionWindow(Plugin plugin) : base("Lord of Verminion##VermaxionVerminion", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        this.plugin = plugin;
        Size = new Vector2(760, 680);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new(520, 620), MaximumSize = new(float.MaxValue, float.MaxValue) };
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
        if (MaterialTheme.Current.IsLcars) MaterialLcars.Header(UiText.T("Verminion"));
        UiGui.TitleWithButtons("Lord of Verminion", UiText.T("Lord of Verminion"), this);
        var config = plugin.ConfigManager.GetActiveConfig();
        var service = plugin.VerminionService;
        var loggedIn = Plugin.PlayerState.IsLoaded && !string.IsNullOrEmpty(plugin.ConfigManager.CurrentCharacterKey);
        UIConstants.Heading("Lord of Verminion", plugin.Configuration.CompactUi);
        UiGui.TextWrapped(UiText.F("Current character: {0}", loggedIn ? plugin.ConfigManager.CurrentCharacterKey : UiText.T("unavailable")));
        var progress = config.VerminionProgress;

        var cleared = Enumerable.Range(0, 24).Count(stage => (progress.ClearedChallengeMask & (1u << stage)) != 0);
        UiGui.TextWrapped(UiText.F($"Permanent CPU campaign: {cleared}/24 cleared"));
        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, UIConstants.Blue);
        ImGui.ProgressBar(cleared / 24f, new Vector2(-1, 20f * UIConstants.Scale), string.Empty);
        ImGui.PopStyleColor();
        if (UIConstants.FullStopButton()) plugin.FullStop();
        ImGui.BeginDisabled(!loggedIn || plugin.Engine.IsRunning || service.IsActive || service.HasQuestAcquisition || plugin.DadHandoffBlocksNewWork);
        if (progress.RunMode != null)
        {
            UIConstants.SameLineIfFits(progress.CampaignRequested || progress.RunIsCampaign ? "Resume CPU campaign" : "Resume");
            if (UIConstants.Button(progress.CampaignRequested || progress.RunIsCampaign ? "Resume CPU campaign" : "Resume")) plugin.RunDashboardAction(service.ResumeTask);
        }
        ImGui.EndDisabled();
        var stageAttempts = progress.CampaignStage == progress.NextUnclearedChallenge ? progress.CampaignStageAttempts : 0;
        var status = UiText.Verminion(service.StatusText, VerminionService.ProgressSummary(config));
        if (!loggedIn) status += "\n" + UiText.T("Log in and wait for character registration to configure or run Verminion.");
        status += "\n" + UiText.F($"Next stage attempts: {stageAttempts}/3 · Losses: {progress.ConsecutiveLosses} · Weekly: {progress.WeeklyMatches}/5 matches; {progress.WeeklyWins} wins.");
        if (progress.RunMode == VerminionMode.MissionRepeat && !progress.RunIsCampaign)
            status += "\n" + UiText.F($"Mission {progress.RunMission}: {progress.RunWins}/{progress.RunRepeatCount} clears; attempts {progress.RunAttempts}/{progress.RunAttemptLimit}.");
        UIConstants.Status("VerminionStatus", status, UIConstants.Metadata, translate: false);
        var warnings = new System.Collections.Generic.List<string>();
        if (progress.PendingPurchase is { } purchase)
            warnings.Add(UiText.F((FormattableString)$"Unresolved purchase: {purchase.Gil:N0} gil / {purchase.Mgp:N0} MGP / {purchase.Certificates:N0} certificates reserved. Further purchases are blocked until acquisition and currency evidence agree. Reload, FULL STOP and weekly reset keep this reservation."));
        if (progress.MinionAcquisition is { } acquisition)
            warnings.Add(UiText.F($"ADS minion acquisition: item {acquisition.ItemId}. FULL STOP cancels only this request; reload reconciles it without submitting it again."));
        if (progress.PendingTournamentReward is { } reward)
            warnings.Add(UiText.F($"Unresolved prize: {reward.Title}, {reward.Mgp:N0} MGP. ") +
                UiText.T(reward.Acknowledged ? "Acknowledgement saved; waiting for the exact MGP receipt." : "Acceptance outcome is unverified; it will not be submitted again."));
        if (warnings.Count > 0) UIConstants.Status("VerminionWarnings", string.Join("\n", warnings), UIConstants.Amber);
        ImGui.Separator();
        bool tabsOpen;
        using (MaterialText.PushLineHeight(new[] { "Run", "Minions & guide", "Purchases", "Tournament" }.Select(UiText.T).ToArray()))
            tabsOpen = ImGui.BeginTabBar("VerminionTabs", ImGuiTabBarFlags.FittingPolicyScroll);
        if (tabsOpen)
        {
            if (UiGui.BeginTabItem("Run"))
            {
                if (ImGui.BeginChild("VerminionRun", Vector2.Zero, false)) DrawRun(config, loggedIn);
                ImGui.EndChild();
                ImGui.EndTabItem();
            }
            if (UiGui.BeginTabItem("Minions & guide"))
            {
                if (ImGui.BeginChild("VerminionGuide", Vector2.Zero, false)) DrawMinionsGuide(config, loggedIn);
                ImGui.EndChild();
                ImGui.EndTabItem();
            }
            if (UiGui.BeginTabItem("Purchases"))
            {
                if (ImGui.BeginChild("VerminionPurchases", Vector2.Zero, false))
                {
                    UIConstants.Heading("Minion purchases", plugin.Configuration.CompactUi);
                    ImGui.BeginDisabled(!loggedIn || service.IsActive || service.HasQuestAcquisition);
                    if (DrawSettings(config, includeGoalSettings: false, includeStatus: false)) plugin.ConfigManager.SaveCurrentAccount();
                    ImGui.EndDisabled();
                }
                ImGui.EndChild();
                ImGui.EndTabItem();
            }
            if (UiGui.BeginTabItem("Tournament"))
            {
                if (ImGui.BeginChild("VerminionTournament", Vector2.Zero, false))
                {
                    UIConstants.Heading("Tournament and prizes", plugin.Configuration.CompactUi);
                    DrawTournamentInfo(config);
                    UiGui.TextWrapped("Check tournament registration and prizes on your Home World. If entry is unavailable, fight remaining weekly CPU matches. For tournament battles, choose a PvP activity and Tournament player battle.");
                    UiGui.TextWrapped("Choose Tournament checks / prizes under Run to start this activity.");
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
        UiGui.TextWrapped("Run Verminion on the current character independently of Run All. Opening this window does not start a match.");
        ImGui.BeginDisabled(!loggedIn || plugin.Engine.IsRunning || service.IsActive || service.HasQuestAcquisition || plugin.DadHandoffBlocksNewWork);
        if (UIConstants.BeginPanel("PermanentCampaign", "Permanent CPU campaign"))
        {
            UiGui.TextWrapped("Complete unfinished challenges in order. Cleared stages stay complete across weekly resets.");
            if (UIConstants.Button("Complete or continue 24 CPU challenges")) plugin.RunDashboardAction(service.RunChallenges);
            UIConstants.EndPanel();
        }
        if (UIConstants.BeginPanel("MissionWeekly", "Mission replay / weekly goals"))
        {
            if (DrawGoalSettings(config)) plugin.ConfigManager.SaveCurrentAccount();
            var runLabel = config.VerminionMode == VerminionMode.MissionRepeat
                ? $"Run mission {Math.Clamp(config.VerminionMission, 1, 24)} ({Math.Clamp(config.VerminionRepeatCount, 1, 1000)} clears)"
                : VerminionDutyRules.IsPlayerMode(config.VerminionMode) ? "Run player matches"
                : config.VerminionMode == VerminionMode.CpuRewards ? "Check tournament / prizes"
                : config.VerminionMode == VerminionMode.Participation ? "Run weekly CPU - stay passive / intentionally lose"
                : "Run weekly CPU - fight to win";
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
            UiGui.TextWrapped(planned.Any(minion => minion.Owned == null)
                ? "Ownership is unavailable; log in and wait for character data."
                : UiText.F($"Current character: {registered}/{planned.Length} planned minions registered for challenges 1-24 and mission replays."));
            UiGui.TextWrapped("This check reads ownership only. You can get missing minions yourself; use their inventory items to register them, then check again.");
            foreach (var (minion, owned) in planned)
            {
                UiGui.BulletText(UiText.F($"{minion.Name}: {UiText.T(owned == true ? "registered" : owned == false ? "missing / not registered" : "ownership unavailable")}"));
                if (owned != true) UiGui.TextWrapped(UiText.F("Get it: {0}", UiText.T(minion.Acquisition)));
            }
            if (registered == planned.Length) UiGui.TextWrapped("All planned minions are registered.");
            ImGui.Separator();
        }
        UiGui.TextWrapped("Missing minions needed for the selected run are purchased automatically. Owned minions and inventory items are used first.");
        UiGui.TextWrapped("Entry minions cost 2,400 gil each; Zu Hatchling costs 10,000 MGP. Quest travel costs are separate.");

        ImGui.Separator();
        var next = VerminionService.PlannedStage(config, progress.CampaignRequested);
        var stage = previewStage == 0 ? Math.Min(next, 24) : previewStage;
        if (UiGui.SliderInt("Preview challenge", ref stage, 1, 24)) previewStage = stage;
        if (UIConstants.Button("Follow next stage")) { previewStage = 0; stage = Math.Min(next, 24); }
        DrawPlan(config, stage, loggedIn);
        UiGui.TextWrapped("Achievement minions: acquire guide support separately before selecting a tested composition. ADS visits Jonathas, claims available certificates and buys only the requested missing item. Minion of Light is excluded until its White Mage form can be selected reliably.");
        foreach (var offer in VerminionRoster.AchievementOffers)
        {
            var owned = loggedIn ? VerminionGameInteraction.OwnsMinion(offer.MinionId) : null;
            UiGui.TextWrapped(UiText.F($"{offer.Name}: {UiText.T(owned == true ? "registered" : owned == false ? "not registered" : "ownership unknown")}; two certificates."));
            ImGui.BeginDisabled(!loggedIn || owned != false || plugin.Engine.IsRunning || service.IsActive || service.HasQuestAcquisition || plugin.DadHandoffBlocksNewWork);
            if (UIConstants.Button($"Acquire {offer.Name} through ADS")) plugin.AcquireVerminionMinion(offer.MinionId);
            ImGui.EndDisabled();
        }
        foreach (var minion in VerminionRoster.Required(stage).Where(minion => VerminionRoster.AcquisitionQuests(minion.Id).Length > 0))
        {
            if (!loggedIn || VerminionGameInteraction.OwnsMinion(minion.Id) != false) continue;
            UiGui.TextWrapped(UiText.F($"Acquire {minion.Name}: Questionable handles the unfinished quests and its configured DAD / FrenRider / ADS duty routes, stopping after the reward quest. Requires WigglyQuest's native priority/stop controls, an empty priority list and AutoRetainer multi mode off. This can be a long quest chain; quest travel also spends gil. Configure the quest and duty providers before starting."));
            UiGui.TextWrapped("Hildibrand includes three eight-player trials. For a solo run with this stack, configure those duties as unsynced in Questionable and enable FrenRider's eight-player ADS handoff. Check that ADS permits each trial at your chosen maturity threshold. Verminion uses these provider settings; it does not change them or guarantee a trial clear.");
            var dutyBlocker = plugin.VerminionQuestHandoffBlocker();
            if (dutyBlocker != null) UiGui.TextWrapped(dutyBlocker);
            ImGui.BeginDisabled(dutyBlocker != null || plugin.Engine.IsRunning || service.IsActive || service.HasQuestAcquisition || plugin.DadHandoffBlocksNewWork);
            if (UIConstants.Button($"Acquire {minion.Name} with Questionable")) plugin.AcquireVerminionMinion(minion.Id);
            ImGui.EndDisabled();
            UiGui.TextWrapped("Verminion stays paused while Questionable runs. Reload observes the handoff without starting it again. FULL STOP cancels the owned acquisition. After the reward, Resume registers the minion and continues the selected Verminion goal.");
        }
        UiGui.TextWrapped("Preview does not select a stage to run. Complete or continue 24 CPU challenges starts with the first unfinished stage.");
    }

    private static bool DrawGoalSettings(CharacterConfig config)
    {
        var changed = false;
        var modes = new[] { VerminionMode.WeeklyWin, VerminionMode.Participation, VerminionMode.MissionRepeat,
            VerminionMode.CpuRewards, VerminionMode.PvpWin, VerminionMode.PvpThrow };
        var mode = Math.Max(0, Array.IndexOf(modes, config.VerminionMode));
        if (UIConstants.Combo("Activity##Verminion", ref mode, "Weekly CPU - fight to win (5 matches)\0Weekly CPU - stay passive / intentionally lose (5 matches)\0Win selected mission - repeat clears\0Tournament checks / prizes\0PvP - Try to win\0PvP - stay passive / throw\0"))
        { config.VerminionMode = modes[mode]; changed = true; }
        UiGui.TextWrapped(config.VerminionMode switch
        {
            VerminionMode.WeeklyWin => "Summon and fight to win. Stop after five completed weekly CPU matches; only actual victories count as wins.",
            VerminionMode.Participation => "Stay passive and intentionally lose CPU battles after the tutorial. Stop after five completed weekly matches.",
            VerminionMode.MissionRepeat => "Repeat one CPU mission for farming or testing. Run starts a fresh clear count; Resume keeps it. Count successful clears only; defeats retry, stopping after three consecutive losses or the attempt limit.",
            VerminionMode.PvpWin => "Summon, reinforce, defend and attack in the selected player queue. Wins are not guaranteed.",
            VerminionMode.PvpThrow => "Summon one weak minion at a time and move it periodically on your home side. No attack, defense or forfeit commands; the actual result may still be a win.",
            _ => "Check tournament registration and prizes on your Home World. If entry is unavailable, fight remaining weekly CPU matches. For tournament battles, choose a PvP activity and Tournament player battle.",
        });
        if (VerminionDutyRules.IsPlayerMode(config.VerminionMode))
        {
            var queues = new[] { VerminionPlayerQueue.NonRp, VerminionPlayerQueue.Rp, VerminionPlayerQueue.Tournament };
            var queue = Math.Max(0, Array.IndexOf(queues, config.VerminionPlayerQueue));
            if (UIConstants.Combo("Player queue##Verminion", ref queue, "Non-RP player battle\0RP player battle\0Tournament player battle\0"))
            { config.VerminionPlayerQueue = queues[queue]; changed = true; }
            if (config.VerminionPlayerQueue == VerminionPlayerQueue.Tournament)
                UiGui.TextWrapped("Tournament battles require your Home World, registration and remaining allowance. This selection plays actual matches.");
            var count = config.VerminionRepeatCount;
            UiGui.TextWrapped("Number of player matches"); UIConstants.SetNextItemWidth(-1);
            if (UIConstants.InputInt("##VerminionPlayerCount", ref count))
            { config.VerminionRepeatCount = Math.Clamp(count, 1, 1000); changed = true; }
            UiGui.TextWrapped("Any opponent; no partner needed. Run resets the match count; Resume keeps it. Only native results count. Live verification pending.");
        }
        if (config.VerminionMode == VerminionMode.MissionRepeat)
        {
            var mission = config.VerminionMission;
            UiGui.TextWrapped("Mission number (1-24)"); UIConstants.SetNextItemWidth(-1);
            if (UIConstants.InputInt("##VerminionMission", ref mission))
            { config.VerminionMission = Math.Clamp(mission, 1, 24); changed = true; }
            UiGui.TextDisabled("Default: 2 (recommended / optimal for farming).");
            var count = config.VerminionRepeatCount;
            UiGui.TextWrapped("Number of clears"); UIConstants.SetNextItemWidth(-1);
            if (UIConstants.InputInt("##VerminionClearCount", ref count))
            { config.VerminionRepeatCount = Math.Clamp(count, 1, 1000); changed = true; }
            UiGui.TextWrapped("Earlier wins and permanent clears do not count. Locked missions require preceding campaign clears. Mission 1 is the tutorial and earns no weekly participation.");
        }
        return changed;
    }

    private static void DrawTournamentInfo(CharacterConfig config)
    {
        if (config.VerminionProgress.LastTournamentInfo is { } tournament)
        {
            UiGui.TextWrapped(config.VerminionProgress.TournamentObservedUtc == default
                ? "Last tournament observation (time unavailable):"
                : UiText.F($"Tournament last checked: {config.VerminionProgress.TournamentObservedUtc:u}"));
            UiGui.TextWrapped(tournament.Summary);
            UiGui.TextWrapped("Saved observation for this character; Tournament checks / prizes reads it again before acting. Weekly completion does not establish tournament completion or a reward claim.");
        }
        else if (config.VerminionMode == VerminionMode.CpuRewards)
            UiGui.TextWrapped("No tournament observation saved for this character. Check tournament / prizes to read the current notice and registration.");
        if (config.VerminionProgress.PendingTournamentReward is { } reward)
            UiGui.TextWrapped(UiText.F($"Unresolved prize: {reward.Title}, {reward.Mgp:N0} MGP. ") +
                UiText.T(reward.Acknowledged ? "Acknowledgement saved; waiting for the exact MGP receipt. " : "Acceptance outcome is unverified. ") +
                UiText.T("Reload and FULL STOP preserve this intent; it will not be submitted again."));
        if (config.VerminionProgress.LastTournamentReward is { ConfirmedUtc: var claimedAt } claimed && claimedAt != default)
            UiGui.TextWrapped(UiText.F($"Last verified prize: {claimed.Title}, {claimed.Mgp:N0} MGP at {claimedAt:u}."));
        if (config.VerminionMode == VerminionMode.CpuRewards && VerminionService.TournamentWorldRequirement() is { } worldRequirement)
            UiGui.TextWrapped(worldRequirement);
    }

    internal static bool DrawSettings(CharacterConfig config, bool includeGoalSettings = true, bool includeStatus = true)
    {
        var changed = includeGoalSettings && DrawGoalSettings(config);
        if (includeStatus) DrawTournamentInfo(config);
        var reserve = (int)Math.Min(config.VerminionGilReserve, int.MaxValue);
        UiGui.TextWrapped("Minimum gil balance"); UIConstants.SetNextItemWidth(-1);
        if (UIConstants.InputInt("##VerminionGilReserve", ref reserve))
        { config.VerminionGilReserve = (uint)Math.Max(0, reserve); changed = true; }
        UiGui.TextWrapped("Missing minions needed for the selected run are purchased automatically. Owned minions and inventory items are used first.");
        UiGui.TextWrapped("Purchases use ADS and check current funds and the minimum gil balance before buying. No purchase budget is required.");
        UiGui.TextWrapped(UiText.F($"Spent on this character: {config.VerminionProgress.GilSpent:N0} gil; {config.VerminionProgress.MgpSpent:N0} MGP; {config.VerminionProgress.CertificatesSpent:N0} Achievement Certificates."));
        UiGui.TextWrapped("Entry minions cost 2,400 gil each; Zu Hatchling costs 10,000 MGP. Quest travel costs are separate.");
        UiGui.TextWrapped("The selected roster has no FATE-vendor requirement. Stage 6 cleared with Zu defenders. The Zu-only Stage 7 roster cleared on its third attempt after two defeats; repeated-win reliability is not established. Stage 15 cleared with Wind-up Gentleman on its second attempt. Stage 16 cleared on the first attempt with Mammet stone attackers and Zu defenders, after three Zu-only defeats.");
        UiGui.TextWrapped("Wind-up Odin and Wind-up Cursor cost two Achievement Certificates each. Their acquisition actions buy only the requested missing minion.");
        if (includeStatus) DrawPurchaseWarnings(config);
        return changed;
    }

    private static void DrawPurchaseWarnings(CharacterConfig config)
    {
        if (config.VerminionProgress.PendingPurchase is { } pending)
            UiGui.TextWrapped(UiText.F((FormattableString)$"Unresolved purchase: {pending.Gil:N0} gil / {pending.Mgp:N0} MGP / {pending.Certificates:N0} certificates reserved. Further purchases are blocked until both acquisition and currency evidence agree. Reload, FULL STOP and weekly reset keep this reservation."));
        if (config.VerminionProgress.MinionAcquisition is { } acquisition)
            UiGui.TextWrapped(UiText.F($"ADS minion acquisition: item {acquisition.ItemId}. FULL STOP cancels only this request; reload reconciles it without submitting it again."));
    }

    internal static void DrawPlan(CharacterConfig config, int stage, bool showOwnership)
    {
        if (stage is < 1 or > 24) { UiGui.TextWrapped("All campaign challenges are recorded complete."); return; }
        UiGui.Text(UiText.F($"Stage {stage}/24"));
        UiGui.TextWrapped(VerminionRoster.Tactics(stage));
        if ((config.VerminionProgress.ClearedChallengeMask & (1u << (stage - 1))) != 0)
            UiGui.TextWrapped("Character clear recorded.");
        UiGui.TextWrapped(stage == 1 ? "Tutorial control has historical runtime evidence." :
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
            UiGui.BulletText(UiText.F($"{minion.Name} - cost {minion.Cost}; {UiText.T(owned == true ? "registered" : owned == false ? "missing / not registered" : "ownership unknown")}"));
            UiGui.TextWrapped(minion.Acquisition);
        }
        if (VerminionRoster.AchievementSupport(stage) is { } achievementSupport)
        {
            UiGui.TextWrapped(UiText.F("Achievement-vendor guide option: {0}", achievementSupport));
            UiGui.TextWrapped("Jonathas, Old Gridania (10.6, 6.2): two Achievement Certificates per minion item. Prefer an already registered minion or its inventory item. ADS travel, certificate claiming, Odin and Cursor purchases, and registration are verified.");
            if (showOwnership && stage is 16 or 19 or 22 or 23)
            {
                var supportId = (ushort)(stage == 23 ? 51 : 76);
                var supportName = stage == 23 ? "Wind-up Cursor" : "Wind-up Odin";
                var owned = VerminionGameInteraction.OwnsMinion(supportId);
                UiGui.TextWrapped(UiText.F($"{supportName}: {UiText.T(owned == true ? "registered" : owned == false ? "not registered (inventory item may still be available)" : "ownership unknown")}. This guide option is not a current admission requirement."));
            }
        }
        if (stage == 23) UiGui.TextWrapped("Campaign prerequisite: register Wind-up Gentleman, rewarded by Her Last Vow (level 50 ARR Hildibrand). Both published guides recommend it for Twintania. Airship, Nero and Zu attempts failed, so the bot no longer buys a substitute for this stage. This guide roster has one observed clear on its first attempt; repeated-win reliability and the special's actual effect remain unverified.");
        if (stage == 24) UiGui.TextWrapped("Wind-up Gentleman baseline: an earlier clear took 7:51; the latest local replay cleared on attempt three in 8:10 with unchanged tactics. Native warning removal, circle responses, tower arrivals and replacement summons are verified. One attempt stopped on an input timeout without credit; another was a verified defeat. The latest test character has 24/24 clears confirmed. Repeated-win reliability remains unverified. Stage 24 uses a nine-minute control window and waits for an explicit result; the normal three-attempt campaign limit applies.");
        if (showOwnership && VerminionRoster.Missing(stage, VerminionGameInteraction.OwnsMinion) is { } missing)
            UiGui.TextWrapped(missing);
        UiGui.TextWrapped("Entry also requires three registered minions, Gold Saucer access and the preceding challenge clears. Setup checks these in game.");
        if (showOwnership && VerminionGameInteraction.TryReadOwnedMinions(out var registered))
        {
            UiGui.TextWrapped(UiText.F($"Registered minions: {registered.Length} (entry requires 3)."));
            var entry = VerminionRoster.EntryPurchases(registered);
            if (entry.Length > 0)
                UiGui.TextWrapped(UiText.F($"After registering inventory items, entry may need {string.Join(", ", entry.Select(offer => offer.Name))} ({entry.Sum(offer => (long)offer.Gil):N0} gil total). Purchases require sufficient funds and preserve the minimum gil balance."));
        }
        UiGui.TextWrapped(UiText.F("Stage guide: {0}", VerminionRoster.GuideUrl(stage)));
        if (stage is 12 or 15 or 23 or 24)
            UiGui.TextWrapped(UiText.F("Gentleman-only guide: {0}", "https://na.finalfantasyxiv.com/lodestone/character/28572768/blog/4629440/"));
    }
}
