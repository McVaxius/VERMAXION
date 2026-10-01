using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;
using VERMAXION.DeliverySupport;
using Action = System.Action;

namespace VERMAXION.CustomDeliveries;

// VSatisfy's data refresh and native automation now belong to VERMAXION's framework lifecycle.
public sealed unsafe class CustomDeliveriesService : IDisposable
{
    private readonly Plugin plugin;
    private readonly Achievements achievements = new();
    private readonly Automation automation = new();
    private readonly List<NPCInfo> npcs = [];
    private CustomDeliveriesSettings? runSettings;
    private ulong loadedCharacter;
    private DateTime resetDate;
    private DateTime nextRefresh;
    private bool active;
    private int verifiedDeliveries;
    private string status = "Idle";
    private bool manualAchievementFetch;

    public CustomDeliveriesService(Plugin plugin)
    {
        this.plugin = plugin;
        Service.Navigation = plugin.VNavmeshIPC;
        achievements.AchievementProgress += OnAchievementProgress;
    }

    public IReadOnlyList<NPCInfo> Npcs => npcs;
    public int? RemainingAllowances { get; private set; }
    public bool IsActive => active || FishingCleanupPending?.Invoke() == true;
    public bool IsComplete { get; private set; }
    public bool IsFailed { get; private set; }
    public int CompletedTurnins => verifiedDeliveries;
    public bool IsFishing => automation.CurrentTask is AutoFish { IsFishing: true };
    public string WatchdogProgress => Service.ClientState.IsLoggedIn && automation.CurrentTask is AutoCommon task
        ? $"{task.WatchdogProgress}|remaining={RemainingAllowances}|verified={verifiedDeliveries}" : StatusText;
    public string StatusText => FishingCleanupPending?.Invoke() == true ? FishingCleanupStatus?.Invoke() ?? "Fishing cleanup pending"
        : automation.CurrentTask?.Status is { Length: > 0 } taskStatus ? taskStatus : status;
    public Func<DeliveryRoute, CancellationToken, Task>? FishingHandler { get; set; }
    public Action? FishingCleanup { get; set; }
    public Func<bool>? FishingCleanupPending { get; set; }
    public Func<string>? FishingCleanupStatus { get; set; }

    public void Update()
    {
        var ui = UIState.Instance();
        var character = Service.PlayerState.ContentId;
        if (!Service.ClientState.IsLoggedIn || ui == null || !ui->PlayerState.IsLoaded || character == 0)
        {
            if (active) Cancel();
            if (loadedCharacter != 0)
            {
                loadedCharacter = 0;
                foreach (var npc in npcs) npc.ClearAchievement();
            }
            RemainingAllowances = null;
            return;
        }
        if (loadedCharacter != character)
        {
            Cancel();
            loadedCharacter = character;
            manualAchievementFetch = false;
            foreach (var npc in npcs) npc.ClearAchievement();
            nextRefresh = DateTime.MinValue;
        }
        if (DateTime.UtcNow < nextRefresh) return;
        nextRefresh = DateTime.UtcNow.AddSeconds(1);
        TryRefreshData();
    }

    private bool TryRefreshData()
    {
        try { RefreshData(); return true; }
        catch (Exception ex)
        {
            RemainingAllowances = null;
            if (active) Fail($"Custom delivery data could not be refreshed: {ex.Message}");
            else if (!IsFailed) status = $"Custom delivery data unavailable: {ex.Message}";
            return false;
        }
    }

