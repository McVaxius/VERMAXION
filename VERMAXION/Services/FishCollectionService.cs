using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;
using VERMAXION.Models;

namespace VERMAXION.Services;

internal sealed unsafe class FishCollectionService : IDisposable
{
    private enum Phase { Idle, Inspect, Select, Equip, Repair, Supplies, Travel, Position, AwaitWindow, Fish, Ocean, Cleanup, Return }
    private readonly Plugin plugin;
    private readonly FishCollectionForecast forecast;
    private readonly FishCollectionAutoHook hook;
    public FishCollectionCatalog Catalog { get; }
    public FishCollectionObservation Observation { get; }
    public FishCollectionSupplies Supplies { get; }
    public FishAssignment? Assignment { get; private set; }
    public IReadOnlyList<FishOpportunity> Opportunities { get; private set; } = [];
    public Dictionary<string, string> InspectionFailures { get; } = new();
    public string Status { get; private set; } = "Stopped";
    public string LastOutcome { get; private set; } = "";
    public bool IsActive => phase != Phase.Idle;
    public bool IsCleanupPending => phase is Phase.Cleanup or Phase.Return;
    public bool Running { get; private set; }
    private Phase phase;
    private string accountId = "", inspectionKey = "";
    private DateTimeOffset entered, tickAfter, equipSent, readySince, castSent, mealSent, nextAlert;
    private DateTimeOffset forecastAfter;
    private Task<FishOpportunity[]>? forecastTask;
    private FisherMeal? meal;
    private FisherObservation? preparedStats;
    private int mealBefore;
    private bool gearsetRequested, navigationOwned, travelOwned, repairOwned, oceanStarted, oceanTargetSeen;
    private bool alertAcknowledged, foodVerified, beforeCastObserved, foodRefreshing;
    private DateTimeOffset readinessObserved;
    private uint foodParam, targetZone;
    private readonly HashSet<string> attempted = new();
    private CollectionSpot? spot;
    private string returnCommand = "";
    private uint returnTerritory;
    private bool returnActivityObserved;
    private int returnCommandsSent;
    private bool arrivalGoalReported;
    private bool returnStarted;

