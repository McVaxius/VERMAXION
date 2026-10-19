using System;
using System.Collections.Generic;
using System.Linq;
using VERMAXION.Beastmaster;
using Xunit;

namespace VERMAXION.Tests;

public sealed class BeastmasterRefreshTests
{
    private static readonly DateTime Start = new(2026, 10, 18, 16, 0, 0, DateTimeKind.Utc);
    private static BeastmasterNotebookPage Page(uint index, uint count, params uint[] numbers) => new(true, count, index, numbers);

    [Fact]
    public void ZeroPageInitializationWaitsForActualRowsInsteadOfDeclaringMissingOwnership()
    {
        var scan = new BeastmasterRefreshTraversal([13], Start);
        Assert.Equal(BeastmasterRefreshAction.Wait, scan.Next(new(false, 0, 0, []), 0, Start).Action);
        Assert.Equal(BeastmasterRefreshAction.Wait, scan.Next(Page(0, 1, 0, 0), 0, Start.AddSeconds(5).AddTicks(-1)).Action);
        var select = scan.Next(Page(0, 1, 0, 13), 0, Start.AddSeconds(5));
        Assert.Equal(BeastmasterRefreshAction.Select, select.Action);
        Assert.Equal(13u, select.Number);
        Assert.Equal(1, select.Slot);
        Assert.Equal(BeastmasterRefreshAction.Record, scan.Next(Page(0, 1, 0, 13), 13, Start.AddSeconds(6)).Action);
        Assert.Equal(BeastmasterRefreshAction.Complete, scan.Next(Page(0, 1, 0, 13), 0, Start.AddSeconds(7)).Action);
    }

    [Fact]
    public void DelayedPageIndexAndRowsAreBothObservedBeforeAnotherSelectionOrPageRequest()
    {
        var scan = new BeastmasterRefreshTraversal([7, 50], Start);
        var first = Page(1, 3, 0, 7);
        Assert.Equal(BeastmasterRefreshAction.Select, scan.Next(first, 0, Start).Action);
        Assert.Equal(BeastmasterRefreshAction.Record, scan.Next(first, 7, Start.AddMilliseconds(100)).Action);
        var request = scan.Next(first, 0, Start.AddMilliseconds(200));
        Assert.Equal(BeastmasterRefreshAction.ShowPage, request.Action);
        Assert.Equal(2u, request.Page);
        for (var second = 1; second <= 4; second++)
            Assert.Equal(BeastmasterRefreshAction.Wait, scan.Next(first, 0, Start.AddSeconds(second)).Action);
        // The header moved, but the entry rows still belong to page 1.
        Assert.Equal(BeastmasterRefreshAction.Wait, scan.Next(Page(2, 3, 0, 7), 0, Start.AddSeconds(5)).Action);
        var secondPage = Page(2, 3, 100, 0, 50);
        var next = scan.Next(secondPage, 0, Start.AddSeconds(6));
        Assert.Equal(BeastmasterRefreshAction.Select, next.Action);
        Assert.Equal(50u, next.Number);
        Assert.Equal(2, next.Slot);
        Assert.Equal(BeastmasterRefreshAction.Record, scan.Next(secondPage, 50, Start.AddSeconds(7)).Action);
        var finalPage = scan.Next(secondPage, 0, Start.AddSeconds(8));
        Assert.Equal(BeastmasterRefreshAction.ShowPage, finalPage.Action);
        Assert.Equal(0u, finalPage.Page);
        Assert.Equal(BeastmasterRefreshAction.Complete, scan.Next(Page(0, 3, 90), 0, Start.AddSeconds(9)).Action);
        Assert.Equal(3, scan.VisitedPages);
        Assert.Empty(scan.Missing);
    }

    [Fact]
    public void MissingOwnedEntryProducesPartialAfterOnePassAndRetainsEverySuccessfulRead()
    {
        var scan = new BeastmasterRefreshTraversal([1, 9], Start);
        var requests = new List<uint>();
        var first = Page(0, 3, 1);
        Assert.Equal(BeastmasterRefreshAction.Select, scan.Next(first, 0, Start).Action);
        Assert.Equal(BeastmasterRefreshAction.Record, scan.Next(first, 1, Start.AddSeconds(1)).Action);
        var request = scan.Next(first, 0, Start.AddSeconds(2));
        requests.Add(request.Page);
        Assert.Equal(BeastmasterRefreshAction.ShowPage, request.Action);
        request = scan.Next(Page(1, 3, 2, 3, 4), 0, Start.AddSeconds(3));
        requests.Add(request.Page);
        Assert.Equal(BeastmasterRefreshAction.ShowPage, request.Action);
        var finish = scan.Next(Page(2, 3, 5), 0, Start.AddSeconds(4));
        Assert.Equal(BeastmasterRefreshAction.Partial, finish.Action);
        Assert.Equal(BeastmasterRefreshProblem.MissingEntries, finish.Problem);
        Assert.Equal(new uint[] { 1, 2 }, requests);
        Assert.Equal(new uint[] { 1 }, scan.Read);
        Assert.Equal(new uint[] { 9 }, scan.Missing);
        Assert.Equal(finish, scan.Next(first, 9, Start.AddSeconds(5)));
    }

