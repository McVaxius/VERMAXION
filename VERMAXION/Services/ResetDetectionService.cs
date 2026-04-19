using System;
using Dalamud.Plugin.Services;
using VERMAXION.Models;

namespace VERMAXION.Services;

public class ResetDetectionService
{
    private const int WeeklyResetHourUtc = 9;
    private const int DailyResetHourUtc = 9;
    private const int FashionReportStartHourUtc = 1;
    private const DayOfWeek FashionReportDay = DayOfWeek.Friday;
    private readonly IPluginLog log;

    public ResetDetectionService(IPluginLog log)
    {
        this.log = log;
    }

    public bool CheckWeeklyReset(CharacterConfig config)
    {
        var now = DateTime.UtcNow;
        var lastReset = GetLastWeeklyReset(now);

        // Only trigger weekly reset detection if weekly timestamp was manually reset
        if (config.LastWeeklyReset == DateTime.MinValue)
        {
            // Set completion time to 1 hour before reset to force re-run
            config.LastWeeklyReset = lastReset.AddHours(-1);
            config.VerminionCompletedThisWeek = false;
            config.JumboCactpotCompletedThisWeek = false;
            config.FashionReportCompletedThisWeek = false;
            log.Information($"Weekly reset detected (manual reset). Setting completion time to: {config.LastWeeklyReset:u}");
            return true;
        }
        
        // Check for actual weekly reset (not manual)
        if (config.LastWeeklyReset < lastReset)
        {
            config.LastWeeklyReset = now; // Set to now when we detect reset and clear tasks
            config.VerminionCompletedThisWeek = false;
            config.JumboCactpotCompletedThisWeek = false;
            config.FashionReportCompletedThisWeek = false;
            log.Information($"Weekly reset detected. Last reset: {lastReset:u}");
            return true;
        }
        
        return false;
    }

    public bool CheckDailyReset(CharacterConfig config)
    {
        var now = DateTime.UtcNow;
        var lastReset = GetLastDailyReset(now);

        // Only trigger daily reset detection if daily timestamp was manually reset
        if (config.LastDailyReset == DateTime.MinValue)
        {
            // Set completion time to 1 hour before reset to force re-run
            config.LastDailyReset = lastReset.AddHours(-1);
            config.MiniCactpotCompletedToday = false;
            config.ChocoboRacingCompletedToday = false;
            config.MiniCactpotTicketsToday = 0;
            log.Information($"Daily reset detected (manual reset). Setting completion time to: {config.LastDailyReset:u}");
            return true;
        }
        
        // Check for actual daily reset (not manual)
        if (config.LastDailyReset < lastReset)
        {
            config.LastDailyReset = now; // Set to now when we detect reset and clear tasks
            config.MiniCactpotCompletedToday = false;
            config.ChocoboRacingCompletedToday = false;
            config.MiniCactpotTicketsToday = 0;
            log.Information($"Daily reset detected. Last reset: {lastReset:u}");
            return true;
        }
        
        return false;
    }

    public bool IsSaturday()
    {
        return DateTime.UtcNow.DayOfWeek == DayOfWeek.Saturday;
    }

    public static DateTime GetLastWeeklyReset(DateTime now)
    {
        return GetLastOccurrence(now, DayOfWeek.Tuesday, WeeklyResetHourUtc);
    }

    public static DateTime GetLastDailyReset(DateTime now)
    {
        var todayReset = now.Date.AddHours(DailyResetHourUtc);
        return now < todayReset ? todayReset.AddDays(-1) : todayReset;
    }

    public bool IsFriday()
    {
        return DateTime.UtcNow.DayOfWeek == DayOfWeek.Friday;
    }

    public bool IsSaturdayAfterReset()
    {
        var now = DateTime.UtcNow;
        var saturdayReset = now.Date.AddHours(WeeklyResetHourUtc); // Today at 9 AM UTC
        return now.DayOfWeek == DayOfWeek.Saturday && now >= saturdayReset;
    }

    // --- NEW: Per-Task Reset System Methods ---

    /// <summary>
    /// Check if a task needs to run based on its completion timestamp and next reset time.
    /// </summary>
    public static bool TaskNeedsRun(DateTime lastCompleted, DateTime nextReset)
    {
        return lastCompleted == DateTime.MinValue || nextReset == DateTime.MinValue || DateTime.UtcNow >= nextReset;
    }

    /// <summary>
    /// Check if a task is currently completed (before its next reset).
    /// </summary>
    public static bool TaskIsCompleted(DateTime lastCompleted, DateTime nextReset)
    {
        return lastCompleted != DateTime.MinValue && DateTime.UtcNow < nextReset;
    }

    /// <summary>
    /// Get the next weekly reset time (Tuesday 9:00 AM UTC).
    /// </summary>
    public static DateTime GetNextWeeklyReset(DateTime now)
    {
        return GetNextOccurrence(now, DayOfWeek.Tuesday, WeeklyResetHourUtc);
    }

