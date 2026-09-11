global using System;
global using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Enums;
using mom.Models;
using mom.Services;
using VERMAXION.IPC;
using VERMAXION.Models;
using Xunit;
using Status = mom.Models.MomRunStatus;

namespace VERMAXION.Tests
{
    public sealed class MomQueueDeadlineTests
    {
        [Theory]
        [InlineData("09:00", "12:00", "2026-09-10T09:00:00", "2026-09-10T12:00:00")]
        [InlineData("22:00", "02:00", "2026-09-10T23:59:59", "2026-09-11T02:00:00")]
        [InlineData("22:00", "02:00", "2026-09-11T00:00:00", "2026-09-11T02:00:00")]
        public void ClosingTimestampFollowsTheCurrentWindow(string start, string end, string now, string closing)
        {
            Assert.True(MomSchedule.TryGetQueueDeadlineUtc(start, end, DateTime.Parse(now), out var deadline));
            Assert.Equal(DateTime.Parse(closing).ToUniversalTime(), deadline);
        }

        [Theory]
        [InlineData("09:00", "12:00", "2026-09-10T12:00:00")]
        [InlineData("22:00", "02:00", "2026-09-11T02:00:00")]
        [InlineData("22:00", "02:00", "2026-09-11T12:00:00")]
        [InlineData("09:00", "09:00", "2026-09-10T09:00:00")]
        [InlineData("25:00", "26:00", "2026-09-10T09:00:00")]
        public void ExactCutoffAndInvalidWindowsCannotDispatch(string start, string end, string now)
            => Assert.False(MomSchedule.TryGetQueueDeadlineUtc(start, end, DateTime.Parse(now), out _));

        [Fact]
        public void QueueStillWaitingEightHoursThirtyNineMinutesAfterClosingIsWithdrawnOnce()
        {
            var f = new Fixture();
            f.Start();
            f.Queue.Register(ContentsFinderQueueState.Queued);
            f.Clock.Now = f.Deadline.AddHours(8).AddMinutes(39);
            f.Coordinator.Update();
            Assert.Equal(1, f.Queue.Withdrawals);
            Assert.True(f.Queue.QueueActionsBlocked);
            Assert.True(f.Coordinator.IsBusy);
            Assert.True(f.Coordinator.CurrentResult.WindowExpired);
            f.Clock.Now = f.Clock.Now.AddHours(24);
            f.Coordinator.Update();
            f.Coordinator.HandleSystemQueueCancellation();
            Assert.Equal(1, f.Queue.Starts);
            Assert.Equal(1, f.Queue.Withdrawals);
            Assert.True(f.Coordinator.IsBusy); // Sending CancelQueue is not confirmation.
            f.ClearQueueAndSettle();
            Assert.Equal(Status.Completed, f.Coordinator.CurrentResult.Status);
            Assert.Equal(0, f.Coordinator.CurrentResult.CompletedRunCount);
            Assert.True(MomSchedule.CompletesCycle(f.ConsumerResult()));
        }

        [Theory]
        [InlineData("job")]
        [InlineData("series")]
        [InlineData("achievement")]
        [InlineData("queue")]
        public void CutoffDuringPreparationNeverStartsAnotherQueue(string stage)
        {
            var f = new Fixture();
            f.Job.Current = stage != "job";
            f.Start(series: stage == "series", route: stage == "achievement" ? "rival-wings" : "casual-cc");
            var started = f.Queue.Starts;
            f.Clock.Now = f.Deadline;
            f.Coordinator.Update();
            f.Clock.Now = f.Clock.Now.AddSeconds(3);
            f.Coordinator.Update();
            Assert.Equal(started, f.Queue.Starts);
            Assert.Equal(0, f.Job.Switches);
            Assert.Equal(Status.Completed, f.Coordinator.CurrentResult.Status);
            Assert.True(f.Coordinator.CurrentResult.WindowExpired);
        }

        [Fact]
        public void VisiblePopAtCutoffWithdrawsWithoutCommencing()
        {
            var f = new Fixture();
            f.Start();
            f.Queue.Register(ContentsFinderQueueState.Ready);
            f.Clock.Now = f.Deadline;
            f.Coordinator.Update();
            Assert.Equal(1, f.Queue.Withdrawals);
            Assert.True(f.Queue.QueueActionsBlocked);
            Assert.Equal(Status.Queued, f.Coordinator.CurrentResult.Status);
        }

