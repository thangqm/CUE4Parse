using CUE4Parse.Cli.Output;
using CUE4Parse.Compression;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.FileProvider.Vfs;
using CUE4Parse.MappingsProvider.Usmap;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using CUE4Parse.Utils;
using CUE4Parse_Conversion.Textures.BC;

namespace CUE4Parse.Cli.Services;

public static class ProviderFactory
{
    private static bool IsAuto(string? value)
        => string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase);

    public static DefaultFileProvider Create(ResolvedProfile profile)
    {
        if (!Directory.Exists(profile.PaksDir))
        {
            throw new CliException(
                ExitCode.Mount, "PAKS_DIR_NOT_FOUND",
                $"Paks directory not found: {profile.PaksDir}");
        }

        // "auto" resolves to the cache written by 'cue4 update'.
        var mappingsPath = IsAuto(profile.Mappings) ? CachePaths.MappingsFile : profile.Mappings;

        if (mappingsPath is not null && !File.Exists(mappingsPath))
        {
            var hint = IsAuto(profile.Mappings)
                ? " Run 'cue4 update' to download them."
                : string.Empty;

            throw new CliException(
                ExitCode.Mappings, "MAPPINGS_NOT_FOUND",
                $"Mappings file not found: {mappingsPath}.{hint}");
        }

        // Order matters: compression backends must be ready before any archive is read.
        InitializeCompression();

        var provider = new DefaultFileProvider(
            profile.PaksDir,
            SearchOption.AllDirectories,
            new VersionContainer(profile.Game),
            StringComparer.OrdinalIgnoreCase);

        try
        {
            // Mappings are assigned before Initialize, matching every call site in
            // CUE4Parse.Tests.
            if (mappingsPath is not null)
                provider.MappingsContainer = new FileUsmapTypeMappingsProvider(mappingsPath);

            provider.Initialize();

            var keys = new Dictionary<FGuid, FAesKey>();

            if (IsAuto(profile.MainAesKey))
            {
                var cached = CachePaths.ReadCachedKeys()
                    ?? throw new CliException(
                        ExitCode.AesKey, "AES_KEYS_NOT_CACHED",
                        $"No cached AES keys at {CachePaths.KeysFile}. Run 'cue4 update' first.");

                if (!string.IsNullOrEmpty(cached.MainKey)) keys[new FGuid()] = ParseAesKey(cached.MainKey);

                // Dynamic keys are not optional: Fortnite ships encrypted chunks under
                // non-zero GUIDs that the main key will not mount.
                foreach (var (guidText, keyText) in cached.DynamicKeys)
                    keys[ParseAesGuid(guidText)] = ParseAesKey(keyText);
            }
            else if (profile.MainAesKey is { } main)
            {
                keys[new FGuid()] = ParseAesKey(main);
            }

            // Profile-level dynamic keys are applied last so they win over the cache.
            foreach (var (guidText, keyText) in profile.DynamicKeys)
                keys[ParseAesGuid(guidText)] = ParseAesKey(keyText);

            // SubmitKeys mounts only the readers matching a submitted GUID...
            if (keys.Count > 0) provider.SubmitKeys(keys);

            // ...Mount() mounts everything else. Without it an unencrypted game
            // mounts nothing at all and every verb silently returns empty.
            provider.Mount();

            provider.PostMount();
        }
        catch (CliException)
        {
            provider.Dispose();
            throw;
        }
        catch (Exception ex)
        {
            provider.Dispose();

            // Defer to the shared classifier so a failure it already recognises keeps
            // its own exit code rather than being flattened into MOUNT_FAILED.
            var error = ErrorClassifier.Classify(ex);
            throw error.Code == "INTERNAL"
                ? new CliException(ExitCode.Mount, "MOUNT_FAILED", $"Failed to mount archives: {ex.Message}")
                : new CliException(error.ExitCode, error.Code, error.Message, error.Details);
        }

        return provider;
    }

    /// <summary>
    /// Both helpers fall back to downloading their native library, and both resolve a
    /// bare filename against the <em>working directory</em> — so the default leaves a
    /// multi-megabyte dll wherever the tool happened to be run, and re-downloads it in
    /// the next directory. Pointing them at the cache makes that a once-per-machine cost.
    /// Oodle keeps its null path when the statically linked native carries it, because
    /// an explicit path bypasses that check.
    /// </summary>
    private static void InitializeCompression()
    {
        Directory.CreateDirectory(CachePaths.Root);

        OodleHelper.Initialize(CUE4ParseNatives.IsFeatureAvailable("Oodle\0"u8)
            ? null
            : Path.Combine(CachePaths.Root, OodleHelper.OodleFileName));

        ZlibHelper.Initialize(Path.Combine(CachePaths.Root, ZlibHelper.DllName));

        // Detex is the third of these and was simply missing. On Windows, TextureDecoder
        // routes BC7, BC6H and the ETC family through it; uninitialized, every one of
        // those throws "Detex decompression failed: not initialized" and the texture is
        // lost. BC7 is the default compression for UE5 base colour maps, so this was not
        // an edge case. Windows-only, mirroring TextureDecoder's own IsWindows branch:
        // every other platform uses the managed AssetRipper decoders instead, and the
        // embedded payload is a Windows DLL.
        if (OperatingSystem.IsWindows())
        {
            var detexPath = Path.Combine(CachePaths.Root, DetexHelper.DLL_NAME);
            if (DetexHelper.LoadDll(detexPath))
            {
                DetexHelper.Initialize(detexPath);
            }
            else
            {
                Serilog.Log.Warning("Detex could not be loaded; BC7, BC6H and ETC textures will fail to decode");
            }
        }
    }

    public static FAesKey ParseAesKey(string value)
    {
        // FAesKey's own string ctor handles the hex decode and the length check.
        var hex = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;
        if (hex.TryParseAesKey(out var key)) return key;

        throw new CliException(
            ExitCode.AesKey, "BAD_AES_KEY",
            $"AES key must be 64 hex characters (32 bytes), optionally prefixed with '0x'. Got {hex.Length} characters.");
    }

    /// <summary>
    /// FGuid exposes no TryParse: the only string entry point is a ctor requiring
    /// exactly 32 hex characters, which throws on the dashed form fortnite-api returns.
    /// </summary>
    public static FGuid ParseAesGuid(string value)
    {
        var hex = value.Trim().Trim('{', '}').Replace("-", string.Empty);

        if (hex.Length != 32 || !hex.All(Uri.IsHexDigit))
        {
            throw new CliException(
                ExitCode.AesKey, "BAD_AES_GUID",
                $"AES GUID must be 32 hex characters, with or without dashes. Got '{value}'.");
        }

        try
        {
            return new FGuid(hex);
        }
        catch (Exception ex)
        {
            throw new CliException(ExitCode.AesKey, "BAD_AES_GUID", $"Malformed AES GUID '{value}': {ex.Message}");
        }
    }

    /// <summary>
    /// Fails with the outstanding GUIDs when archives remain encrypted.
    /// Call this only after a lookup has already failed, so that "asset not found"
    /// stays distinguishable from "the archive holding it is still encrypted".
    /// </summary>
    public static void ThrowIfKeysMissing(AbstractVfsFileProvider provider)
    {
        if (provider.RequiredKeys.Count == 0) return;

        var missing = provider.RequiredKeys.Select(g => g.ToString()).ToArray();
        throw new CliException(
            ExitCode.AesKey, "AES_KEY_MISSING",
            $"{missing.Length} archive(s) remain encrypted. Supply the missing AES key(s).",
            new { missingGuids = missing });
    }
}
