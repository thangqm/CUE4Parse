using System.Text;
using System.Text.RegularExpressions;
using CUE4Parse.Cli.Output;

namespace CUE4Parse.Cli.Services;

public sealed record MatchCriteria(
    string[]? Globs = null,
    string? Regex = null,
    string? Extension = null,
    int? Limit = null);

/// <summary>Truncated results plus the count before truncation.</summary>
public readonly record struct MatchResult(IReadOnlyList<string> Paths, int TotalMatched);

public static class AssetMatcher
{
    public const int DefaultLimit = 1000;

    private static readonly Dictionary<string, Regex> GlobCache = new(StringComparer.Ordinal);

    public static bool IsMatch(string path, MatchCriteria criteria)
    {
        if (criteria.Globs is { Length: > 0 } globs && !globs.Any(g => GlobRegex(g).IsMatch(path)))
            return false;

        if (!string.IsNullOrEmpty(criteria.Regex) &&
            !Regex.IsMatch(path, criteria.Regex, RegexOptions.IgnoreCase))
            return false;

        if (!string.IsNullOrEmpty(criteria.Extension))
        {
            var wanted = criteria.Extension.TrimStart('.');
            var actual = Path.GetExtension(path.AsSpan()).TrimStart('.');
            if (!actual.Equals(wanted, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Filters and deduplicates, returning both the (optionally truncated) list and
    /// the true match count. <c>FileProviderDictionary.Keys</c> concatenates every
    /// mounted index and can repeat a path, hence the distinct pass.
    /// </summary>
    public static MatchResult Filter(IEnumerable<string> paths, MatchCriteria criteria)
    {
        var matched = paths
            .Where(p => IsMatch(p, criteria))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return criteria.Limit is { } limit && limit < matched.Count
            ? new MatchResult(matched.Take(limit).ToList(), matched.Count)
            : new MatchResult(matched, matched.Count);
    }

    /// <summary>
    /// Guards bulk operations against the true match count, never the truncated one —
    /// otherwise <c>--limit 100</c> against 50,000 matches would slip past the guard,
    /// which is the silent truncation this exists to prevent.
    /// An explicit <c>--limit</c> is consent: the caller has stated how many they want.
    /// </summary>
    public static void EnforceLimit(
        MatchResult result, MatchCriteria criteria, bool force, int limit = DefaultLimit)
    {
        if (force || criteria.Limit is not null || result.TotalMatched <= limit) return;

        throw new CliException(
            ExitCode.Usage,
            "LIMIT_EXCEEDED",
            $"Matched {result.TotalMatched} assets, which exceeds the safety limit of {limit}. " +
            "Narrow the pattern, pass --limit to take the first N, or pass --force.",
            new { matchCount = result.TotalMatched, limit });
    }

    private static Regex GlobRegex(string glob)
    {
        lock (GlobCache)
        {
            if (GlobCache.TryGetValue(glob, out var cached)) return cached;

            var sb = new StringBuilder("^");
            for (var i = 0; i < glob.Length; i++)
            {
                var c = glob[i];
                switch (c)
                {
                    case '*' when i + 1 < glob.Length && glob[i + 1] == '*':
                        sb.Append(".*");
                        i++;
                        // Swallow a following separator so "**/x" also matches "x".
                        if (i + 1 < glob.Length && (glob[i + 1] == '/' || glob[i + 1] == '\\')) i++;
                        break;
                    case '*':
                        sb.Append("[^/\\\\]*");
                        break;
                    case '?':
                        sb.Append("[^/\\\\]");
                        break;
                    case '/':
                    case '\\':
                        sb.Append("[/\\\\]");
                        break;
                    default:
                        sb.Append(Regex.Escape(c.ToString()));
                        break;
                }
            }
            sb.Append('$');

            var regex = new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.Compiled);
            GlobCache[glob] = regex;
            return regex;
        }
    }
}
