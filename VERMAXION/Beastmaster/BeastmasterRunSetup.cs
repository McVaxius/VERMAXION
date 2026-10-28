namespace VERMAXION.Beastmaster;

// Only run choices are independent; collection, priorities and supplies stay shared.
public sealed class BeastmasterRunSetup
{
    public BeastmasterRunGoal Goal { get; set; }
    public uint Board { get; set; } = 1;
    public int RunCount { get; set; } = 1;
    public string CompletionCommand { get; set; } = string.Empty;
    public CrucibleDegree Degree { get; set; }
    public bool RankedEntry { get; set; } = true;
    public CrucibleTeamMode TeamMode { get; set; }
    public CrucibleLevelingMode LevelingMode { get; set; } = CrucibleLevelingMode.Minus_3;
    public int RecommendedTeamCap { get; set; } = 15;
    public int ClassificationLock { get; set; }

    public static BeastmasterRunSetup From(BeastmasterSettings settings) => new()
    {
        Goal = settings.Goal, Board = settings.Board, RunCount = settings.RunCount,
        CompletionCommand = settings.CompletionCommand, Degree = settings.Degree, RankedEntry = settings.RankedEntry,
        TeamMode = settings.TeamMode, LevelingMode = settings.LevelingMode,
        RecommendedTeamCap = settings.RecommendedTeamCap, ClassificationLock = settings.ClassificationLock,
    };

    public BeastmasterSettings ApplyTo(BeastmasterSettings shared)
    {
        var settings = shared.Clone();
        settings.Goal = Goal; settings.Board = Board; settings.RunCount = RunCount;
        settings.TrainingTargetRank = 0;
        settings.CompletionCommand = CompletionCommand; settings.Degree = Degree; settings.RankedEntry = RankedEntry;
        settings.TeamMode = TeamMode; settings.LevelingMode = LevelingMode;
        settings.RecommendedTeamCap = RecommendedTeamCap; settings.ClassificationLock = ClassificationLock;
        return settings;
    }

    public BeastmasterRunSetup Clone() => (BeastmasterRunSetup)MemberwiseClone();

    public static BeastmasterSettings ForBoardTest(BeastmasterSettings shared, uint board)
    {
        if (board is < 1 or > 5) throw new System.ArgumentOutOfRangeException(nameof(board));
        var settings = shared.Clone();
        settings.Goal = BeastmasterRunGoal.FixedRuns;
        settings.Board = board;
        settings.Degree = CrucibleDegree.Third;
        settings.RankedEntry = true;
        settings.RunCount = 1;
        settings.CompletionCommand = string.Empty;
        settings.TrainingTargetRank = 0;
        settings.FoodItemId = 0;
        settings.ExtractMateria = false;
        return settings;
    }
}
