using System;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse_Conversion.Options;

namespace CUE4Parse_Conversion.Exporters;

/// <summary>
/// Predicts the file name <see cref="TextureExporter"/> will write, without decoding
/// the texture.
/// <para>
/// Three rules stack: HDR sources ignore <see cref="ExportOptions.TextureFormat"/> and
/// come out as <c>.hdr</c>; <c>--all-mips</c> appends <c>_MIP{n}</c> — and in that mode
/// the unsuffixed file does not exist at all; <see cref="UTexture2DArray"/> appends
/// <c>_LAYER{i}</c>. A caller that hard-codes ".png" is wrong for three separate reasons.
/// </para>
/// <para>
/// The encoder remains authoritative for the bytes it writes; this type only predicts.
/// <c>NamerAgreesWithTheEncoderOnEveryFixtureTexture</c> is what keeps the prediction
/// honest — if it ever fails, fix this file, do not relax the test.
/// </para>
/// </summary>
public static class TextureFileNamer
{
    public static (string Extension, string? Suffix) Name(UTexture texture, ExportOptions options, int layer = 0)
        => (Extension(texture, options), Suffix(texture, options, layer));

    public static string Extension(UTexture texture, ExportOptions options)
    {
        if (options.ExportHdrTexturesAsHdr && IsHdrSource(texture))
            return "hdr";

        return options.TextureFormat switch
        {
            ETextureFormat.Png => "png",
            ETextureFormat.Jpeg => "jpg",
            ETextureFormat.Webp => "webp",
            ETextureFormat.Tga => "tga",
            _ => throw new NotSupportedException("Unsupported texture format: " + options.TextureFormat),
        };
    }

    public static string? Suffix(UTexture texture, ExportOptions options, int layer = 0)
    {
        var mip = options.ExportAllTextureMips ? $"_MIP{texture.GetFirstMipIndex()}" : null;
        return texture is UTexture2DArray ? $"{mip}_LAYER{layer}" : mip;
    }

    /// <summary>
    /// Whether the texture will still be an HDR pixel format <em>after decoding</em> —
    /// which is what <c>TextureEncoder</c> actually branches on.
    /// <para>
    /// <c>PixelFormatUtils.IsHDR</c> alone is not the answer. It is true for BC6H and the
    /// ASTC HDR formats, but <c>TextureDecoder</c> hard-codes every block-compressed HDR
    /// format down to <c>PF_R8G8B8A8</c>, so the encoder sees an LDR image and writes the
    /// requested raster format. Predicting <c>.hdr</c> there names a file that is never
    /// written — verified on the T_BC6H fixture, which decodes to R8G8B8A8 and comes out
    /// as .png. Only uncompressed float formats survive the decode, and those are exactly
    /// the ones with a 1x1 block.
    /// </para>
    /// </summary>
    // FTexturePlatformData.PixelFormat is the cooked format's *name*, e.g. "PF_BC6H".
    private static bool IsHdrSource(UTexture texture)
        => Enum.TryParse<EPixelFormat>(texture.PlatformData.PixelFormat, out var format)
           && PixelFormatUtils.IsHDR(format)
           && PixelFormatUtils.PixelFormats.TryGetValue(format, out var info)
           && info.BlockSizeX == 1;
}
