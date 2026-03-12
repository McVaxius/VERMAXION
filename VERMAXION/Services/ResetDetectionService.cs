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
            log.Information($"Daily reset detected (manual reset). Setting completion time to: {config.LastDailyReset:u}");
            return true;
        }
        
        // Check for actual daily reset (not manual)
        if (config.LastDailyReset < lastReset)
        {
            config.LastDailyReset = now; // Set to now when we detect reset and clear tasks
            config.MiniCactpotCompletedToday = false;
            config.ChocoboRacingCompletedToday = false;
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
}
