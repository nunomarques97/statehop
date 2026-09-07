using Statehop.Core.Abstractions;
using Statehop.Core.Model;
using Statehop.Core.Observation;
using Statehop.Storage;

namespace Statehop.Tests;

/// <summary>
/// The observation layer, driven through fake sources.
///
/// None of this was testable while the service lived inside the WinUI project:
/// it built its own Win32 watchers and its own SQLite store in the
/// constructor. Now it takes both as interfaces, which is what the
/// Observation → Inference → Decision → Execution separation in
/// docs/PRODUCT.md asks for in the first place.
/// </summary>
[TestClass]
public sealed class ObservationServiceTests
{
    private static readonly DateTime At = new(2026, 9, 6, 13, 15, 0, DateTimeKind.Utc);

    private static ProcessIdentity Code =>
        new("Code", @"C:\Program Files\Microsoft VS Code\Code.exe", ProcessAccessState.Accessible);

    // ---- fakes -------------------------------------------------------------

    private sealed class FakeForeground : IForegroundSource
    {
        public event EventHandler<ForegroundChange>? ForegroundChanged;
        public bool Started { get; private set; }
        public bool Disposed { get; private set; }
        public void Start() => Started = true;
        public void Raise(ForegroundChange change) => ForegroundChanged?.Invoke(this, change);
        public void Dispose() => Disposed = true;
    }

    private sealed class FakeIdle : IIdleSource
    {
        public event EventHandler<IdleChange>? IdleChanged;
        public bool IsIdle { get; set; }
        public bool Started { get; private set; }
        public bool Disposed { get; private set; }
        public void Start() => Started = true;
        public void Raise(IdleChange change) { IsIdle = change.IsIdle; IdleChanged?.Invoke(this, change); }
        public void Dispose() => Disposed = true;
    }

    private sealed class FakeProcesses : IProcessSource
    {
        public event EventHandler<ProcessLifetimeChange>? LifetimeChanged;
        public int LastSnapshotCount { get; set; }
        public bool Started { get; private set; }
        public bool Disposed { get; private set; }
        public void Start() => Started = true;
        public void Raise(ProcessLifetimeChange change) => LifetimeChanged?.Invoke(this, change);
        public void Dispose() => Disposed = true;
    }

    private sealed class FakeWindowOwners : IWindowOwnerSource
    {
        public event EventHandler<IReadOnlyList<WindowOwnerProbe>>? Probed;
        public bool Started { get; private set; }
        public bool Disposed { get; private set; }
        public void Start() => Started = true;
        public void Raise(IReadOnlyList<WindowOwnerProbe> probes) => Probed?.Invoke(this, probes);
        public void Dispose() => Disposed = true;
    }

    /// <summary>A store that records what it was asked to do, and can be told to fail.</summary>
    private sealed class RecordingStore : IActivityStore, IDisposable
    {
        public List<string> Calls { get; } = [];
        public bool ThrowOnWrite { get; set; }
        public bool Disposed { get; private set; }
        public string DatabasePath => "(fake)";

        private void Note(string call)
        {
            Calls.Add(call);
            if (ThrowOnWrite)
            {
                throw new InvalidOperationException("disco cheio");
            }
        }

        public void RecordForeground(ForegroundChange change) => Note("foreground");
        public void RecordIdle(IdleChange change) => Note("idle");
        public void RecordLifetime(ProcessLifetimeChange change) => Note("lifetime");
        public void RecordWindowOwnerProbe(IReadOnlyList<WindowOwnerProbe> probes) => Note("probe");
        public void ReclassifyEphemeral() => Note("reclassify");
        public StoreStats GetStats() => new(0, 0, 0, 0, 0, 0, 0);
        public ElevatedAccessReport GetElevatedAccessReport() => new(0, 0, [], 0);
        public void Dispose() => Disposed = true;
    }

    private sealed class Harness : IDisposable
    {
        public RecordingStore Store { get; } = new();
        public FakeForeground Foreground { get; } = new();
        public FakeIdle Idle { get; } = new();
        public FakeProcesses Processes { get; } = new();
        public FakeWindowOwners WindowOwners { get; } = new();
        public ObservationService Service { get; }
        public int UpdatedCount { get; private set; }