    private void RefreshData()
    {
        var manager = SatisfactionSupplyManager.Instance();
        if (manager == null) { RemainingAllowances = null; return; }
        if (npcs.Count == 0)
        {
            foreach (var row in Service.LuminaSheet<SatisfactionNpc>() ?? throw new InvalidOperationException("SatisfactionNpc sheet is unavailable"))
            {
                if (row.Npc.RowId == 0 || row.RowId == 0 || row.RowId > manager->SatisfactionRanks.Length) continue;
                npcs.Add(new(row));
            }
        }
        var currentReset = manager->GetResetDateTime();
        if (resetDate != currentReset)
        {
            if (resetDate != default && active) Cancel();
            resetDate = currentReset;
            foreach (var npc in npcs) npc.ClearAchievement();
        }
        RemainingAllowances = Math.Max(0, manager->GetRemainingAllowances());
        var bonusIndex = manager->BonusGuaranteeRowId != 0xFF ? manager->BonusGuaranteeRowId : Calculations.CalculateBonusGuarantee();
        var bonus = bonusIndex >= 0 ? Service.LuminaRow<SatisfactionBonusGuarantee>((uint)bonusIndex) : null;
        var settings = runSettings ?? plugin.ConfigManager.GetActiveConfig().CustomDeliveriesSettings;
        foreach (var npc in npcs)
        {
            var row = Service.LuminaRow<SatisfactionNpc>(npc.RowId)!.Value;
            npc.Unlocked = npc.IsUnlocked;
            npc.Rank = manager->SatisfactionRanks[npc.Index];
            npc.SatisfactionCur = manager->Satisfaction[npc.Index];
            npc.UsedDeliveries = manager->UsedAllowances[npc.Index];
            npc.SatisfactionMax = npc.Rank < row.SatisfactionNpcParams.Count
                ? row.SatisfactionNpcParams[npc.Rank].SatisfactionRequired : 0;
            Array.Clear(npc.IsBonusOverride);
            Array.Clear(npc.IsBonusEffective);
            Array.Clear(npc.TurnInItems);
            npc.Requests = npc.Rank > 0 && npc.SupplyIndex > 0
                ? Calculations.CalculateRequestedItems(npc.SupplyIndex, manager->SupplySeed) : [];
            if (npc.Rank == 5 && bonus is { } guaranteed)
            {
                npc.IsBonusOverride[0] = guaranteed.BonusDoH.Contains((byte)npc.RowId);
                npc.IsBonusOverride[1] = guaranteed.BonusDoL.Contains((byte)npc.RowId);
                npc.IsBonusOverride[2] = guaranteed.BonusFisher.Contains((byte)npc.RowId);
            }
            if (npc.Requests.Length > 0 && Service.LuminaSubrows<SatisfactionSupply>(npc.SupplyIndex) is { } supplies)
            {
                for (var slot = 0; slot < npc.Requests.Length; ++slot)
                {
                    var request = npc.Requests[slot];
                    if (request >= supplies.Count) continue;
                    var supply = supplies[(int)request];
                    if (npc.IsBonusOverride[slot] && !supply.IsBonus)
                    {
                        for (var j = 0; j < supplies.Count; ++j)
                        {
                            if (supplies[j].Slot == supply.Slot && supplies[j].IsBonus)
                            {
                                request = (uint)j;
                                supply = supplies[j];
                                break;
                            }
                        }
                    }
                    npc.EffectiveRequests[slot] = request;
                    npc.IsBonusEffective[slot] = supply.IsBonus;
                    npc.Rewards[slot] = supply.Reward.RowId;
                    npc.TurnInItems[slot] = supply.Item.RowId;
                    npc.MinCollectibility[slot] = (ushort)supply.CollectabilityLow;
                    npc.TargetCollectibility[slot] = (ushort)supply.CollectabilityHigh;
                    npc.RequiredLevels[slot] = row.LevelUnlock;
                }
                if (npc.TurnInItems[1] != 0 && npc.GatherData?.GatherItemId != npc.TurnInItems[1])
                    npc.GatherData = new(npc.TurnInItems[1]);
                if (npc.TurnInItems[2] != 0 && npc.FishData?.FishItemId != npc.TurnInItems[2])
                {
                    var fishSupply = supplies[(int)npc.EffectiveRequests[2]];
                    npc.FishData = new(npc.TurnInItems[2], fishSupply.FishingSpotId, fishSupply.SpearFishingSpotId);
                }
            }
            if (npc.Unlocked && npc.AchievementCur == null && npc.AchievementId != 0
                && (manualAchievementFetch || settings?.AutoFetchAchievements == true))
                achievements.Request(npc.AchievementId);
        }
        if (manualAchievementFetch && npcs.Where(n => n.Unlocked && n.AchievementId != 0).All(n => n.AchievementCur != null))
            manualAchievementFetch = false;
    }

