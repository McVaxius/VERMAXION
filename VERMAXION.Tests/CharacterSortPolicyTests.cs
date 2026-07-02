#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using VERMAXION.Models;
using Xunit;

namespace VERMAXION.Tests;

public sealed class CharacterSortPolicyTests
{
    [Fact]
    public void NameSortOrdersByCharacterNameThenServer()
    {
        var keys = new[]
        {
            "Beta@Adamantoise",
            "Alpha@Zalera",
            "Alpha@Brynhildr",
        };

        var sorted = CharacterSortPolicy.Sort(keys, CharacterListSortMode.Name).ToArray();

        Assert.Equal(
            [
                "Alpha@Brynhildr",
                "Alpha@Zalera",
                "Beta@Adamantoise",
            ],
            sorted);
    }

    [Fact]
    public void ServerSortOrdersByServerThenCharacterName()
    {
        var keys = new[]
        {
            "Beta@Brynhildr",
            "Alpha@Zalera",
            "Alpha@Brynhildr",
        };

        var sorted = CharacterSortPolicy.Sort(keys, CharacterListSortMode.Server).ToArray();

        Assert.Equal(
            [
                "Alpha@Brynhildr",
                "Beta@Brynhildr",
                "Alpha@Zalera",
            ],
            sorted);
    }

    [Fact]
    public void CreationDateSortHonorsMetadataAndTieBreaksByName()
    {
        var keys = new[]
        {
            "Beta@World",
            "Alpha@World",
            "Gamma@World",
        };
        var createdAt = new Dictionary<string, DateTime>
        {
            ["Beta@World"] = new DateTime(2026, 7, 1, 0, 0, 2, DateTimeKind.Utc),
            ["Alpha@World"] = new DateTime(2026, 7, 1, 0, 0, 1, DateTimeKind.Utc),
            ["Gamma@World"] = new DateTime(2026, 7, 1, 0, 0, 1, DateTimeKind.Utc),
        };

        var sorted = CharacterSortPolicy.Sort(keys, CharacterListSortMode.CreationDate, createdAt).ToArray();

        Assert.Equal(
            [
                "Alpha@World",
                "Gamma@World",
                "Beta@World",
            ],
            sorted);
    }

    [Fact]
    public void CreationDateSortPlacesMissingMetadataDeterministically()
    {
        var keys = new[]
        {
            "Missing Beta@World",
            "Known@World",
            "Missing Alpha@World",
        };
        var createdAt = new Dictionary<string, DateTime>
        {
            ["Known@World"] = new DateTime(2026, 7, 1, 0, 0, 1, DateTimeKind.Utc),
        };

        var sorted = CharacterSortPolicy.Sort(keys, CharacterListSortMode.CreationDate, createdAt).ToArray();

        Assert.Equal(
            [
                "Known@World",
                "Missing Alpha@World",
                "Missing Beta@World",
            ],
            sorted);
    }
}
