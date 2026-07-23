using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;
using VERMAXION.IPC;
using VERMAXION.Models;

namespace VERMAXION.Services;

public sealed class FisherGearsetRuntime : IFisherGearsetRuntime
{
    public int GetCurrentClassJobId()
        => (int)Plugin.PlayerState.ClassJob.RowId;

    public unsafe FisherGearsetLookupResult FindFirstSavedFisherGearset()
    {
        try
        {
            var module = RaptureGearsetModule.Instance();
            if (module == null)
                return FisherGearsetLookupResult.ModuleUnavailable(
                    "RaptureGearsetModule is unavailable.");

            var snapshots = new List<SavedGearsetSnapshot>(100);
            for (var slot = 0; slot < 100; slot++)
            {
                ref var entry = ref module->Entries[slot];
                snapshots.Add(new SavedGearsetSnapshot(
                    slot,
                    entry.Id,
                    entry.ClassJob,
                    (entry.Flags & RaptureGearsetModule.GearsetFlag.Exists) != 0));
            }

            var selection = FisherGearsetSelectionPolicy.SelectLowestSavedFisher(snapshots);
            return selection.HasValue
                ? FisherGearsetLookupResult.Found(selection.Value)
                : FisherGearsetLookupResult.Missing();
        }
        catch (Exception ex)
        {
            return FisherGearsetLookupResult.ModuleUnavailable(
                $"Could not inspect RaptureGearsetModule: {ex.Message}");
        }
    }

    public unsafe FisherGearsetEquipRequestResult EquipGearset(int gearsetId)
    {
        try
        {
            var module = RaptureGearsetModule.Instance();
            return module == null
                ? FisherGearsetEquipRequestResult.ModuleUnavailable(
                    "RaptureGearsetModule is unavailable for the equip request.")
                : FisherGearsetEquipRequestResult.Completed(module->EquipGearset(gearsetId));
        }
        catch (Exception ex)
        {
            return FisherGearsetEquipRequestResult.ModuleUnavailable(
                $"Could not equip Fisher gearset ID {gearsetId}: {ex.Message}");
        }
    }
}

public sealed unsafe class FisherFallbackService
{
    private const ushort EquippedItemsContainer = 1000;
    private const ushort ArmoryMainHandContainer = 2000;
    private static readonly ushort[] InventoryContainers = [3200, 3201, 3202, 3203];

    private readonly IDataManager dataManager;
    private readonly IFramework framework;
    private readonly IPlayerState playerState;
    private readonly AdsIpcClient ads;
    private readonly IPluginLog log;
    private DateTimeOffset deadlineUtc;
    private uint weatheredFishingRodItemId;
    private bool purchaseOwned;
    private bool purchaseFinished;
    private bool moveRequested;

    public FisherFallbackService(
        IDataManager dataManager,
        IFramework framework,
        IPlayerState playerState,
        AdsIpcClient ads,
        IPluginLog log)
    {
        this.dataManager = dataManager;
        this.framework = framework;
        this.playerState = playerState;
        this.ads = ads;
        this.log = log;
    }

    public bool IsActive { get; private set; }
    public bool IsComplete { get; private set; }
    public bool Succeeded { get; private set; }
    public string StatusText { get; private set; } = "Idle";
    public string Failure { get; private set; } = string.Empty;

    public void Start(DateTimeOffset registrationDeadlineUtc)
    {
        Reset(cancelOwnedPurchase: true);
        deadlineUtc = registrationDeadlineUtc.ToUniversalTime();
        IsActive = true;
        StatusText = "Locating Weathered Fishing Rod";
    }

    public void Update()
    {
        if (!IsActive || IsComplete)
            return;
        if (DateTimeOffset.UtcNow >= deadlineUtc)
        {
            Fail("Registration closed before the Fisher rod fallback was verified.");
            return;
        }
        if (!framework.IsInFrameworkUpdateThread)
            return;

        weatheredFishingRodItemId = weatheredFishingRodItemId == 0
            ? ResolveWeatheredFishingRodItemId()
            : weatheredFishingRodItemId;
        if (weatheredFishingRodItemId == 0)
        {
            Fail("Weathered Fishing Rod could not be resolved from game data.");
            return;
        }

        if (IsRodEquippedAndFisher())
        {
            purchaseOwned = false;
            Succeeded = true;
            IsComplete = true;
            IsActive = false;
            StatusText = "Weathered Fishing Rod and Fisher verified";
            return;
        }

        if (TryFindRod(out var sourceType, out var sourceSlot))
        {
            if (!moveRequested)
            {
                var manager = InventoryManager.Instance();
                var result = manager == null
                    ? -1
                    : manager->MoveItemSlot(
                        (InventoryType)sourceType,
                        sourceSlot,
                        (InventoryType)EquippedItemsContainer,
                        0);
                if (result != 0)
                {
                    Fail($"Weathered Fishing Rod inventory move returned {result}.");
                    return;
                }
                moveRequested = true;
                StatusText = "Verifying Weathered Fishing Rod and Fisher";
            }
            return;
        }

        if (moveRequested)
        {
            StatusText = "Waiting to verify Weathered Fishing Rod and Fisher";
            return;
        }

        if (!purchaseOwned && !purchaseFinished)
        {
            if (!ads.StartShopPurchase(weatheredFishingRodItemId, 1, out var failure))
            {
                Fail($"ADS could not buy one Weathered Fishing Rod: {failure}");
                return;
            }
            purchaseOwned = true;
            StatusText = "ADS is buying one Weathered Fishing Rod";
            return;
        }

        var status = ads.RefreshShopPurchase();
        if (!status.StatusReadable ||
            status.ItemId != weatheredFishingRodItemId ||
            !status.IsTerminal)
            return;

        purchaseOwned = false;
        purchaseFinished = true;
        if (TryFindRod(out _, out _))
        {
            StatusText = "Weathered Fishing Rod acquired; equipping";
            return;
        }

        var detail = !string.IsNullOrWhiteSpace(status.FailureMessage)
            ? status.FailureMessage
            : status.StatusMessage;
        if (status.Succeeded == true)
        {
            StatusText = "ADS completed; waiting for Weathered Fishing Rod inventory verification";
            return;
        }
        Fail($"ADS finished without a Weathered Fishing Rod in inventory. {detail}".Trim());
    }

