using System.Diagnostics;
using System.IO;
using System.Windows;
using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.SpecialIcons;
using DesktopIconGrid.Resources;
using DesktopIconGrid.Services;
using DesktopIconGrid.ViewModels;
using DesktopIconGrid.Views;

namespace DesktopIconGrid.SpecialIcons;

/// <summary>Shared pieces of the CPU and disk gauge icons.</summary>
public abstract class MonitorHandlerBase(SystemMonitor monitor, Func<Window?> ownerProvider) : ISpecialIconHandler
{
    protected SystemMonitor Monitor { get; } = monitor;

    public abstract string Kind { get; }

    public abstract string DisplayName { get; }

    public abstract string Description { get; }

    protected abstract bool ShowsDrive { get; }

    public abstract SpecialIconItem CreateItem();

    public abstract GaugeState? CreateState(SpecialIconItem item);

    public abstract LaunchResult Activate(SpecialIconItem item, ActivationModifiers modifiers);

    /// <summary>
    /// Declared here, virtual, so a gauge can use the wheel: the interface's default method is bound where the interface
    /// is declared (this class), and a method of the same name on a subclass alone would never be called through it.
    /// </summary>
    public virtual bool Wheel(SpecialIconItem item, int notches) => false;

    public bool ShowProperties(SpecialIconItem item, bool isNew)
    {
        var viewModel = new MonitorIconViewModel(item, ShowsDrive, DisplayName);
        var window = new MonitorIconWindow { DataContext = viewModel, Owner = ownerProvider() };
        if (window.ShowDialog() != true)
        {
            return false;
        }

        viewModel.Apply(item);
        return true;
    }

    protected static LaunchResult Open(string target)
    {
        var result = ProcessLauncher.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        result.Process?.Dispose();
        return result;
    }
}

/// <summary>Shows overall CPU usage; click opens Task Manager.</summary>
public sealed class CpuUsageHandler(SystemMonitor monitor, Func<Window?> ownerProvider) : MonitorHandlerBase(monitor, ownerProvider)
{
    public override string Kind => SpecialIconKinds.CpuUsage;

    public override string DisplayName => Strings.Monitor_CpuName;

    public override string Description => Strings.Monitor_CpuDesc;

    protected override bool ShowsDrive => false;

    public override SpecialIconItem CreateItem()
    {
        var item = new SpecialIconItem { Name = "CPU", Kind = Kind };
        new MonitorIconSettings(GaugeStyle.Ring, null).ApplyTo(item);
        return item;
    }

    public override GaugeState CreateState(SpecialIconItem item)
    {
        var settings = MonitorIconSettings.From(item);
        var state = new GaugeState("CPU", settings.Style, warningAt: 70, criticalAt: 90, Strings.Monitor_CpuMeasuring);
        Monitor.Register(state, Monitor.Cpu.Sample, everyTicks: 1);
        return state;
    }

    public override LaunchResult Activate(SpecialIconItem item, ActivationModifiers modifiers) => Open("taskmgr.exe");
}

/// <summary>Shows how full a drive is; click opens the drive in File Explorer.</summary>
public sealed class DiskUsageHandler(SystemMonitor monitor, Func<Window?> ownerProvider) : MonitorHandlerBase(monitor, ownerProvider)
{
    public override string Kind => SpecialIconKinds.DiskUsage;

    public override string DisplayName => Strings.Monitor_DiskUsageName;

    public override string Description => Strings.Monitor_DiskDesc;

    protected override bool ShowsDrive => true;

    public override SpecialIconItem CreateItem()
    {
        var drive = MonitorIconSettings.NormalizeDrive(Path.GetPathRoot(Environment.SystemDirectory)) ?? "C:\\";
        var item = new SpecialIconItem { Name = string.Format(Strings.Monitor_DiskName, drive.TrimEnd('\\')), Kind = Kind };
        new MonitorIconSettings(GaugeStyle.Ring, drive).ApplyTo(item);
        return item;
    }