    [Fact]
    public void UnreadDetailDoesNotDiscardOtherBeastsOrSelectTheFailedBeastAgain()
    {
        var scan = new BeastmasterRefreshTraversal([1, 2], Start);
        var page = Page(0, 1, 1, 2);
        Assert.Equal(1u, scan.Next(page, 0, Start).Number);
        Assert.Equal(BeastmasterRefreshAction.Wait, scan.Next(page, 2, Start.AddSeconds(2)).Action);
        Assert.Equal(BeastmasterRefreshAction.Wait, scan.Next(page, 0, Start.AddSeconds(5).AddTicks(-1)).Action);
        Assert.Equal(BeastmasterRefreshAction.Wait, scan.Next(page, 0, Start.AddSeconds(5)).Action);
        var second = scan.Next(page, 0, Start.AddSeconds(6));
        Assert.Equal(BeastmasterRefreshAction.Select, second.Action);
        Assert.Equal(2u, second.Number);
        Assert.Equal(BeastmasterRefreshAction.Record, scan.Next(page, 2, Start.AddSeconds(7)).Action);
        Assert.Equal(BeastmasterRefreshAction.Partial, scan.Next(page, 0, Start.AddSeconds(8)).Action);
        Assert.Equal(BeastmasterRefreshProblem.DetailUnavailable, scan.UnreadDetails[1]);
        Assert.Equal(new uint[] { 2 }, scan.Read);
        Assert.Equal(new uint[] { 1 }, scan.Missing);
    }

    [Fact]
    public void UnconfirmedPageCallbackIsNotReplayedAndInitialReadIsRetained()
    {
        var scan = new BeastmasterRefreshTraversal([1, 2], Start);
        var page = Page(0, 2, 1);
        scan.Next(page, 0, Start);
        scan.Next(page, 1, Start.AddSeconds(1));
        Assert.Equal(BeastmasterRefreshAction.ShowPage, scan.Next(page, 0, Start.AddSeconds(2)).Action);
        for (var second = 3; second < 7; second++)
            Assert.Equal(BeastmasterRefreshAction.Wait, scan.Next(page, 0, Start.AddSeconds(second)).Action);
        var timeout = scan.Next(page, 0, Start.AddSeconds(7));
        Assert.Equal(BeastmasterRefreshAction.Partial, timeout.Action);
        Assert.Equal(BeastmasterRefreshProblem.PageUnconfirmed, timeout.Problem);
        Assert.Equal(new uint[] { 1 }, scan.Read);
        Assert.Equal(new uint[] { 2 }, scan.Missing);
    }

    [Fact]
    public void EmptyInitializationAndChangedPageMapNeverProduceAWholeRosterCompletion()
    {
        var scan = new BeastmasterRefreshTraversal([1], Start);
        var timeout = scan.Next(new(false, 0, 0, []), 0, Start.AddSeconds(30));
        Assert.Equal(BeastmasterRefreshAction.Partial, timeout.Action);
        Assert.Equal(BeastmasterRefreshProblem.InitialPageUnavailable, timeout.Problem);
        Assert.Empty(scan.Read);

        scan = new([1], Start);
        scan.Next(Page(0, 2, 2), 0, Start);
        var changed = scan.Next(Page(1, 3, 1), 0, Start.AddSeconds(1));
        Assert.Equal(BeastmasterRefreshAction.Partial, changed.Action);
        Assert.Equal(BeastmasterRefreshProblem.PageChanged, changed.Problem);
        Assert.Empty(scan.Read);
    }

    [Fact]
    public void LowerRankRejectionLeavesThatBeastUnknownWhileOtherDetailsContinue()
    {
        var scan = new BeastmasterRefreshTraversal([1, 2], Start);
        var page = Page(0, 1, 1, 2);
        Assert.Equal(1u, scan.Next(page, 0, Start).Number);
        scan.RejectSelected(BeastmasterRefreshProblem.LowerRank, Start.AddSeconds(1));
        Assert.Equal(2u, scan.Next(page, 0, Start.AddSeconds(2)).Number);
        scan.Next(page, 2, Start.AddSeconds(3));
        Assert.Equal(BeastmasterRefreshAction.Partial, scan.Next(page, 0, Start.AddSeconds(4)).Action);
        Assert.Equal(BeastmasterRefreshProblem.LowerRank, scan.UnreadDetails[1]);
        Assert.Equal(new uint[] { 2 }, scan.Read);
    }

    [Fact]
    public void SavedPauseTimeDoesNotConsumePageReadinessBounds()
    {
        var scan = new BeastmasterRefreshTraversal([1], Start);
        scan.Next(new(false, 0, 0, []), 0, Start.AddSeconds(1));
        scan.ResumeAfterPause(TimeSpan.FromHours(1));
        Assert.Equal(BeastmasterRefreshAction.Wait, scan.Next(new(false, 0, 0, []), 0, Start.AddHours(1).AddSeconds(5).AddTicks(-1)).Action);
        Assert.Equal(BeastmasterRefreshAction.Partial, scan.Next(new(false, 0, 0, []), 0, Start.AddHours(1).AddSeconds(5)).Action);
    }
}
