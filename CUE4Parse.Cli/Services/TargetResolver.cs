using CUE4Parse.Cli.Output;
using CUE4Parse.FileProvider.Vfs;

namespace CUE4Parse.Cli.Services;

public static class TargetResolver
{
    public static IReadOnlyList<string> Resolve(
        AbstractVfsFileProvider provider, string[] explicitPaths, MatchCriteria criteria, bool force)
    {
        if (explicitPaths.Length == 0)
        {
            var matches = AssetMatcher.Filter(provider.Files.Keys, criteria);
            AssetMatcher.EnforceLimit(matches, criteria, force);
            return matches.Paths;
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

        AssetMatcher.EnforceLimit(
            new MatchResult(explicitPaths, explicitPaths.Length), criteria, force);
        return explicitPaths;
    }
}
