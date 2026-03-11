using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Logging;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace VERMAXION.Services;

// [WIP] - Implementation for highest combat job selector, needs SimpleTweaks testing
public class HighestCombatJobService : IDisposable
{
    private readonly ICommandManager commandManager;
    private readonly IPluginLog log;
    private readonly IPlayerState playerState;
    private readonly IClientState clientState;
    private readonly IObjectTable objectTable;
    private readonly IDataManager dataManager;
    
    private DateTime lastAction = DateTime.MinValue;
    private bool isRunning = false;
    
    // Combat job IDs (DOW/DOM only - all combat jobs 1-100 excluding DOH/DOL)
    private static readonly uint[] CombatJobs = GenerateCombatJobIds();
    
    private static uint[] GenerateCombatJobIds()
    {
        var jobs = new List<uint>();
        
        // Add all job IDs 1-100, excluding DOH/DOL
        for (uint i = 1; i <= 100; i++)
        {
            if (!IsDoLorDoH(i))
            {
                jobs.Add(i);
            }
        }
        
        return jobs.ToArray();
    }

    public HighestCombatJobService(ICommandManager commandManager, IPluginLog log, IPlayerState playerState, IClientState clientState, IObjectTable objectTable, IDataManager dataManager)
    {
        this.commandManager = commandManager;
        this.log = log;
        this.playerState = playerState;
        this.clientState = clientState;
        this.objectTable = objectTable;
        this.dataManager = dataManager;
    }

    public void RunTask()
    {
        if (isRunning)
        {
            log.Information("[HighestCombatJob] Already running");
            return;
        }

        log.Information("[HighestCombatJob] Starting highest combat job selection");
        isRunning = true;
        lastAction = DateTime.UtcNow;

        // Direct detection - no job switching needed
        SelectHighestCombatJob();
        isRunning = false;
        log.Information("[HighestCombatJob] Task complete");
    }

    private static bool IsDoLorDoH(uint jobId)
    {
        // DoL jobs: 16 (MIN), 17 (BOT), 18 (FSH)
        // DoH jobs: 8 (CRP), 9 (BSM), 10 (ARM), 11 (GSM), 12 (LTW), 13 (WVR), 14 (ALC), 15 (CUL)
        return (jobId >= 8 && jobId <= 18);
    }

    private void SelectHighestCombatJob()
    {
        var highestJob = GetHighestCombatJob();
        if (highestJob == null)
        {
            log.Warning("[HighestCombatJob] No combat jobs found");
            return;
        }

        log.Information($"[HighestCombatJob] Highest combat job: {highestJob.Name} (Level {highestJob.Level}, ID {highestJob.JobId})");
        
        // Check if this is a job stone (IDs >= 19) and try base class first
        if (highestJob.JobId >= 19)
        {
            var baseClassId = GetBaseClassId(highestJob.JobId);
            if (baseClassId.HasValue)
            {
                var baseCommand = GetEquipJobCommand(baseClassId.Value);
                log.Information($"[HighestCombatJob] Trying base class first: {GetJobName(baseClassId.Value)} (/equipjob {baseCommand})");
                CommandHelper.SendCommand($"/equipjob {baseCommand}");
                
                // Wait 2 seconds before trying job stone
                System.Threading.Thread.Sleep(2000);
                log.Information($"[HighestCombatJob] Now trying job stone: {highestJob.Name} (/equipjob {GetEquipJobCommand(highestJob.JobId)})");
            }
        }
        
        // Send command to switch to the highest combat job using equipjob
        var jobCommand = GetEquipJobCommand(highestJob.JobId);
        CommandHelper.SendCommand($"/equipjob {jobCommand}");
        log.Information($"[HighestCombatJob] Sent command to switch to {highestJob.Name} (/equipjob {jobCommand})");
    }

    private CombatJobInfo? GetHighestCombatJob()
    {
        var combatJobs = new List<CombatJobInfo>();
        
        // Get all combat jobs and their levels
        foreach (var jobId in CombatJobs)
        {
            var level = GetJobLevel(jobId);
            if (level > 0) // Job is unlocked
            {
                var jobName = GetJobName(jobId);
                combatJobs.Add(new CombatJobInfo(jobId, jobName, level));
            }
        }

        if (!combatJobs.Any())
        {
            return null;
        }

        // Find the job with the highest level
        var highestJob = combatJobs
            .OrderByDescending(j => j.Level)
            .ThenBy(j => j.JobId) // Tie-breaker by job ID
            .First();

        return highestJob;
    }

