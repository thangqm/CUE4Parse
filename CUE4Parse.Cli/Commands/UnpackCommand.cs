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

        var written = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var failed = 0;

        foreach (var path in targets)
        {
            try
            {
                // A .uasset alone is unusable: the exports live in the .uexp.
                // SavePackage returns every payload file for the package.
                var files = provider.Files[path].IsUePackage
                    ? provider.SavePackage(path)
                    : new Dictionary<string, byte[]> { [path] = provider.SaveAsset(path) };

                var outputs = new List<string>(files.Count);
                var bytes = 0L;

                foreach (var (filePath, data) in files)
                {
                    var destination = ResolveDestination(
                        options.Output.FullName, options.Flat, filePath, written);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
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
                context.Output.WriteLine(new { path, status = "error", message = ex.Message });
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
    /// Public so the clash can be tested without an archive that contains one.
    /// </remarks>
    public static string ResolveDestination(
        string outputRoot, bool flat, string filePath, Dictionary<string, string> written)
    {
        var destination = flat
            ? Path.Combine(outputRoot, Path.GetFileName(filePath))
            : Path.Combine(outputRoot, filePath.Replace('/', Path.DirectorySeparatorChar));

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
