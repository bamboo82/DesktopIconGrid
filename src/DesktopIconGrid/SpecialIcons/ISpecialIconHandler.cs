using System.Windows.Media;
using DesktopIconGrid.Core.Launching;
using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.SpecialIcons;

[Flags]
public enum ActivationModifiers
{
    None = 0,
    Control = 1,
    Shift = 2,
    Alt = 4,
}

/// <summary>An extra context-menu entry a special icon offers.</summary>
public sealed record SpecialIconAction(string Header, Action Execute, bool IsEnabled = true);

/// <summary>Behaviour of one special icon kind. Handlers are registered in <see cref="SpecialIconRegistry"/>.</summary>
public interface ISpecialIconHandler
{
    string Kind { get; }

    /// <summary>Menu caption, e.g. "CPU usage".</summary>
    string DisplayName { get; }

    string Description { get; }

    /// <summary>A new item with sensible defaults (shown in the properties dialog before it is added).</summary>
    SpecialIconItem CreateItem();

    /// <summary>Live state the icon template binds to, or <see langword="null"/> when the kind has none.</summary>
    GaugeState? CreateState(SpecialIconItem item);

    /// <summary>The default icon image; <see langword="null"/> shows the placeholder (or the gauge when one is visible).</summary>
    ImageSource? GetIcon(SpecialIconItem item) => null;

    /// <summary>Extra tooltip line shown when no gauge detail is visible.</summary>
    string? GetToolTip(SpecialIconItem item) => null;

    /// <summary>Single click / Enter, with the modifier keys held at the time.</summary>
    LaunchResult Activate(SpecialIconItem item, ActivationModifiers modifiers);

    /// <summary>Shows the kind-specific properties dialog, editing <paramref name="item"/> in place. True when saved.</summary>
    bool ShowProperties(SpecialIconItem item, bool isNew);

    bool AcceptsFileDrops(SpecialIconItem item) => false;

    Task<LaunchResult> DropFilesAsync(SpecialIconItem item, IReadOnlyList<string> paths) =>
        Task.FromResult(LaunchResult.Failure("This icon does not accept files."));

    IReadOnlyList<SpecialIconAction> GetActions(SpecialIconItem item) => [];
}

/// <summary>Looks up handlers by kind; unknown kinds get a placeholder handler.</summary>
public sealed class SpecialIconRegistry
{
    private readonly Dictionary<string, ISpecialIconHandler> _handlers;

    public SpecialIconRegistry(IEnumerable<ISpecialIconHandler> handlers)
    {
        Handlers = handlers.ToList();
        _handlers = Handlers.ToDictionary(h => h.Kind, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Handlers in menu order.</summary>
    public IReadOnlyList<ISpecialIconHandler> Handlers { get; }

    public ISpecialIconHandler Resolve(string? kind) =>
        kind is not null && _handlers.TryGetValue(kind.Trim(), out var handler) ? handler : UnknownSpecialIconHandler.Instance;

    public bool IsKnown(string? kind) => kind is not null && _handlers.ContainsKey(kind.Trim());
}

/// <summary>Stands in for kinds this version does not know (for example a layout written by a newer version).</summary>
public sealed class UnknownSpecialIconHandler : ISpecialIconHandler
{
    public static readonly UnknownSpecialIconHandler Instance = new();

    public string Kind => string.Empty;

    public string DisplayName => "Unknown special icon";

    public string Description => "This icon needs a newer version of Desktop Icon Grid.";

    public SpecialIconItem CreateItem() => new() { Name = DisplayName };

    public GaugeState? CreateState(SpecialIconItem item) => null;

    public LaunchResult Activate(SpecialIconItem item, ActivationModifiers modifiers) => LaunchResult.Failure(Description);

    public bool ShowProperties(SpecialIconItem item, bool isNew) => false;
}
