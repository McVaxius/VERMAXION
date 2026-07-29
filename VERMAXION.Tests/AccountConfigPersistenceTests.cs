using System;
using System.IO;
using System.Text.Json;
using VERMAXION.Models;
using VERMAXION.Services;
using Xunit;

namespace VERMAXION.Tests;

public sealed class AccountConfigPersistenceTests
{
    private const string AccountId = "test-account";
    private static readonly DateTime CreatedAtUtc =
        new(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void StaleClientsEditingDifferentCharactersPreserveBothChanges()
    {
        using var directory = new TemporaryDirectory();
        Seed(directory.Path, CreateAccount("character-a", "character-b"));

        var firstPersistence = new AccountConfigPersistence(directory.Path);
        var secondPersistence = new AccountConfigPersistence(directory.Path);
        var first = LoadSingle(firstPersistence);
        var second = LoadSingle(secondPersistence);

        first.Characters["character-a"].JumboCactpotFixedNumber = 1111;
        second.Characters["character-b"].JumboCactpotFixedNumber = 2222;

        Assert.True(firstPersistence.Save(AccountId, first).Succeeded);
        Assert.True(secondPersistence.Save(AccountId, second).Succeeded);

        var saved = ReadPrimary(directory.Path);
        Assert.Equal(1111, saved.Characters["character-a"].JumboCactpotFixedNumber);
        Assert.Equal(2222, saved.Characters["character-b"].JumboCactpotFixedNumber);
    }

    [Fact]
    public void RemoteCharacterAdditionsAndDeletionsSurviveAnUnrelatedLocalSave()
    {
        using var directory = new TemporaryDirectory();
        Seed(directory.Path, CreateAccount("character-a", "character-b"));

        var localPersistence = new AccountConfigPersistence(directory.Path);
        var remotePersistence = new AccountConfigPersistence(directory.Path);
        var local = LoadSingle(localPersistence);
        var remote = LoadSingle(remotePersistence);

        remote.Characters.Remove("character-b");
        remote.CharacterCreatedAtUtc.Remove("character-b");
        remote.Characters["character-c"] = CreateCharacter(3000);
        remote.CharacterCreatedAtUtc["character-c"] = CreatedAtUtc.AddMinutes(3);
        Assert.True(remotePersistence.Save(AccountId, remote).Succeeded);

        local.AccountAlias = "Locally renamed";
        Assert.True(localPersistence.Save(AccountId, local).Succeeded);

        var saved = ReadPrimary(directory.Path);
        Assert.Equal("Locally renamed", saved.AccountAlias);
        Assert.False(saved.Characters.ContainsKey("character-b"));
        Assert.False(saved.CharacterCreatedAtUtc.ContainsKey("character-b"));
        Assert.True(saved.Characters.ContainsKey("character-c"));
        Assert.Equal(CreatedAtUtc.AddMinutes(3), saved.CharacterCreatedAtUtc["character-c"]);
    }

    [Fact]
    public void IntentionalBulkChangesWinWithoutDroppingRemoteAdditions()
    {
        using var directory = new TemporaryDirectory();
        Seed(directory.Path, CreateAccount("character-a", "character-b", "character-c"));

        var bulkPersistence = new AccountConfigPersistence(directory.Path);
        var remotePersistence = new AccountConfigPersistence(directory.Path);
        var bulk = LoadSingle(bulkPersistence);
        var remote = LoadSingle(remotePersistence);

        remote.Characters["character-d"] = CreateCharacter(4000);
        remote.CharacterCreatedAtUtc["character-d"] = CreatedAtUtc.AddMinutes(4);
        Assert.True(remotePersistence.Save(AccountId, remote).Succeeded);

        bulk.DefaultConfig.EnableJumboCactpot = true;
        foreach (var character in bulk.Characters.Values)
            character.EnableJumboCactpot = true;
        Assert.True(bulkPersistence.Save(AccountId, bulk).Succeeded);

        var saved = ReadPrimary(directory.Path);
        Assert.True(saved.DefaultConfig.EnableJumboCactpot);
        Assert.All(
            new[] { "character-a", "character-b", "character-c" },
            key => Assert.True(saved.Characters[key].EnableJumboCactpot));
        Assert.True(saved.Characters.ContainsKey("character-d"));
    }

    [Fact]
    public void SameCharacterConflictUsesCurrentSaversValue()
    {
        using var directory = new TemporaryDirectory();
        Seed(directory.Path, CreateAccount("character-a"));

        var firstPersistence = new AccountConfigPersistence(directory.Path);
        var currentPersistence = new AccountConfigPersistence(directory.Path);
        var first = LoadSingle(firstPersistence);
        var current = LoadSingle(currentPersistence);

        first.Characters["character-a"].JumboCactpotFixedNumber = 1111;
        current.Characters["character-a"].JumboCactpotFixedNumber = 9999;
        Assert.True(firstPersistence.Save(AccountId, first).Succeeded);
        Assert.True(currentPersistence.Save(AccountId, current).Succeeded);

        Assert.Equal(
            9999,
            ReadPrimary(directory.Path).Characters["character-a"].JumboCactpotFixedNumber);
    }

    [Fact]
    public void MalformedPrimaryLoadsAndRecoversFromLastKnownGoodBackup()
    {
        using var directory = new TemporaryDirectory();
        var persistence = new AccountConfigPersistence(directory.Path);
        var account = CreateAccount("character-a");
        Assert.True(persistence.Save(AccountId, account).Succeeded);

        account.AccountAlias = "Second version";
        Assert.True(persistence.Save(AccountId, account).Succeeded);
        File.WriteAllText(persistence.GetPrimaryPath(AccountId), "{ malformed primary");

        var recoveryPersistence = new AccountConfigPersistence(directory.Path);
        var load = Assert.Single(recoveryPersistence.LoadAll());
        Assert.True(load.Succeeded);
        Assert.True(load.UsedBackup);
        Assert.Equal("Initial", load.Account!.AccountAlias);

        load.Account.AccountAlias = "Recovered";
        Assert.True(recoveryPersistence.Save(AccountId, load.Account).Succeeded);
        Assert.Equal("Recovered", ReadPrimary(directory.Path).AccountAlias);
        Assert.Equal(
            "Initial",
            ReadAccount(recoveryPersistence.GetBackupPath(AccountId)).AccountAlias);
        Assert.False(File.Exists(recoveryPersistence.GetTemporaryPath(AccountId)));
    }

    [Fact]
    public void MalformedPrimaryAndBackupRefuseLoadAndSaveWithoutOverwriting()
    {
        using var directory = new TemporaryDirectory();
        var persistence = new AccountConfigPersistence(directory.Path);
        var primaryPath = persistence.GetPrimaryPath(AccountId);
        var backupPath = persistence.GetBackupPath(AccountId);
        File.WriteAllText(primaryPath, "{ malformed primary");
        File.WriteAllText(backupPath, "{ malformed backup");
        var primaryBefore = File.ReadAllText(primaryPath);
        var backupBefore = File.ReadAllText(backupPath);

        var load = Assert.Single(persistence.LoadAll());
        Assert.False(load.Succeeded);

        var save = persistence.Save(AccountId, CreateAccount("character-a"));
        Assert.False(save.Succeeded);
        Assert.Equal(primaryBefore, File.ReadAllText(primaryPath));
        Assert.Equal(backupBefore, File.ReadAllText(backupPath));
        Assert.False(File.Exists(persistence.GetTemporaryPath(AccountId)));
    }

    [Fact]
    public void ValidSaveAtomicallyReplacesPrimaryAndRetainsOneBackup()
    {
        using var directory = new TemporaryDirectory();
        var persistence = new AccountConfigPersistence(directory.Path);
        var account = CreateAccount("character-a");
        Assert.True(persistence.Save(AccountId, account).Succeeded);

        account.AccountAlias = "Second version";
        Assert.True(persistence.Save(AccountId, account).Succeeded);

        Assert.Equal("Second version", ReadPrimary(directory.Path).AccountAlias);
        Assert.Equal("Initial", ReadAccount(persistence.GetBackupPath(AccountId)).AccountAlias);
        Assert.False(File.Exists(persistence.GetTemporaryPath(AccountId)));
        Assert.Single(Directory.GetFiles(directory.Path, "*_Vermaxion.json.bak"));
        Assert.Equal(
            "Second version",
            LoadSingle(new AccountConfigPersistence(directory.Path)).AccountAlias);
    }

    private static void Seed(string directory, AccountConfig account)
    {
        var save = new AccountConfigPersistence(directory).Save(AccountId, account);
        Assert.True(save.Succeeded, save.Error);
    }

    private static AccountConfig LoadSingle(AccountConfigPersistence persistence)
    {
        var result = Assert.Single(persistence.LoadAll());
        Assert.True(result.Succeeded, result.Error);
        Assert.NotNull(result.Account);
        return result.Account!;
    }

    private static AccountConfig ReadPrimary(string directory)
        => ReadAccount(new AccountConfigPersistence(directory).GetPrimaryPath(AccountId));

    private static AccountConfig ReadAccount(string path)
        => JsonSerializer.Deserialize<AccountConfig>(File.ReadAllText(path))
           ?? throw new InvalidDataException($"Could not deserialize test account file {path}.");

    private static AccountConfig CreateAccount(params string[] characterKeys)
    {
        var account = new AccountConfig
        {
            AccountId = AccountId,
            AccountAlias = "Initial",
        };

        for (var index = 0; index < characterKeys.Length; index++)
        {
            account.Characters[characterKeys[index]] = CreateCharacter(1000 + index);
            account.CharacterCreatedAtUtc[characterKeys[index]] = CreatedAtUtc.AddMinutes(index);
        }

        return account;
    }

    private static CharacterConfig CreateCharacter(int jumboNumber)
    {
        var config = CharacterConfig.CreateNew();
        config.JumboCactpotFixedNumber = jumboNumber;
        return config;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "VermaxionAccountConfigPersistenceTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
