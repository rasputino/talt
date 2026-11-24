using System.Globalization;
using System.Text.Json;

namespace Talt.Core.Towns;

/// <summary>
/// Strict reading helpers for the town data files (constitution VII): unknown keys, missing values and
/// out-of-range numbers fail with an <see cref="InvalidDataException"/> naming the file and the field.
/// </summary>
internal readonly struct TownJson(string fileName)
{
    public string FileName { get; } = fileName;

    public static string ReadResource(string fileName)
    {
        using var stream = typeof(TownJson).Assembly.GetManifestResourceStream("Talt.Core.Towns.Data." + fileName)
            ?? throw new InvalidDataException($"Missing embedded data file '{fileName}'.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public JsonDocument Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                document.Dispose();
                throw Invalid("the file must contain one object");
            }

            return document;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"{FileName}: not valid JSON: {exception.Message}", exception);
        }
    }

    public void Known(JsonElement element, string prefix, params string[] fields)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Invalid($"'{prefix.TrimEnd('.')}' must be an object");
        }

        foreach (var property in element.EnumerateObject())
        {
            if (System.Array.IndexOf(fields, property.Name) < 0)
            {
                throw Invalid($"unknown field '{prefix}{property.Name}'");
            }
        }
    }

    public JsonElement Object(JsonElement parent, string name, string prefix)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
        {
            throw Invalid($"'{prefix}{name}' must be an object");
        }

        return value;
    }

    public JsonElement ArrayOf(JsonElement parent, string name, string prefix, bool allowEmpty = false)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array || (!allowEmpty && value.GetArrayLength() == 0))
        {
            throw Invalid(allowEmpty ? $"'{prefix}{name}' must be an array" : $"'{prefix}{name}' must be a non-empty array");
        }

        return value;
    }

    public string String(JsonElement parent, string name, string prefix)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw Invalid($"'{prefix}{name}' must be a non-empty text");
        }

        return value.GetString()!;
    }

    public int Int(JsonElement parent, string name, string prefix, int min, int max)
    {
        if (!parent.TryGetProperty(name, out var value))
        {
            throw Invalid($"'{prefix}{name}' is missing");
        }

        return IntValue(value, prefix + name, min, max);
    }

    public int IntValue(JsonElement value, string path, int min, int max)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number))
        {
            throw Invalid($"'{path}' must be a whole number");
        }

        if (number < min || number > max)
        {
            throw Invalid($"'{path}' must be between {min} and {max} (was {number})");
        }

        return number;
    }

    /// <summary>A colour written "#RRGGBB".</summary>
    public int Color(JsonElement parent, string name, string prefix)
    {
        var text = String(parent, name, prefix);
        if (text.Length != 7 || text[0] != '#'
            || !int.TryParse(text.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var color))
        {
            throw Invalid($"'{prefix}{name}' must be a colour #RRGGBB (was '{text}')");
        }

        return color;
    }

    public InvalidDataException Invalid(string problem) => new($"{FileName}: {problem}.");
}
