using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse_Conversion.Textures;

namespace CUE4Parse_Conversion.Exporters;

public sealed class TextureExporter(UTexture texture) : ExporterBase(texture)
{
    protected override IReadOnlyList<ExportFile> BuildExportFiles(CancellationToken ct = default)
    {
        Log.Debug("Decoding texture for platform {Platform} as {Format}", Session.Options.TexturePlatform, Session.Options.TextureFormat);

        var files = new List<ExportFile>();
        var all = Session.Options.ExportAllTextureMips;
        if (all)
        {
            if (texture.PlatformData is { FirstMipToSerialize: >= 0, VTData: { } vt } && vt.IsInitialized())
            {
                for (var i = TextureDecoder.GetMinLevel(vt); i < vt.NumMips; i++)
                {
                    AddMip(i);
                }
            }
            else for (var i = 0; i < texture.PlatformData.Mips.Length; i++)
            {
                if (texture.PlatformData.Mips[i].EnsureValidBulkData(texture.MipDataProvider, i))
                {
                    AddMip(i);
                }
                else
                {
                    Log.Warning("Texture mip {Index} has no valid bulk data, skipping", i);
                }
            }
        }
        else
        {
            AddMip(texture.GetFirstMipIndex());
        }

        return files;

        void AddMip(int index)
        {
            ct.ThrowIfCancellationRequested();

            var platform = Session.Options.TexturePlatform;
            CTexture?[]? decoded = texture switch
            {
                UTexture2DArray array => array.DecodeTextureArray(index, platform),
                UTextureCube => texture.DecodeMip(index, platform)?.ToPanorama() is { } panorama ? [panorama] : null,
                _ => texture.DecodeMip(index, platform) is { } single ? [single] : null
            };

            if (decoded is not { Length: > 0 })
            {
                if (!all)
                {
                    throw new Exception($"Failed to decode texture mip {index}");
                }

                Log.Warning("Failed to decode texture mip {Index}, skipping", index);
                return;
            }

            for (var i = 0; i < decoded.Length; i++)
            {
                if (decoded[i] is not { } slice) continue;

                if (Session.Options.FlipNormalY && texture.IsNormalMap)
                {
                    // glTF has no "flip the green channel" concept, so the inversion has
                    // to happen in the written bytes. Off by default: cue4 writes what the
                    // game shipped. If the result lights inside-out in Blender the user
                    // turns this on — guessing for them is worse than asking.
                    InvertGreenChannel(slice);
                }

                var data = slice.Encode(Session.Options, out var ext);

                // The suffix comes from the shared namer, both branches, so the glTF
                // binder and this writer cannot drift apart. Only the mip differs: this
                // loop walks every mip, while a caller predicting a name knows the first.
                var suffix = TextureFileNamer.Suffix(texture, Session.Options, i, index);
                files.Add(new ExportFile(ext, data, suffix));
            }
        }
    }

    /// <summary>
    /// Inverts G in place for a decoded 8-bit texture.
    /// <para>
    /// Restricted to 3- and 4-byte strides on purpose: <c>255 - x</c> only means
    /// "invert" for an 8-bit channel, and silently mangling the low byte of a 16-bit
    /// or float format would be worse than doing nothing.
    /// </para>
    /// </summary>
    private static void InvertGreenChannel(CTexture texture)
    {
        if (!PixelFormatUtils.PixelFormats.TryGetValue(texture.PixelFormat, out var info) || info.NumComponents < 2)
        {
            return;
        }

        var pixels = texture.Width * texture.Height;
        if (pixels <= 0) return;

        var stride = texture.Data.Length / pixels;
        if (stride is not (3 or 4)) return;

        for (var i = 1; i < texture.Data.Length; i += stride)
        {
            texture.Data[i] = (byte)(255 - texture.Data[i]);
        }
    }
}
