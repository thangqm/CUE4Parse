using CUE4Parse.UE4.Assets.Exports.Material;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse_Conversion.Options;
using CUE4Parse_Conversion.Writers.Gltf;
using SharpGLTF.Materials;
using SharpGLTF.Memory;

namespace CUE4Parse.Cli.Tests;

/// <summary>
/// The binder is exercised through fabricated <see cref="CMaterialParams2"/> sets built
/// from <em>real</em> fixture textures.
/// <para>
/// This is not a convenience. The UE5_8 fixture set has exactly one material with one
/// texture parameter, named <c>FixtureTexture</c>, which matches no entry in
/// <c>CMaterialParams2</c>'s classification tables and none of its four regex fallbacks —
/// so a real-asset test could only ever assert "no channels were bound". There is no
/// normal map, no SpecularMasks source, no emissive and no masked material anywhere in
/// the set, and none can be added (the fixtures are redistributable and cannot be
/// regenerated here). Populating <c>Textures</c>/<c>Colors</c>/<c>BlendMode</c> directly
/// keeps the URIs genuine — they are computed from real packages by the real
/// <c>ExporterBase.Resolve</c> — while covering every channel the binder can emit.
/// </para>
/// </summary>
public class GltfMaterialBinderTests
{
    private const string MeshDirectory = "CUE4ParseFixtures/Content/Fixtures/Meshes";

    private static UMaterialInterface FixtureMaterial() =>
        FixtureAssets.LoadExport<UMaterialInterface>(
            "CUE4ParseFixtures/Content/Fixtures/Materials/M_Fixture.uasset", "M_Fixture");

    private static UTexture Texture(string name) =>
        FixtureAssets.LoadExport<UTexture>(
            $"CUE4ParseFixtures/Content/Fixtures/Textures/{name}.uasset", name);

    private static MaterialBuilder BindSynthetic(
        Action<CMaterialParams2> configure,
        UMaterialInterface? material = null,
        string meshSaveDirectory = MeshDirectory)
    {
        var options = new ExportOptions(meshFormat: EMeshFormat.Gltf2);
        var parameters = new CMaterialParams2();
        configure(parameters);
        return GltfMaterialBinder.Bind(
            material ?? FixtureMaterial(), parameters, "Primary", options, meshSaveDirectory);
    }

    [Fact]
    public void BoundMaterialKeepsTheSlotNameAndUsesTheMetallicRoughnessShader()
    {
        var material = BindSynthetic(_ => { });

        Assert.Equal("Primary", material.Name);
        Assert.Equal("PBRMetallicRoughness", material.ShaderStyle);
    }

    /// <summary>
    /// The one end-to-end case the fixture set can express: a real material, resolved
    /// through the real <c>GetParams</c>, produces a base-colour URI pointing at the real
    /// texture it references.
    /// <para>
    /// <c>M_Fixture</c>'s texture parameter reaches the diffuse slot via
    /// <c>CMaterialParams2</c>'s <c>PM_Diffuse</c> fallback rather than the top-level
    /// <c>Diffuse</c> table, which is why <c>HasTopDiffuse</c> is false while the channel
    /// is nonetheless bound. That fallback is the path most real materials take, so
    /// covering it is worth more than covering the exact-name path would be.
    /// </para>
    /// <para>
    /// The other channels stay null because the fixture set has no normal map, no
    /// SpecularMasks source and no emissive — see the class summary. The synthetic tests
    /// below cover those.
    /// </para>
    /// </summary>
    [Fact]
    public void ARealFixtureMaterialBindsItsTextureAsAnExternalBaseColorUri()
    {
        var material = FixtureMaterial();
        var options = new ExportOptions(meshFormat: EMeshFormat.Gltf2);
        var parameters = new CMaterialParams2();
        material.GetParams(parameters, options.MaterialDepth);

        var bound = GltfMaterialBinder.Bind(material, parameters, "Primary", options, MeshDirectory);

        var image = bound.GetChannel(KnownChannel.BaseColor)?.Texture?.PrimaryImage;
        Assert.NotNull(image);
        Assert.Equal("../Textures/T_BC3.png", image!.AlternateWriteFileName);

        Assert.Null(bound.GetChannel(KnownChannel.Normal)?.Texture?.PrimaryImage);
        Assert.Null(bound.GetChannel(KnownChannel.MetallicRoughness)?.Texture?.PrimaryImage);
    }

    [Fact]
    public void BaseColorPointsAtAnExternalRelativeUriNotAnEmbeddedImage()
    {
        var material = BindSynthetic(p => p.Textures[CMaterialParams2.Diffuse[0][0]] = Texture("T_BC3"));

        var image = material.GetChannel(KnownChannel.BaseColor)?.Texture?.PrimaryImage;
        Assert.NotNull(image);
        Assert.StartsWith("../", image!.AlternateWriteFileName);
        Assert.EndsWith("/T_BC3.png", image.AlternateWriteFileName);
    }