    public FishCollectionService(Plugin plugin)
    {
        this.plugin = plugin;
        Catalog = FishCollectionCatalog.Load();
        Observation = new(plugin, Catalog);
        if (Observation.ValidationError.Length > 0) Status = Observation.ValidationError;
        forecast = new(Catalog); hook = new(plugin); Supplies = new(plugin);
    }
    public bool Start()
    {
        if (IsActive) return false;
        if (Observation.ValidationError.Length > 0) { Status = Observation.ValidationError; return false; }
        if (!plugin.CanStartMainMenuTest(false, out var reason)) { Status = reason; return false; }
        if (!plugin.IsCharacterRegistered || plugin.ConfigManager.GetCurrentAccount() is not { } account ||
            !account.Characters.Any(c => c.Value.FishCollectionSelected))
        { Status = "Select at least one registered character on the current account"; return false; }
        var now = DateTimeOffset.UtcNow;
        if (!plugin.FishingRunLifecycle.TryBegin(FishingRunMode.Collection, FishingStartupTrigger.Manual,
            OceanFishingProvider.VermaxionAutoHook, plugin.ConfigManager.CurrentCharacterKey, now, now.AddDays(32), out reason))
        { Status = reason; return false; }
        accountId = plugin.ConfigManager.CurrentAccountId;
        Running = true; Assignment = null; InspectionFailures.Clear(); attempted.Clear();
        forecastAfter = default; LastOutcome = "";
        Set(Phase.Inspect, "Inspecting selected characters before assigning fish or purchasing supplies");
        return true;
    }
    public void Stop(string reason = "Stopped by user")
    {
        Running = false;
        if (phase == Phase.Return) { LastOutcome = reason; Status = reason + "; waiting for owned return verification"; }
        else if (IsActive) EndAttempt(reason);
        else Status = reason;
    }
    public void Acknowledge() => alertAcknowledged = true;
    public void SettingsChanged() { forecastAfter = default; }
    private string CurrentKey => plugin.ConfigManager.CurrentCharacterKey;
    private CharacterConfig CurrentConfig => plugin.ConfigManager.GetCurrentCharacterConfig(CurrentKey);
    private static bool WorldReady => GameHelpers.IsPlayerAvailable() && !Plugin.Condition[ConditionFlag.BetweenAreas] &&
        !Plugin.Condition[ConditionFlag.BetweenAreas51];
    private static string AttemptKey(FishAssignment a) => a.Opportunity.Key;
    private void Set(Phase next, string status)
    {
        phase = next; entered = DateTimeOffset.UtcNow; Status = status;
        Plugin.Log.Information($"[FishCollection] {next}: {status}");
    }
    public void Update()
    {
        try { Observation.Update(); }
        catch (Exception error) { Status = $"Observation unavailable: {error.GetBaseException().Message}"; }
        var now = DateTimeOffset.UtcNow;
        if (!IsActive || now < tickAfter) return;
        tickAfter = now.AddMilliseconds(250);
        try
        {
            if (phase == Phase.Cleanup) { TickCleanup(); return; }
            if (phase == Phase.Return) { TickReturn(); return; }
            if (plugin.ConfigManager.CurrentAccountId != accountId) { Stop("Current account changed"); return; }
            if (!Plugin.ClientState.IsLoggedIn && !plugin.FishingRelogCoordinator.IsActive)
            { Stop("Character disconnected during collection"); return; }
            if (plugin.FishingRelogCoordinator.IsFailed)
            {
                var failure = plugin.FishingRelogCoordinator.FailureReason;
                plugin.FishingRelogCoordinator.Reset();
                if (Assignment != null) EndAttempt($"Character relog failed: {failure}");
                else { InspectionFailures[inspectionKey] = failure; inspectionKey = ""; Set(Phase.Inspect, failure); }
                return;
            }
            if (plugin.FishingRelogCoordinator.IsActive || !plugin.IsCharacterRegistered || !WorldReady) return;
            if (Assignment != null && CurrentKey != Assignment.CharacterKey)
                throw new InvalidOperationException("The committed character changed during this opportunity.");
            if (Assignment is { } assignment && phase != Phase.Ocean && now >= assignment.Opportunity.EndUtc)
            { EndAttempt("Opportunity ended"); return; }
            Alert(now);
            switch (phase)
            {
                case Phase.Inspect: Inspect(); break;
                case Phase.Select: Select(now); break;
                case Phase.Equip:
                    if (!Equip(Assignment!.CharacterKey)) break;
                    var observation = Observation.Capture();
                    var fish = Assignment!.Opportunity.Fish;
                    if (!FishCollectionPolicy.Eligible(fish, observation, Observation.Knowledge.GetValueOrDefault(CurrentKey),
                        plugin.Configuration.FishCollection, Observation.Meals))
                        throw new InvalidOperationException(FishCollectionPolicy.Readiness(fish, observation,
                            Observation.Knowledge.GetValueOrDefault(CurrentKey), plugin.Configuration.FishCollection, Observation.Meals));
                    meal = ChooseMeal(fish, observation);
                    preparedStats = meal?.Apply(observation) ?? observation;
                    hook.Prepare(fish, preparedStats);
                    PrepareSupplies(fish);
                    Set(Phase.Repair, "Checking equipped gear and configured repair policy");
                    break;
                case Phase.Repair: Repair(); break;
                case Phase.Supplies:
                    Supplies.Update(); Status = Supplies.Status;
                    if (!Supplies.IsComplete) break;
                    if (Supplies.Failure.Length > 0) throw new InvalidOperationException(Supplies.Failure);
                    if (!plugin.FishingRunLifecycle.ResumeYesAlreadyPauseAfterShopping("collection supplies"))
                        throw new InvalidOperationException("Could not restore the fishing YesAlready lease.");
                    if (!NavigationSettled()) break;
                    if (Assignment!.Opportunity.Fish.Ocean) BeginOcean();
                    else { spot = Catalog.Spots.Single(s => s.SpotId == Assignment.Opportunity.Fish.SpotId); Set(Phase.Travel, "Traveling to " + spot.Name); }
                    break;
                case Phase.Travel: Travel(); break;
                case Phase.Position: Position(); break;
                case Phase.AwaitWindow:
                    Status = $"Positioned for {Assignment!.Opportunity.Fish.Name}; starts {Assignment.Opportunity.StartUtc.ToLocalTime():g}";
                    if (now < Assignment.Opportunity.StartUtc.AddSeconds(-(Assignment.Opportunity.Fish.IntuitionSeconds ?? 0))) break;
                    if (!VerifyFoodAndReadiness()) break;
                    hook.StartFishing(); castSent = now; alertAcknowledged = true;
                    Set(Phase.Fish, "Fishing for " + Assignment.Opportunity.Fish.Name); break;
                case Phase.Fish:
                    if (Observation.Caught(Assignment!.Opportunity.Fish.ItemId)) { Observation.ObserveLog(); EndAttempt("Target caught"); break; }
                    VerifyChangedReadiness();
                    if (hook.RequiredBait.Any(id => FishCollectionObservation.Count(id) == 0))
                        throw new InvalidOperationException("A mandatory strategy bait was exhausted.");
                    if (!Plugin.Condition[ConditionFlag.Fishing] && now - castSent > TimeSpan.FromSeconds(30))
                        throw new InvalidOperationException("Casting was not observed; check the casting position and preset.");
                    if (foodRefreshing || FoodNeedsRefresh()) RefreshFood();
                    else Status = FishingStatus();
                    break;
                case Phase.Ocean: TickOcean(); break;
            }
        }
        catch (Exception error) { EndAttempt(error.GetBaseException().Message); }
    }
    private void Inspect()
    {
        var account = plugin.ConfigManager.GetCurrentAccount()!;
        if (inspectionKey.Length == 0)
        {
            inspectionKey = account.Characters.Where(c => c.Value.FishCollectionSelected &&
                !InspectionFailures.ContainsKey(c.Key) && (c.Value.FisherObservation is not { BaselineCertain: true } ||
                !Observation.Knowledge.ContainsKey(c.Key))).OrderByDescending(c => c.Key == CurrentKey)
                .Select(c => c.Key).FirstOrDefault() ?? "";
            gearsetRequested = false; readySince = default;
        }
        if (inspectionKey.Length == 0) { Set(Phase.Select, "Forecasting missing fish"); return; }
        if (CurrentKey != inspectionKey)
        {
            if (!plugin.FishingRelogCoordinator.RequestRelog(inspectionKey, DateTimeOffset.UtcNow.AddMinutes(3)))
                throw new InvalidOperationException("Character inspection relog was rejected.");
            return;
        }
        try
        {
            if (!Equip(inspectionKey)) return;
            Observation.Capture(); inspectionKey = ""; gearsetRequested = false; readySince = default;
        }
        catch (Exception error)
        { InspectionFailures[inspectionKey] = error.GetBaseException().Message; inspectionKey = ""; gearsetRequested = false; }
    }
    private bool Equip(string key)
    {
        if (!gearsetRequested)
        {
            var saved = plugin.ConfigManager.GetCurrentCharacterConfig(key).FisherObservation;
            var module = RaptureGearsetModule.Instance();
            if (module == null) throw new InvalidOperationException("Fisher gearsets are unavailable.");
            var savedExists = false;
            for (var i = 0; i < 100; i++)
                if (saved is { GearsetId: >= 0 } && module->Entries[i].Id == saved.GearsetId && module->Entries[i].ClassJob == 18 &&
                    (module->Entries[i].Flags & RaptureGearsetModule.GearsetFlag.Exists) != 0) savedExists = true;
            var gearset = savedExists ? saved!.GearsetId : plugin.FisherGearsetRuntime.FindFirstSavedFisherGearset().Selection?.Id ?? -1;
            if (gearset < 0) throw new InvalidOperationException("No saved Fisher gearset is available.");
            if (saved is { GearsetId: >= 0 } && gearset != saved.GearsetId)
                throw new InvalidOperationException("Last observed Fisher gearset is no longer saved; inspect a replacement.");
            var result = plugin.FisherGearsetRuntime.EquipGearset(gearset);
            if (!result.Available || result.Result < 0) throw new InvalidOperationException("Fisher gearset activation failed: " + result.Error);
            gearsetRequested = true; equipSent = DateTimeOffset.UtcNow; readySince = default; Observation.Request();
            return false;
        }
        if (DateTimeOffset.UtcNow - equipSent > TimeSpan.FromSeconds(30)) throw new InvalidOperationException("Fisher gearset did not settle.");
        if (Plugin.PlayerState.ClassJob.RowId != 18 || Plugin.Condition[ConditionFlag.OccupiedInEvent]) { readySince = default; return false; }
        if (readySince == default) readySince = DateTimeOffset.UtcNow;
        return DateTimeOffset.UtcNow - readySince >= TimeSpan.FromSeconds(2);
    }
    private void Select(DateTimeOffset now)
    {
        var account = plugin.ConfigManager.GetCurrentAccount()!;
        if (account.Characters.Any(c => c.Value.FishCollectionSelected && !InspectionFailures.ContainsKey(c.Key) &&
            (!Observation.Knowledge.ContainsKey(c.Key) || c.Value.FisherObservation is not { BaselineCertain: true })))
        { Set(Phase.Inspect, "Selected character requires inspection"); return; }
        if (forecastTask is { IsCompleted: true })
        {
            Opportunities = forecastTask.GetAwaiter().GetResult(); forecastTask = null;
        }
        if (forecastTask == null && now >= forecastAfter)
        {
            var ocean = Catalog.Targets.Where(f => f.Ocean).Select(f => Observation.NextOcean(f, now)).OfType<FishOpportunity>().ToArray();
            forecastTask = Task.Run(() => Catalog.Targets.Where(f => !f.Ocean).Select(f => forecast.Next(f, now))
                .OfType<FishOpportunity>().Concat(ocean).ToArray());
            forecastAfter = now.AddMinutes(1);
            if (Opportunities.Count == 0) return;
        }
        var settings = plugin.Configuration.FishCollection;
        var candidates = account.Characters.Where(c => c.Value.FishCollectionSelected && !InspectionFailures.ContainsKey(c.Key))
            .SelectMany(c => Opportunities.Where(o => (o.Fish.Ocean ? settings.IncludeFabledFish : settings.IncludeBigFish) &&
                FishCollectionPolicy.Eligible(o.Fish, c.Value.FisherObservation, Observation.Knowledge.GetValueOrDefault(c.Key), settings, Observation.Meals))
                .Select(o => new FishAssignment(c.Key, o))).Where(a => !attempted.Contains(AttemptKey(a))).ToArray();
        var assignment = FishCollectionPolicy.Choose(candidates, settings.PinnedItemId, CurrentKey, now);
        if (assignment == null)
        {
            var next = candidates.OrderBy(a => a.Opportunity.PreparationUtc).FirstOrDefault();
            Status = next == null ? "No eligible missing fish; review readiness and inspection failures" :
                $"Waiting to prepare {next.Opportunity.Fish.Name} at {next.Opportunity.PreparationUtc.ToLocalTime():g}";
            return;
        }
        assignment = FishCollectionPolicy.BoundAlwaysAvailable(assignment, candidates, settings.PinnedItemId, now);
        Assignment = assignment; gearsetRequested = false; readySince = default;
        meal = null; preparedStats = null; mealSent = castSent = default; foodVerified = alertAcknowledged = beforeCastObserved = foodRefreshing = false;
        oceanStarted = oceanTargetSeen = false; nextAlert = default;
        arrivalGoalReported = returnStarted = false;
        var context = plugin.FishingRunLifecycle.Current!;
        plugin.FishingRunLifecycle.ClearWindowOutcome(context.RegistrationStartUtc);
        plugin.FishingRunLifecycle.ClearWindowOutcome(assignment.Opportunity.StartUtc);
        context.QueueRegistrationConfirmed = context.TerminalFailureBeforeQueueConfirmation = false;
        context.TargetCharacterKey = assignment.CharacterKey; context.RegistrationStartUtc = assignment.Opportunity.StartUtc;
        context.RegistrationDeadlineUtc = assignment.Opportunity.EndUtc;
        Set(Phase.Equip, $"Committed {assignment.CharacterKey} to {assignment.Opportunity.Fish.Name}");
        if (CurrentKey != assignment.CharacterKey && !plugin.FishingRelogCoordinator.RequestRelog(assignment.CharacterKey, assignment.Opportunity.EndUtc))
            throw new InvalidOperationException("Committed-character relog was rejected.");
    }
    private FisherMeal? ChooseMeal(CollectionFish fish, FisherObservation baseline)
    {
        if (baseline.Gathering >= fish.RequiredGathering(plugin.Configuration.FishCollection)) return null;
        return Observation.Meals.Where(m => Plugin.DataManager.GetExcelSheet<Item>().GetRow(m.ItemId).LevelEquip <= baseline.Level &&
                m.Apply(baseline).Gathering >= fish.RequiredGathering(plugin.Configuration.FishCollection))
            .OrderByDescending(m => FishCollectionObservation.Count(m.ItemId, m.Hq) > 0)
            .ThenBy(m => plugin.Configuration.FishCollection.FoodPriceLimits.GetValueOrDefault(m.PriceKey, int.MaxValue))
            .FirstOrDefault() ?? throw new InvalidOperationException("No native meal can qualify this character.");
    }
    private void PrepareSupplies(CollectionFish fish)
    {
        var settings = plugin.Configuration.FishCollection;
        var supplies = new List<CollectionSupply>();
        foreach (var id in hook.RequiredBait)
        {
            var item = Plugin.DataManager.GetExcelSheet<Item>().GetRow(id);
            var lure = Catalog.ReusableBait.Contains(id);
            var configured = CurrentConfig.FishingStockItems.GetValueOrDefault(id);
            var target = configured is { Target: > 0 } ? configured.Target : lure ? 2 : 99;
            supplies.Add(new(id, item.Name.ExtractText(), null, target, true, lure ? settings.LureUnitPriceLimit : settings.BaitUnitPriceLimit));
        }
        foreach (var useId in hook.Cordials)
        {
            var id = useId % 1_000_000;
            var cordial = Plugin.DataManager.GetExcelSheet<Item>().GetRow(id);
            supplies.Add(new(id, cordial.Name.ExtractText(), useId >= 1_000_000,
                CurrentConfig.FishingStockItems.GetValueOrDefault(id)?.Target is > 0 and var targetCount ? targetCount : 10,
                false, settings.CordialUnitPriceLimit));
        }
        if (meal != null)
        {
            var start = Assignment!.Opportunity.StartUtc.AddSeconds(-(fish.IntuitionSeconds ?? 0));
            if (start < DateTimeOffset.UtcNow) start = DateTimeOffset.UtcNow;
            var quantity = FishCollectionPolicy.MealQuantity(fish.Ocean ? TimeSpan.FromMinutes(45) : Assignment.Opportunity.EndUtc - start);
            supplies.Add(new(meal.ItemId, meal.Name + (meal.Hq ? " HQ" : " NQ"), meal.Hq,
                quantity, true, settings.FoodPriceLimits.GetValueOrDefault(meal.PriceKey), quantity));
        }
        Supplies.Begin(supplies);
    }
    private void Repair()
    {
        if (repairOwned)
        {
            var result = plugin.AdsIpcClient.Refresh();
            if (!result.StatusReadable || result.UtilityRunning) { if (DateTimeOffset.UtcNow - entered > TimeSpan.FromMinutes(5)) Stop("Repair did not settle"); return; }
            if (DateTimeOffset.UtcNow - entered < TimeSpan.FromSeconds(3)) return;
            repairOwned = false;
            if (!GameHelpers.TryGetLowestEquippedGearConditionPercent(out var repaired) || repaired <= CurrentConfig.FishingRepairThresholdPercent)
                throw new InvalidOperationException("Repair finished without qualifying equipped durability.");
        }
        else
        {
            if (!GameHelpers.TryGetLowestEquippedGearConditionPercent(out var durability)) throw new InvalidOperationException("Equipped durability is unavailable.");
            var settings = new FishingOperationSettings(CurrentConfig.FishingLureRestockTarget, CurrentConfig.FishingReturnDestination,
                CurrentConfig.FishingReturnCommand, CurrentConfig.FishingRepairMode, CurrentConfig.FishingRepairThresholdPercent);
            var decision = FishingOperationPolicy.EvaluateRepair(settings, true, durability);
            if (decision.ShouldRepair)
            {
                if (!plugin.AdsIpcClient.StartRepair(decision.AdsMode, out var error)) throw new InvalidOperationException("Repair rejected: " + error);
                repairOwned = true; entered = DateTimeOffset.UtcNow; return;
            }
            if (durability <= 0) throw new InvalidOperationException("Fishing gear is broken.");
        }
        Set(Phase.Supplies, "Acquiring only this attempt's supplies");
    }
    private bool NavigationSettled()
        => plugin.VNavmeshIPC.TryGetPathIsRunning(out var running) && !running &&
           plugin.VNavmeshIPC.TryGetPathfindInProgress(out var pending) && !pending &&
           plugin.LifestreamIPC.TryReadBusy(out var busy) && !busy;
    private void Travel()
    {
        if (Plugin.ClientState.TerritoryType == spot!.TerritoryId && WorldReady && NavigationSettled())
        { travelOwned = false; Set(Phase.Position, "Moving to the sourced casting position"); return; }
        if (DateTimeOffset.UtcNow - entered > TimeSpan.FromMinutes(3)) throw new InvalidOperationException("Fishing territory travel timed out.");
        if (travelOwned) return;
        var aetheryte = CustomDeliveries.Map.FindClosestAetheryte(spot.TerritoryId, spot.Point);
        var primary = aetheryte == 0 ? 0 : CustomDeliveries.Map.FindPrimaryAetheryte(aetheryte);
        if (primary == 0) throw new InvalidOperationException("No accessible native travel route to this fishing territory.");
        var row = Plugin.DataManager.GetExcelSheet<Aetheryte>().GetRow(primary);
        if (!plugin.LifestreamIPC.ExecuteCommand(row.PlaceName.Value.Name.ExtractText()))
            throw new InvalidOperationException("Fishing territory travel was rejected.");
        travelOwned = true;
    }
    private void Position()
    {
        if (Plugin.ClientState.TerritoryType != spot!.TerritoryId) throw new InvalidOperationException("Fishing territory changed during positioning.");
        var player = Plugin.ObjectTable.LocalPlayer!;
        if (Vector3.Distance(player.Position, spot.Point) <= .75f)
        {
            if (navigationOwned) plugin.VNavmeshIPC.Stop();
            if (!NavigationSettled()) return;
            navigationOwned = false;
            ((FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)player.Address)->SetRotation(spot.Rotation!.Value);
            var framework = EventFramework.Instance();
            if (framework == null || framework->EventHandlerModule.FishingEventHandler == null || !framework->EventHandlerModule.FishingEventHandler->CanFish)
                throw new InvalidOperationException("Casting is unavailable at the sourced position and facing.");
            ReportArrival();
            Set(Phase.AwaitWindow, "Casting position and facing verified"); return;
        }
        if (DateTimeOffset.UtcNow - entered > TimeSpan.FromMinutes(5)) throw new InvalidOperationException("Casting-position navigation timed out.");
        if (!navigationOwned)
        {
            if (!plugin.VNavmeshIPC.TryGetNavReady(out var ready) || !ready) return;
            if (!plugin.VNavmeshIPC.PathfindAndMoveTo(spot.Point)) throw new InvalidOperationException("Casting-position navigation was rejected.");
            navigationOwned = true;
        }
        else if (NavigationSettled()) throw new InvalidOperationException("Navigation stopped before the casting position was reached.");
    }
    private bool VerifyFoodAndReadiness()
    {
        if (foodVerified) return FoodStillQualifies();
        if (!beforeCastObserved)
        {
            var observed = Observation.Capture();
            if (!GameHelpers.TryGetLowestEquippedGearConditionPercent(out var durability) || durability <= 0)
                throw new InvalidOperationException("Equipped fishing durability is unavailable or broken.");
            if (!QualifiesStrategy(observed))
                throw new InvalidOperationException("Equipped Fisher readiness no longer qualifies.");
            readinessObserved = observed.ObservedAtUtc; beforeCastObserved = true;
        }
        if (meal == null)
        {
            if (Observation.Effective().Gathering < Assignment!.Opportunity.Fish.RequiredGathering(plugin.Configuration.FishCollection))
                throw new InvalidOperationException("Equipped Gathering no longer qualifies.");
            foodVerified = true; return true;
        }
        var expectedParam = meal.FoodId + (meal.Hq ? 10000u : 0);
        var status = Plugin.ObjectTable.LocalPlayer!.StatusList.FirstOrDefault(s => s.StatusId == 48);
        if (status != null && status.Param == expectedParam && status.RemainingTime >= 120 &&
            (mealSent == default || FishCollectionObservation.Count(meal.ItemId, meal.Hq) < mealBefore))
        {
            if (Observation.Effective().Gathering < Assignment!.Opportunity.Fish.RequiredGathering(plugin.Configuration.FishCollection))
                throw new InvalidOperationException("Effective Gathering after the exact meal does not qualify.");
            foodParam = expectedParam; foodVerified = true; return true;
        }
        if (mealSent != default)
        {
            if (DateTimeOffset.UtcNow - mealSent > TimeSpan.FromSeconds(10)) throw new InvalidOperationException("Exact food receipt, quality, effect or duration was not verified after consumption.");
            return false;
        }
        if (FishCollectionObservation.Count(meal.ItemId, meal.Hq) == 0) throw new InvalidOperationException("The exact qualifying food quality is missing.");
        var action = ActionManager.Instance();
        if (action == null || action->GetActionStatus(ActionType.Item, meal.UseItemId) != 0)
            throw new InvalidOperationException("The qualifying meal is not usable by this character.");
        mealBefore = FishCollectionObservation.Count(meal.ItemId, meal.Hq);
        if (!action->UseAction(ActionType.Item, meal.UseItemId, extraParam: 65535)) throw new InvalidOperationException("Qualifying food consumption was rejected.");
        mealSent = DateTimeOffset.UtcNow; return false;
    }
    private bool FoodStillQualifies()
    {
        if (meal == null) return true;
        var status = Plugin.ObjectTable.LocalPlayer!.StatusList.FirstOrDefault(s => s.StatusId == 48);
        return status != null && status.Param == foodParam && status.RemainingTime > 10;
    }
    private bool FoodNeedsRefresh()
    {
        if (meal == null) return false;
        var status = Plugin.ObjectTable.LocalPlayer!.StatusList.FirstOrDefault(s => s.StatusId == 48);
        return status == null || status.Param != foodParam || status.RemainingTime < 120;
    }
    private void RefreshFood()
    {
        if (!foodRefreshing)
        {
            foodRefreshing = true; foodVerified = beforeCastObserved = false; mealSent = default;
            hook.StopFutureCasts();
        }
        Status = "Preserving the cast in flight, stowing and refreshing the exact qualifying meal";
        if (!hook.Stow() || !VerifyFoodAndReadiness()) return;
        hook.StartFishing(); castSent = DateTimeOffset.UtcNow; foodRefreshing = false;
    }
#pragma warning disable PendingExcelSchema // Same native table validated by NextOcean.
    private void BeginOcean()
    {
        var opportunity = Assignment!.Opportunity;
        var row = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Experimental.IKDRouteTable>().GetRow((uint)FishCollectionObservation.OceanCycleIndex(opportunity.StartUtc));
        plugin.FishingService.CollectionRoute = row.RubyRoute.RowId == opportunity.RouteId ? OceanFishingRoutePreference.Ruby : OceanFishingRoutePreference.Indigo;
        plugin.FishingService.CollectionCastGate = () => phase == Phase.Ocean && foodVerified && hook.ActionsAllowed && OceanMatches();
        plugin.FishingService.Start(); oceanStarted = true;
        Set(Phase.Ocean, "Preparing for native Ocean route " + opportunity.RouteId);
    }
#pragma warning restore PendingExcelSchema
    private bool OceanMatches()
    {
        var framework = EventFramework.Instance();
        var ocean = framework == null ? null : framework->GetInstanceContentOceanFishing();
        if (ocean == null || Assignment == null || ocean->CurrentRoute != Assignment.Opportunity.RouteId || ocean->CurrentZone > 2) return false;
        var route = Plugin.DataManager.GetExcelSheet<IKDRoute>().GetRow(ocean->CurrentRoute);
        var stop = route.Spot[(int)ocean->CurrentZone].Value;
        return (stop.SpotMain.RowId == Assignment.Opportunity.Fish.SpotId || stop.SpotSub.RowId == Assignment.Opportunity.Fish.SpotId) &&
            route.Time[(int)ocean->CurrentZone].RowId == Assignment.Opportunity.Fish.OceanTime;
    }
    private void TickOcean()
    {
        VerifyChangedReadiness();
        if (plugin.FishingService.IsFailed) throw new InvalidOperationException(plugin.FishingService.StatusText);
        if (plugin.FishingService.IsComplete) { EndAttempt("Ocean voyage ended"); return; }
        var framework = EventFramework.Instance();
        var ocean = framework == null ? null : framework->GetInstanceContentOceanFishing();
        if (ocean == null)
        {
            if (plugin.FishingService.State is FishingService.FishingState.InteractingRegistrar or FishingService.FishingState.ConfirmingRegistration)
                ReportArrival();
            if (!plugin.FishingService.QueueRegistrationObserved && DateTimeOffset.UtcNow >= Assignment!.Opportunity.EndUtc)
                EndAttempt("Ocean registration ended before boarding");
            Status = plugin.FishingService.StatusText; return;
        }
        if (ocean->CurrentRoute != Assignment!.Opportunity.RouteId) throw new InvalidOperationException("The actual Ocean route differs from the committed route.");
        if (OceanMatches())
        {
            oceanTargetSeen = true; targetZone = ocean->CurrentZone;
            if (foodRefreshing) { RefreshFood(); return; }
            if (!foodVerified && !VerifyFoodAndReadiness()) return;
            if (!hook.ActionsAllowed) hook.AllowCasts(true);
            if (Observation.Caught(Assignment.Opportunity.Fish.ItemId)) { Observation.ObserveLog(); EndAttempt("Fabled target caught"); return; }
            if (foodRefreshing || FoodNeedsRefresh()) { RefreshFood(); return; }
            alertAcknowledged = true;
            Status = FishingStatus() + (ocean->SpectralCurrentActive ? "; spectral current active" : "; waiting for spectral current");
        }
        else if (oceanTargetSeen && ocean->CurrentZone != targetZone) EndAttempt("Target Ocean stop ended");
        else Status = $"Route {ocean->CurrentRoute}, stop {ocean->CurrentZone + 1}; target strategy gated until its native stop and time";
    }
    private void VerifyChangedReadiness()
    {
        if (Plugin.PlayerState.ClassJob.RowId != 18) throw new InvalidOperationException("Fisher equipment changed during the attempt.");
        if (CurrentConfig.FisherObservation is not { } latest || latest.ObservedAtUtc <= readinessObserved) return;
        if (!QualifiesStrategy(latest) ||
            !GameHelpers.TryGetLowestEquippedGearConditionPercent(out var durability) || durability <= 0)
            throw new InvalidOperationException("Changed Fisher equipment no longer qualifies.");
        readinessObserved = latest.ObservedAtUtc;
    }
    private bool QualifiesStrategy(FisherObservation baseline)
    {
        var effective = meal?.Apply(baseline) ?? baseline;
        return baseline.BaselineCertain && effective.Gathering >= Assignment!.Opportunity.Fish.RequiredGathering(plugin.Configuration.FishCollection) &&
            (preparedStats == null || effective.MaximumGp >= preparedStats.MaximumGp && effective.Level >= preparedStats.Level);
    }
    private void ReportArrival()
    {
        if (arrivalGoalReported) return;
        arrivalGoalReported = true;
        var now = DateTimeOffset.UtcNow;
        var goal = Assignment!.Opportunity.PositionedUtc;
        Plugin.Log.Information($"[FishCollection] Arrival {now:u}; 45-minute goal {goal:u}; {(now <= goal ? "met" : "late")}");
        LastOutcome = now <= goal ? "45-minute preparation arrival goal met" : $"Preparation arrived {(now - goal).TotalMinutes:F1} minutes after the 45-minute goal";
    }
    private string FishingStatus()
    {
        var intuition = Plugin.ObjectTable.LocalPlayer!.StatusList.FirstOrDefault(s => s.StatusId == 568);
        return $"Fishing {Assignment!.Opportunity.Fish.Name}; " + (intuition != null ? $"native intuition {intuition.RemainingTime:F0}s" : "preparing intuition / target");
    }
    private void EndAttempt(string reason)
    {
        if (phase == Phase.Cleanup) { Status = LastOutcome + "; cleanup pending: " + reason; return; }
        LastOutcome = reason; alertAcknowledged = true;
        if (Assignment != null) attempted.Add(AttemptKey(Assignment));
        Set(Phase.Cleanup, reason + "; waiting for owned work and rod stow");
    }
    private void TickCleanup()
    {
        if (hook.OwnsState) hook.StopFutureCasts();
        if (!Supplies.Cancel()) { Supplies.Update(); Status = "Waiting for owned supply cancellation to settle"; return; }
        if (repairOwned)
        {
            var repair = plugin.AdsIpcClient.Refresh();
            if (!repair.StatusReadable || repair.UtilityRunning) { Status = "Waiting for this attempt's repair to settle"; return; }
            repairOwned = false;
        }
        if (navigationOwned) { plugin.VNavmeshIPC.Stop(); if (!NavigationSettled()) return; navigationOwned = false; }
        if (travelOwned) { if (!NavigationSettled()) { Status = "Waiting for owned travel to settle"; return; } travelOwned = false; }
        if (!NavigationSettled()) { Status = "Waiting for supply-provider navigation and travel to settle"; return; }
        if (hook.OwnsState)
        {
            if (!hook.Stow()) return;
        }
        if (oceanStarted)
        {
            var framework = EventFramework.Instance();
            if (framework != null && framework->GetInstanceContentOceanFishing() != null && !plugin.FishingService.IsActive)
            { Status = "Rod stowed; voyage handling failed, waiting for departure before restoring presets"; return; }
            if (plugin.FishingService.IsActive)
            {
                if (framework != null && framework->GetInstanceContentOceanFishing() != null)
                { Status = "Rod stowed; waiting for voyage results, return and owned navigation"; return; }
                if (plugin.FishingService.State is not (FishingService.FishingState.HandlingResult or FishingService.FishingState.Returning or
                    FishingService.FishingState.WaitingForCleanupReady or FishingService.FishingState.RunningInventoryCleanup))
                    plugin.FishingService.Reset(false);
                else return;
            }
            plugin.FishingService.CollectionCastGate = null; plugin.FishingService.CollectionRoute = null;
            plugin.FishingService.Reset(false); oceanStarted = false;
        }
        if (hook.OwnsState)
        {
            hook.Restore();
            if (WorldReady && plugin.IsCharacterRegistered && Assignment?.CharacterKey == CurrentKey) Observation.ObserveLog();
        }
        if (!plugin.FishingRunLifecycle.ResumeYesAlreadyPauseAfterShopping("collection cleanup"))
            throw new InvalidOperationException("Collection cleanup cannot restore its YesAlready lease.");
        if (plugin.FishingRelogCoordinator.IsActive) { Status = "Waiting for collection relog to settle"; return; }
        returnCommand = Assignment is { Opportunity.Fish.Ocean: false } && CurrentKey == Assignment.CharacterKey &&
            plugin.ConfigManager.CurrentAccountId == accountId ? FishingOperationPolicy.ResolveReturnCommand(
            new FishingOperationSettings(CurrentConfig.FishingLureRestockTarget, CurrentConfig.FishingReturnDestination,
                CurrentConfig.FishingReturnCommand, CurrentConfig.FishingRepairMode, CurrentConfig.FishingRepairThresholdPercent)) : "";
        if (!returnStarted && returnCommand.Length > 0)
        {
            returnStarted = true;
            returnTerritory = Plugin.ClientState.TerritoryType; returnActivityObserved = false; returnCommandsSent = 1;
            if (!CommandHelper.TrySendCommand(returnCommand))
            { Running = false; LastOutcome += "; configured return was rejected"; Finish(); return; }
            travelOwned = true; Set(Phase.Return, "Waiting for configured fishing return"); return;
        }
        Finish();
    }
    private void TickReturn()
    {
        var busy = !NavigationSettled() || !WorldReady;
        returnActivityObserved |= busy;
        var elapsed = DateTimeOffset.UtcNow - entered;
        if (elapsed < TimeSpan.FromSeconds(5)) return;
        if (FishingReturnPolicy.IsVerified(true, returnActivityObserved, Plugin.ClientState.TerritoryType != returnTerritory, busy))
        { travelOwned = false; Finish(); return; }
        if (FishingReturnPolicy.ShouldRetry(returnCommandsSent, elapsed, busy))
        {
            returnCommandsSent++;
            if (!CommandHelper.TrySendCommand(returnCommand)) LastOutcome += "; configured return retry was rejected";
        }
        Status = "Configured return remains unverified; collection retains ownership";
        if (elapsed >= FishingReturnPolicy.FailAfter && !busy)
        {
            Running = false; travelOwned = false;
            LastOutcome += "; configured return failed without verified arrival"; Finish();
        }
    }
    private void Finish()
    {
        Assignment = null; inspectionKey = ""; gearsetRequested = false;
        if (Running) { forecastAfter = default; Set(Phase.Inspect, LastOutcome + "; looking for the next opportunity"); return; }
        plugin.FishingRunLifecycle.Cleanup("fish collection stopped");
        if (plugin.FishingRunLifecycle.IsActive) { phase = Phase.Cleanup; Status = "Collection external-state restoration is pending"; return; }
        Set(Phase.Idle, LastOutcome);
    }
    private void Alert(DateTimeOffset now)
    {
        var settings = plugin.Configuration.FishCollection;
        if (!settings.AudioEnabled || Assignment == null || alertAcknowledged || phase is Phase.Fish or Phase.Cleanup or Phase.Return ||
            now >= Assignment.Opportunity.EndUtc || now < Assignment.Opportunity.StartUtc.AddMinutes(-settings.AlertAdvanceMinutes) || now < nextAlert) return;
        UIGlobals.PlaySoundEffect(Math.Clamp(settings.SoundEffect, 1u, 16u));
        nextAlert = now.AddMinutes(Math.Max(1, settings.AlertRepeatMinutes));
    }
    public void Dispose()
    {
        Running = false;
        try { Supplies.Cancel(); } catch (Exception error) { Plugin.Log.Error(error, "Owned collection order cancellation at unload is unverified"); }
        hook.Dispose(); Observation.Dispose();
    }
}
