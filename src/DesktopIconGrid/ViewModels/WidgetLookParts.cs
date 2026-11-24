using System.ComponentModel;
using DesktopIconGrid.Infrastructure;
using DesktopIconGrid.Resources;
using DesktopIconGrid.Services;

namespace DesktopIconGrid.ViewModels;

/// <summary>
/// The two parts of a widget's 文字外觀… (0.16.0 WP4, the owner's request): its value (時間 for the clock, 數值 for the
/// others) and its name, each a look of its own edited with the same controls in the text icon window, which shows one
/// part at a time (a two-button choice at its top). Switching keeps what was changed in the other part; OK saves both,
/// Cancel neither; a reset in one part leaves the other.
/// </summary>
public sealed class WidgetLookParts : ObservableObject
{
    private TextIconViewModel _current;

    public WidgetLookParts(TextIconViewModel value, TextIconViewModel name, bool isClock)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(name);
        Value = value;
        Name = name;
        ValuePartLabel = isClock ? Strings.TextLook_PartTime : Strings.TextLook_PartValue;
        _current = value;
        value.PropertyChanged += OnPartChanged;
        name.PropertyChanged += OnPartChanged;
    }

    /// <summary>The value's part, the widget's Widget mode as before 0.16.0.</summary>
    public TextIconViewModel Value { get; }

    /// <summary>The name's part.</summary>
    public TextIconViewModel Name { get; }

    /// <summary>The value part's button: 時間 for the clock, 數值 for the others.</summary>
    public string ValuePartLabel { get; }

    /// <summary>The name part's button: 名稱.</summary>
    public string NamePartLabel => Strings.TextLook_PartName;

    /// <summary>The part the window shows and edits now; it opens on the value.</summary>
    public TextIconViewModel Current
    {
        get => _current;
        private set
        {
            if (SetProperty(ref _current, value))
            {
                OnPropertyChanged(nameof(IsValuePart));
                OnPropertyChanged(nameof(IsNamePart));
            }
        }
    }

    public bool IsValuePart
    {
        get => ReferenceEquals(_current, Value);
        set
        {
            if (value)
            {
                Current = Value;
            }
        }
    }

    public bool IsNamePart
    {
        get => ReferenceEquals(_current, Name);
        set
        {
            if (value)
            {
                Current = Name;
            }
        }
    }

    /// <summary>OK saves both parts: neither may hold a value that cannot be saved.</summary>
    public bool CanSave => Value.CanSave && Name.CanSave;

    /// <summary>Both looks as OK saves them.</summary>
    public LookAnswer ToAnswer() => new(Value.ToLook(), Name.ToLook());

    private void OnPartChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or "" or nameof(TextIconViewModel.CanSave))
        {
            OnPropertyChanged(nameof(CanSave));
        }
    }
}
