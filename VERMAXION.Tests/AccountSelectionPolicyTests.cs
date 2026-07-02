using VERMAXION.Models;
using Xunit;

namespace VERMAXION.Tests;

public sealed class AccountSelectionPolicyTests
{
    [Fact]
    public void NoncanonicalSingleAccountMigratesToFirstContentId()
    {
        var decision = AccountSelectionPolicy.Select(
            [new AccountSelectionInput("legacy-account", HasCurrentCharacter: false)],
            0x1234ABCD,
            currentAccountId: "legacy-account",
            hasCurrentCharacterKey: false);

        Assert.Equal(AccountSelectionAction.MigrateLegacy, decision.Action);
        Assert.Equal("1234ABCD", decision.TargetAccountId);
        Assert.Equal("legacy-account", decision.SourceAccountId);
    }

    [Fact]
    public void CanonicalSingleAccountPlusDifferentContentIdCreatesNewCanonicalAccount()
    {
        var existingCanonical = AccountSelectionPolicy.GetCanonicalAccountId(0x1111);

        var decision = AccountSelectionPolicy.Select(
            [new AccountSelectionInput(existingCanonical, HasCurrentCharacter: false)],
            0x2222,
            currentAccountId: existingCanonical,
            hasCurrentCharacterKey: false);

        Assert.Equal(AccountSelectionAction.CreateCanonical, decision.Action);
        Assert.Equal("2222", decision.TargetAccountId);
        Assert.Equal(existingCanonical, decision.SourceAccountId);
    }

    [Fact]
    public void ExistingTargetAccountIsSelectedUnchanged()
    {
        var decision = AccountSelectionPolicy.Select(
            [
                new AccountSelectionInput("AAAA", HasCurrentCharacter: false),
                new AccountSelectionInput("BBBB", HasCurrentCharacter: true),
            ],
            0xAAAA,
            currentAccountId: "BBBB",
            hasCurrentCharacterKey: true);

        Assert.Equal(AccountSelectionAction.SelectExisting, decision.Action);
        Assert.Equal("AAAA", decision.TargetAccountId);
        Assert.False(decision.CopyCurrentCharacterConfig);
    }

    [Fact]
    public void PaddedContentIdAccountIsSelectedAsExistingCanonicalAccount()
    {
        var decision = AccountSelectionPolicy.Select(
            [new AccountSelectionInput("000000000000AAAA", HasCurrentCharacter: false)],
            0xAAAA,
            currentAccountId: "000000000000AAAA",
            hasCurrentCharacterKey: false);

        Assert.Equal(AccountSelectionAction.SelectExisting, decision.Action);
        Assert.Equal("000000000000AAAA", decision.TargetAccountId);
    }

    [Fact]
    public void CurrentCharacterConfigIsCopiedFromOtherAccountWhenCreatingCanonicalAccount()
    {
        var decision = AccountSelectionPolicy.Select(
            [
                new AccountSelectionInput("1111", HasCurrentCharacter: false),
                new AccountSelectionInput("2222", HasCurrentCharacter: true),
            ],
            0x3333,
            currentAccountId: "1111",
            hasCurrentCharacterKey: true);

        Assert.Equal(AccountSelectionAction.CreateCanonical, decision.Action);
        Assert.Equal("3333", decision.TargetAccountId);
        Assert.Equal("2222", decision.SourceAccountId);
        Assert.True(decision.CopyCurrentCharacterConfig);
    }
}
