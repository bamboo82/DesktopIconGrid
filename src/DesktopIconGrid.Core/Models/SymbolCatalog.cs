namespace DesktopIconGrid.Core.Models;

/// <summary>The groups of the symbol picker, in the order it lists them.</summary>
public enum SymbolGroup
{
    Common,
    FilesAndFolders,
    Communication,
    Media,
    Devices,
    Network,
    ToolsAndSettings,
    PeopleAndPlaces,
    ArrowsAndMarks,
}

/// <summary>One symbol of the picker.</summary>
/// <param name="CodePoint">Its code point in Segoe MDL2 Assets, which Segoe Fluent Icons shares.</param>
/// <param name="Name">Its English name: the cell's tooltip and what a screen reader says.</param>
/// <param name="Keywords">More English words a search finds it by, separated by spaces; may be empty.</param>
/// <param name="Group">The heading it is listed under.</param>
public sealed record SymbolEntry(int CodePoint, string Name, string Keywords, SymbolGroup Group)
{
    /// <summary>The character stored as the text icon's <see cref="TextIcon.Text"/>.</summary>
    public string Text => char.ConvertFromUtf32(CodePoint);
}

/// <summary>
/// The symbols a text icon can be (0.12.0): a symbol is a text icon of one character in <see cref="FontFamily"/> that
/// fits its tile. The table is the review session's design (0.12.0 plan, addendum A1): 197 symbols, each picked from
/// a rendering of both fonts on Windows 11 and checked to exist in Segoe Fluent Icons and in Segoe MDL2 Assets, so
/// Windows 10 has them too. Keep it as it is: its order is the picker's. The comment after each entry is the glyph's
/// name in Microsoft's list of Segoe MDL2 Assets. Names and keywords are English only; the group headings are translated.
/// </summary>
public static class SymbolCatalog
{
    /// <summary>The font of a symbol: Fluent Icons on Windows 11, else MDL2 Assets.</summary>
    public const string FontFamily = "Segoe Fluent Icons, Segoe MDL2 Assets";

    /// <summary>The size an older version, which does not fit text to the tile, draws a symbol at.</summary>
    public const double FontSize = 26;