    public static DateTime GetNextFashionReportAvailability(DateTime now)
    {
        return GetNextOccurrence(now, FashionReportDay, FashionReportStartHourUtc);
    }

    public static DateTime GetCurrentFashionReportWindowEnd(DateTime now)
    {
        var currentWindowStart = GetLastOccurrence(now, FashionReportDay, FashionReportStartHourUtc);
        return currentWindowStart.Date.AddDays(1);
    }

    public static bool IsFashionReportAvailable(DateTime now)
    {
        if (now.DayOfWeek != FashionReportDay)
            return false;

        var windowStart = now.Date.AddHours(FashionReportStartHourUtc);
        var windowEnd = now.Date.AddDays(1);
        return now >= windowStart && now < windowEnd;
    }

    public static bool IsJumboCactpotPayoutAvailable(DateTime now)
    {
        var lastSaturdayReset = GetLastJumboCactpotPayoutAvailability(now);
        var nextWeeklyReset = GetNextWeeklyReset(now);
        return now >= lastSaturdayReset && now < nextWeeklyReset;
    }

    public static bool IsJumboPurchasePendingPayout(DateTime lastCompleted, DateTime nextReset)
    {
        if (!TaskIsCompleted(lastCompleted, nextReset))
            return false;

        var now = DateTime.UtcNow;
        return nextReset > now && nextReset < GetNextWeeklyReset(now);
    }

    public static DateTime GetNextJumboCactpotPayoutAvailability(DateTime now)
    {
        if (!TryGetJumboCactpotPayoutSchedule(out var dayOfWeek, out var hourUtc, out _))
            return GetNextOccurrence(now, DayOfWeek.Saturday, WeeklyResetHourUtc);

        return GetNextOccurrence(now, dayOfWeek, hourUtc);
    }

    public static string GetCurrentCharacterJumboDataCenterName()
    {
        return TryGetJumboCactpotPayoutSchedule(out _, out _, out var dataCenterName)
            ? dataCenterName
            : "Unknown DC";
    }

    /// <summary>
    /// Get the next daily reset time (9:00 AM UTC).
    /// </summary>
    public static DateTime GetNextDailyReset(DateTime now)
    {
        var todayReset = now.Date.AddHours(DailyResetHourUtc);
        return now >= todayReset ? todayReset.AddDays(1) : todayReset;
    }

    /// <summary>
    /// Migrate from legacy boolean flags to the new DateTime system.
    /// Called once during engine startup for backward compatibility.
    /// </summary>
    public void MigrateFromLegacyFlags(CharacterConfig config)
    {
        var now = DateTime.UtcNow;
        var migrated = false;

        // Migrate Verminion (weekly)
        if (config.VerminionCompletedThisWeek && config.VerminionLastCompleted == DateTime.MinValue)
        {
            config.VerminionLastCompleted = GetLastWeeklyReset(now).AddHours(1); // Assume completed after last reset
            config.VerminionNextReset = GetNextWeeklyReset(now);
            migrated = true;
            log.Information("[ResetDetection] Migrated Verminion from legacy flags");
        }
        else if (!config.VerminionCompletedThisWeek && config.VerminionNextReset == DateTime.MinValue)
        {
            config.VerminionNextReset = GetNextWeeklyReset(now);
            migrated = true;
            log.Information("[ResetDetection] Migrated Verminion (incomplete) from legacy flags");
        }

        // Migrate Jumbo Cactpot (weekly)
        if (config.JumboCactpotCompletedThisWeek && config.JumboCactpotLastCompleted == DateTime.MinValue)
        {
            config.JumboCactpotLastCompleted = GetLastWeeklyReset(now).AddHours(1);
            config.JumboCactpotNextReset = GetNextWeeklyReset(now);
            migrated = true;
            log.Information("[ResetDetection] Migrated Jumbo Cactpot from legacy flags");
        }
        else if (!config.JumboCactpotCompletedThisWeek && config.JumboCactpotNextReset == DateTime.MinValue)
        {
            config.JumboCactpotNextReset = GetNextWeeklyReset(now);
            migrated = true;
            log.Information("[ResetDetection] Migrated Jumbo Cactpot (incomplete) from legacy flags");
        }

        // Migrate Fashion Report (weekly)
        if (config.FashionReportCompletedThisWeek && config.FashionReportLastCompleted == DateTime.MinValue)
        {
            config.FashionReportLastCompleted = GetLastWeeklyReset(now).AddHours(1);
            config.FashionReportNextReset = GetNextWeeklyReset(now);
            migrated = true;
            log.Information("[ResetDetection] Migrated Fashion Report from legacy flags");
        }
        else if (!config.FashionReportCompletedThisWeek && config.FashionReportNextReset == DateTime.MinValue)
        {
            config.FashionReportNextReset = GetNextWeeklyReset(now);
            migrated = true;
            log.Information("[ResetDetection] Migrated Fashion Report (incomplete) from legacy flags");
        }

        // Migrate Mini Cactpot (daily)
        if (config.MiniCactpotCompletedToday && config.MiniCactpotLastCompleted == DateTime.MinValue)
        {
            config.MiniCactpotLastCompleted = GetLastDailyReset(now).AddHours(1);
            config.MiniCactpotNextReset = GetNextDailyReset(now);
            migrated = true;
            log.Information("[ResetDetection] Migrated Mini Cactpot from legacy flags");
        }
        else if (!config.MiniCactpotCompletedToday && config.MiniCactpotNextReset == DateTime.MinValue)
        {
            config.MiniCactpotNextReset = GetNextDailyReset(now);
            migrated = true;
            log.Information("[ResetDetection] Migrated Mini Cactpot (incomplete) from legacy flags");
        }

        // Migrate Chocobo Racing (daily)
        if (config.ChocoboRacingCompletedToday && config.ChocoboRacingLastCompleted == DateTime.MinValue)
        {
            config.ChocoboRacingLastCompleted = GetLastDailyReset(now).AddHours(1);
            config.ChocoboRacingNextReset = GetNextDailyReset(now);
            migrated = true;
            log.Information("[ResetDetection] Migrated Chocobo Racing from legacy flags");
        }
        else if (!config.ChocoboRacingCompletedToday && config.ChocoboRacingNextReset == DateTime.MinValue)
        {
            config.ChocoboRacingNextReset = GetNextDailyReset(now);
            migrated = true;
            log.Information("[ResetDetection] Migrated Chocobo Racing (incomplete) from legacy flags");
        }

        if (migrated)
        {
            log.Information("[ResetDetection] Legacy migration completed");
        }
    }

