using System.Text;
using CUE4Parse.Cli.Output;
using CUE4Parse.Cli.Services;
using CUE4Parse.UE4.Versions;
using Newtonsoft.Json.Linq;

namespace CUE4Parse.Cli.Tests;

/// <summary>
/// Points the CLI at the real fixture archives. The csproj links
/// CUE4Parse.Tests/Fixtures into this project's own output, so the paths are
/// configuration-independent.
/// </summary>
public static class FixtureSupport
{
    private const string AesKeyText = "CUE4ParseFixtureAESKey0123456789";

    public static string Dir(params string[] parts)
        => Path.GetFullPath(Path.Combine([AppContext.BaseDirectory, "Fixtures", "UE5_8", .. parts]));

    private static string Mappings() => Dir("Mappings", "CUE4ParseFixtures-Oodle.usmap");

    private static string RequireDir(params string[] parts)
    {
        var path = Dir(parts);
        Assert.True(Directory.Exists(path), $"Missing fixture directory: {path}");
        return path;
    }

    /// <summary>
    /// Unencrypted cooked packages. No AES key needed; mappings are.
    /// <para>
    /// This is the <c>IoStore</c> set, not <c>LegacyPak</c>. The legacy paks carry five
    /// assets in total (<c>DT_AllProperties</c>, <c>Empty.umap</c>, <c>DA_AllProperties</c>,
    /// <c>T_BC6H</c>, <c>T_Streaming</c>) and contain no mesh, no material and no BC3
    /// texture, so every export test would have had nothing to export. The IoStore
    /// container holds all 111 fixture assets.
    /// </para>
    /// <para>
    /// The directory is <c>IoStore/Tagged</c>, not a compression-specific leaf under it.
    /// IoStore packages cannot be deserialized without <c>global.utoc</c> ("Found IoStore
    /// Package but global data is missing"), and that file sits beside the compression
    /// folders rather than inside them. <c>DefaultFileProvider</c> is constructed with
    /// <c>SearchOption.AllDirectories</c>, so pointing at <c>Tagged</c> sweeps in both the
    /// global container and the per-compression ones — no explicit <c>RegisterVfs</c> is
    /// needed, contrary to what pointing at a leaf directory would suggest.
    /// </para>
    /// </summary>
    public static ResolvedProfile Profile() => new(
        RequireDir("IoStore", "Tagged"),
        EGame.GAME_UE5_8,
        Mappings(),
        MainAesKey: null,
        new Dictionary<string, string>());

    /// <summary>
    /// The legacy pak container: five assets, all unencrypted, each stored as a real
    /// <c>.uasset</c> + <c>.uexp</c> pair on disk.
    /// <para>
    /// Two things only this container can express. It is the only one with separate
    /// payload files, so the <c>unpack</c> sibling-payload test needs it — an IoStore
    /// package keeps its payload inside the <c>.ucas</c> and writes no <c>.uexp</c>.
    /// And it holds no encrypted archive, so a missing asset is unambiguously "not
    /// found"; under <see cref="Profile"/> the unmounted <c>OodleEncrypted</c> sibling
    /// makes the CLI correctly answer "may be behind an unsubmitted key" (exit 5)
    /// instead, which is the whole reason that exit code is distinct from 7.
    /// </para>
    /// </summary>
    public static ResolvedProfile LegacyProfile() => new(
        RequireDir("LegacyPak", "Unversioned", "Oodle"),
        EGame.GAME_UE5_8,
        Mappings(),
        MainAesKey: null,
        new Dictionary<string, string>());

    /// <summary>Same content behind the fixture AES key, for the SubmitKeys path.</summary>
    public static ResolvedProfile EncryptedProfile() => new(
        RequireDir("LegacyPak", "Unversioned", "OodleEncrypted"),
        EGame.GAME_UE5_8,
        Mappings(),
        "0x" + Convert.ToHexString(Encoding.ASCII.GetBytes(AesKeyText)),
        new Dictionary<string, string>());

    /// <summary>A context writing to a capture buffer, which the caller reads back.</summary>
    public static (CommandContext Context, StringWriter Out) Context(ResolvedProfile? profile = null)
    {
        var sw = new StringWriter();
        return (new CommandContext(profile ?? Profile(), new JsonOutput(sw)), sw);
    }

    /// <summary>Parses captured NDJSON, one object per non-empty line.</summary>
    public static JObject[] Ndjson(StringWriter sw) =>
    [
        .. sw.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(JObject.Parse),
    ];
}
