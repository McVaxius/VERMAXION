using System;
using System.Collections.Generic;
using System.Linq;

namespace VERMAXION.Models;

[Serializable]
public class CharacterConfig
{
    // --- Feature Toggles ---
    public bool EnableVerminionQueue { get; set; } = false;
    public bool EnableJumboCactpot { get; set; } = false;
    public bool EnableMiniCactpot { get; set; } = false;
    public bool EnableChocoboRacing { get; set; } = false;
    public bool EnableFCBuffRefill { get; set; } = false;
    public bool AllowFCBuffActivation { get; set; } = false;
    public bool MaintainFCBuffStockTarget { get; set; } = false;
    public bool EnableMinionRoulette { get; set; } = false;
    public bool EnableSeasonalGearRoulette { get; set; } = false;
    public bool EnableGearUpdater { get; set; } = false;
    public bool EnableHighestCombatJob { get; set; } = false;
    public bool EnableCurrentJobEquipment { get; set; } = false;
    public bool EnableFashionReport { get; set; } = false;
    public bool EnableRegisterRegistrables { get; set; } = false;
    public bool RegisterUnregisteredItemsFromInventory { get; set; } = false;
    public bool EnableVendorStock { get; set; } = false;
    public bool EnableRefillFromListings { get; set; } = false;
    public bool EnableAfterArPark { get; set; } = false;
    public bool EnableAlliedSociety { get; set; } = false;
    public bool EnableNagYourMom { get; set; } = false;
    public bool EnableNagYourMomCasualCc { get; set; } = true;
    public bool EnableNagYourMomFrontline { get; set; } = false;
    public bool EnableNagYourMomRivalWings { get; set; } = false;
    public bool EnableNagYourDad { get; set; } = false;
    public bool EnableEvercoldAdventurerActivity { get; set; } = false;
    public bool EnableMiscCmd { get; set; } = true;
    public bool EnableLootGoblinMapGather { get; set; } = false;
    public bool EnableFishing { get; set; } = false;
    public bool AlwaysFishOnThisCharacterIfWindowOpen { get; set; } = false;
    public OceanFishingRoutePreference? OceanFishingRouteOverride { get; set; }
    public int FishingLureRestockTarget { get; set; } = FishingDefaults.LureRestockTarget;
    public Dictionary<uint, FishingStockSetting> FishingStockItems { get; set; } =
        FishingStockCatalogPolicy.CreateDefaultSettings();
    public FishingReturnDestination FishingReturnDestination { get; set; } = FishingDefaults.ReturnDestination;
    public string FishingReturnCommand { get; set; } = FishingDefaults.ReturnCommand;
    public FishingRepairMode FishingRepairMode { get; set; } = FishingDefaults.RepairMode;
    public int FishingRepairThresholdPercent { get; set; } = FishingDefaults.RepairThresholdPercent;
    public bool FishingDiscardAfterVoyage { get; set; } = false;
    public bool FishingSellAfterVoyage { get; set; } = false;

    /// <summary>Ocean-fishing food: a SPECIFIC item id to eat in the pre-fishing lobby so Well-Fed covers the
    /// whole voyage. 0 = no specific id — fall back to <see cref="FishingEatAnyFood"/>. NQ+HQ both count; the
    /// item must be in inventory. Eating is skipped when already Well-Fed with comfortable margin.</summary>
    public uint FishingFoodItemId { get; set; } = 0;

