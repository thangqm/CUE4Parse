using CUE4Parse.Cli.Output;
using CUE4Parse.Compression;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.FileProvider.Vfs;
using CUE4Parse.MappingsProvider.Usmap;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;

namespace CUE4Parse.Cli.Services;

public static class ProviderFactory
{
    public static DefaultFileProvider Create(ResolvedProfile profile)
    {
        if (!Directory.Exists(profile.PaksDir))
        {
            throw new CliException(
                ExitCode.Mount, "PAKS_DIR_NOT_FOUND",
                $"Paks directory not found: {profile.PaksDir}");
        }

        if (profile.Mappings is { } mappingsPath &&
            !string.Equals(mappingsPath, "auto", StringComparison.OrdinalIgnoreCase) &&
            !File.Exists(mappingsPath))
        {
            throw new CliException(
                ExitCode.Mappings, "MAPPINGS_NOT_FOUND",
                $"Mappings file not found: {mappingsPath}");
        }

        // Order matters: compression backends must be ready before any archive is read.
        OodleHelper.Initialize();
        ZlibHelper.Initialize();

        var provider = new DefaultFileProvider(
            profile.PaksDir,
            SearchOption.AllDirectories,
            new VersionContainer(profile.Game),
            StringComparer.OrdinalIgnoreCase);

        try
        {
            // Mappings are assigned before Initialize, matching every call site in
            // CUE4Parse.Tests. Task 11 replaces this with the "auto" resolution.
            if (profile.Mappings is { } path &&
                !string.Equals(path, "auto", StringComparison.OrdinalIgnoreCase))
            {
                provider.MappingsContainer = new FileUsmapTypeMappingsProvider(path);
            }

            provider.Initialize();

            var keys = new Dictionary<FGuid, FAesKey>();
            if (profile.MainAesKey is { } main) keys[new FGuid()] = ParseAesKey(main);
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
            throw new CliException(ExitCode.Mount, "MOUNT_FAILED", $"Failed to mount archives: {ex.Message}");
        }

        return provider;
    }

    public static FAesKey ParseAesKey(string value)
    {
        var hex = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;

        if (hex.Length != 64 || !hex.All(Uri.IsHexDigit))
        {
            throw new CliException(
                ExitCode.AesKey, "BAD_AES_KEY",
                $"AES key must be 64 hex characters (32 bytes), optionally prefixed with '0x'. Got {hex.Length} characters.");
        }

        try
        {
            return new FAesKey(Convert.FromHexString(hex));
        }
        catch (FormatException ex)
        {
            throw new CliException(ExitCode.AesKey, "BAD_AES_KEY", $"Malformed AES key: {ex.Message}");
        }
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
