using VERMAXION.Models;
using Xunit;

namespace VERMAXION.Tests;

public sealed class JumboCactpotPurchaseMessagePolicyTests
{
    [Theory]
    [InlineData("You use 100 MGP to purchase a Jumbo Cactpot ticket with the numbers 2757.", 2757)]
    [InlineData("You use 100 MGP to purchase a Jumbo Cactpot ticket with the numbers 0001.", 1)]
    public void JumboPurchaseSystemMessagesAreAccepted(string message, int expectedNumber)
    {
        Assert.True(JumboCactpotPurchaseMessagePolicy.TryParsePurchasedNumber(message, out var number));
        Assert.Equal(expectedNumber, number);
    }

    [Theory]
    [InlineData("")]
    [InlineData("You use 100 MGP to purchase a Mini Cactpot ticket.")]
    [InlineData("You use 100 MGP to purchase a Jumbo Cactpot ticket.")]
    [InlineData("You use 100 MGP to purchase a Jumbo Cactpot ticket with the numbers 2757")]
    [InlineData("You use 100 MGP to purchase a Jumbo Cactpot ticket with the numbers 275.")]
    [InlineData("Welcome to drawing number 669 of the Jumbo Cactpot! Can I interest you in a ticket to fame and fortune?")]
    public void NonJumboPurchaseMessagesAreRejected(string message)
    {
        Assert.False(JumboCactpotPurchaseMessagePolicy.TryParsePurchasedNumber(message, out _));
    }
}
