using Dalamud.Plugin.Services;
using Dalamud.Game.ClientState;
using Dalamud.Game.ClientState.Objects;
using System;
using System.Collections.Generic;
using System.Linq;
using Vermaxion.Models;

namespace Vermaxion.Services;

public class CharacterManager : IDisposable
{
    private readonly Configuration _config;
    private readonly IClientState _clientState;
    private readonly IPluginLog _log;

    public CharacterManager(Configuration config, IClientState clientState, IPluginLog log)
    {
        _config = config;
        _clientState = clientState;
        _log = log;

        _clientState.Login += OnLogin;
        _clientState.TerritoryChanged += OnTerritoryChanged;
    }

    public event EventHandler<CharacterChangedEventArgs>? CharacterChanged;

    public CharacterConfig? CurrentCharacter => GetCharacterConfig(GetCurrentCharacterId());

    public ulong GetCurrentCharacterId()
    {
        // Use ObjectTable to get the local player
        var localPlayer = _clientState.LocalPlayer;
        if (localPlayer == null) return 0;
        
        // Use the address as a unique identifier since ObjectId isn't available
        return (ulong)localPlayer.Address.ToInt64();
    }

    public string GetCurrentCharacterName()
    {
        return _clientState.LocalPlayer?.Name?.ToString() ?? "";
    }

    public string GetCurrentCharacterWorld()
    {
        return _clientState.LocalPlayer?.HomeWorld.Value.Name.ToString() ?? "";
    }

    public CharacterConfig GetOrCreateCurrentCharacter()
    {
        var characterId = GetCurrentCharacterId();
        var characterName = GetCurrentCharacterName();
        var characterWorld = GetCurrentCharacterWorld();

        if (characterId == 0 || string.IsNullOrEmpty(characterName))
        {
            _log.Warning("Unable to get current character information");
            return null!;
        }

        var characterKey = GetCharacterKey(characterId);
        
        if (!_config.CharacterConfigs.TryGetValue(characterKey, out var characterConfig))
        {
            characterConfig = new CharacterConfig
            {
                CharacterId = characterId,
                CharacterName = characterName,
                CharacterWorld = characterWorld,
                CreatedAt = DateTime.Now,
                LastUpdated = DateTime.Now
            };

            _config.CharacterConfigs[characterKey] = characterConfig;
            _log.Information($"Created new character config for {characterConfig.GetDisplayName()}");
        }
        else
        {
            // Update name/world if they changed
            if (characterConfig.CharacterName != characterName || characterConfig.CharacterWorld != characterWorld)
            {
                characterConfig.CharacterName = characterName;
                characterConfig.CharacterWorld = characterWorld;
                characterConfig.LastUpdated = DateTime.Now;
                _log.Information($"Updated character info for {characterConfig.GetDisplayName()}");
            }
        }

        return characterConfig;
    }

    public CharacterConfig? GetCharacterConfig(ulong characterId)
    {
        var characterKey = GetCharacterKey(characterId);
        return _config.CharacterConfigs.TryGetValue(characterKey, out var config) ? config : null;
    }

    public List<CharacterConfig> GetAllCharacters()
    {
        return _config.CharacterConfigs.Values.ToList();
    }

    public void RemoveCharacter(ulong characterId)
    {
        var characterKey = GetCharacterKey(characterId);
        if (_config.CharacterConfigs.Remove(characterKey))
        {
            _log.Information($"Removed character config for {characterKey}");
            _config.Save();
        }
    }

    public void SelectCharacter(ulong characterId)
    {
        var character = GetCharacterConfig(characterId);
        if (character != null)
        {
            _config.SelectedCharacter = GetCharacterKey(characterId);
            _config.Save();
            _log.Information($"Selected character: {character.GetDisplayName()}");
        }
    }

    public void UpdateCharacter(CharacterConfig character)
    {
        var characterKey = GetCharacterKey(character.CharacterId);
        character.LastUpdated = DateTime.Now;
        _config.CharacterConfigs[characterKey] = character;
        _config.Save();
    }

    private string GetCharacterKey(ulong characterId)
    {
        return characterId.ToString();
    }

    private void OnLogin()
    {
        var currentCharacter = GetOrCreateCurrentCharacter();
        if (currentCharacter != null)
        {
            _config.SelectedCharacter = GetCharacterKey(currentCharacter.CharacterId);
            _config.Save();
            
            CharacterChanged?.Invoke(this, new CharacterChangedEventArgs { Character = currentCharacter });
            _log.Information($"Character logged in: {currentCharacter.GetDisplayName()}");
        }
    }

    private void OnLogout()
    {
        _log.Information("Character logged out");
        CharacterChanged?.Invoke(this, new CharacterChangedEventArgs { Character = null });
    }

    private void OnTerritoryChanged(ushort territoryType)
    {
        var currentCharacter = CurrentCharacter;
        if (currentCharacter != null)
        {
            // Check for ARPostprocess triggers based on zone change
            CheckZoneTriggers(currentCharacter, territoryType);
        }
    }

    private void CheckZoneTriggers(CharacterConfig character, ushort territoryType)
    {
        // TODO: Implement ARPostprocess zone trigger checking
        // This will be implemented in Phase 2
    }

    public void Dispose()
    {
        _clientState.Login -= OnLogin;
        _clientState.TerritoryChanged -= OnTerritoryChanged;
    }
}

public class CharacterChangedEventArgs : EventArgs
{
    public CharacterConfig? Character { get; set; }
}
