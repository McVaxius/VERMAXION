using System;

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

    // --- Settings ---
    public int ChocoboRacesPerDay { get; set; } = 5;
    public int FCBuffPurchaseAttempts { get; set; } = 15;
    public int FCBuffMinPoints { get; set; } = 500000;
    public int FCBuffMinGil { get; set; } = 16000;

    // --- State Tracking (runtime, persisted per reset cycle) ---
    public DateTime LastWeeklyReset { get; set; } = DateTime.MinValue;
    public DateTime LastDailyReset { get; set; } = DateTime.MinValue;
    public bool VerminionCompletedThisWeek { get; set; } = false;
    public bool JumboCactpotCompletedThisWeek { get; set; } = false;
    public bool MiniCactpotCompletedToday { get; set; } = false;
    public bool ChocoboRacingCompletedToday { get; set; } = false;

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
            ChocoboRacesPerDay = ChocoboRacesPerDay,
            FCBuffPurchaseAttempts = FCBuffPurchaseAttempts,
            FCBuffMinPoints = FCBuffMinPoints,
            FCBuffMinGil = FCBuffMinGil,
            LastWeeklyReset = LastWeeklyReset,
            LastDailyReset = LastDailyReset,
            VerminionCompletedThisWeek = VerminionCompletedThisWeek,
            JumboCactpotCompletedThisWeek = JumboCactpotCompletedThisWeek,
            MiniCactpotCompletedToday = MiniCactpotCompletedToday,
            ChocoboRacingCompletedToday = ChocoboRacingCompletedToday,
            Enabled = Enabled,
        };
    }
}
