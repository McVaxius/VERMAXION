using Dalamud.Plugin.Services;
using Vermaxion.Models;
using System;
using System.Threading.Tasks;

namespace Vermaxion.Services;

public class JumboCactpotService : IDisposable
{
    private readonly IPluginLog _log;
    private readonly IGameGui _gameGui;
    private readonly IChatGui _chatGui;

    public JumboCactpotService(IPluginLog log, IGameGui gameGui, IChatGui chatGui)
    {
        _log = log;
        _gameGui = gameGui;
        _chatGui = chatGui;
    }

    public async Task ExecuteAutomation(CharacterConfig character)
    {
        if (!CanExecute(character))
        {
            _log.Warning($"Jumbo Cactpot automation not available for {character.GetDisplayName()}");
            return;
        }

        _log.Information($"Starting Jumbo Cactpot automation for {character.GetDisplayName()}");

        try
        {
            // TODO: Implement Jumbo Cactpot automation
            // Phase 5 implementation will include:
            // 1. Navigate to Jumbo Cactpot NPC
            // 2. Check if ticket is available
            // 3. Select numbers based on strategy
            // 4. Submit ticket
            // 5. Record results

            // Simulate for now
            await Task.Delay(2000);
            
            character.Statistics.JumboCactpotAttempts++;
            character.Statistics.LastJumboCactpot = DateTime.Now;
            
            _log.Information($"Jumbo Cactpot automation completed for {character.GetDisplayName()}");
        }
        catch (Exception ex)
        {
            _log.Error(ex, $"Error during Jumbo Cactpot automation for {character.GetDisplayName()}");
        }
    }

    public bool CanExecute(CharacterConfig character)
    {
        if (!character.JumboCactpotEnabled) return false;

        // TODO: Check if Jumbo Cactpot is available this week
        // Check weekly reset, ticket availability, etc.
        return true;
    }

    public string GetStatus(CharacterConfig character)
    {
        if (!character.JumboCactpotEnabled)
            return "Disabled";

        if (character.Statistics.LastJumboCactpot.HasValue)
        {
            var lastAttempt = character.Statistics.LastJumboCactpot.Value;
            if (IsThisWeek(lastAttempt))
                return "Completed This Week";
            
            var timeUntilReset = GetTimeUntilWeeklyReset();
            return $"Available in {timeUntilReset:dd\\:hh\\:mm}";
        }

        return "Available";
    }

    private bool IsThisWeek(DateTime dateTime)
    {
        // FFXIV weekly reset is on Tuesday at 15:00 UTC
        var now = DateTime.UtcNow;
        var weekStart = GetWeeklyReset(now);
        
        return dateTime >= weekStart;
    }

    private DateTime GetWeeklyReset(DateTime dateTime)
    {
        // Find the most recent Tuesday at 15:00 UTC
        var daysSinceTuesday = (int)dateTime.DayOfWeek - 2; // Tuesday = 2
        if (daysSinceTuesday < 0) daysSinceTuesday += 7;
        
        var resetTime = dateTime.Date.AddDays(-daysSinceTuesday).AddHours(15);
        
        if (dateTime < resetTime)
        {
            resetTime = resetTime.AddDays(-7);
        }
        
        return resetTime;
    }

    private TimeSpan GetTimeUntilWeeklyReset()
    {
        var now = DateTime.UtcNow;
        var nextReset = GetWeeklyReset(now).AddDays(7);
        
        return nextReset - now;
    }

    public void Dispose()
    {
        // Cleanup resources
    }
}
