using System.Diagnostics;
using CUE4Parse.Cli.Commands;

namespace CUE4Parse.Cli.Tests;

public class BlenderImportTests
{
    /// <summary>
    /// Runs tools/blender/check_import.py over a directory of .glb and returns Blender's
    /// exit code plus its output. The script asserts by raising; a non-zero exit is the
    /// failure signal.
    /// </summary>
    private static async Task<(int ExitCode, string Output)> RunCheckAsync(string blender, string directory)
    {
        // The script path is repo-relative, but the test runs from the build output.
        var script = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "tools", "blender", "check_import.py"));
        Assert.True(File.Exists(script), $"check_import.py not found at {script}");

        var psi = new ProcessStartInfo(blender)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in new[] { "-b", "-P", script, "--", directory })
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        return (process.ExitCode, await stdout + await stderr);
    }

    private static string RequireBlender()
    {
        var blender = Environment.GetEnvironmentVariable("BLENDER");
        if (string.IsNullOrWhiteSpace(blender))
        {
            Assert.Skip("BLENDER not set; install the pinned Blender to run this test.");
        }

        return blender!;
    }

    [Theory]
    [InlineData("CUE4ParseFixtures/Content/Fixtures/Meshes/SM_Fixture.uasset")]
    [InlineData("CUE4ParseFixtures/Content/Fixtures/Meshes/SK_Fixture.uasset")]
    public async Task ExportedGlbImportsIntoBlender(string assetPath)
    {
        var blender = RequireBlender();

        var output = await GltfWriterTests.ExportAsync(assetPath);
        var (exitCode, log) = await RunCheckAsync(blender, output.FullName);

        Assert.True(exitCode == 0, $"Blender import failed:\n{log}");
    }

    /// <summary>
    /// The goal condition of this whole effort: a .glb that opens in Blender <em>with
    /// textures</em>. The fixture meshes have no material assigned, so on them this only
    /// proves geometry imports — the images check has nothing to check. A real asset is
    /// the only thing that closes it.
    /// </summary>
    [Fact]
    public async Task ExportedRealMeshImportsIntoBlenderWithResolvableTextures()
    {
        var blender = RequireBlender();
        RealAssets.SkipIfUnavailable();

        var output = await GltfWriterTests.ExportAsync(RealAssets.Profile(), RealAssets.MeshPath!);
        var (exitCode, log) = await RunCheckAsync(blender, output.FullName);

        Assert.True(exitCode == 0, $"Blender import failed:\n{log}");

        // Guards against a vacuous pass: the script only checks images it finds, so a
        // run that imported none would still exit 0.
        Assert.Contains("images", log);
        Assert.DoesNotContain(", 0 images", log);
    }
}