        public Harness()
        {
            Service = new ObservationService(Store, Foreground, Idle, Processes, WindowOwners);
            Service.Updated += (_, _) => UpdatedCount++;
        }

        public void Dispose() => Service.Dispose();
    }

    // ---- what it records ---------------------------------------------------

    [TestMethod]
    public void ForegroundChange_IsStoredAndShown()
    {
        using var h = new Harness();

        h.Foreground.Raise(new ForegroundChange(100, Code, At));

        CollectionAssert.AreEqual(new[] { "foreground" }, h.Store.Calls.ToArray());
        Assert.AreEqual("Code", h.Service.CurrentForeground);
        Assert.AreEqual("Code", h.Service.RecentActivity()[0].Description);
        Assert.AreEqual("Foreground", h.Service.RecentActivity()[0].Kind);
        Assert.AreEqual(1, h.UpdatedCount);
    }

    [TestMethod]
    public void ForegroundChangeWithDeniedIdentity_SaysSoInTheFeed()
    {
        using var h = new Harness();

        h.Foreground.Raise(new ForegroundChange(100, ProcessIdentity.Denied("algo"), At));

        StringAssert.Contains(h.Service.RecentActivity()[0].Description, "identidade inacessível");
    }

    [TestMethod]
    public void IdleTransitions_AreWordedAsAbsenceOfInput()
    {
        using var h = new Harness();

        h.Idle.Raise(new IdleChange(true, At));
        h.Idle.Raise(new IdleChange(false, At.AddMinutes(5)));

        var lines = h.Service.RecentActivity();
        // The UX rule in docs/PRODUCT.md: this measures absence of user input,
        // never "the application was doing nothing".
        StringAssert.Contains(lines[1].Description, "sem input do utilizador");
        Assert.AreEqual("input do utilizador retomado", lines[0].Description);
        Assert.IsFalse(h.Service.IsIdle);
    }

    [TestMethod]
    public void LifetimeEvents_ReadAsStartedOrEnded()
    {
        using var h = new Harness();

        h.Processes.Raise(new ProcessLifetimeChange(1, Code, ProcessLifetimeKind.Started, At, null));
        h.Processes.Raise(new ProcessLifetimeChange(1, Code, ProcessLifetimeKind.Exited, At.AddMinutes(1), null));

        var lines = h.Service.RecentActivity();
        Assert.AreEqual("Code terminou", lines[0].Description);
        Assert.AreEqual("Code arrancou", lines[1].Description);
    }

    [TestMethod]
    public void WindowOwnerProbe_IsStoredButNotShownInTheFeed()
    {
        using var h = new Harness();

        h.WindowOwners.Raise([new WindowOwnerProbe("Code", ProcessAccessState.Accessible, false)]);

        CollectionAssert.AreEqual(new[] { "probe" }, h.Store.Calls.ToArray());
        Assert.IsEmpty(h.Service.RecentActivity(), "the B0.2 probe is a measurement, not activity");
        Assert.AreEqual(1, h.UpdatedCount);
    }

    // ---- what it does when the store fails ---------------------------------

    [TestMethod]
    public void AFailedWrite_IsCountedAndDoesNotEscape()
    {
        using var h = new Harness();
        h.Store.ThrowOnWrite = true;

        h.Foreground.Raise(new ForegroundChange(1, Code, At));

        // An observer that dies halfway through the day fails the Phase 0 gate
        // whatever else it does.
        Assert.AreEqual(1, h.Service.RecordingErrors);
        Assert.AreEqual("disco cheio", h.Service.LastErrorMessage);
    }

    [TestMethod]
    public void AFailedWrite_StillLeavesTheLineInTheFeed()
    {
        using var h = new Harness();
        h.Store.ThrowOnWrite = true;

        h.Processes.Raise(new ProcessLifetimeChange(1, Code, ProcessLifetimeKind.Started, At, null));

        // The recording failed; the observation did not. Hiding it would make
        // the window disagree with what the user just did.
        Assert.HasCount(1, h.Service.RecentActivity());
        Assert.AreEqual("Code arrancou", h.Service.RecentActivity()[0].Description);
    }

