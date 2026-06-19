using System;

namespace VERMAXION.Models;

public sealed class LootGoblinMapInfo
{
    public uint ItemId { get; set; }
    public string MapName { get; set; } = string.Empty;
    public string Tier { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Expansion { get; set; } = string.Empty;
    public int MinLevel { get; set; }
    public bool HasDungeon { get; set; }
    public bool IsGatherable { get; set; }
    public bool SoloOutdoorSafe { get; set; }
    public string DisplayName => string.IsNullOrWhiteSpace(MapName) ? $"Map {ItemId}" : $"{MapName} ({ItemId})";
}

public sealed class LootGoblinMapGatherRequest
{
    public string RequestId { get; set; } = Guid.NewGuid().ToString("N");
    public uint ItemId { get; set; }
    public string MapName { get; set; } = string.Empty;
    public bool RunAfterGather { get; set; }
}

public sealed class LootGoblinMapGatherResponse
{
    public string RequestId { get; set; } = string.Empty;
    public uint ItemId { get; set; }
    public string MapName { get; set; } = string.Empty;
    public bool RunAfterGather { get; set; }
    public bool Accepted { get; set; }
    public bool Terminal { get; set; }
    public bool Success { get; set; }
    public string State { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Tier { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool HasDungeon { get; set; }
    public bool IsGatherable { get; set; }
    public bool SoloOutdoorSafe { get; set; }

    public static LootGoblinMapGatherResponse Unavailable(string message)
        => new()
        {
            Accepted = false,
            Terminal = true,
            Success = false,
            State = "Unavailable",
            Message = message,
        };
}

public static class LootGoblinMapSafetyPolicy
{
    public static bool IsSoloOutdoorSafe(LootGoblinMapInfo map)
        => string.Equals(map.Tier, "Solo", StringComparison.OrdinalIgnoreCase) &&
           string.Equals(map.Category, "Outdoor", StringComparison.OrdinalIgnoreCase) &&
           !map.HasDungeon;
}
