using CUE4Parse.Cli.Services;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;

namespace CUE4Parse.Cli.Tests;

/// <summary>Loads a single export out of the fixture archives, mounting once per process.</summary>
public static class FixtureAssets
{
    private static readonly Lazy<IFileProvider> Shared = new(() => ProviderFactory.Create(FixtureSupport.Profile()));

    /// <summary>
    /// Texture assets in the UE5_8 fixture set, discovered with
    /// <c>cue4 list --glob "**/Textures/**"</c> against <c>IoStore/Tagged</c>.
    /// <para>
    /// Deliberately excludes <c>T_Virtual</c> and <c>T_UDIM</c> (their payload lives in a
    /// .ubulk the namer has no opinion about) and <c>T_IES</c> / <c>T_CurveAtlas</c>
    /// (not UTexture2D). Everything here is something the exporter will actually decode.
    /// </para>
    /// </summary>
    public static readonly string[] TextureNames =
    [
        "T_BC1", "T_BC3", "T_BC4", "T_BC5", "T_BC6H", "T_BC7",
        "T_BGRA8", "T_G8", "T_G16", "T_R16F", "T_FloatRGBA", "T_Mips",
    ];

    public static T LoadExport<T>(string path, string exportName) where T : UObject
    {
        var package = Shared.Value.LoadPackage(path);
        return (T)package.GetExports().First(export =>
            export.Name.Equals(exportName, StringComparison.OrdinalIgnoreCase));
    }
}
