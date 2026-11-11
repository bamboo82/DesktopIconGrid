using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using DesktopIconGrid.Core.SpecialIcons;
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

    /// <summary>
    /// Samples immediately, then every <paramref name="everyTicks"/> ticks until the state is detached. Each percentage is
    /// kept for the tooltip's sparkline unless <paramref name="recordsPercent"/> is false (the sample records its own value).
    /// </summary>
    public void Register(GaugeState state, Func<(double? Percent, string Detail)> sample, int everyTicks, bool recordsPercent = true)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (recordsPercent)
        {
            var inner = sample;
            sample = () =>
            {
                var reading = inner();
                state.Record(reading.Percent, 100);
                return reading;
            };
        }

        _registrations.Add(new Registration(state, sample, Math.Max(1, everyTicks)));
        Apply(state, sample);
        if (!_timer.IsEnabled)
        {
            _timer.Start();
        }
    }

    /// <summary>
    /// As <see cref="Register"/> for a value that can be slow to read (the adapters' counters, a network drive, a COM
    /// call): <paramref name="read"/> runs on a worker thread, <paramref name="show"/> on the UI thread with what it
    /// read. One read at a time per gauge; a tick that comes while one is under way is skipped (review 24 G1).
    /// </summary>
    public void RegisterSlow<T>(GaugeState state, Func<T> read, Func<T, (double? Percent, string Detail)> show, int everyTicks, bool recordsPercent = true)
    {
        ArgumentNullException.ThrowIfNull(state);
        var busy = false;
        async void Sample()
        {
            if (busy || state.IsDetached)
            {
                return;
            }

            busy = true;
            try
            {
                var value = await Task.Run(read);
                if (state.IsDetached)
                {
                    return;
                }

                var (percent, detail) = show(value);
                if (recordsPercent)
                {
                    state.Record(percent, 100);
                }

                state.Update(percent, detail);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // async void: nothing may escape to the dispatcher.
                state.Update(null, ex.Message);
            }
            finally
            {
                busy = false;
            }
        }

        _slow.Add(new SlowRegistration(state, Sample, Math.Max(1, everyTicks)));
        Sample();
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
        _slow.RemoveAll(r => r.State.IsDetached);
        if (_registrations.Count == 0 && _slow.Count == 0)
        {
            _timer.Stop();
            return;
        }

        foreach (var registration in _slow)
        {
            if (_tick % registration.EveryTicks == 0)
            {
                registration.Sample();
            }
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException
                                       or System.Net.NetworkInformation.NetworkInformationException)
        {
            state.Update(null, ex.Message);
        }
    }

    private static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORYSTATUSEX
        {
            public uint Length;
            public uint MemoryLoad;
            public ulong TotalPhysical;
            public ulong AvailablePhysical;
            public ulong TotalPageFile;
            public ulong AvailablePageFile;
            public ulong TotalVirtual;
            public ulong AvailableVirtual;
            public ulong AvailableExtendedVirtual;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public uint BatteryLifeTime;
            public uint BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);
    }

    private readonly List<SlowRegistration> _slow = [];

    private sealed record SlowRegistration(GaugeState State, Action Sample, int EveryTicks);

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

    /// <summary>Physical memory in use.</summary>
    public static (double? Percent, string Detail) SampleMemory()
    {
        var status = new NativeMethods.MEMORYSTATUSEX { Length = (uint)Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>() };
        if (!NativeMethods.GlobalMemoryStatusEx(ref status) || status.TotalPhysical == 0)
        {
            return (null, Strings.Monitor_MemoryUnavailable);
        }

        var used = status.TotalPhysical - status.AvailablePhysical;
        var percent = used * 100.0 / status.TotalPhysical;
        return (percent, string.Format(Strings.Monitor_MemorySummary, FormatBytes((long)used), FormatBytes((long)status.TotalPhysical), Math.Round(percent)));
    }

    /// <summary>The battery's charge, whether it is charging, or that there is none.</summary>
    public static (double? Percent, bool OnBattery, string Detail) SampleBattery()
    {
        if (!NativeMethods.GetSystemPowerStatus(out var status))
        {
            return (null, false, Strings.Monitor_BatteryUnavailable);
        }

        return ReadBattery(status.BatteryLifePercent, status.BatteryFlag, status.ACLineStatus);
    }

    /// <summary>
    /// Reads the three bytes of <c>SYSTEM_POWER_STATUS</c>: a percentage of 255 or flag 128 means no battery (a desktop PC),
    /// line status 1 is mains power, flag 8 charging.
    /// </summary>
    internal static (double? Percent, bool OnBattery, string Detail) ReadBattery(byte percent, byte flag, byte line)
    {
        if (percent > 100 || (flag & 128) != 0)
        {
            return (null, false, Strings.Monitor_NoBattery);
        }

        var onBattery = line == 0;
        var text = (flag & 8) != 0 ? Strings.Monitor_BatteryCharging : onBattery ? Strings.Monitor_BatteryOn : Strings.Monitor_BatteryPlugged;
        return (percent, onBattery, string.Format(text, percent));
    }

    /// <summary>The battery gauge's colour: a low charge is the bad end, and only while nothing charges it.</summary>
    internal static CommandLevel BatteryLevel(double? percent, bool onBattery) => percent switch
    {
        null => CommandLevel.Unknown,
        <= 10 when onBattery => CommandLevel.Failed,
        <= 20 when onBattery => CommandLevel.Warning,
        _ => CommandLevel.Ok,
    };

    /// <summary>
    /// Bytes received and sent so far, by adapter id, for the connected adapters that carry this PC's traffic: not a
    /// loopback or a tunnel, not a filter layer of another adapter (<see cref="NetworkRate.IsFilterLayer"/>), and, when
    /// any adapter has one, only those with a gateway (a virtual switch's host adapter repeats the card's traffic).
    /// </summary>
    public static IReadOnlyDictionary<string, (long Received, long Sent)> ReadNetworkCounters()
    {
        var all = new List<(string Id, bool HasGateway, long Received, long Sent)>();
        foreach (var adapter in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up
                || adapter.NetworkInterfaceType is System.Net.NetworkInformation.NetworkInterfaceType.Loopback or System.Net.NetworkInformation.NetworkInterfaceType.Tunnel
                || NetworkRate.IsFilterLayer(adapter.Description)
                || NetworkRate.IsFilterLayer(adapter.Name))
            {
                continue;
            }

            var statistics = adapter.GetIPStatistics();
            all.Add((adapter.Id, adapter.GetIPProperties().GatewayAddresses.Count > 0, statistics.BytesReceived, statistics.BytesSent));
        }

        var counted = all.Any(adapter => adapter.HasGateway) ? all.Where(adapter => adapter.HasGateway) : all;
        return counted.GroupBy(adapter => adapter.Id).ToDictionary(group => group.Key, group => (group.First().Received, group.First().Sent));
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