    public void RequestAchievements()
    {
        manualAchievementFetch = true;
        foreach (var npc in npcs.Where(n => n.Unlocked && n.AchievementId != 0 && n.AchievementCur == null))
            achievements.Request(npc.AchievementId);
    }

    public IReadOnlyList<uint> GetEligibleJobs(NPCInfo npc, DeliveryTypes type, CustomDeliveriesSettings settings)
    {
        if (!npc.Unlocked || npc.Rank <= 0) return [];
        type &= settings.AllowedTypes;
        var jobs = new List<uint>();
        if (type.HasFlag(DeliveryTypes.Crafting))
            jobs.AddRange(settings.EligibleCraftJobs.Where(job => job is >= 8 and <= 15
                && npc.TurnInItems[0] != 0 && GetLevel(job) >= npc.RequiredLevels[0]
                && CraftTurnin.GetRecipe(npc.TurnInItems[0], job) is { RowId: > 0 } recipe
                && GetLevel(job) >= recipe.RecipeLevelTable.Value.ClassJobLevel && HasGearset(job)));
        if (type.HasFlag(DeliveryTypes.Mining) && settings.EligibleGatherJobs.Contains(16)
            && npc.TurnInItems[1] != 0 && GetLevel(16) >= npc.RequiredLevels[1] && HasGearset(16)
            && npc.GatherData?.GatherPoints.Any(point => point.ClassJob == 16) == true) jobs.Add(16);
        if (type.HasFlag(DeliveryTypes.Botany) && settings.EligibleGatherJobs.Contains(17)
            && npc.TurnInItems[1] != 0 && GetLevel(17) >= npc.RequiredLevels[1] && HasGearset(17)
            && npc.GatherData?.GatherPoints.Any(point => point.ClassJob == 17) == true) jobs.Add(17);
        if (type.HasFlag(DeliveryTypes.Fishing) && npc.TurnInItems[2] != 0
            && GetLevel(18) >= npc.RequiredLevels[2] && HasGearset(18)) jobs.Add(18);
        return jobs.Distinct().ToArray();
    }

    private static int GetLevel(uint job)
    {
        var player = FFXIVClientStructs.FFXIV.Client.Game.UI.PlayerState.Instance();
        var row = Service.LuminaRow<ClassJob>(job);
        return player != null && row is { ExpArrayIndex: >= 0 } classJob
            && classJob.ExpArrayIndex < player->ClassJobLevels.Length
            ? player->ClassJobLevels[classJob.ExpArrayIndex] : 0;
    }

    public bool HasPendingWork(CustomDeliveriesSettings settings) => SelectRoute(settings) != null;

    private DeliveryRoute? SelectRoute(CustomDeliveriesSettings settings)
    {
        if (RemainingAllowances is not > 0) return null;
        var routes = new List<DeliveryRoute>();
        foreach (var npc in npcs.Where(n => n.Unlocked && n.Rank > 0 && n.UsedDeliveries < n.MaxDeliveries))
        {
            if (settings.NpcPolicy == DeliveryNpcPolicy.ClosestTo150 && npc.AchievementCur is not < 150) continue;
            foreach (var type in new[] { DeliveryTypes.Crafting, DeliveryTypes.Mining, DeliveryTypes.Botany, DeliveryTypes.Fishing })
            {
                if (!settings.AllowedTypes.HasFlag(type)) continue;
                var slot = type == DeliveryTypes.Crafting ? 0 : type == DeliveryTypes.Fishing ? 2 : 1;
                if (settings.NpcPolicy == DeliveryNpcPolicy.BonusesOnly && !npc.IsBonusEffective[slot]) continue;
                var jobs = GetEligibleJobs(npc, type, settings);
                var job = OrderJobs(jobs, type, settings).FirstOrDefault();
                if (job == 0) continue;
                var count = DeliveryPlanning.Count(settings.NpcPolicy, RemainingAllowances.Value,
                    npc.RemainingTurnins(slot), npc.AchievementCur);
                if (count > 0 || npc.PendingRankQuestId != 0) routes.Add(new(npc, type, job, slot, npc.TurnInItems[slot], count,
                    npc.MinCollectibility[slot], npc.TargetCollectibility[slot]));
            }
        }
        return routes.OrderBy(route => settings.NpcPolicy == DeliveryNpcPolicy.ClosestTo150
                ? 150 - route.Npc.AchievementCur!.Value : 0)
            .ThenBy(route => route.Type).ThenBy(route => route.Npc.RowId).FirstOrDefault();
    }

