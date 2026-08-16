using CUE4Parse.Cli.Output;
using CUE4Parse.Cli.Services;
using CUE4Parse.Compression;
using CUE4Parse.Utils;

namespace CUE4Parse.Cli.Commands;

public static class InfoCommand
{
    public static int Execute(CommandContext context)
    {
        // Built before the provider: this is the one part of the report that does not
        // depend on a mountable paks directory, and it is exactly what a user needs
        // when the mount is what failed.
        ProviderFactory.InitializeCompression();
        var native = new
        {
            library = CUE4ParseNatives.IsInitialized,
            acl = CUE4ParseNatives.IsFeatureAvailable("ACL\0"u8),
            oodle = OodleState(),
        };

        try
        {
            using var provider = ProviderFactory.Create(context.Profile);

            context.Output.WriteResult(new
            {
                game = context.Profile.Game.ToString(),
                paksDir = context.Profile.PaksDir,
                mappings = context.Profile.Mappings,
                mountedVfs = provider.MountedVfs.Count,
                unloadedVfs = provider.UnloadedVfs.Count,
                fileCount = provider.Files.Count,
                missingAesGuids = provider.RequiredKeys.Select(g => g.ToString()).ToArray(),
                native,

                // JsonOutput drops nulls, so these two keys simply do not appear without
                // --verbose. Aggregate counts cannot settle an archive-count discrepancy
                // against another tool; naming the archives makes the comparison concrete.
                mountedArchives = context.Verbose
                    ? provider.MountedVfs.Select(vfs => vfs.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray()
                    : null,
                unloadedArchives = context.Verbose
                    ? provider.UnloadedVfs.Select(vfs => vfs.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray()
                    : null,
            }, indent: true);

            return (int)ExitCode.Success;
        }
        catch (Exception ex)
        {
            // The one place the "errors print only an error object" rule is relaxed:
            // for `info`, the diagnosis *is* the product.
            var error = ErrorClassifier.Classify(ex);
            context.Output.WriteResult(new
            {
                native,
                error = new { code = error.Code, message = error.Message },
            }, indent: true);

            return (int)error.ExitCode;
        }
    }

    /// <summary>
    /// Three states, not a boolean. OodleHelper falls back to downloading oo2core when
    /// the native library was built without Oodle, so "no native Oodle" does not mean
    /// "no Oodle" — and the downloaded path needs network access, which is what breaks
    /// on an offline CI runner. A boolean carrying three meanings is exactly what makes
    /// an agent parsing this output reason wrongly with no way to tell.
    /// </summary>
    private static string OodleState()
    {
        if (CUE4ParseNatives.IsFeatureAvailable("Oodle\0"u8)) return "native";
        return OodleHelper.Instance is not null ? "downloaded" : "unavailable";
    }
}
