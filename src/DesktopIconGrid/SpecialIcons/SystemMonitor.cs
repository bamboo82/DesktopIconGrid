using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using DesktopIconGrid.Resources;

namespace DesktopIconGrid.SpecialIcons;

/// <summary>Samples system values on a timer and pushes them into the registered gauge states.</summary>
public sealed class SystemMonitor : IDisposable
{
    public static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(2);

    private readonly DispatcherTimer _timer;
    private readonly List<Registration> _registrations = [];
    private int _tick;

    public SystemMonitor(Dispatcher dispatcher)
    {
        _timer = new DispatcherTimer(TickInterval, DispatcherPriority.Background, (_, _) => Tick(), dispatcher);
        _timer.Stop();
        Cpu = new CpuSampler();
    }

    public CpuSampler Cpu { get; }

    /// <summary>Samples immediately, then every <paramref name="everyTicks"/> ticks until the state is detached.</summary>
    public void Register(GaugeState state, Func<(double? Percent, string Detail)> sample, int everyTicks)
    {
        ArgumentNullException.ThrowIfNull(state);
        _registrations.Add(new Registration(state, sample, Math.Max(1, everyTicks)));
        Apply(state, sample);
        if (!_timer.IsEnabled)
        {
            _timer.Start();
        }
    }

    public void Dispose() => _timer.Stop();

    private void Tick()
    {
        _tick++;
        _registrations.RemoveAll(r => r.State.IsDetached);
        if (_registrations.Count == 0)
        {
            _timer.Stop();
            return;
        }

        foreach (var registration in _registrations)
        {
            if (_tick % registration.EveryTicks == 0)
            {
                Apply(registration.State, registration.Sample);
            }
        }
    }

    private static void Apply(GaugeState state, Func<(double? Percent, string Detail)> sample)
    {
        try
        {
            var (percent, detail) = sample();
            state.Update(percent, detail);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            state.Update(null, ex.Message);
        }
    }

    private sealed record Registration(GaugeState State, Func<(double? Percent, string Detail)> Sample, int EveryTicks);

    /// <summary>Disk space of one drive.</summary>
    public static (double? Percent, string Detail) SampleDrive(string? root)
    {
        if (string.IsNullOrEmpty(root))
        {
            return (null, Strings.Monitor_NoDrive);
        }

        var drive = new DriveInfo(root);
        if (!drive.IsReady)
        {
            return (null, string.Format(Strings.Monitor_DriveNotReadyShort, root));
        }

        var total = drive.TotalSize;
        if (total <= 0)
        {
            return (null, string.Format(Strings.Monitor_NoCapacity, root));
        }

        var used = total - drive.TotalFreeSpace;
        var percent = used * 100.0 / total;
        var label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? root : $"{root} ({drive.VolumeLabel})";
        return (percent, string.Format(Strings.Monitor_DriveSummary, label, FormatBytes(drive.AvailableFreeSpace), FormatBytes(total), Math.Round(percent)));
    }

    public static string FormatBytes(long bytes)
    {
        const double kb = 1024, mb = kb * 1024, gb = mb * 1024, tb = gb * 1024;
        return bytes switch
        {
            >= (long)tb => string.Format(CultureInfo.CurrentCulture, "{0:0.##} TB", bytes / tb),
            >= (long)gb => string.Format(CultureInfo.CurrentCulture, "{0:0.#} GB", bytes / gb),
            >= (long)mb => string.Format(CultureInfo.CurrentCulture, "{0:0} MB", bytes / mb),
            _ => string.Format(CultureInfo.CurrentCulture, "{0:0} KB", bytes / kb),
        };
    }
}

/// <summary>Overall CPU usage from the kernel's idle/busy time counters; the first sample only primes the baseline.</summary>
public sealed class CpuSampler
{
    private ulong _lastIdle;
    private ulong _lastTotal;
    private bool _primed;
    private double? _last;

    public (double? Percent, string Detail) Sample()
    {
        if (!NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            return (null, Strings.Monitor_CpuUnavailable);
        }

        var idleTicks = ToTicks(idle);
        var totalTicks = ToTicks(kernel) + ToTicks(user); // kernel time includes idle
        if (_primed && totalTicks > _lastTotal)
        {
            var busy = 1.0 - (double)(idleTicks - _lastIdle) / (totalTicks - _lastTotal);
            _last = Math.Clamp(busy * 100, 0, 100);
        }

        _lastIdle = idleTicks;
        _lastTotal = totalTicks;
        _primed = true;

        return (_last, _last is { } p
            ? string.Format(Strings.Monitor_CpuBusy, Math.Round(p), Environment.ProcessorCount)
            : Strings.Monitor_CpuMeasuring);
    }

    private static ulong ToTicks(NativeMethods.FILETIME time) => ((ulong)time.High << 32) | time.Low;

    private static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct FILETIME
        {
            public uint Low;
            public uint High;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetSystemTimes(out FILETIME idleTime, out FILETIME kernelTime, out FILETIME userTime);
    }
}
