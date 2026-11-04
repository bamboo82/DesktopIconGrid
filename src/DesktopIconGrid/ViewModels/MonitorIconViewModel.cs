using System.IO;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.SpecialIcons;
using DesktopIconGrid.Infrastructure;
using DesktopIconGrid.Resources;
using DesktopIconGrid.SpecialIcons;

namespace DesktopIconGrid.ViewModels;

public sealed record DriveChoice(string Root, string Label);

public sealed record GaugeStyleChoice(GaugeStyle Style, string Label, string Description);

/// <summary>Backs the properties dialog of the CPU and disk gauge icons.</summary>
public sealed class MonitorIconViewModel : ObservableObject
{
    public static readonly IReadOnlyList<GaugeStyleChoice> StyleChoices =
    [
        new(GaugeStyle.Ring, Strings.Monitor_Ring, Strings.Monitor_RingDesc),
        new(GaugeStyle.Bar, Strings.Monitor_Bar, Strings.Monitor_BarDesc),
        new(GaugeStyle.Number, Strings.Monitor_Number, Strings.Monitor_NumberDesc),
    ];

    private string _name;
    private GaugeStyleChoice _selectedStyle;
    private DriveChoice? _selectedDrive;

    public MonitorIconViewModel(SpecialIconItem item, bool showsDrive, string kindName)
    {
        ArgumentNullException.ThrowIfNull(item);
        ShowsDrive = showsDrive;
        Title = string.Format(Strings.Monitor_Title, kindName);
        _name = item.Name;

        var settings = MonitorIconSettings.From(item);
        _selectedStyle = StyleChoices.First(c => c.Style == settings.Style);

        Drives = showsDrive ? LoadDrives() : [];
        _selectedDrive = Drives.FirstOrDefault(d => string.Equals(d.Root, settings.Drive, StringComparison.OrdinalIgnoreCase))
                         ?? Drives.FirstOrDefault();
    }

    public string Title { get; }

    public bool ShowsDrive { get; }

    public IReadOnlyList<DriveChoice> Drives { get; }

    public IReadOnlyList<GaugeStyleChoice> Styles => StyleChoices;

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    public GaugeStyleChoice SelectedStyle
    {
        get => _selectedStyle;
        set
        {
            if (value is not null)
            {
                SetProperty(ref _selectedStyle, value);
            }
        }
    }

    public DriveChoice? SelectedDrive
    {
        get => _selectedDrive;
        set
        {
            if (SetProperty(ref _selectedDrive, value))
            {
                OnPropertyChanged(nameof(CanSave));
                if (value is not null && IsAutomaticName(_name))
                {
                    Name = string.Format(Strings.Monitor_DiskName, value.Root.TrimEnd('\\'));
                }
            }
        }
    }

    public bool CanSave => _name.Trim().Length > 0 && (!ShowsDrive || _selectedDrive is not null);

    /// <summary>True for an empty name or one the app generated for a drive (in this or the English wording of layouts saved before 0.5.0).</summary>
    private bool IsAutomaticName(string name) =>
        name.Length == 0
        || name.StartsWith("Disk ", StringComparison.Ordinal)
        || Drives.Any(d => name == string.Format(Strings.Monitor_DiskName, d.Root.TrimEnd('\\')));

    public void Apply(SpecialIconItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.Name = _name.Trim();
        new MonitorIconSettings(_selectedStyle.Style, ShowsDrive ? _selectedDrive?.Root : null).ApplyTo(item);
    }

    private static IReadOnlyList<DriveChoice> LoadDrives()
    {
        var drives = new List<DriveChoice>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable or DriveType.Network))
                {
                    continue;
                }

                var root = MonitorIconSettings.NormalizeDrive(drive.Name);
                if (root is null)
                {
                    continue;
                }

                string label;
                try
                {
                    label = drive.IsReady
                        ? string.Format(
                            Strings.Monitor_DriveFree,
                            root,
                            string.IsNullOrWhiteSpace(drive.VolumeLabel) ? drive.DriveType.ToString() : drive.VolumeLabel,
                            SystemMonitor.FormatBytes(drive.AvailableFreeSpace),
                            SystemMonitor.FormatBytes(drive.TotalSize))
                        : string.Format(Strings.Monitor_DriveNotReady, root);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    label = string.Format(Strings.Monitor_DriveNotAvailable, root);
                }

                drives.Add(new DriveChoice(root, label));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Fall through with whatever was collected.
        }

        return drives;
    }
}
