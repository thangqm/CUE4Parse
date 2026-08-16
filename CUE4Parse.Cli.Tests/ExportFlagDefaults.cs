using CUE4Parse.Cli.Commands;

namespace CUE4Parse.Cli.Tests;

/// <summary>The flag record every export test starts from, so a new flag added to
/// ExportFlags breaks one file instead of a dozen.</summary>
public static class ExportFlagDefaults
{
    public static ExportFlags Gltf2() => new(
        MeshFormat: "gltf2",
        TextureFormat: "png",
        TexturePlatform: "desktop",
        MeshQuality: "highest",
        Nanite: "no-nanite",
        SocketFormat: "bone",
        MaterialDepth: "top-layer-only",
        TextureQuality: 100,
        NoMaterials: false,
        AllMips: false);
}
