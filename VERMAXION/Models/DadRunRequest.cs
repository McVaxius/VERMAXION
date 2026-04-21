using System;

namespace VERMAXION.Models;

public sealed class DadRunRequest
{
    public string RequestId { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;
    public string RequestedBy { get; set; } = string.Empty;
    public DadDungeonTask? Dungeon { get; set; }
    public DadDailyMsqTask? DailyMsq { get; set; }
    public DadCommendationTask? Commendation { get; set; }
    public DadAstropeTask? Astrope { get; set; }

    public int GetConfiguredTaskCount()
    {
        var count = 0;
        if (Dungeon != null) count++;
        if (DailyMsq != null) count++;
        if (Commendation != null) count++;
        if (Astrope != null) count++;
        return count;
    }
}

public sealed class DadDungeonTask
{
    public int Count { get; set; } = 1;
    public string Frequency { get; set; } = "per AR";
    public string SelectedDungeon { get; set; } = string.Empty;
    public string SelectedJob { get; set; } = string.Empty;
    public bool Unsynced { get; set; }
}

public sealed class DadDailyMsqTask
{
    public string LanPartyPreset { get; set; } = "Daily MSQ";
}

public sealed class DadCommendationTask
{
    public int Attempts { get; set; } = 1;
}

public sealed class DadAstropeTask
{
    public int Attempts { get; set; } = 1;
    public DadTimeWindow ValidLocalTimeWindow { get; set; } = new();
    public bool CoordinateWithAuraFarmer { get; set; } = true;
}
