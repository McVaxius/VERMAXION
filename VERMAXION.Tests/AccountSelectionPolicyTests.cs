using VERMAXION.Models;
using Xunit;

namespace VERMAXION.Tests;

public sealed class AccountSelectionPolicyTests
{
    [Fact]
    public void DuplicateCharacterMembershipSelectsLargestAccount()
    {
        var decision = AccountSelectionPolicy.Select(
            [
                new AccountSelectionInput("4000174C01C65D", HasCurrentCharacter: true, CharacterCount: 87),
                new AccountSelectionInput("4000174C45A8FE", HasCurrentCharacter: true, CharacterCount: 1),
            ],
            currentAccountId: "4000174C45A8FE",
            hasCurrentCharacterKey: true);

        Assert.Equal(AccountSelectionAction.SelectExisting, decision.Action);
        Assert.Equal("4000174C01C65D", decision.TargetAccountId);
    }

    [Fact]
    public void RepeatedKnownCharacterLoginDoesNotRequestNewAccount()
    {
        var first = AccountSelectionPolicy.Select(
            [new AccountSelectionInput("4000174C01C65D", HasCurrentCharacter: true, CharacterCount: 87)],
            currentAccountId: "4000174C01C65D",
            hasCurrentCharacterKey: true);
        var second = AccountSelectionPolicy.Select(
            [new AccountSelectionInput("4000174C01C65D", HasCurrentCharacter: true, CharacterCount: 87)],
            currentAccountId: "4000174C01C65D",
            hasCurrentCharacterKey: true);

        Assert.Equal(AccountSelectionAction.SelectExisting, first.Action);
        Assert.Equal(AccountSelectionAction.SelectExisting, second.Action);
        Assert.Equal(first.TargetAccountId, second.TargetAccountId);
    }

    [Fact]
    public void UnknownCharacterStaysOnCurrentValidAccount()
    {
        var decision = AccountSelectionPolicy.Select(
            [
                new AccountSelectionInput("primary", HasCurrentCharacter: false, CharacterCount: 87),
                new AccountSelectionInput("secondary", HasCurrentCharacter: false, CharacterCount: 1),
            ],
            currentAccountId: "secondary",
            hasCurrentCharacterKey: true);

        Assert.Equal(AccountSelectionAction.SelectExisting, decision.Action);
        Assert.Equal("secondary", decision.TargetAccountId);
    }

    [Fact]
    public void UnknownCharacterWithoutCurrentAccountUsesLargestExistingAccount()
    {
        var decision = AccountSelectionPolicy.Select(
            [
                new AccountSelectionInput("small", HasCurrentCharacter: false, CharacterCount: 1),
                new AccountSelectionInput("primary", HasCurrentCharacter: false, CharacterCount: 108),
            ],
            currentAccountId: "missing",
            hasCurrentCharacterKey: true);

        Assert.Equal(AccountSelectionAction.SelectExisting, decision.Action);
        Assert.Equal("primary", decision.TargetAccountId);
    }

    [Fact]
    public void EmptyAccountSetRequestsExplicitNewAccountCreation()
    {
        var decision = AccountSelectionPolicy.Select(
            [],
            currentAccountId: string.Empty,
            hasCurrentCharacterKey: true);

        Assert.Equal(AccountSelectionAction.CreateNew, decision.Action);
        Assert.Equal(string.Empty, decision.TargetAccountId);
    }
}
