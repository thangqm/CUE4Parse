using CUE4Parse.Cli.Commands;
using CUE4Parse.Utils;
using Newtonsoft.Json.Linq;

namespace CUE4Parse.Cli.Tests;

public class NativeCapabilityTests
{
    /// <summary>
    /// Asserts the *reporting* is truthful, not that ACL is present: the native library
    /// is built by a non-fatal CMake step, so a runner without CMake legitimately gets
    /// acl:false. What must never happen is acl:true while the feature is absent.
    /// <para>
    /// This is the whole of what can be checked. Decoding an actual ACL-compressed
    /// animation is not covered anywhere — the redistributable fixture set contains
    /// none, and one cannot be added. See the verification gaps in
    /// docs/cue4-output-contract.md.
    /// </para>
    /// </summary>
    [Fact]
    public void AclReportingMatchesTheNativeLibraryState()
    {
        var (context, sw) = FixtureSupport.Context();
        InfoCommand.Execute(context);

        var native = JObject.Parse(sw.ToString())["native"]!;
        var acl = native["acl"]!.Value<bool>();
        var library = native["library"]!.Value<bool>();

        Assert.True(library || !acl, "acl cannot be true when no native library is loaded.");
        Assert.Equal(CUE4ParseNatives.IsFeatureAvailable("ACL\0"u8), acl);
    }

    /// <summary>
    /// Oodle is three-valued because <c>OodleHelper</c> downloads <c>oo2core</c> when the
    /// native library was built without it. "native" specifically claims the compiled-in
    /// path, so it may not be reported while the library itself is absent.
    /// </summary>
    [Fact]
    public void OodleReportingDistinguishesCompiledInFromDownloaded()
    {
        var (context, sw) = FixtureSupport.Context();
        InfoCommand.Execute(context);

        var native = JObject.Parse(sw.ToString())["native"]!;
        var oodle = native["oodle"]!.Value<string>();

        Assert.Contains(oodle, new[] { "native", "downloaded", "unavailable" });

        if (oodle == "native")
        {
            Assert.True(native["library"]!.Value<bool>(),
                "oodle:native claims a compiled-in feature, so the library must be loaded.");
            Assert.True(CUE4ParseNatives.IsFeatureAvailable("Oodle\0"u8));
        }
        else
        {
            Assert.False(CUE4ParseNatives.IsFeatureAvailable("Oodle\0"u8));
        }
    }
}
