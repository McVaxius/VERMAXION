using System;
using System.Collections.Generic;

namespace Vermaxion.Models;

[Serializable]
public class CharacterConfig
{
    public string CharacterName { get; set; } = "";
    public string CharacterWorld { get; set; } = "";
    public ulong CharacterId { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime LastUpdated { get; set; } = DateTime.Now;

    // Mini Cactpot Settings
    public bool MiniCactpotEnabled { get; set; } = false;
    public bool MiniCactpotAutoSubmit { get; set; } = true;
    public int MiniCactpotStrategy { get; set; } = 0; // 0=Random, 1=Optimal, 2=Custom

    // Chocobo Race Settings
    public bool ChocoboRacesEnabled { get; set; } = false;
    public int ChocoboRacesCount { get; set; } = 3;
    public int ChocoboRaceBettingStrategy { get; set; } = 0; // 0=Favorite, 1=Underdog, 2=Balanced
    public bool ChocoboRaceAutoWatch { get; set; } = true;

    // Jumbo Cactpot Settings
    public bool JumboCactpotEnabled { get; set; } = false;
    public bool JumboCactpotAutoSubmit { get; set; } = true;
    public int[] JumboCactpotNumbers { get; set; } = new int[3]; // Custom numbers
    public bool JumboCactpotUseCustomNumbers { get; set; } = false;

    // Varminion Settings
    public bool VarminionEnabled { get; set; } = false;
    public int VarminionAttempts { get; set; } = 5;
    public bool VarminionReEnableAutoRetainer { get; set; } = true;
    public int VarminionQueueDelay { get; set; } = 5000;
    public int VarminionFailureDelay { get; set; } = 3000;

    // ARPostprocess Rules
    public List<ARPostprocessRule> ARPostprocessRules { get; set; } = new();

    // Statistics
    public CharacterStatistics Statistics { get; set; } = new();

    public void Reset()
    {
        LastUpdated = DateTime.Now;
        Statistics.Reset();
    }

    public string GetDisplayName() => $"{CharacterName}@{CharacterWorld}";
}

[Serializable]
public class ARPostprocessRule
{
    public string RuleName { get; set; } = "";
    public TriggerType TriggerType { get; set; } = TriggerType.Manual;
    public string TriggerCondition { get; set; } = "";
    public ActionType ActionType { get; set; } = ActionType.MiniCactpot;
    public Dictionary<string, object> ActionParameters { get; set; } = new();
    public bool Enabled { get; set; } = true;
    public DateTime? LastTriggered { get; set; } = null;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public bool ShouldTrigger(string condition)
    {
        if (!Enabled) return false;
        
        return TriggerType switch
        {
            TriggerType.Manual => false, // Manual triggers are user-initiated
            TriggerType.Login => condition == "login",
            TriggerType.ZoneChange => condition.Contains(TriggerCondition),
            TriggerType.TimeBased => CheckTimeCondition(),
            _ => false
        };
    }

    private bool CheckTimeCondition()
    {
        // TODO: Implement time-based condition checking
        return false;
    }
}

public enum TriggerType
{
    Manual,
    Login,
    ZoneChange,
    TimeBased
}

public enum ActionType
{
    MiniCactpot,
    ChocoboRaces,
    JumboCactpot,
    Varminion
}

[Serializable]
public class CharacterStatistics
{
    // Mini Cactpot Stats
    public int MiniCactpotAttempts { get; set; } = 0;
    public int MiniCactpotWins { get; set; } = 0;
    public long MiniCactpotTotalWinnings { get; set; } = 0;
    public DateTime? LastMiniCactpot { get; set; } = null;

    // Chocobo Race Stats
    public int ChocoboRaceAttempts { get; set; } = 0;
    public int ChocoboRaceWins { get; set; } = 0;
    public long ChocoboRaceTotalWinnings { get; set; } = 0;
    public long ChocoboRaceTotalBets { get; set; } = 0;
    public DateTime? LastChocoboRace { get; set; } = null;

    // Jumbo Cactpot Stats
    public int JumboCactpotAttempts { get; set; } = 0;
    public int JumboCactpotWins { get; set; } = 0;
    public long JumboCactpotTotalWinnings { get; set; } = 0;
    public DateTime? LastJumboCactpot { get; set; } = null;

    // Varminion Stats
    public int VarminionAttempts { get; set; } = 0;
    public int VarminionCompletions { get; set; } = 0;
    public DateTime? LastVarminion { get; set; } = null;

    public void Reset()
    {
        MiniCactpotAttempts = 0;
        MiniCactpotWins = 0;
        MiniCactpotTotalWinnings = 0;
        LastMiniCactpot = null;

        ChocoboRaceAttempts = 0;
        ChocoboRaceWins = 0;
        ChocoboRaceTotalWinnings = 0;
        ChocoboRaceTotalBets = 0;
        LastChocoboRace = null;

        JumboCactpotAttempts = 0;
        JumboCactpotWins = 0;
        JumboCactpotTotalWinnings = 0;
        LastJumboCactpot = null;

        VarminionAttempts = 0;
        VarminionCompletions = 0;
        LastVarminion = null;
    }
}