    public override GaugeState CreateState(SpecialIconItem item)
    {
        var settings = MonitorIconSettings.From(item);
        var caption = settings.Drive?.TrimEnd('\\') ?? Strings.Monitor_Disk;
        var state = new GaugeState(caption, settings.Style, warningAt: 80, criticalAt: 90, Strings.Monitor_ReadingDrive);
        Monitor.RegisterSlow(state, () => SystemMonitor.SampleDrive(settings.Drive), reading => reading, everyTicks: 5); // a network drive can take seconds
        return state;
    }

    public override LaunchResult Activate(SpecialIconItem item, ActivationModifiers modifiers)
    {
        var drive = MonitorIconSettings.From(item).Drive;
        return drive is null ? LaunchResult.Failure(Strings.Monitor_NoDriveSelected) : Open(drive);
    }
}

/// <summary>Shows how much physical memory is in use; click opens Task Manager.</summary>
public sealed class MemoryUsageHandler(SystemMonitor monitor, Func<Window?> ownerProvider) : MonitorHandlerBase(monitor, ownerProvider)
{
    public override string Kind => SpecialIconKinds.MemoryUsage;

    public override string DisplayName => Strings.Monitor_MemoryName;

    public override string Description => Strings.Monitor_MemoryDesc;

    protected override bool ShowsDrive => false;

    public override SpecialIconItem CreateItem()
    {
        var item = new SpecialIconItem { Name = "RAM", Kind = Kind };
        new MonitorIconSettings(GaugeStyle.Ring, null).ApplyTo(item);
        return item;
    }

    public override GaugeState CreateState(SpecialIconItem item)
    {
        var state = new GaugeState("RAM", MonitorIconSettings.From(item).Style, warningAt: 80, criticalAt: 92, Strings.Monitor_MemoryName);
        Monitor.Register(state, SystemMonitor.SampleMemory, everyTicks: 2);
        return state;
    }

    public override LaunchResult Activate(SpecialIconItem item, ActivationModifiers modifiers) => Open("taskmgr.exe");
}

/// <summary>Shows the battery's charge (low is the bad end, while on battery); click opens the battery settings.</summary>
public sealed class BatteryHandler(SystemMonitor monitor, Func<Window?> ownerProvider) : MonitorHandlerBase(monitor, ownerProvider)
{
    public override string Kind => SpecialIconKinds.Battery;

    public override string DisplayName => Strings.Monitor_BatteryName;

    public override string Description => Strings.Monitor_BatteryDesc;

    protected override bool ShowsDrive => false;

    public override SpecialIconItem CreateItem()
    {
        var item = new SpecialIconItem { Name = Strings.Monitor_BatteryName, Kind = Kind };
        new MonitorIconSettings(GaugeStyle.Ring, null).ApplyTo(item);
        return item;
    }

    public override GaugeState CreateState(SpecialIconItem item)
    {
        var state = new GaugeState("BAT", MonitorIconSettings.From(item).Style, warningAt: 101, criticalAt: 101, Strings.Monitor_BatteryName);
        Monitor.Register(
            state,
            () =>
            {
                var (percent, onBattery, detail) = SystemMonitor.SampleBattery();
                state.Level = SystemMonitor.BatteryLevel(percent, onBattery); // the thresholds run the other way: the colour is set here
                return (percent, detail);
            },
            everyTicks: 15);
        return state;
    }

    public override LaunchResult Activate(SpecialIconItem item, ActivationModifiers modifiers) => Open("ms-settings:batterysaver");
}

/// <summary>
/// Shows how fast the network receives (the tile's text) and sends; the ring or bar is the download rate against its
/// peak of the last two minutes. Click opens the network settings. It reads counters only and contacts nothing.
/// </summary>
public sealed class NetworkHandler(SystemMonitor monitor, Func<Window?> ownerProvider) : MonitorHandlerBase(monitor, ownerProvider)
{
    public override string Kind => SpecialIconKinds.Network;