    [TestMethod]
    public void AFailedProbe_IsCountedToo()
    {
        using var h = new Harness();
        h.Store.ThrowOnWrite = true;

        h.WindowOwners.Raise([new WindowOwnerProbe("Code", ProcessAccessState.Accessible, false)]);

        Assert.AreEqual(1, h.Service.RecordingErrors);
    }

    [TestMethod]
    public void AFailedReclassification_IsCountedAndDoesNotEscape()
    {
        using var h = new Harness();
        h.Store.ThrowOnWrite = true;

        h.Service.Reclassify();

        Assert.AreEqual(1, h.Service.RecordingErrors);
        CollectionAssert.Contains(h.Store.Calls, "reclassify");
    }

    // ---- lifecycle ---------------------------------------------------------

    [TestMethod]
    public void NothingObservesUntilStart()
    {
        using var h = new Harness();

        Assert.IsFalse(h.Foreground.Started);
        Assert.IsFalse(h.Idle.Started);
        Assert.IsFalse(h.Processes.Started);
        Assert.IsFalse(h.WindowOwners.Started);

        h.Service.Start();

        Assert.IsTrue(h.Foreground.Started);
        Assert.IsTrue(h.Idle.Started);
        Assert.IsTrue(h.Processes.Started);
        Assert.IsTrue(h.WindowOwners.Started);
    }

    [TestMethod]
    public void Dispose_ReleasesEverySourceAndTheStore()
    {
        var h = new Harness();

        h.Service.Dispose();

        Assert.IsTrue(h.Foreground.Disposed);
        Assert.IsTrue(h.Idle.Disposed);
        Assert.IsTrue(h.Processes.Disposed);
        Assert.IsTrue(h.WindowOwners.Disposed);
        Assert.IsTrue(h.Store.Disposed, "the service owns what it was handed");
    }

    [TestMethod]
    public void DisposeIsIdempotent()
    {
        var h = new Harness();

        h.Service.Dispose();
        h.Service.Dispose();
    }

    // ---- the incremental read the window depends on ------------------------

    [TestMethod]
    public void ActivitySince_GivesTheViewOnlyWhatItHasNotSeen()
    {
        using var h = new Harness();
        h.Foreground.Raise(new ForegroundChange(1, Code, At));
        h.Foreground.Raise(new ForegroundChange(2, Code, At.AddSeconds(1)));

        var watermark = h.Service.RecentActivity()[0].Sequence;
        h.Processes.Raise(new ProcessLifetimeChange(3, Code, ProcessLifetimeKind.Started, At.AddSeconds(2), null));

        var added = h.Service.ActivitySince(watermark);

        Assert.HasCount(1, added, "only the line the view has not seen");
        Assert.AreEqual("Code arrancou", added[0].Description);
        Assert.IsEmpty(h.Service.ActivitySince(added[0].Sequence));
    }

    // ---- against the real store -------------------------------------------

    [TestMethod]
    public void AgainstTheRealStore_ObservationReachesDisk()
    {
        var path = Path.Combine(Path.GetTempPath(), $"statehop-test-{Guid.NewGuid():N}.db");
        try
        {
            using (var service = new ObservationService(
                new SqliteActivityStore(path),
                new FakeForeground(),
                new FakeIdle(),
                new FakeProcesses(),
                new FakeWindowOwners()))
            {
                var foreground = (FakeForeground)GetSource(service);
                foreground.Raise(new ForegroundChange(1, Code, At));

                Assert.AreEqual(1, service.Store.GetStats().ForegroundEvents);
                Assert.AreEqual(0, service.RecordingErrors);
            }

            // Disposing the service must have closed the store cleanly, so the
            // file is readable by a fresh connection.
            using var reopened = new SqliteActivityStore(path);
            Assert.AreEqual(1, reopened.GetStats().ForegroundEvents);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
            {
                if (File.Exists(path + suffix))
                {
                    File.Delete(path + suffix);
                }
            }
        }
    }

    /// <summary>
    /// The service does not expose its sources, so this test reaches the fake
    /// it handed in the only way available. Kept tiny and local on purpose:
    /// widening the public surface just for a test would be the worse trade.
    /// </summary>
    private static object GetSource(ObservationService service) =>
        typeof(ObservationService)
            .GetField("_foreground", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(service)!;
}
