using System;
using System.Collections.Generic;
using System.Linq;

namespace VERMAXION.Models;

internal enum AccountSelectionAction
{
    SelectExisting,
    CreateNew,
    RefuseUnreadable,
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
        bool hasCurrentCharacterKey,
        bool hasUnreadableAccounts = false)
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
                hasUnreadableAccounts
                    ? AccountSelectionAction.RefuseUnreadable
                    : AccountSelectionAction.CreateNew,
                string.Empty,
                hasUnreadableAccounts
                    ? "No readable account config exists."
                    : "No account config exists.");
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

        if (hasUnreadableAccounts)
        {
            return new AccountSelectionDecision(
                AccountSelectionAction.RefuseUnreadable,
                string.Empty,
                "No current readable account can be selected while an account config is unreadable.");
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
