using System;

namespace VERMAXION.Models;

public sealed class SeriesRankSnapshot
{
    public int Rank { get; set; }
    public DateTime CapturedAtUtc { get; set; }
    public string Source { get; set; } = string.Empty;
    public string FailureReason { get; set; } = string.Empty;

    public bool Success => string.IsNullOrWhiteSpace(FailureReason) && Rank >= 1;
}
