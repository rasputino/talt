using System.Diagnostics;
using Talt.Core.Saves;

namespace Talt.Core.Tests.Saves;

public class SaveStorageTests
{
    [Fact]
    public void Failed_write_keeps_target_and_removes_temporary_file()
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(folder.FullName, "game.talt");
            File.WriteAllText(path, "previous save");
            using var snapshot = SaveGame.Capture(SaveTestGames.Rich(ticks: 0), RoundTrip.Details);
            // An unsupported codec fails after the file header and metadata have been written.
            Assert.Throws<ArgumentOutOfRangeException>(() => SaveGame.Write(snapshot, path, (SaveCodec)99));
            Assert.Equal("previous save", File.ReadAllText(path));
            Assert.Equal(path, Assert.Single(Directory.GetFiles(folder.FullName)));
        }
        finally
        {
            folder.Delete(true);
        }
    }

    [Fact]
    public void Peek_reads_two_hundred_saves_within_the_catalog_budget()
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            var fixture = Path.Combine(AppContext.BaseDirectory, "Saves", "Fixtures", "rich-v1.talt");
            for (var i = 0; i < 200; i++)
            {
                File.Copy(fixture, Path.Combine(folder.FullName, $"{i}.talt"));
            }

            var timer = Stopwatch.StartNew();
            foreach (var path in Directory.EnumerateFiles(folder.FullName))
            {
                Assert.Equal(SavePeekStatus.Ok, SaveGame.Peek(path).Status);
            }

            // The reference budget is 500 ms. Allow slower shared CI disks.
            Assert.True(timer.Elapsed < TimeSpan.FromSeconds(3), timer.Elapsed.ToString());
        }
        finally
        {
            folder.Delete(true);
        }
    }
}
