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

    // --- Settings ---
    public int ChocoboRacesPerDay { get; set; } = 5;
    public int FCBuffPurchaseAttempts { get; set; } = 15;
    public int FCBuffMinPoints { get; set; } = 500000;
    public int FCBuffMinGil { get; set; } = 16000;
    
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
    public bool RequireSaucyForMiniCactpot { get; set; } = true;

    // --- Plugin State ---
    public bool Enabled { get; set; } = true;

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
            ChocoboRacesPerDay = ChocoboRacesPerDay,
            FCBuffPurchaseAttempts = FCBuffPurchaseAttempts,
            FCBuffMinPoints = FCBuffMinPoints,
            FCBuffMinGil = FCBuffMinGil,
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
            RequireSaucyForMiniCactpot = RequireSaucyForMiniCactpot,
            Enabled = Enabled,
        };
    }
}