    public void Reset(bool cancelOwnedPurchase = true)
    {
        if (cancelOwnedPurchase && purchaseOwned)
        {
            var status = ads.RefreshShopPurchase(force: true);
            if (status.Running)
                ads.CancelUtility(out _);
        }
        IsActive = false;
        IsComplete = false;
        Succeeded = false;
        StatusText = "Idle";
        Failure = string.Empty;
        purchaseOwned = false;
        purchaseFinished = false;
        moveRequested = false;
        deadlineUtc = default;
    }

    private uint ResolveWeatheredFishingRodItemId()
    {
        var sheet = dataManager.GetExcelSheet<Item>();
        if (sheet == null)
            return 0;
        foreach (var item in sheet)
        {
            if (string.Equals(item.Name.ToString(), "Weathered Fishing Rod", StringComparison.Ordinal))
                return item.RowId;
        }
        return 0;
    }

    private bool TryFindRod(out ushort sourceType, out ushort sourceSlot)
    {
        var manager = InventoryManager.Instance();
        if (manager != null)
        {
            foreach (var containerType in InventoryContainers)
            {
                var container = manager->GetInventoryContainer((InventoryType)containerType);
                if (container == null)
                    continue;
                for (var slot = 0; slot < container->Size; slot++)
                {
                    var item = container->GetInventorySlot(slot);
                    if (item != null && item->ItemId == weatheredFishingRodItemId)
                    {
                        sourceType = containerType;
                        sourceSlot = (ushort)slot;
                        return true;
                    }
                }
            }

            var armory = manager->GetInventoryContainer((InventoryType)ArmoryMainHandContainer);
            if (armory != null)
            {
                for (var slot = 0; slot < armory->Size; slot++)
                {
                    var item = armory->GetInventorySlot(slot);
                    if (item != null && item->ItemId == weatheredFishingRodItemId)
                    {
                        sourceType = ArmoryMainHandContainer;
                        sourceSlot = (ushort)slot;
                        return true;
                    }
                }
            }
        }

        sourceType = 0;
        sourceSlot = 0;
        return false;
    }

    private bool IsRodEquippedAndFisher()
    {
        if ((int)playerState.ClassJob.RowId != ClassJobIds.Fisher)
            return false;
        var manager = InventoryManager.Instance();
        var equipped = manager == null
            ? null
            : manager->GetInventoryContainer((InventoryType)EquippedItemsContainer);
        var mainHand = equipped == null ? null : equipped->GetInventorySlot(0);
        return mainHand != null && mainHand->ItemId == weatheredFishingRodItemId;
    }

    private void Fail(string failure)
    {
        Failure = failure;
        StatusText = failure;
        IsComplete = true;
        IsActive = false;
        log.Warning($"[Fishing][Fallback] {failure}");
    }
}

public sealed class FisherGearsetTestService
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(45);

    private readonly IFisherGearsetRuntime runtime;
    private readonly IPluginLog log;
    private readonly Action<string> print;
    private FisherGearsetEquipOperation? operation;

    public FisherGearsetTestService(
        IFisherGearsetRuntime runtime,
        IPluginLog log,
        Action<string> print)
    {
        this.runtime = runtime;
        this.log = log;
        this.print = print;
    }

    public bool IsActive => operation is { IsComplete: false };
    public string StatusText { get; private set; } = "Idle";

    public bool Start()
    {
        if (IsActive)
            return false;

        var now = DateTimeOffset.UtcNow;
        operation = new FisherGearsetEquipOperation(runtime, now, now + TestTimeout);
        Publish($"Starting class-job ID: {operation.StartingClassJobId}.");
        StatusText = "Equipping and verifying Fisher";
        return true;
    }

    public void Update()
    {
        if (!IsActive || operation == null)
            return;

        foreach (var entry in operation.Tick(DateTimeOffset.UtcNow))
            Publish(entry.Message);

        if (!operation.IsComplete)
            return;

        StatusText = operation.Succeeded
            ? $"Verified Fisher ({operation.FinalClassJobId})"
            : operation.State.ToString();
    }

    public void Cancel()
    {
        operation = null;
        StatusText = "Idle";
    }

    private void Publish(string message)
    {
        var line = $"[Fishing][GearsetTest] {message}";
        log.Information(line);
        print($"[Vermaxion] {line}");
    }
}
