using Talt.Client.Saves;
using Talt.Tests.Support;

namespace Talt.Client.Tests.Saves;

public class SaveStoreTests
{
    [Fact]
    public void Autosave_counts_unpaused_play_and_waits_for_the_writer()
    {
        var scheduler = new AutosaveScheduler();
        Assert.False(scheduler.Update(600, true, 5, false));
        Assert.False(scheduler.Update(299, false, 5, false));
        Assert.False(scheduler.Update(1, false, 5, true));
        Assert.False(scheduler.Update(0, true, 5, false));
        Assert.True(scheduler.Update(0, false, 5, false));
        scheduler.Reset();
        Assert.False(scheduler.Update(1200, false, 0, false));
        Assert.Equal(0, scheduler.PlayedSeconds);
        AllocationAssert.NoSteadyStateAllocations(() => scheduler.Update(0.01, false, 30, false), 100, "autosave scheduling");
    }

    [Theory]
    [InlineData("../quicksave", "_quicksave")]
    [InlineData("CON.txt", "_CON.txt")]
    [InlineData("AUTOSAVE-1", "_AUTOSAVE-1")]
    [InlineData("quicksave", "_quicksave")]
    public void Manual_names_are_safe(string input, string expected) => Assert.Equal(expected, SaveStore.SafeFileName(input));

    [Fact]
    public void Rotation_cleans_surplus_slots_even_when_a_lower_slot_is_free()
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            var store = new SaveStore(folder.FullName);
            var manual = store.PathFor("Player");
            File.WriteAllText(manual, "manual");
            File.WriteAllText(Path.Combine(folder.FullName, "autosave-3.talt"), "old");
            Assert.Equal(Path.Combine(folder.FullName, "autosave-1.talt"), store.NextAutosavePath(2));
            Assert.True(File.Exists(Path.Combine(folder.FullName, "autosave-3.talt")));
            File.WriteAllText(Path.Combine(folder.FullName, "autosave-1.talt"), "new");
            store.PruneAutosaves(1);
            Assert.Equal(manual, store.Existing("player"));
            Assert.Equal(2, store.List().Count);
            Assert.All(store.List(), entry => Assert.True(entry.IsDamaged));
        }
        finally
        {
            folder.Delete(true);
        }
    }
}
