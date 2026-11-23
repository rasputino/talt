using System.Reflection;
using System.Text.Json;

namespace Talt.Client;

/// <summary>
/// Loads the client's embedded data files (<c>**/Data/*.json</c>, constitution VII). A missing or
/// invalid file stops the game with a message naming the file and the problem.
/// </summary>
internal static class DataFiles
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Opens an embedded resource by its logical name (for example <c>data/theme.json</c>).</summary>
    public static Stream Open(string logicalName) =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream(logicalName)
        ?? throw new InvalidDataException($"Missing embedded data file '{logicalName}'.");

    /// <summary>Deserializes <c>data/<paramref name="fileName"/></c>.</summary>
    public static T Load<T>(string fileName)
    {
        var name = "data/" + fileName;
        try
        {
            using var stream = Open(name);
            return JsonSerializer.Deserialize<T>(stream, Options)
                ?? throw new InvalidDataException($"Data file '{fileName}' is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Data file '{fileName}' is not valid: {exception.Message}", exception);
        }
    }

    /// <summary>Throws a validation error for a data file.</summary>
    public static InvalidDataException Invalid(string fileName, string problem) =>
        new($"Data file '{fileName}': {problem}");
}
