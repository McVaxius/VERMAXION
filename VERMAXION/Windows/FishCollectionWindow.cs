using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using VERMAXION.Models;

namespace VERMAXION.Windows;

internal sealed class FishCollectionWindow(Plugin plugin) : Window("Big and fabled fish collection##VMX", ImGuiWindowFlags.None)
{
    private string search = "", foodSearch = "", characterKey = "";
    private bool missingOnly = true, eligibleOnly;
    public override void Draw()
    {
        var service = plugin.FishCollection;
        var settings = plugin.Configuration.FishCollection;
        var account = plugin.ConfigManager.GetCurrentAccount();
        if (account == null) { ImGui.TextWrapped("Select and register the current account first."); return; }
        ImGui.TextWrapped(service.Status);
        if (!service.IsActive)
        { if (ImGui.Button("Start collection")) service.Start(); }
        else
        { if (ImGui.Button("Stop collection")) service.Stop(); }
        ImGui.SameLine(); if (ImGui.Button("Acknowledge alert")) service.Acknowledge();
        ImGui.TextWrapped("Preparation starts 60 real minutes before the opportunity; arrival goal is 45 minutes before. One character stays committed through preparation and fishing.");
        if (service.Assignment is { } assignment)
            ImGui.TextWrapped($"Committed: {assignment.CharacterKey} — {assignment.Opportunity.Fish.Name}; window {assignment.Opportunity.StartUtc.ToLocalTime():g} to {assignment.Opportunity.EndUtc.ToLocalTime():g}");
        if (service.LastOutcome.Length > 0) ImGui.TextWrapped("Last outcome: " + service.LastOutcome);
        var changed = false;
        if (ImGui.CollapsingHeader("Characters and readiness", ImGuiTreeNodeFlags.DefaultOpen))
        {
            foreach (var pair in account.Characters)
            {
                var selected = pair.Value.FishCollectionSelected;
                if (ImGui.Checkbox(pair.Key + "##select", ref selected))
                { pair.Value.FishCollectionSelected = selected; plugin.ConfigManager.SaveCurrentAccount(); service.SettingsChanged(); }
                ImGui.SameLine();
                if (ImGui.SmallButton("View##" + pair.Key)) characterKey = pair.Key;
                if (service.InspectionFailures.TryGetValue(pair.Key, out var failure)) ImGui.TextWrapped("Inspection: " + failure);
            }
            if (!account.Characters.ContainsKey(characterKey)) characterKey = plugin.ConfigManager.CurrentCharacterKey;
            if (account.Characters.TryGetValue(characterKey, out var character))
            {
                ImGui.Text("Viewing " + characterKey);
                var observation = character.FisherObservation;
                ImGui.TextWrapped(observation == null ? "Fisher stats: inspection required" :
                    $"Level {observation.Level}, Gathering {observation.Gathering}, Perception {observation.Perception}, maximum GP {observation.MaximumGp}, gearset {observation.GearsetId + 1}. Observed {observation.ObservedAtUtc.ToLocalTime():g}; baseline {(observation.BaselineCertain ? "known" : "uncertain")}.");
                var knowledge = service.Observation.Knowledge.GetValueOrDefault(characterKey);
                ImGui.Text(knowledge == null ? "Fishing log/unlocks: session inspection required" :
                    $"Progress: {service.Catalog.Targets.Count(f => !f.Ocean && knowledge.CaughtItems.Contains(f.ItemId))}/335 big, " +
                    $"{service.Catalog.Targets.Count(f => f.Ocean && knowledge.CaughtItems.Contains(f.ItemId))}/13 fabled");
                if (characterKey == plugin.ConfigManager.CurrentCharacterKey && Plugin.PlayerState.ClassJob.RowId == 18 && plugin.IsCharacterRegistered &&
                    ImGui.Button("Inspect equipped Fisher")) service.Observation.Request();
            }
            var policy = (int)settings.BuffPolicy;
            if (ImGui.Combo("Stats policy", ref policy, "Gear alone\0Planned buffs\0")) { settings.BuffPolicy = (FisherBuffPolicy)policy; changed = true; }
        }
        if (ImGui.CollapsingHeader("Supplies and prices"))
        {
            ImGui.Text($"This preparation: {service.Supplies.GilSpent:N0}/{settings.PreparationGilLimit:N0} gil");
            foreach (var supply in service.Supplies.Supplies)
                ImGui.TextWrapped($"{supply.Name}: target {supply.Target}; {(supply.Mandatory ? "mandatory" : "optional")}");
            foreach (var failure in service.Supplies.PartialFailures) ImGui.TextWrapped(failure);
            var bait = settings.BaitUnitPriceLimit; var cordial = settings.CordialUnitPriceLimit;
            var lure = settings.LureUnitPriceLimit; var total = settings.PreparationGilLimit;
            changed |= ImGui.InputInt("Consumable bait gil/unit", ref bait);
            changed |= ImGui.InputInt("Cordial gil/unit", ref cordial);
            changed |= ImGui.InputInt("Reusable lure gil/unit", ref lure);
            changed |= ImGui.InputInt("Preparation gil limit (including tax)", ref total);
            settings.BaitUnitPriceLimit = Math.Max(0, bait); settings.CordialUnitPriceLimit = Math.Max(0, cordial);
            settings.LureUnitPriceLimit = Math.Max(0, lure); settings.PreparationGilLimit = Math.Max(0, total);
            ImGui.TextWrapped("Meals use separate NQ/HQ limits. Blank qualities have no market price authorization. Seeded HQ limits are the sampled North American 75th percentiles; your changes are preserved.");
            ImGui.InputText("Find meal", ref foodSearch, 100);
            if (foodSearch.Length > 1)
                foreach (var meal in service.Observation.Meals.Where(m => m.Name.Contains(foodSearch, StringComparison.OrdinalIgnoreCase)).Take(24))
                {
                    var price = settings.FoodPriceLimits.GetValueOrDefault(meal.PriceKey);
                    if (ImGui.InputInt(meal.Name + (meal.Hq ? " HQ" : " NQ") + "##" + meal.PriceKey, ref price))
                    { settings.FoodPriceLimits[meal.PriceKey] = Math.Max(0, price); changed = true; }
                }
        }
        if (ImGui.CollapsingHeader("Audio alerts"))
        {
            var audio = settings.AudioEnabled;
            changed |= ImGui.Checkbox("Native audio enabled", ref audio); settings.AudioEnabled = audio;
            var effect = (int)settings.SoundEffect; var advance = settings.AlertAdvanceMinutes; var repeat = settings.AlertRepeatMinutes;
            changed |= ImGui.SliderInt("Built-in effect", ref effect, 1, 16);
            changed |= ImGui.InputInt("Advance (minutes)", ref advance);
            changed |= ImGui.InputInt("Repeat (minutes)", ref repeat);
            settings.SoundEffect = (uint)effect; settings.AlertAdvanceMinutes = Math.Clamp(advance, 0, 60);
            settings.AlertRepeatMinutes = Math.Clamp(repeat, 1, 60);
        }
        ImGui.Separator();
        var big = settings.IncludeBigFish; var fabled = settings.IncludeFabledFish;
        changed |= ImGui.Checkbox("Big fish", ref big); ImGui.SameLine(); changed |= ImGui.Checkbox("Fabled fish", ref fabled);
        settings.IncludeBigFish = big; settings.IncludeFabledFish = fabled;
        ImGui.InputText("Find target", ref search, 100);
        ImGui.Checkbox("Missing only", ref missingOnly); ImGui.SameLine(); ImGui.Checkbox("Eligible only", ref eligibleOnly);
        if (settings.PinnedItemId != 0)
        {
            ImGui.Text("Pinned: " + service.Catalog.Targets.FirstOrDefault(f => f.ItemId == settings.PinnedItemId)?.Name);
            ImGui.SameLine(); if (ImGui.SmallButton("Clear pin")) { settings.PinnedItemId = 0; changed = true; }
        }
        account.Characters.TryGetValue(characterKey, out var viewed);
        var known = service.Observation.Knowledge.GetValueOrDefault(characterKey);
        if (ImGui.BeginTable("fish", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.ScrollY, new Vector2(0, 300)))
        {
            ImGui.TableSetupColumn("Target"); ImGui.TableSetupColumn("Readiness"); ImGui.TableSetupColumn("Next window"); ImGui.TableSetupColumn("Target controls"); ImGui.TableHeadersRow();
            foreach (var fish in service.Catalog.Targets.Where(f => (f.Ocean ? fabled : big) && f.Name.Contains(search, StringComparison.OrdinalIgnoreCase) &&
                (!missingOnly || known?.CaughtItems.Contains(f.ItemId) != true) &&
                (!eligibleOnly || FishCollectionPolicy.Eligible(f, viewed?.FisherObservation, known, settings, service.Observation.Meals))))
            {
                ImGui.PushID((int)fish.ItemId); ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.Text(fish.Name);
                ImGui.TableNextColumn(); ImGui.TextWrapped(FishCollectionPolicy.Readiness(fish, viewed?.FisherObservation, known, settings, service.Observation.Meals));
                ImGui.TableNextColumn();
                var opportunity = service.Opportunities.FirstOrDefault(o => o.Fish.ItemId == fish.ItemId);
                ImGui.TextWrapped(fish.AlwaysAvailable ? "Always available" : opportunity?.StartUtc.ToLocalTime().ToString("g") ?? "Forecast on Start");
                ImGui.TableNextColumn(); if (ImGui.SmallButton("Pin")) { settings.PinnedItemId = fish.ItemId; changed = true; }
                if (fish.RecommendedGathering > 0)
                {
                    var allow = settings.RecommendationOverrides.Contains(fish.ItemId);
                    if (ImGui.Checkbox($"Override {fish.RecommendedGathering} recommendation", ref allow))
                    { if (allow) settings.RecommendationOverrides.Add(fish.ItemId); else settings.RecommendationOverrides.Remove(fish.ItemId); changed = true; }
                }
                ImGui.PopID();
            }
            ImGui.EndTable();
        }
        if (changed) { plugin.Configuration.Save(); service.SettingsChanged(); }
        if (ImGui.CollapsingHeader("Catalog sources"))
            foreach (var source in service.Catalog.Sources) ImGui.TextWrapped(source.Key + ": " + source.Value);
    }
}
