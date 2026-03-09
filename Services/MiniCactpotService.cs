using Dalamud.Plugin.Services;
using Vermaxion.Models;
using System;
using System.Threading.Tasks;

namespace Vermaxion.Services;

public class MiniCactpotService : IDisposable
{
    private readonly IPluginLog _log;
    private readonly IGameGui _gameGui;
    private readonly IChatGui _chatGui;

    public MiniCactpotService(IPluginLog log, IGameGui gameGui, IChatGui chatGui)
    {
        _log = log;
        _gameGui = gameGui;
        _chatGui = chatGui;
    }

    public async Task ExecuteAutomation(CharacterConfig character)
    {
        if (!CanExecute(character))
        {
            _log.Warning($"Mini Cactpot automation not available for {character.GetDisplayName()}");
            return;
        }

        _log.Information($"Starting Mini Cactpot automation for {character.GetDisplayName()}");

        try
        {
            // TODO: Implement Mini Cactpot automation
            // Phase 3 implementation will include:
            // 1. Navigate to Mini Cactpot NPC
            // 2. Interact with Mini Cactpot interface
            // 3. Select numbers based on strategy
            // 4. Submit ticket
            // 5. Record results

            // Simulate for now
            await Task.Delay(2000);
            
            character.Statistics.MiniCactpotAttempts++;
            character.Statistics.LastMiniCactpot = DateTime.Now;
            
            _log.Information($"Mini Cactpot automation completed for {character.GetDisplayName()}");
        }
        catch (Exception ex)
        {
            _log.Error(ex, $"Error during Mini Cactpot automation for {character.GetDisplayName()}");
        }
    }

    public bool CanExecute(CharacterConfig character)
    {
        if (!character.MiniCactpotEnabled) return false;

        // TODO: Check if Mini Cactpot is available today
        // Check daily reset, ticket availability, etc.
        return true;
    }

    public string GetStatus(CharacterConfig character)
    {
        if (!character.MiniCactpotEnabled)
            return "Disabled";

        if (character.Statistics.LastMiniCactpot.HasValue)
        {
            var lastAttempt = character.Statistics.LastMiniCactpot.Value;
            if (IsToday(lastAttempt))
                return "Completed Today";
            
            var timeUntilReset = GetTimeUntilDailyReset();
            return $"Available in {timeUntilReset:hh\\:mm\\:ss}";
        }

        return "Available";
    }

    private bool IsToday(DateTime dateTime)
    {
        return dateTime.Date == DateTime.UtcNow.Date;
    }

    private TimeSpan GetTimeUntilDailyReset()
    {
        // FFXIV daily reset is at 15:00 UTC
        var resetTime = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, DateTime.UtcNow.Day, 15, 0, 0, DateTimeKind.Utc);
        
        if (DateTime.UtcNow > resetTime)
        {
            resetTime = resetTime.AddDays(1);
        }

        return resetTime - DateTime.UtcNow;
    }

    public void Dispose()
    {
        // Cleanup resources
    }
}
