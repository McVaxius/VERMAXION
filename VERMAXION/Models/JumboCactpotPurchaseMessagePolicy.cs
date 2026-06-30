using System.Globalization;
using System.Text.RegularExpressions;

namespace VERMAXION.Models;

internal static class JumboCactpotPurchaseMessagePolicy
{
    private static readonly Regex PurchaseMessageRegex = new(
        @"^You use 100 MGP to purchase a Jumbo Cactpot ticket with the numbers (?<number>\d{4})\.$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool TryParsePurchasedNumber(string? message, out int number)
    {
        number = 0;

        if (string.IsNullOrWhiteSpace(message))
            return false;

        var match = PurchaseMessageRegex.Match(message.Trim());
        return match.Success &&
               int.TryParse(match.Groups["number"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out number);
    }
}
