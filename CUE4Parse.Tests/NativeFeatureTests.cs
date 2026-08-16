using CUE4Parse.Utils;

namespace CUE4Parse.Tests;

public class NativeFeatureTests
{
    /// <summary>
    /// The native <c>IsFeatureAvailable</c> returns a 1-byte C++ <c>bool</c>. Declaring the
    /// function pointer as returning a managed <c>bool</c> makes the CLR read a 4-byte Win32
    /// <c>BOOL</c> instead, so garbage above AL turns a native <c>false</c> into <c>true</c> and
    /// every feature is reported available. That made <c>OodleHelper</c> bind Oodle exports from
    /// a natives build that has none, failing with <c>EntryPointNotFoundException</c>.
    /// </summary>
    [Fact]
    public void IsFeatureAvailableReportsFalseForAFeatureThatCannotExist()
    {
        // Skipped rather than passed vacuously: with no natives library loaded the guard in
        // IsFeatureAvailable short-circuits and never exercises the marshalling at all.
        Assert.SkipUnless(CUE4ParseNatives.IsInitialized, "CUE4Parse-Natives is not loaded.");

        Assert.False(CUE4ParseNatives.IsFeatureAvailable("NotARealFeature\0"u8));
    }

    [Fact]
    public void IsFeatureAvailableReturnsFalseWhenTheNativeLibraryIsAbsent()
    {
        Assert.SkipWhen(CUE4ParseNatives.IsInitialized, "CUE4Parse-Natives is loaded.");

        Assert.False(CUE4ParseNatives.IsFeatureAvailable("ACL\0"u8));
    }
}
