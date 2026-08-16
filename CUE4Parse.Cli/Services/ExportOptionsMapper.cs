using CUE4Parse.Cli.Commands;
using CUE4Parse.Cli.Output;
using CUE4Parse.UE4.Assets.Exports.Material;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse_Conversion.Options;

namespace CUE4Parse.Cli.Services;

public static class ExportOptionsMapper
{
    // Each map is spelled with its element type: a target-typed `new()` cannot be
    // used where the same call is what infers T.
    private static readonly Dictionary<string, EMeshFormat> MeshFormats = new()
    {
        ["actorx"] = EMeshFormat.ActorX,
        ["gltf2"] = EMeshFormat.Gltf2,
        ["ueformat"] = EMeshFormat.UEFormat,
        ["usd"] = EMeshFormat.USD,
    };

    private static readonly Dictionary<string, ENaniteMeshFormat> NaniteFormats = new()
    {
        ["nanite-only"] = ENaniteMeshFormat.NaniteOnly,
        ["no-nanite"] = ENaniteMeshFormat.NoNanite,
        ["nanite-first"] = ENaniteMeshFormat.NaniteFirst,
        ["nanite-last"] = ENaniteMeshFormat.NaniteLast,
    };

    private static readonly Dictionary<string, EMeshQuality> MeshQualities = new()
    {
        ["highest"] = EMeshQuality.Highest,
        ["lowest"] = EMeshQuality.Lowest,
        ["all"] = EMeshQuality.All,
    };

    private static readonly Dictionary<string, ETextureFormat> TextureFormats = new()
    {
        ["png"] = ETextureFormat.Png,
        ["jpeg"] = ETextureFormat.Jpeg,
        ["tga"] = ETextureFormat.Tga,
        ["webp"] = ETextureFormat.Webp,
    };

    private static readonly Dictionary<string, ETexturePlatform> TexturePlatforms = new()
    {
        ["desktop"] = ETexturePlatform.DesktopMobile,
        ["xbox-ps4"] = ETexturePlatform.XboxAndPlaystation4,
        ["switch"] = ETexturePlatform.NintendoSwitch,
        ["ps5"] = ETexturePlatform.Playstation5,
    };

    private static readonly Dictionary<string, EMaterialDepth> MaterialDepths = new()
    {
        ["top-layer-only"] = EMaterialDepth.TopLayerOnly,
        ["all-layers-no-ref"] = EMaterialDepth.AllLayersNoRef,
        ["all-layers"] = EMaterialDepth.AllLayers,
    };

    private static readonly Dictionary<string, ESocketFormat> SocketFormats = new()
    {
        ["socket"] = ESocketFormat.Socket,
        ["bone"] = ESocketFormat.Bone,
        ["none"] = ESocketFormat.None,
    };

    public static ExportOptions Map(ExportFlags flags) => new(
        meshFormat: Pick(flags.MeshFormat, "--mesh-format", MeshFormats),
        naniteMeshFormat: Pick(flags.Nanite, "--nanite", NaniteFormats),
        meshQuality: Pick(flags.MeshQuality, "--mesh-quality", MeshQualities),
        textureFormat: Pick(flags.TextureFormat, "--texture-format", TextureFormats),
        texturePlatform: Pick(flags.TexturePlatform, "--texture-platform", TexturePlatforms),
        textureQuality: flags.TextureQuality,
        exportAllTextureMips: flags.AllMips,
        materialDepth: Pick(flags.MaterialDepth, "--material-depth", MaterialDepths),
        exportMaterials: !flags.NoMaterials,
        socketFormat: Pick(flags.SocketFormat, "--socket-format", SocketFormats));

    private static T Pick<T>(string value, string flagName, Dictionary<string, T> map)
        => map.TryGetValue(value.ToLowerInvariant(), out var result)
            ? result
            : throw new CliException(
                ExitCode.Usage, "BAD_OPTION",
                $"Invalid value '{value}' for {flagName}. Valid values: {string.Join(", ", map.Keys)}.");
}
