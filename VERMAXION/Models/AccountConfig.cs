using System;
using System.Collections.Generic;

namespace VERMAXION.Models;

[Serializable]
public class AccountConfig
{
    public string AccountId { get; set; } = "";
    public string AccountAlias { get; set; } = "";
    public bool RegistrableInventoryDefaultV04011Applied { get; set; }
    public bool RetainerlessTimerEnabled { get; set; }
    private int retainerlessTimerIntervalMinutes = 30;
    public int RetainerlessTimerIntervalMinutes
    {
        get => retainerlessTimerIntervalMinutes;
        set => retainerlessTimerIntervalMinutes = Math.Max(1, value);
    }
    public CharacterConfig DefaultConfig { get; set; } = CharacterConfig.CreateNew();
    public Dictionary<string, CharacterConfig> Characters { get; set; } = new();
    public Dictionary<string, DateTime> CharacterCreatedAtUtc { get; set; } = new();
}
