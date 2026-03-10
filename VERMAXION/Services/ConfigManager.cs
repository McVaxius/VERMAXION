using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using VERMAXION.Models;

namespace VERMAXION.Services;

public class ConfigManager
{
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly IPluginLog log;
    private readonly string configDir;

    private readonly Dictionary<string, AccountConfig> accounts = new();

    public string CurrentAccountId { get; set; } = "";
    public string SelectedCharacterKey { get; set; } = "";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public ConfigManager(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        this.pluginInterface = pluginInterface;
        this.log = log;
        configDir = Path.Combine(pluginInterface.GetPluginConfigDirectory());
        if (!Directory.Exists(configDir))
            Directory.CreateDirectory(configDir);

        LoadAllAccounts();
    }

    public IReadOnlyDictionary<string, AccountConfig> Accounts => accounts;

    public AccountConfig? GetCurrentAccount()
    {
        if (string.IsNullOrEmpty(CurrentAccountId)) return null;
        return accounts.TryGetValue(CurrentAccountId, out var acc) ? acc : null;
    }

    public CharacterConfig GetActiveConfig()
    {
        var account = GetCurrentAccount();
        if (account == null)
        {
            log.Warning("[ConfigManager] GetCurrentAccount returned null - using default config");
            return new CharacterConfig();
        }

        if (string.IsNullOrEmpty(SelectedCharacterKey))
        {
            log.Warning($"[ConfigManager] SelectedCharacterKey is null - using default config for account {CurrentAccountId}");
            return account.DefaultConfig;
        }

        if (!account.Characters.TryGetValue(SelectedCharacterKey, out var cc))
        {
            log.Warning($"[ConfigManager] Character '{SelectedCharacterKey}' not found in account {CurrentAccountId} - using default config");
            return account.DefaultConfig;
        }

                return cc;
    }

    public CharacterConfig GetCurrentCharacterConfig(string charKey)
    {
        var account = GetCurrentAccount();
        if (account == null) return new CharacterConfig();
        if (string.IsNullOrEmpty(charKey)) return account.DefaultConfig;
        return account.Characters.TryGetValue(charKey, out var cc) ? cc : account.DefaultConfig;
    }

    public void EnsureAccountSelected(ulong contentId, string? aliasHint = null)
    {
        if (contentId == 0)
        {
            log.Warning("Cannot select account with content ID 0 - using fallback");
            if (accounts.Count > 0)
            {
                CurrentAccountId = accounts.Keys.First();
                return;
            }
            else
            {
                var fallbackId = Guid.NewGuid().ToString("N")[..8];
                var fallbackAccount = new AccountConfig
                {
                    AccountId = fallbackId,
                    AccountAlias = aliasHint ?? "Fallback Account",
                };
                accounts[fallbackId] = fallbackAccount;
                CurrentAccountId = fallbackId;
                SaveAccount(fallbackId);
                return;
            }
        }

        var accountId = contentId.ToString("X");
        if (!accounts.TryGetValue(accountId, out var account))
        {
            if (accounts.Count == 1)
            {
                var kvp = accounts.First();
                var oldId = kvp.Key;
                account = kvp.Value;
                accounts.Remove(oldId);
                account.AccountId = accountId;
                accounts[accountId] = account;

                try
                {
                    var oldFile = Path.Combine(configDir, $"{oldId}_Vermaxion.json");
                    if (File.Exists(oldFile))
                        File.Delete(oldFile);
                }
                catch (Exception ex)
                {
                    log.Warning($"Failed to delete legacy config file for {oldId}: {ex.Message}");
                }

                SaveAccount(accountId);
                log.Information($"Migrated legacy account {oldId} -> {accountId}");
            }
            else
            {
                account = new AccountConfig
                {
                    AccountId = accountId,
                    AccountAlias = !string.IsNullOrWhiteSpace(aliasHint)
                        ? aliasHint
                        : $"Account {accounts.Count + 1}",
                };
                accounts[accountId] = account;
                SaveAccount(accountId);
                log.Information($"Created account {accountId} ({account.AccountAlias})");
            }
        }
        else if (!string.IsNullOrWhiteSpace(aliasHint) && string.IsNullOrWhiteSpace(account.AccountAlias))
        {
            account.AccountAlias = aliasHint;
            SaveAccount(accountId);
        }

        CurrentAccountId = accountId;
    }