    /// <summary>When no specific <see cref="FishingFoodItemId"/> is set (0), eat whatever edible food is in the
    /// bags — scanning for any Meal that grants Well-Fed and preferring GP food (the only stat that helps ocean
    /// fishing). On by default so fishers eat without a per-character item pick; both off = no food eaten.</summary>
    public bool FishingEatAnyFood { get; set; } = true;
    public bool EnableRetainerEquipping { get; set; } = false;
    public RetainerGearSourceMode RetainerGearSourceMode { get; set; } = RetainerGearSourceMode.IgnoreGearset;
    public bool RetainerGearNonUniqueOnly { get; set; } = true;
    public int RetainerCombatItemLevelTarget { get; set; } = 0;
    public int RetainerGatheringPerceptionTarget { get; set; } = 0;
    public string RetainerEquipmentLastUnmetSignature { get; set; } = string.Empty;
    public bool RetainerEquipmentCheckpointPending { get; set; } = false;
    public bool? RetainerEquipmentOriginalCollectOnly { get; set; }

    // --- Settings ---
    public int ChocoboRacesPerDay { get; set; } = 5;
    public bool SkipChocoboRacingAtRank50 { get; set; } = true;
    public ChocoboAutomationMode ChocoboAutomationMode { get; set; } = ChocoboAutomationMode.AlwaysRace;
    public int ChocoboTargetPedigree { get; set; } = 9;
    public int ChocoboRetirementRank { get; set; } = 40;
    public int ChocoboPreferredFeedGrade { get; set; } = 3;
    public int FCBuffPurchaseAttempts { get; set; } = 15;
    public int FCBuffMinPoints { get; set; } = 500000;
    public int FCBuffMinGil { get; set; } = 16000;
    public FCBuffFrequency FCBuffFrequency { get; set; } = FCBuffFrequency.EveryAR;
    public int VendorStockGysahlGreensTarget { get; set; } = 0;
    public int VendorStockGrade8DarkMatterTarget { get; set; } = 0;
    public RefillFromListingsFrequency RefillFromListingsFrequency { get; set; } = RefillFromListingsFrequency.Weekly;
    public RefillFromListingsSelectionMode RefillFromListingsSelectionMode { get; set; } = RefillFromListingsSelectionMode.All;
    public RefillFromListingsRoute RefillFromListingsRoute { get; set; } = RefillFromListingsRoute.Workshop;
    public int RefillFromListingsMinFreeInventorySlots { get; set; } = 20;
    public AfterArParkDestination AfterArParkDestination { get; set; } = AfterArParkDestination.Home;
    public string AfterArParkCustomCommand { get; set; } = string.Empty;
    public AlliedSocietyGearsetSelection AlliedSocietyGearsetSelection { get; set; } = AlliedSocietyGearsetSelection.CurrentJob;
    public int AlliedSocietyGearsetId { get; set; } = -1;
    public int NagYourMomRunsPerDay { get; set; } = 1;
    public int NagYourMomFrontlineRunsPerDay { get; set; } = 1;
    public int NagYourMomRivalWingsRunsPerDay { get; set; } = 1;
    public string NagYourMomJob { get; set; } = "";
    public string NagYourMomWindowStartLocal { get; set; } = "00:00";
    public string NagYourMomWindowEndLocal { get; set; } = "23:59";
    public bool NagYourMomStopAtSeriesRank25 { get; set; } = true;
    public DadSelectionKind NagYourDadSelectionKind { get; set; } = DadSelectionKind.None;
    public string NagYourDadSelectionId { get; set; } = string.Empty;
    public string NagYourDadSelectionDisplayName { get; set; } = string.Empty;
    // Legacy DAD task-builder fields are retained only so older JSON can deserialize.
    public int NagYourDadDungeonCount { get; set; } = 0;
    public string NagYourDadDungeonFrequency { get; set; } = DadRunRequestOptions.FrequencyPerArRun;
    public uint NagYourDadDungeonContentFinderConditionId { get; set; } = 0;
    public string NagYourDadDungeonName { get; set; } = "";
    public string NagYourDadDungeonJob { get; set; } = "";
    public bool NagYourDadQueueViaLanParty { get; set; } = false;
    public bool NagYourDadDungeonUnsynced { get; set; } = false;
    public bool NagYourDadDailyMsq { get; set; } = false;
    public string NagYourDadLanPartyPreset { get; set; } = "Daily MSQ";
    public int NagYourDadCommendationAttempts { get; set; } = 0;
    public int NagYourDadAstropeAttempts { get; set; } = 0;
    public string NagYourDadWindowStartLocal { get; set; } = "00:00";
    public string NagYourDadWindowEndLocal { get; set; } = "23:59";
    public int EvercoldAdventurerActivityCurrentPoints { get; set; } = 0;
    public int EvercoldAdventurerActivityTargetPoints { get; set; } = 0;
    public bool EvercoldAdventurerActivityCompleted { get; set; } = false;
    public uint LootGoblinMapGatherItemId { get; set; } = 43556;
    public bool LootGoblinMapGatherRunAfterGather { get; set; } = false;
    
