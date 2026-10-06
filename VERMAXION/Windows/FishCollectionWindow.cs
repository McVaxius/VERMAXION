using System;
using AethertekUI;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using VERMAXION.Models;

namespace VERMAXION.Windows;

internal sealed class FishCollectionWindow : Window
{
    private readonly AethertekUI.Dalamud.MaterialWindowMotion motion = new();
    private readonly Plugin plugin;
    private string search = "", foodSearch = "", characterKey = "";
    private bool missingOnly = true, eligibleOnly;
    private int testStatusFilter;
    private uint selectedFishId;

    public FishCollectionWindow(Plugin plugin) : base("Big and fabled fish collection##VMX", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        this.plugin = plugin;
        Size = new Vector2(760, 680);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new(520, 480), MaximumSize = new(float.MaxValue, float.MaxValue) };
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
        UiGui.Title("Big and fabled fish collection", UiText.T("Big and fabled fish collection"));
        var service = plugin.FishCollection;
        var settings = plugin.Configuration.FishCollection;
        var supplyNames = service.Supplies.Supplies.Select(supply => supply.Name).ToArray();
        var account = plugin.ConfigManager.GetCurrentAccount();
        if (account == null) { UiGui.TextWrapped("Select and register the current account first."); return; }
        UIConstants.Heading("Fish collection", plugin.Configuration.CompactUi);
        if (UIConstants.BeginPanel("CollectionScope"))
        {
        UiGui.TextWrapped(UiText.F("Account: {0}", string.IsNullOrWhiteSpace(account.AccountAlias) ? account.AccountId : account.AccountAlias));
        if (!service.IsActive)
        { if (UIConstants.Button("Start collection")) service.Start(); }
        else
        { if (UIConstants.Button("Stop collection")) service.Stop(); }
        UIConstants.SameLineIfFits("Acknowledge alert");
        if (UIConstants.Button("Acknowledge alert")) service.Acknowledge();
        var collectionStatus = UiText.Collection(service.Status, supplyNames, service.LastOutcome);
        var status = service.Assignment is { } assignment
            ? UiText.F($"Committed: {assignment.CharacterKey} — {assignment.Opportunity.Fish.Name}; window {assignment.Opportunity.StartUtc.ToLocalTime():g} to {assignment.Opportunity.EndUtc.ToLocalTime():g}\n{collectionStatus}")
            : UiText.F("No character committed to an opportunity.\n{0}", collectionStatus);
        UIConstants.Status("FishCollectionStatus", status, UIConstants.Metadata, translate: false);
        UIConstants.EndPanel();
        }
        if (!account.Characters.ContainsKey(characterKey)) characterKey = plugin.ConfigManager.CurrentCharacterKey;
        ImGui.Separator();
        var changed = false;
        bool tabsOpen;
        using (MaterialText.PushLineHeight(new[] { "Targets", "Characters", "Supplies", "Alerts" }.Select(UiText.T).ToArray()))
            tabsOpen = ImGui.BeginTabBar("FishCollectionTabs", ImGuiTabBarFlags.FittingPolicyScroll);
        if (tabsOpen)
        {
            if (UiGui.BeginTabItem("Targets"))
            {
                if (ImGui.BeginChild("FishTargetsBody", Vector2.Zero, false)) changed |= DrawTargets(account);
                ImGui.EndChild();
                ImGui.EndTabItem();
            }
            if (UiGui.BeginTabItem("Characters"))
            {
                if (ImGui.BeginChild("FishCharactersBody", Vector2.Zero, false))
                {
                    UIConstants.Heading("Characters and readiness", plugin.Configuration.CompactUi);
                    foreach (var pair in account.Characters)
                    {
                        var selected = pair.Value.FishCollectionSelected;
                        if (UIConstants.Checkbox(pair.Key + "##select", ref selected))
                        { pair.Value.FishCollectionSelected = selected; plugin.ConfigManager.SaveCurrentAccount(); service.SettingsChanged(); }
                        UIConstants.SameLineIfFits("View");
                        if (UIConstants.Button("View##" + pair.Key)) characterKey = pair.Key;
                        if (service.InspectionFailures.TryGetValue(pair.Key, out var failure)) MaterialText.TextWrapped(UiText.F("Inspection: {0}", UiText.Collection(failure, supplyNames)));
                    }
                    if (!account.Characters.ContainsKey(characterKey)) characterKey = plugin.ConfigManager.CurrentCharacterKey;
                    if (account.Characters.TryGetValue(characterKey, out var character))
                    {
                        UiGui.TextWrapped(UiText.F("Viewing {0}", characterKey));
                        var observation = character.FisherObservation;
                        UiGui.TextWrapped(observation == null ? "Fisher stats: inspection required" :
                            UiText.F($"Level {observation.Level}, Gathering {observation.Gathering}, Perception {observation.Perception}, maximum GP {observation.MaximumGp}, gearset {observation.GearsetId + 1}. Observed {observation.ObservedAtUtc.ToLocalTime():g}; baseline {UiText.T(observation.BaselineCertain ? "known" : "uncertain")}."));
                        var knowledge = service.Observation.Knowledge.GetValueOrDefault(characterKey);
                        UiGui.TextWrapped(knowledge == null ? "Fishing log/unlocks: session inspection required" :
                            UiText.F($"Progress: {service.Catalog.Targets.Count(f => !f.Ocean && knowledge.CaughtItems.Contains(f.ItemId))}/335 big, ") +
                            UiText.F("{0}/13 fabled", service.Catalog.Targets.Count(f => f.Ocean && knowledge.CaughtItems.Contains(f.ItemId))));
                        if (characterKey == plugin.ConfigManager.CurrentCharacterKey && Plugin.PlayerState.ClassJob.RowId == 18 && plugin.IsCharacterRegistered &&
                            UIConstants.Button("Inspect equipped Fisher")) service.Observation.Request();
                    }
                    var policy = (int)settings.BuffPolicy;
                    if (UIConstants.Combo("Stats policy", ref policy, "Gear alone\0Planned buffs\0")) { settings.BuffPolicy = (FisherBuffPolicy)policy; changed = true; }
                }
                ImGui.EndChild();
                ImGui.EndTabItem();
            }
            if (UiGui.BeginTabItem("Supplies"))
            {
                if (ImGui.BeginChild("FishSuppliesBody", Vector2.Zero, false))
                {
                    UIConstants.Heading("Supplies and prices", plugin.Configuration.CompactUi);
                    UiGui.Text(UiText.F($"This preparation: {service.Supplies.GilSpent:N0}/{settings.PreparationGilLimit:N0} gil"));
                    foreach (var supply in service.Supplies.Supplies)
                        UiGui.TextWrapped(UiText.F($"{supply.Name}: target {supply.Target}; {UiText.T(supply.Mandatory ? "mandatory" : "optional")}"));
                    foreach (var failure in service.Supplies.PartialFailures) MaterialText.TextWrapped(UiText.Collection(failure, supplyNames));
                    var bait = settings.BaitUnitPriceLimit; var cordial = settings.CordialUnitPriceLimit;
                    var lure = settings.LureUnitPriceLimit; var total = settings.PreparationGilLimit;
                    UiGui.TextWrapped("Consumable bait gil/unit"); UIConstants.SetNextItemWidth(-1);
                    changed |= UIConstants.InputInt("##BaitUnitPrice", ref bait);
                    UiGui.TextWrapped("Cordial gil/unit"); UIConstants.SetNextItemWidth(-1);
                    changed |= UIConstants.InputInt("##CordialUnitPrice", ref cordial);
                    UiGui.TextWrapped("Reusable lure gil/unit"); UIConstants.SetNextItemWidth(-1);
                    changed |= UIConstants.InputInt("##LureUnitPrice", ref lure);
                    UiGui.TextWrapped("Preparation gil limit (including tax)"); UIConstants.SetNextItemWidth(-1);
                    changed |= UIConstants.InputInt("##PreparationGilLimit", ref total);
                    settings.BaitUnitPriceLimit = Math.Max(0, bait); settings.CordialUnitPriceLimit = Math.Max(0, cordial);
                    settings.LureUnitPriceLimit = Math.Max(0, lure); settings.PreparationGilLimit = Math.Max(0, total);
                    UiGui.TextWrapped("Meals use separate NQ/HQ limits. Blank qualities have no market price authorization. Seeded HQ limits are the sampled North American 75th percentiles; your changes are preserved.");
                    UIConstants.InputText("Find meal", ref foodSearch, 100);
                    if (foodSearch.Length > 1)
                        foreach (var meal in service.Observation.Meals.Where(m => m.Name.Contains(foodSearch, StringComparison.OrdinalIgnoreCase)).Take(24))
                        {
                            var price = settings.FoodPriceLimits.GetValueOrDefault(meal.PriceKey);
                            UiGui.TextWrapped(meal.Name + UiText.T(meal.Hq ? " HQ" : " NQ")); UIConstants.SetNextItemWidth(-1);
                            if (UIConstants.InputInt("##" + meal.PriceKey, ref price))
                            { settings.FoodPriceLimits[meal.PriceKey] = Math.Max(0, price); changed = true; }
                        }
                }
                ImGui.EndChild();
                ImGui.EndTabItem();
            }
            if (UiGui.BeginTabItem("Alerts"))
            {
                if (ImGui.BeginChild("FishAlertsBody", Vector2.Zero, false))
                {
                    UIConstants.Heading("Alerts and last outcome", plugin.Configuration.CompactUi);
                    if (service.LastOutcome.Length > 0) MaterialText.TextWrapped(UiText.F("Last outcome: {0}", UiText.Collection(service.LastOutcome, supplyNames)));
                    UiGui.TextWrapped("Acknowledge alert remains available above these tabs.");
                    var audio = settings.AudioEnabled;
                    changed |= UIConstants.Checkbox("Native audio enabled", ref audio); settings.AudioEnabled = audio;
                    var effect = (int)settings.SoundEffect; var advance = settings.AlertAdvanceMinutes; var repeat = settings.AlertRepeatMinutes;
                    changed |= UiGui.SliderInt("Built-in effect", ref effect, 1, 16);
                    changed |= UIConstants.InputInt("Advance (minutes)", ref advance);
                    changed |= UIConstants.InputInt("Repeat (minutes)", ref repeat);
                    settings.SoundEffect = (uint)effect; settings.AlertAdvanceMinutes = Math.Clamp(advance, 0, 60);
                    settings.AlertRepeatMinutes = Math.Clamp(repeat, 1, 60);
                }
                ImGui.EndChild();
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
        if (changed) { plugin.Configuration.Save(); service.SettingsChanged(); }
    }

    private bool DrawTargets(AccountConfig account)
    {
        var service = plugin.FishCollection;
        var settings = plugin.Configuration.FishCollection;
        var changed = false;
        UIConstants.Heading("Collection targets", plugin.Configuration.CompactUi);
        var protect = settings.RemoveFishFromAutoRetainerLists;
        if (UIConstants.Checkbox("Remove all big/fabled fish from AR discard and sell lists", ref protect))
        {
            settings.RemoveFishFromAutoRetainerLists = protect;
            changed = true;
            if (protect) service.ProtectAutoRetainerFish();
        }
        UiGui.TextWrapped("Applies to every AutoRetainer cleanup plan on enable, login/reload and collection start. Other entries are preserved.");
        if (protect)
        {
            if (UIConstants.Button("Clean lists now")) service.ProtectAutoRetainerFish();
            if (service.AutoRetainerProtectionStatus.Length > 0)
                UiGui.TextWrapped(UiText.Collection(service.AutoRetainerProtectionStatus));
        }
        var tested = service.Catalog.Targets.Count(f => f.Tested);
        UiGui.TextWrapped(UiText.F("Tested: {0}; Untested: {1}", tested, service.Catalog.Targets.Count() - tested));
        UiGui.TextWrapped("Tested means a verified collection catch with completed cleanup. Caught is each character's fishing-log progress.");
        UiGui.TextWrapped("Preparation starts 60 real minutes before the opportunity; arrival goal is 45 minutes before. One character stays committed through preparation and fishing.");
        UiGui.TextWrapped("Viewing character");
        UIConstants.SetNextItemWidth(-1f);
        if (UIConstants.BeginCombo("##ViewingCharacter", characterKey.Length == 0 ? "Choose character" : characterKey))
        {
            foreach (var key in account.Characters.Keys)
                if (UIConstants.WrappedSelectable(key, key == characterKey)) characterKey = key;
            ImGui.EndCombo();
        }
        var big = settings.IncludeBigFish; var fabled = settings.IncludeFabledFish;
        changed |= UIConstants.Checkbox("Big fish", ref big); UIConstants.SameLineIfFits("Fabled fish"); changed |= UIConstants.Checkbox("Fabled fish", ref fabled);
        settings.IncludeBigFish = big; settings.IncludeFabledFish = fabled;
        UIConstants.SetNextItemWidth(-1f);
        UiGui.InputTextWithHint("##FindTarget", "Find target...", ref search, 100);
        UIConstants.Checkbox("Missing only", ref missingOnly); UIConstants.SameLineIfFits("Eligible only"); UIConstants.Checkbox("Eligible only", ref eligibleOnly);
        UIConstants.SetNextItemWidth(-1f);
        if (UIConstants.Combo("Test status", ref testStatusFilter, "All\0Tested\0Untested\0") && testStatusFilter == 1)
            missingOnly = false;
        if (settings.PinnedItemId != 0)
        {
            UiGui.TextWrapped(UiText.F("Pinned: {0}", service.Catalog.Targets.FirstOrDefault(f => f.ItemId == settings.PinnedItemId)?.Name));
            UIConstants.SameLineIfFits("Clear pin"); if (UIConstants.Button("Clear pin")) { settings.PinnedItemId = 0; changed = true; }
        }
        account.Characters.TryGetValue(characterKey, out var viewed);
        var known = service.Observation.Knowledge.GetValueOrDefault(characterKey);
        var targets = service.Catalog.Targets.Where(f => (f.Ocean ? fabled : big) && f.Name.Contains(search, StringComparison.OrdinalIgnoreCase) &&
            (testStatusFilter == 0 || f.Tested == (testStatusFilter == 1)) &&
            (!missingOnly || known?.CaughtItems.Contains(f.ItemId) != true) &&
            (!eligibleOnly || FishCollectionPolicy.Eligible(f, viewed?.FisherObservation, known, settings, service.Observation.Meals))).ToArray();
        UiGui.TextDisabled(UiText.F($"{targets.Length} targets match"));
        if (targets.Length == 0) UiGui.TextWrapped("No targets match these filters. Change the search or readiness filters to show more fish.");
        if (ImGui.BeginTable("fish", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.ScrollY, new Vector2(0, ImGui.GetTextLineHeightWithSpacing() * 9)))
        {
            ImGui.TableSetupColumn("Target", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("Test status", ImGuiTableColumnFlags.WidthFixed,
                Math.Max(MaterialText.Measure(UiText.T("Tested")).X, MaterialText.Measure(UiText.T("Untested")).X) + 16f * UIConstants.Scale);
            ImGui.TableSetupColumn("Readiness", ImGuiTableColumnFlags.WidthFixed, 120f * UIConstants.Scale);
            ImGui.TableSetupColumn("Next window", ImGuiTableColumnFlags.WidthFixed, 130f * UIConstants.Scale);
            ImGui.TableSetupScrollFreeze(0, 1); UiGui.TableHeadersRow();
            foreach (var fish in targets)
            {
                ImGui.PushID((int)fish.ItemId); ImGui.TableNextRow(); ImGui.TableNextColumn();
                var nameHeight = MaterialText.Measure(fish.Name, false, ImGui.GetContentRegionAvail().X).Y;
                var namePosition = ImGui.GetCursorPos();
                if (UiGui.Selectable("##SelectFish", selectedFishId == fish.ItemId, ImGuiSelectableFlags.None, new Vector2(0, nameHeight))) selectedFishId = fish.ItemId;
                if (ImGui.IsItemHovered()) UiGui.SetTooltip(fish.Name);
                ImGui.SetCursorPos(namePosition);
                UiGui.TextWrapped(fish.Name);
                ImGui.TableNextColumn();
                UiGui.TextWrapped(fish.Tested ? "Tested" : "Untested");
                ImGui.TableNextColumn();
                var ready = FishCollectionPolicy.Eligible(fish, viewed?.FisherObservation, known, settings, service.Observation.Meals);
                var readiness = FishCollectionPolicy.Readiness(fish, viewed?.FisherObservation, known, settings, service.Observation.Meals);
                UIConstants.WrappedText(ready ? UIConstants.Metadata : UIConstants.Amber,
                    ready ? UiText.T("Eligible") : viewed?.FisherObservation == null || known == null ? UiText.T("Needs inspection") : UiText.Collection(readiness), translate: false);
                if (ImGui.IsItemHovered()) MaterialText.SetTooltip(UiText.Collection(readiness));
                ImGui.TableNextColumn();
                var opportunity = service.Opportunities.FirstOrDefault(o => o.Fish.ItemId == fish.ItemId);
                UiGui.TextWrapped(fish.AlwaysAvailable ? "Always available" : opportunity?.StartUtc.ToLocalTime().ToString("g", UiText.Current.Culture) ?? "Forecast on Start");
                ImGui.PopID();
            }
            ImGui.EndTable();
        }
        if (service.Catalog.Targets.FirstOrDefault(f => f.ItemId == selectedFishId) is { } selected)
        {
            if (UIConstants.BeginPanel("SelectedFish"))
            {
            UIConstants.Heading(selected.Name, plugin.Configuration.CompactUi);
            UiGui.TextWrapped(UiText.F("Test status: {0}", UiText.T(selected.Tested ? "Tested" : "Untested")));
            if (!targets.Any(f => f.ItemId == selectedFishId)) UiGui.TextDisabled("Selected target is outside the current filters.");
            MaterialText.TextWrapped(UiText.Collection(FishCollectionPolicy.Readiness(selected, viewed?.FisherObservation, known, settings, service.Observation.Meals)));
            UiGui.TextWrapped(UiText.F($"{UiText.T(selected.Ocean ? "Fabled ocean fish" : "Big fish")}; minimum Gathering {selected.MinimumGathering}; recommended {selected.RecommendedGathering}."));
            var opportunity = service.Opportunities.FirstOrDefault(o => o.Fish.ItemId == selected.ItemId);
            UiGui.TextWrapped(selected.AlwaysAvailable ? "Always available" : opportunity == null ? "Forecast on Start" : UiText.F($"Next window: {opportunity.StartUtc.ToLocalTime():g} to {opportunity.EndUtc.ToLocalTime():g}"));
            if (UIConstants.Button("Pin selected fish")) { settings.PinnedItemId = selected.ItemId; changed = true; }
            if (selected.RecommendedGathering > 0)
            {
                var allow = settings.RecommendationOverrides.Contains(selected.ItemId);
                if (UIConstants.Checkbox($"Override {selected.RecommendedGathering} recommendation", ref allow))
                { if (allow) settings.RecommendationOverrides.Add(selected.ItemId); else settings.RecommendationOverrides.Remove(selected.ItemId); changed = true; }
            }
            UIConstants.EndPanel();
            }
        }
        else UiGui.TextWrapped("Select a fish to see readiness and pin or override its recommendation.");
        if (UIConstants.CollapsingHeading("Catalog sources"))
            foreach (var source in service.Catalog.Sources) UiGui.TextWrapped(source.Key + ": " + source.Value);
        return changed;
    }
}