    private int GetJobLevel(uint jobId)
    {
        try
        {
            log.Debug($"[HighestCombatJob] Getting level for job {jobId}");
            
            // Method 1: Try to access through PlayerState (current job only)
            uint currentJobId = 0;
            var classJob = playerState?.ClassJob;
            if (classJob.HasValue)
            {
                currentJobId = classJob.Value.RowId;
            }
            
            if (currentJobId == jobId)
            {
                var currentLevel = (int)(playerState?.Level ?? 0);
                log.Debug($"[HighestCombatJob] Job {jobId} is current, level: {currentLevel}");
                return currentLevel;
            }
            
            // Method 2: Access ALL job levels through PlayerState.GetClassJobLevel()
            // This is the equivalent of FUTA's Player.GetJob(i).Level
            unsafe
            {
                var playerStateInstance = PlayerState.Instance();
                if (playerStateInstance == null)
                {
                    log.Debug($"[HighestCombatJob] PlayerState.Instance() null for job {jobId}");
                    return 0;
                }

                // Use the public GetClassJobLevel method
                // shouldGetSynced = false to get actual level, not synced level
                var level = (int)playerStateInstance->GetClassJobLevel((int)jobId, false);
                log.Debug($"[HighestCombatJob] Job {jobId} level from PlayerState.GetClassJobLevel: {level}");
                return level;
            }
        }
        catch (Exception ex)
        {
            log.Error($"[HighestCombatJob] Error getting job level for {jobId}: {ex.Message}");
            return 0;
        }
    }

    private string GetEquipJobCommand(uint jobId)
    {
        return jobId switch
        {
            // Base classes and their job stones
            1 or 19 => "pld",  // GLA -> PLD
            2 or 20 => "mnk",  // PGL -> MNK
            3 or 21 => "war",  // MRD -> WAR
            4 or 22 => "drg",  // ARC -> DRG
            5 or 23 => "brd",  // LNC -> BRD
            6 or 24 => "whm",  // CNJ -> WHM
            7 or 25 => "blm",  // THM -> BLM
            26 => "acn",      // Arcanist
            27 => "smn",      // Summoner
            28 => "sch",      // Scholar
            29 or 37 => "nin", // ROG -> NIN
            30 => "mch",      // Machinist
            31 => "drk",      // Dark Knight
            32 => "ast",      // Astrologian
            33 => "sam",      // Samurai
            34 => "rpr",      // Reaper
            35 => "sge",      // Sage
            
            // Other jobs (if any)
            36 => "pld",      // Fallback for any other
            38 => "mnk",
            39 => "war",
            40 => "drg",
            41 => "brd",
            42 => "whm",
            43 => "blm",
            
            _ => "pld" // fallback
        };
    }
    
    private uint? GetBaseClassId(uint jobId)
    {
        return jobId switch
        {
            19 => 1,  // PLD -> GLA
            20 => 2,  // MNK -> PGL
            21 => 3,  // WAR -> MRD
            22 => 4,  // DRG -> ARC
            23 => 5,  // BRD -> LNC
            24 => 6,  // WHM -> CNJ
            25 => 7,  // BLM -> THM
            27 => 26, // SMN -> ACN
            28 => 26, // SCH -> ACN
            37 => 29, // NIN -> ROG
            _ => null // No base class or already a base class
        };
    }

    private bool IsCombatJob(uint jobId)
    {
        // Check if this is a combat job (DOW/DOM)
        return CombatJobs.Contains(jobId);
    }

    private string GetJobName(uint jobId)
    {
        return jobId switch
        {
            1 or 26 => "Paladin",
            2 or 27 => "Monk", 
            3 or 28 => "Warrior",
            4 or 29 => "Dragoon",
            5 or 30 => "Bard",
            6 or 31 => "White Mage",
            7 or 32 => "Black Mage",
            33 => "Arcanist",
            34 => "Summoner",
            35 => "Scholar",
            36 or 37 => "Ninja",
            38 => "Machinist",
            39 => "Dark Knight",
            40 => "Astrologian",
            41 => "Samurai",
            42 => "Reaper",
            43 => "Sage",
            _ => $"Job{jobId}"
        };
    }

    public void Dispose()
    {
        isRunning = false;
    }
}

public record CombatJobInfo(uint JobId, string Name, int Level);
