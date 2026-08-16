using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse_Conversion.Exporters;
using CUE4Parse_Conversion.Writers.Gltf;
using SkiaSharp;

namespace CUE4Parse.Cli.Tests;

public class OrmTextureExporterTests
{
    [Fact]
    public void OrmExporterTakesADifferentObjectPathFromThePlainTextureExporter()
    {
        var texture = FixtureAssets.LoadExport<UTexture>(
            "CUE4ParseFixtures/Content/Fixtures/Textures/T_BC3.uasset", "T_BC3");

        var plain = new TextureExporter(texture);
        var orm = new OrmTextureExporter(texture);

        // Distinct ObjectPath is what makes ExportSession.Add's first-wins dedup keep
        // both, instead of the ORM image depending on which material enqueued first.
        Assert.NotEqual(plain.ObjectPath, orm.ObjectPath);
        Assert.Equal(plain.ObjectName + GltfMaterialBinder.OrmSuffix, orm.ObjectName);

        // Same folder as the source texture — a sibling, not a nested subfolder.
        Assert.Equal(plain.SaveDirectory, orm.SaveDirectory);
        Assert.Equal(plain.SavePath + GltfMaterialBinder.OrmSuffix, orm.SavePath);
    }

    /// <summary>
    /// The channel transposition, checked pixel by pixel against the source image the
    /// same run wrote. Needs a material whose SpecularMasks slot is populated, which no
    /// fixture has — see <see cref="RealAssets"/>.
    /// </summary>
    [Fact]
    public async Task ExportedOrmImageHasGreenFromTheSourceBlueAndBlueFromTheSourceGreen()
    {
        RealAssets.SkipIfUnavailable();

        var output = await GltfWriterTests.ExportAsync(RealAssets.Profile(), RealAssets.MeshPath!);

        // Pair by construction, source -> sibling. Globbing for "*_ORM.png" would be
        // wrong: games routinely name their own SRM textures "<something>_ORM", so that
        // glob matches the plain TextureExporter output too, and stripping the suffix
        // then points at a file that never existed. The repacked sibling of X is always
        // exactly X + "_ORM".
        var pairs = Directory
            .GetFiles(output.FullName, "*.png", SearchOption.AllDirectories)
            .Select(source => (Source: source, Orm: Path.Combine(
                Path.GetDirectoryName(source)!,
                Path.GetFileNameWithoutExtension(source) + GltfMaterialBinder.OrmSuffix + ".png")))
            .Where(pair => File.Exists(pair.Orm))
            .ToArray();

        Assert.NotEmpty(pairs);

        var compared = 0;
        foreach (var (source, orm) in pairs)
        {
            using var ormBitmap = SKBitmap.Decode(orm);
            using var sourceBitmap = SKBitmap.Decode(source);
            Assert.Equal(sourceBitmap.Width, ormBitmap.Width);
            Assert.Equal(sourceBitmap.Height, ormBitmap.Height);

            for (var y = 0; y < ormBitmap.Height; y += Math.Max(1, ormBitmap.Height / 8))
            for (var x = 0; x < ormBitmap.Width; x += Math.Max(1, ormBitmap.Width / 8))
            {
                var src = sourceBitmap.GetPixel(x, y);
                var dst = ormBitmap.GetPixel(x, y);

                Assert.Equal(src.Blue, dst.Green);   // roughness
                Assert.Equal(src.Green, dst.Blue);   // metallic
                Assert.Equal(255, dst.Red);          // occlusion is not carried; keep it neutral
            }

            compared++;
        }

        Assert.True(compared > 0, "No ORM image was compared against its source.");
    }
}
