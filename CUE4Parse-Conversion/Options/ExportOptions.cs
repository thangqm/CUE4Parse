using CUE4Parse_Conversion.Writers.UEFormat.Enums;
using CUE4Parse.UE4.Assets.Exports.Material;
using CUE4Parse.UE4.Assets.Exports.Texture;

namespace CUE4Parse_Conversion.Options;

public class ExportOptions(
    EMeshFormat meshFormat = EMeshFormat.UEFormat,
    ENaniteMeshFormat naniteMeshFormat = ENaniteMeshFormat.NoNanite,
    EMeshQuality meshQuality = EMeshQuality.Highest,
    ETexturePlatform texturePlatform = ETexturePlatform.DesktopMobile,
    ETextureFormat textureFormat = ETextureFormat.Png,
    int textureQuality = 100,
    bool exportHdrTexturesAsHdr = true,
    bool exportAllTextureMips = false,
    EMaterialDepth materialDepth = EMaterialDepth.TopLayerOnly,
    bool exportMaterials = true,
    bool exportMorphTargets = true,
    ESocketFormat socketFormat = ESocketFormat.Bone,
    EFileCompressionFormat compressionFormat = EFileCompressionFormat.None)
{
    public readonly EMeshFormat MeshFormat = meshFormat;
    public readonly ENaniteMeshFormat NaniteMeshFormat = naniteMeshFormat;
    public readonly EMeshQuality MeshQuality = meshQuality;

    public readonly ETexturePlatform TexturePlatform = texturePlatform;
    public readonly ETextureFormat TextureFormat = meshFormat == EMeshFormat.USD ? ETextureFormat.Png : textureFormat; // USD pipeline requires PNG textures
    public readonly int TextureQuality = Math.Clamp(textureQuality, 1, 100);
    // glTF 2.0 core accepts only image/png and image/jpeg. Radiance HDR has no
    // extension at all, so an .hdr URI would produce a file no glTF reader can load.
    // Unlike --texture-format, the user did not ask for this, and the conflict only
    // surfaces per texture — so downgrade rather than fail the whole command.
    public readonly bool ExportHdrTexturesAsHdr = meshFormat != EMeshFormat.Gltf2 && exportHdrTexturesAsHdr;
    public readonly bool ExportAllTextureMips = exportAllTextureMips;

    public readonly EMaterialDepth MaterialDepth = materialDepth;
    public readonly bool ExportMaterials = exportMaterials; // not to be confused, when we export a mesh we will look (or not) for its materials and export them (or not)

    public readonly bool ExportMorphTargets = exportMorphTargets;
    public readonly ESocketFormat SocketFormat = socketFormat;

    public readonly EFileCompressionFormat CompressionFormat = meshFormat == EMeshFormat.UEFormat ? compressionFormat : EFileCompressionFormat.None;
}
