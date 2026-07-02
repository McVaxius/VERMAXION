using System;
using System.Collections.Generic;
using System.Linq;

namespace VERMAXION.Models;

internal enum AccountSelectionAction
{
    SelectExisting,
    CreateNew,
}

internal readonly record struct AccountSelectionInput(
    string AccountId,
    bool HasCurrentCharacter,
    int CharacterCount);

internal readonly record struct AccountSelectionDecision(
    AccountSelectionAction Action,
    string TargetAccountId,
    string Reason);

internal static class AccountSelectionPolicy
{
    public static AccountSelectionDecision Select(
        IEnumerable<AccountSelectionInput> accounts,
        string currentAccountId,
        bool hasCurrentCharacterKey)
    {
        var accountList = accounts
            .Where(account => !string.IsNullOrWhiteSpace(account.AccountId))
            .GroupBy(account => account.AccountId, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var grouped = group.ToList();
                return new AccountSelectionInput(
                    group.Key.Trim(),
                    grouped.Any(account => account.HasCurrentCharacter),
                    grouped.Max(account => Math.Max(0, account.CharacterCount)));
            })
            .ToList();

        if (accountList.Count == 0)
        {
            return new AccountSelectionDecision(
                AccountSelectionAction.CreateNew,
                string.Empty,
                "No account config exists.");
        }

        if (hasCurrentCharacterKey)
        {
            var membershipAccount = accountList
                .Where(account => account.HasCurrentCharacter)
                .OrderByDescending(account => Math.Max(0, account.CharacterCount))
                .ThenBy(account => account.AccountId, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(membershipAccount.AccountId))
            {
                return new AccountSelectionDecision(
                    AccountSelectionAction.SelectExisting,
                    membershipAccount.AccountId,
                    "Selected account containing the current character.");
            }
        }

        var currentAccount = accountList.FirstOrDefault(
            account => string.Equals(account.AccountId, currentAccountId, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(currentAccount.AccountId))
        {
            return new AccountSelectionDecision(
                AccountSelectionAction.SelectExisting,
                currentAccount.AccountId,
                "Selected current account because the character is not configured elsewhere.");
        }

        var largestAccount = accountList
            .OrderByDescending(account => Math.Max(0, account.CharacterCount))
            .ThenBy(account => account.AccountId, StringComparer.OrdinalIgnoreCase)
            .First();

        return new AccountSelectionDecision(
            AccountSelectionAction.SelectExisting,
            largestAccount.AccountId,
            "Selected largest existing account because no current account was valid.");
    }
}
