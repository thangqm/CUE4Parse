using System;
using System.Linq;
using System.Numerics;
using System.Text;
using CUE4Parse.UE4.Assets.Exports.Material;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse_Conversion.Exporters;
using CUE4Parse_Conversion.Options;
using SharpGLTF.Materials;
using SharpGLTF.Memory;
using SkiaSharp;

namespace CUE4Parse_Conversion.Writers.Gltf;

/// <summary>
/// Turns a UE material into a glTF <see cref="MaterialBuilder"/> whose images are
/// external, relative URIs.
/// <para>
/// Pure by design: it takes no <c>ExportSession</c>, touches no disk, and never waits
/// for a texture to be written. It classifies textures out of the same
/// <see cref="CMaterialParams2"/> the caller resolved at <c>options.MaterialDepth</c> —
/// the depth <c>MaterialExporter</c> uses — so the set it references and the set the
/// session writes are the same set by construction, not by a cross-check that could rot.
/// </para>
/// <para>
/// Anything that cannot be classified is left blank. A blank channel is a material a
/// human can fix in Blender; an invented channel is data that is simply wrong.
/// </para>
/// </summary>
public static class GltfMaterialBinder
{
    /// <summary>Name suffix of the repacked ORM sibling written by <c>OrmTextureExporter</c>.</summary>
    public const string OrmSuffix = "_ORM";

    private static readonly string[] DiffuseNames = [.. CMaterialParams2.Diffuse[0], CMaterialParams2.FallbackDiffuse];
    private static readonly string[] NormalNames = [.. CMaterialParams2.Normals[0], CMaterialParams2.FallbackNormals];
    private static readonly string[] SpecularNames = [.. CMaterialParams2.SpecularMasks[0], CMaterialParams2.FallbackSpecularMasks];
    private static readonly string[] EmissiveNames = [.. CMaterialParams2.Emissive[0], CMaterialParams2.FallbackEmissive];

    /// <param name="parameters">
    /// Resolved by the caller, not here, so tests can fabricate parameter sets the fixture
    /// assets cannot provide. The caller must have resolved them at
    /// <c>options.MaterialDepth</c> — that is what keeps the referenced textures and the
    /// written textures the same set.
    /// </param>
    public static MaterialBuilder Bind(
        UMaterialInterface material, CMaterialParams2 parameters,
        string slotName, ExportOptions options, string meshSaveDirectory)
    {
        var builder = new MaterialBuilder(slotName).WithMetallicRoughnessShader();

        BindBaseColor(builder, parameters, options, meshSaveDirectory);
        BindMetallicRoughness(builder, parameters, options, meshSaveDirectory);
        BindNormal(builder, parameters, options, meshSaveDirectory);
        BindEmissive(builder, parameters, options, meshSaveDirectory);
        BindAlphaAndSides(builder, material, parameters);

        // occlusionTexture is deliberately never written: the R channel of a UE
        // SpecularMasks/SRM pack is specular, not ambient occlusion.

        return builder;
    }

    private static void BindBaseColor(MaterialBuilder builder, CMaterialParams2 parameters, ExportOptions options, string from)
    {
        Vector4? tint = parameters.TryGetLinearColor(out var color, CMaterialParams2.DiffuseColors[0])
            ? new Vector4(color.R, color.G, color.B, color.A)
            : null;

        if (TryImage(parameters, DiffuseNames, options, from, null, out var image))
        {
            builder.WithBaseColor(image, tint);
        }
        else if (tint.HasValue)
        {
            builder.WithBaseColor(tint.Value);
        }
    }

    private static void BindMetallicRoughness(MaterialBuilder builder, CMaterialParams2 parameters, ExportOptions options, string from)
    {
        // glTF fixes G = roughness, B = metallic; UE's SRM pack is G = metallic,
        // B = roughness. glTF 2.0 has no channel swizzle, so the URI points at the
        // repacked sibling OrmTextureExporter writes, always as PNG.
        if (TryImage(parameters, SpecularNames, options, from, OrmSuffix, out var image, forcePng: true))
        {
            builder.WithMetallicRoughness(image, null, null);
        }
    }

    private static void BindNormal(MaterialBuilder builder, CMaterialParams2 parameters, ExportOptions options, string from)
    {
        if (TryImage(parameters, NormalNames, options, from, null, out var image))
        {
            builder.WithNormal(image, 1f);
        }
    }

    private static void BindEmissive(MaterialBuilder builder, CMaterialParams2 parameters, ExportOptions options, string from)
    {
        Vector3? factor = parameters.TryGetLinearColor(out var color, CMaterialParams2.EmissiveColors[0])
            ? new Vector3(color.R, color.G, color.B)
            : null;

        if (TryImage(parameters, EmissiveNames, options, from, null, out var image))
        {
            builder.WithEmissive(image, factor, 1f);
        }
        else if (factor.HasValue)
        {
            builder.WithEmissive(factor.Value, 1f);
        }
    }

