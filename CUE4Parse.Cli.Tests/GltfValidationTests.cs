namespace CUE4Parse.Cli.Tests;

public class GltfValidationTests
{
    [Theory]
    [InlineData("CUE4ParseFixtures/Content/Fixtures/Meshes/SM_Fixture.uasset")]
    [InlineData("CUE4ParseFixtures/Content/Fixtures/Meshes/SK_Fixture.uasset")]
    [InlineData("CUE4ParseFixtures/Content/Fixtures/Meshes/SM_Nanite.uasset")]
    public async Task ExportedGlbHasNoValidatorErrors(string assetPath)
    {
        if (!GltfValidator.TryLocate(out _))
        {
            Assert.Skip("GLTF_VALIDATOR not set; install the pinned validator to run this test.");
        }

        var output = await GltfWriterTests.ExportAsync(assetPath);

        var checkedAny = false;
        foreach (var glb in Directory.GetFiles(output.FullName, "*.glb", SearchOption.AllDirectories))
        {
            var report = GltfValidator.Validate(glb);
            Assert.True(report.Errors == 0, $"{Path.GetFileName(glb)} has {report.Errors} validator errors:\n{report.Raw}");
            checkedAny = true;
        }

        Assert.True(checkedAny, $"No .glb was produced for {assetPath}; nothing was validated.");
    }

    /// <summary>
    /// The same check on a mesh that actually has textures. On the fixture set every
    /// image URI is absent, so the validator's UNRESOLVED_REFERENCE checks — the ones
    /// that matter most here — have nothing to resolve.
    /// </summary>
    [Fact]
    public async Task ExportedRealMeshGlbHasNoValidatorErrors()
    {
        if (!GltfValidator.TryLocate(out _))
        {
            Assert.Skip("GLTF_VALIDATOR not set; install the pinned validator to run this test.");
        }

        RealAssets.SkipIfUnavailable();

        var output = await GltfWriterTests.ExportAsync(RealAssets.Profile(), RealAssets.MeshPath!);

        foreach (var glb in Directory.GetFiles(output.FullName, "*.glb", SearchOption.AllDirectories))
        {
            var report = GltfValidator.Validate(glb);
            Assert.True(report.Errors == 0, $"{Path.GetFileName(glb)} has {report.Errors} validator errors:\n{report.Raw}");
        }
    }
}
