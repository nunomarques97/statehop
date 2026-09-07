using Statehop.Core.Activity;

namespace Statehop.Tests;

/// <summary>
/// The buffer behind the activity list. This logic once lived
/// inside the WinUI project and could not be tested at all; the incremental
/// read below is the whole reason the app stopped costing 7 % of a core.
/// </summary>
[TestClass]
public sealed class ActivityFeedTests
{
    private static readonly DateTime At = new(2026, 9, 6, 13, 15, 0, DateTimeKind.Local);

    private static ActivityFeed Filled(int lines, int capacity = ActivityFeed.DefaultCapacity)
    {
        var feed = new ActivityFeed(capacity);
        for (var i = 1; i <= lines; i++)
        {
            feed.Add(At.AddSeconds(i), "Processo", $"linha {i}");
        }

        return feed;
    }

    [TestMethod]
    public void SequencesAreMonotonicAndStartAtOne()
    {
        var feed = new ActivityFeed();

        Assert.AreEqual(0, feed.LastSequence, "an empty feed has issued nothing");
        Assert.AreEqual(1, feed.Add(At, "Idle", "primeira").Sequence);
        Assert.AreEqual(2, feed.Add(At, "Idle", "segunda").Sequence);
        Assert.AreEqual(2, feed.LastSequence);
    }

    [TestMethod]
    public void RecentIsNewestFirst()
    {
        var feed = Filled(3);

        var recent = feed.Recent();

        CollectionAssert.AreEqual(
            new[] { "linha 3", "linha 2", "linha 1" },
            recent.Select(x => x.Description).ToArray());
    }

    [TestMethod]
    public void SinceZero_ReturnsEverythingOldestFirst()
    {
        var feed = Filled(3);

        var added = feed.Since(0);

        // Oldest first is what lets a view insert each line at index 0 in turn
        // and end up with the newest on top.
        CollectionAssert.AreEqual(
            new[] { "linha 1", "linha 2", "linha 3" },
            added.Select(x => x.Description).ToArray());
    }

    [TestMethod]
    public void SinceTheLatest_ReturnsNothing()
    {
        var feed = Filled(5);

        Assert.IsEmpty(feed.Since(feed.LastSequence), "a view that is up to date must be given no work");
    }

    [TestMethod]
    public void SinceAWatermark_ReturnsOnlyWhatIsNewer()
    {
        var feed = Filled(5);

        var added = feed.Since(3);

        CollectionAssert.AreEqual(
            new[] { "linha 4", "linha 5" },
            added.Select(x => x.Description).ToArray());
    }

    [TestMethod]
    public void OverflowDropsTheOldest_AndKeepsTheBound()
    {
        var feed = Filled(10, capacity: 4);

        Assert.AreEqual(4, feed.Count);
        CollectionAssert.AreEqual(
            new[] { "linha 10", "linha 9", "linha 8", "linha 7" },
            feed.Recent().Select(x => x.Description).ToArray());
    }

    [TestMethod]
    public void AViewThatFellFarBehind_GetsAtMostAFullBuffer()
    {
        // The lines that fell off the end are gone on purpose: they are no
        // longer "recent activity", and a view must not be handed more rows
        // than the buffer holds.
        var feed = Filled(50, capacity: 4);

        var added = feed.Since(1);

        Assert.HasCount(4, added);
        Assert.AreEqual("linha 47", added[0].Description);
        Assert.AreEqual("linha 50", added[^1].Description);
    }

    [TestMethod]
    public void CapacityOfOne_IsAllowed()
    {
        var feed = Filled(3, capacity: 1);

        Assert.AreEqual(1, feed.Count);
        Assert.AreEqual("linha 3", feed.Recent()[0].Description);
    }

    [TestMethod]
    public void CapacityBelowOne_IsRejected()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ActivityFeed(0));
    }

    [TestMethod]
    public void TimeTextIsPreFormattedForDisplay()
    {
        var line = new ActivityFeed().Add(new DateTime(2026, 9, 6, 21, 4, 9), "Idle", "x");

        Assert.AreEqual("21:04:09", line.TimeText);
    }

    [TestMethod]
    public void ConcurrentWriters_LoseNoLineAndReuseNoSequence()
    {
        // Observation events arrive on three different threads: the foreground
        // hook on the UI thread, process and idle polling on timer threads.
        const int writers = 8;
        const int perWriter = 250;
        var feed = new ActivityFeed(capacity: writers * perWriter);

        Parallel.For(0, writers, w =>
        {
            for (var i = 0; i < perWriter; i++)
            {
                feed.Add(At, "Processo", $"{w}:{i}");
            }
        });

        var all = feed.Recent();
        Assert.HasCount(writers * perWriter, all, "no line may be lost");
        Assert.AreEqual(writers * perWriter, all.Select(x => x.Sequence).Distinct().Count(),
            "no sequence may be issued twice");
        Assert.AreEqual(writers * perWriter, feed.LastSequence);
    }
}
