using CUE4Parse.Cli.Commands;
using CUE4Parse.Cli.Output;
using CUE4Parse.Cli.Services;
using Newtonsoft.Json.Linq;

namespace CUE4Parse.Cli.Tests;

public class GltfWriterTests
{
    /// <summary>Reads the JSON chunk out of a binary glTF container.</summary>
    public static JObject ReadGlbJson(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var length = BitConverter.ToInt32(bytes, 12);
        return JObject.Parse(System.Text.Encoding.UTF8.GetString(bytes, 20, length));
    }

    /// <summary>Exports one asset from the fixture archives into a fresh temp directory.</summary>
    public static Task<DirectoryInfo> ExportAsync(string assetPath, ExportFlags? flags = null)
        => ExportAsync(FixtureSupport.Profile(), assetPath, flags);

    public static async Task<DirectoryInfo> ExportAsync(
        ResolvedProfile profile, string assetPath, ExportFlags? flags = null)
    {
        var output = Directory.CreateTempSubdirectory();
        var (context, _) = FixtureSupport.Context(profile);

        var code = await ExportCommand.ExecuteAsync(context, new ExportCommandOptions(
            Paths: [assetPath],
            Criteria: new MatchCriteria(null, null, null, null),
            Output: output,
            Flags: flags ?? ExportFlagDefaults.Gltf2(),
            Parallel: 1,
            Force: false), TestContext.Current.CancellationToken);

        Assert.Equal((int)ExitCode.Success, code);
        return output;
    }

    public static string SingleGlb(DirectoryInfo output)
        => Directory.GetFiles(output.FullName, "*.glb", SearchOption.AllDirectories).Single();

    [Fact]
    public async Task MeshDatablockAndRootNodeAreNamedAfterTheFile()
    {
        var output = await ExportAsync("CUE4ParseFixtures/Content/Fixtures/Meshes/SM_Fixture.uasset");
        var glb = SingleGlb(output);

        var expected = Path.GetFileNameWithoutExtension(glb);
        var json = ReadGlbJson(glb);

        Assert.Equal(expected, json["meshes"]![0]!["name"]!.Value<string>());
        Assert.Contains(((JArray)json["nodes"]!).Select(node => node["name"]?.Value<string>()), name => name == expected);
    }

    /// <summary>
    /// The armature root is the file stem plus <c>.ao</c> — nothing more. Upstream also
    /// appends <c>lod._suffix</c> here; this fork must not, because
    /// <c>GltfMeshFormat.DatablockName</c> already builds the name as
    /// <c>ObjectName + lod._suffix</c>, so appending it again gives
    /// <c>SK_X_LOD2.ao_LOD2</c>. Nothing else pins this, and it type-checks either way.
    /// </summary>
    [Fact]
    public async Task SkeletalArmatureRootIsTheFileNamePlusAo()
    {
        var flags = ExportFlagDefaults.Gltf2() with { MeshQuality = "all" };
        var output = await ExportAsync("CUE4ParseFixtures/Content/Fixtures/Meshes/SK_Fixture.uasset", flags);

        var files = Directory.GetFiles(output.FullName, "*.glb", SearchOption.AllDirectories);
        Assert.True(files.Length > 1, $"single-LOD export cannot tell this fork from upstream; got {files.Length} glb file(s)");

        foreach (var glb in files)
        {
            var expected = Path.GetFileNameWithoutExtension(glb) + ".ao";
            var names = ((JArray)ReadGlbJson(glb)["nodes"]!).Select(node => node["name"]?.Value<string>());

            Assert.Contains(expected, names);
        }
    }

    /// <summary>
    /// §5 of the output contract holds for the <c>_LOD{n}</c> siblings too, not just for a
    /// single-LOD export: every <c>.glb</c> names its datablock and root node after its
    /// own file. <c>MeshDatablockAndRootNodeAreNamedAfterTheFile</c> only ever sees one
    /// file, so it cannot catch a writer that ignores <c>lod._suffix</c>.
    /// <para>
    /// The <c>_Nanite</c> suffix is not reachable from the fixture set: <c>SM_Nanite</c>
    /// carries no render LODs, so every <c>--nanite</c> mode yields its Nanite LOD first
    /// and therefore unsuffixed.
    /// </para>
    /// </summary>
    [Fact]
    public async Task EverySuffixedSiblingIsNamedAfterItsOwnFile()
    {
        var flags = ExportFlagDefaults.Gltf2() with { MeshQuality = "all" };
        var output = await ExportAsync("CUE4ParseFixtures/Content/Fixtures/Meshes/SM_Fixture.uasset", flags);

        var files = Directory.GetFiles(output.FullName, "*.glb", SearchOption.AllDirectories);
        var stems = files.Select(Path.GetFileNameWithoutExtension).ToList();

        Assert.Contains(stems, stem => stem == "SM_Fixture");
        Assert.Contains(stems, stem => stem!.StartsWith("SM_Fixture_LOD", StringComparison.Ordinal));

        foreach (var glb in files)
        {
            var expected = Path.GetFileNameWithoutExtension(glb);
            var json = ReadGlbJson(glb);

            Assert.Equal(expected, json["meshes"]![0]!["name"]!.Value<string>());
            Assert.Contains(expected, ((JArray)json["nodes"]!).Select(node => node["name"]?.Value<string>()));
        }
    }