    private static void BindAlphaAndSides(MaterialBuilder builder, UMaterialInterface material, CMaterialParams2 parameters)
    {
        var mode = ToAlphaMode(parameters.BlendMode);
        var root = RootMaterial(material);

        builder.WithAlpha(mode, mode == SharpGLTF.Materials.AlphaMode.MASK ? OpacityMaskClipValue(material, root) : 0.5f);
        builder.WithDoubleSide(root?.TwoSided == true);
    }

    public static SharpGLTF.Materials.AlphaMode ToAlphaMode(EBlendMode blendMode) => blendMode switch
    {
        EBlendMode.BLEND_Opaque => SharpGLTF.Materials.AlphaMode.OPAQUE,
        EBlendMode.BLEND_Masked => SharpGLTF.Materials.AlphaMode.MASK,
        _ => SharpGLTF.Materials.AlphaMode.BLEND,
    };

    /// <summary>
    /// The nearest explicit override wins; otherwise the root UMaterial's value;
    /// otherwise UE's own default of 0.333.
    /// </summary>
    private static float OpacityMaskClipValue(UMaterialInterface material, UMaterial? root)
    {
        var current = material;
        for (var depth = 0; current is not null && depth < 16; depth++)
        {
            if (current is UMaterialInstance { BasePropertyOverrides.OpacityMaskClipValue: > 0f } instance)
                return instance.BasePropertyOverrides!.OpacityMaskClipValue;

            if (current is UMaterial concrete)
                return concrete.OpacityMaskClipValue;

            current = (current as UMaterialInstance)?.Parent as UMaterialInterface;
        }

        return root?.OpacityMaskClipValue ?? 0.333f;
    }

    /// <summary>Walks the Parent chain to the concrete UMaterial. Bounded so a cyclic
    /// or self-referencing chain cannot hang an export.</summary>
    private static UMaterial? RootMaterial(UMaterialInterface material)
    {
        var current = material;
        for (var depth = 0; current is not null && depth < 16; depth++)
        {
            if (current is UMaterial concrete) return concrete;
            current = (current as UMaterialInstance)?.Parent as UMaterialInterface;
        }

        return null;
    }

    private static bool TryImage(
        CMaterialParams2 parameters, string[] names, ExportOptions options,
        string from, string? nameSuffix, out ImageBuilder image, bool forcePng = false)
    {
        image = null!;
        if (!parameters.TryGetTexture2d(out var texture, names)) return false;

        // A cube map's panorama has no UV mapping that matches the mesh, so pointing a
        // channel at it would be an invention rather than a conversion.
        if (texture is UTextureCube)
        {
            Log.Debug("Skipping cube map {Name} for glTF material channel", texture.Name);
            return false;
        }

        var extension = forcePng ? "png" : TextureFileNamer.Extension(texture, options);
        var suffix = nameSuffix ?? TextureFileNamer.Suffix(texture, options);
        var uri = EncodeUri(ExporterBase.Resolve(texture, from, extension, suffix));

        image = CreateImage(uri);
        return true;
    }

    /// <summary>RFC 3986 encoding, per path segment, always with '/' separators.</summary>
    public static string EncodeUri(string relativePath) => string.Join('/', relativePath
        .Split('/')
        .Select(segment => segment is "." or ".." or "" ? segment : Uri.EscapeDataString(segment)));

    /// <summary>
    /// A placeholder image whose bytes exist only to give SharpGLTF a per-URI identity.
    /// <para>
    /// The bytes are never written: <c>WriteSettings.ImageWriteCallback</c> returns the
    /// URI and no image data reaches the GLB. But <c>SceneBuilder.ToGltf2()</c>
    /// deduplicates images <b>by content</b>, so a shared placeholder would merge every
    /// texture in the model into one entry with one wrong URI. Encoding the URI's own
    /// bytes into the pixels makes identity exact and deterministic: same URI, same
    /// image; different URI, different image.
    /// </para>
    /// </summary>
    public static ImageBuilder CreateImage(string uri)
    {
        var bytes = Encoding.ASCII.GetBytes(uri);
        using var bitmap = new SKBitmap(Math.Max(bytes.Length, 1), 1, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        for (var i = 0; i < bytes.Length; i++)
        {
            bitmap.SetPixel(i, 0, new SKColor(bytes[i], 0, 0, 255));
        }

        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        var image = ImageBuilder.From(new MemoryImage(data.ToArray()), uri);
        image.AlternateWriteFileName = uri;
        return image;
    }
}
