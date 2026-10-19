namespace VERMAXION.Beastmaster;

// Only run choices are independent; collection, priorities and supplies stay shared.
public sealed class BeastmasterRunSetup
{
    public BeastmasterRunGoal Goal { get; set; }
    public uint Board { get; set; } = 1;
    public int RunCount { get; set; } = 1;
    public string CompletionCommand { get; set; } = string.Empty;
    public CrucibleDegree Degree { get; set; }
    public CrucibleTeamMode TeamMode { get; set; }
    public CrucibleLevelingMode LevelingMode { get; set; } = CrucibleLevelingMode.Minus_3;
    public int RecommendedTeamCap { get; set; } = 15;
    public int ClassificationLock { get; set; }

    public static BeastmasterRunSetup From(BeastmasterSettings settings) => new()
    {
        Goal = settings.Goal, Board = settings.Board, RunCount = settings.RunCount,
        CompletionCommand = settings.CompletionCommand, Degree = settings.Degree,
        TeamMode = settings.TeamMode, LevelingMode = settings.LevelingMode,
        RecommendedTeamCap = settings.RecommendedTeamCap, ClassificationLock = settings.ClassificationLock,
    };

    public BeastmasterSettings ApplyTo(BeastmasterSettings shared)
    {
        var settings = shared.Clone();
        settings.Goal = Goal; settings.Board = Board; settings.RunCount = RunCount;
        settings.TrainingTargetRank = 0;
        settings.CompletionCommand = CompletionCommand; settings.Degree = Degree;
        settings.TeamMode = TeamMode; settings.LevelingMode = LevelingMode;
        settings.RecommendedTeamCap = RecommendedTeamCap; settings.ClassificationLock = ClassificationLock;
        return settings;
    }

    public BeastmasterRunSetup Clone() => (BeastmasterRunSetup)MemberwiseClone();
}
