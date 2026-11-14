using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Core.Models;
using DesktopIconGrid.Core.Sync;
using DesktopIconGrid.Infrastructure;
using DesktopIconGrid.Resources;
using DesktopIconGrid.SpecialIcons;

namespace DesktopIconGrid.ViewModels;

/// <summary>
/// Backs the properties dialog of a "Send to PC" icon (review 27 S2, S3): its name, which PC, and whether this PC
/// receives too. The dialog opens at once; the other PCs are looked for off the UI thread after it shows, and again on
/// ↻. Nothing is announced or changed until OK.
/// </summary>
public sealed class SendToDeviceViewModel : ObservableObject
{
    /// <summary>How long a look may take before the dialog says there was no answer; tests shorten it.</summary>
    internal static TimeSpan LookTimeout { get; set; } = TimeSpan.FromSeconds(20);

    private readonly Func<CancellationToken, Task<IReadOnlyList<InboxDevice>>> _look;
    private CancellationTokenSource? _lookCancel;
    private int _lookRequest;
    private string _name;
    private InboxDevice? _device;
    private IReadOnlyList<InboxDevice> _devices;
    private bool _isLooking = true;
    private string _status = Strings.SendTo_Looking;
    private bool _statusIsWarning;
    private bool _receives;

    /// <param name="item">The icon, edited only by <see cref="Apply"/>.</param>
    /// <param name="receives">The box's first state: ticked for a new icon, else whether the icon carries this PC's mark.</param>
    /// <param name="receivesByMenu">The menu's <em>Receive files from my other PCs</em> is on: the box shows ticked and cannot be changed.</param>
    /// <param name="look">Lists the other PCs that receive; called off the UI thread.</param>
    public SendToDeviceViewModel(SpecialIconItem item, bool receives, bool receivesByMenu, Func<CancellationToken, Task<IReadOnlyList<InboxDevice>>> look)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(look);
        _look = look;
        _name = item.Name;
        _receives = receives;
        ReceivesByMenu = receivesByMenu;

        // The PC chosen before is a choice as it was stored until the list says more, so that OK never drops it: the
        // list may be slow, fail, or lack that PC while it does not receive.
        _device = item.GetSetting(SendToDeviceHandler.DeviceIdKey) is { } id && FolderInbox.IsDeviceId(id)
            ? new InboxDevice(id.ToLowerInvariant(), FolderInbox.ShownName(item.GetSetting(SendToDeviceHandler.DeviceNameKey), id))
            : null;
        _devices = _device is null ? [] : [_device];
    }

    public IReadOnlyList<InboxDevice> Devices
    {
        get => _devices;
        private set
        {
            if (SetProperty(ref _devices, value))
            {
                OnPropertyChanged(nameof(CanChooseDevice));
            }
        }
    }

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

    /// <summary>A look is under way: the list and ↻ wait for it.</summary>
    public bool IsLooking
    {
        get => _isLooking;
        private set
        {
            if (SetProperty(ref _isLooking, value))
            {
                OnPropertyChanged(nameof(CanLook));
                OnPropertyChanged(nameof(CanChooseDevice));
            }
        }
    }

    public bool CanLook => !_isLooking;

    public bool CanChooseDevice => !_isLooking && _devices.Count > 0;

    /// <summary>The one line under the list: looking, the hint, why the list is empty, or why it could not be made.</summary>
    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public bool StatusIsWarning
    {
        get => _statusIsWarning;
        private set => SetProperty(ref _statusIsWarning, value);
    }

    public bool ReceivesByMenu { get; }

    public bool CanChangeReceives => !ReceivesByMenu;

    /// <summary>The user's choice for this icon: OK sets its mark on this PC, or takes it away (unless the menu decides).</summary>
    public bool Receives => _receives;

    /// <summary>What the box shows: ticked while the menu turns receiving on, whatever the icon's mark.</summary>
    public bool ReceivesShown
    {
        get => ReceivesByMenu || _receives;
        set
        {
            if (!ReceivesByMenu && _receives != value)
            {
                _receives = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Receives));
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    /// <summary>A name, and a PC or the box ticked: the first PC can be set to receive before there is another to choose.</summary>
    public bool CanSave => _name.Trim().Length > 0 && (_device is not null || ReceivesShown);

    /// <summary>Starts a look (the window shows, or ↻); a later one replaces one still under way.</summary>
    public async void Look()
    {
        try
        {
            await LookAsync();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // async void: nothing may escape to the dispatcher. LookAsync catches what the look throws already.
            AppLog.Default.Warning("Inbox", $"The other PCs could not be listed: {ex.Message}");
        }
    }

    public async Task LookAsync()
    {
        var request = ++_lookRequest;
        _lookCancel?.Cancel();
        var cancel = _lookCancel = new CancellationTokenSource(LookTimeout);
        IsLooking = true;
        Status = Strings.SendTo_Looking;
        StatusIsWarning = false;

        IReadOnlyList<InboxDevice>? found = null;
        string? failure = null;
        try
        {
            var work = Task.Run(() => _look(cancel.Token));
            if (await Task.WhenAny(work, Task.Delay(LookTimeout)) == work)
            {
                found = await work;
            }
            else
            {
                failure = Strings.SendTo_NoAnswer; // a sync folder on a share that sleeps does not watch the token
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            failure = ex is OperationCanceledException ? Strings.SendTo_NoAnswer : ex.Message;
            if (request == _lookRequest)
            {
                AppLog.Default.Warning("Inbox", $"The other PCs could not be listed: {ex.Message}");
            }
        }

        if (request != _lookRequest)
        {
            return; // a newer look, or the dialog closed
        }

        IsLooking = false;
        if (found is null)
        {
            Status = string.Format(Strings.SendTo_LookFailed, failure);
            StatusIsWarning = true;
            return;
        }

        var current = _device;
        var listed = current is null ? null : found.FirstOrDefault(device => string.Equals(device.Id, current.Id, StringComparison.OrdinalIgnoreCase));
        Devices = current is not null && listed is null ? [.. found, current] : found; // the PC chosen before stays a choice
        _device = listed ?? current ?? found.FirstOrDefault(); // as the list gives it, and the icon is not renamed for it
        OnPropertyChanged(nameof(SelectedDevice));
        OnPropertyChanged(nameof(CanSave));
        Status = found.Count == 0 ? Strings.SendTo_NoOtherPcs : Strings.SendTo_Hint;
        StatusIsWarning = found.Count == 0;
    }

    /// <summary>The dialog closed: a look under way is given up and its answer ignored.</summary>
    public void Cancel()
    {
        _lookRequest++;
        _lookCancel?.Cancel();
    }

    public void Apply(SpecialIconItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.Name = _name.Trim();
        item.SetSetting(SendToDeviceHandler.DeviceIdKey, _device?.Id);
        item.SetSetting(SendToDeviceHandler.DeviceNameKey, _device?.Name);
    }
}
