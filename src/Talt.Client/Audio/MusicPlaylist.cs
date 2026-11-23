namespace Talt.Client.Audio;

/// <summary>
/// Ordered music playlist (data-model.md MusicPlaylist). It tracks the current track, skips files
/// that fail to play and loops after the last one; the audio system owns the actual voice.
/// </summary>
internal sealed class MusicPlaylist
{
    private readonly string[] _tracks;
    private readonly bool[] _failed;
    private int _index = -1;

    public MusicPlaylist(string[] tracks)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        _tracks = tracks;
        _failed = new bool[tracks.Length];
        if (tracks.Length == 0)
        {
            IsDisabled = true;
        }
    }

    public int CurrentIndex => _index;

    public bool IsDisabled { get; private set; }

    public bool HasStarted => _index >= 0;

    public int TrackCount => _tracks.Length;

    public string? CurrentTrack => !IsDisabled && _index >= 0 ? _tracks[_index] : null;

    /// <summary>Selects the first playable track; returns null when every track failed.</summary>
    public string? Begin()
    {
        if (IsDisabled)
        {
            return null;
        }

        _index = 0;
        if (_failed[0])
        {
            return Next();
        }

        return _tracks[0];
    }

    /// <summary>Advances to the next playable track, wrapping; returns null when all failed.</summary>
    public string? Next()
    {
        if (IsDisabled)
        {
            return null;
        }

        for (var step = 0; step < _tracks.Length; step++)
        {
            _index = (_index + 1) % _tracks.Length;
            if (!_failed[_index])
            {
                return _tracks[_index];
            }
        }

        IsDisabled = true;
        _index = -1;
        return null;
    }

    /// <summary>Marks the current track as unplayable; the next call to <see cref="Next"/> skips it.</summary>
    public void MarkFailed()
    {
        if ((uint)_index < (uint)_failed.Length)
        {
            _failed[_index] = true;
        }
    }

    /// <summary>Whether every track has failed (music is disabled for the session).</summary>
    public bool AllFailed
    {
        get
        {
            foreach (var failed in _failed)
            {
                if (!failed)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
