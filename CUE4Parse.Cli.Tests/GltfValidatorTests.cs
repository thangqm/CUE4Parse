using System.Diagnostics;
using Newtonsoft.Json.Linq;

namespace CUE4Parse.Cli.Tests;

public readonly record struct GltfValidatorReport(int Errors, int Warnings, string Raw);

/// <summary>
/// Locates the pinned glTF-Validator binary. CI sets GLTF_VALIDATOR; developers
/// who have not installed it get skipped tests rather than false green ones.
/// </summary>
public static class GltfValidator
{
    public static bool TryLocate(out string path)
    {
        path = Environment.GetEnvironmentVariable("GLTF_VALIDATOR") ?? string.Empty;
        return path.Length > 0 && File.Exists(path);
    }

    public static GltfValidatorReport Validate(string glbPath)
    {
        Assert.True(TryLocate(out var exe), "GLTF_VALIDATOR is not set to an existing file.");

        // Run from the .glb's own directory and pass a bare file name. The validator
        // resolves a referenced resource's relative URI against the *working directory*,
        // not against the asset — verified: the same file reports 0 errors from one cwd
        // and 14 IO_ERROR "Resource not found" from a deeper one, with every referenced
        // file present on disk both times. Anchoring the cwd makes resolution mean what
        // a glTF consumer means by it.
        var directory = Path.GetDirectoryName(Path.GetFullPath(glbPath))!;

        using var process = Process.Start(new ProcessStartInfo(exe, ["-o", "-r", "-a", Path.GetFileName(glbPath)])
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;

        // -o puts the JSON report on stdout; -r validates referenced resources; -a sends
        // every issue message to stderr. stdout and stderr must NOT be concatenated: the
        // stderr half is a human-readable summary ("Errors: 0, Warnings: 0, ...") and
        // splicing it onto the JSON makes the report unparseable. stderr is read anyway,
        // on a separate task so a full pipe buffer cannot deadlock the child, and is kept
        // only for the diagnostic text of a failing assertion.
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();

        var report = stdout.GetAwaiter().GetResult();
        var issues = JObject.Parse(report)["issues"];
        var raw = report + Environment.NewLine + stderr.GetAwaiter().GetResult();
        return new GltfValidatorReport(
            issues?["numErrors"]?.Value<int>() ?? -1,
            issues?["numWarnings"]?.Value<int>() ?? -1,
            raw);
    }
}

public class GltfValidatorTests
{
    [Fact]
    public void ValidatorIsReachableAndReportsZeroErrorsOnAMinimalGlb()
    {
        if (!GltfValidator.TryLocate(out _))
        {
            Assert.Skip("GLTF_VALIDATOR not set; install the pinned validator to run this test.");
        }

        // The smallest thing we can assert without depending on the exporter is that the
        // tool runs and produces a parseable report for a real file written by SharpGLTF.
        var dir = Directory.CreateTempSubdirectory();
        var glb = Path.Combine(dir.FullName, "minimal.glb");
        File.WriteAllBytes(glb, MinimalGlb.Bytes());

        var report = GltfValidator.Validate(glb);

        Assert.Equal(0, report.Errors);
    }
}