    // --- Personal Registrable Items ---
    public List<uint> PersonalRegistrableItems { get; set; } = new();

    // --- State Tracking (runtime, persisted per reset cycle) ---
    public DateTime LastWeeklyReset { get; set; } = DateTime.MinValue;
    public DateTime LastDailyReset { get; set; } = DateTime.MinValue;
    public bool VerminionCompletedThisWeek { get; set; } = false;
    public bool JumboCactpotCompletedThisWeek { get; set; } = false;
    public bool MiniCactpotCompletedToday { get; set; } = false;
    public bool ChocoboRacingCompletedToday { get; set; } = false;
    public bool FashionReportCompletedThisWeek { get; set; } = false;
    
    // --- NEW: Per-Task Reset System (LastCompleted/NextReset) ---
    // Weekly Tasks (Tuesday 9:00 UTC)
    public DateTime VerminionLastCompleted { get; set; } = DateTime.MinValue;
    public DateTime VerminionNextReset { get; set; } = DateTime.MinValue;
    public DateTime JumboCactpotLastCompleted { get; set; } = DateTime.MinValue;
    public DateTime JumboCactpotNextReset { get; set; } = DateTime.MinValue;
    public int? JumboCactpotUnclaimedTickets { get; set; }
    public DateTime JumboCactpotPayoutAvailableAt { get; set; } = DateTime.MinValue;
    public DateTime FashionReportLastCompleted { get; set; } = DateTime.MinValue;
    public DateTime FashionReportNextReset { get; set; } = DateTime.MinValue;
    
    // Daily Tasks (9:00 UTC)
    public DateTime MiniCactpotLastCompleted { get; set; } = DateTime.MinValue;
    public DateTime MiniCactpotNextReset { get; set; } = DateTime.MinValue;
    public DateTime ChocoboRacingLastCompleted { get; set; } = DateTime.MinValue;
    public DateTime ChocoboRacingNextReset { get; set; } = DateTime.MinValue;
    public DateTime LootGoblinMapGatherLastCompleted { get; set; } = DateTime.MinValue;
    public DateTime LootGoblinMapGatherNextReset { get; set; } = DateTime.MinValue;
    public DateTime FCBuffLastCompleted { get; set; } = DateTime.MinValue;
    public DateTime FCBuffNextReset { get; set; } = DateTime.MinValue;
    public DateTime RefillFromListingsLastCompleted { get; set; } = DateTime.MinValue;
    public DateTime RefillFromListingsNextReset { get; set; } = DateTime.MinValue;
    public DateTime AlliedSocietyLastCompleted { get; set; } = DateTime.MinValue;
    public DateTime AlliedSocietyNextReset { get; set; } = DateTime.MinValue;
    
