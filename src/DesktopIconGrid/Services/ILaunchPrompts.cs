namespace DesktopIconGrid.Services;

/// <summary>What a launch may need from the user: the clipboard text for <c>{clipboard}</c>, an answer for <c>{ask:…}</c>.</summary>
public interface ILaunchPrompts
{
    /// <summary>The clipboard's text, or <see langword="null"/> when it holds none (or another program has it open).</summary>
    string? ClipboardText();

    /// <summary>Asks for a value; <see langword="null"/> when the user cancels.</summary>
    string? Ask(string title, string prompt);
}
