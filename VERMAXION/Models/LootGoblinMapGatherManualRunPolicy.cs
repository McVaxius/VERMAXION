using System;

namespace VERMAXION.Models;

public static class LootGoblinMapGatherRowPolicy
{
    public static string GetStatus(
        string dailyStatus,
        LootGoblinMapGatherServiceState serviceState,
        string serviceStatus)
    {
        if (serviceState == LootGoblinMapGatherServiceState.Idle)
            return dailyStatus;

        return string.IsNullOrWhiteSpace(serviceStatus)
            ? serviceState.ToString()
            : serviceStatus;
    }
}

public sealed class LootGoblinMapGatherManualRunTracker
{
    private string startedCharacterKey = string.Empty;

    public bool IsTracking { get; private set; }

    public void Begin(string characterKey, LootGoblinMapGatherResponse response)
    {
        if (!response.Accepted)
        {
            Clear();
            return;
        }

        startedCharacterKey = characterKey ?? string.Empty;
        IsTracking = true;
    }

    public bool Complete(LootGoblinMapGatherResponse response, string currentCharacterKey)
    {
        if (!response.Terminal)
            return false;

        var shouldStamp = IsTracking &&
                          response.Success &&
                          string.Equals(startedCharacterKey, currentCharacterKey, StringComparison.Ordinal);
        Clear();
        return shouldStamp;
    }

    public void Cancel()
        => Clear();

    private void Clear()
    {
        startedCharacterKey = string.Empty;
        IsTracking = false;
    }
}
