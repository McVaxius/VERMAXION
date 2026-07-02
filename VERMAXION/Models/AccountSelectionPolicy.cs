using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace VERMAXION.Models;

internal enum AccountSelectionAction
{
    SelectExisting,
    MigrateLegacy,
    CreateCanonical,
}

internal readonly record struct AccountSelectionInput(
    string AccountId,
    bool HasCurrentCharacter);

internal readonly record struct AccountSelectionDecision(
    AccountSelectionAction Action,
    string TargetAccountId,
    string SourceAccountId,
    bool CopyCurrentCharacterConfig);

internal static class AccountSelectionPolicy
{
    public static string GetCanonicalAccountId(ulong contentId)
        => contentId.ToString("X", CultureInfo.InvariantCulture);

    public static bool IsCanonicalContentAccountId(string accountId)
        => TryParseContentAccountId(accountId, out _);

    private static bool TryParseContentAccountId(string accountId, out ulong contentId)
    {
        contentId = 0;
        var normalized = accountId.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        if (normalized.Length > 16 || normalized.Any(ch => !Uri.IsHexDigit(ch)))
            return false;

        if (!ulong.TryParse(normalized, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var parsed) || parsed == 0)
            return false;

        contentId = parsed;
        return true;
    }

    public static AccountSelectionDecision Select(
        IEnumerable<AccountSelectionInput> accounts,
        ulong contentId,
        string currentAccountId,
        bool hasCurrentCharacterKey)
    {
        if (contentId == 0)
            throw new ArgumentOutOfRangeException(nameof(contentId), "Content ID 0 cannot be canonicalized.");

        var targetAccountId = GetCanonicalAccountId(contentId);
        var accountList = accounts
            .Where(account => !string.IsNullOrWhiteSpace(account.AccountId))
            .GroupBy(account => account.AccountId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(account => account.AccountId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var existingTarget = accountList.FirstOrDefault(account =>
            string.Equals(account.AccountId, targetAccountId, StringComparison.OrdinalIgnoreCase) ||
            (TryParseContentAccountId(account.AccountId, out var accountContentId) && accountContentId == contentId));
        if (!string.IsNullOrWhiteSpace(existingTarget.AccountId))
        {
            return new AccountSelectionDecision(
                AccountSelectionAction.SelectExisting,
                existingTarget.AccountId,
                string.Empty,
                CopyCurrentCharacterConfig: false);
        }

        if (accountList.Count == 1 && !IsCanonicalContentAccountId(accountList[0].AccountId))
        {
            return new AccountSelectionDecision(
                AccountSelectionAction.MigrateLegacy,
                targetAccountId,
                accountList[0].AccountId,
                CopyCurrentCharacterConfig: false);
        }

        var source = ChooseSourceAccount(accountList, currentAccountId);
        var copyCharacterConfig = hasCurrentCharacterKey && source.HasCurrentCharacter;
        return new AccountSelectionDecision(
            AccountSelectionAction.CreateCanonical,
            targetAccountId,
            source.AccountId ?? string.Empty,
            copyCharacterConfig);
    }

    private static AccountSelectionInput ChooseSourceAccount(
        IReadOnlyList<AccountSelectionInput> accounts,
        string currentAccountId)
    {
        if (accounts.Count == 0)
            return default;

        var characterSource = accounts.FirstOrDefault(account => account.HasCurrentCharacter);
        if (!string.IsNullOrWhiteSpace(characterSource.AccountId))
            return characterSource;

        var currentSource = accounts.FirstOrDefault(
            account => string.Equals(account.AccountId, currentAccountId, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(currentSource.AccountId))
            return currentSource;

        return accounts[0];
    }
}
