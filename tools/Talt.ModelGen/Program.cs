using System.Text;
using Talt.Core.Content;
using Talt.Core.Meshing.Models;
using Talt.Core.Towns;
using Talt.ModelGen;

// Generates the original voxel models of TALT (feature 018, research R8) as MagicaVoxel .vox files plus the
// model assignment file models.json. Deterministic: running it again without changes rewrites the same bytes.
//
//   dotnet run --project tools/Talt.ModelGen -c Release [-- <repository root>]
var root = args.Length > 0 ? args[0] : FindRepositoryRoot();
var modelFolder = Path.Combine(root, "src", "Talt.Client", "Assets", "Models");
var assignmentFile = Path.Combine(root, "src", "Talt.Client", "Rendering", "Data", "models.json");

var set = ModelCatalog.Build(TownRules.Default.Buildings, GameContent.LoadDefault().AllIndustryKinds);
Directory.CreateDirectory(modelFolder);
foreach (var stale in Directory.GetFiles(modelFolder, "*.vox"))
{
    if (!set.Files.ContainsKey(Path.GetFileName(stale)))
    {
        File.Delete(stale);
    }
}

foreach (var (file, model) in set.Files)
{
    File.WriteAllBytes(Path.Combine(modelFolder, file), VoxWriter.Write(model));
}

File.WriteAllText(assignmentFile, set.AssignmentJson(), new UTF8Encoding(false));
Console.WriteLine($"Wrote {set.Files.Count} models and {set.Keys.Count} model keys to {modelFolder}.");
return 0;

static string FindRepositoryRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Talt.slnx")))
    {
        directory = directory.Parent;
    }

    return directory?.FullName ?? throw new InvalidOperationException("Run from inside the repository or pass its root folder.");
}
