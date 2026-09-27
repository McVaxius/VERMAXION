using VERMAXION.Models;
using Xunit;

namespace VERMAXION.Tests;

public sealed class JumboCactpotPurchaseMessagePolicyTests
{
    [Theory]
    [InlineData("You use 100 MGP to purchase a Jumbo Cactpot ticket with the numbers 2757.", 2757)]
    [InlineData("You use 100 MGP to purchase a Jumbo Cactpot ticket with the numbers 0001.", 1)]
    [InlineData("You use 150 MGP to purchase a Jumbo Cactpot ticket with the numbers 0508.", 508)]
    [InlineData("You use 200 MGP to purchase a Jumbo Cactpot ticket with the numbers 0000.", 0)]
    [InlineData("You use 1,250 MGP to purchase a Jumbo Cactpot ticket with the numbers 9999.", 9999)]
    [InlineData("You use 75 MGP to purchase a Jumbo Cactpot ticket with the numbers 1234.", 1234)]
    public void JumboPurchaseSystemMessagesAreAccepted(string message, int expectedNumber)
    {
        Assert.True(JumboCactpotPurchaseMessagePolicy.TryParsePurchasedNumber("SystemMessage", string.Empty, message, out var number));
        Assert.Equal(expectedNumber, number);
    }

    [Theory]
    [InlineData("")]
    [InlineData("You use 100 MGP to purchase a Mini Cactpot ticket.")]
    [InlineData("You use 100 MGP to purchase a Jumbo Cactpot ticket.")]
    [InlineData("You use 100 MGP to purchase a Jumbo Cactpot ticket with the numbers 2757")]
    [InlineData("You use 100 MGP to purchase a Jumbo Cactpot ticket with the numbers 275.")]
    [InlineData("You use 200 MGP to purchase a Jumbo Cactpot ticket with the numbers 02757.")]
    [InlineData("You use MGP to purchase a Jumbo Cactpot ticket with the numbers 0508.")]
    [InlineData("You use 150 gil to purchase a Jumbo Cactpot ticket with the numbers 0508.")]
    [InlineData("You use 150 MGP to purchase a Jumbo Cactpot ticket with the numbers ０５０８.")]
    [InlineData("Player says: You use 150 MGP to purchase a Jumbo Cactpot ticket with the numbers 0508.")]
    [InlineData(null)]
    [InlineData("Welcome to drawing number 669 of the Jumbo Cactpot! Can I interest you in a ticket to fame and fortune?")]
    public void NonJumboPurchaseMessagesAreRejected(string? message)
    {
        Assert.False(JumboCactpotPurchaseMessagePolicy.TryParsePurchasedNumber("SystemMessage", string.Empty, message, out _));
    }

    [Theory]
    [InlineData("Say", "Test Player")]
    [InlineData("NPCDialogue", "Jumbo Cactpot Broker")]
    [InlineData("ErrorMessage", "")]
    [InlineData("SystemMessage", "Test Player")]
    public void PurchaseReceiptsRequireSystemMetadata(string chatType, string speaker)
    {
        Assert.False(JumboCactpotPurchaseMessagePolicy.TryParsePurchasedNumber(
            chatType, speaker, "You use 150 MGP to purchase a Jumbo Cactpot ticket with the numbers 0508.", out _));
    }

    [Theory]
    [InlineData("Use 100 MGP to purchase a Jumbo Cactpot ticket?", true)]
    [InlineData("Purchase a Jumbo Cactpot ticket with the number 0508 for 200 MGP?", true)]
    [InlineData("Purchase another Jumbo Cactpot ticket?", true)]
    [InlineData("Welcome to drawing number 669 of the Jumbo Cactpot! Can I interest you in a ticket to fame and fortune?", false)]
    [InlineData("Purchase a Mini Cactpot ticket?", false)]
    [InlineData("", false)]
    public void JumboPurchaseConfirmationPolicyGuardsPromptText(string prompt, bool expected)
    {
        Assert.Equal(expected, JumboCactpotPurchaseConfirmationPolicy.ShouldConfirmPurchasePrompt(prompt, allowUnreadable: false));
    }

    [Theory]
    [InlineData("Buy another ticket?\nIt'll cost you 150 MGP.", true)]
    [InlineData("Buy another ticket?It'll cost you 200 MGP.", true)]
    [InlineData("Purchase another Jumbo Cactpot ticket?", true)]
    [InlineData("Claim more prizes?", false)]
    [InlineData("Purchase a Mini Cactpot ticket?", false)]
    [InlineData("Buy another ticket?", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void NextTicketConfirmationAcceptsTheNativeContinuationPrompt(string? prompt, bool expected)
    {
        Assert.Equal(expected, JumboCactpotPurchaseConfirmationPolicy.ShouldConfirmNextTicketPrompt(prompt));
    }

    [Fact]
    public void JumboPurchaseConfirmationPolicyOnlyAllowsUnreadableWhenExplicitlyAllowed()
    {
        Assert.False(JumboCactpotPurchaseConfirmationPolicy.ShouldConfirmPurchasePrompt("", allowUnreadable: false));
        Assert.True(JumboCactpotPurchaseConfirmationPolicy.ShouldConfirmPurchasePrompt("", allowUnreadable: true));
    }
}
