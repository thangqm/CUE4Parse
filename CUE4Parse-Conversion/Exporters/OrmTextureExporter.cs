using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse_Conversion.Textures;
using CUE4Parse_Conversion.Writers.Gltf;
using SkiaSharp;

namespace CUE4Parse_Conversion.Exporters;

/// <summary>
/// Repacks a UE SpecularMasks/SRM texture into glTF's metallicRoughness channel order.
/// <para>
/// UE packs G = metallic, B = roughness; glTF fixes G = roughness, B = metallic. The two
/// are exact transpositions and glTF 2.0 has no channel swizzle, so the only honest
/// option is a second image.
/// </para>
/// <para>
/// It lives behind its own <c>ObjectPath</c> rather than as a flag on
/// <see cref="TextureExporter"/> on purpose. <c>ExportSession.Add</c> deduplicates on
/// <c>ObjectPath</c> first-wins, so a flag would make the output depend on which
/// material happened to enqueue the shared texture first under
/// <c>Parallel.ForEachAsync</c>. Here every enqueue is equivalent, so the file is
/// written exactly once no matter how many materials point at it, and the result does
/// not depend on ordering.
/// </para>
/// </summary>
public sealed class OrmTextureExporter(UTexture texture)
    : ExporterBase(texture, nameSuffix: GltfMaterialBinder.OrmSuffix)
{
    protected override IReadOnlyList<ExportFile> BuildExportFiles(CancellationToken ct = default)
    {
        // Only the first mip: this image exists so a glTF material can point at it, and
        // a glTF material can point at exactly one image.
        var index = texture.GetFirstMipIndex();
        var decoded = texture.DecodeMip(index, Session.Options.TexturePlatform)
            ?? throw new Exception($"Failed to decode texture mip {index}");

        ct.ThrowIfCancellationRequested();

        using var source = decoded.ToSkBitmap();
        using var repacked = new SKBitmap(new SKImageInfo(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));

        // Skia's per-pixel accessors cross into native code twice per pixel — a 4K
        // specular map is 33M interop calls, more than the block decode and the PNG
        // encode on either side of this cost together. Let Skia do the format
        // normalisation in one native pass instead, then shuffle bytes in place.
        using var pixmap = source.PeekPixels();
        if (pixmap is null || !pixmap.ReadPixels(repacked.Info, repacked.GetPixels(), repacked.RowBytes))
        {
            throw new Exception("Failed to read decoded texture pixels for ORM repacking");
        }

        unsafe
        {
            var pixels = new Span<byte>((void*)repacked.GetPixels(), repacked.ByteCount);
            for (var i = 0; i < pixels.Length; i += 4)
            {
                // R = occlusion, left neutral: the source red channel is specular, not
                // AO, and writing it would be an invention. G = roughness (source blue),
                // B = metallic (source green).
                (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) =
                    ((byte)255, pixels[i + 2], pixels[i + 1], (byte)255);
            }
        }

        using var data = repacked.Encode(SKEncodedImageFormat.Png, Session.Options.TextureQuality);
        return [new ExportFile("png", data.ToArray())];
    }
}
