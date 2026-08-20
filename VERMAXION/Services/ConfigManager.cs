using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using VERMAXION.Models;

namespace VERMAXION.Services;

public class ConfigManager
{
    private readonly IPluginLog log;
    private readonly string configDir;
    private readonly AccountConfigPersistence persistence;

    private readonly Dictionary<string, AccountConfig> accounts = new();
    private readonly HashSet<string> unreadableAccountIds = new(StringComparer.OrdinalIgnoreCase);

    public string CurrentAccountId { get; set; } = "";
    public string CurrentCharacterKey { get; private set; } = "";

    private string selectedCharacterKey = "";
    public string SelectedCharacterKey
    {
        get => selectedCharacterKey;
        set => selectedCharacterKey = value;
    }

    public event Action<string, string>? OnCharacterChanged;

    public ConfigManager(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        this.log = log;
        configDir = Path.Combine(pluginInterface.GetPluginConfigDirectory());
        if (!Directory.Exists(configDir))
            Directory.CreateDirectory(configDir);

        persistence = new AccountConfigPersistence(configDir);
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
            return CharacterConfig.CreateNew();
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
            log.Warning("Current character content ID is 0; selecting by existing account membership only.");

        var hasCurrentCharacterKey = !string.IsNullOrWhiteSpace(currentCharacterKey);
        var selectionInputs = accounts
            .Select(pair => new AccountSelectionInput(
                pair.Key,
                hasCurrentCharacterKey &&
                pair.Value.Characters.ContainsKey(currentCharacterKey!),
                pair.Value.Characters.Count))
            .ToList();
        var decision = AccountSelectionPolicy.Select(
            selectionInputs,
            CurrentAccountId,
            hasCurrentCharacterKey,
            unreadableAccountIds.Count > 0);

        switch (decision.Action)
        {
            case AccountSelectionAction.SelectExisting:
                SelectExistingAccount(decision.TargetAccountId, aliasHint, contentId);
                break;
            case AccountSelectionAction.CreateNew:
                var newId = CreateNewAccount(aliasHint ?? "Account 1");
                CurrentAccountId = newId;
                log.Information($"[ConfigManager] Account selection: created first accountId={newId}, contentId={contentId:X16}");
                break;
            case AccountSelectionAction.RefuseUnreadable:
                log.Error("[ConfigManager] Account selection failed closed because one or more account files are unreadable.");
                break;
        }
    }

    public void EnsureCharacterExists(string characterName, string worldName)
    {
        if (string.IsNullOrEmpty(characterName) || string.IsNullOrEmpty(worldName))
            return;

        var charKey = $"{characterName}@{worldName}";

        if (TryFindBestAccountContainingCharacter(charKey, out var existingAccountId, out var existingAccount))
        {
            CurrentAccountId = existingAccountId;
            if (EnsureCharacterCreatedAtUtc(existingAccount, charKey, DateTime.UtcNow))
            {
                SaveAccount(existingAccountId);
            }

            SetCurrentCharacterKey(charKey);
            if (string.IsNullOrEmpty(SelectedCharacterKey))
                SelectedCharacterKey = charKey;
            return;
        }

        if (string.IsNullOrEmpty(CurrentAccountId) ||
            !accounts.TryGetValue(CurrentAccountId, out var accountForChar))
        {
            if (unreadableAccountIds.Count > 0)
            {
                log.Error("[ConfigManager] Refusing to add an unknown character because no readable account is selected.");
                return;
            }

            var fallbackId = accounts
                .OrderByDescending(pair => pair.Value.Characters.Count)
                .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => pair.Key)
                .FirstOrDefault();

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
            accountForChar = accounts[fallbackId];
        }

        accountForChar.Characters[charKey] = accountForChar.DefaultConfig.Clone();
        accountForChar.Characters[charKey].JumboCactpotUnclaimedTickets = 0;
        accountForChar.Characters[charKey].JumboCactpotPayoutAvailableAt = DateTime.MinValue;
        EnsureCharacterCreatedAtUtc(accountForChar, charKey, DateTime.UtcNow);
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

