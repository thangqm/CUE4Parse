using System.Security.Cryptography;
using CUE4Parse_Conversion;
using CUE4Parse_Conversion.Options;
using Newtonsoft.Json;

namespace CUE4Parse.Cli.Output;

/// <summary>
/// A sorted, hashed record of one export run.
/// <para>
/// Purely a CLI concern: the conversion layer reports <see cref="ExportResult"/> and
/// knows nothing about manifests. Ordering is by ordinal <c>objectPath</c> so two runs
/// with the same arguments produce byte-identical files and the output can be diffed —
/// the NDJSON stream cannot, because the session is parallel.
/// </para>
/// </summary>
public static class ExportManifest
{
    public static object Build(
        IReadOnlyList<ExportResult> results, string outputRoot, ExportOptions options, string toolVersion) => new
        {
            version = 1,
            tool = toolVersion,
            options = new
            {
                meshFormat = options.MeshFormat.ToString(),
                textureFormat = options.TextureFormat.ToString(),
                texturePlatform = options.TexturePlatform.ToString(),
                meshQuality = options.MeshQuality.ToString(),
                nanite = options.NaniteMeshFormat.ToString(),
                socketFormat = options.SocketFormat.ToString(),
                materialDepth = options.MaterialDepth.ToString(),
                textureQuality = options.TextureQuality,
                exportMaterials = options.ExportMaterials,
                allMips = options.ExportAllTextureMips,
                flipNormalY = options.FlipNormalY,
                compressionFormat = options.CompressionFormat.ToString(),
                exportMorphTargets = options.ExportMorphTargets,
                exportHdrTexturesAsHdr = options.ExportHdrTexturesAsHdr,
            },
            entries = results
                .OrderBy(result => result.ObjectPath, StringComparer.Ordinal)
                .Select(result => new
                {
                    objectPath = result.ObjectPath,
                    @class = result.ClassName,
                    status = result.Success ? "ok" : "error",
                    message = result.Error?.Message,
                    files = (result.DiskFilePaths ?? [])
                        .Select(path => Describe(path, outputRoot))
                        .OrderBy(file => file.path, StringComparer.Ordinal)
                        .ToArray(),
                })
                .ToArray(),
        };

    public static void Write(object manifest, FileInfo destination)
    {
        destination.Directory?.Create();

        // Indented and newline-terminated so a human can read a diff; the byte-identity
        // guarantee comes from the ordering above, not from the formatting.
        File.WriteAllText(destination.FullName,
            JsonConvert.SerializeObject(manifest, Formatting.Indented) + "\n");
    }

    private static FileRecord Describe(string absolutePath, string outputRoot)
    {
        var relative = Path.GetRelativePath(outputRoot, absolutePath).Replace(Path.DirectorySeparatorChar, '/');

        // Streamed rather than ReadAllBytes: exported 4K textures and Nanite meshes run to
        // hundreds of megabytes, and buffering one whole file per entry only to hash it
        // puts every one of them on the large object heap.
        using var stream = File.OpenRead(absolutePath);
        var hash = SHA256.HashData(stream);
        return new FileRecord(relative, stream.Length, Convert.ToHexString(hash).ToLowerInvariant());
    }

    private sealed record FileRecord(string path, long bytes, string sha256);
}
