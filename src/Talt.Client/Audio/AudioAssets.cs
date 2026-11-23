namespace Talt.Client.Audio;

/// <summary>Resolves audio asset paths beneath the game's <c>audio/</c> folder.</summary>
internal static class AudioAssets
{
    /// <summary>The folder copied next to the executable by the client project.</summary>
    public static string DefaultRoot => Path.Combine(AppContext.BaseDirectory, "audio");

    /// <summary>Converts a data-file path (forward slashes) to an absolute native path.</summary>
    public static string Resolve(string root, string relative) =>
        Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
}