        [Fact]
        public void PreexistingAndUnownedQueuesAreNotWithdrawn()
        {
            var f = new Fixture();
            f.Queue.NativeState = ContentsFinderQueueState.Queued;
            Assert.Equal(Status.Rejected, f.Coordinator.StartRun(new MomRunRequest
            { RequestedRunCount = 5, RequestedJob = "PLD", QueueDeadlineUtc = f.Deadline }).Status);
            f.Queue.NativeState = ContentsFinderQueueState.None;
            f.Start();
            // Another caller registered a queue before mom submitted its own Join.
            f.Queue.NativeState = ContentsFinderQueueState.Queued;
            f.Clock.Now = f.Deadline;
            f.Coordinator.Update();
            Assert.Equal(0, f.Queue.Withdrawals);
            Assert.True(f.Coordinator.IsBusy);
        }

        [Fact]
        public void GameCancellationAtCutoffCannotRequeueARecognizedMatch()
        {
            var f = new Fixture();
            f.Start();
            f.Territory.Mode = PvpArenaMode.CrystallineConflict;
            f.Conditions[ConditionFlag.BoundByDuty] = true;
            f.Clock.Now = f.Deadline;
            f.Coordinator.HandleSystemQueueCancellation();
            Assert.Equal(Status.Running, f.Coordinator.CurrentResult.Status);
            Assert.Equal(1, f.Queue.Starts);
        }