    [Fact]
    public void NormalMapsAreBoundToTheNormalChannel()
    {
        var material = BindSynthetic(p => p.Textures[CMaterialParams2.Normals[0][0]] = Texture("T_BC5"));

        var image = material.GetChannel(KnownChannel.Normal)?.Texture?.PrimaryImage;
        Assert.NotNull(image);
        Assert.EndsWith("/T_BC5.png", image!.AlternateWriteFileName);
    }

    [Fact]
    public void MetallicRoughnessPointsAtTheRepackedOrmSibling()
    {
        var material = BindSynthetic(p => p.Textures[CMaterialParams2.SpecularMasks[0][0]] = Texture("T_BC3"));

        var image = material.GetChannel(KnownChannel.MetallicRoughness)?.Texture?.PrimaryImage;
        Assert.NotNull(image);
        Assert.EndsWith("_ORM.png", image!.AlternateWriteFileName);
        Assert.EndsWith("/T_BC3_ORM.png", image.AlternateWriteFileName);
    }

    [Fact]
    public void OcclusionIsNeverWrittenBecauseTheSpecularRedChannelIsNotAmbientOcclusion()
    {
        var material = BindSynthetic(p => p.Textures[CMaterialParams2.SpecularMasks[0][0]] = Texture("T_BC3"));

        Assert.Null(material.GetChannel(KnownChannel.Occlusion)?.Texture?.PrimaryImage);
    }

    [Fact]
    public void EmissiveTexturesAndColorsAreBound()
    {
        var withTexture = BindSynthetic(p => p.Textures[CMaterialParams2.Emissive[0][0]] = Texture("T_BC1"));
        Assert.NotNull(withTexture.GetChannel(KnownChannel.Emissive)?.Texture?.PrimaryImage);

        var colorOnly = BindSynthetic(p =>
            p.Colors[CMaterialParams2.EmissiveColors[0][0]] = new FLinearColor(1f, 0.5f, 0.25f, 1f));
        var parameter = colorOnly.GetChannel(KnownChannel.Emissive)?.Parameters;
        Assert.NotNull(parameter);
    }

    /// <summary>A cube map has no UV mapping matching the mesh, so binding it would invent data.</summary>
    [Fact]
    public void CubeMapsAreSkippedRatherThanBoundToAChannel()
    {
        var cube = Texture("T_Cube");
        Assert.IsAssignableFrom<UTextureCube>(cube);

        var material = BindSynthetic(p => p.Textures[CMaterialParams2.Diffuse[0][0]] = cube);

        Assert.Null(material.GetChannel(KnownChannel.BaseColor)?.Texture?.PrimaryImage);
    }

    [Fact]
    public void UrisArePercentEncodedAndAlwaysUseForwardSlashes()
    {
        Assert.Equal("../Common/L21%20Body%20Color.png",
            GltfMaterialBinder.EncodeUri("../Common/L21 Body Color.png"));
        Assert.Equal("./T_Foo.png", GltfMaterialBinder.EncodeUri("./T_Foo.png"));
    }

    [Theory]
    [InlineData(EBlendMode.BLEND_Opaque, "OPAQUE")]
    [InlineData(EBlendMode.BLEND_Masked, "MASK")]
    [InlineData(EBlendMode.BLEND_Translucent, "BLEND")]
    [InlineData(EBlendMode.BLEND_Additive, "BLEND")]
    [InlineData(EBlendMode.BLEND_Modulate, "BLEND")]
    public void BlendModeMapsToTheGltfAlphaMode(EBlendMode blendMode, string expected)
        => Assert.Equal(expected, GltfMaterialBinder.ToAlphaMode(blendMode).ToString());

    [Fact]
    public void MaskedMaterialsCarryTheMaterialsOwnOpacityMaskClipValue()
    {
        var material = BindSynthetic(p => p.BlendMode = EBlendMode.BLEND_Masked);

        var alpha = material.GetChannel(KnownChannel.BaseColor);
        Assert.Equal(SharpGLTF.Materials.AlphaMode.MASK, material.AlphaMode);

        // M_Fixture does not override it, so UE's own default must survive the walk.
        Assert.Equal(0.333f, material.AlphaCutoff, 5);
    }

    [Fact]
    public void DistinctUrisProduceDistinctPlaceholderImagesSoSharpGltfDoesNotMergeThem()
    {
        // Compare MemoryImage content, not ImageBuilder.AreEqualByContent: the latter
        // returns false even for byte-identical images, while ToGltf2's deduplication
        // keys on the image bytes.
        var a = GltfMaterialBinder.CreateImage("../C/T_A.png");
        var b = GltfMaterialBinder.CreateImage("../C/T_B.png");

        Assert.False(MemoryImage.AreEqual(a.Content, b.Content));
        Assert.True(MemoryImage.AreEqual(a.Content, GltfMaterialBinder.CreateImage("../C/T_A.png").Content));
    }
}
