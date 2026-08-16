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
    bool AllMips);

public sealed record ExportCommandOptions(
    string[] Paths,
    MatchCriteria Criteria,
    DirectoryInfo Output,
    ExportFlags Flags,
    int Parallel,
    bool Force);

public static class ExportCommand
{
    public static async Task<int> ExecuteAsync(
        CommandContext context, ExportCommandOptions options, CancellationToken ct)
    {
        using var provider = ProviderFactory.Create(context.Profile);

        var targets = TargetResolver.Resolve(provider, options.Paths, options.Criteria, options.Force);
        options.Output.Create();

        // MaxDegreeOfParallelism must be positive; 0 or a negative value throws deep
        // inside the session where the error is unrecognizable.
        var session = new ExportSession { MaxDegreeOfParallelism = Math.Max(1, options.Parallel) };
        var skipped = new List<object>();
        var loadFailures = 0;

        foreach (var path in targets)
        {
            // LoadPackage throws for non-package entries (.ini, .locres, .bin), which a
            // broad glob will happily match. Skipping is not a failure.
            if (!provider.Files[path].IsUePackage)
            {
                skipped.Add(new { path, status = "skipped", reason = "not a UE package" });
                continue;
            }

            try
            {
                foreach (var export in provider.LoadPackage(path).GetExports())
                {
                    try
                    {
                        session.Add(export);
                    }
                    catch (NotSupportedException)
                    {
                        // ExportSession.Add throws for object types with no exporter.
                        skipped.Add(new
                        {
                            path, status = "skipped", export = export.Name,
                            reason = "no exporter for this type",
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                loadFailures++;
                context.Output.WriteLine(new { path, status = "error", message = ex.Message });
            }
        }

        var results = await session.RunAsync(
            options.Output.FullName, ExportOptionsMapper.Map(options.Flags), progress: null, ct);

        foreach (var result in results)
        {
            context.Output.WriteLine(new
            {
                path = result.ObjectPath,
                status = result.Success ? "ok" : "error",
                files = result.DiskFilePaths,
                message = result.Error?.Message,
            });
        }

        foreach (var entry in skipped) context.Output.WriteLine(entry);

        // Exit 8 means something failed. "No exporter for a DataTable" is not a
        // failure — counting it as one would make almost every real glob return 8.
        var failed = results.Count(r => !r.Success) + loadFailures;
        return failed > 0 ? (int)ExitCode.PartialExport : (int)ExitCode.Success;
    }
}
