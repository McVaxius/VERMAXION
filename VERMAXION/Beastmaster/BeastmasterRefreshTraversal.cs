using System;
using System.Collections.Generic;
using System.Linq;

namespace VERMAXION.Beastmaster;

internal readonly record struct BeastmasterNotebookPage(bool Ready, uint PageCount, uint Page, IReadOnlyList<uint> Numbers);
internal enum BeastmasterRefreshAction { Wait, ShowPage, Select, Record, Complete, Partial }
internal enum BeastmasterRefreshProblem { None, InitialPageUnavailable, PageUnconfirmed, PageChanged, DetailUnavailable, LowerRank, MissingEntries, TimeLimit }
internal readonly record struct BeastmasterRefreshDecision(BeastmasterRefreshAction Action, uint Page = 0, uint Number = 0, int Slot = -1,
    BeastmasterRefreshProblem Problem = BeastmasterRefreshProblem.None);

// The production full-refresh sequencer. Each page is requested once, then its
// index AND changed, populated entry rows must be observed before it is visited.
internal sealed class BeastmasterRefreshTraversal(IEnumerable<uint> ownedNumbers, DateTime observedAt)
{
    private static readonly TimeSpan PageLimit = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DetailLimit = TimeSpan.FromSeconds(5);
    private readonly HashSet<uint> owned = ownedNumbers.Where(number => number != 0).ToHashSet();
    private readonly HashSet<uint> read = [];
    private readonly HashSet<uint> attempted = [];
    private readonly HashSet<uint> visited = [];
    private readonly Dictionary<uint, BeastmasterRefreshProblem> unreadDetails = [];
    private uint? pageCount;
    private uint currentPage;
    private uint? requestedPage;
    private uint[] previousNumbers = [];
    private uint selected;
    private DateTime started = observedAt;
    private DateTime stepStarted = observedAt;
    private BeastmasterRefreshDecision? terminal;

    internal IReadOnlyCollection<uint> Owned => owned;
    internal IReadOnlyCollection<uint> Read => read;
    internal IReadOnlyCollection<uint> Missing => owned.Except(read).Order().ToArray();
    internal IReadOnlyDictionary<uint, BeastmasterRefreshProblem> UnreadDetails => unreadDetails;
    internal uint Selected => selected;
    internal uint? RequestedPage => requestedPage;
    internal IReadOnlyList<uint> RequestedPagePreviousRows => previousNumbers;
    internal uint PageCount => pageCount ?? 0;
    internal int VisitedPages => visited.Count;

    internal BeastmasterRefreshDecision Next(BeastmasterNotebookPage page, uint observedNumber, DateTime now)
    {
        if (terminal is { } finished) return finished;
        if (now - started >= TimeSpan.FromMinutes(5)) return Finish(BeastmasterRefreshProblem.TimeLimit);
        if (owned.Count == 0) return Finish(BeastmasterRefreshProblem.None);
        if (!page.Ready || page.PageCount == 0 || page.Page >= page.PageCount || !page.Numbers.Any(number => number != 0))
            return now - stepStarted >= PageLimit
                ? Finish(requestedPage.HasValue || pageCount.HasValue ? BeastmasterRefreshProblem.PageUnconfirmed : BeastmasterRefreshProblem.InitialPageUnavailable)
                : new(BeastmasterRefreshAction.Wait);

        if (pageCount == null)
        {
            pageCount = page.PageCount;
            currentPage = page.Page;
            stepStarted = now;
        }
        else if (page.PageCount != pageCount.Value) return Finish(BeastmasterRefreshProblem.PageChanged);

        if (requestedPage is { } requested)
        {
            // An eagerly changed page index with the previous page's rows is not
            // confirmation that the requested native page has arrived.
            if (page.Page != requested || page.Numbers.SequenceEqual(previousNumbers))
                return now - stepStarted >= PageLimit ? Finish(BeastmasterRefreshProblem.PageUnconfirmed) : new(BeastmasterRefreshAction.Wait);
            currentPage = requested;
            requestedPage = null;
            stepStarted = now;
        }
        if (page.Page != currentPage) return Finish(BeastmasterRefreshProblem.PageChanged);

        if (selected != 0)
        {
            if (observedNumber == selected)
            {
                var number = selected;
                read.Add(number); attempted.Add(number); selected = 0;
                stepStarted = now;
                return new(BeastmasterRefreshAction.Record, currentPage, number);
            }
            if (now - stepStarted >= DetailLimit) RejectSelected(BeastmasterRefreshProblem.DetailUnavailable, now);
            return new(BeastmasterRefreshAction.Wait);
        }

        for (var slot = 0; slot < page.Numbers.Count; slot++)
        {
            var number = page.Numbers[slot];
            if (!owned.Contains(number) || attempted.Contains(number)) continue;
            selected = number; stepStarted = now;
            return new(BeastmasterRefreshAction.Select, currentPage, number, slot);
        }

        visited.Add(currentPage);
        if (visited.Count == pageCount.Value) return Finish(read.Count == owned.Count ? BeastmasterRefreshProblem.None : BeastmasterRefreshProblem.MissingEntries);
        requestedPage = (currentPage + 1) % pageCount.Value;
        previousNumbers = page.Numbers.ToArray();
        stepStarted = now;
        return new(BeastmasterRefreshAction.ShowPage, requestedPage.Value);
    }

    internal void RejectSelected(BeastmasterRefreshProblem problem, DateTime now)
    {
        if (selected == 0) return;
        attempted.Add(selected);
        unreadDetails[selected] = problem;
        selected = 0;
        stepStarted = now;
    }

    internal void ResumeAfterPause(TimeSpan elapsed)
    {
        started += elapsed;
        stepStarted += elapsed;
    }

    private BeastmasterRefreshDecision Finish(BeastmasterRefreshProblem problem)
    {
        terminal = new(problem == BeastmasterRefreshProblem.None && read.Count == owned.Count
            ? BeastmasterRefreshAction.Complete : BeastmasterRefreshAction.Partial, currentPage, Problem: problem);
        return terminal.Value;
    }
}