    /// <summary>Every symbol, grouped and in the order the picker lists them.</summary>
    public static IReadOnlyList<SymbolEntry> All { get; } =
    [
        new(0xE80F, "Home", "house start", SymbolGroup.Common), // Home
        new(0xE721, "Search", "find magnifier", SymbolGroup.Common), // Search
        new(0xE713, "Settings", "gear options preferences", SymbolGroup.Common), // Setting
        new(0xE734, "Star", "favorite favourite", SymbolGroup.Common), // FavoriteStar
        new(0xE735, "Star filled", "favorite favourite", SymbolGroup.Common), // FavoriteStarFill
        new(0xEB51, "Heart", "like love", SymbolGroup.Common), // Heart
        new(0xEB52, "Heart filled", "like love", SymbolGroup.Common), // HeartFill
        new(0xE7C1, "Flag", "mark", SymbolGroup.Common), // Flag
        new(0xE718, "Pin", "pinned", SymbolGroup.Common), // Pin
        new(0xE8EC, "Tag", "label", SymbolGroup.Common), // Tag
        new(0xE82F, "Light bulb", "idea tip", SymbolGroup.Common), // ToolTip
        new(0xE7E8, "Power", "shutdown off on", SymbolGroup.Common), // PowerButton
        new(0xE72E, "Lock", "secure locked", SymbolGroup.Common), // Lock
        new(0xE785, "Unlock", "open unlocked", SymbolGroup.Common), // Unlock
        new(0xE8D7, "Key", "password permission", SymbolGroup.Common), // Permissions
        new(0xE946, "Info", "information about", SymbolGroup.Common), // Info
        new(0xE7BA, "Warning", "alert caution", SymbolGroup.Common), // Warning
        new(0xE897, "Help", "question", SymbolGroup.Common), // Help
        new(0xE73E, "Check mark", "ok done yes tick", SymbolGroup.Common), // CheckMark
        new(0xE711, "Close", "cancel x no", SymbolGroup.Common), // Cancel
        new(0xE710, "Add", "plus new", SymbolGroup.Common), // Add
        new(0xE74D, "Delete", "trash bin remove", SymbolGroup.Common), // Delete
        new(0xE70F, "Edit", "pencil write", SymbolGroup.Common), // Edit
        new(0xE72C, "Refresh", "reload", SymbolGroup.Common), // Refresh
        new(0xE712, "More", "dots ellipsis", SymbolGroup.Common), // More
        new(0xE700, "Menu", "hamburger lines", SymbolGroup.Common), // GlobalNavigationButton
        new(0xE8B7, "Folder", "directory", SymbolGroup.FilesAndFolders), // Folder
        new(0xE838, "Folder open", "directory", SymbolGroup.FilesAndFolders), // FolderOpen
        new(0xE8D5, "Folder filled", "directory", SymbolGroup.FilesAndFolders), // FolderFill
        new(0xE8F4, "New folder", "add directory", SymbolGroup.FilesAndFolders), // NewFolder
        new(0xEC50, "File explorer", "files browse", SymbolGroup.FilesAndFolders), // FileExplorer
        new(0xE7C3, "Page", "file blank document", SymbolGroup.FilesAndFolders), // Page
        new(0xE8A5, "Document", "file text", SymbolGroup.FilesAndFolders), // Document
        new(0xF000, "Text document", "file article notes", SymbolGroup.FilesAndFolders), // KnowledgeArticle
        new(0xE9F9, "Report", "document chart", SymbolGroup.FilesAndFolders), // ReportDocument
        new(0xE8E5, "Open file", "document arrow", SymbolGroup.FilesAndFolders), // OpenFile
        new(0xE74E, "Save", "disk floppy", SymbolGroup.FilesAndFolders), // Save
        new(0xE8C8, "Copy", "duplicate", SymbolGroup.FilesAndFolders), // Copy
        new(0xE77F, "Paste", "clipboard", SymbolGroup.FilesAndFolders), // Paste
        new(0xE8C6, "Cut", "scissors", SymbolGroup.FilesAndFolders), // Cut
        new(0xE723, "Attach", "paper clip attachment", SymbolGroup.FilesAndFolders), // Attach
        new(0xE71B, "Link", "chain url", SymbolGroup.FilesAndFolders), // Link
        new(0xE7B8, "Package", "box archive", SymbolGroup.FilesAndFolders), // Package
        new(0xE8F1, "Library", "books shelf", SymbolGroup.FilesAndFolders), // Library
        new(0xE82D, "Book", "dictionary read", SymbolGroup.FilesAndFolders), // Dictionary
        new(0xE896, "Download", "arrow down save", SymbolGroup.FilesAndFolders), // Download
        new(0xE898, "Upload", "arrow up send", SymbolGroup.FilesAndFolders), // Upload
        new(0xE715, "Mail", "email envelope", SymbolGroup.Communication), // Mail
        new(0xE8C3, "Mail open", "email read envelope", SymbolGroup.Communication), // Read
        new(0xE724, "Send", "paper plane", SymbolGroup.Communication), // Send
        new(0xE8BD, "Message", "chat bubble sms", SymbolGroup.Communication), // Message
        new(0xE8F2, "Chat", "conversation bubbles talk", SymbolGroup.Communication), // ChatBubbles
        new(0xE717, "Phone", "call telephone", SymbolGroup.Communication), // Phone
        new(0xE910, "At sign", "email address account", SymbolGroup.Communication), // Accounts
        new(0xE72D, "Share", "forward", SymbolGroup.Communication), // Share
        new(0xE789, "Megaphone", "announce news", SymbolGroup.Communication), // Megaphone
        new(0xEA8F, "Bell", "notification alarm ring", SymbolGroup.Communication), // Ringer
        new(0xE787, "Calendar", "date schedule", SymbolGroup.Communication), // Calendar
        new(0xE823, "Clock", "time", SymbolGroup.Communication), // Recent
        new(0xE916, "Stopwatch", "timer", SymbolGroup.Communication), // Stopwatch
        new(0xE81C, "History", "recent time back", SymbolGroup.Communication), // History
        new(0xE768, "Play", "start", SymbolGroup.Media), // Play
        new(0xE769, "Pause", "hold", SymbolGroup.Media), // Pause
        new(0xE71A, "Stop", "square", SymbolGroup.Media), // Stop
        new(0xE7C8, "Record", "rec", SymbolGroup.Media), // Record
        new(0xE892, "Previous", "skip back", SymbolGroup.Media), // Previous
        new(0xE893, "Next", "skip forward", SymbolGroup.Media), // Next
        new(0xEB9E, "Rewind", "back fast", SymbolGroup.Media), // Rewind
        new(0xEB9D, "Fast forward", "fast", SymbolGroup.Media), // FastForward
        new(0xE767, "Volume", "sound speaker audio", SymbolGroup.Media), // Volume
        new(0xE74F, "Mute", "silent sound off", SymbolGroup.Media), // Mute
        new(0xE720, "Microphone", "mic voice record", SymbolGroup.Media), // Microphone
        new(0xE7F5, "Speakers", "sound audio", SymbolGroup.Media), // Speakers
        new(0xE7F6, "Headphones", "audio headset", SymbolGroup.Media), // Headphone
        new(0xEC4F, "Music note", "song audio", SymbolGroup.Media), // MusicNote
        new(0xE8D6, "Music", "notes songs audio", SymbolGroup.Media), // Audio
        new(0xE714, "Video", "camera film movie", SymbolGroup.Media), // Video
        new(0xE8B2, "Film", "movie video clip", SymbolGroup.Media), // Movies
        new(0xE722, "Camera", "photo picture", SymbolGroup.Media), // Camera
        new(0xE8B9, "Picture", "image photo", SymbolGroup.Media), // Picture
        new(0xEB9F, "Photo", "image picture", SymbolGroup.Media), // Photo2
        new(0xE790, "Palette", "colour color paint art", SymbolGroup.Media), // Color
        new(0xE771, "Brush", "paint draw art personalize", SymbolGroup.Media), // Personalize
        new(0xE7FC, "Game", "controller gamepad play", SymbolGroup.Media), // Game
        new(0xE958, "Disc", "cd dvd music", SymbolGroup.Media), // StorageOptical
        new(0xE977, "PC", "desktop computer", SymbolGroup.Devices), // PC1
        new(0xE7F8, "Laptop", "notebook computer", SymbolGroup.Devices), // DeviceLaptopNoPic
        new(0xE7F4, "Monitor", "screen display tv", SymbolGroup.Devices), // TVMonitor
        new(0xE8EA, "Mobile phone", "smartphone cell", SymbolGroup.Devices), // CellPhone
        new(0xE70A, "Tablet", "pad", SymbolGroup.Devices), // Tablet
        new(0xE765, "Keyboard", "type keys", SymbolGroup.Devices), // KeyboardClassic
        new(0xE962, "Mouse", "pointer click", SymbolGroup.Devices), // Mouse
        new(0xE749, "Printer", "print", SymbolGroup.Devices), // Print
        new(0xE8FE, "Scanner", "scan", SymbolGroup.Devices), // Scan
        new(0xE950, "Chip", "cpu processor hardware", SymbolGroup.Devices), // Component
        new(0xEDA2, "Hard drive", "disk storage", SymbolGroup.Devices), // HardDrive
        new(0xE88E, "USB drive", "stick storage", SymbolGroup.Devices), // USB
        new(0xE7F1, "SD card", "memory storage", SymbolGroup.Devices), // SDCard
        new(0xE83F, "Battery", "power charge", SymbolGroup.Devices), // Battery10
        new(0xE701, "Wi-Fi", "wireless wifi", SymbolGroup.Network), // Wifi
        new(0xE702, "Bluetooth", "wireless", SymbolGroup.Network), // Bluetooth
        new(0xE839, "Ethernet", "wired lan cable", SymbolGroup.Network), // Ethernet
        new(0xE968, "Network", "lan computers", SymbolGroup.Network), // Network
        new(0xE704, "Hotspot", "tower signal antenna sharing", SymbolGroup.Network), // InternetSharing
        new(0xE705, "VPN", "shield lock secure tunnel", SymbolGroup.Network), // VPN
        new(0xE703, "Connect", "devices cast pair", SymbolGroup.Network), // Connect
        new(0xE753, "Cloud", "online storage", SymbolGroup.Network), // Cloud
        new(0xE774, "Globe", "web internet browser", SymbolGroup.Network), // Globe
        new(0xE909, "World", "earth web internet", SymbolGroup.Network), // World
        new(0xE895, "Sync", "synchronize arrows", SymbolGroup.Network), // Sync
        new(0xE8AB, "Switch", "swap transfer arrows", SymbolGroup.Network), // Switch
        new(0xEA18, "Shield", "security protect", SymbolGroup.Network), // Shield
        new(0xE928, "Fingerprint", "security identity", SymbolGroup.Network), // Fingerprint
        new(0xE90F, "Wrench", "repair fix tool", SymbolGroup.ToolsAndSettings), // Repair
        new(0xEC7A, "Tools", "developer wrench screwdriver", SymbolGroup.ToolsAndSettings), // DeveloperTools
        new(0xE756, "Command prompt", "terminal console cmd shell", SymbolGroup.ToolsAndSettings), // CommandPrompt
        new(0xE943, "Code", "braces program script json", SymbolGroup.ToolsAndSettings), // Code
        new(0xEBE8, "Bug", "debug insect", SymbolGroup.ToolsAndSettings), // Bug
        new(0xE99A, "Robot", "bot automation ai", SymbolGroup.ToolsAndSettings), // Robot
        new(0xE8EF, "Calculator", "math numbers", SymbolGroup.ToolsAndSettings), // Calculator
        new(0xE9D9, "Diagnostic", "pulse monitor health", SymbolGroup.ToolsAndSettings), // Diagnostic
        new(0xE9D2, "Chart", "graph statistics", SymbolGroup.ToolsAndSettings), // AreaChart
        new(0xE9E9, "Sliders", "equalizer adjust mixer", SymbolGroup.ToolsAndSettings), // Equalizer
        new(0xE71C, "Filter", "funnel", SymbolGroup.ToolsAndSettings), // Filter
        new(0xE8CB, "Sort", "order arrows", SymbolGroup.ToolsAndSettings), // Sort
        new(0xE9D5, "Checklist", "tasks todo", SymbolGroup.ToolsAndSettings), // CheckList
        new(0xE8FD, "List", "bullets", SymbolGroup.ToolsAndSettings), // BulletedList
        new(0xE80A, "Grid", "table cells", SymbolGroup.ToolsAndSettings), // TiltDown
        new(0xECA5, "Tiles", "apps squares", SymbolGroup.ToolsAndSettings), // Tiles
        new(0xEA86, "Puzzle", "extension plugin add-in", SymbolGroup.ToolsAndSettings), // Puzzle
        new(0xE8A7, "Open in new window", "external launch", SymbolGroup.ToolsAndSettings), // OpenInNewWindow
        new(0xE890, "Eye", "view show watch", SymbolGroup.ToolsAndSettings), // View
        new(0xE7E6, "Highlighter", "marker pen", SymbolGroup.ToolsAndSettings), // Highlight
        new(0xE75C, "Eraser", "rubber clear", SymbolGroup.ToolsAndSettings), // EraseTool
        new(0xEF3C, "Eyedropper", "colour color picker", SymbolGroup.ToolsAndSettings), // Eyedropper
        new(0xE81E, "Layers", "stack map", SymbolGroup.ToolsAndSettings), // MapLayers
        new(0xF158, "Cube", "3d box model", SymbolGroup.ToolsAndSettings), // DialShape3
        new(0xE945, "Lightning", "bolt fast quick power", SymbolGroup.ToolsAndSettings), // LightningBolt
        new(0xECAD, "Fire", "flame hot burn", SymbolGroup.ToolsAndSettings), // Calories
        new(0xEB42, "Drop", "water liquid", SymbolGroup.ToolsAndSettings), // Drop
        new(0xE9CA, "Thermometer", "temperature", SymbolGroup.ToolsAndSettings), // Frigid
        new(0xE706, "Sun", "brightness light day", SymbolGroup.ToolsAndSettings), // Brightness
        new(0xE708, "Moon", "night dark quiet", SymbolGroup.ToolsAndSettings), // QuietHours
        new(0xE77B, "Person", "user contact account", SymbolGroup.PeopleAndPlaces), // Contact
        new(0xEA8C, "Person filled", "user contact account", SymbolGroup.PeopleAndPlaces), // ContactSolid
        new(0xE716, "People", "users contacts", SymbolGroup.PeopleAndPlaces), // People
        new(0xE902, "Group", "team people users", SymbolGroup.PeopleAndPlaces), // Group
        new(0xE779, "Contact card", "person details id", SymbolGroup.PeopleAndPlaces), // ContactInfo
        new(0xE7BE, "Education", "school graduation cap learn", SymbolGroup.PeopleAndPlaces), // Education
        new(0xE821, "Briefcase", "work job business", SymbolGroup.PeopleAndPlaces), // Work
        new(0xE825, "Bank", "money finance building", SymbolGroup.PeopleAndPlaces), // Bank
        new(0xE731, "Building", "office company", SymbolGroup.PeopleAndPlaces), // EMI
        new(0xE7BF, "Shopping cart", "buy store", SymbolGroup.PeopleAndPlaces), // ShoppingCart
        new(0xE719, "Shopping bag", "shop store buy", SymbolGroup.PeopleAndPlaces), // Shop
        new(0xE8C7, "Payment card", "credit money pay", SymbolGroup.PeopleAndPlaces), // PaymentCard
        new(0xE707, "Map pin", "place location", SymbolGroup.PeopleAndPlaces), // MapPin
        new(0xE81D, "Location", "gps target position", SymbolGroup.PeopleAndPlaces), // Location
        new(0xE804, "Car", "drive vehicle", SymbolGroup.PeopleAndPlaces), // Car
        new(0xE806, "Bus", "transit vehicle", SymbolGroup.PeopleAndPlaces), // Bus
        new(0xE7C0, "Train", "rail transit", SymbolGroup.PeopleAndPlaces), // Train
        new(0xE709, "Airplane", "flight travel plane", SymbolGroup.PeopleAndPlaces), // Airplane
        new(0xE7E3, "Ferry", "boat ship", SymbolGroup.PeopleAndPlaces), // Ferry
        new(0xE805, "Walk", "pedestrian person", SymbolGroup.PeopleAndPlaces), // Walk
        new(0xEC32, "Cup", "coffee tea drink break", SymbolGroup.PeopleAndPlaces), // Cafe
        new(0xED56, "Pizza", "food eat lunch", SymbolGroup.PeopleAndPlaces), // EmojiTabFoodPlants
        new(0xE95E, "Health", "heart pulse medical", SymbolGroup.PeopleAndPlaces), // Health
        new(0xE776, "Accessibility", "ease of access person", SymbolGroup.PeopleAndPlaces), // EaseOfAccess
        new(0xE76E, "Smile", "emoji face happy", SymbolGroup.PeopleAndPlaces), // Emoji2
        new(0xE74A, "Up", "arrow", SymbolGroup.ArrowsAndMarks), // Up
        new(0xE74B, "Down", "arrow", SymbolGroup.ArrowsAndMarks), // Down
        new(0xE72B, "Back", "arrow left", SymbolGroup.ArrowsAndMarks), // Back
        new(0xE72A, "Forward", "arrow right", SymbolGroup.ArrowsAndMarks), // Forward
        new(0xE70E, "Chevron up", "arrow", SymbolGroup.ArrowsAndMarks), // ChevronUp
        new(0xE70D, "Chevron down", "arrow", SymbolGroup.ArrowsAndMarks), // ChevronDown
        new(0xE76B, "Chevron left", "arrow", SymbolGroup.ArrowsAndMarks), // ChevronLeft
        new(0xE76C, "Chevron right", "arrow", SymbolGroup.ArrowsAndMarks), // ChevronRight
        new(0xE7A7, "Undo", "back revert", SymbolGroup.ArrowsAndMarks), // Undo
        new(0xE7A6, "Redo", "again", SymbolGroup.ArrowsAndMarks), // Redo
        new(0xE7AD, "Rotate", "turn", SymbolGroup.ArrowsAndMarks), // Rotate
        new(0xE8EE, "Repeat", "loop", SymbolGroup.ArrowsAndMarks), // RepeatAll
        new(0xE8B1, "Shuffle", "random mix", SymbolGroup.ArrowsAndMarks), // Shuffle
        new(0xE740, "Full screen", "expand maximize", SymbolGroup.ArrowsAndMarks), // FullScreen
        new(0xE73F, "Back to window", "shrink restore", SymbolGroup.ArrowsAndMarks), // BackToWindow
        new(0xE8B0, "Pointer", "cursor mouse arrow", SymbolGroup.ArrowsAndMarks), // Click
        new(0xE738, "Minus", "remove line dash", SymbolGroup.ArrowsAndMarks), // Remove
        new(0xE94C, "Percent", "percentage", SymbolGroup.ArrowsAndMarks), // CalculatorPercentage
        new(0xE783, "Error", "exclamation circle alert", SymbolGroup.ArrowsAndMarks), // Error
        new(0xEA39, "Blocked", "circle x error stop", SymbolGroup.ArrowsAndMarks), // ErrorBadge
        new(0xE930, "Completed", "circle check done", SymbolGroup.ArrowsAndMarks), // Completed
        new(0xE9CE, "Unknown", "circle question help", SymbolGroup.ArrowsAndMarks), // Unknown
        new(0xE8C9, "Important", "exclamation", SymbolGroup.ArrowsAndMarks), // Important
        new(0xE739, "Square", "checkbox empty box", SymbolGroup.ArrowsAndMarks), // Checkbox
        new(0xE73A, "Checkbox", "checked tick box", SymbolGroup.ArrowsAndMarks), // CheckboxComposite
        new(0xEA3A, "Circle", "ring empty", SymbolGroup.ArrowsAndMarks), // CircleRing
        new(0xEA3B, "Circle filled", "dot", SymbolGroup.ArrowsAndMarks), // CircleFill
        new(0xE8E1, "Like", "thumb up good", SymbolGroup.ArrowsAndMarks), // Like
        new(0xE8E0, "Dislike", "thumb down bad", SymbolGroup.ArrowsAndMarks), // Dislike
    ];

