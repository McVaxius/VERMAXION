namespace VERMAXION.Models;

public readonly record struct SetupWizardMigrationDecision(
    bool Completed,
    bool Migrated,
    bool ShouldAutoOpen);

public static class SetupWizardMigrationPolicy
{
    public static SetupWizardMigrationDecision Decide(
        bool hasStoredConfiguration,
        bool stateMigrated,
        bool completed)
    {
        if (stateMigrated)
            return new SetupWizardMigrationDecision(completed, true, false);

        return new SetupWizardMigrationDecision(
            Completed: hasStoredConfiguration,
            Migrated: true,
            ShouldAutoOpen: !hasStoredConfiguration);
    }
}
