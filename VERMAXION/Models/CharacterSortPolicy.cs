using System;
using System.Collections.Generic;
using System.Linq;

namespace VERMAXION.Models;

public enum CharacterListSortMode
{
    Name = 0,
    Server = 1,
    CreationDate = 2,
}

public static class CharacterSortPolicy
{
    public static IEnumerable<string> Sort(
        IEnumerable<string> characterKeys,
        CharacterListSortMode sortMode,
        IReadOnlyDictionary<string, DateTime>? characterCreatedAtUtc = null)
    {
        var keys = characterKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToList();

        return sortMode switch
        {
            CharacterListSortMode.Server => keys
                .OrderBy(GetServerName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(GetCharacterName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(key => key, StringComparer.OrdinalIgnoreCase),

            CharacterListSortMode.CreationDate => keys
                .OrderBy(key => GetCreationDate(key, characterCreatedAtUtc))
                .ThenBy(GetCharacterName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(GetServerName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(key => key, StringComparer.OrdinalIgnoreCase),

            _ => keys
                .OrderBy(GetCharacterName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(GetServerName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(key => key, StringComparer.OrdinalIgnoreCase),
        };
    }

    public static string GetCharacterName(string characterKey)
    {
        var separatorIndex = characterKey.IndexOf('@');
        return (separatorIndex >= 0 ? characterKey[..separatorIndex] : characterKey).Trim();
    }

    public static string GetServerName(string characterKey)
    {
        var separatorIndex = characterKey.IndexOf('@');
        return separatorIndex >= 0 && separatorIndex + 1 < characterKey.Length
            ? characterKey[(separatorIndex + 1)..].Trim()
            : string.Empty;
    }

    private static DateTime GetCreationDate(
        string characterKey,
        IReadOnlyDictionary<string, DateTime>? characterCreatedAtUtc)
    {
        if (characterCreatedAtUtc != null &&
            characterCreatedAtUtc.TryGetValue(characterKey, out var createdAt) &&
            createdAt != DateTime.MinValue)
        {
            return createdAt;
        }

        return DateTime.MaxValue;
    }
}
