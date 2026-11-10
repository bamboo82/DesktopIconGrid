using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.Sync;
using DesktopIconGrid.Infrastructure;
using DesktopIconGrid.Resources;
using DesktopIconGrid.SpecialIcons;

namespace DesktopIconGrid.ViewModels;

/// <summary>Backs the properties dialog of a "Send to PC" icon: its name and which PC.</summary>
public sealed class SendToDeviceViewModel : ObservableObject
{
    private string _name;
    private InboxDevice? _device;

    public SendToDeviceViewModel(SpecialIconItem item, IReadOnlyList<InboxDevice> devices)
    {
        ArgumentNullException.ThrowIfNull(item);
        Devices = devices;
        _name = item.Name;
        _device = devices.FirstOrDefault(d => string.Equals(d.Id, item.GetSetting(SendToDeviceHandler.DeviceIdKey), StringComparison.OrdinalIgnoreCase))
                  ?? devices.FirstOrDefault();
    }

    public IReadOnlyList<InboxDevice> Devices { get; }

    public bool HasDevices => Devices.Count > 0;

    public bool HasNoDevices => Devices.Count == 0;

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

    public InboxDevice? SelectedDevice
    {
        get => _device;
        set
        {
            var automatic = _device is null || _name == Strings.SendTo_Name || _name == string.Format(Strings.SendTo_NameFor, _device.Name);
            if (SetProperty(ref _device, value))
            {
                OnPropertyChanged(nameof(CanSave));
                if (value is not null && automatic)
                {
                    Name = string.Format(Strings.SendTo_NameFor, value.Name);
                }
            }
        }
    }

    public bool CanSave => _name.Trim().Length > 0 && _device is not null;

    public void Apply(SpecialIconItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.Name = _name.Trim();
        item.SetSetting(SendToDeviceHandler.DeviceIdKey, _device?.Id);
        item.SetSetting(SendToDeviceHandler.DeviceNameKey, _device?.Name);
    }
}