    // --- Enhanced State Tracking ---
    public int MiniCactpotTicketsToday { get; set; } = 0;
    public int MinionRouletteAttemptsToday { get; set; } = 0;
    public DateTime LastMinionRouletteReset { get; set; } = DateTime.MinValue;
    public int NagYourMomAttemptsToday { get; set; } = 0;
    public DateTime NagYourMomLastLocalDate { get; set; } = DateTime.MinValue;
    public int NagYourMomFrontlineAttemptsToday { get; set; } = 0;
    public DateTime NagYourMomFrontlineLastLocalDate { get; set; } = DateTime.MinValue;
    public int NagYourMomRivalWingsAttemptsToday { get; set; } = 0;
    public DateTime NagYourMomRivalWingsLastLocalDate { get; set; } = DateTime.MinValue;
    public bool RequireSaucyForMiniCactpot { get; set; } = true;
    public JumboCactpotNumberMode JumboCactpotNumberMode { get; set; } = JumboCactpotNumberMode.Random;
    public int JumboCactpotFixedNumber { get; set; } = 1;

    // --- Plugin State ---
    public bool Enabled { get; set; } = true;

    public static CharacterConfig CreateNew() => new()
    {
        JumboCactpotUnclaimedTickets = 0,
    };

    public void ResetVerminionState()
    {
        VerminionCompletedThisWeek = false;
        VerminionLastCompleted = DateTime.MinValue;
        VerminionNextReset = DateTime.MinValue;
    }

    public void ResetJumboCactpotState()
    {
        JumboCactpotCompletedThisWeek = false;
        JumboCactpotLastCompleted = DateTime.MinValue;
        JumboCactpotNextReset = DateTime.MinValue;
        JumboCactpotUnclaimedTickets = null;
        JumboCactpotPayoutAvailableAt = DateTime.MinValue;
    }

    public void ResetFashionReportState()
    {
        FashionReportCompletedThisWeek = false;
        FashionReportLastCompleted = DateTime.MinValue;
        FashionReportNextReset = DateTime.MinValue;
    }

    public void ResetMiniCactpotState()
    {
        MiniCactpotCompletedToday = false;
        MiniCactpotLastCompleted = DateTime.MinValue;
        MiniCactpotNextReset = DateTime.MinValue;
        MiniCactpotTicketsToday = 0;
    }

    public void ResetChocoboRacingState()
    {
        ChocoboRacingCompletedToday = false;
        ChocoboRacingLastCompleted = DateTime.MinValue;
        ChocoboRacingNextReset = DateTime.MinValue;
    }

    public void ResetLootGoblinMapGatherState()
    {
        LootGoblinMapGatherLastCompleted = DateTime.MinValue;
        LootGoblinMapGatherNextReset = DateTime.MinValue;
    }

    public void ResetFCBuffState()
    {
        FCBuffLastCompleted = DateTime.MinValue;
        FCBuffNextReset = DateTime.MinValue;
    }

    public void ResetNagYourMomDailyState()
    {
        NagYourMomAttemptsToday = 0;
        NagYourMomLastLocalDate = DateTime.MinValue;
        NagYourMomFrontlineAttemptsToday = 0;
        NagYourMomFrontlineLastLocalDate = DateTime.MinValue;
        NagYourMomRivalWingsAttemptsToday = 0;
        NagYourMomRivalWingsLastLocalDate = DateTime.MinValue;
    }

    public void ResetMinionRouletteDailyState()
    {
        MinionRouletteAttemptsToday = 0;
        LastMinionRouletteReset = DateTime.MinValue;
    }

    public void ResetEvercoldAdventurerActivityState()
    {
        EvercoldAdventurerActivityCurrentPoints = 0;
        EvercoldAdventurerActivityCompleted = false;
    }

    public void ResetRefillFromListingsState()
    {
        RefillFromListingsLastCompleted = DateTime.MinValue;
        RefillFromListingsNextReset = DateTime.MinValue;
    }

    public void ResetAlliedSocietyState()
    {
        AlliedSocietyLastCompleted = DateTime.MinValue;
        AlliedSocietyNextReset = DateTime.MinValue;
    }

    public void ResetWeeklySectionState()
    {
        LastWeeklyReset = DateTime.MinValue;
        ResetVerminionState();
        ResetJumboCactpotState();
        ResetFashionReportState();
    }

