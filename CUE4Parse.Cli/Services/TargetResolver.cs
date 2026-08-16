using CUE4Parse.Cli.Output;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.FileProvider.Vfs;

namespace CUE4Parse.Cli.Services;

public static class TargetResolver
{
    /// <summary>
    /// Resolves to <see cref="GameFile"/>s rather than paths.
    /// <c>FileProviderDictionary.TryGetValue</c> sorts the mounted-index bag on every
    /// lookup, and each consumer would otherwise pay for it twice per target: once for
    /// the <c>IsUePackage</c> check and again for the load.
    /// </summary>
    public static IReadOnlyList<GameFile> Resolve(
        AbstractVfsFileProvider provider, string[] explicitPaths, MatchCriteria criteria, bool force)
    {
        if (explicitPaths.Length == 0)
        {
            var matches = AssetMatcher.Filter(provider.Files, criteria);
            AssetMatcher.EnforceLimit(matches.TotalMatched, criteria, force);
            return matches.Files;
        }

        // ContainsKey beats materializing a HashSet of every mounted path.
        var missing = explicitPaths.Where(p => !provider.Files.ContainsKey(p)).ToArray();
        if (missing.Length > 0)
        {
            // Distinguish "no such asset" from "the archive holding it is still
            // encrypted" — the reason exit 5 and exit 7 are separate codes.
            ProviderFactory.ThrowIfKeysMissing(provider);

            throw new CliException(
                ExitCode.NotFound, "ASSET_NOT_FOUND",
                $"{missing.Length} asset path(s) not found in the mounted archives.",
                new { missing });
        }

        AssetMatcher.EnforceLimit(explicitPaths.Length, criteria, force);
        return Array.ConvertAll(explicitPaths, p => provider.Files[p]);
    }
}
