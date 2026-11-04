using System.IO;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.SpecialIcons;
using DesktopIconGrid.Infrastructure;
using DesktopIconGrid.SpecialIcons;

namespace DesktopIconGrid.ViewModels;

public sealed record DriveChoice(string Root, string Label);

public sealed record GaugeStyleChoice(GaugeStyle Style, string Label, string Description);

/// <summary>Backs the properties dialog of the CPU and disk gauge icons.</summary>
public sealed class MonitorIconViewModel : ObservableObject
{
    public static readonly IReadOnlyList<GaugeStyleChoice> StyleChoices =
    [
        new(GaugeStyle.Ring, "Ring", "A ring that fills up, with the number in the middle"),
        new(GaugeStyle.Bar, "Bar", "A horizontal bar under the caption"),
        new(GaugeStyle.Number, "Number", "A large number with the caption beneath"),
    ];

    private string _name;
    private GaugeStyleChoice _selectedStyle;
    private DriveChoice? _selectedDrive;

    public MonitorIconViewModel(SpecialIconItem item, bool showsDrive, string kindName)
    {
        ArgumentNullException.ThrowIfNull(item);
        ShowsDrive = showsDrive;
        Title = $"{kindName} icon";
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
                if (value is not null && (_name.Length == 0 || _name.StartsWith("Disk ", StringComparison.Ordinal)))
                {
                    Name = $"Disk {value.Root.TrimEnd('\\')}";
                }
            }
        }
    }

    public bool CanSave => _name.Trim().Length > 0 && (!ShowsDrive || _selectedDrive is not null);

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
                        ? $"{root}  {(string.IsNullOrWhiteSpace(drive.VolumeLabel) ? drive.DriveType.ToString() : drive.VolumeLabel)} — {SystemMonitor.FormatBytes(drive.AvailableFreeSpace)} free of {SystemMonitor.FormatBytes(drive.TotalSize)}"
                        : $"{root}  (not ready)";
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    label = $"{root}  (not available)";
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