    private static IEnumerable<uint> OrderJobs(IEnumerable<uint> jobs, DeliveryTypes type, CustomDeliveriesSettings settings)
    {
        return settings.CraftJobType switch
        {
            DeliveryJobChoice.Current => jobs.OrderBy(job => job == Service.PlayerState.ClassJob.RowId ? 0 : 1).ThenBy(job => job),
            DeliveryJobChoice.LowestXP => jobs.OrderBy(GetJobProgress).ThenBy(job => job),
            DeliveryJobChoice.HighestXP => jobs.OrderByDescending(GetJobProgress).ThenBy(job => job),
            _ => jobs.OrderBy(job => job == (type == DeliveryTypes.Crafting ? settings.SelectedCraftJob : settings.SelectedGatherJob) ? 0 : 1).ThenBy(job => job),
        };
    }

    private static double GetJobProgress(uint job)
    {
        var player = FFXIVClientStructs.FFXIV.Client.Game.UI.PlayerState.Instance();
        var level = GetLevel(job);
        if (player == null) return level;
        var needed = player->GetClassJobNeededExp((int)job);
        return level + (needed > 0 ? player->GetClassJobExp(job) / (double)needed : 0);
    }

    public string? GetStartBlockedReason(CustomDeliveriesSettings settings)
    {
        if (FishingCleanupPending?.Invoke() == true) return StatusText;
        if (RemainingAllowances == null) return "Custom delivery data is unavailable for the current character";
        if (RemainingAllowances == 0) return null;
        var route = SelectRoute(settings);
        if (route == null)
        {
            if (settings.NpcPolicy == DeliveryNpcPolicy.ClosestTo150 && npcs.Any(n => n.Unlocked && n.UsedDeliveries < n.MaxDeliveries && n.AchievementCur == null))
                return "Achievement progress is unknown; fetch verified progress before choosing an NPC below 150";
            return "No NPC route matches the selected policy, types, eligible jobs and remaining allowances";
        }
        if (route.Npc.CraftData is not { TurnInInstanceId: > 0 }) return "Custom delivery NPC position is unavailable";
        if (!HasGearset(route.Job)) return $"No saved gearset is available for job {route.Job}";
        if (route.Npc.PendingRankQuestId != 0) return AutoDeliveryQuest.GetBlockedReason();
        if (route.Type == DeliveryTypes.Crafting && Game.NumItemsInInventory(route.ItemId, (short)route.MinCollectibility) < route.Count)
        {
            if (!Service.PluginInterface.GetIpcSubscriber<ushort, int, object>("Artisan.CraftItem").HasAction
                || !Service.PluginInterface.GetIpcSubscriber<bool, object>("Artisan.SetEnduranceStatus").HasAction
                || !Service.PluginInterface.GetIpcSubscriber<bool>("Artisan.IsBusy").HasFunction
                || !Service.PluginInterface.GetIpcSubscriber<bool>("Artisan.GetStopRequest").HasFunction)
                return "Artisan crafting integration is unavailable";
            if (Service.PluginInterface.GetIpcSubscriber<bool>("Artisan.IsBusy").InvokeFunc())
                return "Artisan is already running another crafting request";
            if (Service.PluginInterface.GetIpcSubscriber<bool>("Artisan.GetStopRequest").InvokeFunc())
                return "Artisan has an active stop request";
            if (route.Npc.CraftData.VendorInstanceId == 0 || route.Npc.CraftData.VendorShopId == 0)
                return "Custom delivery ingredient vendor data is unavailable";
        }
        if (route.Type is DeliveryTypes.Mining or DeliveryTypes.Botany
            && Game.NumItemsInInventory(route.ItemId, (short)route.MinCollectibility) < route.Count
            && WigglyGathering.GetBlockedReason(route) is { } gatheringBlocker)
            return gatheringBlocker;
        if (route.Type is DeliveryTypes.Mining or DeliveryTypes.Botany
            && Service.PluginInterface.GetIpcSubscriber<bool>("WigglyQuest.IsRunning").HasFunction
            && Service.PluginInterface.GetIpcSubscriber<bool>("WigglyQuest.IsRunning").InvokeFunc())
            return "Wiggly Questionable is already running another task";
        if (route.Type == DeliveryTypes.Fishing && FishingHandler == null)
            return "AutoHook custom-delivery fishing integration is unavailable";
        return null;
    }

