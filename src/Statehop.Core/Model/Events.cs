namespace Statehop.Core.Model;

/// <summary>
/// The foreground application changed. Carries process identity and the
/// instant of the change — never the window title.
/// </summary>
/// <param name="Pid">Process id owning the new foreground window.</param>
/// <param name="Identity">Identity of that process.</param>
/// <param name="AtUtc">When the change was observed.</param>
public sealed record ForegroundChange(int Pid, ProcessIdentity Identity, DateTime AtUtc);

/// <summary>
/// The user went idle or came back.
///
/// "Idle" here means absence of user input (keyboard/mouse) for longer than
/// the configured threshold. It does NOT mean an application is inactive or
/// unneeded — see the UX rule in docs/PRODUCT.md ("inatividade não é
/// equivalência a inutilidade").
/// </summary>
/// <param name="IsIdle">True when entering idle, false when input resumed.</param>
/// <param name="AtUtc">When the transition was observed.</param>
public sealed record IdleChange(bool IsIdle, DateTime AtUtc);

/// <summary>Kind of process lifetime transition observed.</summary>
public enum ProcessLifetimeKind
{
    Started,
    Exited,
}

/// <summary>
/// A process appeared or disappeared between two enumeration snapshots.
/// </summary>
/// <param name="Pid">Process id.</param>
/// <param name="Identity">Identity of the process.</param>
/// <param name="Kind">Started or exited.</param>
/// <param name="ObservedUtc">When the snapshot diff detected it.</param>
/// <param name="ProcessStartUtc">
/// Real start time from GetProcessTimes, when readable. More precise than
/// <paramref name="ObservedUtc"/>, which is bounded by the polling interval.
/// </param>
public sealed record ProcessLifetimeChange(
    int Pid,
    ProcessIdentity Identity,
    ProcessLifetimeKind Kind,
    DateTime ObservedUtc,
    DateTime? ProcessStartUtc);

/// <summary>
/// One row of the B0.2 measurement: a process that owns a visible top-level
/// window, and whether this unelevated app could read its identity.
/// </summary>
/// <param name="Name">Process name.</param>
/// <param name="AccessState">Accessible or Denied.</param>
/// <param name="IsSystemProtected">
/// True for processes that are inaccessible to any unelevated app regardless
/// of packaging (see docs/adr/001-packaging.md). Excluded from the headline
/// B0.2 number, because they are not "the user's apps".
/// </param>
public sealed record WindowOwnerProbe(
    string Name,
    ProcessAccessState AccessState,
    bool IsSystemProtected);
