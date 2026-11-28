namespace Talt.Client.Saves;

/// <summary>
/// Decides when to autosave (FR-011, research R16): after the configured number of real minutes of unpaused play, at any
/// game speed. Paused time and time in menus that pause the game do not count. It measures real time, which is why it
/// lives in the client (Core never reads the clock). Allocation-free per frame.
/// </summary>
internal sealed class AutosaveScheduler
{
    private double _playedSeconds;

    /// <summary>Unpaused seconds since the last autosave (or load, or start).</summary>
    public double PlayedSeconds => _playedSeconds;

    /// <summary>
    /// Adds a frame's time; returns true when an autosave is due. <paramref name="writing"/> holds the request back while
    /// a save is being written (the next frame after it finishes saves).
    /// </summary>
    public bool Update(double elapsedSeconds, bool paused, int intervalMinutes, bool writing)
    {
        if (intervalMinutes <= 0)
        {
            _playedSeconds = 0;
            return false;
        }

        if (!paused && elapsedSeconds > 0)
        {
            _playedSeconds += elapsedSeconds;
        }

        return !paused && !writing && _playedSeconds >= intervalMinutes * 60.0;
    }

    /// <summary>Starts counting again (after an autosave, a manual save or a load).</summary>
    public void Reset() => _playedSeconds = 0;
}
