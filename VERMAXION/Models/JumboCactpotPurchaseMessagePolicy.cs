using System.Globalization;
using System.Text.RegularExpressions;

namespace VERMAXION.Models;

internal static class JumboCactpotPurchaseMessagePolicy
{
    private static readonly Regex PurchaseMessageRegex = new(
        @"^You use [0-9]+(?:,[0-9]{3})* MGP to purchase a Jumbo Cactpot ticket with the numbers (?<number>[0-9]{4})\.$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool TryParsePurchasedNumber(string chatType, string speaker, string? message, out int number)
    {
        number = 0;

        if (chatType != "SystemMessage" || !string.IsNullOrEmpty(speaker) || string.IsNullOrWhiteSpace(message))
            return false;

        var match = PurchaseMessageRegex.Match(message.Trim());
        return match.Success &&
               int.TryParse(match.Groups["number"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out number);
    }
}

internal static class JumboCactpotPurchaseConfirmationPolicy
{
    private static readonly Regex NextTicketPromptRegex = new(
        @"^Buy another ticket\?\s*It'll cost you [0-9]+(?:,[0-9]{3})* MGP\.$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool ShouldConfirmNextTicketPrompt(string? promptText)
        => promptText != null &&
           (NextTicketPromptRegex.IsMatch(promptText.Trim()) ||
            ShouldConfirmPurchasePrompt(promptText, allowUnreadable: false));

    public static bool ShouldConfirmPurchasePrompt(string? promptText, bool allowUnreadable)
    {
        if (string.IsNullOrWhiteSpace(promptText))
            return allowUnreadable;

        var prompt = promptText.Trim();
        if (!prompt.Contains("Jumbo Cactpot", System.StringComparison.OrdinalIgnoreCase))
            return false;

        return prompt.Contains("purchase", System.StringComparison.OrdinalIgnoreCase) ||
               prompt.Contains("another", System.StringComparison.OrdinalIgnoreCase);
    }
}
