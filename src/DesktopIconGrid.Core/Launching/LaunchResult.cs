using System.Diagnostics;

namespace DesktopIconGrid.Core.Launching;

public enum LaunchOutcome
{
    Started,

    /// <summary>The user dismissed a prompt (for example the elevation consent dialog).</summary>
    Cancelled,

    Failed,

    /// <summary>
    /// The program was in front, and the click minimised its window: nothing was started or brought forward, so no use
    /// is counted (0.12.2, review 29 N2).
    /// </summary>
    Minimized,
}

public sealed record LaunchResult(LaunchOutcome Outcome, string? ErrorMessage = null)
{
    public static readonly LaunchResult Started = new(LaunchOutcome.Started);
    public static readonly LaunchResult Cancelled = new(LaunchOutcome.Cancelled);

    public static LaunchResult Failure(string message) => new(LaunchOutcome.Failed, message);

    public bool Succeeded => Outcome == LaunchOutcome.Started;

    /// <summary>The started process when Windows created a new one; the caller owns and disposes it.</summary>
    public Process? Process { get; init; }
}