    private bool TryFindBestAccountContainingCharacter(
        string charKey,
        out string accountId,
        out AccountConfig account)
    {
        var match = accounts
            .Where(pair => pair.Value.Characters.ContainsKey(charKey))
            .OrderByDescending(pair => pair.Value.Characters.Count)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (match.Value != null)
        {
            accountId = match.Key;
            account = match.Value;
            return true;
        }

        accountId = string.Empty;
        account = null!;
        return false;
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
            account.DefaultConfig = CharacterConfig.CreateNew();
        }
        else if (account.Characters.ContainsKey(charKey))
        {
            account.Characters[charKey] = account.DefaultConfig.Clone();
            account.Characters[charKey].JumboCactpotUnclaimedTickets = 0;
            account.Characters[charKey].JumboCactpotPayoutAvailableAt = DateTime.MinValue;
        }

        SaveCurrentAccount();
    }

    public bool DeleteCharacter(string charKey)
    {
        var account = GetCurrentAccount();
        if (account == null || string.IsNullOrEmpty(charKey)) return false;
        if (!account.Characters.ContainsKey(charKey)) return false;

        account.Characters.Remove(charKey);
        account.CharacterCreatedAtUtc.Remove(charKey);
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

    /// <summary>Bridge-callable "set default and apply to all toons" for the fishing return destination only.
    /// Sets the destination (and command, used when destination is Custom) on every account's DefaultConfig
    /// and every character config, then saves each account. Destination is passed as an int so the reflection
    /// bridge (which cannot construct the enum or a copy lambda) can call it, e.g. (5, "/li inn") for Inn.</summary>
    public int SetFishingReturnDestinationForAllCharacters(int destination, string command)
    {
        var dest = (FishingReturnDestination)destination;
        command ??= string.Empty;
        var count = 0;
        foreach (var account in accounts.Values)
        {
            account.DefaultConfig.FishingReturnDestination = dest;
            account.DefaultConfig.FishingReturnCommand = command;
            foreach (var character in account.Characters.Values)
            {
                character.FishingReturnDestination = dest;
                character.FishingReturnCommand = command;
                count++;
            }
            SaveAccount(account.AccountId);
        }
        log.Information($"[ConfigManager] Set fishing return destination {dest} ('{command}') on {count} character config(s) across {accounts.Count} account(s)");
        return count;
    }

    public int MigrateFishingStockCatalog(IEnumerable<FishingStockCatalogEntry> catalog)
    {
        var rows = catalog.ToList();
        var count = 0;
        foreach (var account in accounts.Values)
        {
            if (FishingStockCatalogPolicy.MigrateLegacy(
                    account.DefaultConfig.FishingStockItems,
                    account.DefaultConfig.FishingLureRestockTarget,
                    rows))
            {
                count++;
            }

            foreach (var character in account.Characters.Values)
            {
                if (FishingStockCatalogPolicy.MigrateLegacy(
                        character.FishingStockItems,
                        character.FishingLureRestockTarget,
                        rows))
                {
                    count++;
                }
            }

            SaveAccount(account.AccountId);
        }

        return count;
    }

    public bool AddFishingStockCatalogEntry(
        Configuration configuration,
        uint itemId,
        int defaultTarget,
        bool defaultEnabled,
        int defaultMin = 0)
    {
        if (!FishingStockCatalogPolicy.TryAdd(
                configuration.FishingStockCatalog,
                itemId,
                defaultTarget,
                defaultEnabled,
                defaultMin))
        {
            return false;
        }

        var row = configuration.FishingStockCatalog[^1];
        foreach (var account in accounts.Values)
        {
            FishingStockCatalogPolicy.SyncRow(account.DefaultConfig.FishingStockItems, row);
            foreach (var character in account.Characters.Values)
                FishingStockCatalogPolicy.SyncRow(character.FishingStockItems, row);
            SaveAccount(account.AccountId);
        }
        configuration.Save();
        return true;
    }

    public bool RemoveFishingStockCatalogEntry(Configuration configuration, uint itemId)
    {
        var maps = accounts.Values.SelectMany(account =>
            new[] { account.DefaultConfig.FishingStockItems }
                .Concat(account.Characters.Values.Select(character => character.FishingStockItems)));
        if (!FishingStockCatalogPolicy.Remove(configuration.FishingStockCatalog, itemId, maps))
            return false;

        foreach (var account in accounts.Values)
            SaveAccount(account.AccountId);
        configuration.Save();
        return true;
    }

    public int SyncFishingStockRowToCurrentAccount(FishingStockCatalogEntry row)
    {
        var account = GetCurrentAccount();
        if (account == null)
            return 0;

        FishingStockCatalogPolicy.SyncRow(account.DefaultConfig.FishingStockItems, row);
        foreach (var character in account.Characters.Values)
            FishingStockCatalogPolicy.SyncRow(character.FishingStockItems, row);
        SaveCurrentAccount();
        return account.Characters.Count + 1;
    }

    public int SyncAllFishingStockRowsToCurrentAccount(
        IEnumerable<FishingStockCatalogEntry> catalog)
    {
        var account = GetCurrentAccount();
        if (account == null)
            return 0;

        foreach (var row in catalog)
        {
            FishingStockCatalogPolicy.SyncRow(account.DefaultConfig.FishingStockItems, row);
            foreach (var character in account.Characters.Values)
                FishingStockCatalogPolicy.SyncRow(character.FishingStockItems, row);
        }
        SaveCurrentAccount();
        return account.Characters.Count + 1;
    }

    private static void CopyDefaultSettings(CharacterConfig source, CharacterConfig target)
    {
        target.Enabled = source.Enabled;
        target.EnableVerminionQueue = source.EnableVerminionQueue;
        target.EnableJumboCactpot = source.EnableJumboCactpot;
        target.EnableMiniCactpot = source.EnableMiniCactpot;
        target.EnableChocoboRacing = source.EnableChocoboRacing;
        target.EnableFCBuffRefill = source.EnableFCBuffRefill;
        target.EnableMinionRoulette = source.EnableMinionRoulette;
        target.EnableSeasonalGearRoulette = source.EnableSeasonalGearRoulette;
        target.EnableGearUpdater = source.EnableGearUpdater;
        target.EnableHighestCombatJob = source.EnableHighestCombatJob;
        target.EnableCurrentJobEquipment = source.EnableCurrentJobEquipment;
        target.EnableFashionReport = source.EnableFashionReport;
        target.EnableRegisterRegistrables = source.EnableRegisterRegistrables;
        target.RegisterUnregisteredItemsFromInventory = source.RegisterUnregisteredItemsFromInventory;
        target.EnableVendorStock = source.EnableVendorStock;
        target.EnableRefillFromListings = source.EnableRefillFromListings;
        target.EnableAfterArPark = source.EnableAfterArPark;
        target.EnableAlliedSociety = source.EnableAlliedSociety;
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
        target.FishingStockItems = source.FishingStockItems.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Clone());
        target.EnableRetainerEquipping = source.EnableRetainerEquipping;
        target.RetainerGearSourceMode = source.RetainerGearSourceMode;
        target.RetainerGearNonUniqueOnly = source.RetainerGearNonUniqueOnly;
        target.RetainerCombatItemLevelTarget = source.RetainerCombatItemLevelTarget;
        target.RetainerGatheringPerceptionTarget = source.RetainerGatheringPerceptionTarget;
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
        target.AfterArParkDestination = source.AfterArParkDestination;
        target.AfterArParkCustomCommand = source.AfterArParkCustomCommand;
        target.AlliedSocietyGearsetSelection = source.AlliedSocietyGearsetSelection;
        target.AlliedSocietyGearsetId = source.AlliedSocietyGearsetId;
        target.NagYourMomRunsPerDay = source.NagYourMomRunsPerDay;
        target.NagYourMomFrontlineRunsPerDay = source.NagYourMomFrontlineRunsPerDay;
        target.NagYourMomRivalWingsRunsPerDay = source.NagYourMomRivalWingsRunsPerDay;
        target.NagYourMomJob = NormalizeJobAbbreviation(source.NagYourMomJob);
        target.NagYourMomWindowStartLocal = source.NagYourMomWindowStartLocal;
        target.NagYourMomWindowEndLocal = source.NagYourMomWindowEndLocal;
        target.NagYourMomStopAtSeriesRank25 = source.NagYourMomStopAtSeriesRank25;
        target.NagYourDadSelectionKind = source.NagYourDadSelectionKind;
        target.NagYourDadSelectionId = source.NagYourDadSelectionId;
        target.NagYourDadSelectionDisplayName = source.NagYourDadSelectionDisplayName;
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
        target.FishingDiscardAfterVoyage = source.FishingDiscardAfterVoyage;
        target.FishingSellAfterVoyage = source.FishingSellAfterVoyage;
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
        => GetSortedCharacterKeys(CharacterListSortMode.Name);

    public IEnumerable<string> GetSortedCharacterKeys(CharacterListSortMode sortMode)
    {
        var account = GetCurrentAccount();
        if (account == null) return Enumerable.Empty<string>();
        return CharacterSortPolicy.Sort(account.Characters.Keys, sortMode, account.CharacterCreatedAtUtc);
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
            var previousAccounts = accounts.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.OrdinalIgnoreCase);
            var loadedAccounts = new Dictionary<string, AccountConfig>(StringComparer.OrdinalIgnoreCase);
            var accountsNeedingSave = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            unreadableAccountIds.Clear();

            foreach (var result in persistence.LoadAll())
            {
                if (!result.Succeeded || result.Account == null)
                {
                    unreadableAccountIds.Add(result.AccountId);
                    if (previousAccounts.TryGetValue(result.AccountId, out var previousAccount))
                        loadedAccounts[result.AccountId] = previousAccount;

                    log.Error($"Failed to load account config {result.AccountId}: {result.Error}");
                    continue;
                }

                var account = result.Account;
                loadedAccounts[account.AccountId] = account;
                if (result.UsedBackup)
                    log.Warning($"[ConfigManager] Loaded last-known-good backup for account {account.AccountId}");
                if (BackfillCharacterCreatedAtUtc(account))
                    accountsNeedingSave.Add(account.AccountId);
            }

            accounts.Clear();
            foreach (var pair in loadedAccounts)
                accounts[pair.Key] = pair.Value;

            foreach (var accountId in accountsNeedingSave)
                SaveAccount(accountId);

            if (!string.IsNullOrWhiteSpace(CurrentAccountId) &&
                !accounts.ContainsKey(CurrentAccountId))
            {
                CurrentAccountId = accounts
                    .OrderByDescending(pair => pair.Value.Characters.Count)
                    .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(pair => pair.Key)
                    .FirstOrDefault() ?? string.Empty;
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

        var result = persistence.Save(accountId, account);
        if (!result.Succeeded || result.Account == null)
        {
            log.Error($"Failed to save account {accountId}: {result.Error}");
            return;
        }

        ApplyMergedAccount(account, result.Account);
        unreadableAccountIds.Remove(accountId);
        if (result.UsedBackup)
            log.Warning($"[ConfigManager] Recovered account {accountId} from its last-known-good backup while saving");
        log.Debug($"Saved account {accountId}");
    }

    private static void ApplyMergedAccount(AccountConfig target, AccountConfig merged)
    {
        target.AccountId = merged.AccountId;
        target.AccountAlias = merged.AccountAlias;

        if (!AccountConfigPersistence.AreEquivalent(target.DefaultConfig, merged.DefaultConfig))
            target.DefaultConfig = merged.DefaultConfig;

        foreach (var removedKey in target.Characters.Keys
                     .Where(key => !merged.Characters.ContainsKey(key))
                     .ToList())
        {
            target.Characters.Remove(removedKey);
        }

        foreach (var pair in merged.Characters)
        {
            if (!target.Characters.TryGetValue(pair.Key, out var current) ||
                !AccountConfigPersistence.AreEquivalent(current, pair.Value))
            {
                target.Characters[pair.Key] = pair.Value;
            }
        }

        target.CharacterCreatedAtUtc.Clear();
        foreach (var pair in merged.CharacterCreatedAtUtc)
            target.CharacterCreatedAtUtc[pair.Key] = pair.Value;
    }

    private static bool EnsureCharacterCreatedAtUtc(AccountConfig account, string characterKey, DateTime createdAtUtc)
    {
        if (string.IsNullOrWhiteSpace(characterKey) ||
            account.CharacterCreatedAtUtc.ContainsKey(characterKey))
        {
            return false;
        }

        account.CharacterCreatedAtUtc[characterKey] = createdAtUtc.ToUniversalTime();
        return true;
    }

    private static bool BackfillCharacterCreatedAtUtc(AccountConfig account)
    {
        var missingKeys = account.Characters.Keys
            .Where(key => !account.CharacterCreatedAtUtc.ContainsKey(key))
            .ToList();

        if (missingKeys.Count == 0)
            return false;

        var backfillStartUtc = DateTime.UtcNow.AddTicks(-missingKeys.Count);
        for (var index = 0; index < missingKeys.Count; index++)
            account.CharacterCreatedAtUtc[missingKeys[index]] = backfillStartUtc.AddTicks(index);

        return true;
    }

    private static bool TryGetCharacterCreatedAtUtc(
        AccountConfig? account,
        string characterKey,
        out DateTime createdAtUtc)
    {
        createdAtUtc = DateTime.MinValue;
        return account != null &&
               account.CharacterCreatedAtUtc.TryGetValue(characterKey, out createdAtUtc) &&
               createdAtUtc != DateTime.MinValue;
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
