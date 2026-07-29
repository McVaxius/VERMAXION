using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using VERMAXION.Models;

namespace VERMAXION.Services;

internal sealed record AccountConfigLoadResult(
    string AccountId,
    bool Succeeded,
    AccountConfig? Account,
    bool UsedBackup,
    string Error);

internal sealed record AccountConfigSaveResult(
    bool Succeeded,
    AccountConfig? Account,
    bool UsedBackup,
    string Error);

internal sealed class AccountConfigPersistence
{
    private const string PrimarySuffix = "_Vermaxion.json";
    private const string BackupSuffix = ".bak";
    private const string TemporarySuffix = ".tmp";
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string configDirectory;
    private readonly Dictionary<string, AccountConfig> baselines =
        new(StringComparer.OrdinalIgnoreCase);

    public AccountConfigPersistence(string configDirectory)
    {
        this.configDirectory = configDirectory;
    }

    public IReadOnlyList<AccountConfigLoadResult> LoadAll()
    {
        var accountIds = EnumerateAccountIds();
        var results = new List<AccountConfigLoadResult>(accountIds.Count);

        foreach (var accountId in accountIds)
        {
            using var mutex = new Mutex(false, GetMutexName(accountId));
            var lockTaken = false;
            try
            {
                lockTaken = WaitForLock(mutex);
                if (!lockTaken)
                {
                    results.Add(new AccountConfigLoadResult(
                        accountId,
                        false,
                        null,
                        false,
                        "Timed out waiting for the account configuration lock."));
                    continue;
                }

                var disk = ReadNewestValidCopy(accountId);
                if (!disk.Succeeded || disk.Account == null)
                {
                    results.Add(new AccountConfigLoadResult(
                        accountId,
                        false,
                        null,
                        false,
                        disk.Error));
                    continue;
                }

                baselines[accountId] = Clone(disk.Account);
                results.Add(new AccountConfigLoadResult(
                    accountId,
                    true,
                    Clone(disk.Account),
                    disk.UsedBackup,
                    string.Empty));
            }
            catch (Exception ex)
            {
                results.Add(new AccountConfigLoadResult(
                    accountId,
                    false,
                    null,
                    false,
                    ex.Message));
            }
            finally
            {
                if (lockTaken)
                    mutex.ReleaseMutex();
            }
        }

        return results;
    }

    public AccountConfigSaveResult Save(string accountId, AccountConfig localAccount)
    {
        if (!IsValidAccount(localAccount, accountId, out var validationError))
            return new AccountConfigSaveResult(false, null, false, validationError);

        using var mutex = new Mutex(false, GetMutexName(accountId));
        var lockTaken = false;
        try
        {
            lockTaken = WaitForLock(mutex);
            if (!lockTaken)
            {
                return new AccountConfigSaveResult(
                    false,
                    null,
                    false,
                    "Timed out waiting for the account configuration lock.");
            }

            var disk = ReadNewestValidCopy(accountId);
            if (disk.HasAnyCopy && (!disk.Succeeded || disk.Account == null))
                return new AccountConfigSaveResult(false, null, false, disk.Error);

            var baseline = baselines.TryGetValue(accountId, out var loadedBaseline)
                ? loadedBaseline
                : CreateEmptyAccount(accountId);
            var newestDisk = disk.Account ?? CreateEmptyAccount(accountId);
            var merged = Merge(baseline, localAccount, newestDisk);

            if (!IsValidAccount(merged, accountId, out validationError))
                return new AccountConfigSaveResult(false, null, disk.UsedBackup, validationError);

            WriteAtomically(accountId, merged, disk.PrimaryValid);
            baselines[accountId] = Clone(merged);
            return new AccountConfigSaveResult(true, merged, disk.UsedBackup, string.Empty);
        }
        catch (Exception ex)
        {
            return new AccountConfigSaveResult(false, null, false, ex.Message);
        }
        finally
        {
            if (lockTaken)
                mutex.ReleaseMutex();
        }
    }

    internal string GetPrimaryPath(string accountId)
        => Path.Combine(configDirectory, $"{accountId}{PrimarySuffix}");

