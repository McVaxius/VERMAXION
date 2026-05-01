using System;
using System.Collections.Generic;

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
    public bool EnableHenchmanManagement { get; set; } = false;
    public bool EnableMinionRoulette { get; set; } = false;
    public bool EnableSeasonalGearRoulette { get; set; } = false;
    public bool EnableGearUpdater { get; set; } = false;
    public bool EnableHighestCombatJob { get; set; } = false;
    public bool EnableCurrentJobEquipment { get; set; } = false;
    public bool EnableFashionReport { get; set; } = false;
    public bool EnableRegisterRegistrables { get; set; } = false;
    public bool EnableVendorStock { get; set; } = false;
    public bool EnableNagYourMom { get; set; } = false;
    public bool EnableNagYourDad { get; set; } = false;
    public bool EnableEvercoldAdventurerActivity { get; set; } = false;

    // --- Settings ---
    public int ChocoboRacesPerDay { get; set; } = 5;
    public bool SkipChocoboRacingAtRank50 { get; set; } = true;
    public int FCBuffPurchaseAttempts { get; set; } = 15;
    public int FCBuffMinPoints { get; set; } = 500000;
    public int FCBuffMinGil { get; set; } = 16000;
    public int VendorStockGysahlGreensTarget { get; set; } = 0;
    public int VendorStockGrade8DarkMatterTarget { get; set; } = 0;
    public int NagYourMomRunsPerDay { get; set; } = 1;
    public string NagYourMomJob { get; set; } = "";
    public string NagYourMomWindowStartLocal { get; set; } = "00:00";
    public string NagYourMomWindowEndLocal { get; set; } = "23:59";
    public bool NagYourMomStopAtSeriesRank25 { get; set; } = true;
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
    public DateTime FashionReportLastCompleted { get; set; } = DateTime.MinValue;
    public DateTime FashionReportNextReset { get; set; } = DateTime.MinValue;
    
    // Daily Tasks (9:00 UTC)
    public DateTime MiniCactpotLastCompleted { get; set; } = DateTime.MinValue;
    public DateTime MiniCactpotNextReset { get; set; } = DateTime.MinValue;
    public DateTime ChocoboRacingLastCompleted { get; set; } = DateTime.MinValue;
    public DateTime ChocoboRacingNextReset { get; set; } = DateTime.MinValue;
    
    // --- Enhanced State Tracking ---
    public int MiniCactpotTicketsToday { get; set; } = 0;
    public int MinionRouletteAttemptsToday { get; set; } = 0;
    public DateTime LastMinionRouletteReset { get; set; } = DateTime.MinValue;
    public int NagYourMomAttemptsToday { get; set; } = 0;
    public DateTime NagYourMomLastLocalDate { get; set; } = DateTime.MinValue;
    public bool RequireSaucyForMiniCactpot { get; set; } = true;
    public JumboCactpotNumberMode JumboCactpotNumberMode { get; set; } = JumboCactpotNumberMode.Random;
    public int JumboCactpotFixedNumber { get; set; } = 1;

    // --- Plugin State ---
    public bool Enabled { get; set; } = true;

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

    public void ResetNagYourMomDailyState()
    {
        NagYourMomAttemptsToday = 0;
        NagYourMomLastLocalDate = DateTime.MinValue;
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
        ResetNagYourMomDailyState();
        ResetMinionRouletteDailyState();
    }

    public void ResetAllTaskState()
    {
        ResetWeeklySectionState();
        ResetDailySectionState();
        ResetEvercoldAdventurerActivityState();
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
            EnableHenchmanManagement = EnableHenchmanManagement,
            EnableMinionRoulette = EnableMinionRoulette,
            EnableSeasonalGearRoulette = EnableSeasonalGearRoulette,
            EnableGearUpdater = EnableGearUpdater,
            EnableHighestCombatJob = EnableHighestCombatJob,
            EnableCurrentJobEquipment = EnableCurrentJobEquipment,
            EnableFashionReport = EnableFashionReport,
            EnableRegisterRegistrables = EnableRegisterRegistrables,
            EnableVendorStock = EnableVendorStock,
            EnableNagYourMom = EnableNagYourMom,
            EnableNagYourDad = EnableNagYourDad,
            EnableEvercoldAdventurerActivity = EnableEvercoldAdventurerActivity,
            ChocoboRacesPerDay = ChocoboRacesPerDay,
            SkipChocoboRacingAtRank50 = SkipChocoboRacingAtRank50,
            FCBuffPurchaseAttempts = FCBuffPurchaseAttempts,
            FCBuffMinPoints = FCBuffMinPoints,
            FCBuffMinGil = FCBuffMinGil,
            VendorStockGysahlGreensTarget = VendorStockGysahlGreensTarget,
            VendorStockGrade8DarkMatterTarget = VendorStockGrade8DarkMatterTarget,
            NagYourMomRunsPerDay = NagYourMomRunsPerDay,
            NagYourMomJob = NagYourMomJob,
            NagYourMomWindowStartLocal = NagYourMomWindowStartLocal,
            NagYourMomWindowEndLocal = NagYourMomWindowEndLocal,
            NagYourMomStopAtSeriesRank25 = NagYourMomStopAtSeriesRank25,
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
            FashionReportLastCompleted = FashionReportLastCompleted,
            FashionReportNextReset = FashionReportNextReset,
            MiniCactpotLastCompleted = MiniCactpotLastCompleted,
            MiniCactpotNextReset = MiniCactpotNextReset,
            ChocoboRacingLastCompleted = ChocoboRacingLastCompleted,
            ChocoboRacingNextReset = ChocoboRacingNextReset,
            MiniCactpotTicketsToday = MiniCactpotTicketsToday,
            MinionRouletteAttemptsToday = MinionRouletteAttemptsToday,
            LastMinionRouletteReset = LastMinionRouletteReset,
            NagYourMomAttemptsToday = NagYourMomAttemptsToday,
            NagYourMomLastLocalDate = NagYourMomLastLocalDate,
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
