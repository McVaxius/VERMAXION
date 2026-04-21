namespace VERMAXION;

public static class UIConstants
{
    public static class ConfigLabels
    {
        // Global Settings
        public const string KrangleNames = "Krangle Names";
        public const string DtrBarEntry = "DTR Bar Entry";
        
        // Character Settings
        public const string Enabled = "Enabled";
        public const string FCBuffRefill = "FC Buff Refill (Seal Sweetener)";
        public const string MaxPurchaseAttempts = "Max Purchase Attempts";
        public const string MinFCPoints = "Min FC Points";
        public const string MinGil = "Min Gil";
        public const string HenchmanManagement = "Henchman Disable/Enable";
        public const string MinionRoulette = "Minion Roulette";
        public const string SeasonalGearRoulette = "Seasonal Gear Roulette";
        public const string GearUpdater = "Gear Updater";
        public const string VerminionQueue = "Lord of Verminion (5 fails)";
        public const string JumboCactpot = "Jumbo Cactpot (auto DC timing)";
        public const string MiniCactpot = "Mini Cactpot";
        public const string ChocoboRacing = "Chocobo Racing (via Chocoholic)";
        public const string NagYourMom = "nag your mom";
        public const string NagYourMomRunsPerDay = "mom runs per day";
        public const string NagYourMomJob = "mom job";
        public const string NagYourMomWindowStartLocal = "Local start (HH:mm)";
        public const string NagYourMomWindowEndLocal = "Local end (HH:mm)";
        public const string NagYourMomStopAtSeriesRank25 = "Stop at series rank 25";
        public const string NagYourDad = "nag your dad";
        public const string NagYourDadDungeonCount = "dad dungeon count";
        public const string NagYourDadDungeonFrequency = "dad dungeon frequency";
        public const string NagYourDadDungeonName = "dad dungeon";
        public const string NagYourDadDungeonJob = "dad dungeon job";
        public const string NagYourDadQueueViaLanParty = "QUEUE via LAN PARTY module";
        public const string NagYourDadDungeonUnsynced = "Run dungeon unsynced";
        public const string NagYourDadDailyMsq = "Run daily MSQ via LAN Party";
        public const string NagYourDadLanPartyPreset = "LAN Party preset";
        public const string NagYourDadCommendationAttempts = "Commendation attempts";
        public const string NagYourDadAstropeAttempts = "Astrope attempts";
        public const string NagYourDadWindowStartLocal = "Astrope local start (HH:mm)";
        public const string NagYourDadWindowEndLocal = "Astrope local end (HH:mm)";
        public const string RacesPerDay = "Races Per Day";
        public const string SkipChocoboRacingIfLevel50 = "Don't race if racing chocobo is level 50";
        
        // Section Headers
        public const string GlobalSettings = "Global Settings";
        public const string EveryARPostProcess = "Every AR PostProcess";
        public const string WeeklyTasks = "Weekly Tasks";
        public const string DailyTasks = "Daily Tasks";
        
        // Other Labels
        public const string Account = "Account:";
        public const string Characters = "Characters";
        public const string Settings = "Settings";
        public const string NewCharactersInheritThese = "New characters inherit these settings";
        public const string AccountAlias = "Account Alias:";
        public const string Save = "Save";
    }
    
    public static class Tooltips
    {
        public const string KrangleNames = "Replace character names with exercise words for screenshots";
        public const string HenchmanManagement = "Stop Henchman before tasks, restart after";
        public const string MinionRoulette = "Fire off /minion roulette once per AR postprocess";
        public const string SeasonalGearRoulette = "Randomly equip seasonal event gear for a fun ensemble each AR run";
        public const string GearUpdater = "Cycle through all unlocked jobs: auto equip recommended gear and save gearset (2s intervals)";
        public const string NagYourMom = "AR-only mom task. Evaluated only during the normal VERMAXION post-process flow, gated by a local time window and daily attempt count.";
        public const string NagYourDad = "AR-only dad task. VERMAXION packages configured dungeon, MSQ, commendation, and Astrope asks into one IPC payload and retries on the next AR pass if dad is unavailable.";
    }
}
