using CUE4Parse.Cli.Commands;
using CUE4Parse.Cli.Output;
using CUE4Parse.Cli.Services;
using CUE4Parse.UE4.Assets.Exports.Material;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse_Conversion.Options;
using CUE4Parse_Conversion.Writers.UEFormat.Enums;

namespace CUE4Parse.Cli.Tests;

public class ExportOptionsMapperTests
{
    private static ExportFlags Defaults() => new(
        MeshFormat: "ueformat", TextureFormat: "png", TexturePlatform: "desktop",
        MeshQuality: "highest", Nanite: "no-nanite", SocketFormat: "bone",
        MaterialDepth: "top-layer-only", TextureQuality: 100, NoMaterials: false, AllMips: false);

    [Fact]
    public void MapTranslatesAllDefaultsToTheExpectedEnums()
    {
        var mapped = ExportOptionsMapper.Map(Defaults());

        Assert.Equal(EMeshFormat.UEFormat, mapped.MeshFormat);
        Assert.Equal(ETextureFormat.Png, mapped.TextureFormat);
        Assert.Equal(ETexturePlatform.DesktopMobile, mapped.TexturePlatform);
        Assert.Equal(EMeshQuality.Highest, mapped.MeshQuality);
        Assert.Equal(ENaniteMeshFormat.NoNanite, mapped.NaniteMeshFormat);
        Assert.Equal(ESocketFormat.Bone, mapped.SocketFormat);
        Assert.Equal(EMaterialDepth.TopLayerOnly, mapped.MaterialDepth);
        Assert.True(mapped.ExportMaterials);
    }

    [Theory]
    [InlineData("desktop", ETexturePlatform.DesktopMobile)]
    [InlineData("xbox-ps4", ETexturePlatform.XboxAndPlaystation4)]
    [InlineData("switch", ETexturePlatform.NintendoSwitch)]
    [InlineData("ps5", ETexturePlatform.Playstation5)]
    public void MapTranslatesEachTexturePlatform(string flag, ETexturePlatform expected)
        => Assert.Equal(expected, ExportOptionsMapper.Map(Defaults() with { TexturePlatform = flag }).TexturePlatform);

    /// <summary>
    /// ExportOptions rewrites TextureFormat to Png for USD regardless of the flag.
    /// Documented here so the behaviour is not mistaken for a mapper bug.
    /// </summary>
    [Fact]
    public void UsdForcesPngTexturesRegardlessOfTheTextureFormatFlag()
        => Assert.Equal(
            ETextureFormat.Png,
            ExportOptionsMapper.Map(Defaults() with { MeshFormat = "usd", TextureFormat = "tga" }).TextureFormat);

    [Theory]
    [InlineData("actorx", EMeshFormat.ActorX)]
    [InlineData("gltf2", EMeshFormat.Gltf2)]
    [InlineData("usd", EMeshFormat.USD)]
    public void MapTranslatesEachMeshFormat(string flag, EMeshFormat expected)
        => Assert.Equal(expected, ExportOptionsMapper.Map(Defaults() with { MeshFormat = flag }).MeshFormat);

    [Theory]
    [InlineData("nanite-only", ENaniteMeshFormat.NaniteOnly)]
    [InlineData("nanite-first", ENaniteMeshFormat.NaniteFirst)]
    [InlineData("nanite-last", ENaniteMeshFormat.NaniteLast)]
    public void MapTranslatesEachNaniteMode(string flag, ENaniteMeshFormat expected)
        => Assert.Equal(expected, ExportOptionsMapper.Map(Defaults() with { Nanite = flag }).NaniteMeshFormat);

    [Fact]
    public void NoMaterialsFlagDisablesMaterialExport()
        => Assert.False(ExportOptionsMapper.Map(Defaults() with { NoMaterials = true }).ExportMaterials);

    [Fact]
    public void MapThrowsUsageErrorForAnUnknownMeshFormat()
    {
        var ex = Assert.Throws<CliException>(
            () => ExportOptionsMapper.Map(Defaults() with { MeshFormat = "nope" }));

        Assert.Equal(ExitCode.Usage, ex.ExitCode);
        Assert.Equal("BAD_OPTION", ex.ErrorCode);
    }

    [Theory]
    [InlineData("tga")]
    [InlineData("webp")]
    public void Gltf2RejectsTextureFormatsGltfCannotCarry(string textureFormat)
    {
        var flags = ExportFlagDefaults.Gltf2() with { TextureFormat = textureFormat };

        var ex = Assert.Throws<CliException>(() => ExportOptionsMapper.Map(flags));

        Assert.Equal(ExitCode.Usage, ex.ExitCode);
        Assert.Equal("BAD_OPTION", ex.ErrorCode);
    }

    [Fact]
    public void CompressionFormatDefaultsToNoneAndMapsByName()
    {
        Assert.Equal(EFileCompressionFormat.None, ExportOptionsMapper.Map(Ueformat()).CompressionFormat);
        Assert.Equal(EFileCompressionFormat.ZSTD,
            ExportOptionsMapper.Map(Ueformat() with { CompressionFormat = "zstd" }).CompressionFormat);
        Assert.Equal(EFileCompressionFormat.GZIP,
            ExportOptionsMapper.Map(Ueformat() with { CompressionFormat = "gzip" }).CompressionFormat);
    }

    [Fact]
    public void MorphTargetsAreExportedUnlessTheFlagOptsOut()
    {
        Assert.True(ExportOptionsMapper.Map(Ueformat()).ExportMorphTargets);
        Assert.False(ExportOptionsMapper.Map(Ueformat() with { NoMorphTargets = true }).ExportMorphTargets);
    }

    [Fact]
    public void HdrIsKeptUnlessTheFlagOptsOutAndIsAlwaysOffForGltf2()
    {
        Assert.True(ExportOptionsMapper.Map(Ueformat()).ExportHdrTexturesAsHdr);
        Assert.False(ExportOptionsMapper.Map(Ueformat() with { NoHdr = true }).ExportHdrTexturesAsHdr);
        Assert.False(ExportOptionsMapper.Map(ExportFlagDefaults.Gltf2()).ExportHdrTexturesAsHdr);
    }

    private static ExportFlags Ueformat() => ExportFlagDefaults.Gltf2() with { MeshFormat = "ueformat" };
}