    private static bool HasGearset(uint job)
    {
        if (Service.PlayerState.ClassJob.RowId == job) return true;
        var module = RaptureGearsetModule.Instance();
        if (module == null) return false;
        for (var i = 0; i < module->Entries.Length; ++i)
            if (module->Entries[i].ClassJob == job && (module->Entries[i].Flags & RaptureGearsetModule.GearsetFlag.Exists) != 0) return true;
        return false;
    }

    public void Start(CustomDeliveriesSettings settings)
    {
        if (IsActive) return;
        Reset();
        if (IsFailed) return;
        runSettings = settings.Clone();
        active = true;
        verifiedDeliveries = 0;
        StartNextRoute();
    }

    private void StartNextRoute(DeliveryRoute? completedRoute = null)
    {
        if (!TryRefreshData()) return;
        if (!active || runSettings == null) return;
        // Finish a gate reached by our last batch even when that batch used the
        // final weekly allowance. New delivery work still obeys all allowances.
        if (completedRoute?.Npc.PendingRankQuestId is > 0)
        {
            StartRankQuest(completedRoute.Npc, completedRoute.Job);
            return;
        }
        var route = SelectRoute(runSettings);
        if (route == null)
        {
            active = false;
            var unknownTarget = runSettings.NpcPolicy == DeliveryNpcPolicy.ClosestTo150
                && npcs.Any(n => n.Unlocked && n.UsedDeliveries < n.MaxDeliveries && n.AchievementCur == null);
            IsComplete = RemainingAllowances == 0 || (verifiedDeliveries > 0 && !unknownTarget);
            IsFailed = !IsComplete;
            status = IsComplete ? $"Completed {verifiedDeliveries} verified deliveries" : GetStartBlockedReason(runSettings) ?? "No eligible delivery route";
            return;
        }
        var blocked = GetStartBlockedReason(runSettings);
        if (blocked != null) { Fail(blocked); return; }
        if (route.Npc.PendingRankQuestId != 0)
        {
            StartRankQuest(route.Npc, route.Job);
            return;
        }
        AutoTask task = route.Type switch
        {
            DeliveryTypes.Crafting => new AutoCraft(route),
            DeliveryTypes.Mining or DeliveryTypes.Botany => new AutoGather(route),
            _ => new AutoFish(route, FishingHandler!, FishingCleanup),
        };
        var before = route.Npc.UsedDeliveries;
        status = $"Selected {route.Npc.Name}: {route.Type}, job {route.Job}, {route.Count} deliveries";
        Service.Log.Information($"[CustomDeliveries] {status}");
        var inventory = Game.NumItemsInInventory(route.ItemId, (short)route.MinCollectibility);
        Service.Log.Information($"[CustomDeliveries] Plan: npc={route.Npc.Name}; progress={route.Npc.AchievementCur?.ToString() ?? "unknown"}/150; rank={route.Npc.Rank}; satisfaction={route.Npc.SatisfactionCur}/{route.Npc.SatisfactionMax}; characterAllowances={RemainingAllowances}; npcAllowancesAtRank={route.Npc.RemainingTurnins(route.Slot)}; item={route.ItemId}; planned={route.Count}; eligibleInventory={inventory}; toProduce={DeliveryPlanning.MissingItems(route.Count, inventory)}");
        automation.Start(task, finished =>
        {
            if (!active) return;
            if (!TryRefreshData()) return;
            var delivered = route.Npc.UsedDeliveries - before;
            verifiedDeliveries += Math.Max(0, delivered);
            if (!finished.CompletedSuccessfully || delivered < route.Count)
            {
                Fail(finished.Failure ?? $"Only {Math.Max(0, delivered)} of {route.Count} deliveries were verified");
                return;
            }
            StartNextRoute(route);
        });
    }

