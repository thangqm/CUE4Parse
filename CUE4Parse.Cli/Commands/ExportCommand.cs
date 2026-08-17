using CUE4Parse.Cli.Output;
using CUE4Parse.Cli.Services;
using CUE4Parse_Conversion;

namespace CUE4Parse.Cli.Commands;

public sealed record ExportFlags(
    string MeshFormat,
    string TextureFormat,
    string TexturePlatform,
    string MeshQuality,
    string Nanite,
    string SocketFormat,
    string MaterialDepth,
    int TextureQuality,
    bool NoMaterials,
    bool AllMips,
    // Defaulted so ExportFlagDefaults and every existing call site keep compiling.
    string CompressionFormat = "none",
    bool NoMorphTargets = false,
    bool NoHdr = false,
    bool FlipNormalY = false);

public sealed record ExportCommandOptions(
    string[] Paths,
    MatchCriteria Criteria,
    DirectoryInfo Output,
    ExportFlags Flags,
    int Parallel,
    bool Force,
    FileInfo? Manifest = null);

public static class ExportCommand
{
    public static async Task<int> ExecuteAsync(
        CommandContext context, ExportCommandOptions options, CancellationToken ct)
    {
        // Map the flags before mounting: an invalid --mesh-format is a usage error and
        // must not cost the caller a full archive mount first.
        var exportOptions = ExportOptionsMapper.Map(options.Flags);

        using var provider = ProviderFactory.Create(context.Profile);

        var targets = TargetResolver.Resolve(provider, options.Paths, options.Criteria, options.Force);
        options.Output.Create();

        // MaxDegreeOfParallelism must be positive; 0 or a negative value throws deep
        // inside the session where the error is unrecognizable.
        var session = new ExportSession { MaxDegreeOfParallelism = Math.Max(1, options.Parallel) };
        var output = context.Output;
        var loadFailures = 0;

        foreach (var file in targets)
        {
            var path = file.Path;

            // LoadPackage throws for non-package entries (.ini, .locres, .bin), which a
            // broad glob will happily match. Skipping is not a failure.
            if (!file.IsUePackage)
            {
                output.WriteLine(new { path, status = "skipped", reason = "not a UE package" });
                continue;
            }

            try
            {
                foreach (var export in provider.LoadPackage(file).GetExports())
                {
                    // The common case on a broad glob: data tables, blueprints and
                    // settings objects have no exporter and that is not a failure.
                    if (session.TryAdd(export)) continue;

                    output.WriteLine(new
                    {
                        path, status = "skipped", export = export.Name,
                        reason = "no exporter for this type",
                    });
                }
            }
            catch (Exception ex)
            {
                loadFailures++;
                var error = ErrorClassifier.Classify(ex);
                output.WriteLine(new { path, status = "error", code = error.Code, message = error.Message });
            }
        }

        var results = await session.RunAsync(options.Output.FullName, exportOptions, progress: null, ct);

        var failed = loadFailures;
        foreach (var result in results)
        {
            if (!result.Success) failed++;

            output.WriteLine(new
            {
                path = result.ObjectPath,
                status = result.Success ? "ok" : "error",
                files = result.DiskFilePaths,
                message = result.Error?.Message,
            });
        }

        if (options.Manifest is { } manifestFile)
        {
            var version = typeof(ExportCommand).Assembly.GetName().Version?.ToString() ?? "0.0.0";
            ExportManifest.Write(
                ExportManifest.Build(results, options.Output.FullName, exportOptions, $"cue4 {version}"),
                manifestFile);
        }

        // Exit 8 means something failed. "No exporter for a DataTable" is not a
        // failure — counting it as one would make almost every real glob return 8.
        return failed > 0 ? (int)ExitCode.PartialExport : (int)ExitCode.Success;
    }
}
