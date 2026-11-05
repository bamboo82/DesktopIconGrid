using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Core.Storage;

/// <summary>
/// Reads and writes <see cref="LauncherItem"/> with a <c>$type</c> discriminator. Known kinds map to
/// their classes; anything else becomes an <see cref="UnknownItem"/> that round-trips verbatim, so a
/// layout written by a newer version is never thrown away.
/// </summary>
public sealed class LauncherItemConverter : JsonConverter<LauncherItem>
{
    public const string Discriminator = "$type";

    private static readonly Dictionary<string, Type> KnownKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["shortcut"] = typeof(ShortcutItem),
        ["group"] = typeof(GroupItem),
        ["special"] = typeof(SpecialIconItem),
    };

    private static readonly Dictionary<Type, string> KindNames = KnownKinds.ToDictionary(p => p.Value, p => p.Key);

    public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(LauncherItem);

    public override LauncherItem? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("A launcher item must be a JSON object.");
        }

        var discriminator = root.TryGetProperty(Discriminator, out var kind) && kind.ValueKind == JsonValueKind.String
            ? kind.GetString()
            : null;

        if (discriminator is not null && KnownKinds.TryGetValue(discriminator, out var type))
        {
            // Properties of the kind's class are read normally; everything else lands in ExtensionData and is written
            // back by Write. The discriminator itself is written by Write, so it is not kept as an unknown property.
            var item = (LauncherItem?)JsonSerializer.Deserialize(root.GetRawText(), type, options);
            if (item?.ExtensionData is { } extra && extra.Remove(Discriminator) && extra.Count == 0)
            {
                item.ExtensionData = null;
            }

            return item;
        }

        var unknown = new UnknownItem { TypeDiscriminator = discriminator ?? string.Empty, Raw = root.Clone() };
        if (root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String && Guid.TryParse(id.GetString(), out var parsedId))
        {
            unknown.Id = parsedId;
        }

        if (root.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
        {
            unknown.Name = name.GetString() ?? string.Empty;
        }

        if (root.TryGetProperty("iconPath", out var iconPath) && iconPath.ValueKind == JsonValueKind.String)
        {
            unknown.IconPath = iconPath.GetString();
        }

        return unknown;
    }

    public override void Write(Utf8JsonWriter writer, LauncherItem value, JsonSerializerOptions options)
    {
        if (value is UnknownItem unknown)
        {
            var original = unknown.Raw.ValueKind == JsonValueKind.Object
                ? JsonNode.Parse(unknown.Raw.GetRawText()) as JsonObject ?? new JsonObject()
                : new JsonObject();
            original["id"] = unknown.Id.ToString();
            original["name"] = unknown.Name;
            if (unknown.TypeDiscriminator.Length > 0)
            {
                original[Discriminator] = unknown.TypeDiscriminator;
            }

            original.WriteTo(writer, options);
            return;
        }

        if (!KindNames.TryGetValue(value.GetType(), out var kindName))
        {
            throw new JsonException($"Cannot serialize {value.GetType().Name}.");
        }

        var properties = JsonSerializer.SerializeToNode(value, value.GetType(), options) as JsonObject ?? new JsonObject();
        var ordered = new JsonObject { [Discriminator] = kindName };
        foreach (var (key, node) in properties.ToList())
        {
            if (key == Discriminator)
            {
                continue;
            }

            ordered[key] = node?.DeepClone();
        }

        ordered.WriteTo(writer, options);
    }
}
