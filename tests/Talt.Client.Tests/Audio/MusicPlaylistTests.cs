using Talt.Client.Audio;
using Xunit;

namespace Talt.Client.Tests.Audio;

public class MusicPlaylistTests
{
    [Fact]
    public void Playlist_AdvancesAndLoops()
    {
        var playlist = new MusicPlaylist(["one", "two"]);
        Assert.Equal("one", playlist.Begin());
        Assert.Equal(0, playlist.CurrentIndex);
        Assert.Equal("two", playlist.Next());
        Assert.Equal("one", playlist.Next());
        Assert.True(playlist.HasStarted);
    }

    [Fact]
    public void FailedTrack_IsSkipped()
    {
        var playlist = new MusicPlaylist(["one", "two", "three"]);
        playlist.Begin();
        playlist.MarkFailed();
        Assert.Equal("two", playlist.Next());
        Assert.False(playlist.IsDisabled);
    }

    [Fact]
    public void AllTracksFailing_DisablesMusic()
    {
        var playlist = new MusicPlaylist(["one", "two"]);
        playlist.Begin();
        playlist.MarkFailed();
        playlist.Next();
        playlist.MarkFailed();
        Assert.Null(playlist.Next());
        Assert.True(playlist.IsDisabled);
    }

    [Fact]
    public void EmptyPlaylist_IsDisabled()
    {
        var playlist = new MusicPlaylist([]);
        Assert.True(playlist.IsDisabled);
        Assert.Null(playlist.Begin());
    }

    [Fact]
    public void ThirtyMinuteSoak_KeepsCycling()
    {
        // SC-005: the real tracks are about a minute each; a fake-time soak covers many loops.
        var playlist = new MusicPlaylist(["one", "two"]);
        Assert.Equal("one", playlist.Begin());
        for (var i = 0; i < 60; i++)
        {
            Assert.NotNull(playlist.Next());
        }

        Assert.False(playlist.IsDisabled);
    }
}
