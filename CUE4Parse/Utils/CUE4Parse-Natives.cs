using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace CUE4Parse.Utils;

public static unsafe class CUE4ParseNatives
{
    public const string LibraryName = "CUE4Parse-Natives";

    public static nint LibraryHandle { get; }
    public static bool IsInitialized => LibraryHandle != nint.Zero;

    // Returns byte, not bool: the native side returns a 1-byte C++ bool, while a
    // `bool` in an unmanaged function pointer is marshalled as a 4-byte Win32 BOOL.
    // Reading 4 bytes picks up whatever garbage sits above AL, so a native `false`
    // reads back as `true` and every feature is reported available.
    private static readonly delegate* unmanaged<byte*, byte> _isFeatureAvailableFunctionPointer;

    static CUE4ParseNatives()
    {
        if (!NativeLibrary.TryLoad(
            LibraryName,
            Assembly.GetExecutingAssembly(),
            DllImportSearchPath.AssemblyDirectory,
            out var handle))
        {
            LibraryHandle = nint.Zero;
            return;
        }

        if (!NativeLibrary.TryGetExport(handle, "IsFeatureAvailable", out var isFeatureAvailableAddress))
        {
            NativeLibrary.Free(handle);
            LibraryHandle = nint.Zero;
            return;
        }

        _isFeatureAvailableFunctionPointer = (delegate* unmanaged<byte*, byte>)isFeatureAvailableAddress;
        LibraryHandle = handle;
    }

    public static bool IsFeatureAvailable(ReadOnlySpan<byte> utf8FeatureName)
    {
        if (!IsInitialized || utf8FeatureName.Length < 1 || utf8FeatureName[^1] != 0)
            return false;

        fixed (byte* featureNamePtr = utf8FeatureName)
        {
            return _isFeatureAvailableFunctionPointer(featureNamePtr) != 0;
        }
    }

    public static bool IsFeatureAvailable(ReadOnlySpan<char> featureName)
    {
        if (!IsInitialized || featureName.IsEmpty || featureName.Length > 128)
            return false;

        var utf8Chars = Encoding.UTF8.GetByteCount(featureName) + 1;
        Span<byte> utf8FeatureName = stackalloc byte[utf8Chars];
        if (!Encoding.UTF8.TryGetBytes(featureName, utf8FeatureName, out _))
            return false;

        return IsFeatureAvailable(utf8FeatureName);
    }
}
