using CUE4Parse.Cli.Output;
using CUE4Parse.Cli.Services;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using Newtonsoft.Json.Linq;

namespace CUE4Parse.Cli.Commands;

public sealed record DumpOptions(
    string[] Paths,
    MatchCriteria Criteria,
    string? ExportName,
    string? ClassName,
    FileInfo? Output,
    bool Indent,
    bool Force);

public static class DumpCommand
{
    public static int Execute(CommandContext context, DumpOptions options)
    {
        using var provider = ProviderFactory.Create(context.Profile);

        var targets = TargetResolver.Resolve(provider, options.Paths, options.Criteria, options.Force);

        using var writer = options.Output is null
            ? null
            : new StreamWriter(options.Output.FullName, append: false);

        var output = writer is null ? context.Output : new JsonOutput(writer);
        var single = targets.Count == 1 && options.Paths.Length == 1;

        foreach (var path in targets)
        {
            try
            {
                var payload = Load(provider, path, options);

                if (single)
                {
                    output.WriteResult(payload, options.Indent);
                    return (int)ExitCode.Success;
                }

                output.WriteLine(new { path, status = "ok", data = payload });
            }
            catch (Exception ex)
            {
                // A single explicit target rethrows so the error boundary can classify it
                // (notably MappingException -> exit 6). In NDJSON mode, per-asset failures
                // are data, not a process failure.
                if (single) throw;
                output.WriteLine(new { path, status = "error", message = ex.Message });
            }
        }

        return (int)ExitCode.Success;
    }

    /// <summary>
    /// JToken.FromObject runs the same [JsonConverter(typeof(UObjectConverter))]
    /// declared on UObject, without serializing the export graph to a string and
    /// parsing it back only to serialize it a second time.
    /// </summary>
    private static JToken Load(AbstractFileProvider provider, string path, DumpOptions options)
    {
        if (options.ExportName is { } name)
            return JToken.FromObject(provider.LoadPackageObject(path, name));

        IEnumerable<UObject> exports = provider.LoadPackage(path).GetExports();

        if (options.ClassName is { } className)
        {
            exports = exports.Where(e =>
                string.Equals(e.ExportType, className, StringComparison.OrdinalIgnoreCase));
        }

        return JToken.FromObject(exports.ToArray());
    }
}