    private static readonly Dictionary<int, SymbolEntry> ByCodePoint = All.ToDictionary(symbol => symbol.CodePoint);

    /// <summary>The symbol <paramref name="text"/> is, or <see langword="null"/> when it is no symbol of the catalogue.</summary>
    public static SymbolEntry? Find(string? text) =>
        text is { Length: 1 } && ByCodePoint.TryGetValue(text[0], out var symbol) ? symbol : null;

    /// <summary>Whether <paramref name="icon"/> is a symbol: it fits the tile and is drawn in the symbol font.</summary>
    public static bool IsSymbol(TextIcon? icon) =>
        icon is { Fit: true } && string.Equals(icon.FontFamily, FontFamily, StringComparison.OrdinalIgnoreCase);

    /// <summary>The text icon of <paramref name="symbol"/>, with a text and a tile colour (or none).</summary>
    public static TextIcon Icon(SymbolEntry symbol, string? color = null, string? background = null) =>
        new(symbol.Text, FontFamily, FontSize, Bold: false, color, Fit: true, background);

    /// <summary>
    /// Whether <paramref name="symbol"/> is found by <paramref name="query"/>: every word of the query begins a word of
    /// the name or the keywords, ignoring case. An empty query finds every symbol.
    /// </summary>
    public static bool Matches(SymbolEntry symbol, string? query)
    {
        var wanted = Words(query);
        if (wanted.Length == 0)
        {
            return true;
        }

        var words = Words(symbol.Name + " " + symbol.Keywords);
        return wanted.All(part => words.Any(word => word.StartsWith(part, StringComparison.OrdinalIgnoreCase)));
    }

    private static string[] Words(string? text) =>
        (text ?? string.Empty).Split([' ', '\t', '-', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
