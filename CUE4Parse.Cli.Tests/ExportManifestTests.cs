using CUE4Parse.Cli.Commands;
using CUE4Parse.Cli.Output;
using CUE4Parse.Cli.Services;
using Newtonsoft.Json.Linq;

namespace CUE4Parse.Cli.Tests;

public class ExportManifestTests
{
    private static async Task<(DirectoryInfo Output, FileInfo Manifest)> ExportWithManifestAsync()
    {
        var output = Directory.CreateTempSubdirectory();
        var manifest = new FileInfo(Path.Combine(Directory.CreateTempSubdirectory().FullName, "manifest.json"));
        var (context, _) = FixtureSupport.Context();

        var code = await ExportCommand.ExecuteAsync(context, new ExportCommandOptions(
            Paths: ["CUE4ParseFixtures/Content/Fixtures/Meshes/SM_Fixture.uasset"],
            Criteria: new MatchCriteria(null, null, null, null),
            Output: output,
            Flags: ExportFlagDefaults.Gltf2(),
            Parallel: 4,
            Force: false,
            Manifest: manifest), TestContext.Current.CancellationToken);

        Assert.Equal((int)ExitCode.Success, code);
        return (output, manifest);
    }

    [Fact]
    public async Task ManifestRecordsClassRelativePathSizeAndHash()
    {
        var (output, manifestFile) = await ExportWithManifestAsync();
        var manifest = JObject.Parse(await File.ReadAllTextAsync(
            manifestFile.FullName, TestContext.Current.CancellationToken));

        Assert.Equal(1, manifest["version"]!.Value<int>());
        var entries = (JArray)manifest["entries"]!;
        Assert.NotEmpty(entries);

        var mesh = entries.Single(e => e["class"]!.Value<string>() == "StaticMesh");
        var file = (JObject)mesh["files"]![0]!;

        var path = file["path"]!.Value<string>()!;
        Assert.DoesNotContain('\\', path);
        Assert.True(File.Exists(Path.Combine(output.FullName, path.Replace('/', Path.DirectorySeparatorChar))));
        Assert.True(file["bytes"]!.Value<long>() > 0);
        Assert.Equal(64, file["sha256"]!.Value<string>()!.Length);
    }

    [Fact]
    public async Task EntriesAreSortedByObjectPathSoTheManifestDiffs()
    {
        var (_, manifestFile) = await ExportWithManifestAsync();
        var entries = (JArray)JObject.Parse(await File.ReadAllTextAsync(
            manifestFile.FullName, TestContext.Current.CancellationToken))["entries"]!;

        var paths = entries.Select(e => e["objectPath"]!.Value<string>()!).ToArray();
        Assert.Equal(paths.OrderBy(p => p, StringComparer.Ordinal), paths);
    }

    /// <summary>
    /// ExportSession runs in parallel, so NDJSON line order changes between runs and
    /// output cannot be diffed. The manifest is the ordered view — and it must be
    /// byte-identical across two runs with the same arguments.
    /// </summary>
    [Fact]
    public async Task TwoRunsWithTheSameArgumentsProduceByteIdenticalManifests()
    {
        var (_, first) = await ExportWithManifestAsync();
        var (_, second) = await ExportWithManifestAsync();

        Assert.Equal(
            await File.ReadAllBytesAsync(first.FullName, TestContext.Current.CancellationToken),
            await File.ReadAllBytesAsync(second.FullName, TestContext.Current.CancellationToken));
    }
}