    [Fact]
    public async Task NoMaterialsFallsBackToBareMaterialSlots()
    {
        var flags = ExportFlagDefaults.Gltf2() with { NoMaterials = true };
        var output = await ExportAsync("CUE4ParseFixtures/Content/Fixtures/Meshes/SM_Fixture.uasset", flags);
        var glb = SingleGlb(output);

        var json = ReadGlbJson(glb);

        Assert.Null(json["images"]);
        Assert.NotEmpty((JArray)json["materials"]!);
    }

    /// <summary>
    /// SM_Fixture's two material slots both have a null MaterialInterface, so the writer
    /// falls back to bare slots. This pins that path — reached here by the asset itself
    /// rather than by the flag, as <see cref="NoMaterialsFallsBackToBareMaterialSlots"/>
    /// does — and documents that it is the reason no fixture export can carry images.
    /// <para>
    /// The slot is named "None" rather than the mesh's "Primary": with a null
    /// MaterialInterface the mesh DTO has no material to take a name from. That is
    /// pre-existing behaviour, unchanged here, and asserted so a future change to it is
    /// visible rather than silent.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AMeshWithNoMaterialAssignedGetsBareSlotsAndNoImages()
    {
        var output = await ExportAsync("CUE4ParseFixtures/Content/Fixtures/Meshes/SM_Fixture.uasset");
        var json = ReadGlbJson(SingleGlb(output));

        Assert.Null(json["images"]);

        var names = ((JArray)json["materials"]!).Select(m => m["name"]?.Value<string>() ?? "").ToArray();
        Assert.NotEmpty(names);
        Assert.All(names, name => Assert.False(string.IsNullOrWhiteSpace(name)));
        Assert.Equal(["None"], names);
    }

    // ---------------------------------------------------------------------------------
    // Real-asset checks. See RealAssets for why these cannot run on the fixture set.
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task RealMeshGlbCarriesImagesAndTexturesRatherThanBareMaterials()
    {
        RealAssets.SkipIfUnavailable();

        var output = await ExportAsync(RealAssets.Profile(), RealAssets.MeshPath!);
        var glb = Directory.GetFiles(output.FullName, "*.glb", SearchOption.AllDirectories).First();

        var json = ReadGlbJson(glb);

        Assert.NotNull(json["images"]);
        Assert.NotEmpty((JArray)json["images"]!);
        Assert.NotNull(json["textures"]);
        Assert.NotEmpty((JArray)json["textures"]!);
    }

    /// <summary>
    /// The single assertion the whole design turns on: the URI the writer emitted and the
    /// file the session wrote are the same file. A fresh temp directory per run is what
    /// stops a leftover file from a previous run making this green for the wrong reason.
    /// </summary>
    [Fact]
    public async Task EveryImageUriResolvesToAFileThatThisRunActuallyWrote()
    {
        RealAssets.SkipIfUnavailable();

        var output = await ExportAsync(RealAssets.Profile(), RealAssets.MeshPath!);

        var checkedAny = false;
        foreach (var glb in Directory.GetFiles(output.FullName, "*.glb", SearchOption.AllDirectories))
        {
            var json = ReadGlbJson(glb);
            if (json["images"] is not JArray images) continue;

            var glbDirectory = Path.GetDirectoryName(glb)!;
            foreach (var uri in images.Select(image => image["uri"]!.Value<string>()!))
            {
                var decoded = Uri.UnescapeDataString(uri);
                var resolved = Path.GetFullPath(Path.Combine(
                    glbDirectory, decoded.Replace('/', Path.DirectorySeparatorChar)));
                Assert.True(File.Exists(resolved), $"glTF image URI '{uri}' does not resolve to a file: {resolved}");
                checkedAny = true;
            }
        }

        Assert.True(checkedAny, "No glTF image URI was checked; the export produced no images.");
    }
}