    private void StartRankQuest(NPCInfo npc, uint job)
    {
        var questId = npc.PendingRankQuestId;
        var blocked = AutoDeliveryQuest.GetBlockedReason();
        if (blocked != null) { Fail(blocked); return; }
        status = $"Completing {npc.PendingRankQuestName} for {npc.Name} before further deliveries";
        Service.Log.Information($"[CustomDeliveries][Quest] {status}; rank={npc.Rank}; satisfaction={npc.SatisfactionCur}/{npc.SatisfactionMax}; weeklyAllowances={RemainingAllowances}");
        automation.Start(new AutoDeliveryQuest(npc, questId, job), finished =>
        {
            if (!active) return;
            if (!finished.CompletedSuccessfully || !QuestManager.IsQuestComplete(questId))
            {
                Fail(finished.Failure ?? "Rank quest completion was not verified");
                return;
            }
            Service.Log.Information($"[CustomDeliveries][Quest] Resuming delivery selection after verified quest {questId}");
            StartNextRoute();
        });
    }

    private void Fail(string reason)
    {
        var cleanupFailure = CleanupFishing();
        automation.Stop();
        active = false;
        IsFailed = true;
        IsComplete = false;
        status = cleanupFailure == null ? reason : $"{reason}; cleanup failed: {cleanupFailure}";
        Service.Log.Warning($"[CustomDeliveries] {reason}");
    }

    public void Cancel()
    {
        var cleanupFailure = CleanupFishing();
        automation.Stop();
        active = false;
        IsComplete = false;
        IsFailed = cleanupFailure != null;
        runSettings = null;
        status = cleanupFailure == null ? "Cancelled" : $"Cancelled; AutoHook cleanup failed: {cleanupFailure}";
    }
    public void Reset()
    {
        Cancel();
        verifiedDeliveries = 0;
        if (!IsFailed) status = "Idle";
    }
    private string? CleanupFishing()
    {
        try { FishingCleanup?.Invoke(); return null; }
        catch (Exception ex)
        {
            Service.Log.Error(ex, "Custom delivery AutoHook cleanup failed");
            return ex.Message;
        }
    }
    private void OnAchievementProgress(uint id, uint current, uint maximum)
    {
        var npc = npcs.FirstOrDefault(n => n.AchievementId == id);
        if (npc == null || loadedCharacter == 0 || loadedCharacter != Service.PlayerState.ContentId
            || !Service.ClientState.IsLoggedIn || maximum == 0 || current > maximum) return;
        var manager = SatisfactionSupplyManager.Instance();
        if (manager == null) return;
        npc.AchievementAtSample = current;
        npc.UsedDeliveriesAtSample = manager->UsedAllowances[npc.Index];
        npc.AchievementMax = maximum;
    }
    public void Dispose()
    {
        Cancel();
        achievements.AchievementProgress -= OnAchievementProgress;
        achievements.Dispose();
        automation.Dispose();
    }
}
