using System;

namespace VERMAXION.Models;

public enum AutomationDashboardSection
{
    DueNow,
    Blocked,
    ScheduledLater,
    Complete,
}

public enum ConfigurationSection
{
    EveryAr,
    Weekly,
    Daily,
    VariableTime,
    Wip,
}

public static class AutomationDashboardPolicy
{
    public static AutomationDashboardSection Classify(
        TaskEligibilityStatus status,
        bool completed,
        string? automationId = null,
        string? reason = null)
    {
        if (completed && status is TaskEligibilityStatus.NotDue or TaskEligibilityStatus.Blocked)
            return AutomationDashboardSection.Complete;

        return status switch
        {
            TaskEligibilityStatus.Runnable => AutomationDashboardSection.DueNow,
            TaskEligibilityStatus.Blocked when
                automationId == AutomationCatalog.FashionReport &&
                reason?.Contains("outside", StringComparison.OrdinalIgnoreCase) == true => AutomationDashboardSection.ScheduledLater,
            TaskEligibilityStatus.Blocked or TaskEligibilityStatus.Unsupported => AutomationDashboardSection.Blocked,
            _ => AutomationDashboardSection.ScheduledLater,
        };
    }

    public static string GetStateLabel(AutomationDashboardSection section)
        => section switch
        {
            AutomationDashboardSection.DueNow => "Due now",
            AutomationDashboardSection.Blocked => "Blocked",
            AutomationDashboardSection.ScheduledLater => "Scheduled later",
            AutomationDashboardSection.Complete => "Complete",
            _ => section.ToString(),
        };

    public static ConfigurationSection? GetRecoverySection(string automationId, string reason)
        => automationId switch
        {
            AutomationCatalog.VendorStock or
            AutomationCatalog.RegisterRegistrables or
            AutomationCatalog.RetainerEquipping => ConfigurationSection.EveryAr,
            AutomationCatalog.AlliedSociety => ConfigurationSection.Daily,
            AutomationCatalog.AfterArPark => ConfigurationSection.EveryAr,
            AutomationCatalog.NagYourMom when
                reason.Contains("job", StringComparison.OrdinalIgnoreCase) ||
                reason.Contains("window", StringComparison.OrdinalIgnoreCase) => ConfigurationSection.Daily,
            AutomationCatalog.NagYourDad when
                reason.Contains("select", StringComparison.OrdinalIgnoreCase) ||
                reason.Contains("configured", StringComparison.OrdinalIgnoreCase) => ConfigurationSection.VariableTime,
            _ => null,
        };
}
