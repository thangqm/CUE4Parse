using System.Runtime.InteropServices;
using CUE4Parse.ACL;
using CUE4Parse.Cli.Commands;
using CUE4Parse.Cli.Services;
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
    /// Each value names a different source, and only "cached" ever needed the network.
    /// "native" claims the compiled-in path, so it cannot be reported without the library.
    /// </summary>
    [Fact]
    public void OodleReportsWhichSourceItLoadedFrom()
    {
        var (context, sw) = FixtureSupport.Context();
        InfoCommand.Execute(context);

        var native = JObject.Parse(sw.ToString())["native"]!;
        var oodle = native["oodle"]!.Value<string>();

        Assert.Contains(oodle, new[] { "native", "sidecar", "cached", "unavailable" });

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

        Assert.Equal(oodle == "sidecar", ProviderFactory.OodleFromSidecar);
    }

    /// <summary>
    /// <c>IsFeatureAvailable("ACL")</c> only reports the <c>WITH_ACL</c> compile flag.
    /// Driving a bogus buffer through ACL's own <c>compressed_tracks::is_valid</c> and
    /// requiring ACL's diagnostic back proves it is linked and callable. Not an
    /// end-to-end decode — no redistributable fixture is ACL-compressed.
    /// </summary>
    [Fact]
    public void AclNativeCodeIsCallableWhenTheFeatureIsReported()
    {
        if (!CUE4ParseNatives.IsFeatureAvailable("ACL\0"u8))
            Assert.Skip("Native library was built without ACL; nothing to exercise.");

        const int size = 256;
        var handle = ACLNative.nAllocate(size);
        Assert.NotEqual(IntPtr.Zero, handle);

        try
        {
            // All-zero is deliberately not a valid compressed_tracks buffer.
            Marshal.Copy(new byte[size], 0, handle, size);
            var error = new CompressedTracks(handle).IsValid(false);

            Assert.False(string.IsNullOrEmpty(error),
                "ACL accepted an all-zero buffer, so is_valid did not actually run.");
        }
        finally
        {
            ACLNative.nDeallocate(handle, size);
        }
    }
}