        [Fact]
        public void AlreadyExpiredRequestCannotStartPreparationOrQueueing()
        {
            var f = new Fixture();
            f.Clock.Now = f.Deadline;
            f.Start();
            Assert.Equal(0, f.Queue.Starts);
            f.Clock.Now = f.Clock.Now.AddSeconds(3);
            f.Coordinator.Update();
            Assert.Equal(Status.Completed, f.Coordinator.CurrentResult.Status);
            Assert.True(f.Coordinator.CurrentResult.WindowExpired);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void AcceptedTransitionAndActiveMatchFinishBeforeCutoffCompletion(bool expireDuringTransition)
        {
            var f = new Fixture();
            f.Start();
            if (expireDuringTransition)
            {
                f.Queue.Register(ContentsFinderQueueState.Accepted);
                f.Clock.Now = f.Deadline;
                f.Coordinator.Update();
                f.Clock.Now = f.Clock.Now.AddMinutes(4); // Accepted ownership must outlive the old 20-second grace.
                f.Coordinator.Update();
                Assert.True(f.Coordinator.IsBusy);
                Assert.Equal(0, f.Queue.Withdrawals);
            }
            f.EnterMatch();
            f.Clock.Now = f.Deadline.AddMinutes(5);
            f.Coordinator.Update();
            Assert.Equal(Status.Running, f.Coordinator.CurrentResult.Status);
            Assert.True(f.Coordinator.CurrentResult.WindowExpired);
            f.Coordinator.ObserveDutyCompletion();
            f.Coordinator.ObserveDutyCompletion();
            f.ExitMatch();
            f.Clock.Now = f.Clock.Now.AddSeconds(3);
            f.Coordinator.Update();
            f.Coordinator.Update();
            Assert.Equal(Status.Completed, f.Coordinator.CurrentResult.Status);
            Assert.Equal(1, f.Coordinator.CurrentResult.CompletedRunCount);
            Assert.Equal(1, f.Queue.Starts);
            Assert.Equal(0, f.Queue.Withdrawals);
        }

        [Fact]
        public void LeavingWithoutObservedCompletionDoesNotCreditAMatch()
        {
            var f = new Fixture();
            f.Start();
            f.EnterMatch();
            f.Clock.Now = f.Deadline;
            f.ExitMatch();
            f.Clock.Now = f.Clock.Now.AddSeconds(3);
            f.Coordinator.Update();
            Assert.Equal(Status.Completed, f.Coordinator.CurrentResult.Status);
            Assert.Equal(0, f.Coordinator.CurrentResult.CompletedRunCount);
        }

        [Fact]
        public void InFlightRegistrationAndUnreadableWithdrawalRetainOwnership()
        {
            var f = new Fixture();
            f.Start();
            f.Queue.SubmitWithoutAcknowledgement();
            f.Clock.Now = f.Deadline;
            f.Coordinator.Update();
            f.Clock.Now = f.Clock.Now.AddHours(9);
            f.Coordinator.Update();
            Assert.True(f.Coordinator.IsBusy);
            Assert.Equal(1, f.Queue.Starts);
            f.Queue.NativeState = null;
            f.Coordinator.HandleSystemQueueCancellation();
            f.Coordinator.Update();
            Assert.True(f.Coordinator.IsBusy);
            Assert.Contains("confirmed queue withdrawal", f.Coordinator.CurrentResult.Summary);
            f.ClearQueueAndSettle();
            Assert.Equal(Status.Completed, f.Coordinator.CurrentResult.Status);
        }

        [Fact]
        public void FailedNativeWithdrawalDoesNotRetryOrReleaseOwnership()
        {
            var f = new Fixture();
            f.Start();
            f.Queue.Register(ContentsFinderQueueState.Queued);
            f.Queue.ThrowOnWithdraw = true;
            f.Clock.Now = f.Deadline;
            f.Coordinator.Update();
            Assert.Contains("cannot be confirmed", f.Coordinator.CurrentResult.Summary);
            f.Coordinator.Update();
            Assert.True(f.Coordinator.IsBusy);
            Assert.Equal(1, f.Queue.Withdrawals);
        }

        [Fact]
        public void CancellationBeforeCutoffCanRetryButNeverAfterIt()
        {
            var f = new Fixture();
            f.Start();
            f.Coordinator.HandleSystemQueueCancellation();
            Assert.Equal(2, f.Queue.Starts);
            f.Clock.Now = f.Deadline;
            f.Coordinator.HandleSystemQueueCancellation();
            f.Coordinator.Update();
            Assert.Equal(2, f.Queue.Starts);
            Assert.True(f.Coordinator.CurrentResult.WindowExpired);
        }

        [Fact]
        public void BatchSpanningMidnightKeepsItsDeadlineAndCreditsOnlyNewObservedMatches()
        {
            var f = new Fixture();
            f.Clock.Now = new DateTime(2026, 9, 10, 23, 58, 0, DateTimeKind.Utc);
            f.Deadline = f.Clock.Now.Date.AddDays(1).AddHours(2);
            f.Start();
            f.EnterMatch();
            f.Coordinator.ObserveDutyCompletion();
            f.ExitMatch();
            var highWater = 0;
            Assert.Equal(1, MomSchedule.ObserveCompletedRuns(f.ConsumerResult(), ref highWater));
            Assert.Equal(0, MomSchedule.ObserveCompletedRuns(f.ConsumerResult(), ref highWater));
            f.Clock.Now = f.Clock.Now.AddMinutes(4);
            f.EnterMatch();
            f.Coordinator.ObserveDutyCompletion();
            f.Clock.Now = f.Deadline;
            f.ExitMatch();
            f.Clock.Now = f.Clock.Now.AddSeconds(3);
            f.Coordinator.Update();
            var terminal = f.ConsumerResult();
            Assert.Equal(1, MomSchedule.ObserveCompletedRuns(terminal, ref highWater));
            Assert.Equal(0, MomSchedule.ObserveCompletedRuns(terminal, ref highWater));
            Assert.Equal(2, highWater);
            Assert.Equal(5, terminal.RequestedRunCount);
            Assert.True(MomSchedule.CompletesCycle(terminal));
            Assert.Equal(2, f.Queue.Starts);
        }

        [Fact]
        public void CompletedWithPartialCountNeverFillsTheRequestedCount()
        {
            var result = new VERMAXION.Models.MomRunResult
            {
                Status = VERMAXION.Models.MomRunStatus.Completed,
                CompletedRunCount = 2,
                RequestedRunCount = 30,
            };
            var credited = 0;
            Assert.Equal(2, MomSchedule.ObserveCompletedRuns(result, ref credited));
            Assert.False(MomSchedule.CompletesCycle(result));
            result.WindowExpired = true;
            Assert.True(MomSchedule.CompletesCycle(result));
            Assert.Equal(0, MomSchedule.ObserveCompletedRuns(result, ref credited));
            result.Status = VERMAXION.Models.MomRunStatus.Queued;
            Assert.False(MomSchedule.CompletesCycle(result));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void MissingDeadlineCapabilityBlocksDispatchIncludingLegacyReadiness(bool legacy)
        {
            var starts = 0;
            var ipc = new Dalamud.Plugin.IDalamudPluginInterface((name, args) =>
            {
                if (name == "mom.GetReadiness")
                    return legacy ? throw new InvalidOperationException("missing") : "{\"canStart\":true,\"ipcReady\":true}";
                if (name == "mom.IsReady") return true;
                starts++;
                throw new InvalidOperationException(name);
            });
            var client = new MomIPCClient(ipc, new IPluginLog());
            var result = client.StartRun(5, "PLD", false, queueDeadlineUtc: DateTime.UtcNow.AddHours(1));
            Assert.Equal(VERMAXION.Models.MomRunStatus.Rejected, result.Status);
            Assert.Equal(MomSchedule.DeadlineSupportBlocker, result.Summary);
            Assert.Equal(0, starts);
        }

        [Fact]
        public void DeadlineRoundTripsAndFailedStartCannotUseLegacyFallback()
        {
            var f = new Fixture();
            MomRunRequest? received = null;
            var legacyCalls = 0;
            var fail = false;
            var ipc = new Dalamud.Plugin.IDalamudPluginInterface((name, args) =>
            {
                if (name == "mom.GetReadiness") return MomIpcJson.Serialize(f.Coordinator.GetReadiness());
                if (name == "mom.StartRun")
                {
                    if (fail) throw new InvalidOperationException("dispatch unavailable");
                    received = MomIpcJson.Deserialize<MomRunRequest>((string)args[0]!);
                    return MomIpcJson.Serialize(f.Coordinator.StartRun(received!));
                }
                legacyCalls++;
                throw new InvalidOperationException(name);
            });
            var client = new MomIPCClient(ipc, new IPluginLog());
            Assert.Equal(VERMAXION.Models.MomRunStatus.Queued, client.StartRun(5, "PLD", false, queueDeadlineUtc: f.Deadline).Status);
            Assert.Equal(f.Deadline, received!.QueueDeadlineUtc);
            fail = true;
            Assert.Equal(VERMAXION.Models.MomRunStatus.Failed, client.StartRun(5, "PLD", false, queueDeadlineUtc: f.Deadline).Status);
            Assert.Equal(0, legacyCalls);
        }

        [Fact]
        public void NativeActionsAndEngineHandoffUseTheTestedCutoffGuards()
        {
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            var queue = File.ReadAllText(Path.Combine(root, "..", "mom", "mom", "Services", "CcQueueService.cs"));
            foreach (var method in new[] { "StartRouteQueue", "Update", "OpenPvpTab", "ClearDutySelection", "SelectCasualMatch", "JoinDutyFinder", "RestartActiveQueueAttempt", "OpenNextCandidate", "OpenCandidate", "OpenRouletteCandidate" })
            {
                var declaration = System.Text.RegularExpressions.Regex.Match(queue, @"(?:public|private)\s+(?:unsafe\s+)?void\s+" + method + @"\(");
                Assert.True(declaration.Success, method);
                var start = declaration.Index;
                var body = queue.IndexOf('{', start);
                Assert.Contains("QueueActionsBlocked", queue.Substring(body, 240));
            }
            Assert.Contains("!QueueActionsBlocked && GameHelpers.FireAddonCallback(\"ContentsFinderConfirm\"", queue);
            Assert.Contains("finder->QueueInfo.CancelQueue();", queue);
            var engine = File.ReadAllText(Path.Combine(root, "VERMAXION", "Services", "VermaxionEngine.cs"));
            Assert.Contains("MomSchedule.CompletesCycle(currentMomStatus)", engine);
            Assert.Contains("awaiting confirmed withdrawal or match exit", engine);
            Assert.Contains("GetServiceOwnedHandoffBlocker() ?? GetExternalHandoffBlocker()", engine);
        }

        private sealed class Fixture
        {
            public TestClock Clock { get; } = new();
            public DateTime Deadline;
            public ICondition Conditions { get; } = new();
            public CcQueueService Queue { get; }
            public JobSelectionService Job { get; } = new();
            public MatchStateService Match { get; } = new();
            public TerritoryProfileService Territory { get; } = new();
            public RunCoordinatorService Coordinator { get; }

            public Fixture()
            {
                Deadline = Clock.Now.AddHours(1);
                Queue = new CcQueueService(Clock, Conditions);
                Coordinator = new RunCoordinatorService(new IClientState(), new IObjectTable(), Conditions,
                    new IPluginLog(), new mom.Configuration(), new StartupHealth
                    {
                        ConfigLoaded = true, ConfigManagerReady = true, ServiceGraphReady = true, WindowsReady = true, IpcReady = true,
                    }, Job, Queue, new SeriesRankService(), new AchievementStatusService(), Match, Territory, Clock);
            }

            public void Start(bool series = false, string route = "casual-cc")
                => Assert.Equal(Status.Queued, Coordinator.StartRun(new MomRunRequest
                {
                    RequestedRunCount = 5, RequestedJob = "PLD", QueueDeadlineUtc = Deadline,
                    StopAtSeriesRank25 = series, Route = route,
                }).Status);

            public void EnterMatch()
            {
                Queue.NativeState = ContentsFinderQueueState.InContent;
                Queue.Waiting = false;
                Conditions[ConditionFlag.BoundByDuty] = true;
                Territory.Mode = PvpArenaMode.CrystallineConflict;
                Coordinator.Update();
            }

            public void ExitMatch()
            {
                Conditions[ConditionFlag.BoundByDuty] = false;
                Territory.Mode = PvpArenaMode.None;
                Queue.NativeState = ContentsFinderQueueState.None;
                Coordinator.Update();
            }

            public void ClearQueueAndSettle()
            {
                Queue.NativeState = ContentsFinderQueueState.None;
                Queue.Waiting = false;
                Coordinator.HandleSystemQueueCancellation();
                Coordinator.Update();
                Clock.Now = Clock.Now.AddSeconds(3);
                Coordinator.Update();
            }

            public VERMAXION.Models.MomRunResult ConsumerResult()
                => JsonSerializer.Deserialize<VERMAXION.Models.MomRunResult>(MomIpcJson.Serialize(Coordinator.CurrentResult), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        }
    }

    public sealed class TestClock : TimeProvider
    {
        public DateTime Now = new(2026, 9, 10, 10, 0, 0, DateTimeKind.Utc);
        public override DateTimeOffset GetUtcNow() => new(Now);
    }
}

// Only game, IPC transport, and unrelated preparation services are substituted.
// The coordinator, queue cutoff/withdrawal state, JSON contracts, consumer IPC, and accounting are production code.
namespace Dalamud.Game.ClientState.Conditions
{
    public enum ConditionFlag { BetweenAreas, BetweenAreas51, BoundByDuty, BoundByDuty56 }
}

namespace FFXIVClientStructs.FFXIV.Client.Enums
{
    public enum ContentsFinderQueueState : byte { None, Pending, Queued, Ready, Accepted, InContent }
}

namespace Dalamud.Plugin.Services
{
    public sealed class ICondition
    {
        private readonly Dictionary<ConditionFlag, bool> flags = new();
        public bool this[ConditionFlag flag] { get => flags.GetValueOrDefault(flag); set => flags[flag] = value; }
    }
    public sealed class IClientState { public bool IsLoggedIn => true; }
    public sealed class IObjectTable { public object? LocalPlayer => this; }
    public sealed class IPluginLog
    {
        public void Information(string text, params object[] args) { }
        public void Warning(string text, params object[] args) { }
        public void Debug(string text) { }
    }
}

namespace Dalamud.Plugin.Ipc
{
    public sealed class ICallGateSubscriber<T>(Func<object?[], object?> invoke)
    { public T InvokeFunc() => (T)invoke([])!; }
    public sealed class ICallGateSubscriber<T1, T>(Func<object?[], object?> invoke)
    { public T InvokeFunc(T1 arg1) => (T)invoke([arg1])!; }
    public sealed class ICallGateSubscriber<T1, T2, T>(Func<object?[], object?> invoke)
    { public T InvokeFunc(T1 arg1, T2 arg2) => (T)invoke([arg1, arg2])!; }
    public sealed class ICallGateSubscriber<T1, T2, T3, T>(Func<object?[], object?> invoke)
    { public T InvokeFunc(T1 arg1, T2 arg2, T3 arg3) => (T)invoke([arg1, arg2, arg3])!; }
}

namespace Dalamud.Plugin
{
    public sealed class IDalamudPluginInterface(Func<string, object?[], object?> invoke)
    {
        public Ipc.ICallGateSubscriber<T> GetIpcSubscriber<T>(string name) => new(args => invoke(name, args));
        public Ipc.ICallGateSubscriber<T1, T> GetIpcSubscriber<T1, T>(string name) => new(args => invoke(name, args));
        public Ipc.ICallGateSubscriber<T1, T2, T> GetIpcSubscriber<T1, T2, T>(string name) => new(args => invoke(name, args));
        public Ipc.ICallGateSubscriber<T1, T2, T3, T> GetIpcSubscriber<T1, T2, T3, T>(string name) => new(args => invoke(name, args));
    }
}

namespace mom
{
    public sealed class Configuration
    {
        public bool PluginEnabled { get; set; } = true;
        public void Save() { }
    }
}

namespace mom.Services
{
    public sealed partial class CcQueueService(VERMAXION.Tests.TestClock clock, ICondition conditions)
    {
        private DateTime UtcNow => clock.Now;
        private DateTime lastDutyPopAcceptedAt = DateTime.MinValue;
        private bool queueActive;
        private bool IsBetweenAreas => conditions[ConditionFlag.BetweenAreas] || conditions[ConditionFlag.BetweenAreas51];
        public bool IsInDuty => conditions[ConditionFlag.BoundByDuty] || conditions[ConditionFlag.BoundByDuty56];
        public bool IsInQueue => Waiting;
        public bool IsWaitingForDutyPop => NativeState == ContentsFinderQueueState.Ready;
        public bool CanOwnNewQueue => !IsInDuty && !IsInQueue && NativeState == ContentsFinderQueueState.None;
        public string LastFailureReason => "test timeout";
        public ContentsFinderQueueState? NativeState = ContentsFinderQueueState.None;
        public bool Waiting;
        public bool ThrowOnWithdraw;
        public int Starts;
        public int Withdrawals;
        private ContentsFinderQueueState? ReadNativeQueueState() => NativeState;
        private void CancelNativeQueue() { Withdrawals++; if (ThrowOnWithdraw) throw new InvalidOperationException("native unavailable"); }
        public void SubmitWithoutAcknowledgement() { queueSubmissionIssued = true; queueSubmissionPending = true; }
        public void Register(ContentsFinderQueueState state) { NativeState = state; Waiting = true; SubmitWithoutAcknowledgement(); }
        public void Reset(bool clearSeriesSelection = false)
        { queueActive = false; queueSubmissionIssued = false; queueSubmissionPending = false; lastDutyPopAcceptedAt = DateTime.MinValue; }
        public void StartRouteQueue(string route, bool continuingSeries = false)
        { if (!QueueActionsBlocked) { Starts++; queueActive = true; } }
        public void Update() { if (IsInQueue || IsWaitingForDutyPop) queueSubmissionPending = false; }
        public bool TimedOut(TimeSpan timeout) => queueActive;
        public bool WasDutyPopAcceptedRecently(TimeSpan window) => false;
    }
    public static class GameHelpers
    {
        public static bool IsPlayerAvailable() => true;
        public static void ResetInteractionState() { }
    }
    public sealed class JobSelectionService
    {
        public bool Current = true;
        public int Switches;
        public bool TryResolveJob(string name, out byte id, out string resolved, out string failure)
        { id = 19; resolved = name; failure = ""; return true; }
        public bool IsCurrentJob(byte id) => Current;
        public bool TryEquipRequestedJob(byte id, string name, out string failure)
        { Switches++; failure = ""; return true; }
    }
    public sealed class SeriesRankService
    {
        public const int MaxSeriesRank = 25;
        public bool IsRefreshPending { get; private set; }
        public mom.Models.SeriesRankSnapshot LastSnapshot => new() { Rank = 1 };
        public string StatusSummary => "pending";
        public bool RequestRefresh() { IsRefreshPending = true; return true; }
        public void Cancel(string reason = "") => IsRefreshPending = false;
    }
    public sealed class AchievementStatusService
    {
        public static TimeSpan GateTimeout => TimeSpan.FromSeconds(30);
        public mom.Models.RivalWingsAchievementGateResult GetRivalWingsGate(bool requestRefresh = false) => new();
        public void RequestRivalWingsGateRefresh(bool force = false) { }
    }
    public sealed class MatchStateService { public MatchSnapshot Capture() => new(); }
    public sealed class TerritoryProfileService
    {
        public PvpArenaMode Mode;
        public TerritoryProfileSnapshot Evaluate(MatchSnapshot match) => new() { PvpMode = Mode };
    }
}
