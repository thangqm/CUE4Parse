using CUE4Parse.Cli.Commands;
using CUE4Parse.Cli.Output;
using CUE4Parse.Cli.Services;

namespace CUE4Parse.Cli.Tests;

public class ExportPathTests
{
    /// <summary>
    /// ExportSession.ResolveOutputPath used to end with Replace('/', '\\') unconditionally.
    /// On Linux '\' is a legal file name character, so the whole tree collapsed into a
    /// single file named "\tmp\out\..." in the working directory while the real
    /// directories were created empty next to it. Nothing threw.
    /// NOTE: this test is green on Windows both before and after the fix — the bug is
    /// Linux-only. Trust the CI run, not a local Windows pass.
    /// </summary>
    [Fact]
    public async Task ExportWritesFilesUsingThePlatformSeparatorAndTheyExistOnDisk()
    {
        var output = Directory.CreateTempSubdirectory();
        var (context, _) = FixtureSupport.Context();

        var code = await ExportCommand.ExecuteAsync(context, new ExportCommandOptions(
            Paths: ["CUE4ParseFixtures/Content/Fixtures/Meshes/SM_Fixture.uasset"],
            Criteria: new MatchCriteria(null, null, null, null),
            Output: output,
            Flags: ExportFlagDefaults.Gltf2(),
            Parallel: 1,
            Force: false), TestContext.Current.CancellationToken);

        Assert.Equal((int)ExitCode.Success, code);

        var written = Directory.GetFiles(output.FullName, "*", SearchOption.AllDirectories);
        Assert.NotEmpty(written);

        var foreign = Path.DirectorySeparatorChar == '/' ? '\\' : '/';
        Assert.All(written, path => Assert.DoesNotContain(foreign, Path.GetFileName(path)));

        // Nothing may have leaked into the process working directory.
        Assert.Empty(Directory.GetFiles(Directory.GetCurrentDirectory(), "*tmp*"));
    }
}
