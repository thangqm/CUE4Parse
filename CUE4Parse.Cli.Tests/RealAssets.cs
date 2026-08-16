using CUE4Parse.Cli.Services;
using CUE4Parse.UE4.Versions;

namespace CUE4Parse.Cli.Tests;

/// <summary>
/// An opt-in profile pointing at a real game install, for the assertions the
/// redistributable fixture set structurally cannot make.
/// <para>
/// The UE5_8 fixtures contain no mesh with a material assigned — <c>SM_Fixture</c>'s two
/// slots both have a null <c>MaterialInterface</c> — so no fixture export can ever
/// produce a glTF <c>images</c> array, a resolvable texture URI or an <c>_ORM</c>
/// sibling. Those are precisely the goal conditions of this work, and they are checked
/// here instead.
/// </para>
/// <para>
/// Gated on environment variables so nothing game-specific enters the repository and CI
/// stays green without them, exactly like <c>GLTF_VALIDATOR</c> and <c>BLENDER</c>:
/// </para>
/// <list type="bullet">
/// <item><c>CUE4_REAL_PAKS</c> — the game's paks directory (required).</item>
/// <item><c>CUE4_REAL_GAME</c> — an EGame name or shorthand (default <c>4.27</c>).</item>
/// <item><c>CUE4_REAL_MAPPINGS</c> — a .usmap path (optional).</item>
/// <item><c>CUE4_REAL_AES</c> — the main AES key (optional).</item>
/// <item><c>CUE4_REAL_MESH</c> — a mesh asset path to export (required).</item>
/// </list>
/// </summary>
public static class RealAssets
{
    public static string? PaksDir => Env("CUE4_REAL_PAKS");
    public static string? MeshPath => Env("CUE4_REAL_MESH");

    public static bool Available => PaksDir is not null && MeshPath is not null && Directory.Exists(PaksDir);

    /// <summary>Skips the calling test when no real game install is configured.</summary>
    public static void SkipIfUnavailable()
    {
        if (!Available)
        {
            Assert.Skip(
                "Set CUE4_REAL_PAKS and CUE4_REAL_MESH (optionally CUE4_REAL_GAME, " +
                "CUE4_REAL_MAPPINGS, CUE4_REAL_AES) to run the real-asset checks. " +
                "The redistributable fixtures have no mesh with a material assigned.");
        }
    }

    public static ResolvedProfile Profile() => new(
        PaksDir!,
        GameParser.Parse(Env("CUE4_REAL_GAME") ?? "4.27"),
        Env("CUE4_REAL_MAPPINGS"),
        Env("CUE4_REAL_AES"),
        new Dictionary<string, string>());

    private static string? Env(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