    public override string DisplayName => Strings.Monitor_NetworkName;

    public override string Description => Strings.Monitor_NetworkDesc;

    protected override bool ShowsDrive => false;

    public override SpecialIconItem CreateItem()
    {
        var item = new SpecialIconItem { Name = Strings.Monitor_NetworkName, Kind = Kind };
        new MonitorIconSettings(GaugeStyle.Number, null).ApplyTo(item);
        return item;
    }

    public override GaugeState CreateState(SpecialIconItem item)
    {
        var state = new GaugeState("NET", MonitorIconSettings.From(item).Style, warningAt: 101, criticalAt: 101, Strings.Monitor_NetworkMeasuring)
        {
            Level = CommandLevel.Ok, // a busy network is not a warning
            LabelText = "…",
        };
        var peak = new GaugeHistory();
        IReadOnlyDictionary<string, (long Received, long Sent)>? before = null;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Monitor.RegisterSlow(
            state,
            SystemMonitor.ReadNetworkCounters, // about 50 ms with many adapters: not on the UI thread
            now =>
            {
                var seconds = watch.Elapsed.TotalSeconds;
                watch.Restart();
                // Adapter by adapter: one that just came up does not add its lifetime counter (review 24 R4, G2).
                var moved = before is null ? ((long Received, long Sent)?)null : NetworkRate.Delta(before, now);
                var down = moved is { } m && seconds > 0 ? m.Received / seconds : (double?)null;
                var up = moved is { } n && seconds > 0 ? n.Sent / seconds : (double?)null;
                before = now;
                state.Record(down, max: null);
                peak.Add(down);
                if (down is null || up is null)
                {
                    state.LabelText = "…";
                    return (null, Strings.Monitor_NetworkMeasuring);
                }

                state.LabelText = NetworkRate.Short(down.Value);
                var percent = peak.Peak > 0 ? down.Value * 100 / peak.Peak : 0;
                return (percent, string.Format(Strings.Monitor_NetworkSummary, SystemMonitor.FormatBytes((long)down.Value), SystemMonitor.FormatBytes((long)up.Value)));
            },
            everyTicks: 1,
            recordsPercent: false);
        return state;
    }

    public override LaunchResult Activate(SpecialIconItem item, ActivationModifiers modifiers) => Open("ms-settings:network-status");
}

/// <summary>
/// The default microphone's mute switch: a click mutes or unmutes it for every program, red while muted. It follows
/// mutes made elsewhere (checked every 2 seconds); the ring or bar is the microphone's level.
/// </summary>
public sealed class MicrophoneMuteHandler(SystemMonitor monitor, Func<Window?> ownerProvider) : MonitorHandlerBase(monitor, ownerProvider)
{
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<SpecialIconItem, GaugeState> _states = [];

    public override string Kind => SpecialIconKinds.MicrophoneMute;

    public override string DisplayName => Strings.Mic_Name;

    public override string Description => Strings.Mic_Desc;

    protected override bool ShowsDrive => false;

    public override SpecialIconItem CreateItem()
    {
        var item = new SpecialIconItem { Name = Strings.Mic_Name, Kind = Kind };
        new MonitorIconSettings(GaugeStyle.Number, null).ApplyTo(item);
        return item;
    }

    public override GaugeState CreateState(SpecialIconItem item)
    {
        var state = new GaugeState("MIC", MonitorIconSettings.From(item).Style, warningAt: 101, criticalAt: 101, Strings.Mic_Name);
        _states.AddOrUpdate(item, state);
        Monitor.RegisterSlow(state, MicrophoneControl.Read, reading => Show(state, reading), everyTicks: 1, recordsPercent: false);
        return state;
    }

    public override LaunchResult Activate(SpecialIconItem item, ActivationModifiers modifiers)
    {
        if (MicrophoneControl.Toggle() is null)
        {
            return LaunchResult.Failure(Strings.Mic_None);
        }

        if (_states.TryGetValue(item, out var state))
        {
            var (percent, detail) = Show(state, MicrophoneControl.Read());
            state.Update(percent, detail); // at once, not at the next sample
        }

        return LaunchResult.Started;
    }

