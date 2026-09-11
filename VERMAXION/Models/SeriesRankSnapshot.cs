using System;

namespace VERMAXION.Models;

public sealed class SeriesRankSnapshot
{
    public int Rank { get; set; }
    public bool Pending { get; set; }
    public DateTime CapturedAtUtc { get; set; }
    public string Source { get; set; } = string.Empty;
    public string FailureReason { get; set; } = string.Empty;

    public bool Success => !Pending && string.IsNullOrWhiteSpace(FailureReason) && Rank is >= 1 and <= 25;
}