    private static DateTime GetLastJumboCactpotPayoutAvailability(DateTime now)
    {
        if (!TryGetJumboCactpotPayoutSchedule(out var dayOfWeek, out var hourUtc, out _))
            return GetLastOccurrence(now, DayOfWeek.Saturday, WeeklyResetHourUtc);

        return GetLastOccurrence(now, dayOfWeek, hourUtc);
    }

    private static DateTime GetLastOccurrence(DateTime now, DayOfWeek dayOfWeek, int hourUtc)
    {
        var daysSince = ((int)now.DayOfWeek - (int)dayOfWeek + 7) % 7;
        var occurrence = now.Date.AddDays(-daysSince).AddHours(hourUtc);
        return now < occurrence ? occurrence.AddDays(-7) : occurrence;
    }

    private static DateTime GetNextOccurrence(DateTime now, DayOfWeek dayOfWeek, int hourUtc)
    {
        var daysUntil = ((int)dayOfWeek - (int)now.DayOfWeek + 7) % 7;
        var occurrence = now.Date.AddDays(daysUntil).AddHours(hourUtc);
        return now >= occurrence ? occurrence.AddDays(7) : occurrence;
    }

    private static bool TryGetJumboCactpotPayoutSchedule(out DayOfWeek payoutDayUtc, out int payoutHourUtc, out string dataCenterName)
    {
        dataCenterName = ResolveCurrentCharacterDataCenterName();
        switch (dataCenterName)
        {
            case "Elemental":
            case "Gaia":
            case "Mana":
            case "Meteor":
                payoutDayUtc = DayOfWeek.Saturday;
                payoutHourUtc = 12;
                return true;

            case "Aether":
            case "Crystal":
            case "Dynamis":
            case "Primal":
                payoutDayUtc = DayOfWeek.Sunday;
                payoutHourUtc = 2;
                return true;

            case "Chaos":
            case "Light":
                payoutDayUtc = DayOfWeek.Saturday;
                payoutHourUtc = 19;
                return true;

            case "Materia":
                payoutDayUtc = DayOfWeek.Saturday;
                payoutHourUtc = 9;
                return true;

            default:
                payoutDayUtc = DayOfWeek.Saturday;
                payoutHourUtc = WeeklyResetHourUtc;
                return false;
        }
    }

    private static string ResolveCurrentCharacterDataCenterName()
    {
        var player = Plugin.ObjectTable.LocalPlayer;
        if (player == null)
            return string.Empty;

        try
        {
            var homeWorldRow = (object)player.HomeWorld.Value;
            var dataCenterRef = homeWorldRow.GetType().GetProperty("DataCenter")?.GetValue(homeWorldRow);
            var dataCenterRow = dataCenterRef?.GetType().GetProperty("Value")?.GetValue(dataCenterRef);
            var dataCenterName = dataCenterRow?.GetType().GetProperty("Name")?.GetValue(dataCenterRow)?.ToString();
            return dataCenterName?.Trim() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