    public void EnsureCharacterExists(string characterName, string worldName)
    {
        if (string.IsNullOrEmpty(characterName) || string.IsNullOrEmpty(worldName))
            return;

        var charKey = $"{characterName}@{worldName}";

        foreach (var kvp in accounts)
        {
            if (kvp.Value.Characters.ContainsKey(charKey))
            {
                CurrentAccountId = kvp.Key;
                SelectedCharacterKey = charKey;
                return;
            }
        }

        if (string.IsNullOrEmpty(CurrentAccountId))
        {
            var fallbackId = accounts.Keys.FirstOrDefault();
            if (fallbackId == null)
            {
                fallbackId = Guid.NewGuid().ToString("N")[..8];
                accounts[fallbackId] = new AccountConfig
                {
                    AccountId = fallbackId,
                    AccountAlias = "Account 1",
                };
                SaveAccount(fallbackId);
            }
            CurrentAccountId = fallbackId;
        }

        if (!accounts.TryGetValue(CurrentAccountId, out var accountForChar))
        {
            log.Error($"Current account {CurrentAccountId} missing when adding {charKey}");
            return;
        }

        accountForChar.Characters[charKey] = accountForChar.DefaultConfig.Clone();
        SelectedCharacterKey = charKey;
        SaveAccount(CurrentAccountId);
        log.Information($"Added character {charKey} to account {CurrentAccountId}");
    }

    public string CreateNewAccount(string alias)
    {
        var newId = Guid.NewGuid().ToString("N")[..8];
        var newAccount = new AccountConfig
        {
            AccountId = newId,
            AccountAlias = alias,
        };
        accounts[newId] = newAccount;
        SaveAccount(newId);
        return newId;
    }

    public void SaveCurrentAccount()
    {
        if (!string.IsNullOrEmpty(CurrentAccountId))
            SaveAccount(CurrentAccountId);
    }

    public void ResetCharacterToDefault(string charKey)
    {
        var account = GetCurrentAccount();
        if (account == null) return;

        if (string.IsNullOrEmpty(charKey))
        {
            account.DefaultConfig = new CharacterConfig();
        }
        else if (account.Characters.ContainsKey(charKey))
        {
            account.Characters[charKey] = account.DefaultConfig.Clone();
        }

        SaveCurrentAccount();
    }

    public bool DeleteCharacter(string charKey)
    {
        var account = GetCurrentAccount();
        if (account == null || string.IsNullOrEmpty(charKey)) return false;
        if (!account.Characters.ContainsKey(charKey)) return false;

        account.Characters.Remove(charKey);
        if (SelectedCharacterKey == charKey)
            SelectedCharacterKey = "";

        SaveCurrentAccount();
        log.Information($"Deleted character config: {charKey}");
        return true;
    }

    public IEnumerable<string> GetSortedCharacterKeys()
    {
        var account = GetCurrentAccount();
        if (account == null) return Enumerable.Empty<string>();
        return account.Characters.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase);
    }

    public void UpdateAccountAlias(string alias)
    {
        var account = GetCurrentAccount();
        if (account == null) return;
        account.AccountAlias = alias;
        SaveCurrentAccount();
    }

    public void LoadAllAccounts()
    {
        try
        {
            var files = Directory.GetFiles(configDir, "*_Vermaxion.json");
            foreach (var file in files)
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var account = JsonSerializer.Deserialize<AccountConfig>(json, JsonOptions);
                    if (account != null && !string.IsNullOrEmpty(account.AccountId))
                    {
                        accounts[account.AccountId] = account;
                        log.Information($"Loaded account {account.AccountId} ({account.AccountAlias}) with {account.Characters.Count} characters");
                    }
                }
                catch (Exception ex)
                {
                    log.Error($"Failed to load config file {file}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            log.Error($"Failed to enumerate config files: {ex.Message}");
        }
    }

    private void SaveAccount(string accountId)
    {
        if (!accounts.TryGetValue(accountId, out var account)) return;

        try
        {
            var fileName = $"{accountId}_Vermaxion.json";
            var filePath = Path.Combine(configDir, fileName);
            var json = JsonSerializer.Serialize(account, JsonOptions);
            File.WriteAllText(filePath, json);
            log.Debug($"Saved account {accountId}");
        }
        catch (Exception ex)
        {
            log.Error($"Failed to save account {accountId}: {ex.Message}");
        }
    }

    public static string FixNameCapitalization(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;

        var parts = input.Split('@');
        var charPart = parts[0].Trim();
        var serverPart = parts.Length > 1 ? parts[1].Trim() : "";

        charPart = string.Join(" ", charPart.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Length > 0
                ? char.ToUpper(w[0]) + (w.Length > 1 ? w[1..].ToLower() : "")
                : w));

        if (serverPart.Length > 0)
            serverPart = char.ToUpper(serverPart[0]) + (serverPart.Length > 1 ? serverPart[1..].ToLower() : "");

        return serverPart.Length > 0 ? $"{charPart}@{serverPart}" : charPart;
    }
}
