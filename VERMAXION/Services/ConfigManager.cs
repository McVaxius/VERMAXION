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
    public string CurrentCharacterKey { get; private set; } = "";

    private string selectedCharacterKey = "";
    public string SelectedCharacterKey
    {
        get => selectedCharacterKey;
        set => selectedCharacterKey = value;
    }

    public event Action<string, string>? OnCharacterChanged;

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
        return GetConfigForKey(CurrentCharacterKey);
    }

    public CharacterConfig GetSelectedConfig()
    {
        return GetConfigForKey(SelectedCharacterKey);
    }

    public CharacterConfig GetConfigForKey(string charKey)
    {
        var account = GetCurrentAccount();
        if (account == null)
        {
            log.Warning("[ConfigManager] GetCurrentAccount returned null - using default config");
            return new CharacterConfig();
        }

        if (string.IsNullOrEmpty(charKey))
        {
            return account.DefaultConfig;
        }

        if (!account.Characters.TryGetValue(charKey, out var cc))
        {
            return account.DefaultConfig;
        }

        return cc;
    }

    public CharacterConfig GetCurrentCharacterConfig(string charKey)
    {
        return GetConfigForKey(charKey);
    }

    public void EnsureAccountSelected(ulong contentId, string? aliasHint = null, string? currentCharacterKey = null)
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

        var selectionInputs = accounts
            .Select(pair => new AccountSelectionInput(
                pair.Key,
                !string.IsNullOrWhiteSpace(currentCharacterKey) &&
                pair.Value.Characters.ContainsKey(currentCharacterKey)))
            .ToList();
        var decision = AccountSelectionPolicy.Select(
            selectionInputs,
            contentId,
            CurrentAccountId,
            !string.IsNullOrWhiteSpace(currentCharacterKey));

        switch (decision.Action)
        {
            case AccountSelectionAction.SelectExisting:
                SelectExistingAccount(decision.TargetAccountId, aliasHint, contentId);
                break;
            case AccountSelectionAction.MigrateLegacy:
                MigrateLegacyAccount(decision.SourceAccountId, decision.TargetAccountId, aliasHint, contentId);
                break;
            case AccountSelectionAction.CreateCanonical:
                CreateCanonicalAccountFromSelection(decision, aliasHint, currentCharacterKey, contentId);
                break;
        }
    }

    public void EnsureCharacterExists(string characterName, string worldName)
    {
        if (string.IsNullOrEmpty(characterName) || string.IsNullOrEmpty(worldName))
            return;

        var charKey = $"{characterName}@{worldName}";

        if (!string.IsNullOrEmpty(CurrentAccountId) &&
            accounts.TryGetValue(CurrentAccountId, out var currentAccount))
        {
            if (!currentAccount.Characters.ContainsKey(charKey))
            {
                currentAccount.Characters[charKey] = FindCharacterConfigInOtherAccount(charKey)?.Clone()
                                                     ?? currentAccount.DefaultConfig.Clone();
                SaveAccount(CurrentAccountId);
                log.Information($"Added character {charKey} to account {CurrentAccountId}");
            }

            SetCurrentCharacterKey(charKey);
            if (string.IsNullOrEmpty(SelectedCharacterKey))
                SelectedCharacterKey = charKey;
            return;
        }

        foreach (var kvp in accounts)
        {
            if (kvp.Value.Characters.ContainsKey(charKey))
            {
                CurrentAccountId = kvp.Key;
                SetCurrentCharacterKey(charKey);
                if (string.IsNullOrEmpty(SelectedCharacterKey))
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
        SetCurrentCharacterKey(charKey);
        if (string.IsNullOrEmpty(SelectedCharacterKey))
            SelectedCharacterKey = charKey;
        SaveAccount(CurrentAccountId);
        log.Information($"Added character {charKey} to account {CurrentAccountId}");
    }

    private void SelectExistingAccount(string accountId, string? aliasHint, ulong contentId)
    {
        if (!accounts.TryGetValue(accountId, out var account))
        {
            log.Error($"[ConfigManager] Account selection chose missing existing account {accountId}");
            return;
        }

        if (!string.IsNullOrWhiteSpace(aliasHint) && string.IsNullOrWhiteSpace(account.AccountAlias))
        {
            account.AccountAlias = aliasHint;
            SaveAccount(accountId);
        }

        CurrentAccountId = accountId;
        log.Information($"[ConfigManager] Account selection: select existing accountId={accountId}, contentId={contentId:X16}");
    }

    private void MigrateLegacyAccount(string oldId, string accountId, string? aliasHint, ulong contentId)
    {
        if (!accounts.TryGetValue(oldId, out var account))
        {
            log.Error($"[ConfigManager] Account selection chose missing legacy account {oldId}");
            return;
        }

        accounts.Remove(oldId);
        account.AccountId = accountId;
        if (!string.IsNullOrWhiteSpace(aliasHint) && string.IsNullOrWhiteSpace(account.AccountAlias))
            account.AccountAlias = aliasHint;
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
        CurrentAccountId = accountId;
        log.Information($"[ConfigManager] Account selection: migrate legacy source={oldId}, target={accountId}, contentId={contentId:X16}");
    }

    private void CreateCanonicalAccountFromSelection(
        AccountSelectionDecision decision,
        string? aliasHint,
        string? currentCharacterKey,
        ulong contentId)
    {
        var accountId = decision.TargetAccountId;
        if (accounts.TryGetValue(accountId, out _))
        {
            SelectExistingAccount(accountId, aliasHint, contentId);
            return;
        }

        AccountConfig? sourceAccount = null;
        if (!string.IsNullOrWhiteSpace(decision.SourceAccountId))
            accounts.TryGetValue(decision.SourceAccountId, out sourceAccount);

        var account = new AccountConfig
        {
            AccountId = accountId,
            AccountAlias = !string.IsNullOrWhiteSpace(aliasHint)
                ? aliasHint
                : $"Account {accounts.Count + 1}",
            DefaultConfig = sourceAccount?.DefaultConfig.Clone() ?? new CharacterConfig(),
        };

        if (decision.CopyCurrentCharacterConfig &&
            sourceAccount != null &&
            !string.IsNullOrWhiteSpace(currentCharacterKey) &&
            sourceAccount.Characters.TryGetValue(currentCharacterKey, out var sourceCharacterConfig))
        {
            account.Characters[currentCharacterKey] = sourceCharacterConfig.Clone();
            log.Information($"[ConfigManager] Account selection: copy character config character={currentCharacterKey}, source={decision.SourceAccountId}, target={accountId}");
        }

        accounts[accountId] = account;
        SaveAccount(accountId);
        CurrentAccountId = accountId;
        log.Information($"[ConfigManager] Account selection: create canonical accountId={accountId}, source={decision.SourceAccountId}, contentId={contentId:X16}");
    }

    private CharacterConfig? FindCharacterConfigInOtherAccount(string charKey)
    {
        foreach (var pair in accounts)
        {
            if (string.Equals(pair.Key, CurrentAccountId, StringComparison.OrdinalIgnoreCase))
                continue;

            if (pair.Value.Characters.TryGetValue(charKey, out var config))
                return config;
        }

        return null;
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
        if (CurrentCharacterKey == charKey)
            SetCurrentCharacterKey("");

        SaveCurrentAccount();
        log.Information($"Deleted character config: {charKey}");
        return true;
    }

    public int ApplyDefaultToAllCharacters()
    {
        var account = GetCurrentAccount();
        if (account == null) return 0;

        var defaultConfig = account.DefaultConfig;
        int count = 0;

        foreach (var charKey in account.Characters.Keys.ToList())
        {
            var cc = account.Characters[charKey];
            CopyDefaultSettings(defaultConfig, cc);
            count++;
        }

        SaveCurrentAccount();
        log.Information($"[ConfigManager] Applied default settings to {count} characters");
        return count;
    }

    public int ApplyDefaultSettingToAllCharacters(string label, Action<CharacterConfig, CharacterConfig> copy)
    {
        var account = GetCurrentAccount();
        if (account == null) return 0;

        var defaultConfig = account.DefaultConfig;
        int count = 0;

        foreach (var charKey in account.Characters.Keys.ToList())
        {
            copy(defaultConfig, account.Characters[charKey]);
            count++;
        }

        SaveCurrentAccount();
        log.Information($"[ConfigManager] Applied default setting '{label}' to {count} characters");
        return count;
    }

    public int ApplyFishingOperationSettingsToAllAccounts(FishingOperationSettings settings)
    {
        var count = 0;
        foreach (var account in accounts.Values)
        {
            CopyFishingOperationSettings(settings, account.DefaultConfig);
            count++;

            foreach (var character in account.Characters.Values)
            {
                CopyFishingOperationSettings(settings, character);
                count++;
            }

            SaveAccount(account.AccountId);
        }

        if (count > 0)
            log.Information($"[ConfigManager] Migrated legacy fishing operation settings to {count} character config record(s)");

        return count;
    }

    private static void CopyDefaultSettings(CharacterConfig source, CharacterConfig target)
    {
        target.Enabled = source.Enabled;
        target.EnableVerminionQueue = source.EnableVerminionQueue;
        target.EnableJumboCactpot = source.EnableJumboCactpot;
        target.EnableMiniCactpot = source.EnableMiniCactpot;
        target.EnableChocoboRacing = source.EnableChocoboRacing;
        target.EnableFCBuffRefill = source.EnableFCBuffRefill;
        target.EnableHenchmanManagement = source.EnableHenchmanManagement;
        target.EnableMinionRoulette = source.EnableMinionRoulette;
        target.EnableSeasonalGearRoulette = source.EnableSeasonalGearRoulette;
        target.EnableGearUpdater = source.EnableGearUpdater;
        target.EnableHighestCombatJob = source.EnableHighestCombatJob;
        target.EnableCurrentJobEquipment = source.EnableCurrentJobEquipment;
        target.EnableFashionReport = source.EnableFashionReport;
        target.EnableRegisterRegistrables = source.EnableRegisterRegistrables;
        target.EnableVendorStock = source.EnableVendorStock;
        target.EnableRefillFromListings = source.EnableRefillFromListings;
        target.EnableNagYourMom = source.EnableNagYourMom;
        target.EnableNagYourMomCasualCc = source.EnableNagYourMomCasualCc;
        target.EnableNagYourMomFrontline = source.EnableNagYourMomFrontline;
        target.EnableNagYourMomRivalWings = source.EnableNagYourMomRivalWings;
        target.EnableNagYourDad = source.EnableNagYourDad;
        target.EnableEvercoldAdventurerActivity = source.EnableEvercoldAdventurerActivity;
        target.EnableMiscCmd = source.EnableMiscCmd;
        target.EnableLootGoblinMapGather = source.EnableLootGoblinMapGather;
        target.EnableFishing = source.EnableFishing;
        target.AlwaysFishOnThisCharacterIfWindowOpen = source.AlwaysFishOnThisCharacterIfWindowOpen;
        CopyFishingOperationSettings(source, target);
        target.ChocoboRacesPerDay = source.ChocoboRacesPerDay;
        target.SkipChocoboRacingAtRank50 = source.SkipChocoboRacingAtRank50;
        target.FCBuffPurchaseAttempts = source.FCBuffPurchaseAttempts;
        target.FCBuffMinPoints = source.FCBuffMinPoints;
        target.FCBuffMinGil = source.FCBuffMinGil;
        target.VendorStockGysahlGreensTarget = source.VendorStockGysahlGreensTarget;
        target.VendorStockGrade8DarkMatterTarget = source.VendorStockGrade8DarkMatterTarget;
        target.RefillFromListingsFrequency = source.RefillFromListingsFrequency;
        target.RefillFromListingsSelectionMode = source.RefillFromListingsSelectionMode;
        target.RefillFromListingsRoute = source.RefillFromListingsRoute;
        target.RefillFromListingsMinFreeInventorySlots = source.RefillFromListingsMinFreeInventorySlots;
        target.NagYourMomRunsPerDay = source.NagYourMomRunsPerDay;
        target.NagYourMomFrontlineRunsPerDay = source.NagYourMomFrontlineRunsPerDay;
        target.NagYourMomRivalWingsRunsPerDay = source.NagYourMomRivalWingsRunsPerDay;
        target.NagYourMomJob = NormalizeJobAbbreviation(source.NagYourMomJob);
        target.NagYourMomWindowStartLocal = source.NagYourMomWindowStartLocal;
        target.NagYourMomWindowEndLocal = source.NagYourMomWindowEndLocal;
        target.NagYourMomStopAtSeriesRank25 = source.NagYourMomStopAtSeriesRank25;
        target.NagYourDadDungeonCount = source.NagYourDadDungeonCount;
        target.NagYourDadDungeonFrequency = DadRunRequestOptions.NormalizeFrequency(source.NagYourDadDungeonFrequency);
        target.NagYourDadDungeonContentFinderConditionId = source.NagYourDadDungeonContentFinderConditionId;
        target.NagYourDadDungeonName = source.NagYourDadDungeonName;
        target.NagYourDadDungeonJob = NormalizeJobAbbreviation(source.NagYourDadDungeonJob);
        target.NagYourDadQueueViaLanParty = source.NagYourDadQueueViaLanParty;
        target.NagYourDadDungeonUnsynced = source.NagYourDadDungeonUnsynced;
        target.NagYourDadDailyMsq = source.NagYourDadDailyMsq;
        target.NagYourDadLanPartyPreset = source.NagYourDadLanPartyPreset;
        target.NagYourDadCommendationAttempts = source.NagYourDadCommendationAttempts;
        target.NagYourDadAstropeAttempts = source.NagYourDadAstropeAttempts;
        target.NagYourDadWindowStartLocal = source.NagYourDadWindowStartLocal;
        target.NagYourDadWindowEndLocal = source.NagYourDadWindowEndLocal;
        target.EvercoldAdventurerActivityTargetPoints = source.EvercoldAdventurerActivityTargetPoints;
        target.LootGoblinMapGatherItemId = source.LootGoblinMapGatherItemId;
        target.LootGoblinMapGatherRunAfterGather = source.LootGoblinMapGatherRunAfterGather;
        target.RequireSaucyForMiniCactpot = source.RequireSaucyForMiniCactpot;
        target.JumboCactpotNumberMode = source.JumboCactpotNumberMode;
        target.JumboCactpotFixedNumber = source.JumboCactpotFixedNumber;
        target.PersonalRegistrableItems = new List<uint>(source.PersonalRegistrableItems);
    }

    private static void CopyFishingOperationSettings(CharacterConfig source, CharacterConfig target)
    {
        target.FishingLureRestockTarget = source.FishingLureRestockTarget;
        target.FishingReturnDestination = source.FishingReturnDestination;
        target.FishingReturnCommand = source.FishingReturnCommand;
        target.FishingRepairMode = source.FishingRepairMode;
        target.FishingRepairThresholdPercent = source.FishingRepairThresholdPercent;
    }

    private static void CopyFishingOperationSettings(FishingOperationSettings source, CharacterConfig target)
    {
        target.FishingLureRestockTarget = Math.Max(0, source.LureRestockTarget);
        target.FishingReturnDestination = source.ReturnDestination;
        target.FishingReturnCommand = source.ReturnCommand;
        target.FishingRepairMode = source.RepairMode;
        target.FishingRepairThresholdPercent = Math.Clamp(source.RepairThresholdPercent, 0, 100);
    }

    private static string NormalizeJobAbbreviation(string value)
        => value?.Trim().ToUpperInvariant() ?? string.Empty;

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

    public int DisableAlwaysFishOnOtherCharacters(string selectedCharacterKey)
    {
        var account = GetCurrentAccount();
        if (account == null || string.IsNullOrWhiteSpace(selectedCharacterKey))
            return 0;

        var count = 0;
        foreach (var pair in account.Characters)
        {
            if (string.Equals(pair.Key, selectedCharacterKey, StringComparison.OrdinalIgnoreCase) ||
                !pair.Value.AlwaysFishOnThisCharacterIfWindowOpen)
            {
                continue;
            }

            pair.Value.AlwaysFishOnThisCharacterIfWindowOpen = false;
            count++;
        }

        if (count > 0)
            SaveCurrentAccount();

        return count;
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

    private void SetCurrentCharacterKey(string charKey)
    {
        if (CurrentCharacterKey == charKey)
            return;

        var previousCharacterKey = CurrentCharacterKey;
        CurrentCharacterKey = charKey;
        OnCharacterChanged?.Invoke(previousCharacterKey, charKey);
    }
}
