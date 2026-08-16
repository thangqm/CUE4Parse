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
    // These are the single source of each flag's vocabulary — Program.cs drives
    // AcceptOnlyFromAmong off the same keys, so the parser and the mapper cannot
    // disagree about which values are legal.
    public static readonly Dictionary<string, EMeshFormat> MeshFormats = new()
    {
        ["actorx"] = EMeshFormat.ActorX,
        ["gltf2"] = EMeshFormat.Gltf2,
        ["ueformat"] = EMeshFormat.UEFormat,
        ["usd"] = EMeshFormat.USD,
    };

    public static readonly Dictionary<string, ENaniteMeshFormat> NaniteFormats = new()
    {
        ["nanite-only"] = ENaniteMeshFormat.NaniteOnly,
        ["no-nanite"] = ENaniteMeshFormat.NoNanite,
        ["nanite-first"] = ENaniteMeshFormat.NaniteFirst,
        ["nanite-last"] = ENaniteMeshFormat.NaniteLast,
    };

    public static readonly Dictionary<string, EMeshQuality> MeshQualities = new()
    {
        ["highest"] = EMeshQuality.Highest,
        ["lowest"] = EMeshQuality.Lowest,
        ["all"] = EMeshQuality.All,
    };

    public static readonly Dictionary<string, ETextureFormat> TextureFormats = new()
    {
        ["png"] = ETextureFormat.Png,
        ["jpeg"] = ETextureFormat.Jpeg,
        ["tga"] = ETextureFormat.Tga,
        ["webp"] = ETextureFormat.Webp,
    };

    public static readonly Dictionary<string, ETexturePlatform> TexturePlatforms = new()
    {
        ["desktop"] = ETexturePlatform.DesktopMobile,
        ["xbox-ps4"] = ETexturePlatform.XboxAndPlaystation4,
        ["switch"] = ETexturePlatform.NintendoSwitch,
        ["ps5"] = ETexturePlatform.Playstation5,
    };

    public static readonly Dictionary<string, EMaterialDepth> MaterialDepths = new()
    {
        ["top-layer-only"] = EMaterialDepth.TopLayerOnly,
        ["all-layers-no-ref"] = EMaterialDepth.AllLayersNoRef,
        ["all-layers"] = EMaterialDepth.AllLayers,
    };

    public static readonly Dictionary<string, ESocketFormat> SocketFormats = new()
    {
        ["socket"] = ESocketFormat.Socket,
        ["bone"] = ESocketFormat.Bone,
        ["none"] = ESocketFormat.None,
    };

    public static ExportOptions Map(ExportFlags flags)
    {
        var meshFormat = Pick(flags.MeshFormat, "--mesh-format", MeshFormats);
        var textureFormat = Pick(flags.TextureFormat, "--texture-format", TextureFormats);

        // glTF 2.0 core carries only PNG and JPEG. TGA and WebP have no core support
        // (WebP needs EXT_texture_webp, TGA nothing at all). The user typed the flag,
        // so quietly handing them something else would be dishonest.
        if (meshFormat == EMeshFormat.Gltf2 && textureFormat is ETextureFormat.Tga or ETextureFormat.Webp)
        {
            throw new CliException(
                ExitCode.Usage, "BAD_OPTION",
                $"--texture-format {flags.TextureFormat} cannot be used with --mesh-format gltf2. " +
                "glTF 2.0 accepts only png and jpeg.");
        }

        return new ExportOptions(
            meshFormat: meshFormat,
            naniteMeshFormat: Pick(flags.Nanite, "--nanite", NaniteFormats),
            meshQuality: Pick(flags.MeshQuality, "--mesh-quality", MeshQualities),
            textureFormat: textureFormat,
            texturePlatform: Pick(flags.TexturePlatform, "--texture-platform", TexturePlatforms),
            textureQuality: flags.TextureQuality,
            exportAllTextureMips: flags.AllMips,
            materialDepth: Pick(flags.MaterialDepth, "--material-depth", MaterialDepths),
            exportMaterials: !flags.NoMaterials,
            socketFormat: Pick(flags.SocketFormat, "--socket-format", SocketFormats));
    }

    private static T Pick<T>(string value, string flagName, Dictionary<string, T> map)
        => map.TryGetValue(value.ToLowerInvariant(), out var result)
            ? result
            : throw new CliException(
                ExitCode.Usage, "BAD_OPTION",
                $"Invalid value '{value}' for {flagName}. Valid values: {string.Join(", ", map.Keys)}.");
}