    public void ResetDailySectionState()
    {
        LastDailyReset = DateTime.MinValue;
        ResetMiniCactpotState();
        ResetChocoboRacingState();
        ResetLootGoblinMapGatherState();
        ResetNagYourMomDailyState();
        ResetMinionRouletteDailyState();
        ResetAlliedSocietyState();
    }

    public void ResetAllTaskState()
    {
        ResetWeeklySectionState();
        ResetDailySectionState();
        ResetEvercoldAdventurerActivityState();
        ResetFCBuffState();
        ResetRefillFromListingsState();
    }

    public CharacterConfig Clone()
    {
        return new CharacterConfig
        {
            EnableVerminionQueue = EnableVerminionQueue,
            EnableJumboCactpot = EnableJumboCactpot,
            EnableMiniCactpot = EnableMiniCactpot,
            EnableChocoboRacing = EnableChocoboRacing,
            EnableFCBuffRefill = EnableFCBuffRefill,
            AllowFCBuffActivation = AllowFCBuffActivation,
            MaintainFCBuffStockTarget = MaintainFCBuffStockTarget,
            EnableMinionRoulette = EnableMinionRoulette,
            EnableSeasonalGearRoulette = EnableSeasonalGearRoulette,
            EnableGearUpdater = EnableGearUpdater,
            EnableHighestCombatJob = EnableHighestCombatJob,
            EnableCurrentJobEquipment = EnableCurrentJobEquipment,
            EnableFashionReport = EnableFashionReport,
            EnableRegisterRegistrables = EnableRegisterRegistrables,
            RegisterUnregisteredItemsFromInventory = RegisterUnregisteredItemsFromInventory,
            EnableVendorStock = EnableVendorStock,
            EnableRefillFromListings = EnableRefillFromListings,
            EnableAfterArPark = EnableAfterArPark,
            EnableAlliedSociety = EnableAlliedSociety,
            EnableNagYourMom = EnableNagYourMom,
            EnableNagYourMomCasualCc = EnableNagYourMomCasualCc,
            EnableNagYourMomFrontline = EnableNagYourMomFrontline,
            EnableNagYourMomRivalWings = EnableNagYourMomRivalWings,
            EnableNagYourDad = EnableNagYourDad,
            EnableEvercoldAdventurerActivity = EnableEvercoldAdventurerActivity,
            EnableMiscCmd = EnableMiscCmd,
            EnableLootGoblinMapGather = EnableLootGoblinMapGather,
            EnableFishing = EnableFishing,
            AlwaysFishOnThisCharacterIfWindowOpen = AlwaysFishOnThisCharacterIfWindowOpen,
            OceanFishingRouteOverride = OceanFishingRouteOverride,
            FishingLureRestockTarget = FishingLureRestockTarget,
            FishingStockItems = FishingStockItems.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Clone()),
            FishingReturnDestination = FishingReturnDestination,
            FishingReturnCommand = FishingReturnCommand,
            FishingRepairMode = FishingRepairMode,
            FishingRepairThresholdPercent = FishingRepairThresholdPercent,
            FishingDiscardAfterVoyage = FishingDiscardAfterVoyage,
            FishingSellAfterVoyage = FishingSellAfterVoyage,
            EnableRetainerEquipping = EnableRetainerEquipping,
            RetainerGearSourceMode = RetainerGearSourceMode,
            RetainerGearNonUniqueOnly = RetainerGearNonUniqueOnly,
            RetainerCombatItemLevelTarget = RetainerCombatItemLevelTarget,
            RetainerGatheringPerceptionTarget = RetainerGatheringPerceptionTarget,
            RetainerEquipmentLastUnmetSignature = RetainerEquipmentLastUnmetSignature,
            RetainerEquipmentCheckpointPending = RetainerEquipmentCheckpointPending,
            RetainerEquipmentOriginalCollectOnly = RetainerEquipmentOriginalCollectOnly,
            ChocoboRacesPerDay = ChocoboRacesPerDay,
            SkipChocoboRacingAtRank50 = SkipChocoboRacingAtRank50,
            ChocoboAutomationMode = ChocoboAutomationMode,
            ChocoboTargetPedigree = ChocoboTargetPedigree,
            ChocoboRetirementRank = ChocoboRetirementRank,
            ChocoboPreferredFeedGrade = ChocoboPreferredFeedGrade,
            FCBuffPurchaseAttempts = FCBuffPurchaseAttempts,
            FCBuffMinPoints = FCBuffMinPoints,
            FCBuffMinGil = FCBuffMinGil,
            FCBuffFrequency = FCBuffFrequency,
            VendorStockGysahlGreensTarget = VendorStockGysahlGreensTarget,
            VendorStockGrade8DarkMatterTarget = VendorStockGrade8DarkMatterTarget,
            RefillFromListingsFrequency = RefillFromListingsFrequency,
            RefillFromListingsSelectionMode = RefillFromListingsSelectionMode,
            RefillFromListingsRoute = RefillFromListingsRoute,
            RefillFromListingsMinFreeInventorySlots = RefillFromListingsMinFreeInventorySlots,
            AfterArParkDestination = AfterArParkDestination,
            AfterArParkCustomCommand = AfterArParkCustomCommand,
            AlliedSocietyGearsetSelection = AlliedSocietyGearsetSelection,
            AlliedSocietyGearsetId = AlliedSocietyGearsetId,
            NagYourMomRunsPerDay = NagYourMomRunsPerDay,
            NagYourMomFrontlineRunsPerDay = NagYourMomFrontlineRunsPerDay,
            NagYourMomRivalWingsRunsPerDay = NagYourMomRivalWingsRunsPerDay,
            NagYourMomJob = NagYourMomJob,
            NagYourMomWindowStartLocal = NagYourMomWindowStartLocal,
            NagYourMomWindowEndLocal = NagYourMomWindowEndLocal,
            NagYourMomStopAtSeriesRank25 = NagYourMomStopAtSeriesRank25,
            NagYourDadSelectionKind = NagYourDadSelectionKind,
            NagYourDadSelectionId = NagYourDadSelectionId,
            NagYourDadSelectionDisplayName = NagYourDadSelectionDisplayName,
            NagYourDadDungeonCount = NagYourDadDungeonCount,
            NagYourDadDungeonFrequency = DadRunRequestOptions.NormalizeFrequency(NagYourDadDungeonFrequency),
            NagYourDadDungeonContentFinderConditionId = NagYourDadDungeonContentFinderConditionId,
            NagYourDadDungeonName = NagYourDadDungeonName,
            NagYourDadDungeonJob = NagYourDadDungeonJob,
            NagYourDadQueueViaLanParty = NagYourDadQueueViaLanParty,
            NagYourDadDungeonUnsynced = NagYourDadDungeonUnsynced,
            NagYourDadDailyMsq = NagYourDadDailyMsq,
            NagYourDadLanPartyPreset = NagYourDadLanPartyPreset,
            NagYourDadCommendationAttempts = NagYourDadCommendationAttempts,
            NagYourDadAstropeAttempts = NagYourDadAstropeAttempts,
            NagYourDadWindowStartLocal = NagYourDadWindowStartLocal,
            NagYourDadWindowEndLocal = NagYourDadWindowEndLocal,
            EvercoldAdventurerActivityCurrentPoints = EvercoldAdventurerActivityCurrentPoints,
            EvercoldAdventurerActivityTargetPoints = EvercoldAdventurerActivityTargetPoints,
            EvercoldAdventurerActivityCompleted = EvercoldAdventurerActivityCompleted,
            LootGoblinMapGatherItemId = LootGoblinMapGatherItemId,
            LootGoblinMapGatherRunAfterGather = LootGoblinMapGatherRunAfterGather,
            PersonalRegistrableItems = new List<uint>(PersonalRegistrableItems),
            LastWeeklyReset = LastWeeklyReset,
            LastDailyReset = LastDailyReset,
            VerminionCompletedThisWeek = VerminionCompletedThisWeek,
            JumboCactpotCompletedThisWeek = JumboCactpotCompletedThisWeek,
            MiniCactpotCompletedToday = MiniCactpotCompletedToday,
            ChocoboRacingCompletedToday = ChocoboRacingCompletedToday,
            FashionReportCompletedThisWeek = FashionReportCompletedThisWeek,
            // NEW: Per-Task Reset System
            VerminionLastCompleted = VerminionLastCompleted,
            VerminionNextReset = VerminionNextReset,
            JumboCactpotLastCompleted = JumboCactpotLastCompleted,
            JumboCactpotNextReset = JumboCactpotNextReset,
            JumboCactpotUnclaimedTickets = JumboCactpotUnclaimedTickets,
            JumboCactpotPayoutAvailableAt = JumboCactpotPayoutAvailableAt,
            FashionReportLastCompleted = FashionReportLastCompleted,
            FashionReportNextReset = FashionReportNextReset,
            MiniCactpotLastCompleted = MiniCactpotLastCompleted,
            MiniCactpotNextReset = MiniCactpotNextReset,
            ChocoboRacingLastCompleted = ChocoboRacingLastCompleted,
            ChocoboRacingNextReset = ChocoboRacingNextReset,
            LootGoblinMapGatherLastCompleted = LootGoblinMapGatherLastCompleted,
            LootGoblinMapGatherNextReset = LootGoblinMapGatherNextReset,
            FCBuffLastCompleted = FCBuffLastCompleted,
            FCBuffNextReset = FCBuffNextReset,
            RefillFromListingsLastCompleted = RefillFromListingsLastCompleted,
            RefillFromListingsNextReset = RefillFromListingsNextReset,
            AlliedSocietyLastCompleted = AlliedSocietyLastCompleted,
            AlliedSocietyNextReset = AlliedSocietyNextReset,
            MiniCactpotTicketsToday = MiniCactpotTicketsToday,
            MinionRouletteAttemptsToday = MinionRouletteAttemptsToday,
            LastMinionRouletteReset = LastMinionRouletteReset,
            NagYourMomAttemptsToday = NagYourMomAttemptsToday,
            NagYourMomLastLocalDate = NagYourMomLastLocalDate,
            NagYourMomFrontlineAttemptsToday = NagYourMomFrontlineAttemptsToday,
            NagYourMomFrontlineLastLocalDate = NagYourMomFrontlineLastLocalDate,
            NagYourMomRivalWingsAttemptsToday = NagYourMomRivalWingsAttemptsToday,
            NagYourMomRivalWingsLastLocalDate = NagYourMomRivalWingsLastLocalDate,
            RequireSaucyForMiniCactpot = RequireSaucyForMiniCactpot,
            JumboCactpotNumberMode = JumboCactpotNumberMode,
            JumboCactpotFixedNumber = JumboCactpotFixedNumber,
            Enabled = Enabled,
        };
    }
}

public enum JumboCactpotNumberMode
{
    Random,
    Fixed,
}

public enum RefillFromListingsFrequency
{
    EveryAR,
    Daily,
    Weekly,
    Monthly,
}

public enum FCBuffFrequency
{
    EveryAR,
    Daily,
    Weekly,
    Monthly,
}

public enum RefillFromListingsSelectionMode
{
    All,
    Random,
}

public enum RefillFromListingsRoute
{
    Workshop = 0,
    Inn = 1,
    Limsa = 2,
}

public enum AfterArParkDestination
{
    Home = 0,
    Limsa = 1,
    FreeCompany = 2,
    Inn = 3,
    Workshop = 4,
    Custom = 5,
}

public enum AlliedSocietyGearsetSelection
{
    CurrentJob = 0,
    SavedGearset = 1,
}
