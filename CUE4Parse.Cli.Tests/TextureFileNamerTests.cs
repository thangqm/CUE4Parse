using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse_Conversion.Exporters;
using CUE4Parse_Conversion.Options;
using CUE4Parse_Conversion.Textures;

namespace CUE4Parse.Cli.Tests;

public class TextureFileNamerTests
{
    private static UTexture LoadFixtureTexture(string name) =>
        FixtureAssets.LoadExport<UTexture>($"CUE4ParseFixtures/Content/Fixtures/Textures/{name}.uasset", name);

    [Theory]
    [InlineData(ETextureFormat.Png, "png")]
    [InlineData(ETextureFormat.Jpeg, "jpg")]
    [InlineData(ETextureFormat.Tga, "tga")]
    [InlineData(ETextureFormat.Webp, "webp")]
    public void ExtensionFollowsTheRequestedFormatForNonHdrTextures(ETextureFormat format, string expected)
    {
        var texture = LoadFixtureTexture("T_BC3");
        var options = new ExportOptions(meshFormat: EMeshFormat.UEFormat, textureFormat: format);

        Assert.Equal(expected, TextureFileNamer.Extension(texture, options));
    }

    [Fact]
    public void AllMipsAddsAMipSuffixAndTheDefaultBranchAddsNone()
    {
        var texture = LoadFixtureTexture("T_BC3");

        var single = new ExportOptions(meshFormat: EMeshFormat.UEFormat);
        Assert.Null(TextureFileNamer.Suffix(texture, single));

        var all = new ExportOptions(meshFormat: EMeshFormat.UEFormat, exportAllTextureMips: true);
        Assert.Equal($"_MIP{texture.GetFirstMipIndex()}", TextureFileNamer.Suffix(texture, all));
    }

    [Fact]
    public void Gltf2ForcesHdrTexturesDownToTheRequestedRasterFormat()
    {
        var options = new ExportOptions(meshFormat: EMeshFormat.Gltf2, exportHdrTexturesAsHdr: true);
        Assert.False(options.ExportHdrTexturesAsHdr);
    }

    [Fact]
    public void TextureArraysGetALayerSuffix()
    {
        var array = FixtureAssets.LoadExport<UTexture>(
            "CUE4ParseFixtures/Content/Fixtures/Textures/T_Array.uasset", "T_Array");
        Assert.IsAssignableFrom<UTexture2DArray>(array);

        var options = new ExportOptions(meshFormat: EMeshFormat.UEFormat);
        Assert.Equal("_LAYER2", TextureFileNamer.Suffix(array, options, layer: 2));
    }

    /// <summary>
    /// The namer predicts what TextureExporter will actually write. If the two ever
    /// disagree, every glTF image URI points at a file that does not exist.
    /// <para>
    /// Note the two sides read different things: the encoder branches on the *decoded*
    /// CTexture's pixel format, the namer on the source UTexture's cooked format name.
    /// That is precisely the drift this test exists to catch.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(ETextureFormat.Png)]
    [InlineData(ETextureFormat.Jpeg)]
    [InlineData(ETextureFormat.Tga)]
    [InlineData(ETextureFormat.Webp)]
    public void NamerAgreesWithTheEncoderOnEveryFixtureTexture(ETextureFormat format)
    {
        var checkedAny = false;

        foreach (var hdr in new[] { true, false })
        foreach (var name in FixtureAssets.TextureNames)
        {
            var texture = LoadFixtureTexture(name);
            var options = new ExportOptions(
                meshFormat: EMeshFormat.UEFormat, textureFormat: format, exportHdrTexturesAsHdr: hdr);

            var decoded = texture.DecodeMip(texture.GetFirstMipIndex(), options.TexturePlatform);
            if (decoded is null) continue;

            decoded.Encode(options, out var actual);
            Assert.Equal(actual, TextureFileNamer.Extension(texture, options));
            checkedAny = true;
        }

        Assert.True(checkedAny, "No fixture texture decoded; the agreement was never actually checked.");
    }
}
