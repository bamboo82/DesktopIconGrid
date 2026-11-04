using System.Diagnostics;
using System.IO;
using System.Windows;
using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.SpecialIcons;
using DesktopIconGrid.Resources;
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
        Monitor.Register(state, () => SystemMonitor.SampleDrive(settings.Drive), everyTicks: 5);
        return state;
    }

    public override LaunchResult Activate(SpecialIconItem item, ActivationModifiers modifiers)
    {
        var drive = MonitorIconSettings.From(item).Drive;
        return drive is null ? LaunchResult.Failure(Strings.Monitor_NoDriveSelected) : Open(drive);
    }
}
