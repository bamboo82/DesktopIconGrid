using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopIconGrid.Core.Models;

/// <summary>
/// Where the window at the pointer sits relative to the pointer (0.16.0 WP2, the owner's request): the point of the
/// window that is put under the pointer, in whole percent of its width (<see cref="X"/>: 0 the left edge, 100 the right)
/// and of its height (<see cref="Y"/>: 0 the top edge, 100 the bottom); 50/50 puts the window's centre there. It applies
/// to both forms of the window, the tap and the hold. Stored in the settings as <c>pointerAnchor</c>
/// <c>{ "x": …, "y": … }</c> beside <c>showAtPointerHotkey</c>, and synced with them, only when set: absent, the window is
/// placed as before 0.16.0, centred across with its top edge a little above the pointer
/// (<see cref="PopupPlacement.AtPointer"/>). What is not two whole numbers from 0 to 100 at load is absent
/// (<see cref="PointerAnchorConverter"/>), and the rest of the settings is read as ever.
/// </summary>
[JsonConverter(typeof(PointerAnchorConverter))]
public sealed record PointerAnchor(int X, int Y)
{
    public const int Min = 0;

    public const int Max = 100;

    /// <summary>The middle of the window, which Home goes to in Settings.</summary>
    public static PointerAnchor Centre { get; } = new(50, 50);

    /// <summary>Both values are whole percents from <see cref="Min"/> to <see cref="Max"/>.</summary>
    public bool IsValid => X is >= Min and <= Max && Y is >= Min and <= Max;

    /// <summary>This anchor while it is valid, else none (the placement before 0.16.0).</summary>
    public PointerAnchor? Normalized() => IsValid ? this : null;
}

/// <summary>
/// Reads <c>pointerAnchor</c> leniently (0.16.0 WP2): an object with <c>x</c> and <c>y</c>, each a whole number from 0
/// to 100, is an anchor; anything else (a value out of range or with a fraction, a missing one, text, an array, a number)
/// is passed over whole and read as none, so a damaged value never stops the layout from loading.
/// </summary>
public sealed class PointerAnchorConverter : JsonConverter<PointerAnchor?>
{
    public override PointerAnchor? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip(); // an array is passed over whole; a single value is already read
            return null;
        }

        int? x = null;
        int? y = null;
        var damaged = false;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var name = reader.GetString();
            reader.Read();
            int? value = reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number) ? number : null;
            reader.Skip(); // an object or an array is passed over whole

            if (string.Equals(name, "x", StringComparison.OrdinalIgnoreCase))
            {
                x = value;
                damaged |= value is null;
            }
            else if (string.Equals(name, "y", StringComparison.OrdinalIgnoreCase))
            {
                y = value;
                damaged |= value is null;
            }
        }

        return !damaged && x is { } across && y is { } down ? new PointerAnchor(across, down).Normalized() : null;
    }

    public override void Write(Utf8JsonWriter writer, PointerAnchor? value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        writer.WriteNumber("x", value.X);
        writer.WriteNumber("y", value.Y);
        writer.WriteEndObject();
    }
}
