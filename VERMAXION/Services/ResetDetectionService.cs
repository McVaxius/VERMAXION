using System;
using Dalamud.Plugin.Services;
using VERMAXION.Models;

namespace VERMAXION.Services;

public class ResetDetectionService
{
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
        // Weekly Reset: Every Tuesday at 9:00 AM UTC (actual FFXIV 8:00 AM UTC + 1 hour DST)
        var daysSinceTuesday = ((int)now.DayOfWeek - (int)DayOfWeek.Tuesday + 7) % 7;
        var lastTuesday = now.Date.AddDays(-daysSinceTuesday).AddHours(9);

        if (now < lastTuesday)
            lastTuesday = lastTuesday.AddDays(-7);

        return lastTuesday;
    }

    public static DateTime GetLastDailyReset(DateTime now)
    {
        // Daily Reset: Every day at 9:00 AM UTC (actual FFXIV 8:00 AM UTC + 1 hour DST)
        var todayReset = now.Date.AddHours(9);

        if (now < todayReset)
            todayReset = todayReset.AddDays(-1);

        return todayReset;
    }

    public static DateTime GetLastFridayReset(DateTime now)
    {
        // Friday Reset: Every Friday at 9:00 AM UTC (actual FFXIV 8:00 AM UTC + 1 hour DST) - Fashion Report availability
        var daysSinceFriday = ((int)now.DayOfWeek - (int)DayOfWeek.Friday + 7) % 7;
        var lastFriday = now.Date.AddDays(-daysSinceFriday).AddHours(9);

        if (now < lastFriday)
            lastFriday = lastFriday.AddDays(-7);

        return lastFriday;
    }

    public static DateTime GetLastSaturdayReset(DateTime now)
    {
        // Saturday Reset: Every Saturday at 9:00 AM UTC (actual FFXIV 8:00 AM UTC + 1 hour DST) - Jumbo Cactpot availability
        var daysSinceSaturday = ((int)now.DayOfWeek - (int)DayOfWeek.Saturday + 7) % 7;
        var lastSaturday = now.Date.AddDays(-daysSinceSaturday).AddHours(9);

        if (now < lastSaturday)
            lastSaturday = lastSaturday.AddDays(-7);

        return lastSaturday;
    }

    public bool IsFriday()
    {
        return DateTime.UtcNow.DayOfWeek == DayOfWeek.Friday;
    }

    public bool IsSaturdayAfterReset()
    {
        var now = DateTime.UtcNow;
        var saturdayReset = now.Date.AddHours(9); // Today at 9 AM UTC
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
        var daysUntilTuesday = ((int)DayOfWeek.Tuesday - (int)now.DayOfWeek + 7) % 7;
        var nextTuesday = now.Date.AddDays(daysUntilTuesday).AddHours(9);

        if (now >= nextTuesday)
            nextTuesday = nextTuesday.AddDays(7);

        return nextTuesday;
    }

    public static DateTime GetNextFridayAvailability(DateTime now)
    {
        var daysUntilFriday = ((int)DayOfWeek.Friday - (int)now.DayOfWeek + 7) % 7;
        var nextFriday = now.Date.AddDays(daysUntilFriday).AddHours(9);

        if (now >= nextFriday)
            nextFriday = nextFriday.AddDays(7);

        return nextFriday;
    }

    public static DateTime GetNextSaturdayAvailability(DateTime now)
    {
        var daysUntilSaturday = ((int)DayOfWeek.Saturday - (int)now.DayOfWeek + 7) % 7;
        var nextSaturday = now.Date.AddDays(daysUntilSaturday).AddHours(9);

        if (now >= nextSaturday)
            nextSaturday = nextSaturday.AddDays(7);

        return nextSaturday;
    }

    public static bool IsFashionReportAvailable(DateTime now)
    {
        var lastFridayReset = GetLastFridayReset(now);
        var nextWeeklyReset = GetNextWeeklyReset(now);
        return now >= lastFridayReset && now < nextWeeklyReset;
    }

    public static bool IsJumboCactpotPayoutAvailable(DateTime now)
    {
        var lastSaturdayReset = GetLastSaturdayReset(now);
        var nextWeeklyReset = GetNextWeeklyReset(now);
        return now >= lastSaturdayReset && now < nextWeeklyReset;
    }

    /// <summary>
    /// Get the next daily reset time (9:00 AM UTC).
    /// </summary>
    public static DateTime GetNextDailyReset(DateTime now)
    {
        var todayReset = now.Date.AddHours(9);

        if (now >= todayReset)
            todayReset = todayReset.AddDays(1);

        return todayReset;
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
}
