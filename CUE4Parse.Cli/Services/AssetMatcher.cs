using System.Text;
using System.Text.RegularExpressions;
using CUE4Parse.Cli.Output;
using CUE4Parse.FileProvider.Objects;

namespace CUE4Parse.Cli.Services;

public sealed record MatchCriteria(
    string[]? Globs = null,
    string? Regex = null,
    string? Extension = null,
    int? Limit = null);

/// <summary>Truncated results plus the count before truncation.</summary>
public readonly record struct MatchResult(IReadOnlyList<string> Paths, int TotalMatched);

/// <summary>The same, resolved to <see cref="GameFile"/>s so consumers need no second lookup.</summary>
public readonly record struct FileMatchResult(IReadOnlyList<GameFile> Files, int TotalMatched);

/// <summary>
/// <see cref="MatchCriteria"/> with its patterns resolved once. Matching runs per
/// asset over indices with hundreds of thousands of entries, so the regex compile,
/// the cache probe and the extension trim must not happen inside the loop.
/// </summary>
public sealed class CompiledCriteria
{
    private readonly Regex[] _globs;
    private readonly Regex? _regex;
    private readonly string? _extension;

    private CompiledCriteria(Regex[] globs, Regex? regex, string? extension)
    {
        _globs = globs;
        _regex = regex;
        _extension = extension;
    }

    public static CompiledCriteria From(MatchCriteria criteria) => new(
        criteria.Globs is { Length: > 0 } globs ? Array.ConvertAll(globs, AssetMatcher.GlobRegex) : [],
        string.IsNullOrEmpty(criteria.Regex)
            ? null
            : new Regex(criteria.Regex, RegexOptions.IgnoreCase | RegexOptions.Compiled),
        string.IsNullOrEmpty(criteria.Extension) ? null : criteria.Extension.TrimStart('.'));

    public bool IsMatch(string path)
    {
        if (_globs.Length > 0 && !MatchesAnyGlob(path)) return false;
        if (_regex is not null && !_regex.IsMatch(path)) return false;

        return _extension is null ||
               Path.GetExtension(path.AsSpan()).TrimStart('.')
                   .Equals(_extension, StringComparison.OrdinalIgnoreCase);
    }

    private bool MatchesAnyGlob(string path)
    {
        foreach (var glob in _globs)
        {
            if (glob.IsMatch(path)) return true;
        }

        return false;
    }
}

public static class AssetMatcher
{
    public const int DefaultLimit = 1000;

    private static readonly Dictionary<string, Regex> GlobCache = new(StringComparer.Ordinal);

    /// <summary>Single-path convenience; compile the criteria once when matching many.</summary>
    public static bool IsMatch(string path, MatchCriteria criteria)
        => CompiledCriteria.From(criteria).IsMatch(path);

    /// <summary>
    /// Filters and deduplicates, returning both the (optionally truncated) list and
    /// the true match count. <c>FileProviderDictionary.Keys</c> concatenates every
    /// mounted index and can repeat a path, hence the distinct pass.
    /// </summary>
    public static MatchResult Filter(IEnumerable<string> paths, MatchCriteria criteria)
    {
        var (matched, total) = Collect(paths, static path => path, criteria);
        return new MatchResult(matched, total);
    }

    /// <summary>
    /// Overload over the mounted index itself. Enumerating yields the
    /// <see cref="GameFile"/> alongside its path, whereas re-reading
    /// <c>provider.Files[path]</c> re-sorts the mounted-index bag once per asset.
    /// </summary>
    public static FileMatchResult Filter(
        IEnumerable<KeyValuePair<string, GameFile>> files, MatchCriteria criteria)
    {
        var (matched, total) = Collect(files, static entry => entry.Key, criteria);
        return new FileMatchResult(matched.ConvertAll(static entry => entry.Value), total);
    }

    /// <summary>
    /// Guards bulk operations against the true match count, never the truncated one —
    /// otherwise <c>--limit 100</c> against 50,000 matches would slip past the guard,
    /// which is the silent truncation this exists to prevent.
    /// An explicit <c>--limit</c> is consent: the caller has stated how many they want.
    /// </summary>
    public static void EnforceLimit(int totalMatched, MatchCriteria criteria, bool force)
    {
        if (force || criteria.Limit is not null || totalMatched <= DefaultLimit) return;

        throw new CliException(
            ExitCode.Usage,
            "LIMIT_EXCEEDED",
            $"Matched {totalMatched} assets, which exceeds the safety limit of {DefaultLimit}. " +
            "Narrow the pattern, pass --limit to take the first N, or pass --force.",
            new { matchCount = totalMatched, limit = DefaultLimit });
    }

    private static (List<T> Matched, int Total) Collect<T>(
        IEnumerable<T> source, Func<T, string> pathOf, MatchCriteria criteria)
    {
        var compiled = CompiledCriteria.From(criteria);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matched = new List<T>();

        foreach (var item in source)
        {
            var path = pathOf(item);
            if (compiled.IsMatch(path) && seen.Add(path)) matched.Add(item);
        }

        return criteria.Limit is { } limit && limit < matched.Count
            ? (matched.GetRange(0, limit), matched.Count)
            : (matched, matched.Count);
    }

    internal static Regex GlobRegex(string glob)
    {
        lock (GlobCache)
        {
            if (GlobCache.TryGetValue(glob, out var cached)) return cached;

            var regex = new Regex(BuildGlobPattern(glob), RegexOptions.IgnoreCase | RegexOptions.Compiled);
            GlobCache[glob] = regex;
            return regex;
        }
    }

    private static string BuildGlobPattern(string glob)
    {
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

        return sb.Append('$').ToString();
    }
}