    internal string GetBackupPath(string accountId)
        => GetPrimaryPath(accountId) + BackupSuffix;

    internal string GetTemporaryPath(string accountId)
        => GetPrimaryPath(accountId) + TemporarySuffix;

    internal static bool AreEquivalent<T>(T left, T right)
        => JsonSerializer.SerializeToUtf8Bytes(left, JsonOptions)
            .AsSpan()
            .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(right, JsonOptions));

    private List<string> EnumerateAccountIds()
    {
        var accountIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(configDirectory, $"*{PrimarySuffix}"))
            AddAccountId(path, PrimarySuffix, accountIds);
        foreach (var path in Directory.EnumerateFiles(configDirectory, $"*{PrimarySuffix}{BackupSuffix}"))
            AddAccountId(path, $"{PrimarySuffix}{BackupSuffix}", accountIds);

        return accountIds
            .OrderBy(accountId => accountId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AddAccountId(
        string path,
        string suffix,
        ISet<string> accountIds)
    {
        var fileName = Path.GetFileName(path);
        if (fileName.Length <= suffix.Length ||
            !fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        accountIds.Add(fileName[..^suffix.Length]);
    }

    private DiskReadResult ReadNewestValidCopy(string accountId)
    {
        var primary = ReadCopy(GetPrimaryPath(accountId), accountId, isBackup: false);
        var backup = ReadCopy(GetBackupPath(accountId), accountId, isBackup: true);
        var validCopies = new[] { primary, backup }
            .Where(copy => copy.IsValid)
            .OrderByDescending(copy => copy.LastWriteTimeUtc)
            .ThenBy(copy => copy.IsBackup)
            .ToList();

        if (validCopies.Count > 0)
        {
            var selected = validCopies[0];
            return new DiskReadResult(
                primary.Exists || backup.Exists,
                true,
                Clone(selected.Account!),
                selected.IsBackup,
                primary.IsValid,
                string.Empty);
        }

        var hasAnyCopy = primary.Exists || backup.Exists;
        var error = hasAnyCopy
            ? $"Neither the primary nor backup account configuration is readable. Primary: {primary.Error} Backup: {backup.Error}"
            : string.Empty;
        return new DiskReadResult(
            hasAnyCopy,
            false,
            null,
            false,
            false,
            error);
    }

    private static CopyReadResult ReadCopy(string path, string accountId, bool isBackup)
    {
        if (!File.Exists(path))
            return new CopyReadResult(false, false, null, isBackup, DateTime.MinValue, "missing");

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var account = JsonSerializer.Deserialize<AccountConfig>(stream, JsonOptions);
            if (!IsValidAccount(account, accountId, out var error))
            {
                return new CopyReadResult(
                    true,
                    false,
                    null,
                    isBackup,
                    File.GetLastWriteTimeUtc(path),
                    error);
            }

            return new CopyReadResult(
                true,
                true,
                account,
                isBackup,
                File.GetLastWriteTimeUtc(path),
                string.Empty);
        }
        catch (Exception ex)
        {
            return new CopyReadResult(
                true,
                false,
                null,
                isBackup,
                DateTime.MinValue,
                ex.Message);
        }
    }

    private void WriteAtomically(string accountId, AccountConfig account, bool primaryValid)
    {
        var primaryPath = GetPrimaryPath(accountId);
        var backupPath = GetBackupPath(accountId);
        var temporaryPath = GetTemporaryPath(accountId);

        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None))
            {
                JsonSerializer.Serialize(stream, account, JsonOptions);
                stream.Flush(flushToDisk: true);
            }

            var validation = ReadCopy(temporaryPath, accountId, isBackup: false);
            if (!validation.IsValid)
                throw new InvalidDataException($"Temporary account configuration validation failed: {validation.Error}");

            if (!File.Exists(primaryPath))
            {
                File.Move(temporaryPath, primaryPath);
            }
            else if (primaryValid)
            {
                File.Replace(temporaryPath, primaryPath, backupPath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, primaryPath, overwrite: true);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static AccountConfig Merge(
        AccountConfig baseline,
        AccountConfig local,
        AccountConfig newestDisk)
    {
        var merged = Clone(newestDisk);
        merged.AccountId = local.AccountId;

        if (!string.Equals(local.AccountAlias, baseline.AccountAlias, StringComparison.Ordinal))
            merged.AccountAlias = local.AccountAlias;

        if (!AreEquivalent(local.DefaultConfig, baseline.DefaultConfig))
            merged.DefaultConfig = Clone(local.DefaultConfig);

        foreach (var baselineCharacter in baseline.Characters)
        {
            if (!local.Characters.TryGetValue(baselineCharacter.Key, out var localCharacter))
            {
                merged.Characters.Remove(baselineCharacter.Key);
                continue;
            }

            if (!AreEquivalent(localCharacter, baselineCharacter.Value))
                merged.Characters[baselineCharacter.Key] = Clone(localCharacter);
        }

        foreach (var localCharacter in local.Characters)
        {
            if (!baseline.Characters.ContainsKey(localCharacter.Key))
                merged.Characters[localCharacter.Key] = Clone(localCharacter.Value);
        }

        foreach (var baselineCreatedAt in baseline.CharacterCreatedAtUtc)
        {
            if (!local.CharacterCreatedAtUtc.TryGetValue(baselineCreatedAt.Key, out var localCreatedAt))
            {
                merged.CharacterCreatedAtUtc.Remove(baselineCreatedAt.Key);
                continue;
            }

            if (localCreatedAt != baselineCreatedAt.Value)
                merged.CharacterCreatedAtUtc[baselineCreatedAt.Key] = localCreatedAt;
        }

        foreach (var localCreatedAt in local.CharacterCreatedAtUtc)
        {
            if (!baseline.CharacterCreatedAtUtc.ContainsKey(localCreatedAt.Key))
                merged.CharacterCreatedAtUtc[localCreatedAt.Key] = localCreatedAt.Value;
        }

        foreach (var removedKey in merged.CharacterCreatedAtUtc.Keys
                     .Where(key => !merged.Characters.ContainsKey(key))
                     .ToList())
        {
            merged.CharacterCreatedAtUtc.Remove(removedKey);
        }

        return merged;
    }

    private static AccountConfig CreateEmptyAccount(string accountId)
        => new()
        {
            AccountId = accountId,
        };

    private static AccountConfig Clone(AccountConfig account)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(account, JsonOptions);
        return JsonSerializer.Deserialize<AccountConfig>(json, JsonOptions)
               ?? throw new InvalidDataException("Account configuration clone failed.");
    }

    private static CharacterConfig Clone(CharacterConfig config)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(config, JsonOptions);
        return JsonSerializer.Deserialize<CharacterConfig>(json, JsonOptions)
               ?? throw new InvalidDataException("Character configuration clone failed.");
    }

    private static bool IsValidAccount(
        AccountConfig? account,
        string expectedAccountId,
        out string error)
    {
        if (account == null)
        {
            error = "The account configuration is null.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(account.AccountId) ||
            !string.Equals(account.AccountId, expectedAccountId, StringComparison.OrdinalIgnoreCase))
        {
            error = "The account ID is missing or does not match the file name.";
            return false;
        }

        if (account.DefaultConfig == null ||
            account.Characters == null ||
            account.CharacterCreatedAtUtc == null)
        {
            error = "The account configuration is missing required records.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool WaitForLock(Mutex mutex)
    {
        try
        {
            return mutex.WaitOne(LockTimeout);
        }
        catch (AbandonedMutexException)
        {
            return true;
        }
    }

    private string GetMutexName(string accountId)
    {
        var normalizedPath = Path.GetFullPath(GetPrimaryPath(accountId)).ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath)));
        return $"VERMAXION_AccountConfig_{hash[..24]}";
    }

    private sealed record CopyReadResult(
        bool Exists,
        bool IsValid,
        AccountConfig? Account,
        bool IsBackup,
        DateTime LastWriteTimeUtc,
        string Error);

    private sealed record DiskReadResult(
        bool HasAnyCopy,
        bool Succeeded,
        AccountConfig? Account,
        bool UsedBackup,
        bool PrimaryValid,
        string Error);
}
