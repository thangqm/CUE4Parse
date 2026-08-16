using CUE4Parse.Cli.Output;
using CUE4Parse.Cli.Services;

namespace CUE4Parse.Cli.Commands;

public sealed record UnpackOptions(
    string[] Paths,
    MatchCriteria Criteria,
    DirectoryInfo Output,
    bool Flat,
    bool Force);

public static class UnpackCommand
{
    public static int Execute(CommandContext context, UnpackOptions options)
    {
        using var provider = ProviderFactory.Create(context.Profile);

        var targets = TargetResolver.Resolve(provider, options.Paths, options.Criteria, options.Force);
        options.Output.Create();

        // Only --flat can collide, so only --flat fills this in.
        var written = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var createdDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var failed = 0;

        foreach (var file in targets)
        {
            var path = file.Path;

            try
            {
                // A .uasset alone is unusable: the exports live in the .uexp.
                // SavePackage returns every payload file for the package.
                var files = file.IsUePackage
                    ? provider.SavePackage(file)
                    : new Dictionary<string, byte[]> { [path] = provider.SaveAsset(file) };

                var outputs = new List<string>(files.Count);
                var bytes = 0L;

                foreach (var (filePath, data) in files)
                {
                    var destination = ResolveDestination(
                        options.Output.FullName, options.Flat, filePath, written);

                    // Assets arrive grouped by directory, so this walks the path chain
                    // once per directory rather than once per file.
                    var directory = Path.GetDirectoryName(destination)!;
                    if (createdDirectories.Add(directory)) Directory.CreateDirectory(directory);

                    File.WriteAllBytes(destination, data);
                    outputs.Add(destination);
                    bytes += data.Length;
                }

                context.Output.WriteLine(new { path, status = "ok", bytes, output = outputs });
            }
            catch (CliException)
            {
                throw;  // OUTPUT_COLLISION is a usage error, not a per-item failure.
            }
            catch (Exception ex)
            {
                failed++;
                var error = ErrorClassifier.Classify(ex);
                context.Output.WriteLine(new { path, status = "error", code = error.Code, message = error.Message });
            }
        }

        return failed > 0 ? (int)ExitCode.PartialExport : (int)ExitCode.Success;
    }

    /// <summary>
    /// Maps a VFS path to its output path, failing loudly on a --flat name clash.
    /// Silently overwriting would hand the caller fewer files than they asked for
    /// with no way to notice.
    /// </summary>
    /// <remarks>
    /// Only two <em>different</em> source paths collide. A package's own payload files
    /// differ by extension (<c>.uasset</c> / <c>.uexp</c> / <c>.ubulk</c>) and never
    /// clash, and re-visiting the same source path is a no-op — a glob that matches
    /// both a package and its own <c>.uexp</c> must not be reported as a collision.
    /// Without <c>--flat</c> the destination is an injective function of the source
    /// path, so nothing needs recording.
    /// Public so the clash can be tested without an archive that contains one.
    /// </remarks>
    public static string ResolveDestination(
        string outputRoot, bool flat, string filePath, Dictionary<string, string> written)
    {
        if (!flat) return Path.Combine(outputRoot, filePath.Replace('/', Path.DirectorySeparatorChar));

        var destination = Path.Combine(outputRoot, Path.GetFileName(filePath));

        if (!written.TryAdd(destination, filePath) && written[destination] != filePath)
        {
            throw new CliException(
                ExitCode.Usage, "OUTPUT_COLLISION",
                $"'{filePath}' and '{written[destination]}' both map to '{destination}'. " +
                "Drop --flat to preserve the directory structure.",
                new { destination, first = written[destination], second = filePath });
        }

        return destination;
    }
}
