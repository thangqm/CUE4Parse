using CUE4Parse.Cli.Output;
using CUE4Parse.Cli.Services;
using CUE4Parse.FileProvider;
using CUE4Parse.FileProvider.Objects;
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

        // A single explicit target emits the payload bare, and lets failures reach the
        // error boundary (notably MappingException -> exit 6). In NDJSON mode, per-asset
        // failures are data, not a process failure.
        if (targets.Count == 1 && options.Paths.Length == 1)
        {
            output.WriteResult(Load(provider, targets[0], options), options.Indent);
            return (int)ExitCode.Success;
        }

        foreach (var file in targets)
        {
            try
            {
                output.WriteLine(new { path = file.Path, status = "ok", data = Load(provider, file, options) });
            }
            catch (Exception ex)
            {
                var error = ErrorClassifier.Classify(ex);
                output.WriteLine(new { path = file.Path, status = "error", code = error.Code, message = error.Message });
            }
        }

        return (int)ExitCode.Success;
    }

    /// <summary>
    /// JToken.FromObject runs the same [JsonConverter(typeof(UObjectConverter))]
    /// declared on UObject, without serializing the export graph to a string and
    /// parsing it back only to serialize it a second time.
    /// </summary>
    private static JToken Load(AbstractFileProvider provider, GameFile file, DumpOptions options)
    {
        if (options.ExportName is { } name)
            return JToken.FromObject(provider.LoadPackageObject(file.Path, name));

        IEnumerable<UObject> exports = provider.LoadPackage(file).GetExports();

        if (options.ClassName is { } className)
        {
            exports = exports.Where(e =>
                string.Equals(e.ExportType, className, StringComparison.OrdinalIgnoreCase));
        }

        return JToken.FromObject(exports.ToArray());
    }
}
