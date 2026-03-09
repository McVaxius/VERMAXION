using Dalamud.Plugin.Services;
using Vermaxion.Models;
using System;
using System.Threading.Tasks;

namespace Vermaxion.Services;

public class GoldSaucerService : IDisposable
{
    private readonly Configuration _config;
    private readonly CharacterManager _characterManager;
    private readonly IPluginLog _log;
    private readonly MiniCactpotService _miniCactpotService;
    private readonly ChocoboRaceService _chocoboRaceService;
    private readonly JumboCactpotService _jumboCactpotService;
    // private readonly VarminionService _varminionService;

    public GoldSaucerService(Configuration config, CharacterManager characterManager, IPluginLog log, 
        MiniCactpotService miniCactpotService, ChocoboRaceService chocoboRaceService,
        JumboCactpotService jumboCactpotService/*, VarminionService varminionService*/)
    {
        _config = config;
        _characterManager = characterManager;
        _log = log;
        _miniCactpotService = miniCactpotService;
        _chocoboRaceService = chocoboRaceService;
        _jumboCactpotService = jumboCactpotService;
        // _varminionService = varminionService;

        _characterManager.CharacterChanged += OnCharacterChanged;
    }

    public async Task ExecuteAutomation(ActionType actionType, CharacterConfig character)
    {
        _log.Information($"Executing {actionType} for {character.GetDisplayName()}");

        try
        {
            switch (actionType)
            {
                case ActionType.MiniCactpot:
                    await _miniCactpotService.ExecuteAutomation(character);
                    break;
                case ActionType.ChocoboRaces:
                    await _chocoboRaceService.ExecuteAutomation(character);
                    break;
                case ActionType.JumboCactpot:
                    await _jumboCactpotService.ExecuteAutomation(character);
                    break;
                case ActionType.Varminion:
                    _log.Information("Varminion automation not yet implemented");
                    return;
                default:
                    _log.Warning($"Unknown action type: {actionType}");
                    break;
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, $"Error executing {actionType} for {character.GetDisplayName()}");
        }
    }

    public bool CanExecuteAutomation(ActionType actionType, CharacterConfig character)
    {
        return actionType switch
        {
            ActionType.MiniCactpot => _miniCactpotService.CanExecute(character),
            ActionType.ChocoboRaces => _chocoboRaceService.CanExecute(character),
            ActionType.JumboCactpot => _jumboCactpotService.CanExecute(character),
            ActionType.Varminion => false, // Not yet implemented
            _ => false
        };
    }

    public string GetAutomationStatus(ActionType actionType, CharacterConfig character)
    {
        return actionType switch
        {
            ActionType.MiniCactpot => _miniCactpotService.GetStatus(character),
            ActionType.ChocoboRaces => _chocoboRaceService.GetStatus(character),
            ActionType.JumboCactpot => _jumboCactpotService.GetStatus(character),
            ActionType.Varminion => "Not yet implemented",
            _ => "Unknown"
        };
    }

    private void OnCharacterChanged(object? sender, CharacterChangedEventArgs e)
    {
        if (e.Character != null)
        {
            _log.Information($"Character changed to: {e.Character.GetDisplayName()}");
            // TODO: Check for login triggers and execute automations
        }
    }

    public void Dispose()
    {
        _characterManager.CharacterChanged -= OnCharacterChanged;
    }
}
