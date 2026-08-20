using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace VERMAXION.Models;

public sealed record RegistrableImportPreview(
    bool IsValid,
    string Error,
    IReadOnlyList<uint> AcceptedIds,
    int AcceptedCount,
    int DuplicateCount,
    int UnknownCount,
    int InvalidCount,
    int AddedCount,
    int RemovedCount);

public static class RegistrableEditorPolicy
{
    public static IReadOnlyList<uint> AddIfMissing(IEnumerable<uint>? configuredIds, uint itemId)
    {
        var result = Normalize(configuredIds).ToList();
        if (itemId != 0 && !result.Contains(itemId))
            result.Add(itemId);
        return result;
    }

    public static IReadOnlyList<uint> Normalize(IEnumerable<uint>? configuredIds)
    {
        var result = new List<uint>();
        var seen = new HashSet<uint>();
        foreach (var itemId in configuredIds ?? [])
        {
            if (itemId != 0 && seen.Add(itemId))
                result.Add(itemId);
        }
        return result;
    }

    public static IReadOnlyList<uint> SearchConfigured(
        IEnumerable<uint>? configuredIds,
        string query,
        IReadOnlyDictionary<uint, string> names)
    {
        var normalized = Normalize(configuredIds);
        if (string.IsNullOrWhiteSpace(query))
            return normalized;

        var term = query.Trim();
        return normalized.Where(itemId =>
            itemId.ToString().Contains(term, StringComparison.OrdinalIgnoreCase) ||
            names.TryGetValue(itemId, out var name) &&
            name.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public static RegistrableImportPreview ParseImport(
        string clipboardText,
        IReadOnlySet<uint> knownIds,
        IEnumerable<uint>? currentIds)
    {
        if (string.IsNullOrWhiteSpace(clipboardText))
            return Invalid("Clipboard is empty.");

        try
        {
            using var document = JsonDocument.Parse(clipboardText);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return Invalid("Clipboard content must be a JSON array of item IDs.");

            var accepted = new List<uint>();
            var seen = new HashSet<uint>();
            var duplicateCount = 0;
            var unknownCount = 0;
            var invalidCount = 0;

            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Number ||
                    !element.TryGetUInt32(out var itemId) ||
                    itemId == 0)
                {
                    invalidCount++;
                    continue;
                }

                if (!seen.Add(itemId))
                {
                    duplicateCount++;
                    continue;
                }

                if (!knownIds.Contains(itemId))
                {
                    unknownCount++;
                    continue;
                }

                accepted.Add(itemId);
            }

            var current = Normalize(currentIds).ToHashSet();
            var acceptedSet = accepted.ToHashSet();
            return new RegistrableImportPreview(
                true,
                string.Empty,
                accepted,
                accepted.Count,
                duplicateCount,
                unknownCount,
                invalidCount,
                acceptedSet.Count(id => !current.Contains(id)),
                current.Count(id => !acceptedSet.Contains(id)));
        }
        catch (JsonException ex)
        {
            return Invalid($"Invalid JSON: {ex.Message}");
        }
    }

    public static IReadOnlyList<uint> ApplyImport(
        IEnumerable<uint>? currentIds,
        RegistrableImportPreview preview,
        bool confirmed)
        => confirmed && preview.IsValid
            ? preview.AcceptedIds.ToList()
            : Normalize(currentIds);

    private static RegistrableImportPreview Invalid(string error)
        => new(false, error, [], 0, 0, 0, 0, 0, 0);
}
