using System.Globalization;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Core.Health;
using DesktopIconGrid.Infrastructure;
using DesktopIconGrid.Resources;
using DesktopIconGrid.Services;

namespace DesktopIconGrid.ViewModels;

/// <summary>
/// The health light of one shortcut: checks on a timer (<see cref="HealthProbe"/>) and keeps the status, the latency
/// and when it last changed. A change between up and down goes to the log (<em>Health</em>) and, when asked, to a
/// notification. Stopped when the icon leaves the bar or its check is edited.
/// </summary>
public sealed class HealthLight : ObservableObject
{
    private static readonly SolidColorBrush UpBrush = Frozen(Color.FromRgb(0x34, 0xC7, 0x59));
    private static readonly SolidColorBrush DownBrush = Frozen(Color.FromRgb(0xE5, 0x48, 0x4D));
    private static readonly SolidColorBrush UnknownBrush = Frozen(Color.FromRgb(0x9A, 0x9A, 0x9A));

    private readonly string _name;
    private readonly HealthCheck _check;
    private readonly bool _notify;
    private readonly Action<string, string>? _notifier;
    private readonly DispatcherTimer _timer;
    private readonly CancellationTokenSource _stop = new();
    private HealthStatus _status;
    private TimeSpan? _latency;
    private string? _detail;
    private DateTime? _changed;
    private bool _checking;

    public HealthLight(string name, HealthCheck check, int intervalSeconds, bool notify, Action<string, string>? notifier, Dispatcher dispatcher)
    {
        _name = name;
        _check = check;
        _notify = notify;
        _notifier = notifier;
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(HealthCheck.NormalizeInterval(intervalSeconds)), DispatcherPriority.Background, (_, _) => CheckNow(), dispatcher);
    }

    public HealthStatus Status => _status;

    public Brush Brush => _status switch
    {
        HealthStatus.Up => UpBrush,
        HealthStatus.Down => DownBrush,
        _ => UnknownBrush,
    };

    /// <summary>The tooltip line: what is checked, the answer, and since when.</summary>
    public string Text
    {
        get
        {
            var state = _status switch
            {
                HealthStatus.Up => _latency is { } latency
                    ? string.Format(Strings.Health_UpIn, Math.Round(latency.TotalMilliseconds).ToString(CultureInfo.CurrentCulture))
                    : Strings.Health_Up,
                HealthStatus.Down => _detail is null ? Strings.Health_Down : string.Format(Strings.Health_DownBecause, _detail),
                _ => Strings.Health_Unknown,
            };
            var since = _changed is { } changed ? " " + string.Format(Strings.Health_Since, changed.ToString("g", CultureInfo.CurrentCulture)) : string.Empty;
            return $"{_check.Shown}: {state}{since}";
        }
    }

    public void Start()
    {
        CheckNow();
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        _stop.Cancel();
    }

    private async void CheckNow()
    {
        if (_checking || _stop.IsCancellationRequested)
        {
            return;
        }

        _checking = true;
        try
        {
            var result = await HealthProbe.CheckAsync(_check, _stop.Token);
            Apply(result);
        }
        catch (OperationCanceledException)
        {
            // Stopped.
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // async void: nothing may escape to the dispatcher.
            Apply(new HealthResult(HealthStatus.Down, null, ex.Message));
        }
        finally
        {
            _checking = false;
        }
    }

    private void Apply(HealthResult result)
    {
        if (_stop.IsCancellationRequested)
        {
            return;
        }

        var before = _status;
        _status = result.Status;
        _latency = result.Latency;
        _detail = result.Detail;
        if (before != _status)
        {
            _changed = DateTime.Now;
            if (before != HealthStatus.Unknown || _status == HealthStatus.Down)
            {
                var message = $"\"{_name}\" ({_check.Shown}) is {(_status == HealthStatus.Up ? "up" : "down")}{(_detail is null ? string.Empty : ": " + _detail)}";
                if (_status == HealthStatus.Up)
                {
                    AppLog.Default.Info("Health", message);
                }
                else
                {
                    AppLog.Default.Warning("Health", message);
                }
            }

            if (_notify && before != HealthStatus.Unknown)
            {
                _notifier?.Invoke(_name, _status == HealthStatus.Up ? Strings.Health_NowUp : string.Format(Strings.Health_NowDown, _check.Shown, _detail ?? Strings.Health_Down));
            }
        }

        OnAllPropertiesChanged();
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
