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

        // Check for actual reset
        if (config.LastWeeklyReset < lastReset)
        {
            var wasMinValue = config.LastWeeklyReset == DateTime.MinValue;
            
            // Only update timestamp if it's not a manual reset (DateTime.MinValue)
            if (!wasMinValue)
            {
                config.LastWeeklyReset = lastReset;
                config.VerminionCompletedThisWeek = false;
                config.JumboCactpotCompletedThisWeek = false;
                log.Information($"Weekly reset detected. Last reset: {lastReset:u}");
                return true;
            }
            else
            {
                // Manual reset - don't update timestamp, just clear completion flags
                config.VerminionCompletedThisWeek = false;
                config.JumboCactpotCompletedThisWeek = false;
                log.Information($"Weekly reset detected (manual reset). Last reset: {lastReset:u}");
                return true;
            }
        }
        return false;
    }

    public bool CheckDailyReset(CharacterConfig config)
    {
        var now = DateTime.UtcNow;
        var lastReset = GetLastDailyReset(now);

        // Check for actual reset
        if (config.LastDailyReset < lastReset)
        {
            var wasMinValue = config.LastDailyReset == DateTime.MinValue;
            
            // Only update timestamp if it's not a manual reset (DateTime.MinValue)
            if (!wasMinValue)
            {
                config.LastDailyReset = lastReset;
                config.MiniCactpotCompletedToday = false;
                config.ChocoboRacingCompletedToday = false;
                log.Information($"Daily reset detected. Last reset: {lastReset:u}");
                return true;
            }
            else
            {
                // Manual reset - don't update timestamp, just clear completion flags
                config.MiniCactpotCompletedToday = false;
                config.ChocoboRacingCompletedToday = false;
                log.Information($"Daily reset detected (manual reset). Last reset: {lastReset:u}");
                return true;
            }
        }
        return false;
    }

    public bool IsSaturday()
    {
        return DateTime.UtcNow.DayOfWeek == DayOfWeek.Saturday;
    }

    public static DateTime GetLastWeeklyReset(DateTime now)
    {
        // Weekly Reset: Every Tuesday at 8:00 AM UTC
        var daysSinceTuesday = ((int)now.DayOfWeek - (int)DayOfWeek.Tuesday + 7) % 7;
        var lastTuesday = now.Date.AddDays(-daysSinceTuesday).AddHours(8);

        if (now < lastTuesday)
            lastTuesday = lastTuesday.AddDays(-7);

        return lastTuesday;
    }

    public static DateTime GetLastDailyReset(DateTime now)
    {
        // Daily Reset: Every day at 3:00 PM UTC (15:00 UTC)
        var todayReset = now.Date.AddHours(15);

        if (now < todayReset)
            todayReset = todayReset.AddDays(-1);

        return todayReset;
    }
}
