using SkiaSharp;

namespace CUE4Parse.Cli.Tests;

public class FlipNormalYTests
{
    /// <summary>
    /// Selection is by CompressionSettings == TC_Normalmap, a property of the texture
    /// itself, not by CMaterialParams2's name-based classification: a texture knows what
    /// it is, a name heuristic only guesses.
    /// <para>
    /// Real-asset gated. The plan wrote this against SM_Fixture, but that mesh has no
    /// material assigned, so its export writes no PNG at all and the comparison loop
    /// would have no body to run — the assertion could only ever have been skipped.
    /// Stellar Blade has confirmed TC_Normalmap textures (spec §5.3), so the check runs
    /// for real here instead of being disabled.
    /// </para>
    /// </summary>
    [Fact]
    public async Task FlipNormalYInvertsTheGreenChannelOfNormalMapsOnly()
    {
        RealAssets.SkipIfUnavailable();

        var profile = RealAssets.Profile();
        var plain = await GltfWriterTests.ExportAsync(profile, RealAssets.MeshPath!);
        var flipped = await GltfWriterTests.ExportAsync(
            profile, RealAssets.MeshPath!, ExportFlagDefaults.Gltf2() with { FlipNormalY = true });

        var changed = 0;
        var compared = 0;

        foreach (var before in Directory.GetFiles(plain.FullName, "*.png", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(plain.FullName, before);
            var after = Path.Combine(flipped.FullName, relative);
            Assert.True(File.Exists(after), after);

            using var a = SKBitmap.Decode(before);
            using var b = SKBitmap.Decode(after);
            if (a is null || b is null) continue;

            compared++;

            var pa = a.GetPixel(a.Width / 2, a.Height / 2);
            var pb = b.GetPixel(b.Width / 2, b.Height / 2);

            if (pa.Green == pb.Green) continue;

            changed++;

            // Green inverts exactly; nothing else may move.
            Assert.Equal(255 - pa.Green, pb.Green);
            Assert.Equal(pa.Red, pb.Red);
            Assert.Equal(pa.Blue, pb.Blue);
        }

        Assert.True(compared > 0, "No PNG was compared; the export produced no textures.");
        Assert.True(changed > 0, "No normal map changed; the export has no TC_Normalmap texture.");
    }
}