    /// <summary>Sets the tile's text and colour for a reading and returns its level and tooltip.</summary>
    internal static (double? Percent, string Detail) Show(GaugeState state, (bool Muted, double Volume)? reading)
    {
        if (reading is not { } mic)
        {
            state.LabelText = "—";
            state.Level = CommandLevel.Unknown;
            return (null, Strings.Mic_None);
        }

        state.LabelText = mic.Muted ? Strings.Mic_LabelMuted : Strings.Mic_LabelOn;
        state.Level = mic.Muted ? CommandLevel.Failed : CommandLevel.Ok;
        return (mic.Volume, string.Format(mic.Muted ? Strings.Mic_Muted : Strings.Mic_On, Math.Round(mic.Volume)));
    }
}

/// <summary>
/// The speakers' volume as a gauge: the mouse wheel over the icon turns it up and down, a click mutes or unmutes
/// (red while muted). It follows changes made elsewhere (checked every 2 seconds).
/// </summary>
public sealed class VolumeHandler(SystemMonitor monitor, Func<Window?> ownerProvider) : MonitorHandlerBase(monitor, ownerProvider)
{
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<SpecialIconItem, GaugeState> _states = [];

    public override string Kind => SpecialIconKinds.Volume;

    public override string DisplayName => Strings.Volume_Name;

    public override string Description => Strings.Volume_Desc;

    protected override bool ShowsDrive => false;

    public override SpecialIconItem CreateItem()
    {
        var item = new SpecialIconItem { Name = Strings.Volume_Name, Kind = Kind };
        new MonitorIconSettings(GaugeStyle.Ring, null).ApplyTo(item);
        return item;
    }

    public override GaugeState CreateState(SpecialIconItem item)
    {
        var state = new GaugeState("VOL", MonitorIconSettings.From(item).Style, warningAt: 101, criticalAt: 101, Strings.Volume_Name);
        _states.AddOrUpdate(item, state);
        Monitor.RegisterSlow(state, MicrophoneControl.ReadSpeakers, reading => Show(state, reading), everyTicks: 1, recordsPercent: false);
        return state;
    }

    public override LaunchResult Activate(SpecialIconItem item, ActivationModifiers modifiers)
    {
        if (MicrophoneControl.ToggleSpeakers() is null)
        {
            return LaunchResult.Failure(Strings.Volume_None);
        }

        Redraw(item, MicrophoneControl.ReadSpeakers());
        return LaunchResult.Started;
    }

    public override bool Wheel(SpecialIconItem item, int notches)
    {
        if (notches != 0)
        {
            Redraw(item, MicrophoneControl.StepSpeakers(notches)); // at once: the wheel must feel direct
        }

        return true;
    }

    private void Redraw(SpecialIconItem item, (bool Muted, double Volume)? reading)
    {
        if (_states.TryGetValue(item, out var state))
        {
            var (percent, detail) = Show(state, reading);
            state.Update(percent, detail);
        }
    }

    /// <summary>Sets the tile's text and colour for a reading and returns its level and tooltip.</summary>
    internal static (double? Percent, string Detail) Show(GaugeState state, (bool Muted, double Volume)? reading)
    {
        if (reading is not { } speakers)
        {
            state.LabelText = "—";
            state.Level = CommandLevel.Unknown;
            return (null, Strings.Volume_None);
        }

        state.LabelText = speakers.Muted ? Strings.Mic_LabelMuted : Math.Round(speakers.Volume).ToString(System.Globalization.CultureInfo.CurrentCulture);
        state.Level = speakers.Muted ? CommandLevel.Failed : CommandLevel.Ok;
        return (speakers.Volume, string.Format(speakers.Muted ? Strings.Volume_Muted : Strings.Volume_On, Math.Round(speakers.Volume)));
    }
}
