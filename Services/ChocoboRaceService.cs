using Dalamud.Plugin.Services;
using Vermaxion.Models;
using System;
using System.Threading.Tasks;

namespace Vermaxion.Services;

public class ChocoboRaceService : IDisposable
{
    private readonly IPluginLog _log;
    private readonly IGameGui _gameGui;
    private readonly IChatGui _chatGui;

    public ChocoboRaceService(IPluginLog log, IGameGui gameGui, IChatGui chatGui)
    {
        _log = log;
        _gameGui = gameGui;
        _chatGui = chatGui;
    }

    public async Task ExecuteAutomation(CharacterConfig character)
    {
        if (!CanExecute(character))
        {
            _log.Warning($"Chocobo Race automation not available for {character.GetDisplayName()}");
            return;
        }

        _log.Information($"Starting Chocobo Race automation for {character.GetDisplayName()} ({character.ChocoboRacesCount} races)");

        try
        {
            // TODO: Implement Chocobo Race automation
            // Phase 4 implementation will include:
            // 1. Navigate to Chocobo Square
            // 2. Interact with race betting NPC
            // 3. Place bets based on strategy
            // 4. Watch races and collect winnings
            // 5. Repeat for configured number of races

            for (int i = 0; i < character.ChocoboRacesCount; i++)
            {
                _log.Information($"Executing race {i + 1}/{character.ChocoboRacesCount} for {character.GetDisplayName()}");
                
                // Simulate race execution
                await Task.Delay(3000);
                
                character.Statistics.ChocoboRaceAttempts++;
                // TODO: Update with actual race results
            }

            character.Statistics.LastChocoboRace = DateTime.Now;
            
            _log.Information($"Chocobo Race automation completed for {character.GetDisplayName()}");
        }
        catch (Exception ex)
        {
            _log.Error(ex, $"Error during Chocobo Race automation for {character.GetDisplayName()}");
        }
    }

    public bool CanExecute(CharacterConfig character)
    {
        if (!character.ChocoboRacesEnabled) return false;

        // TODO: Check if Chocobo Races are available today
        // Check daily reset, available races, etc.
        return true;
    }

    public string GetStatus(CharacterConfig character)
    {
        if (!character.ChocoboRacesEnabled)
            return "Disabled";

        if (character.Statistics.LastChocoboRace.HasValue)
        {
            var lastAttempt = character.Statistics.LastChocoboRace.Value;
            if (IsToday(lastAttempt))
            {
                var completedRaces = character.Statistics.ChocoboRaceAttempts;
                var targetRaces = character.ChocoboRacesCount;
                
                if (completedRaces >= targetRaces)
                    return "Completed Today";
                else
                    return $"{completedRaces}/{targetRaces} Races";
            }
            
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
